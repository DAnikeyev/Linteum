# Docker Cheatsheet — Linteum

## Services

| Service          | Container            | Host Port | Container Port |
|------------------|----------------------|-----------|----------------|
| PostgreSQL 16    | `linteum-db`         | 5434      | 5432           |
| API (.NET)       | `linteum-api`        | 8080      | 8080           |
| Blazor App       | `linteum-blazor`     | 5000      | 8090           |
| Bots (profile)   | `linteum-bots`       | —         | —              |

> **Bots** are behind the `bots` profile and won't start unless explicitly included.

---

## Compose — Start / Stop

```bash
# Start core services (db, api, blazor)
docker compose up -d

# Start core services + bots
docker compose --profile bots up -d

# Stop all running services
docker compose down

# Stop all services including bots profile
docker compose --profile bots down

# Restart a single service
docker compose restart linteum-api

# Run the Cleaner bot on canvas "Default"
docker compose --profile bots run --rm linteum-bots cleaner Default

# Run the VanGogh bot
docker compose --profile bots run --rm linteum-bots vangogh

# Run Munch
docker compose --profile bots run --rm linteum-bots munch

docker compose --profile bots run --rm linteum-bots xerox canvas-name image-file

# Reuse the already-built bots image without rebuild
docker run --rm --network linteum_prod1 -e BOT_API_URL=http://linteum-api:8080 -e BOT_TIMEOUT_MINUTES=10 linteum-linteum-bots xerox canvas-name image-file
```

## Compose — Build

```bash
# Build all images
docker compose build

# Build without cache (clean rebuild)
docker compose build --no-cache

# Build + start in one step
docker compose up -d --build

# Build + start including bots
docker compose --profile bots up -d --build

# Rebuild a single service
docker compose build linteum-api
```

## Logs

```bash
# Follow logs for all services
docker compose logs -f

# Follow logs for a specific service
docker compose logs -f linteum-api

# Last 100 lines of a service
docker compose logs --tail 100 linteum-blazor
```

## Exec / Shell

```bash
# Open a shell in a running container
docker exec -it linteum-api /bin/bash

# Connect to the PostgreSQL database
docker exec -it linteum-db psql -U LinteumUser -d Linteum
```

## Database

```bash
# Dump the database to a file
docker exec linteum-db pg_dump -U LinteumUser Linteum > backup.sql

# Restore from a dump file
cat backup.sql | docker exec -i linteum-db psql -U LinteumUser -d Linteum
```

## Volumes

```bash
# List volumes
docker volume ls

# Remove data volumes (⚠️ destroys DB data and Blazor keys)
docker compose down -v
```

Named volumes used:
- `db_data` — PostgreSQL data
- `blazor_keys` — Blazor data-protection keys

## Cleanup

```bash
# Remove stopped containers, unused networks, dangling images
docker system prune

# Remove everything unused (including unused images)
docker system prune -a

# Remove only dangling images
docker image prune
```

## Useful Inspect Commands

```bash
# Show running containers
docker ps

# Show all containers (including stopped)
docker ps -a

# Inspect a container's environment variables
docker inspect linteum-api --format '{{json .Config.Env}}'

# Check the network
docker network inspect linteum_prod1
```

## Build Context

The build context is the **repo root** (`.`) for all services.
Each service points to its own Dockerfile:

| Service        | Dockerfile                      |
|----------------|---------------------------------|
| linteum-api    | `Linteum.Api/Dockerfile`        |
| linteum-blazor | `Linteum.BlazorApp/Dockerfile`  |
| linteum-bots   | `Linteum.Bots/Dockerfile`       |

## Environment

All configuration is driven by the `.env` file in the repo root.
Docker Compose reads it automatically — no `--env-file` flag needed.

