# Problems

A consolidated list of problems found across the Linteum codebase, its deployment, and the
monitoring stack, each with a **severity**, a short description, where it lives, and a
**suggested solution**. Severities:

- 🔴 **Critical** — exploitable security hole or data‑loss / data‑corruption risk.
- 🟠 **High** — correctness bug, reliability risk, or a problem likely to bite under load.
- 🟡 **Medium** — quality / maintainability / operability issue with a real but bounded impact.
- ⚪ **Low** — cosmetic, minor, or hygiene.

IDs are grouped by area: `SEC` security · `CON` concurrency/integrity · `DATA` schema ·
`PERF` performance · `RT` realtime · `OPS` operations/deployment · `NET` networking ·
`OBS` observability · `CFG` config · `MAIN` maintainability · `UI` UI/UX · `TEST` testing ·
`BOT` bots · `DOC` documentation.

## Summary

| ID | Severity | Area | Problem | Resolution |
|---|---|---|---|---|
| P‑SEC‑01 | 🔴 | SEC | Many REST endpoints have no authentication. | ✅ Fixed: `SessionAuthMiddleware` gates every controller action by default; `[PublicEndpoint]` opts out (login/signup/validate/guest/google/colors); `[DisabledEndpoint]` returns 404 for ~16 frontend-unused endpoints. In-method session checks removed; `HttpContext.GetSessionUserId()` derives the acting user. |
| P‑SEC‑02 | 🔴 | SEC | Password hashes passed in query strings. | ✅ Fixed: all credential material moved to POST bodies — `LoginRequestDto`, `SignupRequestDto`, `ChangePasswordRequestDto`, `CreateCanvasRequestDto`; subscribe + image-upload form carry plaintext `Password`. No secrets in URLs. |
| P‑SEC‑03 | 🟠 | SEC | Master‑password compare is non‑constant‑time; bypasses all batch limits. | ✅ Fixed: master-password compare is constant-time (`FixedTimeEquals`), and the bypass is no longer a shared magic string handed to bots — bots use a separate `BOT_SERVICE_TOKEN` (P‑SEC‑11). |
| P‑SEC‑04 | 🔴 | SEC | Passwords hashed with plain SHA‑256 (no KDF); `==` comparisons. | ✅ Fixed: server-side PBKDF2-HMAC-SHA256 (600k) + random per-credential salt + `PASSWORD_SALT` pepper, in a self-describing string (no DB migration); constant-time verify; legacy SHA-256/plaintext credentials auto-upgrade on next login/subscribe. Covered by `SecurityHelperTests`. |
| P‑SEC‑06 | 🟠 | SEC | Guest sessions mintable via `?guest=1` URL; bots hold the master password. | 🟡 Partial: bots no longer hold the master password — they use a dedicated `BOT_SERVICE_TOKEN` (P‑SEC‑11). Guest-session minting via `?guest=1` is still unrate-limited. |
| P‑SEC‑09 | 🟠 | BOT | Bots disable TLS certificate validation. | ✅ Fixed: `BotBase` now trusts the system trust store by default; the validation bypass is dev-only and must be opted into explicitly via `BOT_INSECURE_SKIP_TLS_VALIDATION` (logs a warning when active). |
| P‑SEC‑10 | 🟠 | BOT | Hardcoded, weak bot credentials committed in source. | ✅ Fixed: bot passwords removed from source entirely; `BotBase` reads `BOT_PASSWORD` from the environment (required, fail-fast). Non-secret service-account identifiers (email/userName) remain in code. |
| P‑SEC‑11 | 🔴 | BOT | Bots effectively hold `MASTER_PASSWORD` (quota/balance bypass). | ✅ Fixed: bots no longer receive `MASTER_PASSWORD`; they use a dedicated `BOT_SERVICE_TOKEN` sent as `ServiceToken` on the two batch-paint endpoints. The API validates it separately (constant-time), grants paint-only override (never delete), and optionally scopes it to `BOT_ALLOWED_CANVASES`. |
| P‑CON‑01 | 🟠 | CON | Post‑commit side effects (`Subscribe`, `AddOrUpdateUser`) can leave partial state. | ✅ Fixed: balance credit moved inside `Subscribe`'s transaction (before commit); `Unsubscribe` wraps balance‑zeroing + delete in one transaction under the canvas lock; a new user's main‑canvas subscription + `+1` credit now commit atomically with the user row. |
| P‑CON‑02 | 🟡 | CON | Unique‑violation conflict returns empty (no retry) on the hot write path. | ✅ Fixed: `ExecuteFreeDrawChunkAsync` retries once on a `UniqueViolation` (reloads the conflicting pixel and updates it) before returning empty. |
| P‑CON‑03 | 🟡 | CON | `CanvasWriteCoordinator` semaphore dictionary never evicts (leak). | ✅ Fixed: replaced the per‑canvas `ConcurrentDictionary` with a fixed pool of striped `SemaphoreSlim`s (bounded memory; same canvas still serializes). |
| P‑CON‑05 | 🟠 | CON | `PixelRepository` writes balance events directly, bypassing the negative‑balance guard. | ✅ Fixed: economy payments now route through the shared `TryChangeBalanceCoreAsync` (one `PixelPayment` ledger event per chunk) so the negative‑balance guard always applies. |
| P‑CON‑06 | 🟡 | CON | Static mutable `DefaultColorId` cache can serve stale data. | ✅ Fixed: removed the static cache; the default color is resolved per chunk from the palette. |
| P‑DATA‑01 | 🟡 | DATA | EF migrations live in `Api`; Infrastructure `.csproj` declares an empty `Migrations\`. | ✅ Fixed: removed the empty `<Folder Include="Migrations\"/>` placeholder from `Linteum.Infrastructure.csproj`; the API remains the migration host (`MigrationsAssembly("Linteum.Api")`). |
| P‑DATA‑03 | 🟡 | DATA | No unique index on `Color.HexValue`. | ✅ Fixed: `AppDbContext` declares `HasIndex(HexValue).IsUnique()`; migration `AddColorUniqueHexAndFks` adds `IX_Colors_HexValue` (unique) after deduping any existing duplicate colors, so it applies cleanly on live data. |
| P‑DATA‑04 | 🟡 | DATA | No FK from `Pixel.ColorId` (and event color ids) to `Color`. | ✅ Fixed: `Pixel.ColorId` and `PixelChangedEvent.Old/NewColorId` now carry FKs to `Colors.Id` (RESTRICT); the migration reassigns orphaned refs to the default color first and adds the constraints `NOT VALID`+`VALIDATE` to avoid write-locking large tables. Verified against a throwaway Postgres. |
| P‑DATA‑05 | 🟡 | DATA | `Canvas.Pixels` collection is uninitialized (NRE risk). | ✅ Fixed: `Canvas.Pixels` is now initialized `= new List<Pixel>()` (matching `Subscriptions`). |
| P‑PERF‑01 | 🟠 | PERF | Whole‑canvas endpoints materialize all pixels into memory (OOM risk). | ✅ Fixed: `GetImage` streams pixels into the raster via `StreamPixelsForCanvasAsync` (no full `List<PixelDto>` buffer); the `Pixels/canvases/{id}` endpoint stays `[DisabledEndpoint]`. |
| P‑PERF‑02 | 🟡 | PERF | History cleanup materializes all events into memory. | ✅ Fixed: `CleanPixelHistoryBatchAsync` is now a single server-side `ROW_NUMBER()` delete; nothing is loaded into memory. |
| P‑PERF‑03 | ⚪ | PERF | `Unsubscribe` loads up to 500 balance rows to read the last. | ✅ Fixed: reads only the single latest balance row (`OrderByDescending(ChangedAt,Id).Select(NewBalance).FirstOrDefaultAsync`). |
| P‑PERF‑04 | 🟡 | PERF | Seeder subscribes all users one‑by‑one (N transactions). | ✅ Fixed: `SubscribeAllAsync` inserts all subscriptions + balance credits in one transaction under the canvas lock. |
| P‑PERF‑06 | 🟡 | PERF | `MyApiClient` caches grow unbounded. | ✅ Fixed: pixel/history caches are now bounded TTL+LRU (`BoundedLruCache`); expired entries evict lazily and capacity-bounded LRU evicts the coldest. |
| P‑PERF‑07 | ⚪ | PERF | Render batches sent as JSON rather than typed arrays. | ✅ Fixed: `CanvasRenderer` sends parallel typed arrays (`xs/ys/rgbs/flags`) to `canvasRenderer.renderBatchTyped`, with the object form kept as a fallback. |
| P‑RT‑02 | 🟠 | RT | Reconnect rejoin may use a stale canvas group (lost events). | ✅ Fixed: reconnect rejoins the **current** canvas (not the stale `_connectedGroup` — it is cleared first since a reconnect gets a new connection id), then backfills the disconnect gap from the server-side expirable buffer via `GetCanvasChanges(lastSeq, joinSeq)` (P‑RT‑05). |
| P‑RT‑03 | 🟡 | RT | No "reconnecting…" UX; silent loss of realtime. | ✅ Fixed: `Reconnecting`/`Reconnected`/`Closed` hub events drive a connection-status banner ("Reconnecting…" / "Realtime connection lost" + Retry). |
| P‑RT‑04 | 🟡 | RT | 20 s init timeout forces a full page reload. | ✅ Fixed: the timeout no longer force-reloads; it shows a live countdown and a manual Retry that rebuilds the connection / reloads the canvas, preserving pending local strokes. |
| P‑RT‑05 | 🟠 | RT | Canvas can load a snapshot taken before its live subscription, silently missing changes in between. | ✅ Fixed: broadcasts are mirrored into a per-canvas expirable buffer (`ICanvasEventBuffer`, 45 s / 500-entry TTL) as sequence-numbered entries. After the snapshot paints, a non-blocking post-job fetches the **newest** entries (`GetRecentCanvasChanges`) and replays them — covering the load gap and any clobbered live update, idempotently (last-writer-wins). No anchor → nothing goes stale → no reload loop. |
| P‑OPS‑02 | 🟡 | OPS | `nlog.config` internal‑log path is a hardcoded Windows path. | ✅ Fixed: `internalLogFile` now uses the cross‑platform `${tempdir}` renderer (`/tmp` in the container) instead of `c:\temp`. |
| P‑OPS‑03 | 🟡 | OPS | Seeder blocks 10 s on color cleanup at startup. | ✅ Fixed: removed the unconditional 10 s `Task.Delay` from `CleanupColorsRemovedFromConfigAsync`. |
| P‑OPS‑04 | 🔴 | OPS | `linteum-db` running at 93 % of its 512 M limit (OOM risk). | ✅ Fixed: container limit 512 M → 1 G, `effective_cache_size` 512 → 768 MB (`shared_buffers` stays 256 MB). |
| P‑OPS‑05 | 🟡 | OPS | `bots` profile + `restart: unless-stopped` + no command → restart loop. | ✅ Fixed: `linteum-bots` now `restart: on-failure`; the no‑arg usage print (exit 0) no longer loops, a crashing bot still restarts. |
| P‑OPS‑06 | 🟡 | OPS | ~50 GB of reclaimable Docker build cache. | 🟡 Partial: added `scripts/maintenance/prune-docker-build-cache.sh` (keeps < 7 d, prunes the rest) + cron doc; scheduling it on the VPS is operational. |
| P‑OPS‑07 | 🟡 | OPS | Backups have no off‑host copy and no failure alerting. | 🟡 Partial: backup loop writes `.last-success`/`.last-failure` + a container `healthcheck` (unhealthy if no dump in 25 h); off‑host copy documented as a VPS‑side rclone step. |
| P‑NET‑02 | ⚪ | NET | No explicit WebSocket timeouts/buffering settings on the Blazor proxy. | ✅ Fixed (docs): added `proxy_read_timeout`/`proxy_send_timeout` 3600s + `proxy_buffering off` to the Blazor nginx block in `Networking-and-TLS.md`; the live VPS change is operational. |
| P‑MAIN‑01 | ⚪ | MAIN | DTO/exception filenames with typos (`Pasword`, `Subsribed`, `DTO` casing). | ✅ Fixed: renamed every typo'd file/identifier (`UserPasswordDto`, `GetDefaultColor`, `ServiceCollectionExtensions`, `UserAlreadySubscribedException`, `SubscribeCanvasRequestDto`, `PermanentDbTest`) and updated all references; no duplicate `CanvasPasswordDto` existed. |
| P‑MAIN‑02 | 🟡 | MAIN | Dead single‑pixel write helpers in `PixelRepository`. | ✅ Fixed: deleted `TryChangePixelInternalAsync` + its Normal/Economy/FreeDraw variants and the `PixelAttemptResult` record; the live batch path (incl. `BatchExecutionState`) is untouched. |
| P‑UI‑01 | 🟡 | UI | Coordinate display hidden by CSS while JS populates it. | ✅ Fixed: removed the redundant `display: none` from `.canvas-coordinates`; JS toggles visibility on hover. |
| P‑TEST‑01..05 | 🟡 | TEST | Scratch test with no assert; no API/controller tests; no Testcontainers; per‑test DB drop; namespace drift. | ✅ Fixed: Testcontainers Postgres (03), per-test `TRUNCATE` isolation (04), `WebApplicationFactory` API auth tests (02), removed `Hashing.cs` scratch test (01), normalized namespaces + removed dead files (05). |
| P‑BOT‑03 | ⚪ | BOT | VanGogh/VanGogh2 share a canvas; `BOT_API_URL` default is a dev port. | ✅ Fixed: `VanGogh2Bot` already targets its own `VanGogh2` canvas and `BOT_API_URL` defaults to `:8080`; the stale doc note was corrected. |
| P‑BOT‑05 | ⚪ | BOT | Unused NuGet packages; phantom `<None Remove>`. | ✅ Already resolved: the bots csproj references no NuGet packages and has no phantom `<None Remove>`. |
| P‑DOC‑01 | ⚪ | DOC | Changelog overstates XeroxBot (claims 16 parallel workers). | ✅ Fixed: `Changelog.md` already states XeroxBot uses batch pixel placement; the stale "16 workers" note in `Linteum.Bots.md` was corrected. |
| P‑DOC‑02 | ⚪ | DOC | Root README omits bots/features; `.env.example` stale. | ✅ Already resolved: README lists all bots; `.env.example` is current (`VERSION=0.2.2`, no `NETWORK_NAME`, has `TZ`/`WEEKLY_BACKUP_WEEKDAY`/`BACKUP_INITIAL_DELAY_SECONDS`). |
| P‑MAIN‑03 | 🟡 | MAIN | `MyApiClient` is a ~1170‑line god‑class. | ✅ Fixed: split into focused collaborators under `Linteum.BlazorApp/Api/` — `ApiHttp` (HTTP client), `PixelCacheManager` (the 3 caches), `SessionStore` (session/local-storage), and one repository per resource (Colors/Canvases/Subscriptions/CanvasChat/Pixels/History/Balance/Account). `MyApiClient` is now a thin forwarding facade (~130 lines, no logic); all 12 consumers are unchanged. |

---

## Security (SEC)

### P‑SEC‑01 — Many REST endpoints have no authentication · 🔴 Critical
Numerous endpoints trust path/body `userId`/`canvasId` values with **no `Session-Id` check**:
`BalanceChangedEventsController.TryChangeBalance` (`POST change` — anyone can change any user's
balance), balance/login/pixel‑event reads, `UsersController` add/add‑or‑update/delete and all
lookups, `ColorsController`, `CanvasesController.GetAll/search/check-password`,
`SubscriptionsController`, and `GET Pixels/canvases/{canvasId}` (returns the entire canvas).
**Solution:** require a validated session on every endpoint; derive the acting user from the
session (`SessionService.GetUserIdAndUpdateTimeLimit`) instead of trusting a request‑supplied
`userId`; remove truly public reads or gate them behind an explicit `[AllowAnonymous]`.

### P‑SEC‑02 — Password hashes passed in query strings · 🔴 Critical
`UsersController` (add/add‑or‑update/login/changePassword), `CanvasesController` (Add/check‑password)
take the password hash as a `?passwordHashOrKey=` query parameter. Query strings are logged by
nginx, captured in browser history, and may be forwarded by proxies. `changePassword` also
`Uri.EscapeDataString`s the hash before storing (not hashing). **Solution:** move all credential
material into the request body (POST bodies over TLS); never put secrets in URLs; ensure the
hash reaches the DB only through `SecurityHelper` (and ideally replace the hash scheme — P‑SEC‑04).

### P‑SEC‑03 — Non‑constant‑time master‑password compare; bypasses all limits · 🟠 High
`PixelsController.IsValidMasterPassword` compares the supplied password to `MASTER_PASSWORD`
with `string.Equals(…, StringComparison.Ordinal)` (timing side channel). The master password is
accepted on every batch endpoint and bypasses Economy budgets and Normal‑mode quotas. The
credential is also shared with the bots (P‑SEC‑11). **Solution:** use a constant‑time compare
(`CryptographicOperations.FixedTimeEquals` over equal‑length bytes); scope the override to a
single admin principal rather than a shared magic string; rotate the value and avoid shipping it
to clients/bots.

### P‑SEC‑04 — Passwords hashed with plain SHA‑256 (no KDF) · 🔴 Critical
`Linteum.Shared.SecurityHelper.HashPassword` = SHA‑256 of `password + salt` (Base64), with a
single global `PASSWORD_SALT` and no work factor. `UserRepository.TryLogin`,
`CanvasRepository.CheckPassword`, and `SubscriptionRepository.Subscribe` compare hashes with
plain `==` (timing leak). SHA‑256 is fast and trivially brute‑forceable. **Solution:** replace
with a real KDF — PBKDF2‑HMAC‑SHA256 (≥ 600k iterations) or Argon2id/bcrypt — with a per‑user
salt; use constant‑time comparison; migrate existing hashes on next login. (`PASSWORD_SALT`
becomes a pepper only.)

### P‑SEC‑05 — Sessions are in‑memory only · 🟠 High
`SessionService` stores sessions in `ConcurrentDictionary`s. Every API restart logs out every
user; sessions cannot be shared across API instances (relevant if the API is ever scaled).
**Solution:** back sessions with a distributed cache (Redis) or persist them with an expiry;
this also unblocks multi‑instance API deployments.

### P‑SEC‑06 — Guest sessions mintable via `?guest=1`; bots hold master password · 🟠 High
Anyone can mint an unlimited supply of guest sessions via the `?guest=1` URL parameter
(`Login.razor`) / `POST /users/login-guest`. Separately, bots receive `BOT_MASTER_PASSWORD =
MASTER_PASSWORD`, letting them bypass quotas/balances on any canvas. **Solution:** rate‑limit
guest creation (per IP) and cap concurrent guests; give bots a least‑privilege service token
scoped to specific canvases instead of the master override.

> **Partially fixed:** the "bots hold the master password" half is resolved — bots now use a
> dedicated `BOT_SERVICE_TOKEN` (paint-only, optionally canvas-scoped) instead of
> `MASTER_PASSWORD` (see P‑SEC‑11). Guest-session minting via `?guest=1` remains unrate-limited.

### P‑SEC‑07 — Dev HttpClientHandler disables TLS validation · 🟡 Medium
`Linteum.BlazorApp/Program.cs` (dev handler) and `Linteum.Bots/BotBase` set
`ServerCertificateCustomValidationCallback = (_,_,_,_) => true`. For Blazor this is DEBUG‑only,
but the bots version is unconditional. **Solution:** gate the Blazor bypass behind `#if DEBUG`
explicitly; for bots, trust the system trust store (or a pinned CA) instead of disabling
validation.

> **Bot half fixed (P‑SEC‑09):** `BotBase` now trusts the system trust store by default; the
> bypass is opt-in via `BOT_INSECURE_SKIP_TLS_VALIDATION` only. The Blazor dev handler is
> untouched.

### P‑SEC‑08 — `Colors` page and several reads are not admin‑gated · 🟡 Medium
`/colors` is a read‑only palette page with no admin gate; `ColorsController.GET` is unauth.
**Solution:** restrict palette management to admins (and make the page admin‑only if it is not
meant to be public); review all "read" endpoints under P‑SEC‑01.

### P‑SEC‑09 — Bots disable TLS certificate validation · 🟠 High
`Linteum.Bots/BotBase` constructed its `HttpClient` with an unconditional `HttpClientHandler {
ServerCertificateCustomValidationCallback = (_,_,_,_) => true }`, disabling TLS certificate
validation for **all** bot→API traffic. Any TLS connection from a bot would accept any
certificate (self‑signed, expired, wrong‑hostname, or a MITM‑injected one), defeating server
identity checks. (The bots normally reach the API over plain HTTP — `http://linteum-api:8080` in
compose, `http://localhost:8080` locally — so the callback was dormant in the default flow, but
it silently removed all protection the moment the API was reached over HTTPS.)

> **Fixed:** `BotBase` now trusts the system trust store by default (a plain `HttpClientHandler()`
> with no custom callback). The validation bypass survives only as an explicit, dev‑only escape
> hatch, gated behind the `BOT_INSECURE_SKIP_TLS_VALIDATION` env var; when set to `1`/`true` it
> prints a warning that TLS validation is disabled. Production leaves it unset and validates
> normally. (See also the bot‑half note on P‑SEC‑07; the Blazor dev handler is untouched.)

### P‑SEC‑10 — Hardcoded, weak bot credentials committed in source · 🟠 High
Each bot baked its account password into source through its base constructor: `MunchBot`
`Scream123!`, `VanGoghBot`/`VanGogh2Bot` `SecurePassword123!`, `XeroxBot` `XeroxCopy123!`,
`CleanerBot` `CleanCanvas123!`. Weak, guessable, present in git history, identical across every
deployment, and shipped in the public image — anyone with repo access already knew the bot
account passwords.

> **Fixed:** all five hardcoded passwords are deleted. `BotBase` reads `BOT_PASSWORD` from the
> environment and fails fast (`InvalidOperationException`) if it is unset, so a misconfigured bot
> cannot start. The bot email/userName remain in code — they are non‑secret service‑account
> identifiers tied to existing canvas ownership, not credentials. Compose passes
> `BOT_PASSWORD=${BOT_PASSWORD}` to the `linteum-bots` service; `.env`/`.env.example` carry the
> value. **Migration:** existing bot accounts were registered with the old per‑bot passwords, so
> they must be reset to the new shared `BOT_PASSWORD` (or deleted so they re‑register) before bots
> can log in.

### P‑SEC‑11 — Bots effectively hold `MASTER_PASSWORD` (quota/balance bypass) · 🔴 Critical
The compose `linteum-bots` service was wired `BOT_MASTER_PASSWORD=${MASTER_PASSWORD}`, and
`BotBase` attached that value to every batch‑paint request
(`PixelBatchChangeRequestDto.MasterPassword` / `PixelBatchDto.MasterPassword`). `MASTER_PASSWORD`
is the server's universal override — `PixelsController.IsValidMasterPassword` treats it as a
constant‑time bypass of Economy budgets, Normal‑mode daily quotas, and (on `delete-batch`) the
FreeDraw‑only restriction. So a process running the bots held the same secret that gates admin
operations across every canvas, and a single leaked bot image/secret exposed the master override.
(Functionally the override is a no‑op on FreeDraw canvases — which is what all bot canvases are —
because the chunk executor ignores `useMasterOverride` on the FreeDraw path; the risk is the
shared secret, not the quota behaviour.)

> **Fixed:** bots no longer receive `MASTER_PASSWORD`. A dedicated `BOT_SERVICE_TOKEN` is read by
> `BotBase` and sent as a new `ServiceToken` field on the two batch‑paint DTOs; `MasterPassword` is
> untouched (the Blazor client still uses it for legitimate admin batch paint/delete).
> `PixelsController.IsValidServiceToken` validates it separately (constant‑time via
> `SecurityHelper.FixedTimeEqualsString`) and **only** on the two paint endpoints — `delete-batch`
> stays master‑password‑only, so a leaked bot token cannot delete pixels or perform admin
> operations and can be rotated independently of `MASTER_PASSWORD`. An optional
> `BOT_ALLOWED_CANVASES` (comma‑separated) further restricts the token to named canvases; unset =
> any canvas (so Xerox/Cleaner ad‑hoc targets and dynamically‑created bot canvases still work).
> Compose passes `BOT_SERVICE_TOKEN` (and `BOT_ALLOWED_CANVASES`) to both the API and the bots;
> `.env`/`.env.example` carry the value, distinct from `MASTER_PASSWORD`.

### P‑SEC‑12 — Secrets stored as plain environment variables · 🟡 Medium
`MASTER_PASSWORD`, `PASSWORD_SALT`, `POSTGRES_PASSWORD`, `GOOGLE_CLIENT_SECRET`,
`BOT_PASSWORD`, `BOT_SERVICE_TOKEN` are injected verbatim and are visible via `docker inspect`.
**Solution:** use Docker secrets / a secrets manager (or at minimum bind‑mounted env files with
restricted permissions); never log them.

---

## Networking (NET)

### P‑NET‑03 — `linteum-api` (8080) exposed publicly · 🔴 Critical
The API is published on `0.0.0.0:8080` and is directly reachable, bypassing nginx/Blazor. With
P‑SEC‑01 this means the whole unauthenticated surface (and balance mutation) is internet‑
reachable. **Solution:** bind the API port to `127.0.0.1` or remove the host publish — the
Blazor replicas already reach it over the `linteum_prod1` Docker network.

### P‑NET‑04 — `linteum-db` (5434) exposed publicly · 🔴 Critical
PostgreSQL is published on `0.0.0.0:5434` with the `.env` credentials. **Solution:** remove the
host port mapping entirely (only the API/bots need DB access, both on the Docker network).

### P‑NET‑02 — No explicit WebSocket proxy hardening · ⚪ Low
The Blazor `location /` block omits `proxy_read_timeout`/`proxy_send_timeout`/`proxy_buffering off`.
It works today via the upgrade handshake, but long‑idle circuits could be dropped. **Solution:**
add `proxy_read_timeout 3600; proxy_buffering off;` to the Blazor server block.

---

## Concurrency & data integrity (CON)

### P‑CON‑01 — Post‑commit side effects can leave partial state · 🟠 High
`SubscriptionRepository.Subscribe` credits the `+1` balance **after** `CommitAsync`; if that
fails, the subscription exists with no credit. `UserRepository.AddOrUpdateUserAsync` subscribes
to default canvases after commit; `Unsubscribe` adjusts balance and deletes with **no
encompassing transaction**. **Solution:** move all dependent writes inside the same transaction
(or use an outbox), and make the balance change part of the subscription transaction.

> **Fixed:** `Subscribe` now credits the `+1` inside its own transaction before commit (via the shared
> `TryChangeBalanceCoreAsync`), under the canvas write-coordinator lock. `Unsubscribe` wraps the balance
> zeroing and the subscription delete in one transaction under the same lock (and reads the authoritative
> latest balance instead of `GetByUserAndCanvasIdAsync().Last()`, which also fixes P‑PERF‑03). In
> `AddOrUpdateUserAsync` / `CreateGuestUserAsync`, the new user's main-canvas subscription and its `+1`
> credit now commit atomically with the user row (secondary canvases remain best-effort, each in its own
> transaction).

### P‑CON‑02 — Unique‑violation returns empty (no retry) · 🟡 Medium
In `ExecuteFreeDrawChunkAsync`, a Postgres `UniqueViolation` on `(CanvasId,X,Y)` is treated as a
conflict and the chunk silently returns empty (no retry). Under concurrent painters this drops
legitimate writes. **Solution:** on conflict, reload the conflicting pixel and retry the update
(a proper "upsert" via `ON CONFLICT … DO UPDATE`).

> **Fixed:** `ExecuteFreeDrawChunkAsync` now retries once on a `UniqueViolation` — it clears the change
> tracker and reloads the now-existing pixel, so the second attempt takes the update path. Only after a
> second conflict does it return empty (with a warning). The conflict can only originate from another API
> process (P‑CON‑04), so one retry covers the typical cross-process race.

### P‑CON‑03 — Write‑coordinator semaphore dictionary never evicts · 🟡 Medium
`CanvasWriteCoordinator._locks` is a `ConcurrentDictionary<Guid, SemaphoreSlim>` that grows
forever (one entry per canvas ever written). **Solution:** use a bounded/idle‑evicting cache or
striped locks; periodically remove entries for deleted canvases.

> **Fixed:** `CanvasWriteCoordinator` now uses a fixed pool of striped `SemaphoreSlim(1,1)`s (64 stripes,
> `canvasId` hashed to a stripe) instead of a per-canvas `ConcurrentDictionary`. Memory is bounded, there
> is no eviction race, and writes to a given canvas still serialize (same canvas → same stripe).

### P‑CON‑04 — Write coordination is in‑process only · 🟠 High
The per‑canvas semaphore serializes writes only within one API process. With more than one API
instance (or the current 3 Blazor replicas issuing writes through one API), the economy ledger
and pixel writes can race. **Solution:** use a DB‑level lock (e.g. `SELECT … FOR UPDATE` on the
canvas row / advisory lock) or a distributed lock for cross‑process serialization.

### P‑CON‑05 — Balance writes bypass the negative‑balance guard · 🟠 High
`PixelRepository.ExecuteEconomyChunkAsync` appends `PixelPayment` `BalanceChangedEvent` rows
directly instead of going through `BalanceChangedEventRepository.TryChangeBalanceAsync`, which
is the only place that rejects negatives. Two concurrent accepted bids could overdraw.
**Solution:** route all balance mutations through `TryChangeBalanceAsync` (ideally under a row
lock) so the guard always applies.

> **Fixed:** the guarded append was extracted into `TryChangeBalanceCoreAsync` (no lock, no own
> transaction — caller supplies both), and `ExecuteEconomyChunkAsync` now routes the chunk's total payment
> through it as a single `PixelPayment` ledger event per chunk. The negative-balance guard therefore
> applies on the economy path too. (Per-pixel balance-event granularity is removed; final balance is
> unchanged and nothing reads per-pixel events for correctness.) Calling the public
> `TryChangeBalanceAsync` directly from the economy path would re-enter the non-reentrant canvas lock, so
> the lockless core is used instead — true cross-process safety still needs P‑CON‑04.

### P‑CON‑06 — Static mutable `DefaultColorId` can serve stale data · 🟡 Medium
`PixelRepository.DefaultColorId` is a static cached at first use; if the white color is ever
deleted/re‑seeded with a new Id, the cache is stale. **Solution:** invalidate the cache when the
palette changes, or resolve the default color per‑request from the (cached) palette.

> **Fixed:** the static `DefaultColorId` field was removed; `GetDefaultColorIdAsync` resolves the default
> color from `_colorRepository.GetDefautColor()` on each call (once per chunk), so it is always correct
> after a re-seed.

---

## Data model (DATA)

### P‑DATA‑01 — Migration placement mismatch · 🟡 Medium
Migrations live in `Linteum.Api/Migrations` (`MigrationsAssembly = "Linteum.Api"`), but
`Linteum.Infrastructure.csproj` declares an (empty) `Migrations\` folder. Confusing for new
contributors. **Solution:** remove the empty folder from Infrastructure; document that the API
is the migration host (or move the DbContext + migrations together into Infrastructure).

> **Fixed:** removed the `<Folder Include="Migrations\"/>` placeholder from `Linteum.Infrastructure.csproj`. The API stays the migration host (`MigrationsAssembly("Linteum.Api")`); the DbContext lives in Infrastructure, migrations in `Linteum.Api/Migrations`.

### P‑DATA‑03 — No unique index on `Color.HexValue` · 🟡 Medium
Duplicate colors can be inserted. **Solution:** add a unique index on `HexValue` (a migration).

> **Fixed:** `AppDbContext` now declares `entity.HasIndex(c => c.HexValue).IsUnique()`, and migration `20260620194538_AddColorUniqueHexAndFks` adds `IX_Colors_HexValue` after first deduping any existing duplicate `HexValue` rows (keeping the lowest `Id`), so it applies cleanly on live data.

### P‑DATA‑04 — No FK from `Pixel.ColorId` to `Color` · 🟡 Medium
`Pixel.ColorId` and `PixelChangedEvent.Old/NewColorId` are unreferenced `int` columns with no FK
configured; orphaned/invalid color references are possible (and P‑CON‑06 relies on a stable
default color). **Solution:** add the FK relationships (with the default‑color reassignment the
seeder already does) or enforce validity in the repository.

> **Fixed:** `Pixel` and `PixelChangedEvent` gained `Color`/`OldColor`/`NewColor` navigations with FKs to `Colors.Id` (`DeleteBehavior.Restrict`). The migration reassigns any orphaned `ColorId`/`OldColorId`/`NewColorId` to the default color (`#FFFFFF`) first (mirroring the seeder's removal logic), then adds the constraints as `NOT VALID` + `VALIDATE` so the large `Pixels`/`PixelChangedEvents` tables are not write-locked during the deploy. Verified against a throwaway Postgres seeded with duplicate-color and orphan-reference data: dedupe, reassignment, and all three FKs (validated) applied without error.

### P‑DATA‑05 — `Canvas.Pixels` collection uninitialized · 🟡 Medium
Unlike other collections, `Canvas.Pixels` has no `= new List<Pixel>()` initializer; accessing it
before lazy load throws NRE. **Solution:** initialize it (and audit cascade behavior for audit
tables — deleting a user currently cascades their `PixelChangedEvent`/`BalanceChangedEvent`
history).

> **Fixed:** `Canvas.Pixels` is now initialized `= new List<Pixel>()` (matching `Subscriptions`). The cascade-behavior audit is out of scope here.

---

## Performance (PERF)

### P‑PERF‑01 — Whole‑canvas endpoints can OOM · 🟠 High
`GET Pixels/canvases/{canvasId}` and `GET Canvases/image/{name}` materialize every pixel (and,
for export, every color + a full `Image<Rgba32>`) into memory; the controller comments
explicitly warn about OOM. **Solution:** paginate/stream pixels; for exports, render in tiles or
stream from a server‑side cache; cap response size and reject oversized requests.

> **Fixed:** `CanvasesController.GetImage` now streams pixels straight into the raster via
> `PixelRepository.StreamPixelsForCanvasAsync` (an `IAsyncEnumerable<PixelDto>` projecting only X/Y/ColorId),
> so the full `List<PixelDto>` is never buffered. The `Pixels/canvases/{id}` endpoint stays
> `[DisabledEndpoint]`; `GetByCanvasIdAsync` remains for tests/seeding. The PNG raster buffer itself is
> inherent to image export.

### P‑PERF‑02 — History cleanup materializes all events · 🟡 Medium
`CleanPixelHistoryBatchAsync` loads all events for the given pixel IDs into memory to compute
which to keep. **Solution:** do this server‑side (window function / `ROW_NUMBER()`) so only the
rows to delete are touched.

> **Fixed:** `CleanPixelHistoryBatchAsync` is now a single `ExecuteSqlInterpolatedAsync` delete that prunes
> `rn > maxHistoryEntries` per pixel via `ROW_NUMBER() OVER (PARTITION BY "PixelId" ORDER BY "ChangedAt" DESC,
> "Id" DESC)`. Nothing is loaded into memory; the existing `PixelId` index serves the partition scan.

### P‑PERF‑03 — `Unsubscribe` loads 500 balance rows to read one · ⚪ Low
It reads the whole balance history just to get the last `NewBalance`. **Solution:** use a
"latest row" query (`ORDER BY ChangedAt DESC, Id DESC LIMIT 1`) like the balance repository does.

> **Fixed:** `Unsubscribe` reads only the single latest row —
> `OrderByDescending(ChangedAt).ThenByDescending(Id).Select(e => (long?)e.NewBalance).FirstOrDefaultAsync()`.

### P‑PERF‑04 — Seeder subscribes all users one‑by‑one · 🟡 Medium
On startup, for secondary canvases with zero subscriptions it issues N `Subscribe` calls (each
its own transaction + balance write). On a large user base this is slow. **Solution:** bulk
insert subscriptions and a batched balance credit.

> **Fixed:** new `SubscriptionRepository.SubscribeAllAsync(canvasId, userIds)` skips already‑subscribed users,
> resolves each candidate's latest balance once, and inserts all subscriptions + `+1` balance credits in a
> single transaction under the canvas write‑coordinator lock. The seeder calls it instead of the per‑user loop.

### P‑PERF‑06 — `MyApiClient` caches grow unbounded · 🟡 Medium
Pixel/history caches never evict expired entries proactively. **Solution:** use a bounded cache
or a periodic sweep; consider `MemoryCache` with eviction.

> **Fixed:** `_pixelCache`/`_historyCache` are now `BoundedLruCache` (capacity‑bounded LRU with a uniform TTL).
> Expired entries evict lazily on access; the coldest entry evicts when capacity is exceeded. All existing cache
> methods keep their behavior — the client keeps a local canvas pixel cache, now size‑managed (8192 pixel /
> 1024 history entries) and kept effective via LRU.

### P‑PERF‑07 — Render batches sent as JSON · ⚪ Low
`canvasRenderer.renderBatch` receives JSON arrays; typed arrays (`Uint32Array`/`Int32Array`)
would cut serialization cost on the hot pixel path. **Solution:** pass binary‑typed buffers via
JS interop.

> **Fixed:** `CanvasRenderer.RenderLoop` now builds parallel typed arrays (`int[] xs/ys/rgbs`, `byte[] flags`)
> and calls `canvasRenderer.renderBatchTyped`; the object‑form `renderBatch` is kept as a fallback and is still
> used by the JS‑internal callers in `canvas-viewport.js`.

---

## Realtime (RT)

### P‑RT‑01 — No SignalR backplane (per‑replica state) · 🟠 High
Online‑user tracking (`ConnectionTracker`) and the Blazor client caches are per‑process. With 3
Blazor replicas, a user on replica B won't see users/caches on replica A, and the API broadcasts
to whichever replica group members are on. **Solution:** add the Redis SignalR backplane
(`AddStackExchangeRedis`) so groups/fan‑out are cluster‑wide; share `ConnectionTracker` via Redis.

### P‑RT‑02 — Reconnect rejoin may use a stale group · 🟠 High
`CanvasPage.SignalR.cs` rejoin logic uses `_connectedGroup`, which can be stale if the user
switched canvases during a disconnect — events for the current canvas are lost, with no replay
and no error handling. **Solution:** rejoin based on the **current** canvas name on reconnect,
verify group membership, and reconcile missed state (e.g. reload the canvas on reconnect).

> **Fixed:** the `Reconnected` handler now rejoins the *current* canvas (`_canvas?.Name ?? canvasName`)
> via `EnsureJoinedCurrentCanvasAsync`, not the stale `_connectedGroup`. Reconnect then reconciles
> the disconnect gap via the server-side expirable buffer (P‑RT‑05): the client fetches events from
> its last-applied sequence to the new high-water sequence returned by `JoinCanvasGroup` and replays
> them. If the API process restarted (sequence went backwards) or the buffer has evicted the range
> (long disconnect), it falls back to a canvas-image reload. See P‑RT‑05 for the buffer/reconcile
> mechanics.

### P‑RT‑03 — No "reconnecting…" UX · 🟡 Medium
When the hub connection drops or fails to start, the UI keeps accepting input with no feedback.
**Solution:** surface connection state (`Reconnecting`/`Reconnected`/`Closed`) in the UI.

> **Fixed:** `EnsureInteractiveReadyAsync` registers `Reconnecting`/`Reconnected`/`Closed` handlers
> that drive a `_connectionStatus` field rendered as a top-of-canvas banner ("Reconnecting…" while
> the automatic reconnect policy retries, "Realtime connection lost" + a Retry button once it gives
> up / on a failed initial start). The Retry button calls `RetryInitializationAsync`, which rebuilds
> the hub connection and reloads the canvas.

### P‑RT‑04 — 20 s init timeout forces a full reload · 🟡 Medium
If init exceeds 20 s, `CanvasPage` force‑reloads the page, losing pending strokes without
warning. **Solution:** show a countdown and a manual retry; preserve pending local state.

> **Fixed:** `WatchCanvasInitializationTimeoutAsync` no longer calls
> `NavigateTo(…, forceLoad: true)`. It ticks a 1-second countdown shown in the loading overlay, and
> on expiry sets `_initTimedOut` to reveal a "Taking longer than expected" panel with a Retry
> button. Pending brush strokes are preserved (no page reload). `RetryInitializationAsync` rebuilds
> the SignalR connection and re-runs the canvas load.

### P‑RT‑05 — Canvas snapshot can precede its live subscription (lost changes) · 🟠 High
A canvas is loaded from a server-rendered PNG snapshot, then the client subscribes to the SignalR
group for live updates. Any pixel change that lands between the snapshot being rendered and the
subscription becoming active is in neither the snapshot nor the live stream, so the client silently
shows a stale pixel until that coordinate is touched again. The same applies after a transient
disconnect. **Solution:** capture the gap window precisely and backfill it as a non-blocking
post-job, without delaying the main load.

> **Fixed:** every broadcast leaving `SignalRPixelNotifier` is mirrored into `ICanvasEventBuffer`
> (singleton, in-memory, per-canvas, 45 s / 500-entry TTL) as a normalized `CanvasChangeEntryDto`
> carrying a per-canvas monotonic sequence. After the snapshot is on screen, a fire-and-forget
> post-job (`TryReconcileRecentCanvasAsync`) fetches the **newest** buffered entries
> (`GetRecentCanvasChanges`) and replays them through the existing `HandlePixelUpdatedAsync` /
> `HandlePixelsDeletedAsync`. Because the snapshot was rendered only moments earlier, the newest
> entries always cover the gap (plus any live update the freshly painted image clobbered); no
> sequence anchor is needed, so nothing goes stale and there is no reload loop. On reconnect,
> `JoinCanvasGroup` returns the buffer's high-water sequence **after** establishing group membership
> and `TryReconcileCanvasAsync` fetches the precise range `(_lastReconciledSeq, joinSeq]` via
> `GetCanvasChanges`; a detected gap or truncation (long disconnect / API restart) falls back to a
> full image reload. Pixel writes are last-writer-wins per coordinate, so re-applying an event the
> client already has is idempotent. The reconcile is fire-and-forget and self-guarded, so it never
> delays the visible canvas load. The buffer is per-process (single-API deployment); horizontal API
> scaling would require backing it with Redis, the same limitation as P‑RT‑01.

---

## Operations & deployment (OPS)

### P‑OPS‑01 — No repo/`.env` on the VPS · 🟠 High
Deployment happens from the dev machine via a remote Docker context; the VPS has no Linteum
`docker-compose.yml`/`.env`. Recovery, secret rotation, and disaster recovery all depend on the
dev machine. **Solution:** keep a deploy checkout (+ templated `.env`, secrets via a vault) on
the VPS, or move to CI/CD that builds and deploys from Git.

### P‑OPS‑04 — `linteum-db` at 93 % of its 512 M limit · 🔴 Critical
At inspection, `linteum-db` used 478 MiB of its 512 MiB container limit (Postgres data volume
5.7 GB). It is at risk of OOM‑kill under load (which would abort in‑flight transactions).
**Solution:** raise the DB memory limit (e.g. to 1 GiB) and align `shared_buffers`/`max_connections`
with the new budget; add a memory alert.

> **Fixed:** the `linteum-db` container limit is raised 512 M → 1 G, with `effective_cache_size`
> aligned 512 → 768 MB (`shared_buffers` stays 256 MB ≈ 25 % of the new budget; `max_connections=100`
> and `work_mem=4 MB` are unchanged — 100 × 4 MB worst case + 256 MB shared fits comfortably in 1 G).
> Takes effect on the next `docker compose up -d --build`. A memory alert still needs wiring into the
> monitoring stack (the new limit buys headroom, not a warning).

### P‑OPS‑02 — `nlog.config` hardcodes a Windows internal‑log path · 🟡 Medium
The internal log path is `c:\temp\internal-nlog-AspNetCore.txt`, which does not exist in the
Linux container. **Solution:** use a Linux path (or `${tempdir}`) or disable the internal log.

> **Fixed:** `Linteum.Api/nlog.config` now writes the internal log to
> `${tempdir}/internal-nlog-AspNetCore.txt`, which resolves to `/tmp` in the Linux container (and the
> OS temp dir on Windows) instead of the hardcoded `c:\temp` path that never existed there.

### P‑OPS‑03 — Seeder blocks 10 s on color cleanup · 🟡 Medium
`CleanupColorsRemovedFromConfigAsync` sleeps 10 s whenever cleanup is needed, delaying startup.
**Solution:** remove the artificial delay or make it conditional on actual work.

> **Fixed:** the unconditional `await Task.Delay(TimeSpan.FromSeconds(10))` was removed from
> `CleanupColorsRemovedFromConfigAsync`; color cleanup (reassign references to the default color, then
> delete) runs as soon as candidates are identified, shaving 10 s off startup whenever a config color
> was removed. The transaction wrapping the reassign + delete is unchanged.

### P‑OPS‑05 — `bots` profile restart‑loops with no command · 🟡 Medium
`linteum-bots` has `restart: unless-stopped` but no default `CMD`, so enabling the profile via
`up` (rather than `run --rm`) starts a usage‑printing container that exits and is restarted
indefinitely. **Solution:** set a no‑op/wait default command or change the restart policy for
the bots service.

> **Fixed:** `linteum-bots` now declares `restart: "on-failure"`. With no default command the
> entrypoint prints usage and exits 0, and `on-failure` does not restart a clean exit — so enabling
> the profile with `up` no longer loops. A bot that crashes (non-zero exit) still restarts. Bots remain
> one-shot by design: `docker compose --profile bots run --rm linteum-bots <bot>`.

### P‑OPS‑06 — ~50 GB of reclaimable Docker build cache · 🟡 Medium
`docker system df` reported 50.48 GB of build cache (50 GB reclaimable). **Solution:** schedule
`docker builder prune` (or set `max-size` on the builder) to reclaim disk.

> **Partially fixed:** added `scripts/maintenance/prune-docker-build-cache.sh`, which runs
> `docker builder prune --all --filter until=KEEP_HOURS` (default 168 h) — keeping the last week of
> cache for fast incremental rebuilds while reclaiming older layers — with a weekly cron example in
> `Documentation/Deployment.md`. Running/scheduling it is operational: there is no repo checkout on the
> VPS (P‑OPS‑01), so the script must be placed and cron'd on the host.

### P‑OPS‑07 — Backups have no off‑host copy or failure alerting · 🟡 Medium
The daily/weekly `pg_dump` lives only in the `db_backups` volume on the same host; `pg_dump.log`
is append‑only and unchecked. **Solution:** ship backups off‑host (object storage) and alert on
backup failure/staleness.

> **Partially fixed:** the backup loop now writes `/backups/.last-success` and `/backups/.last-failure`
> markers, and the `linteum-db-backup` container gained a `healthcheck` that reports **unhealthy**
> when no successful dump has landed within 25 h (24 h cycle + slack) — wiring that healthcheck into a
> monitor (`docker ps` / a healthcheck watcher / Uptime-Kuma) closes the "no failure alerting" gap.
> The off-host-copy half is documented as an operational follow-up (a host-cron rclone step to object
> storage) because it needs an external destination and credentials not in the repo.

---

## Observability (OBS)

### P‑OBS‑01 — Monitoring repo is not deployable as‑committed · 🟠 High
In `ash-twin-monitoring`, `.gitignore` excludes `scripts/` (so `setup-ilm.sh` is absent from
Git) and `.env.example` is empty (3‑byte BOM). A fresh clone cannot reproduce the deployment,
and the live stack has drifted (data streams, ~2‑month retention, 512 m heap, 1.5 GiB limit —
none of which match the repo). **Solution:** commit `scripts/` and a real `.env.example`;
reconcile the repo with the live configuration; version the ILM/retention policy.

### P‑OBS‑05 — No multiline/JSON parsing for NLog output · 🟠 High
Filebeat has no `multiline` config and no `decode_json_fields`. .NET/NLog stack traces are split
across unrelated documents and any JSON log fields are not queryable. **Solution:** add multiline
handling (hint‑based via a container label or `multiline.type: pattern`) and decode NLog JSON
into fields.

### P‑OBS‑03 — Single‑node ES, no replicas, no snapshots · 🟡 Medium
`discovery.type=single-node`, `number_of_replicas: 0`, no snapshot/SLM policy. Volume loss =
total log loss. **Solution:** add a snapshot repository + SLM policy; consider a replica node.

### P‑OBS‑04 — Kibana login is `superuser` · 🟡 Medium
`kibana-user-setup` creates the operator login with role `superuser`. **Solution:** use a
least‑privilege role (`kibana_admin` + read on `docker-logs-*`).

### P‑OBS‑06 — Filebeat runs as root with the Docker socket · 🟡 Medium
`user: root` + `/var/run/docker.sock:ro` is effectively full host control. **Solution:** prefer
rootless Docker / `socket-proxy` with a read‑only subset, or accept and document the risk.

### P‑OBS‑07 — Elasticsearch at 93 % of its 1.5 GiB limit · 🟡 Medium
Headroom is thin; a busy log day could OOM‑kill ES and interrupt log search. **Solution:** raise
the limit / heap and add a disk+memory alert; consider lowering retention or adding a node.

> Also note: `vm.max_map_count` is required by ES 8 in production but is not set anywhere in the
> repo — confirm it is configured on the host (`sysctl vm.max_map_count=262144`).

---

## Configuration (CFG)

### P‑CFG‑01 — `Config` tunables not bound from environment · 🟡 Medium
`Config` is registered as a default‑constructed singleton; quotas (100/10), guest lifetime (24 h),
session timeout (60 min), default canvases, and the palette cannot be changed without recompiling,
even though `MASTER_PASSWORD`/`GOOGLE_*` are read from env ad hoc. **Solution:** bind `Config`
from `appsettings`/environment (`IOptions<Config>`), so operators can tune limits per deployment.

---

## Maintainability (MAIN)

### P‑MAIN‑01 — Filename/identifier typos · ⚪ Low
`CanvasPaswordDto.cs`, `UserPaswordDto.cs`, `UserAlreadySubsribedException.cs`,
`SubscribeCanvasRequestDTO.cs` (DTO casing); `ServiceCollectionExtenstions.cs`; `PermamentDbTest`;
`GetDefautColor()`. Also a **duplicate** `CanvasPasswordDto` exists in both `CanvasPasswordDto`
and `CanvasPaswordDto` files. **Solution:** rename + remove the duplicate; keep one canonical
type.

### P‑MAIN‑02 — Dead single‑pixel write helpers · 🟡 Medium
`PixelRepository.TryChangePixelInternalAsync` and its `*Normal*`/`*Economy*`/`*FreeDraw*`
variants are not reached from the live batch path and re‑enter `TryChangePixelsBatchAsync`.
**Solution:** delete the dead code (or wire it up intentionally) to prevent accidental misuse.

### P‑MAIN‑03 — `MyApiClient` is a ~1170‑line god‑class · 🟡 Medium
It mixes API access, caching, and session storage. **Solution:** split into an HTTP client, a
cache manager, and a session store; extract one repository per resource.

> **Fixed:** the god‑class is decomposed into focused, single‑responsibility collaborators under a new
> `Linteum.BlazorApp/Api/` namespace, all `internal sealed` and scoped (one instance per circuit, so the
> caches stay per‑circuit exactly as before):
> - **`ApiHttp`** — the HTTP client: owns the server‑side `HttpClient` + `LocalStorageService` and builds
>   requests with the `Session-Id` header attached (`CreateAsync`). This is the "HTTP client" of the split.
> - **`PixelCacheManager`** — the cache manager: owns the three caches (pixel / history / color, single
>   lock, 1‑minute TTL, bounded LRU from P‑PERF‑06) and all invalidation/write‑through hooks
>   (`InvalidatePixelCache`/`InvalidateHistoryCache`/`ClearCanvasCache`/`ClearAllCaches`,
>   `HandlePixelColorChanged`/`HandlePixelDeleted`/`ApplyBatchPaintCache`/`StorePaintedPixel`, plus the
>   white‑color and `IsPixelKnownWhite` queries).
> - **`SessionStore`** — the session store: owns `SetSessionAsync`/`ClearSession`/`GetCurrentUserIdAsync`/
>   `GetCurrentLoginMethodAsync`/`IsGuestUserAsync`/`PersistAuthenticatedUserAsync` and clears the caches
>   on login/logout.
> - **Eight resource repositories** — `ColorsRepository`, `CanvasesRepository`, `SubscriptionsRepository`,
>   `CanvasChatRepository`, `PixelsRepository`, `HistoryRepository`, `BalanceRepository`,
>   `AccountRepository`; each method was moved verbatim (same URLs, bodies, status‑code → exception
>   mapping, cache side‑effects), with caches read/written through `PixelCacheManager`. Shared error
>   parsing moved to `ApiErrors`.
>
> `MyApiClient` is now a thin forwarding **facade** (the 12 consuming components still `@inject
> MyApiClient` and call the same methods — zero consumer churn). It holds no state and no logic, so the
> three concerns are no longer mixed. The `Program.cs` DI container registers the collaborators; the
> unused `@inject MyApiClient` in `App.razor` (empty `@code`) was removed.

---

## UI / UX (UI)

### P‑UI‑01 — Coordinate display hidden by CSS · 🟡 Medium
`CanvasPage.razor.css` sets `.canvas-coordinates { display: none }` while JS populates it, so the
feature is invisible. **Solution:** remove the rule or gate the element behind a setting.

---

## Testing (TEST)

### P‑TEST‑01 — `Hashing.cs` is a scratch test with no assertion · 🟡 Medium
It just prints a hash; missing namespace; no `Tests` suffix. **Solution:** assert expected
deterministic output or delete it.

### P‑TEST‑02 — No API/controller tests · 🟡 Medium
The whole HTTP surface (auth, quotas, batch results, mode gates) is untested at the integration
level. **Solution:** add `WebApplicationFactory`‑based API tests covering auth, modes, and batch
semantics.

### P‑TEST‑03 — DB tests need a pre‑provisioned Postgres · 🟡 Medium
Hardcoded `Host=localhost;5432;…;postgres/password`; no Testcontainers; not CI‑portable.
**Solution:** use Testcontainers for Postgres (per‑test ephemeral DB).

### P‑TEST‑04 — DB dropped/recreated per test · 🟡 Medium
`EnsureDeleted`+`EnsureCreated` per test is slow (the code even has a `//ToDo: Clear instead of
delete.`). **Solution:** truncate/clear tables between tests, or share the schema and wrap each
test in a transaction.

### P‑TEST‑05 — Namespace drift / typos in tests · 🟡 Medium
`DatabaseTests.cs` is `namespace Linteum.Tests` (not `.Db`); `SubscriptionRepositoryReadTest.cs`
is under `Read/` but declares `namespace Linteum.Tests.Db.Delete`; class/file name mismatches;
empty `SyntheticDataTestPopulated.cs`. **Solution:** normalize namespaces and remove dead files.

---

## Bots (BOT)

### P‑BOT‑03 — VanGogh bots share a canvas; dev‑port default · ⚪ Low
`VanGoghBot` and `VanGogh2Bot` both target the `VanGogh` canvas; running both makes them fight.
`BOT_API_URL` defaults to `http://localhost:5182` (a dev port). **Solution:** give them distinct
canvases (or document the conflict); update the default URL.

### P‑BOT‑05 — Unused NuGet packages; phantom file ref · ⚪ Low
`Linteum.Bots.csproj` references `Microsoft.Extensions.Configuration(.Binder)` and
`NetEscapades.Configuration.Yaml` but builds no config; `<None Remove="sigame.jpg">` references a
non‑existent file. **Solution:** drop the unused packages and the stray remove.

---

## Documentation (DOC)

### P‑DOC‑01 — Changelog overstates XeroxBot · ⚪ Low
`Changelog.md` claims `XeroxBot` uses "16 workers, channel‑based queue"; the code is
single‑threaded. **Solution:** correct the changelog (or implement the parallel version).

### P‑DOC‑02 — Root README/`.env.example` stale · ⚪ Low
The root `README.md` omits `MunchBot`/`VanGoghBot`/`VanGogh2Bot` and several features;
`.env.example` has `VERSION=0.1.3` (current is 0.2.2), an unused `NETWORK_NAME`, and is missing
`TZ`/`WEEKLY_BACKUP_WEEKDAY`/`BACKUP_INITIAL_DELAY_SECONDS`. **Solution:** refresh the README
(this `Documentation/` folder now covers the gaps) and align `.env.example`.

---

## Suggested fix order

If tackling these, a sensible order is:

1. **Lock down the network** (P‑NET‑03, P‑NET‑04) — smallest change, biggest risk reduction.
2. **Authentication** (P‑SEC‑01) — require sessions on all mutating/read endpoints.
3. **Credentials** (P‑SEC‑02, P‑SEC‑04) — bodies not URLs; real password KDF.
4. **Memory safety** (P‑OPS‑04) — raise the DB limit before it OOM‑kills.
5. **Integrity** (P‑CON‑01, P‑CON‑05) — transactions and the balance guard.
6. **Realtime correctness** (P‑RT‑01/02) — backplane + correct reconnect.
7. Everything else, roughly by severity.

Each item above references the detailed project documents for the exact code locations.
