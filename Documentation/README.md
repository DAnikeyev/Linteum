# Linteum — Technical Documentation

This folder is the engineering reference for **Linteum**, a collaborative pixel‑canvas
application. It complements the user‑facing `README.md` and the release history in
`Changelog.md` by describing *how the system is built and operated*.

- **Live site:** <https://linteum.ash-twin.com>
- **Deployed version (at the time of writing):** `0.2.2`
- **Source repo:** `C:\Repos\GitHub\Linteum` (the live stack is deployed from this repo)
- **Status of this documentation:** reflects code at commit `03070e6` on `main` and the
  running production deployment inspected on the VPS.

---

## Document index

| Document | What it covers |
|---|---|
| [01‑Architecture.md](01-Architecture.md) | Layered architecture, solution topology, request lifecycle, realtime model, data model, background processing. |
| [Projects](projects/) | One file per project with its responsibilities, internals, configuration and dependencies. |
| &nbsp;&nbsp;[Linteum.Api](projects/Linteum.Api.md) | ASP.NET Core Web API: controllers, SignalR hub, background services, migrations, DI. |
| &nbsp;&nbsp;[Linteum.Infrastructure](projects/Linteum.Infrastructure.md) | EF Core `AppDbContext`, repositories, write coordination, seeder, economy engine. |
| &nbsp;&nbsp;[Linteum.Domain](projects/Linteum.Domain.md) | Entities and repository contracts. |
| &nbsp;&nbsp;[Linteum.Shared](projects/Linteum.Shared.md) | DTOs, enums, config, security and image/text helpers. |
| &nbsp;&nbsp;[Linteum.BlazorApp](projects/Linteum.BlazorApp.md) | Blazor Server UI, `MyApiClient`, JS canvas renderer and viewport. |
| &nbsp;&nbsp;[Linteum.Bots](projects/Linteum.Bots.md) | Automated canvas clients. |
| &nbsp;&nbsp;[Tests](projects/Tests.md) | `Linteum.Tests` and `Linteum.Tests.Db`. |
| [Deployment.md](Deployment.md) | Production host, Docker Compose topology, the remote‑Docker‑context deploy model, resource budget, backups. |
| [Networking-and-TLS.md](Networking-and-TLS.md) | nginx reverse proxy, sticky load balancing, Let's Encrypt / Certbot, networks and exposed ports. |
| [Observability.md](Observability.md) | The Elasticsearch + Kibana + Filebeat stack (`ash-twin-monitoring`), index lifecycle, log routing. |
| [Capabilities.md](Capabilities.md) | Functional and non‑functional capabilities of the system. |
| [Problems.md](Problems.md) | Known problems across code, security, data integrity, deployment and observability, each with a suggested solution and severity. |

---

## System at a glance

Linteum is a multi‑user pixel canvas where many people can draw on shared boards in real
time. It is built on **.NET 10** and split into focused projects:

```
Linteum.BlazorApp  ── (server-side HttpClient + SignalR) ──▶  Linteum.Api
   (Blazor Server UI)                                          (Web API + SignalR hub)
                                                                     │
                                                                     ▼
                                                       Linteum.Infrastructure  ──▶  PostgreSQL 16
                                                          (EF Core repositories)        (linteum-db)
                                                                     ▲
                                                       Linteum.Domain  (entities + contracts)
                                                                     ▲
                                                       Linteum.Shared  (DTOs, enums, helpers)

Linteum.Bots ── (HTTP, acts as a normal client) ──▶ Linteum.Api
```

The whole stack runs in **Docker** on a single Debian 13 VPS, fronted by **nginx** (TLS via
Let's Encrypt) and observed by an **Elasticsearch + Kibana + Filebeat** stack that lives in
a separate repo (`ash-twin-monitoring`).

### Three canvas modes

| Mode | Behavior |
|---|---|
| `Normal` | Standard shared canvas. Each user gets a configurable daily pixel quota per canvas (100/day for accounts, 10/day for guests). |
| `FreeDraw` | Fast editing. Batch paint, batch delete, and a queued text‑drawing tool. No quota. |
| `Economy` | Pixels have prices and ownership. Buying a pixel raises its price and debits the buyer's balance; subscribed users earn hourly income proportional to how many pixels they own on the canvas. |

### Quick facts

- **Runtime:** .NET 10, ASP.NET Core, Blazor Server (Interactive Server), SignalR, EF Core 10, Npgsql.
- **Database:** PostgreSQL 16 (single instance, tuned, daily + weekly `pg_dump` backups).
- **Auth:** Custom `Session-Id` header sessions (in‑memory), local accounts, Google OAuth, and a guest mode.
- **Realtime:** A SignalR `CanvasHub` broadcasts pixel changes, chat, online users, economy income, and lifecycle events.
- **Deployment:** Docker Compose, deployed from the dev machine through a remote Docker context. The Blazor UI runs as 3 replicas behind nginx with IP‑hash sticky routing.
- **Host:** Debian 13, 4 vCPU, 8 GB RAM, 251 GB disk.

> The README in the repo root is aimed at users/contributors and is partially out of date
> (it understates the bots and omits several features). This folder is the authoritative
> technical reference.
