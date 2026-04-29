# Linteum

Linteum is a collaborative pixel canvas project built with Blazor, ASP.NET Core, SignalR, and PostgreSQL. You can open a canvas, place pixels, watch updates show up live, and even let bots join in.

Live site: [linteum.ash-twin.com](https://linteum.ash-twin.com)

![Linteum UI Example](resources/LimteumUiExample.png)

---

## Features

- Live pixel updates across connected clients through SignalR.
- Multiple canvases, including public, private, and password-protected ones.
- Three canvas modes: `Normal`, `FreeDraw`, and `Economy`.
- Smoother freehand drawing with interpolated strokes.
- Canvas creation from either a blank board or a JPG starting image.
- Batch drawing support, plus bulk delete and queued text drawing on `FreeDraw` canvases.
- Lobby chat so people can talk before jumping into a canvas.
- Local accounts plus optional Google login.
- Canvas image export.
- Bot support for cleaning, painting, and image-based drawing.

## Canvas modes

- `Normal` - the standard shared canvas mode. It keeps a daily pixel quota per user for each canvas (100 by default).
- `FreeDraw` - meant for faster editing. It supports batch pixel updates, batch delete, and queued text drawing.
- `Economy` - pixels have prices, balances matter, and subscribed users can earn hourly income based on what they own on that canvas.

By default, the app seeds a small starter set of canvases: `home`, `home_FreeDraw`, and `home_Economy`.

---

## Architecture and tech stack

Linteum is split into a few focused projects:

- **Frontend:** [Blazor](https://dotnet.microsoft.com/en-us/apps/aspnet/web-apps/blazor) in interactive server mode.
- **Backend:** [ASP.NET Core Web API](https://dotnet.microsoft.com/en-us/apps/aspnet/apis).
- **Realtime updates:** [SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr).
- **Database:** [PostgreSQL](https://www.postgresql.org/) with [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/).
- **Containers:** [Docker](https://www.docker.com/) and Docker Compose.

### Project breakdown

- `Linteum.Api` - API endpoints, realtime hubs, and background services.
- `Linteum.BlazorApp` - the main web UI.
- `Linteum.Bots` - automated clients for cleanup, art bots, and image drawing.
- `Linteum.Domain` - entities and repository contracts.
- `Linteum.Infrastructure` - EF Core, repositories, and data access logic.
- `Linteum.Shared` - shared DTOs, enums, config, and helper utilities.

---

## Getting started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) if you want to run the projects locally without Docker
- PowerShell if you want to use the helper script

### Running with Docker

The simplest way to start the main services is:

```powershell
docker compose up -d --build
```

There is also a small helper script in the repo:

```powershell
.\build-and-up.ps1
```

If you want the bots too, start Compose with the `bots` profile:

```powershell
docker compose --profile bots up -d --build
```

### Environment variables

Set up your `.env` file or environment variables as described in `docker-compose.yml`. The main ones are:

- `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
- `MASTER_PASSWORD`, `MASTER_USER`, `MASTER_EMAIL`
- `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` for Google login

---

## Bots

The repo includes a few bot clients:

- `CleanerBot` for clearing or tidying up a canvas.
- `XeroxBot` for drawing an image onto a canvas.

For bot commands and Docker examples, see `DockerCheatsheet.md`.

---

## Contributing

This project is still evolving, so issues and pull requests are welcome. If you want to try something, fix a rough edge, or add a feature, feel free to jump in.
