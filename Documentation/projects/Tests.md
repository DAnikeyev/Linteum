# Tests — `Linteum.Tests` and `Linteum.Tests.Db`

Two NUnit test projects.

## `Linteum.Tests`

Small unit‑test project for Blazor‑side helpers. Packages: `NUnit` 4.5.1, `NUnit3TestAdapter`
6.2.0, `Microsoft.NET.Test.Sdk` 18.3.0, `coverlet.collector` 8.0.1. References
`Linteum.BlazorApp` + `Linteum.Shared`.

- **`CanvasRendererTests.cs`** — tests the `CanvasRenderer` render loop with a hand‑rolled
  `RecordingJsRuntime`: (1) the loop is event‑driven (two pixels render within 25 ms, not on a
  fixed timer); (2) a batch collapses duplicate coordinates, keeping the latest color.
- **`TextConverterTests.cs`** — five tests over `TextConverter.FromImage` /
  `GetPreviewMetrics`: font‑size clamping (4–25), newlines/edge margins, transparent cells when
  no background color, margin/line‑height math.
- **`SecurityHelperTests.cs`** — PBKDF2 hashing round‑trip, wrong‑password rejection, fresh
  per‑credential salt, legacy SHA‑256/plaintext migration, empty‑input handling, and byte‑for‑byte
  legacy compatibility.

> Remaining coverage gap: no tests for `ImageConverter` (used by every bot and the seed path).
> Referencing the whole `Linteum.BlazorApp` for a handful of small tests is a heavy dependency
> graph. (The old assertion‑less `Hashing.cs` scratch test was removed — P‑TEST‑01.)

## `Linteum.Tests.Db`

Database + API integration tests against an **ephemeral Testcontainers PostgreSQL** (P‑TEST‑03).
Packages: `NUnit` 4.5.1, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1,
`Testcontainers.PostgreSQL` 4.12.x, `Microsoft.AspNetCore.Mvc.Testing` 10.x, `NLog` 6.1.x,
`Microsoft.NET.Test.Sdk`. References `Linteum.Api` + `Linteum.Infrastructure` (so it can
instantiate API‑hosted services like `CanvasSeedQueueService` and boot the API in‑process).
Running requires the Docker daemon — the container and its databases are created and torn down
per test run.

### Plumbing
- **`DbTestSetup.cs`** (`[SetUpFixture]`) — starts one Postgres container per run and provisions
  two databases: `linteum_repo_tests` (EF‑model schema via `EnsureCreated`) for the repository
  tests, and `linteum_api_tests` (real EF migrations, run by `DbMigrator` inside the API host) for
  the API tests. Exposes `RepoConnectionString` / `ApiConnectionString`.
- **`SyntheticDataTest.cs`** (abstract base) — `[SetUp]` builds `AppDbContext` against
  `linteum_repo_tests`, creates the schema once, then per test `TRUNCATE … RESTART IDENTITY
  CASCADE` of every table + `DbSeeder.SeedDefaults` + virtual `SeedData()`; `[TearDown]` only
  disposes the context (P‑TEST‑04 — no more per‑test DB drop/recreate).
- **`DbHelper.cs`** — constructs a `RepositoryManager`; `AddDefaultUser(name)`.
- **`TestMapper.cs`** — singleton AutoMapper from `MappingProfile`.
- **`PermanentDbTest.cs`** — two `[Explicit]` manual create/delete‑database utilities (renamed
  from the `PermamentDbTest` typo, P‑MAIN‑01).

### API tests (`Api/`)
- **`LinteumWebAppFactory.cs`** — `WebApplicationFactory<Program>` boots the real API (TestServer),
  pointing `DbMigrator` at `linteum_api_tests` and setting the startup env vars
  (`MASTER_PASSWORD`, `DEFAULT_DB_HOST_CONNECTION`, …). `ApiTestBase` builds the host once per
  fixture.
- **`SessionAuthMiddlewareTests.cs`** — the session‑auth contract: a protected endpoint returns
  401 without a `Session-Id`, a `[PublicEndpoint]` is reachable, and a `[DisabledEndpoint]`
  returns 404 (P‑TEST‑02).

### Coverage by folder
- **`Create/`** — balance change (+/− and refusal), canvas add ± password, two users painting
  the same pixel (history transitions), economy pixel purchase, subscription with wrong/correct
  password, guest auto‑subscription to default canvases.
- **`Read/`** — `DbSeederTests` (seeding, color reassignment on config removal, default‑canvas
  resize + out‑of‑bounds cleanup, idempotent subscription, inactive‑canvas candidate selection),
  canvas/color/subscription/user/balance/event lookups, `CleanPixelHistoryBatchAsync` keeps
  newest 10.
- **`Update/`** — `PixelRepositoryUpdateTest` (543 lines): economy ownership transfer, balance
  depletion, bounds checks, Normal‑mode daily quota per canvas, batch dedup, budget/limit stops,
  master override. `HourlyCanvasIncomeProcessorTest`: income formula + guest exclusion.
  `CanvasSeedQueueServiceTests`: seeding ownership/pricing, batch sizes, color grouping,
  concurrent‑paint‑not‑overwritten.
- **`Delete/`** — erase (keeps canvas row), delete (cascades), gradual delete in batches,
  protected‑canvas refusal; unsubscribe wrong‑password/unknown cases; user delete.

## Notable issues (full detail in [Problems.md](../Problems.md))

All previously tracked test issues are resolved: DB tests run on Testcontainers (P‑TEST‑03, ✅),
per‑test isolation uses `TRUNCATE` instead of dropping/recreating the DB (P‑TEST‑04, ✅), the
assertion‑less `Hashing.cs` scratch test was removed (P‑TEST‑01, ✅), API/controller integration
tests were added (P‑TEST‑02, ✅), and namespaces + dead files were normalized
(`SyntheticDataTestPopulated.cs` and `DatabaseTests.cs` removed; `TestMapper`,
`BalanceChangedEventRepositoryReadTest`, and `SubscriptionRepositoryReadTest` namespaces fixed)
— P‑TEST‑05, ✅.
