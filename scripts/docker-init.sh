#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════
# Inicializa el entorno de demostración dentro de Docker.
# Lo ejecuta el servicio `init` del perfil "demo":
#
#   docker compose --profile demo up -d
#
# Hace, en orden: migraciones EF → bootstrap del local → seed de la carta.
# Es IDEMPOTENTE: si la carta ya está sembrada, no la duplica, así que
# puedes levantar el perfil tantas veces como quieras.
# ══════════════════════════════════════════════════════════════════
set -euo pipefail

API_URL="${MESTOBEACH_API_URL:-http://api:5011}"
TENANT_ID="${MESTOBEACH_TENANT_ID:-d0ebd974-8e71-4624-81b1-60384e132a95}"
PG_HOST="${POSTGRES_HOST:-postgres}"
PG_USER="${POSTGRES_USER:-mestobeach}"
PG_DB="${POSTGRES_DB:-mestobeach}"
export PGPASSWORD="${POSTGRES_PASSWORD:-mestobeach_dev}"

CONN="Host=${PG_HOST};Port=5432;Database=${PG_DB};Username=${PG_USER};Password=${PGPASSWORD}"

log() { echo "──▶ $*"; }

# ── 1. Esperar a PostgreSQL ───────────────────────────────────────
log "Esperando a PostgreSQL en ${PG_HOST}…"
for _ in $(seq 1 60); do
  if pg_isready -h "$PG_HOST" -U "$PG_USER" -d "$PG_DB" >/dev/null 2>&1; then break; fi
  sleep 2
done
pg_isready -h "$PG_HOST" -U "$PG_USER" -d "$PG_DB" >/dev/null 2>&1 || {
  echo "PostgreSQL no respondió a tiempo." >&2
  exit 1
}

# ── 2. Migraciones EF (una por módulo) ────────────────────────────
# Idempotentes: EF salta las que ya están aplicadas.
log "Aplicando migraciones…"
for m in Identity Sessions Payments Notifications Orders; do
  echo "   · $m"
  # Capturamos la salida en vez de encauzarla: así un fallo de EF aborta el
  # script en lugar de quedar enterrado por el grep que resume el resultado.
  if ! salida=$(dotnet ef database update \
      --project "src/Modules/$m/MestoBeach.$m.Infrastructure" \
      --startup-project src/MestoBeach.API \
      --context "${m}DbContext" \
      --connection "$CONN" 2>&1); then
    echo "$salida" >&2
    echo "Fallo aplicando las migraciones de $m." >&2
    exit 1
  fi
  echo "$salida" | grep -E "Applying migration|No migrations were applied" || echo "     (sin cambios)"
done

# ── 3. Bootstrap del local + cuenta de administración ─────────────
# El .sql lleva ON CONFLICT DO NOTHING: repetirlo no duplica nada.
log "Sembrando el local y la cuenta de administración…"
# ON_ERROR_STOP: por defecto psql sigue tras un ERROR y termina con código 0,
# lo que haría pasar por bueno un bootstrap que no sembró nada.
psql -h "$PG_HOST" -U "$PG_USER" -d "$PG_DB" -q -v ON_ERROR_STOP=1 -f /scripts/bootstrap-mestobeach.sql

# ── 4. Esperar a que la API esté sana ─────────────────────────────
log "Esperando a la API en ${API_URL}…"
for _ in $(seq 1 90); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' "${API_URL}/health" 2>/dev/null)" = "200" ]; then
    break
  fi
  sleep 2
done
if [ "$(curl -s -o /dev/null -w '%{http_code}' "${API_URL}/health" 2>/dev/null)" != "200" ]; then
  echo "La API no respondió a tiempo en ${API_URL}/health." >&2
  exit 1
fi

# ── 5. Seed de la carta (solo si está vacía) ──────────────────────
# seed-demo-mestobeach.sh NO es idempotente: duplicaría la carta si se repite.
# Por eso preguntamos antes al catálogo público cuántas categorías hay.
existing=$(curl -s "${API_URL}/api/v1/catalog/${TENANT_ID}" | jq -r '(.categories // []) | length' 2>/dev/null || echo 0)

if [ "${existing:-0}" -gt 0 ]; then
  log "La carta ya tiene ${existing} categorías — omito el seed (no duplicar)."
else
  log "Carta vacía — sembrando datos de demostración…"
  MESTOBEACH_API_URL="$API_URL" MESTOBEACH_TENANT_ID="$TENANT_ID" \
    bash /scripts/seed-demo-mestobeach.sh
fi

echo ""
echo "══════ ENTORNO LISTO ══════"
echo "  Menú (comensal)  http://localhost:3000/mesa?tenant=${TENANT_ID}&table=H01"
echo "  Staff            http://localhost:3001"
echo "  Admin            http://localhost:3003"
echo "  API / Swagger    http://localhost:5011/swagger"
echo "  MailHog          http://localhost:8025"
echo ""
echo "  Acceso: admin@mestobeach.demo / MestoBeach2026!"
