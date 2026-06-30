# Linteum.Domain

The domain contract layer: entities and repository interfaces, with no EF Core or
infrastructure references (depends only on `Linteum.Shared`, plus `NLog` 6.1.1). Target
framework `net10.0`.

This project deliberately holds **no data‑access logic** — only the shape of the data and the
contracts the repositories must satisfy. EF Core configuration lives in `Linteum.Infrastructure`
(`AppDbContext.OnModelCreating`).

## Entities (`Entity/`)

### `Canvas`
| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `CreatorId` | `Guid` | FK → `User` (`OnDelete Restrict`). |
| `Name` | `string` | `[MaxLength(128)]`, unique. |
| `Width` / `Height` | `int` | `[Range(1,1920)]` / `[Range(1,1080)]`. |
| `CanvasMode` | `CanvasMode` | Default `Normal`. |
| `CreatedAt` / `UpdatedAt` | `DateTime` | |
| `PasswordHash` | `string?` | `[MaxLength(128)]`; `null` = public. |
| `Creator` | `User` | Nav (1 user → many canvases). |
| `Pixels` | `ICollection<Pixel>` | (Note: uninitialized — see P‑DATA‑05.) |
| `Subscriptions` | `ICollection<Subscription>` | init `List<Subscription>`. |

### `Pixel`
| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `X` / `Y` | `int` | Part of unique composite `(CanvasId, X, Y)`. |
| `ColorId` | `int` | FK to `Color` (no nav property / no FK configured — P‑DATA‑04). |
| `OwnerId` | `Guid?` | FK → `User`. |
| `CanvasId` | `Guid` | FK → `Canvas`. |
| `Price` | `long` | Economy price. |
| `Owner` | `User?` / `Canvas?` | Navs. |

### `User`
| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `UserName` | `string` | `[MaxLength(64)]`, unique. |
| `Email` | `string` | `[MaxLength(64)]`, unique. |
| `PasswordHashOrKey` | `string` | `[MaxLength(128)]` (hash for password accounts, Google subject/key for Google accounts). |
| `LoginMethod` | `LoginMethod` | `Password` / `Google` / `Guest`. |
| `CreatedAt` | `DateTime` | |
| Collections | | `LoginEvents`, `PixelChangedEvents`, `BalanceChangedEvents`, `Subscriptions`. |

There is **no `Balance` field** — balance is derived from the newest `BalanceChangedEvent`.
There is no explicit "role"/"admin" column; the master/admin user is created by the seeder and
master authority is conveyed by the `MASTER_PASSWORD` override.

### `Subscription`
Composite primary key `{UserId, CanvasId}`; navs to `User` + `Canvas`; both FKs `Cascade`.

### `BalanceChangedEvent` (the balance ledger)
`Id`, `UserId`, `CanvasId`, `OldBalance: long`, `NewBalance: long`, `Reason: BalanceChangedReason`,
`ChangedAt`. Append‑only; the newest `NewBalance` for `(user, canvas)` is the current balance.

### `PixelChangedEvent` (per‑pixel history)
`Id`, `PixelId`, `OldOwnerUserId?`, `OwnerUserId`, `OldColorId`, `NewColorId`, `ChangedAt`,
`NewPrice`. Pruned to ≤ 10 rows per pixel.

### `LoginEvent`
`Id`, `UserId`, `Provider: LoginMethod`, `LoggedInAt`, `IpAddress?` (`[MaxLength(128)]`).

### `Color`
`Id: int`, `HexValue: string` (`[MaxLength(7)]`, regex `^#[0-9A-Fa-f]{6}$`), `Name: string?`.

### `UserSession` (`Linteum.Domain/UserSession.cs`)
A plain POCO — `SessionId`, `UserId`, `CreatedOrUpdatedAt`. **Not mapped** to the database;
sessions live in memory in `SessionService`.

## Repository contracts (`Repository/`)

| Interface | Key operations |
|---|---|
| `IUserRepository` | `GetByEmail/UserName/Id`, `GetById(IList<Guid>)`, `AddOrUpdateUserAsync`, `CreateGuestUserAsync`, `DeleteExpiredGuestUsersAsync`, `DeleteUserAsync`, `TryLogin`. |
| `ICanvasRepository` | `GetByUserId`, `GetByName`, `GetAll(includePrivates)`, `SearchByName`, `TryErase/Delete/DeleteGradually*`, `CheckPassword`, `TryAddCanvas`. |
| `IPixelRepository` | `GetByCanvasId`, `GetByOwnerId`, `GetByPixelDto`, `TryChangePixelAsync`, `TryChangePixelsBatchAsync`, `TryDeletePixelsBatchAsync`, `GetNormalModeQuotaAsync`. |
| `IPixelChangedEventRepository` | `GetByUserId/PixelId/CanvasId`, `AddPixelChangedEvent`, `CleanPixelHistoryBatchAsync`. |
| `IBalanceChangedEventRepository` | `GetByUserId`, `GetByUserAndCanvasId`, `TryChangeBalanceAsync`. |
| `ISubscriptionRepository` | `GetByUserId`, `GetByCanvasId`, `Subscribe`, `Unsubscribe`. |
| `ILoginEventRepository` | `GetByUserIdAsync`, `AddLoginEvent`. |
| `IColorRepository` | `GetAllAsync`, `GetDefautColor()` (typo preserved). |

Implementations live in `Linteum.Infrastructure`; see
[Linteum.Infrastructure.md](Linteum.Infrastructure.md).
