---
name: reset-pruebas
description: >-
  Deja el entorno local de Mesto Beach limpio para volver a probar: borra los datos
  TRANSACCIONALES (sesiones, comandas/pedidos, líneas, pagos, viajes de reparto,
  participantes, turnos de caja, tokens de dispositivo, cuentas de cliente, auditoría) en
  Postgres y el estado operativo en Redis (mesas abiertas, PINs, llamadas de camarero),
  CONSERVANDO la configuración (tenant, staff, mesas, planos, estaciones KDS y menú) y la
  sesión de login. Úsala cuando el usuario diga "reset de pruebas", "resetear/limpiar los
  datos de prueba", "borrar las sesiones/comandas para volver a probar", "dejar el entorno
  limpio para testear", "truncar los datos operativos", "empezar las pruebas de cero" o
  similar. NO la uses para un wipe total de fábrica (eso borraría también tenant/staff/menú
  y requiere re-provisionar).
---

# Reset de datos de prueba — Mesto Beach

Objetivo: dejar el entorno local **en cero de datos operativos** para volver a probar el
flujo completo (pedir desde el menú, KDS, PDA, caja, salón, runner) sin arrastrar sesiones
ni comandas viejas, y **sin** perder la configuración ni tener que re-loguearse.

## Qué borra vs. qué conserva

**Borra (datos de prueba):**
- Postgres: `sessions`, `session_participants`, `orders`, `order_items`, `payments`,
  `delivery_trips`, `shifts`, `device_tokens`, `customer_accounts`, `audit_log`.
- Redis: `table:*` (mesas abiertas), `session:*`, `pin_validated:*`, `waiter_call:*`.

**Conserva (configuración + auth):**
- Postgres: tenant, `users` (staff), `tables`, `floors`, `kds_stations`, plantillas de
  email, configs de pago, platform users.
- Mongo: el catálogo del menú (`mestobeach_catalog`).
- Redis: `refresh:*` y `user_sessions:*` → **no te desloguea**.

## Cómo ejecutarla

Requiere el entorno local levantado (contenedores `mestobeach_postgres` y `mestobeach_redis`;
si no están, primero `scripts/dev-up.ps1`). Corre el script incluido y reporta al usuario
la verificación que imprime:

```bash
bash .claude/skills/reset-pruebas/reset-pruebas.sh
```

El script imprime la config **antes** (para probar que no se toca), hace el `TRUNCATE …
RESTART IDENTITY CASCADE`, limpia Redis, y al final verifica que lo operativo quedó en 0 y
la config intacta. Tras correrlo, avísale al usuario que **las mesas quedan libres** para
volver a abrir sesión.

## Por qué este alcance (no un wipe total)

Limpiar sólo Redis y dejar filas en Postgres —o al revés— deja **mesas fantasma**
("ocupadas" en Redis sin sesión en Postgres). Por eso el reset toca ambos almacenes en un
solo paso.

Un **wipe total de fábrica** (borrar también tenant/staff/mesas/menú) es distinto y **no**
es lo que hace esta skill: el `scripts/seed-demo-mestobeach.ps1` re-crea la carta, las
estaciones y las mesas, pero **necesita el tenant y el admin ya creados** (se loguea con
ellos). Así que un wipe total dejaría el sistema **sin login** hasta re-provisionar el
tenant/admin por separado. Si el usuario pide eso, confírmalo explícitamente antes de
proceder — no lo hagas con esta skill.
