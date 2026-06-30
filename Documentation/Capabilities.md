# Capabilities

This document enumerates what Linteum **does** (functional capabilities) and **how well** it
does it (non‑functional qualities). It is derived from the code and the running deployment.

## Functional capabilities

### Accounts & authentication
- **Local accounts** with email + password (signup enforces username ≥ 4 chars, unique email and
  username; password 4–32 chars).
- **Google OAuth** login (GIS code client; server‑side token exchange + `id_token` validation).
- **Guest mode** — a one‑click ephemeral account (`guest########` @ `guestmail.com`) that can be
  auto‑triggered via `?guest=1`. Guests are subject to lower quotas and are auto‑cleaned after
  24 h.
- **Custom sessions** via the `Session-Id` header (60‑minute sliding expiry, one session per user).

### Canvases
- Multiple canvases: **public, private, and password‑protected**.
- **Three modes** with distinct rules:
  - `Normal` — daily pixel quota per user per canvas (100 for accounts, 10 for guests).
  - `FreeDraw` — batch paint, batch delete, and a queued text‑drawing tool (no quota).
  - `Economy` — pixels have prices and ownership; buying raises the price and debits balance;
    subscribed users earn hourly income `floor(10·(1 + log2(1 + ownedPixels)))`.
- **Create** a canvas blank (10–1920 × 10–1080) or from a **JPG** starting image (≤ 20 MB; the
  canvas is sized to the image and seeded asynchronously by `CanvasSeedQueueService`).
- **Subscribe / unsubscribe**, with canvas‑password verification. Auto‑subscribe to default
  public canvases. The built‑in `home` canvas cannot be unsubscribed.
- **Erase / delete** canvases (creator‑controlled, with a password‑protected‑canvas guard),
  executed gradually via a queue with realtime progress events.
- **Search** canvases by name (`ILike`).
- **Export** a canvas as an image (PNG via the API, JPEG download via the UI).
- Default‑canvas **seeding & self‑healing**: on startup the seeder creates/syncs default
  canvases (`home`, `home_FreeDraw`, `home_Economy`, …), resizes them, cleans out‑of‑bounds
  pixels, and reconciles the color palette.

### Drawing & interaction
- Live pixel placement with **fluid brush strokes** (Bresenham interpolation, color‑grouped
  batching, 75 ms paint / 200 ms erase flush loops).
- **GPU‑composited pan & zoom** (CSS `translate3d`, no redraw on pan/zoom), pinch‑zoom, click
  vs. drag detection, pixel‑perfect (`image-rendering: pixelated`).
- **Eraser** with selectable size (1/3/7).
- **Batch operations**: batch paint, batch delete, deduplication, budget/quota stops with
  detailed per‑request results.
- **Text tool** (FreeDraw): rasterize text to pixels with configurable font size (4–25),
  foreground color, and optional background color, with a live caret preview.
- **Pixel inspection**: click a pixel to see its owner, color, price, and up to 10 history
  entries (refreshed live via SignalR).
- **Master‑password override** on batch endpoints (used by bots/admin) to bypass Economy budgets
  and Normal quotas.

### Realtime
- SignalR `CanvasHub` broadcasts: pixel updates/batches, confirmed stroke playback, deletions,
  online users, lobby chat, economy income, erase/delete progress, and session expiry.
- **Online‑user tracking** per canvas.

### Economy
- Per‑(user, canvas) balance as an **append‑only ledger** (`BalanceChangedEvent`); current
  balance derived from the newest entry.
- **Hourly income** for Economy subscribers based on pixels owned.
- Subscriptions credit an initial balance; unsubscribing zeroes it.

### Chat
- **Lobby chat** per canvas (ephemeral, 4000‑char limit, Enter‑to‑send, minimized state).

### Automation (bots)
- `CleanerBot` (clear to white), `MunchBot`/`VanGoghBot`/`VanGogh2Bot` (noise painters),
  `XeroxBot` (reproduce an image onto a canvas). See [Linteum.Bots](projects/Linteum.Bots.md).

### Administration / operational
- Daily + weekly **PostgreSQL backups** (`pg_dump -Fc`).
- Background **cleanup**: minute‑level session expiry, daily guest + inactive‑canvas cleanup,
  pixel‑history pruning (≤ 10/pixel).
- **Structured logging** (NLog) aggregated into Elasticsearch/Kibana.
- Centralized **log search** across all containers via Kibana.

## Non‑functional capabilities

### Performance
- **Interactive smoothness** is achieved client‑side: pan/zoom/hover never round‑trip to .NET
  (CSS transforms + a 2D‑canvas renderer with rAF batching and per‑frame dedup ≤ 4096).
- Pixel writes are **batched** (≤ 500) and serialized per canvas; EF Core uses **pooled
  DbContexts** (pool 64) and `AutoDetectChangesEnabled=false` on the seed path.
- Short‑lived **client caches** (pixel/history, 1‑min TTL) and a server‑side canvas lookup cache.
- **Limits:** `GET Pixels/canvases/{id}` and `GET Canvases/image/{name}` materialize whole
  canvases into memory (explicit OOM warnings). `CanvasWriteCoordinator` serializes per canvas
  but is **single‑process**.

### Scalability
- **Horizontal UI scaling**: 3 Blazor replicas behind nginx with **cookie-based** sticky routing (the `linteum_route` cookie; `ip_hash` was replaced because Cloudflare rotates its edge IP per connection, defeating IP hashing).
- **Bottlenecks**: no SignalR backplane (online state per replica); in‑process write
  coordination (safe only with a single API instance); single Postgres + single ES node.

### Reliability & availability
- `restart: unless-stopped` on all services; DB healthcheck gates API startup.
- **Backups**: daily + weekly, crash‑safe (temp + atomic rename).
- **Weaknesses**: in‑memory sessions (lost on API restart → everyone logged out); ES has no
  replicas/snapshots; backup loop has no off‑host copy or failure alerting.

### Security
- TLS terminated at nginx (Let's Encrypt ECDSA); per‑canvas passwords; session‑based access
  control on mutating endpoints; Google OAuth.
- **Significant weaknesses** (detailed in [Problems.md](Problems.md)): SHA‑256 password hashing
  (no KDF); password hashes passed in query strings; many unauthenticated endpoints; the API
  and DB ports are publicly exposed; secrets in plain env vars; bots run with TLS validation
  disabled and hold the master password.

### Maintainability & testability
- Clean layered architecture with a shared contract assembly; AutoMapper; repository pattern.
- NUnit tests for canvas rendering and text rasterization; a substantial **database integration
  test suite** (Create/Read/Update/Delete across all repositories, the seeder, the income
  engine, and the seed queue).
- **Gaps**: no Testcontainers (DB tests need a pre‑provisioned Postgres); no controller/API
  tests; DB dropped/recreated per test.

### Operability
- **Deploy** from the dev machine via a remote Docker context (`docker compose up -d --build`).
- Centralized logs in Kibana; per‑canvas pixels/sec metrics logged every second.
- **Drift risk**: the monitoring stack and several config values have drifted from the committed
  repo (see [Observability.md](Observability.md)).

### Portability
- .NET 10 / Linux containers; ImageSharp (cross‑platform) for all image/text work; no
  Windows‑only dependencies in the runtime path (one stray Windows path in `nlog.config`).
