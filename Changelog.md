# 0.1.2 - 2026/04/22

## Performance and Reliability
- Added `PixelChangeCounterService` to aggregate and log successful pixel updates per second by canvas.
- Reworked pixel-history cleanup to batch processing (`CleanPixelHistoryBatchAsync`) for reduced database load.
- Improved concurrent pixel updates in sandbox mode with conflict-aware retry handling for unique-key races.
- Switched API DbContext registration to pooled contexts and added in-memory cache for improved runtime efficiency.

## Configuration and Data Seeding
- Increased default canvas size to `1024x1024` and added `Thailand` to secondary canvases.
- Expanded the default color palette with additional shades (e.g., Neon Lime, Azure, Electric Violet, Hot Pink, Olive Drab, Lemon Lime).
- Added color synchronization cleanup in DB seeding: colors removed from config are reassigned to default color before deletion.
- Added default-canvas dimension synchronization with automatic cleanup of out-of-bounds pixels and related history.

## API and Observability
- Added more structured logging across controllers and repositories for pixel retrieval, update flow, and cleanup operations.
- Added configurable console log minimum level via `NLOG_CONSOLE_MIN_LEVEL` (`.env`, `docker-compose`, and `nlog.config` integration).
- Updated startup/runtime configuration with explicit `.env` loading and minimum thread pool settings.

## UI and UX Improvements
- Improved Login and Signup flows with session-check loading states, clearer busy messaging, and redirect-on-valid-session behavior.
- Enhanced sidebar behavior with persisted collapsed state handling, mobile auto-collapse, backdrop close action, and canvas sorting.
- Updated FAQ with additional public canvas references and source-code/contact details.

## Bots and Deployment Tooling
- Added bot image assets (`Levitan.jpg`, `M42.jpg`, `Thailand.jpg`, `home.jpg`) and ensured they are copied to output.
- Updated Docker Compose with PostgreSQL tuning settings, API connection pool settings, and a fixed network name.
- Expanded Docker bot usage docs with Xerox bot examples and guidance for running prebuilt images without rebuilds.

# 0.1.1 - 2026/04/07

## Authentication and Security
- Added Google OAuth2 authentication support (`LoginWithGoogle`, `LoginWithGoogleCode`).
- Added session validation endpoint (`POST /users/validate`) to verify and refresh active sessions.
- Implemented `Session-Id` header-based authentication across all API controllers.
- Added password protection and verification for canvases.

## Canvas and Rendering
- Introduced a performance-optimized Javascript-based canvas renderer for smooth panning and zooming.
- Added client-side GPU-accelerated viewport scaling and translation to minimize SignalR round-trips.
- Made canvas image loading fully awaitable to prevent white flash on initial render.
- Moved coordinates display to a Blazor-managed fixed-position element for smoother sidebar transitions.
- Consolidated JS layout measurements into single interop calls (`getLayoutMetrics`, `getElementSize`) to reduce round-trips.
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
- Replaced plain-text loading indicators with CSS spinner animations across canvas, colors, and sidebar.
- Added fade-in reveal animation for the canvas shell to eliminate layout flash during initialization.
- Refactored canvas page to use flexible container layout for more robust viewport sizing.
- Enhanced Signup page with animated gradient background and dynamic floating blur effects.
- Updated base layout to use `dvh` units and flexbox for correct full-height rendering on mobile.
- Implemented responsive design for mobile devices and varied screen sizes.
- Refreshed styling for Login, Signup, and Settings pages.

## Bots and Tooling
- Introduced bot framework with `CleanerBot`, `MunchBot`, and artist bots (`VanGogh`, `VanGogh2`).
- Added `XeroxBot` for reproducing images onto canvases with multi-threaded parallel pixel placement (16 workers, channel-based queue).
- Added `Inception.jpg` and `Earth.jpg` reference images for bot use.
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
