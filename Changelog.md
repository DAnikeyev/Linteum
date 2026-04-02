# 0.1.0 - 2026/04/01

## API and backend
- Added `POST /users/validate` to validate active sessions and return the authenticated user payload for existing sessions.
- Added richer logging in `PixelsController` for invalid sessions, missing canvases, failed writes, and successful pixel updates.
- Expanded service bootstrap logging in API startup and DI registration.
- Added per-pixel history cleanup in `DbCleanupService` with repository support (`CleanPixelHistory`) to keep only recent history entries.
- Added `DailyCleanupService` (canvas/subscription cleanup) and `MinuteCleanupService` (expired-session cleanup and forced SignalR group removal).
- Extended session cleanup logic to return expired session metadata, enabling downstream cleanup actions.

## Realtime and sessions
- Updated SignalR `CanvasHub` to resolve connected usernames from session header/query token and track named connections.
- Added realtime broadcasting of online users (`UpdateOnlineUsers`) on group join, leave, disconnect, and session-expiration cleanup.
- Added connection tracker support for:
  - per-group usernames
  - per-connection group listing
  - reverse lookup of active connections by username
- Added session-aware SignalR client setup in the Blazor app by passing `Session-Id` into hub connection headers.

## Canvas and pixel workflows
- Added pixel history DTO (`HistoryResponseItem`) and UI rendering for pixel change history in `PixelManager`.
- Added client-side history refresh when the currently selected pixel changes via SignalR events.
- Improved canvas resize handling by registering/unregistering JS window resize listeners and recalculating viewport/fit dynamically.
- Updated canvas rendering styles for more consistent scaled rendering and pixelated visuals.
- Added safer selected/hovered pixel state transitions when canvases reload or selected pixels are cleared.

## Client API behavior and caching
- Added comprehensive structured logging in `MyApiClient` for request flow, failures, and successful operations.
- Added short-lived client caches for:
  - pixel data by `(canvas, x, y)`
  - pixel history by `pixelId`
- Added explicit cache invalidation hooks for pixel updates, history updates, canvas-level clearing, and session changes.
- Added cloning/normalization logic to prevent cache key mismatches and accidental mutation side effects.

## UI and UX
- Refreshed major page styling (login, signup, settings, canvas add/subscribe, pixel manager, sidebar, global app styles).
- Updated base layout background and sidebar/panel look for a more cohesive visual theme.
- Improved online user presentation and styling inside `PixelManager`.
- Adjusted canvas sidebar canvas-name layout behavior and unsubscribe control spacing.

## Bot/runtime/tooling updates
- Updated bot launcher to support explicit bot selection by argument (`Cleaner`, `Munch`, `VanGogh`) instead of hardcoded startup.
- Removed legacy YAML config copy behavior from bots project file.
- Upgraded solution target frameworks from `.NET 9` to `.NET 10` across API, Blazor app, domain, infrastructure, shared, bots, and tests.
- Updated Docker base/build images to `.NET 10`.
- Upgraded key package dependencies (EF Core, SignalR client, Npgsql, NLog, OpenAPI-related packages).

# 0.0.1 - 2026/02/19
 - Initial pre-release
