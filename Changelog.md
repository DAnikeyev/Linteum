# 0.2.3 - 2026/06/21

## Realtime
- Canvas changes are no longer silently lost when a client loads a snapshot slightly before (or reconnects slightly after) its live SignalR subscription: pixel-change broadcasts are now mirrored into a per-canvas expirable buffer, and a non-blocking post-job backfills the missed events by sequence after the snapshot is on screen (P‑RT‑05).
- Reconnect now rejoins the *current* canvas instead of a potentially stale group, and reconciles the disconnect gap from the buffer (P‑RT‑02).
- Added a connection-status banner so users see "Reconnecting…" / "Realtime connection lost" with a Retry button instead of a silent loss of realtime (P‑RT‑03).
- The 20 s initialization timeout no longer force-reloads the page; it shows a countdown and a manual Retry that rebuilds the connection, preserving pending strokes (P‑RT‑04).

## Performance
- Canvas image rendering (`GET /canvases/image/{name}`) now streams pixels straight into the raster instead of materializing the whole canvas into memory, removing the OOM risk on large canvases (P‑PERF‑01).
- Pixel history cleanup now prunes everything past the kept-window per pixel in a single server-side `ROW_NUMBER()` delete, with no client-side materialization (P‑PERF‑02).
- The seeder now bulk-subscribes all users to secondary default canvases in one transaction instead of one transaction per user (P‑PERF‑04).
- The Blazor client's pixel/history caches are now backed by a bounded TTL+LRU cache, so they can no longer grow without limit while staying effective for hot pixels (P‑PERF‑06).
- Render batches are sent to the canvas as typed parallel arrays instead of per-pixel JSON objects, cutting serialization cost on the hot pixel path (P‑PERF‑07).
- (Already resolved) `Unsubscribe` reads only the single latest balance row (P‑PERF‑03).

## Maintainability
- Renamed typo'd DTO/exception filenames and identifiers to their canonical spellings (`UserPaswordDto` → `UserPasswordDto`, `GetDefautColor` → `GetDefaultColor`, and the `CanvasPaswordDto`/`UserAlreadySubsribedException`/`SubscribeCanvasRequestDTO`/`ServiceCollectionExtenstions`/`PermamentDbTest` files), updating every reference (P‑MAIN‑01).
- Removed the dead single-pixel write helpers in `PixelRepository` (`TryChangePixelInternalAsync` and its Normal/Economy/FreeDraw variants plus the `PixelAttemptResult` record) that re-entered the batch path and were never reached (P‑MAIN‑02).
- Decomposed the `MyApiClient` god-class: the ~1170-line class that mixed HTTP access, caching, and session storage is now a thin forwarding facade over focused collaborators under `Linteum.BlazorApp/Api/` — `ApiHttp` (HTTP client), `PixelCacheManager` (the three caches), `SessionStore` (session/local-storage), and one repository per resource (Colors/Canvases/Subscriptions/CanvasChat/Pixels/History/Balance/Account). All 12 consuming components are unchanged (P‑MAIN‑03).

## UI
- The mouse-coordinate readout is no longer hidden by CSS while JavaScript populates it — the redundant `display: none` was removed so the element shows on hover (P‑UI‑01).

## Testing
- DB integration tests now run against an ephemeral Testcontainers PostgreSQL instead of a pre-provisioned localhost instance, and isolate per test with `TRUNCATE … RESTART IDENTITY CASCADE` instead of dropping/recreating the database (P‑TEST‑03, P‑TEST‑04).
- Added `WebApplicationFactory`-based API tests covering the session-auth contract: protected endpoints return 401 without a `Session-Id`, `[PublicEndpoint]`s are reachable, and `[DisabledEndpoint]`s return 404 (P‑TEST‑02).
- Removed the assertion-less `Hashing.cs` scratch test and normalized namespaces / removed dead files across the test projects (P‑TEST‑01, P‑TEST‑05).

## Networking / ops docs
- Documented explicit WebSocket hardening for the Blazor nginx proxy (`proxy_read_timeout`/`proxy_send_timeout` 3600s, `proxy_buffering off`) in `Networking-and-TLS.md`; applying it on the live VPS is operational (P‑NET‑02).

# 0.2.2 - 2026/05/07
- Added GuestMode.
- More small UI improvements and optimizations.
- Added image exporting.

# 0.2.1 - 2026/04/29

## Fluid Strokes and Interpolation
- Updated fluid stroke interpolation to improve stroke continuity and rendering smoothness.
- Refined stroke handling for more stable drawing behavior during fast or irregular input.

## Bug Fixes and Improvements
- Fixed FreeDraw text-tool behavior so selected pixels persist while interacting with text controls, the caret preview remains visible, and text submission works with `Ctrl+Enter`.
- Improved Blazor text-preview resilience by adding preview-safe font fallback logic and bundling runtime fonts in the Blazor Docker image.

## Lobby Chat
- Added lobby chat support for the main user flow.
- Improved realtime interaction in the lobby to make chat presence and messaging available alongside the canvas experience.

# 0.2.0 - 2026/04/28

## Canvas Modes and Creation
- Introduced dedicated canvas modes: `Normal`, `FreeDraw`, and `Economy`, with mode-aware behavior across creation, rendering, and server-side validation.
- Expanded canvas creation to support both blank canvases and JPG-based starting images, including dimension checks and clearer setup guidance.
- Added stronger default-canvas seeding and configuration support for mode-specific canvases such as `home_FreeDraw` and `home_Economy`.

## Pixel and Drawing Workflows
- Added batch pixel change support with detailed per-request results for successful updates, deduplication, budget stops, and normal-mode daily-limit handling.
- Added batch pixel deletion support for FreeDraw canvases to speed up cleanup and editing workflows.
- Added queued text drawing for FreeDraw canvases, enabling users to place text with configurable font size, text color, and optional background color.

## Economy and Event Processing
- Improved repository logic and event handling for balance changes, login activity, and pixel-changed event delivery to better support the new drawing workflows.
- Added queue-based background processing for canvas maintenance and text drawing requests to keep interactive operations responsive.
- Expanded database coverage for the new batch-update, seeding, and queue-processing scenarios.

## UI and UX Improvements
- Refined the canvas page and pixel manager to better support mode-specific actions, bulk operations, and clearer feedback while editing.
- Reworked canvas add and subscribe pages with search, mode labels, password hints, and more guided canvas setup flows.
- Updated styling across the new canvas-management experience for a cleaner, more consistent release.

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
- Added `XeroxBot` for reproducing images onto canvases with batch pixel placement.
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
