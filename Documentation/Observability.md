# Observability — ELK + Filebeat (`ash-twin-monitoring`)

Linteum's logs are collected by a dedicated **Elasticsearch + Kibana + Filebeat** stack that
lives in a **separate repository**, [`ash-twin-monitoring`](https://github.com/DAnikeyev/ash-twin-monitoring).
It is deployed on the same VPS as Linteum but as an independent Docker Compose project
(`ash-twin-monitoring`) on its own bridge network (`elk`).

> **Two sources of truth, and they differ.** This document describes both:
> (a) what the **committed `ash-twin-monitoring` repo** says, and
> (b) what is **actually running on the VPS**, which has drifted ahead of the repo. Differences
> are called out explicitly.

## 1. What the stack does

Filebeat runs as root, reads every Docker container's JSON log files on the host
(`/var/lib/docker/containers/**/*.log`) via Docker **autodiscovery**, enriches each line with
container metadata (image, container name, Compose project/service), and ships them to
Elasticsearch. Kibana is the query UI.

```
Any container (linteum-api, blazor, db, …)
  │  stdout/stderr → Docker json-file driver
  ▼
/var/lib/docker/containers/<id>/<id>.log
  │  Filebeat (autodiscover, root, on network `elk`)
  ▼
Elasticsearch  (index/datastream `docker-logs-<date>`)
  ▲
Kibana  :5601  (proxied at https://kibana.ash-twin.com)
```

Because Filebeat collects by **log file**, the two stacks need **no shared Docker network**.
Linteum logs are distinguished in Kibana by the `docker.container.labels.com.docker.compose.project`
field (= `linteum`), the `container.name` field, or `container.image.name`.

## 2. Components (committed repo)

`ash-twin-monitoring/docker-compose.yml` defines four services on the `elk` network, all
self‑excluded from collection via the label `co.elastic.logs/enabled: "false"`:

| Service | Image | Notes |
|---|---|---|
| `elasticsearch` | `elasticsearch:8.12.2` | `discovery.type=single-node`, `xpack.security.enabled=true`, heap via `ES_JAVA_OPTS`, bound to `127.0.0.1`. |
| `kibana-user-setup` | `curlimages/curl:8.7.1` | One‑shot: creates the Kibana login user with role **`superuser`** (P‑OBS‑04). |
| `kibana` | `kibana:8.12.2` | Connects to ES over **plain HTTP** with a service‑account token; bound to `127.0.0.1`. |
| `filebeat` | built `ash-twin-monitoring-filebeat:8.12.2` | Runs as **root**, mounts `/var/lib/docker/containers:ro` + `/var/run/docker.sock:ro`. |

`filebeat/filebeat.yml` (baked into the image): Docker autodiscover with hints; `add_docker_metadata`
+ `add_host_metadata` processors; output to Elasticsearch. The committed config writes a flat
daily index `docker-logs-YYYY.MM.DD` with `setup.ilm.enabled: false`, and an ILM policy is
applied separately by `scripts/setup-ilm.sh` (a **1‑day** delete retention by default).

## 3. Live deployment on the VPS (drifted)

Inspection of the running stack shows the deployed monitoring is **ahead of the committed
repo** in several respects:

| Aspect | Committed repo | Live on VPS |
|---|---|---|
| Filebeat output target | flat index `docker-logs-*` | **data stream** `docker-logs` (indices named `.ds-docker-logs-YYYY.MM.DD-*`) |
| ILM retention | 1 day (default in `setup-ilm.sh`) | **~2 months** of indices present (2026‑04‑22 → 2026‑06‑19) |
| ES heap (`ES_JAVA_OPTS`) | `-Xms384m -Xmx384m` (default) | **`-Xms512m -Xmx512m`** |
| ES memory limit | 1 GiB (default `.env`) | **1.5 GiB** (1.40 GiB in use, ~93 %) |
| Filebeat input | `log` (deprecated) | still `log` input (warns in filebeat logs) |

Cluster health (single node): `yellow` — expected, because replica shards cannot be assigned on
a one‑node cluster (99 active primary shards, 61 unassigned replicas). ~2 months of log history
is retained across daily data streams.

> **Operational risk:** the live stack is configured in ways that are **not captured in the
> committed repo**, so the repo is not a faithful source of truth for the deployment.
> Re‑creating this stack from the repo would yield a 1‑day‑retention, 384 MB‑heap, flat‑index
> deployment that does not match production. The `scripts/setup-ilm.sh` script is also
> **gitignored** (`.gitignore` excludes `scripts/`) — it exists only on the operator's machine
> (P‑OBS‑01). Reconstructing the real ILM/retention policy requires reading the live cluster.

## 4. Access

- **Kibana UI:** `https://kibana.ash-twin.com` → nginx → `127.0.0.1:5601` (Basic‑auth‑less in
  the proxy; Kibana itself authenticates the operator with the `kibana-user-setup` login).
- **Data view:** `docker-logs-*`, timestamp `@timestamp`.
- **Filter Linteum logs:** KQL on `docker.container.labels.com.docker.compose.project: "linteum"`,
  or `container.name: linteum-api`, etc.

## 5. How Linteum's logs are produced

- `linteum-api` and the Blazor replicas use **NLog** (console target), so their structured logs
  go to container stdout and are picked up by Filebeat. Console level is tunable via
  `NLOG_CONSOLE_MIN_LEVEL`.
- ⚠️ **No multiline / JSON parsing is configured in Filebeat.** .NET/NLog stack traces that span
  multiple stdout lines become separate, uncorrelated documents, and any JSON‑formatted NLog
  payload is not parsed into queryable fields (P‑OBS‑05). This is the main observability gap for
  the very logs the stack exists to serve.

## 6. Problems (full detail in [Problems.md](Problems.md))

- P‑OBS‑01: `scripts/` is gitignored; `.env.example` is empty → repo is not deployable as‑is.
- P‑OBS‑02: `vm.max_map_count` not set anywhere in the repo (live host presumably has it set
  manually).
- P‑OBS‑03: single‑node ES, `number_of_replicas: 0`, **no snapshots** → volume loss = total log
  loss.
- P‑OBS‑04: the Kibana login user is created as `superuser` (over‑broad).
- P‑OBS‑05: no multiline / JSON decoding for NLog output.
- P‑OBS‑06: Filebeat runs as root with the Docker socket mounted.
- P‑OBS‑07: ES at 93 % of its 1.5 GiB limit — headroom is thin.
