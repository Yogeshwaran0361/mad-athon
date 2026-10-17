#!/usr/bin/env bash
# Runs every check CI runs. Needs Docker (Testcontainers) and the .NET 8 SDK + Node 22.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

echo "== backend: build =="
(cd "$root/backend" && dotnet tool restore >/dev/null && dotnet build -warnaserror)
echo "== backend: migrations in sync =="
(cd "$root/backend" && dotnet ef migrations has-pending-model-changes -p src/ReworkGuard.Infrastructure -s src/ReworkGuard.Api --no-build)
echo "== backend: tests =="
(cd "$root/backend" && dotnet test --no-build)
echo "== frontend =="
(cd "$root/frontend" && npm ci && npm run lint && npm run typecheck && npm test && npm run build)
