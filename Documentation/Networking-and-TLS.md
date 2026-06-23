# Networking and TLS

How traffic reaches Linteum from the internet: a single nginx instance terminates TLS and
reverse‑proxies to the Blazor replicas, with Let's Encrypt certificates managed by Certbot.
This documents the **running** configuration on the VPS.

## 1. Networks

Each Compose project has its own bridge network; the projects are **isolated** from each other
at the Docker level.

| Network | Compose project | Members |
|---|---|---|
| `linteum_prod1` | `linteum` | `linteum-db`, `linteum-db-backup`, `linteum-api`, `linteum-blazor-{1,2,3}`, (opt‑in `linteum-bots`) |
| `ash-twin-monitoring_elk` | `ash-twin-monitoring` | `elasticsearch`, `kibana`, `filebeat` |
| `ash-twin_default` | `ash-twin` | `ash-twin` |
| `emojimental-spring_default` | `emojimental-spring` | `emojimental-spring` |

The monitoring stack reaches Linteum **only** through the host's `/var/lib/docker/containers`
log directory (Filebeat reads container logs by file, not over the network). See
[Observability.md](Observability.md).

## 2. Exposed host ports

```
22     sshd
80,443 nginx (TLS terminator)
5001   ash-twin          (5020 emojimental-spring)
5010-5012  linteum-blazor replicas (→ 8090)
8080   linteum-api       (→ 8080)
5434   linteum-db        (→ 5432)   ⚠️ public
127.0.0.1:9200   elasticsearch      (loopback only)
127.0.0.1:5601   kibana             (loopback only)
```

> ⚠️ `linteum-api` (8080) and `linteum-db` (5434) are published on **all interfaces** and are
> reachable directly, bypassing nginx. The API exposes many unauthenticated endpoints (P‑NET‑03,
> P‑SEC‑01) and the Postgres port is open with `.env` credentials (P‑NET‑04). The monitoring
> services are correctly bound to loopback. Recommendation: bind API/DB to `127.0.0.1` (or
> remove the host publish entirely, since the Blazor replicas reach the API over the Docker
> network).

## 3. nginx reverse proxy

All server blocks live in a single file, `/etc/nginx/sites-available/ash-twin`, symlinked into
`sites-enabled`. nginx 1.26.3.

### Blazor — `linteum.ash-twin.com` (sticky, WebSocket)

```nginx
upstream blazor_cluster {
    ip_hash;                         # sticky by client IP — required for Blazor Server circuits
    server 127.0.0.1:5010;
    server 127.0.0.1:5011;
    server 127.0.0.1:5012;
}

server {
    server_name linteum.ash-twin.com;
    listen 443 ssl;
    listen [::]:443 ssl;
    ssl_certificate     /etc/letsencrypt/live/linteum.ash-twin.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/linteum.ash-twin.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    location / {
        proxy_pass http://blazor_cluster;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;      # WebSocket upgrade for the Blazor circuit
        proxy_set_header Connection "upgrade";

        # WebSocket hardening (P‑NET‑02): keep long‑idle Blazor circuits alive and stream
        # server‑rendered output immediately instead of buffering.
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
        proxy_buffering off;
    }
}
# HTTP → HTTPS redirect for linteum.ash-twin.com (managed by Certbot)
```

`ip_hash` is essential: a Blazor Server circuit is stateful and bound to one process, so a
client must consistently reach the same replica. `proxy_read_timeout`/`proxy_send_timeout` are
raised to 3600 s and `proxy_buffering` is disabled for the Blazor site, so long‑idle circuits
survive and server‑rendered output streams immediately (P‑NET‑02).

### Other proxies in the same file
- **`ash-twin.com` / `www.ash-twin.com`** (80 + 443) → `http://localhost:5001` (the ash‑tin hub).
- **`kibana.ash-twin.com`** (443) → `http://127.0.0.1:5601`, with `proxy_read_timeout 300` for
  long‑running Kibana queries.
- **`emojimental-spring.ash-twin.com`** (443) → `127.0.0.1:5020`, using a **self‑signed/local
  cert** from `/etc/ssl/localcerts/` (not Let's Encrypt).
- HTTP → HTTPS 301 redirects for each HTTPS site.

> Note: the Blazor site now sets explicit WebSocket‑friendly timeouts
> (`proxy_read_timeout`/`proxy_send_timeout` 3600 s, `proxy_buffering off`) — applied above
> (P‑NET‑02, ✅ in the documented config). The other proxies in this file still use defaults.

## 4. TLS — Let's Encrypt / Certbot

Three ECDSA certificates (auto‑managed by the Certbot nginx plugin):

| Certificate name | Domains | Expiry (at inspection) |
|---|---|---|
| `ash-twin.com` | `ash-twin.com`, `www.ash-twin.com` | 2026‑09‑04 (~75 days) |
| `kibana.ash-twin.com` | `kibana.ash-twin.com` | 2026‑09‑05 (~76 days) |
| `linteum.ash-twin.com` | `linteum.ash-twin.com` | 2026‑09‑02 (~73 days) |

Renewal:

- **`certbot.timer`** (systemd) runs `certbot -q renew` on a schedule (next run ~05:20 local,
  last run ~15 min before inspection). The nginx plugin reloads nginx automatically after a
  successful renewal.
- Fallback `/etc/cron.d/certbot` runs twice daily for non‑systemd environments.
- No explicit `--deploy-hook` script is present; reload is handled by the nginx plugin's
  renewal hooks.

SSL parameters come from `/etc/letsencrypt/options-ssl-nginx.conf` + `ssl-dhparams.pem`.

## 5. End‑to‑end request flow

```
Browser ──HTTPS──▶ nginx :443 (linteum.ash-twin.com, ECDSA cert)
                     │  ip_hash sticky routing + WebSocket upgrade
                     ▼
        Blazor replica 1/2/3  (Blazor Server circuit over WebSocket)
                     │
                     │  server‑side HttpClient + SignalR (CanvasHub)
                     ▼
              linteum-api :8080  (internal: http://linteum-api:8080)
                     │
                     ▼  EF Core / Npgsql
              linteum-db :5432 (postgres)
```

The browser holds exactly one WebSocket (the Blazor circuit). All API and realtime traffic is
issued by the Blazor **server** over the `linteum_prod1` Docker network — the browser never
calls the API directly.
