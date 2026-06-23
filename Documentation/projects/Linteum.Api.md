# Linteum.Api

ASP.NET Core Web API. Exposes REST controllers and the SignalR `CanvasHub`, hosts all
background services, owns EF Core migrations, and is the DI composition root for the backend.
Target framework `net10.0`; references `Linteum.Domain`, `Linteum.Infrastructure`,
`Linteum.Shared`.

## Startup (`Program.cs`)

Order of operations:

1. `ThreadPool.SetMinThreads(25, 25)` — preallocate worker/IOCP threads.
2. `DotNetEnv.Env.Load("../.env")` — load `.env` from the repo root (parent of the project).
3. Bootstrap NLog; console minimum level from `NLOG_CONSOLE_MIN_LEVEL` (default `Info`).
4. DI registrations (see below).
5. **Pre‑host migration + seeding**: `new DbMigrator(...)` → `InitializeAsync()` runs
   synchronously before the app serves traffic.
6. Pipeline (Dev only: `MapOpenApi`, `UseHttpsRedirection`) → `UseCors("AllowBlazorApp")` →
   `MapControllers()` → `MapHub<CanvasHub>("/canvashub")` → `Run()`.
7. Global try/catch logs and rethrows; `finally` shuts NLog down.

**Absent from the pipeline:** there is no `UseAuthentication`/`UseAuthorization`, no global
exception handler, and no Swagger UI in production (only the OpenAPI document in Dev).
Authentication is handled manually via the `Session-Id` header. The Kestrel port is driven by
the `API_CONTAINER_PORT` Dockerfile arg (default **8080**).

## Dependency injection (`Services/ServiceCollectionExtenstions.cs`)

`AddApplicationServices` registers:

- `AutoMapper` (profile from Infrastructure), `MemoryCache`.
- `ICanvasWriteCoordinator` → `CanvasWriteCoordinator` (singleton) — per‑canvas write
  serialization.
- `Channel.CreateUnbounded<PixelDto>()` — the shared changed‑pixel channel.
- The four queue/counter services as `singleton + interface + IHostedService`:
  `PixelChangeCounterService`, `CanvasSeedQueueService`, `CanvasMaintenanceQueueService`,
  `TextDrawQueueService`.
- `HourlyCanvasIncomeProcessor` (scoped), `ICanvasIncomeNotifier` → `SignalRCanvasIncomeNotifier`.
- **DbContext pooling:** `AddDbContextPool<AppDbContext>(… UseNpgsql …, poolSize: 64)`,
  `MigrationsAssembly("Linteum.Api")`.
- `Config` (default‑constructed singleton), `SessionService`, `RepositoryManager` (scoped).
- Hosted cleanup services: `DbCleanupService`, `DailyCleanupService`, `MinuteCleanupService`,
  `HourlyCanvasIncomeService`.
- **Startup guard:** if `MASTER_PASSWORD` env is missing/empty, logs `Fatal` and throws — the
  app refuses to start.

`GetRequiredConnectionString` uses `DEFAULT_DB_HOST_CONNECTION` on Windows and
`ConnectionStrings:DefaultConnection` elsewhere. CORS policy `"AllowBlazorApp"` is built from
the `CorsOrigins` config array with `AllowCredentials()`.

## Controllers (`Controllers/`)

All controllers use `[ApiController]` + `[Route("[controller]")]`. Authentication is via the
custom `Session-Id` header resolved through `SessionService`. **Many read endpoints have no
auth at all** — see [Problems.md](../Problems.md) (P‑SEC‑01).

### UsersController — `/Users`
| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `email/{email}` `username/{userName}` `id/{id}` | none | Lookups. |
| POST | `add-or-update` `?passwordHashOrKey=&loginMethod=` | none | Create/update. |
| DELETE | (body `UserDto`) | none | Delete user. |
| POST | `login` `?passwordHashOrKey=` | none | Returns `LoginResponse { User, SessionId }`. |
| POST | `login-guest` | none | Mints a guest user + session. |
| POST | `login-google-code` | — | Exchanges an OAuth code (`redirect_uri=postmessage`) and validates the `id_token`. |
| POST | `login-google` | — | Validates a Google `id_token` directly. |
| POST | `validate` (body `Guid sessionId`) | — | Returns `LoginResponse` if the session is valid. |
| POST | `add` `?passwordHashOrKey=` | none | Signup (username ≥ 4 chars, unique email/username). |
| POST | `changeName` | Session‑Id | Rename. |
| POST | `changePassword` `?passwordHashOrKey=&loginMethod=` | Session‑Id | Password accounts only. |

### CanvasesController — `/Canvases`
| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `` `?includePrivate=true` `` | none | List canvases. |
| GET | `user/{userId}` | none | Canvases by creator. |
| GET | `name/{name}` | Session‑Id | Get one; also auto‑subscribes the caller to public canvases. |
| GET | `search?name=&includePrivate=` | none | Search by name (`EF.Functions.ILike`). |
| POST | `Add` `?passwordHash=` | Session‑Id | Create blank canvas (blocks guests), subscribe creator. |
| POST | `add-with-image` (form) | Session‑Id | JPG ≤ 20 MB; sizes canvas to the image; enqueues seed job. |
| POST | `subscribe` | Session‑Id | Subscribe (maps exceptions → 404/401/400). |
| POST | `unsubscribe` | Session‑Id | Blocks built‑in canvas `home`. |
| POST | `erase/{name}` | Session‑Id | Creator or FreeDraw; queued erase. |
| DELETE | `delete/{name}` | Session‑Id | Creator‑only; queued delete. |
| POST | `check-password` `?passwordHash=` | none | Check a canvas password. |
| GET | `image/{name}` | Session‑Id | Render full PNG via ImageSharp (loads all pixels — OOM risk). |
| GET | `subscribed` | Session‑Id | Session user's canvases. |

`MaxCanvasImageUploadBytes = 20 MiB`. Size validation uses `IOptions<CanvasSizeOptions>`
(10–1920 × 10–1080).

### PixelsController — `/Pixels`
The most complex controller. Injects the shared `Channel<PixelDto>`, `IPixelChangeCounter`,
`ITextDrawQueue`, `IPixelNotifier`, `Config`.
| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `canvases/{canvasId}` | none | All pixels (explicit **OOM warning**; logs if > 100 000). |
| GET | `getpixel/{canvasName}` | Session‑Id | Single pixel; synthesizes a white default if absent. |
| GET | `owner/{ownerId}` | none | Pixels by owner. |
| GET | `quota/{canvasName}` | Session‑Id | Normal‑mode daily quota. |
| POST | `change/{canvasName}` | Session‑Id | Single pixel; blocks guests on Economy. |
| POST | `change-batch/{canvasName}` | Session‑Id | Batch with optional `MasterPassword`. |
| POST | `change-batch-coordinates/{canvasName}` | Session‑Id | Coordinate expansion + stroke playback metadata. |
| POST | `delete-batch/{canvasName}` | Session‑Id | Batch delete (FreeDraw) with master override. |
| POST | `text/{canvasName}` | Session‑Id | FreeDraw text tool (enqueued). |

`IsValidMasterPassword` compares the request password to `MASTER_PASSWORD` with a plain
**ordinal, non‑constant‑time** `string.Equals` — a master‑password bypass exists on every
batch endpoint (P‑SEC‑03).

### Other controllers
- **ColorsController** (`/Colors`): `GET` all colors (no auth).
- **SubscriptionsController** (`/Subscriptions`): `GET user/{userId}`, `GET canvas/{canvasId}` (no auth).
- **LoginEventsController** (`/LoginEvents`): `GET user/{userId}`, `POST` insert (no auth).
- **PixelChangedEventsController** (`/PixelChangedEvents`): `GET user/`, `GET pixel/{pixelId}`,
  `GET canvas/{canvasId}?startDate=&limit=` (clamped to ≤ 10 000), `POST` (no auth).
- **BalanceChangedEventsController** (`/BalanceChangedEvents`): `GET user/`, `GET user/canvas/`,
  `GET current/canvas/{canvasId}` (Session‑Id, computes current balance), `POST change`
  (`userId`, `canvasId`, `delta`, `reason` from query — **no auth**: anyone can change balances,
  P‑SEC‑01).
- **CanvasChatController** (`/CanvasChat`): `POST {canvasName}` (Session‑Id). Chat is
  broadcast‑only, never persisted.

## SignalR hub — `Hubs/CanvasHub.cs`

Mapped at `/canvashub`. Client‑invokable methods: `JoinCanvasGroup`, `LeaveCanvasGroup`,
`SendCanvasChatMessage` (requires the connection to have joined that canvas's group). Server
events are declared as `const` strings in the hub (`ReceivePixelUpdate`, `ReceivePixelBatchUpdate`,
`ReceiveConfirmedPixelPlaybackBatch`, `ReceiveConfirmedPixelDeletionPlaybackBatch`,
`UpdateOnlineUsers`, `ReceiveCanvasChatMessage`, `SessionExpired`, `CanvasErased`,
`CanvasDeleted`, `CanvasMaintenanceProgress`, `PixelsDeleted`). Connection lifecycle
(`OnConnectedAsync`/`OnDisconnectedAsync`) maintains `ConnectionTracker`; the session is read
from the `access_token` query string, falling back to the `Session-Id` header.

## Services (`Services/`)

| File | Role |
|---|---|
| `DbMigrator.cs` | Collation‑version check + `MigrateAsync` (6 attempts / 10 s, env‑tunable) + seed. |
| `SessionService.cs` | In‑memory sessions (`ConcurrentDictionary`), sliding 60‑min expiry, one session per user. |
| `ConnectionTracker.cs` | In‑memory connection↔user↔group maps; `GetGroupUsers` returns distinct usernames. |
| `SignalRPixelNotifier.cs` | `IPixelNotifier` impl; pushes pixel events to `Clients.Group(canvasName)`. |
| `SignalRCanvasIncomeNotifier.cs` | Pushes `ReceiveCanvasIncomeUpdates` (magic string, not a const). |
| `CanvasChatBroadcaster.cs` | Pushes `ReceiveCanvasChatMessage`; no persistence. |
| `DbCleanupService.cs` | Prunes `PixelChangedEvent` to ≤ 10/pixel. |
| `DailyCleanupService.cs` | 24 h: expire guests (> 24 h), delete canvases inactive > 30 days (2 s spacing). |
| `MinuteCleanupService.cs` | 60 s: expire sessions, push `SessionExpired`, rebroadcast online users. |
| `HourlyEconomyIncomeService.cs` | Top of each hour: run `HourlyCanvasIncomeProcessor`, notify. |
| `PixelChangeCounterService.cs` | 1 s: log pixels/sec per canvas. |
| `CanvasSeedQueueService.cs` | Channel‑driven: rasterize JPG → seed pixels (color‑grouped batches of 500). |
| `CanvasMaintenanceQueueService.cs` | Channel‑driven: queued erase/delete with progress events; per‑canvas dedupe. |
| `TextDrawQueueService.cs` | Channel‑driven: FreeDraw text → pixels (batches of 100, 10 ms pace). |
| `ICanvasIncomeNotifier.cs` | Interface for the income notifier. |

## Migrations (`Migrations/`)

- **`20260403111656_InitialCreate`** — full base schema (8 tables, all indexes/FKs).
- **`20260426120000_SplitSandboxIntoNormalAndFreeDraw`** — data‑only: `UPDATE Canvases SET
  CanvasMode=1 WHERE CanvasMode=0` (legacy `Sandbox` → `Normal`). The `Down` is asymmetric.
- **`20260428184708_AddPixelsCanvasIdIndex`** — adds non‑unique `IX_Pixels_CanvasId`.
- **`AppDbContextModelSnapshot.cs`** — EF product version 10.0.5; reflects the final schema.

## Configuration

- `appsettings.json`: `CanvasSize` (10–1920 × 10–1080), `CorsOrigins` array, logging levels.
- `Configuration/CanvasSizeOptions.cs`: POCO bound to the `CanvasSize` section.
- `nlog.config`: console target; **internal log path is hardcoded `c:\temp\…`** (a Windows path
  that will not exist in the Linux container — P‑OPS‑02).
- `Models/CanvasImageUploadForm.cs`: multipart form model (`Name`, `CanvasMode`,
  `PasswordHash`, `Image`).

## Environment variables (read in code / injected by Compose)

`MASTER_PASSWORD` (required), `MASTER_USER`, `MASTER_EMAIL`, `PASSWORD_SALT`,
`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `NLOG_CONSOLE_MIN_LEVEL`,
`ConnectionStrings__DefaultConnection` / `MaintenanceConnection`,
`DEFAULT_DB_HOST_CONNECTION` (Windows), `DB_MIGRATION_MAX_ATTEMPTS`,
`DB_MIGRATION_RETRY_DELAY_SECONDS`, `CorsOrigins__0/1`, `ASPNETCORE_HTTP_PORTS`.

## Dockerfile

Multi‑stage .NET 10: `aspnet:10.0` base installs `fontconfig`, `fonts-dejavu-core`, and Kerberos
libs (for ImageSharp font/text rendering); `sdk:10.0` build + publish
(`/p:UseAppHost=false`); `ENTRYPOINT ["dotnet","Linteum.Api.dll"]`; `EXPOSE` from
`API_CONTAINER_PORT` (8080). No `HEALTHCHECK`, runs as root.

## Notable issues (full detail in [Problems.md](../Problems.md))

Many unauthenticated endpoints (P‑SEC‑01); password hashes passed in query strings (P‑SEC‑02);
non‑constant‑time master‑password compare (P‑SEC‑03); in‑memory sessions lost on restart
(P‑SEC‑05); `Config` tunables not bound from environment (P‑CFG‑01); `GET Pixels/canvases/{id}`
and `GET Canvases/image/{name}` can OOM (P‑PERF‑01); stale `.http` template artifact; two
methods named `GetImage`.
