# Linteum.Shared

The common vocabulary of the solution: DTOs, enums, configuration constants, HTTP headers,
and cross‑cutting helpers (security, image/text rasterization). Depends on nothing else in
the solution (only NuGet packages). Target framework `net10.0`.

Key packages: `NLog` 6.1.1, `SixLabors.ImageSharp` 3.1.12, `SixLabors.ImageSharp.Drawing` 2.1.7.
Uses ImageSharp (not `System.Drawing`) — cross‑platform.

## Enums

- **`CanvasMode`** — `Normal = 1`, `Economy = 2`, `FreeDraw = 3`.
- **`BalanceChangedReason`** — `Regular=1, PixelPayment=2, Pity=3, Other=4, Subscription=5,
  Unsubscription=6, HourlyIncome=7`.
- **`LoginMethod`** — `Password=1, Google=2, Guest=3`.

## `Config` (static configuration constants)

Operational defaults used across the app:

| Constant | Value | Notes |
|---|---|---|
| `DefaultCanvasName` | `"home"` | The main seeded canvas. |
| `SecondaryDefaultCanvasNames` | `home_FreeDraw`, `home_Economy`, `VanGogh`, `Thailand` | New users are auto‑subscribed. |
| `DefaultPage` | `"/canvas/home"` | Post‑login landing page. |
| `DefaultCanvasWidth/Height` | `1024 × 1024` | Size for seeded canvases. |
| `NormalModeDailyPixelLimit` | `100` | Per account, per canvas. |
| `GuestNormalModeDailyPixelLimit` | `10` | Per guest, per canvas. |
| `GuestUserLifetimeHours` | `24` | Guest auto‑cleanup threshold. |
| `ExpiredSessionTimeoutMinutes` | `60` | Sliding session expiry. |
| `Colors` | 34‑color palette | Seeded into the DB. |
| `MasterPasswordHash` | literal `"MasterPasswordHash"` | Placeholder constant (not used as a real hash). |
| `GoogleClientId` | runtime | Populated from `GOOGLE_CLIENT_ID`. |

`GetProtectedCanvasNames()`, `GetNonUnsubscribableCanvasNames()` (just `home`),
`GetDefaultCanvases()`.

> **Configurability gap:** these constants are **not** bound from environment or `appsettings`
> despite `Config` being registered as a singleton. Changing a quota or guest lifetime requires
> a recompile (P‑CFG‑01).

## `CustomHeaders`

`public const string SessionId = "Session-Id";` — the single auth header used everywhere.

## `GuestUserHelper`

`GuestUserNamePrefix = "guest"`, `GuestEmailDomain = "guestmail.com"`. `IsGuest(...)` and
`BuildGuestEmail(userName) → $"{userName}@guestmail.com"`.

## `SecurityHelper`

`Salt` is read from `PASSWORD_SALT` (empty fallback). `HashPassword(password, salt?)` =
**SHA‑256** of `password + salt`, Base64‑encoded.

> ⚠️ **SHA‑256 is a fast hash, not a password KDF.** There is no per‑user salt and no work
> factor, so stored password hashes are vulnerable to offline brute‑force. This is the system's
> most significant credential‑handling weakness (P‑SEC‑04). Recommendation: PBKDF2 / bcrypt /
> Argon2, ideally per‑user salts.

## Helpers (`Helpers/`)

### `ImageConverter` — image → pixels
`ConvertImageToGrid(path|image, w, h, palette)` resizes the image (ImageSharp), iterates pixel
rows, and matches each pixel to the nearest palette color by **squared Euclidean RGB
distance** (early‑exit on exact match). Returns `ColorDto[,]`. `GroupCoordinatesByColor(grid)`
groups coordinates for efficient single‑color batch requests. Used by the canvas‑from‑image
seed path and by the bots.

### `TextConverter` — text → pixels (FreeDraw text tool)
`FromImage(textColor, backgroundColor?, text, fontSize)` rasterizes text to a
`ColorDto?[w,h]` grid (null cell = transparent). Constants: `MinimumFontSize=4`,
`MaximumFontSize=25`, `DefaultFontSize=12`. Font fallback chain: Arial → Segoe UI → Tahoma →
Verdana → DejaVu Sans → Liberation Sans → Noto Sans. Uses `TextMeasurer.MeasureBounds` and
draws onto a transparent mask (alpha > 0 → text color). Margin = `Ceil(fontSize*0.4)` (min 2);
line height from measuring `"Ag"`. `GetPreviewMetrics` provides safe fallbacks for the Blazor
preview.

## DTOs (`DTO/`)

Grouped summary (all plain records/classes serialized as JSON):

- **Canvas/pixel core** — `CanvasDto`, `PixelDto`, `PixelBatchDto` (+ `CoordinateDto`),
  `PixelBatchChangeRequestDto`, `PixelBatchChangeResultDto` (`StoppedByBudget`,
  `StoppedByNormalModeLimit`, `UsedMasterOverride`), `PixelBatchDeleteRequestDto`,
  `PixelBatchDeleteResultDto`, `NormalModeQuotaDto`.
- **Text** — `TextDrawRequestDto` (`X, Y, Text, FontSize, TextColorId, BackgroundColorId?`).
- **Stroke playback** (`StrokePlaybackDtos.cs`) — `StrokePlaybackMetadataDto`,
  `ConfirmedPixelPlaybackBatchDto`, `ConfirmedPixelDeletionPlaybackBatchDto`.
- **User/auth** — `UserDto`, `LoginResponse`, `UserPaswordDto` (typo), `GoogleLoginCodeRequestDto`,
  `GoogleLoginRequestDto`.
- **Canvas management** — `CanvasOperationResponseDto`, `CanvasPasswordDto` /
  `CanvasPaswordDto.cs` (**duplicate with a typo filename** — P‑MAIN‑01),
  `SubscribeCanvasRequestDTO.cs` (DTO/Dto casing typo).
- **Subscription** — `SubscriptionDto`.
- **Chat** — `CanvasChatMessageDto`, `SendCanvasChatMessageRequestDto`
  (`const MaxMessageLength = 4000`).
- **Color** — `ColorDto`.
- **Events** — `LoginEventDto`, `PixelChangedEventDto`, `BalanceChangedEventDto`,
  `HistoryResponseItem`.
- **Economy/maintenance** — `CanvasIncomeBatchDto` + `CanvasIncomeUpdateDto`,
  `CanvasMaintenanceProgressDto`.

## Exceptions (`Exceptions/`)

All extend `InvalidOperationException`: `BalanceUpdateException`, `CanvasNotFoundException`,
`InvalidCanvasPasswordException`, `UserAlreadySubscribedException` (in
`UserAlreadySubsribedException.cs` — filename typo). Mapped to HTTP codes in the controllers.

## Filename/casing typos to clean up

`CanvasPaswordDto.cs`, `UserPaswordDto.cs`, `UserAlreadySubsribedException.cs`,
`SubscribeCanvasRequestDTO.cs` (P‑MAIN‑01).
