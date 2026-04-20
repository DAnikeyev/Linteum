# 0.1.2 - 2026/04/20

- Migrated the app to WebAssembly — pages now load and run client-side where supported, falling back to server rendering automatically.
- Fixed a bug where navigating between canvases could show a leftover image from the previous one.
- Sidebar collapse state is now saved and restored correctly after a page reload.
- Sidebar auto-collapses on mobile when you open a canvas.
- Fixed pixel lookup requests that were broken in WASM due to a GET-with-body limitation in the browser.
- Various rendering and interactivity fixes that were exposed by the move to WASM.

# 0.1.1 - 2026/04/07

- Added Google sign-in alongside the existing username/password login.
- Canvas passwords — you can now protect a canvas with a password when creating it.
- Added canvas search by name.
- You can now mark a canvas as private or public.
- Canvas image export — download a snapshot of any canvas as an image.
- Real-time online user count shown per canvas.
- Pixel history — click any pixel to see who changed it and when.
- Sidebar is now collapsible and remembers its state.
- Responsive layout — the app now works on mobile and small screens.
- Refreshed design for Login, Signup, Settings, and canvas-related pages.
- Canvas panning and zooming performance improved significantly.
- Introduced bots: `CleanerBot`, `MunchBot`, `VanGogh`, and `XeroxBot` (reproduces images onto a canvas using 16 parallel workers).
- Background cleanup jobs keep the database tidy (expired sessions, old pixel history, stale subscriptions).

# 0.1.0 - 2026/04/01

- Sessions are validated on startup and refreshed automatically so you don't get unexpectedly logged out.
- Online users are broadcast in real time when people join or leave a canvas.
- Pixel history is refreshed live when someone else changes a pixel you're inspecting.
- Canvas resizes correctly when the browser window changes size.
- Client-side caching for pixel data and history to reduce unnecessary API calls.
- Upgraded the whole stack from .NET 9 to .NET 10.
- General styling pass across all major pages.

# 0.0.1 - 2026/02/19

- Initial pre-release.
