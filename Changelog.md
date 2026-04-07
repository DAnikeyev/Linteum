# 0.1.1 - 2026/04/07

## Authentication and Security
- Added Google OAuth2 authentication support (`LoginWithGoogle`, `LoginWithGoogleCode`).
- Added session validation endpoint (`POST /users/validate`) to verify and refresh active sessions.
- Implemented `Session-Id` header-based authentication across all API controllers.
- Added password protection and verification for canvases.

## Canvas and Rendering
- Introduced a performance-optimized Javascript-based canvas renderer for smooth panning and zooming.
- Added client-side GPU-accelerated viewport scaling and translation to minimize SignalR round-trips.
- Added canvas search functionality by name (`GET /canvases/search`).
- Added support for user-owned canvases with private/public visibility settings.
- Added canvas image export endpoint (`GET /canvases/image/{name}`) to download snapshots.

## Realtime and Backend
- Integrated SignalR `CanvasHub` for real-time pixel updates and online user tracking.
- Added `ConnectionTracker` service to monitor active connections and per-canvas participant counts.
- Implemented background cleanup services (`DailyCleanupService`, `MinuteCleanupService`) for expired sessions and database maintenance.
- Expanded repository layer with structured logging and performance tracking.

## UI and UX Improvements
- Rebuilt Blazor application layout with a responsive sidebar and unified navigation.
- Added `PixelManager` component for detailed pixel inspection and history viewing.
- Implemented responsive design for mobile devices and varied screen sizes.
- Refreshed styling for Login, Signup, and Settings pages.

## Bots and Tooling
- Introduced bot framework with `CleanerBot`, `MunchBot`, and artist bots (`VanGogh`, `VanGogh2`).
- Updated Docker configuration for multi-service deployment with environment variable support.
- Added database migration and seeding tools for automated environment setup.

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
