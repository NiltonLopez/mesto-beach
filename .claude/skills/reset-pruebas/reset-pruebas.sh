#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Reset de datos de PRUEBA — Mesto Beach (entorno local)
#
# Borra lo TRANSACCIONAL (sesiones, comandas, líneas, pagos, reparto, participantes,
# turnos de caja, tokens de dispositivo, cuentas de cliente, auditoría) en Postgres y
# el estado operativo en Redis (mesas abiertas, PINs, llamadas de camarero).
#
# CONSERVA la configuración (tenant, staff, mesas, planos, estaciones KDS, menú en Mongo)
# y las llaves de auth de Redis (refresh:*, user_sessions:*) — así que NO te desloguea.
#
# Es idempotente. Requiere el entorno local levantado (scripts/dev-up.ps1).
#   Uso:  bash .claude/skills/reset-pruebas/reset-pruebas.sh
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

PG=mestobeach_postgres
RD=mestobeach_redis
PGUSER=mestobeach
PGDB=mestobeach
RDPASS=mestobeach_dev

# Tablas operativas (lo que se acumula al probar). NO incluye config.
OPS_TABLES="sessions, session_participants, orders, order_items, payments, delivery_trips, shifts, device_tokens, customer_accounts, audit_log"
# Patrones operativos en Redis. NO incluye refresh:* ni user_sessions:* (auth).
REDIS_PATTERNS="table:* session:* pin_validated:* waiter_call:*"

command -v docker >/dev/null || { echo "ERROR: docker no disponible."; exit 1; }
docker ps --format '{{.Names}}' | grep -qx "$PG" || { echo "ERROR: contenedor $PG no corre. Levanta el entorno (scripts/dev-up.ps1)."; exit 1; }
docker ps --format '{{.Names}}' | grep -qx "$RD" || { echo "ERROR: contenedor $RD no corre."; exit 1; }

echo "== Config ANTES (debe quedar igual) =="
docker exec "$PG" psql -U "$PGUSER" -d "$PGDB" -tAc \
  "SELECT 'tenants='||(SELECT count(*) FROM tenants)||' users='||(SELECT count(*) FROM users)||' tables='||(SELECT count(*) FROM tables)||' floors='||(SELECT count(*) FROM floors)||' kds='||(SELECT count(*) FROM kds_stations);"

echo "== TRUNCATE operativo (Postgres) =="
docker exec "$PG" psql -U "$PGUSER" -d "$PGDB" -c "TRUNCATE $OPS_TABLES RESTART IDENTITY CASCADE;"

echo "== Limpiar estado operativo (Redis), conservando auth =="
for pat in $REDIS_PATTERNS; do
  docker exec "$RD" redis-cli -a "$RDPASS" --no-auth-warning --scan --pattern "$pat" 2>/dev/null \
    | while read -r k; do [ -n "$k" ] && docker exec "$RD" redis-cli -a "$RDPASS" --no-auth-warning DEL "$k" >/dev/null 2>&1 || true; done
done

echo "== Verificación =="
docker exec "$PG" psql -U "$PGUSER" -d "$PGDB" -tAc \
  "SELECT 'OPERATIVO -> sessions='||(SELECT count(*) FROM sessions)||' orders='||(SELECT count(*) FROM orders)||' payments='||(SELECT count(*) FROM payments)||' trips='||(SELECT count(*) FROM delivery_trips)||' participants='||(SELECT count(*) FROM session_participants); SELECT 'CONFIG    -> tenants='||(SELECT count(*) FROM tenants)||' users='||(SELECT count(*) FROM users)||' tables='||(SELECT count(*) FROM tables)||' floors='||(SELECT count(*) FROM floors)||' kds='||(SELECT count(*) FROM kds_stations);"
for pat in $REDIS_PATTERNS; do
  n=$(docker exec "$RD" redis-cli -a "$RDPASS" --no-auth-warning --scan --pattern "$pat" 2>/dev/null | grep -c . || true)
  echo "  redis $pat -> $n"
done
keep_refresh=$(docker exec "$RD" redis-cli -a "$RDPASS" --no-auth-warning --scan --pattern 'refresh:*' 2>/dev/null | grep -c . || true)
keep_user=$(docker exec "$RD" redis-cli -a "$RDPASS" --no-auth-warning --scan --pattern 'user_sessions:*' 2>/dev/null | grep -c . || true)
echo "  (auth conservada: refresh:* -> $keep_refresh, user_sessions:* -> $keep_user)"
echo "Reset de pruebas completo. Las mesas quedan libres para volver a abrir sesión."
