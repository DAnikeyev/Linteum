# Linteum.BlazorApp

The web UI. **Pure Blazor Server (Interactive Server render mode)** — there is no `.Client`
WebAssembly project. The browser holds a Blazor circuit (one WebSocket) to a sticky replica;
all API and SignalR calls originate **on the Blazor server**, never in the browser. Target
framework `net10.0`; references `Linteum.Shared` only.

Key packages: `DotNetEnv` 3.1.1, `Microsoft.AspNetCore.SignalR.Client` 10.0.5, `NLog` 6.1.1 /
`NLog.Web.AspNetCore` 6.1.2, `SixLabors.ImageSharp` 3.1.12.

## Render‑mode architecture (important)

Evidence it is Blazor Server, not a unified Web App with a `.Client` project:

- `Linteum.BlazorApp.csproj` references only `Linteum.Shared` — no `Linteum.BlazorApp.Client`.
- `App.razor` renders `<Routes @rendermode="RenderMode.InteractiveServer">` and loads
  `blazor.web.js`.
- `Program.cs` registers only `AddInteractiveServerComponents()` / `AddInteractiveServerRenderMode()`.
- No `AuthenticationStateProvider`/`AuthorizeRouteView` — auth is handled manually in
  `BaseLayout.ValidateLocally()` and via the `Session-Id` header + `SessionExpired` hub event.

Consequence: the **same** SignalR technology is used twice — (1) as the Blazor circuit
transport (browser ↔ Blazor server) and (2) as a second hub connection from the `CanvasPage`
component to the API's `CanvasHub` (Blazor server ↔ API) for realtime pixel/chat events.

## `Program.cs`

- NLog bootstrap; loads `../.env`; reads `VERSION` (default `"dev"`).
- API base: DEBUG → `ApiBaseUrl` or `http://localhost:5182`; RELEASE →
  `http://{API_CONTAINER_NAME}:{API_CONTAINER_PORT}` (defaults `http://api:8080`). A named
  HttpClient `"ApiClient"`; the dev handler **bypasses SSL validation** (P‑SEC‑07).
- `ProtectedLocalStorage` / `ProtectedSessionStorage`; `LocalStorageService`,
  `CanvasChatStateService`, `NotificationService`; `Config` singleton (`GoogleClientId` from env).
- Data Protection keys persisted to `{ContentRoot}/keys`, app name `"LinteumApp"` (shared via the
  `blazor_keys` volume across replicas).
- Custom endpoint `GET /_canvas-image/{name}` (Session‑Id required) proxies image export to the
  API.
- Pipeline: exception handler + HSTS (non‑dev), `UseAntiforgery`, `MapStaticAssets`, the image
  proxy, `MapRazorComponents<App>()` + `AddInteractiveServerRenderMode()`.

## `MyApiClient` — the API gateway (facade) + collaborators

`MyApiClient` (`MyApiClient.cs`) is now a **thin forwarding facade** (`internal sealed`, ~130 lines,
no state/logic) over focused collaborators in the `Linteum.BlazorApp/Api/` namespace, all `internal
sealed` and **scoped** (one instance per Blazor circuit). It was decomposed from a single ~1170‑line
god‑class that mixed HTTP access, caching, and session storage (P‑MAIN‑03). The 12 consuming
components still `@inject MyApiClient` and call the same methods — zero consumer churn.

- **`ApiHttp`** — the HTTP client. Owns the server‑side `HttpClient` + `LocalStorageService`; `CreateAsync`
  builds a request with the `Session-Id` header attached (via `HttpRequest.AddSessionId`, reading the
  session from `ProtectedLocalStorage`).
- **`PixelCacheManager`** — the three client caches (all under one lock, 1‑minute TTL, bounded LRU from
  P‑PERF‑06): pixel cache keyed `(canvasName, x, y)` (normalized `Trim().ToUpperInvariant()`), history
  cache keyed `pixelId`, color cache (no expiry). Exposes the primitive get/set the repositories use plus
  the invalidation/write‑through hooks `CanvasPage` calls directly: `InvalidatePixelCache`/
  `InvalidateHistoryCache` (cascades to pixel)/`ClearCanvasCache`/`ClearAllCaches`, and
  `HandlePixelColorChanged`/`HandlePixelDeleted`/`ApplyBatchPaintCache`/`StorePaintedPixel`, plus the
  `GetWhiteColorId`/`IsPixelKnownWhite` queries. `SetSessionAsync` clears all caches on login/out.
- **`SessionStore`** — session persistence: `SetSessionAsync`/`PersistAuthenticatedUserAsync`/
  `ClearSession`/`GetCurrentUserIdAsync`/`GetCurrentLoginMethodAsync`/`IsGuestUserAsync`, storing
  `SessionId`/`UserId`/`UserName`/etc. in `ProtectedLocalStorage`.
- **Eight resource repositories** mirror the API one resource each: `ColorsRepository`,
  `CanvasesRepository` (CRUD/subscribe/erase/delete/export), `SubscriptionsRepository`,
  `CanvasChatRepository`, `PixelsRepository` (get/paint/batch-paint/batch-delete/text/quota),
  `HistoryRepository`, `BalanceRepository` (current gold), `AccountRepository`
  (login/signup/guest/Google/change‑name/change‑password). Shared status‑code → exception mapping
  (400 → funds/quota, 401/403 → `UnauthorizedAccessException`, 404 → not found) and error parsing
  (`ApiErrors`) live with the repositories that use them.

`Program.cs` registers all collaborators as scoped. No retry/backoff, no cancellation tokens, no HTTP
429 handling (unchanged).

## Pages (`Components/Pages/`)

| Page | Route | Role |
|---|---|---|
| `CanvasPage` (+ `.razor.cs`, `.Brush.cs`, `.SignalR.cs`) | `/canvas/{canvasName}` | The canvas itself. Two `<canvas>` elements (main + overlay), embeds `PixelManager` and `CanvasLobbyChat`. |
| `PixelManager` (+ `.razor.cs`, `.Actions.cs`) | (child) | Tool panel: palette, brush/eraser, text tool, economy info, quota, history, erase/delete, export. |
| `CanvasAdd` | `/canvas_add` | Create blank or JPG canvas (≤ 20 MB; dims validated with ImageSharp). |
| `CanvasSubscribe` | `/canvas_subscribe` | Search + subscribe by name (300 ms debounced search); password for protected canvases. |
| `CanvasLobbyChat` | (child) | Ephemeral per‑canvas chat (4000‑char limit, Enter‑to‑send, minimized state in SessionStorage). |
| `Login` | `/`, `/login` (EmptyLayout) | Email/password, Google, "Continue as guest"; auto‑guest via `?guest=1`. |
| `Signup` | `/signup` (EmptyLayout) | Email (must contain `@` and `.`), password 4–32, username 4–32. |
| `SettingsPage` | `/settings` | Rename; change password (password accounts only); logout. |
| `Colors` | `/colors` | Read‑only palette table (not admin‑gated). |
| `FaqPage` | `/faq` | Static help (quotas, economy formula, contact). |
| `RedirectToLogin` | — | Redirect to `/login`. |

### `CanvasPage` internals
- `OnParametersSetAsync` bumps a `_canvasLoadVersion` for race detection and arms a **20 s init
  watchdog** that forces a full reload on timeout.
- `OnAfterRenderAsync` creates a `DotNetObjectReference`, registers a resize listener, then
  initializes SignalR (`EnsureInteractiveReadyAsync`), loads the canvas, inits `CanvasRenderer`,
  and calls `canvasViewport.init`.
- **Brush pipeline** (`CanvasPage.Brush.cs`): JS callbacks `OnBrushPixelPaintRequested` (dedup
  via `_brushStrokePixels`), `OnBrushStrokeStarted/Ended`; a **75 ms** paint‑flush loop and a
  **200 ms** erase‑flush loop group batches (`BrushBatchSize = 500`) with stroke‑playback
  metadata and call `ApiClient.PaintBatch`. Rejected pixels trigger `RestoreRejectedBrushPixelsAsync`.
- **Confirmed playback**: a bounded `Channel<ConfirmedPlaybackWorkItem>` replays remote strokes;
  `NormalizeConfirmedPlaybackDuration` scales duration by queue depth.
- Remote handlers update/invalidation caches and enqueue to `CanvasRenderer`.
- `DisposeAsync` cancels timeouts, flushes, disposes JS refs + the hub connection + semaphores.

### `CanvasPage.SignalR.cs`
Hub URL `{ApiBaseUrl}/canvashub`, `Session-Id` header, `WithAutomaticReconnect()`. Handlers for
all hub events; `SessionExpired` clears storage and force‑loads `/login`. The reconnect‑rejoin
path uses a `_connectedGroup` that may be stale after a canvas switch (P‑RT‑02), and there is no
"reconnecting…" UX (P‑RT‑03).

## Layout (`Components/Layout/`)

- `BaseLayout` — main app shell; `ValidateLocally()` on first render checks the session and
  redirects to `/login?returnUrl=…&guest=1` for canvas routes if invalid. Renders `CanvasSidebar`,
  the route `@Body`, a "Back to hub" button (`HUB_LINK`), a version label, and `NotificationManager`.
- `CanvasSidebar` — hub navigation: username, FAQ, settings, canvas list with mode emojis
  (🎨 Normal / ✏️ FreeDraw / 💰 Economy), Add/Subscribe, **collapse persisted** to
  `LocalStorageKey.SidebarCollapsed`, mobile auto‑collapse at 768 px, two‑click unsubscribe.
- `EmptyLayout` — bare shell for login/signup.

## Services & notifications

- `CanvasRenderer` — **JS‑interop wrapper** (not a renderer itself). Background `RenderLoop`
  batches pixel updates (≤ 4096/frame) into `canvasRenderer.renderBatch`; dedups by coordinate.
- `ColorPaletteOrdering` — `SortByHue` (RGB→HSV) for the palette.
- `LocalStorageService` / `LocalStorageKey` / `SessionStorageKey` — typed wrappers over
  protected browser storage.
- `CanvasChatStateService` (scoped) — in‑memory per‑canvas chat state (≤ 100 messages, FIFO).
- `NotificationService` / `NotificationManager` — unbounded channel of `CustomNotification`
  (`Success/Error/Info`) rendered with fade animations.

## JavaScript interop (`wwwroot/js/`)

This is the performance core of the UI. Pan, zoom, and hover are handled **entirely in the
browser** — no .NET round‑trip — so the canvas stays smooth under load.

- **`canvasRenderer.js`** (`window.canvasRenderer`) — dual 2D‑canvas renderer (committed +
  overlay). Pixel state cached as `Uint8ClampedArray` via `getImageData`; writes via
  `fillRect`. Ripple/click‑preview effects; `image-rendering: pixelated`; `disableImageSmoothing`.
  One‑way (.NET→JS) interop; batches sent as JSON (P‑PERF‑07).
- **`canvas-viewport.js`** (`window.canvasViewport`) — pan/zoom/brush layer using a **GPU‑composited
  CSS `translate3d`** transform on the renderer (no canvas redraw on pan/zoom). Wheel
  zoom‑to‑cursor (factor 1.1/0.9, clamped), pinch‑zoom, **Bresenham line interpolation** for
  brushes, rAF batching, `devicePixelRatio` snapping. `[JSInvokable]` callbacks:
  `OnPixelClicked`, `OnBrushStrokeStarted/Ended`, `OnBrushPixelPaintRequested`,
  `OnPixelSelectionCleared`.
- **`canvas-helpers.js`** (`window.canvasHelpers`) — `getLayoutMetrics` (single batched interop),
  `getElementSize`, `enableEnterToSend`/`disableEnterToSend` (IME‑aware via `isComposing`),
  resize‑listener registration.
- **`google-auth.js`** (`window.googleAuth`) — GIS OAuth2 **code client** (popup); sends only the
  auth code to .NET (`OnGoogleCodeReceived`); token exchange happens server‑side.
- **`blazor-exit-navigation.js`** — adds a class to `<html>` on `.hub-back-btn` clicks to suppress
  the Blazor reconnect modal during intentional navigation.

## Dockerfile

Multi‑stage .NET 10 (`aspnet:10.0` + `sdk:10.0`); installs `fontconfig`/`fonts-dejavu-core` +
Kerberos libs for font rendering; `EXPOSE` from `BLAZOR_CONTAINER_PORT` (8090);
`ENTRYPOINT ["dotnet","Linteum.BlazorApp.dll"]`. No `HEALTHCHECK`, runs as root. Deployed as
**3 replicas** with a shared `blazor_keys` Data Protection volume.

## Notable issues (full detail in [Problems.md](../Problems.md))

`MyApiClient` was a god‑class (P‑MAIN‑03, ✅ fixed — now a facade over focused collaborators); unbounded client caches (P‑PERF‑06); SignalR
reconnect race / no reconnecting UX (P‑RT‑02/03); 20 s timeout forces full reload (P‑RT‑04);
coordinate display hidden by CSS while JS populates it (P‑UI‑01); `Colors` page not admin‑gated
(P‑SEC‑08); guest auto‑login via `?guest=1` is URL‑discoverable (P‑SEC‑06).
