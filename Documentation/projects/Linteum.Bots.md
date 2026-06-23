# Linteum.Bots

A .NET 10 console app (`Exe`) that drives automated canvas clients against the Linteum API
over plain HTTP — each bot behaves like a normal user (login → session → paint). References
only `Linteum.Shared` (which transitively brings ImageSharp). Behind the `bots` Docker Compose
profile, so it only starts with `docker compose --profile bots …`.

## Entry point (`Program.cs`)

`Main(string[] args)` selects a bot by `args[0].ToLower()`:

| Arg | Bot | Extra args |
|---|---|---|
| `cleaner` | `CleanerBot` | `[canvasName]` (default `"Default"`) |
| `munch` | `MunchBot` | — |
| `vangogh` | `VanGoghBot` | — |
| `vangogh2` | `VanGogh2Bot` | — |
| `xerox` | `XeroxBot` | `<canvasName> <imageFile>` (required) |

With no args it prints usage and exits. Unknown bots throw `ArgumentException`.

## `BotBase` (abstract)

Common machinery for every bot:

- Reads env: `BOT_API_URL` (default `http://localhost:8080`),
  `BOT_PASSWORD` (required bot-account password, read from the environment so no secret lives
  in source, P‑SEC‑10), `BOT_SERVICE_TOKEN` (optional; sent on batch paint requests to bypass
  Economy/Normal limits — a dedicated secret, never the API `MASTER_PASSWORD`, P‑SEC‑11),
  `BOT_TIMEOUT_MINUTES` (overall run timeout).
- `HttpClientHandler` trusts the system trust store by default; the TLS-validation bypass is
  dev-only and opt-in via `BOT_INSECURE_SKIP_TLS_VALIDATION` (P‑SEC‑09). Per‑request
  `HttpClient.Timeout = 10s`.
- `RunAsync()`: login/register → attach `Session-Id` → `GET /Colors` →
  `GetOrCreateCanvasAsync()` → start a timeout CTS + a 1 s throughput‑logger task →
  `RunBehaviorAsync(...)`.
- Painting helpers: `TryPaintPixelAsync`, `TryPaintPixelsAsync` (routes uniform batches to
  `change-batch-coordinates`, otherwise `change-batch`), `TryPaintCoordinatesAsync`,
  `PaintCoordinateBatchWithRetriesAsync` (`MaxRetries = 5`). Constants: `DefaultTimeout = 300 min`,
  `MaxPaintBatchSize = 500`.
- Abstract: `GetOrCreateCanvasAsync()`, `RunBehaviorAsync(canvas, colors, ct)`.

## The bots

| Bot | Email | Canvas | Size | Mode | Image | Behavior |
|---|---|---|---|---|---|---|
| `CleanerBot` | cleaner@ | arg | existing | — | — | Clears the whole canvas to white; batches of 500; 5 retries. |
| `MunchBot` | munch@ | `Munch` | 100×127 | FreeDraw | `Scream.jpg` | Infinite random painter (80% image / 20% white); batch 100; 1 ms. |
| `VanGoghBot` | vangogh@ | `VanGogh` | 100×80 | FreeDraw | `StarryNight.jpg` | Infinite random painter (99% image / 1% white); batch 100; 1 ms. |
| `VanGogh2Bot` | vangogh2@ | `VanGogh2` | 100×80 | FreeDraw | `StarryNight.jpg` | Deterministic row‑major sweep; batch 100; 10 ms. |
| `XeroxBot` | xerox@ | arg | = image native dims | FreeDraw | arg | One‑shot full reproduction: Fisher‑Yates shuffle, color‑grouped batches of 500, 5 retries. **Single‑threaded** (see note). |

> ✅ `XeroxBot` is single‑threaded (color‑grouped batches of 500); the `Changelog.md` entry for
> it no longer claims a parallel "16 workers" implementation (P‑DOC‑01, corrected).
>
> ✅ `VanGoghBot` (`VanGogh`) and `VanGogh2Bot` (`VanGogh2`) paint separate canvases, so running
> both concurrently no longer makes them fight over the same board (P‑BOT‑03, fixed).

## Image assets

Eight JPGs are bundled as content (`PreserveNewest`): `StarryNight.jpg`, `Scream.jpg`,
`Inception.jpg`, `Earth.jpg`, `Levitan.jpg`, `M42.jpg`, `Thailand.jpg`, `home.jpg`. Only
`Scream.jpg` (MunchBot) and `StarryNight.jpg` (VanGogh*) are referenced by code; the rest are
available for `xerox <canvas> <file>`. (`XeroxBot` matches the canvas size to the image's native
dimensions via `Image.Identify`.)

## Dockerfile & operation

Multi‑stage .NET 10 on the **runtime** image (`runtime:10.0`, no ASP.NET needed); no `EXPOSE`,
no `HEALTHCHECK`, no default `CMD`. Therefore `docker compose --profile bots up` starts a
container that prints usage and exits; the intended usage is `docker compose run --rm linteum-bots
<bot> [args]` (see `DockerCheatsheet.md`). Compose sets `BOT_API_URL=http://linteum-api:8080`,
`BOT_PASSWORD=${BOT_PASSWORD}`, `BOT_SERVICE_TOKEN=${BOT_SERVICE_TOKEN}`,
`BOT_TIMEOUT_MINUTES=10`.

## Notable issues (full detail in [Problems.md](../Problems.md))

Hardcoded bot credentials removed from source — `BOT_PASSWORD` is read from the environment
(P‑SEC‑10, ✅ fixed); TLS validation restored — bypass is opt-in via
`BOT_INSECURE_SKIP_TLS_VALIDATION` (P‑SEC‑09, ✅ fixed); bots no longer hold `MASTER_PASSWORD` —
they use a paint-only `BOT_SERVICE_TOKEN` (P‑SEC‑11, ✅ fixed);
the `bots` profile with `restart: unless-stopped` and no command will restart‑loop a no‑op
container if enabled via `up` (P‑OPS‑05).
