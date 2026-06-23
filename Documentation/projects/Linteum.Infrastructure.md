# Linteum.Infrastructure

Owns all data access: the EF Core `AppDbContext`, repository implementations, the per‑canvas
write coordinator, the database seeder, and the Economy income engine. Target framework
`net10.0`; references `Linteum.Domain` + `Linteum.Shared`.

Key packages: `Microsoft.EntityFrameworkCore` 10.0.5 (+ `Relational`, `Design`),
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1, `AutoMapper` 14.0.0, `NLog` 6.1.1.

> **Migrations live in `Linteum.Api`, not here.** `MigrationsAssembly` is set to `"Linteum.Api"`.
> This project's `.csproj` still declares an empty `Migrations\` folder (a leftover). See
> [Problems.md](../Problems.md) (P‑DATA‑01).

## `AppDbContext` (`AppDbContext.cs`)

**DbSets:** `Users, Pixels, PixelChangedEvents, BalanceChangedEvents, LoginEvents, Colors,
Canvases, Subscriptions`. (`UserSession` is **not** a DbSet — sessions are in‑memory.)

Notable `OnModelCreating` configuration:

- `Pixel`: unique composite index `(CanvasId, X, Y)`; non‑unique indexes on `CanvasId` and `OwnerId`.
- `PixelChangedEvent`: indexes on `PixelId`, `OwnerUserId`, `ChangedAt`.
- `BalanceChangedEvent`: indexes on `CanvasId` and composite `(UserId, CanvasId)`.
- `Canvas`: unique index on `Name`; `CanvasMode` DB default `Normal`; `Creator` FK is
  `OnDelete(Restrict)` (a user cannot be deleted while they own canvases).
- `Subscription`: composite PK `{UserId, CanvasId}`; both FKs `Cascade`.
- Enums are stored as `int` (no `HasConversion`).
- **No unique index on `Color.HexValue`** and **no FK from `Pixel.ColorId` → Color** — both are
  integrity gaps (P‑DATA‑03, P‑DATA‑04).

DbContext is supplied via DI (pooled in the API host, pool size 64). No `EnsureCreated`/`Migrate`
calls here — migration runs in `DbMigrator` at startup.

## Repositories

### `PixelRepository` — the heart of the write path
`MaxBatchSize = 500`. Static mutable `DefaultColorId` cache.

`TryChangePixelsBatchAsync(canvasId, pixels, useMasterOverride, suppressNotifications)`:

1. All writes run inside `CanvasWriteCoordinator.ExecuteAsync(canvas.Id, …)` → **per‑canvas
   serialized**.
2. Clone + dedup inputs by `(X, Y)` (last wins); reject batches spanning more than one canvas;
   split into 500‑pixel chunks.
3. Mode dispatch:
   - **Normal** — compute remaining daily quota (100 for accounts, 10 for guests, per canvas)
     by counting today's `PixelChangedEvent`s for that user/canvas; truncate the chunk;
     set `StoppedByNormalModeLimit` if exceeded.
   - **FreeDraw / Normal** — `ExecuteFreeDrawChunkAsync`: load existing pixels in the bounding
     box, insert/update with `Price = 0`, append a `PixelChangedEvent` each, `SaveChanges`,
     `TouchCanvasAsync` (`ExecuteUpdate` on `UpdatedAt`).
   - **Economy** — `ExecuteEconomyChunkAsync` inside a DB transaction: read current balance
     (latest `BalanceChangedEvent.NewBalance`), required price = `previousPrice + 1`, reject if
     `paid < requiredPrice` (`EconomyBidTooLow`) or `currentBalance < paid`
     (`EconomyInsufficientBalance`), debit and append `PixelPayment` balance events, commit.
     `useMasterOverride` forces price 1 and ignores balance.
4. On `DbUpdateException` wrapping a Postgres `UniqueViolation` (the `(CanvasId,X,Y)` race),
   the chunk returns empty (no retry) (P‑CON‑02).
5. Notifications are chunked through `IPixelNotifier`; exceptions are swallowed and logged.

Other methods: `TryDeletePixelsBatchAsync` (FreeDraw/master only; deletes events then pixels in
500‑batches, each its own transaction), `GetNormalModeQuotaAsync`, `GetByCanvasIdAsync`,
`GetByOwnerIdAsync`, `TryChangePixelAsync` (routes a single pixel into the batch path).

> **Dead code:** the `TryChangePixelInternalAsync` / `*Normal*` / `*Economy*` / `*FreeDraw*`
> single‑pixel helpers are not reached from the live batch path and re‑enter
> `TryChangePixelsBatchAsync`. P‑MAIN‑02.

### `CanvasRepository`
- 60 s `IMemoryCache` on `GetByNameAsync` (invalidated on erase/delete).
- `GetAllAsync`/`SearchByNameAsync` filter `PasswordHash == null` unless `includePrivate`;
  search uses `EF.Functions.ILike` with manual escaping of `\ % _`.
- `TryEraseCanvasAsync` — inside the write coordinator; refuses protected canvases; bumps
  command timeout to **10 min**; `ExecuteDelete` pixels + `ExecuteUpdate` `UpdatedAt`; relies on
  DB cascade for history.
- `TryDeleteCanvasAsync` — single `ExecuteDelete` on the row (cascades pixels/subs/balances).
- `TryDeleteCanvasGraduallyAsync` — batched deletes (500/batch, 150 ms delay), cancellation‑aware.
- `CheckPassword` — plain `==` on the stored hash (P‑SEC‑04).
- `TryAddCanvas` — `Any(name==)` then `Add`; does **not** seed pixels (canvas starts empty).

### `UserRepository`
- Lookups by Email/UserName/Id via `ProjectTo<UserDto>`; `GetByIdAsync(IList<Guid>)` preserves
  input order in a single query.
- `AddOrUpdateUserAsync` — transaction; on create, subscribes the user to default canvases
  **after** commit (a failure there leaves a committed user with partial subscriptions — P‑CON‑01).
- `CreateGuestUserAsync` — up to 32 attempts to generate a unique `guest########` name.
- `DeleteExpiredGuestUsersAsync` — deletes guest canvases, related events, owned pixels, users.
- `TryLogin` — matches `Email && PasswordHashOrKey && LoginMethod` with a plain `==` (no
  constant‑time compare; hashing is the caller's job — P‑SEC‑04).

### `BalanceChangedEventRepository`
`TryChangeBalanceAsync(canvasId, userId, delta, reason)` runs inside the write coordinator **and**
a transaction: reads the latest event for `(user, canvas)` ordered by
`(ChangedAt DESC, Id DESC)`, computes `newBalance = last?.NewBalance + delta`, rejects negatives.
This is the append‑only ledger used for payments and hourly income.

### `SubscriptionRepository`
- `Subscribe` — transaction; verifies the canvas password with a plain `==`; checks not already
  subscribed; inserts; commits; **then** credits `+1` balance (the credit runs after commit —
  P‑CON‑01).
- `Unsubscribe` — **no transaction**; zeroes the balance (`-last.NewBalance`) then removes the
  subscription; loads up to 500 balance rows just to read the last one (P‑PERF‑03).

### Other repositories
- `PixelChangedEventRepository` — `GetByPixelIdAsync` (no limit), `GetByCanvasIdAsync` (no cap),
  `CleanPixelHistoryBatchAsync` materializes all events for the given pixel IDs into memory to
  keep the newest N (P‑PERF‑02).
- `ColorRepository` — returns colors in config palette order; `GetDefautColor()` (typo) returns
  `#FFFFFF`.
- `LoginEventRepository` — simple add + last‑100 lookup (`//ToDo: Add tests.`).

## `CanvasWriteCoordinator`

A `ConcurrentDictionary<Guid, SemaphoreSlim>` — **one semaphore per canvas Id**, capacity 1.
`ExecuteAsync` acquires that canvas's semaphore for the whole action. Two limitations:
**semaphores are never evicted** (unbounded growth — P‑CON‑03), and coordination is
**in‑process only** (not safe with multiple API instances — P‑CON‑04).

## `DbSeeder` — `SeedDefaults`

Run by `DbMigrator` at startup:

1. Insert any missing palette colors.
2. `CleanupColorsRemovedFromConfigAsync` — reassign pixels/events from removed colors to
   `#FFFFFF`, then delete the colors. **Blocks 10 s** on every startup that needs cleanup
   (P‑OPS‑03).
3. Create the **admin/master** user from `MASTER_USER` / `MASTER_EMAIL` / `MASTER_PASSWORD`
   (defaults `admin` / `linteumsu@gmail.com` / `password`, with three warnings).
4. For each default canvas: create it (creator = admin) or `SyncSeedCanvasAsync` (resize,
   delete out‑of‑bounds pixels + history, update mode).
5. Subscribe **all** users to secondary default canvases that have zero subscriptions (N
   transactions + N balance writes — P‑PERF‑04).

`GetInactiveCanvasCleanupCandidatesAsync` returns unprotected canvases inactive since a cutoff.

## `HourlyCanvasIncomeProcessor`

`ProcessAsync` joins `Subscriptions × Economy Canvases × non‑guest Users × Pixels`, counts owned
pixels per (user, canvas), and credits income. Formula:

```
income = floor( 10 * (1 + log2(1 + ownedPixels)) )
```

(e.g. 0 px → 10, 1 → 20, 3 → 30, 7 → 40, 15 → 50 — diminishing logarithmic returns), tagged
`BalanceChangedReason.HourlyIncome`. Aggregates a `CanvasIncomeBatchDto` per canvas.

## Other types

- `MappingProfile` (AutoMapper) — bidirectional maps for all entities; `Canvas→CanvasDto` sets
  `IsPasswordProtected = !IsNullOrWhiteSpace(PasswordHash)`.
- `RepositoryManager` — hand‑rolled Unit‑of‑Work facade; constructs all 8 repositories with a
  shared `AppDbContext`/`IMapper`/`Config`/`IPixelNotifier`/cache/coordinator.
- `IPixelNotifier` / `SimplePixelNotifier` — default no‑op notifier (`Console.WriteLine`); the
  real SignalR notifier is registered in the API host.

## Notable issues (full detail in [Problems.md](../Problems.md))

In‑process‑only coordination (P‑CON‑04); post‑commit side effects in `Subscribe` /
`AddOrUpdateUserAsync` (P‑CON‑01); `PixelRepository` writes balance events directly, bypassing
the negative‑balance guard (P‑CON‑05); unbounded history materialization (P‑PERF‑02); static
mutable `DefaultColorId` (P‑CON‑06); weak password hashing lives in `Linteum.Shared`
(P‑SEC‑04).
