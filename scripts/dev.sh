#!/usr/bin/env bash
# scripts/dev.sh — Start all services for local development
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> DocumentOCR — Starting dev servers"
echo "    Frontend : http://localhost:5173"
echo "    API      : http://localhost:5282"
echo "    Swagger  : http://localhost:5282/swagger"
echo ""

# Start PostgreSQL if not running
docker compose -f "$ROOT/docker-compose.yml" up -d postgres

# Start API in background
echo "[api] Starting ASP.NET Core..."
cd "$ROOT/apps/api/DocumentOCR.WebApi"
dotnet run --launch-profile http &
API_PID=$!

# Start frontend
echo "[web] Starting Vite dev server..."
cd "$ROOT/apps/web"
npm run dev &
WEB_PID=$!

# Wait — kill both on Ctrl+C
trap "kill $API_PID $WEB_PID 2>/dev/null; exit 0" SIGINT SIGTERM
wait