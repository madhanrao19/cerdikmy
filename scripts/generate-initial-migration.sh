#!/usr/bin/env bash
# =============================================================================
# Generate the initial EF Core migration for cerdikMY.
#
# Requires the .NET 10 SDK (the build/CI environment that produced this repo
# could not install the SDK, so migrations are generated here, on demand, by a
# developer/CI runner that has it).
#
# After generation, the migration lives in
#   src/Cerdik.Infrastructure/Persistence/Migrations/
# and DbInitializer automatically prefers Database.Migrate() over EnsureCreated()
# once any migration is present. Apply it in a deploy with:
#   dotnet Cerdik.Api.dll --migrate
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")/.."

NAME="${1:-Initial}"

echo "==> Ensuring dotnet-ef tool is available"
if ! dotnet tool list --global | grep -q dotnet-ef; then
  dotnet tool install --global dotnet-ef
fi
export PATH="$PATH:$HOME/.dotnet/tools"

echo "==> Generating migration '$NAME'"
dotnet ef migrations add "$NAME" \
  --project src/Cerdik.Infrastructure \
  --startup-project src/Cerdik.Api \
  --output-dir Persistence/Migrations

echo "==> Done. Review the migration under src/Cerdik.Infrastructure/Persistence/Migrations,"
echo "    then apply with:  dotnet ef database update --project src/Cerdik.Infrastructure --startup-project src/Cerdik.Api"
echo "    or at deploy time: dotnet Cerdik.Api.dll --migrate"
