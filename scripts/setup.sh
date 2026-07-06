#!/usr/bin/env bash
# scripts/setup.sh — First-time project setup
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> SafeFamily — Setup"

# ── Frontend ──────────────────────────────────────────────────────────────────
echo ""
echo "[1/3] Installing frontend dependencies..."
cd "$ROOT/apps/web"
npm install
cp -n .env.example .env.local 2>/dev/null && echo "  Created .env.local from .env.example" || echo "  .env.local already exists"

# ── Backend ───────────────────────────────────────────────────────────────────
echo ""
echo "[2/3] Restoring .NET packages..."
cd "$ROOT/apps/api"
dotnet restore

# ── Docker (PostgreSQL) ───────────────────────────────────────────────────────
echo ""
echo "[3/3] Starting PostgreSQL via Docker Compose..."
cd "$ROOT"
docker compose up -d postgres

echo ""
echo "==> Done. Run 'scripts/dev.sh' to start all services."