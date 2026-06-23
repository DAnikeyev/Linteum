#!/usr/bin/env bash
# Reclaim aged Docker build cache so the VPS disk stays bounded (P-OPS-06).
#
# Builds run on the VPS through a remote Docker context, so the build cache lives
# in the VPS daemon and grows without limit (~50 GB reclaimable at last inspection).
#
# Run this on the Docker host (the VPS). Schedule weekly from root crontab, e.g.:
#   17 4 * * 0  /opt/linteum/scripts/maintenance/prune-docker-build-cache.sh >> /var/log/linteum-build-prune.log 2>&1
#
# Keeps cache younger than KEEP_HOURS (default 168h = 7 days) so incremental
# rebuilds stay fast while stale layers are reclaimed. Tune KEEP_HOURS as needed:
#   KEEP_HOURS=336 /opt/.../prune-docker-build-cache.sh   # keep ~2 weeks
set -euo pipefail

KEEP_HOURS="${KEEP_HOURS:-168}"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] pruning build cache older than ${KEEP_HOURS}h"
# --all: also drop cache still referenced by old build contexts (recent layers in the
#        KEEP_HOURS window are preserved by the until filter).
# --filter until=<h>: only touch entries older than the window.
docker builder prune --all --filter "until=${KEEP_HOURS}h" --force
echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] done"
