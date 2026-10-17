#!/usr/bin/env bash
# One-time local setup: generates random local credentials into .env and stores the API's
# secrets (connection string, JWT signing key, demo seed password) in .NET user-secrets.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
api="$root/backend/src/ReworkGuard.Api"
env_file="$root/.env"

random() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c "$1"; }

if [[ ! -f "$env_file" ]]; then
  cat > "$env_file" <<ENV
MYSQL_PORT=3306
MYSQL_PASSWORD=$(random 24)
MYSQL_ROOT_PASSWORD=$(random 24)
ENV
  echo "Created .env with random MySQL passwords."
fi

# Keys used by `docker compose up` (API container); appended when missing from an older .env.
grep -q '^JWT_SIGNING_KEY=' "$env_file" || echo "JWT_SIGNING_KEY=$(random 64)" >> "$env_file"
grep -q '^SEED_PASSWORD=' "$env_file" || echo "SEED_PASSWORD=$(random 16)" >> "$env_file"

# shellcheck disable=SC1090
source "$env_file"

seed_password="${SEED_PASSWORD}"

dotnet user-secrets --project "$api" set "ConnectionStrings:Default" \
  "Server=127.0.0.1;Port=${MYSQL_PORT};Database=reworkguard;User=reworkguard;Password=${MYSQL_PASSWORD}" >/dev/null
dotnet user-secrets --project "$api" set "Jwt:SigningKey" "$(random 64)" >/dev/null
dotnet user-secrets --project "$api" set "Database:SeedPassword" "$seed_password" >/dev/null

echo "API user-secrets configured."
echo "Demo users (e.g. manager@reworkguard.local, lead@reworkguard.local, qa@reworkguard.local, priya@reworkguard.local)"
echo "share the seed password: $seed_password"
