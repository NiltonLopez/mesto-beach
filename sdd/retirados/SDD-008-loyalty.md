# SDD-008 — Loyalty

## Servicio
**Módulo**: Loyalty  
**Base path**: `/api/v1/loyalty`, `/api/v1/platform/loyalty-contracts`  
**Responsabilidad**: Red de Lealtad Mesto cross-tenant. Acumulación de puntos al pagar, canje de ítems elegibles en sesión activa, contratos negociados entre Mesto y restaurantes, y settlement mensual en billing.

**PRDs de referencia**: PRD-012 (Fidelización y Cuentas de Cliente), PRD-015 §Módulo 11 (Contratos de Fidelización)

**Reglas clave**:
- Máximo 1 ítem canjeado por sesión (MVP)
- Los puntos se acreditan/descuentan al confirmar el pago (no al crear el pedido)
- Propina no genera puntos
- Cortesías no generan puntos
- Contrato pausado: sin acumulación ni canje, puntos existentes preservados

**Dependencias**:
- PostgreSQL: `loyalty_contracts`, `loyalty_redeemable_items`, `loyalty_transactions`, `loyalty_redemptions`, `customer_accounts`, `orders`, `order_items`, `session_payments`
- Redis: `loyalty:balance:{customerId}` TTL 10min, `loyalty:contract:{tenantId}` TTL 10min
- MongoDB: `catalogs` (para validar catalog_item_id en items canjeables)
- SDD-001 Identity: JWT validation
- SDD-005 Payments: triggers earn/confirm-redemption after payment
- SDD-007 Notifications: SignalR events loyalty.points_earned, loyalty.points_redeemed

---

## Endpoints

---

### GET /api/v1/loyalty/me

**Objetivo**
Retorna el balance actual de puntos de fidelidad y las últimas 10 transacciones del cliente autenticado.

**Auth**
Customer JWT (Bearer token con `role=customer`, emitido por SDD-001)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "cliente@example.com",
  "loyaltyPoints": 1450,
  "recentTransactions": [
    {
      "id": "7b3c9f12-1234-4abc-9def-0123456789ab",
      "type": "EARNED",
      "points": 120,
      "balanceAfter": 1450,
      "description": "Puntos por pago en La Candelaria · Mesa 4",
      "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
      "tenantName": "La Candelaria",
      "orderId": "eeee1111-aaaa-bbbb-cccc-dddddddd0001",
      "sessionPaymentId": "pppp1111-aaaa-bbbb-cccc-dddddddd0001",
      "createdAt": "2026-06-05T19:45:12Z"
    },
    {
      "id": "7b3c9f12-1234-4abc-9def-0123456789ac",
      "type": "REDEEMED",
      "points": -200,
      "balanceAfter": 1330,
      "description": "Canje: Limonada de coco",
      "tenantId": "a1b2c3d4-0002-0002-0002-000000000002",
      "tenantName": "El Patio Grill",
      "orderId": "eeee2222-aaaa-bbbb-cccc-dddddddd0002",
      "sessionPaymentId": "pppp2222-aaaa-bbbb-cccc-dddddddd0002",
      "createdAt": "2026-06-03T14:22:05Z"
    }
  ]
}
```

**Validaciones**
- `Authorization` header: debe ser Bearer JWT válido con `role=customer`

**Reglas de Negocio**
- Consultar primero `loyalty:balance:{customerId}` en Redis. Si existe, usar ese valor como `loyaltyPoints` (cache hit).
- Si no existe en Redis, leer `customer_accounts.loyalty_points WHERE id = customerId` y poblar cache con TTL 10min.
- `recentTransactions`: últimos 10 registros de `loyalty_transactions WHERE customer_id = customerId ORDER BY created_at DESC LIMIT 10`.
- Para cada transacción, hacer JOIN con `tenants` para incluir `tenantName`.
- El balance y las transacciones son cross-tenant; no se filtra por ningún `tenant_id`.
- `customerId` se obtiene del campo `sub` del JWT.

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=customer` |
| 404 | No existe registro en `customer_accounts` para el `sub` del JWT |

**Observabilidad**
- Log: `"loyalty.me.fetched"` `{ customerId, loyaltyPoints, recentTransactionCount, cacheHit }`
- Métrica: `loyalty.me_fetched` (counter)

**Casos de Aceptación**
- [ ] Cliente autenticado con puntos y transacciones: retorna 200 con balance y últimas 10 transacciones
- [ ] Balance en cache Redis: `cacheHit=true` en log, no se consulta PostgreSQL para `loyaltyPoints`
- [ ] Cache miss: se lee de PostgreSQL y se escribe en Redis con TTL 10min
- [ ] Cliente sin transacciones previas: retorna 200 con `recentTransactions: []` y balance correcto
- [ ] Token ausente: retorna 401
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] `sub` del JWT sin `customer_accounts`: retorna 404

---

### GET /api/v1/loyalty/me/transactions

**Objetivo**
Retorna el historial paginado completo de transacciones de fidelidad del cliente autenticado con soporte de filtrado por tipo y tenant.

**Auth**
Customer JWT (Bearer token con `role=customer`, emitido por SDD-001)

**Request**
- Path params: Ninguno
- Query params:
  - `page` INT — número de página (default: 1, min: 1)
  - `limit` INT — registros por página (default: 20, min: 1, max: 100)
  - `type` STRING — opcional; uno de `EARNED`, `REDEEMED`, `EXPIRED`, `ADJUSTED`
  - `tenantId` UUID — opcional; filtra transacciones de un restaurante específico
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "id": "7b3c9f12-1234-4abc-9def-0123456789ab",
      "type": "EARNED",
      "points": 120,
      "balanceAfter": 1450,
      "description": "Puntos por pago en La Candelaria · Mesa 4",
      "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
      "tenantName": "La Candelaria",
      "orderId": "eeee1111-aaaa-bbbb-cccc-dddddddd0001",
      "sessionPaymentId": "pppp1111-aaaa-bbbb-cccc-dddddddd0001",
      "createdAt": "2026-06-05T19:45:12Z"
    },
    {
      "id": "7b3c9f12-1234-4abc-9def-0123456789ac",
      "type": "REDEEMED",
      "points": -200,
      "balanceAfter": 1330,
      "description": "Canje: Limonada de coco",
      "tenantId": "a1b2c3d4-0002-0002-0002-000000000002",
      "tenantName": "El Patio Grill",
      "orderId": "eeee2222-aaaa-bbbb-cccc-dddddddd0002",
      "sessionPaymentId": "pppp2222-aaaa-bbbb-cccc-dddddddd0002",
      "createdAt": "2026-06-03T14:22:05Z"
    }
  ],
  "meta": {
    "page": 1,
    "limit": 20,
    "total": 47,
    "totalPages": 3,
    "hasNextPage": true,
    "hasPrevPage": false
  }
}
```

**Validaciones**
- `page`: entero ≥ 1; retorna 400 si < 1
- `limit`: entero entre 1 y 100 inclusive; retorna 400 si fuera de rango
- `type`: si presente, debe ser exactamente uno de `EARNED`, `REDEEMED`, `EXPIRED`, `ADJUSTED`
- `tenantId`: si presente, debe ser UUID v4 válido

**Reglas de Negocio**
- Consultar `loyalty_transactions WHERE customer_id = customerId`.
- Aplicar filtro `AND type = :type` si se recibió el parámetro.
- Aplicar filtro `AND tenant_id = :tenantId` si se recibió el parámetro.
- `ORDER BY created_at DESC`.
- `OFFSET = (page - 1) * limit`, `LIMIT = limit`.
- `total` refleja el COUNT(*) con todos los filtros aplicados (sin paginación).
- JOIN con `tenants` para incluir `tenantName` en cada registro.
- Esta operación no usa cache Redis.

**Errores**
| Código | Condición |
|---|---|
| 400 | `page` < 1 o no es entero |
| 400 | `limit` < 1, > 100 o no es entero |
| 400 | `type` con valor distinto a los cuatro permitidos |
| 400 | `tenantId` presente pero no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=customer` |

**Observabilidad**
- Log: `"loyalty.transactions.listed"` `{ customerId, page, limit, type, tenantId, total }`
- Métrica: `loyalty.transactions_listed` (counter)

**Casos de Aceptación**
- [ ] Cliente con 47 transacciones, `page=1`, `limit=20`: retorna 20 items, `total=47`, `totalPages=3`, `hasNextPage=true`, `hasPrevPage=false`
- [ ] Filtro `type=REDEEMED`: retorna solo transacciones de tipo canje
- [ ] Filtro `tenantId` válido: retorna solo transacciones de ese restaurante
- [ ] `limit=101`: retorna 400
- [ ] `limit=0`: retorna 400
- [ ] `type=CREDITO`: retorna 400
- [ ] `tenantId=not-a-uuid`: retorna 400
- [ ] Cliente sin transacciones: retorna 200 con `data: []`, `total: 0`
- [ ] Token ausente: retorna 401

---

### GET /api/v1/loyalty/catalog/{tenantId}/redeemable

**Objetivo**
Retorna la lista de ítems canjeables activos del restaurante indicado, acompañada del balance del cliente para calcular elegibilidad de canje.

**Auth**
Customer JWT (Bearer token con `role=customer`, emitido por SDD-001)

**Request**
- Path params:
  - `tenantId` UUID — identificador del restaurante
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "tenantName": "La Candelaria",
  "contractStatus": "active",
  "customerBalance": 1450,
  "redeemableItems": [
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000001",
      "catalogItemId": "mongo_item_id_001",
      "itemNameSnapshot": "Limonada de Coco",
      "pointsCost": 200,
      "isActive": true,
      "customerCanRedeem": true
    },
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000002",
      "catalogItemId": "mongo_item_id_002",
      "itemNameSnapshot": "Café Americano",
      "pointsCost": 1600,
      "isActive": true,
      "customerCanRedeem": false
    }
  ]
}
```

**Validaciones**
- `tenantId`: debe ser UUID v4 válido
- El cliente autenticado debe tener al menos una sesión con `status='open'` en el `tenantId` indicado

**Reglas de Negocio**
- Verificar que el cliente tiene sesión activa en ese tenant: `SELECT id FROM sessions WHERE customer_id = :customerId AND tenant_id = :tenantId AND status = 'open' LIMIT 1`. Si no existe, retornar 403.
- Consultar contrato: leer de `loyalty:contract:{tenantId}` en Redis. Si no existe, leer `loyalty_contracts WHERE tenant_id = :tenantId AND status IN ('active','paused')` y poblar cache con TTL 10min.
- Si no existe contrato para el `tenantId`: retornar 404.
- Si `contractStatus = 'paused'`: retornar 200 con `redeemableItems: []` y `contractStatus: "paused"`. No es un error; la UI muestra el mensaje correspondiente.
- Si `contractStatus = 'active'`: retornar solo ítems con `is_active = true` de `loyalty_redeemable_items WHERE contract_id = :contractId`.
- `customerBalance`: leer de `loyalty:balance:{customerId}` en Redis. Si cache miss, leer `customer_accounts.loyalty_points WHERE id = :customerId` y poblar cache TTL 10min.
- `customerCanRedeem = (customerBalance >= pointsCost)`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=customer` |
| 403 | El cliente no tiene sesión activa (`status='open'`) en ese tenant |
| 404 | No existe contrato de fidelidad para ese `tenantId` |

**Observabilidad**
- Log: `"loyalty.catalog.redeemable.fetched"` `{ customerId, tenantId, contractStatus, itemCount, customerBalance, contractCacheHit, balanceCacheHit }`
- Métrica: `loyalty.catalog_redeemable_fetched` (counter)

**Casos de Aceptación**
- [ ] Contrato activo con 2 ítems, cliente con 1450 pts: retorna ambos ítems con `customerCanRedeem` correcto según balance
- [ ] Contrato con `status='paused'`: retorna 200 con `redeemableItems: []` y `contractStatus: "paused"`
- [ ] Cliente sin sesión activa en ese tenant: retorna 403
- [ ] Contrato inexistente para el tenant: retorna 404
- [ ] Cache hit en contrato y balance: ambos flags `cacheHit=true` en log
- [ ] `tenantId=not-a-uuid`: retorna 400
- [ ] Token ausente: retorna 401

---

### POST /api/v1/loyalty/sessions/{sessionId}/redeem

**Objetivo**
Registra la reserva de canje de un ítem elegible en la sesión activa; los puntos quedan reservados pero no se descuentan hasta confirmar el pago.

**Auth**
Customer JWT (Bearer token con `role=customer`, emitido por SDD-001)

**Request**
- Path params:
  - `sessionId` UUID — identificador de la sesión activa
- Query params: Ninguno
- Body:
```json
{
  "redeemableItemId": "9fbd1234-aaaa-bbbb-cccc-000000000001"
}
```

**Response 201**
```json
{
  "reservationId": "rsrv-7b3c9f12-aabb-ccdd-eeff-001122334455",
  "sessionId": "sess-aaaa-bbbb-cccc-dddddddddddd",
  "redeemableItemId": "9fbd1234-aaaa-bbbb-cccc-000000000001",
  "catalogItemId": "mongo_item_id_001",
  "itemNameSnapshot": "Limonada de Coco",
  "pointsCost": 200,
  "status": "pending",
  "message": "Ítem reservado. Los puntos se descontarán al confirmar el pago."
}
```

**Validaciones**
- `sessionId`: debe ser UUID v4 válido
- `redeemableItemId`: requerido, debe ser UUID v4 válido
- El `redeemableItemId` debe existir en `loyalty_redeemable_items` con `is_active = true`

**Reglas de Negocio**
- Verificar que `sessionId` pertenece al cliente autenticado (`sessions.customer_id = sub` del JWT). Si no, retornar 403.
- Verificar que la sesión tiene `status = 'open'`. Si está cerrada o pagada, retornar 422.
- Obtener el `tenant_id` de la sesión para resolver el contrato de fidelidad correspondiente.
- Verificar que el contrato del tenant tiene `status = 'active'`. Si está `paused`, retornar 409.
- Verificar que no existe ya una reserva en Redis con key `loyalty:pending_redemption:{sessionId}`. Si existe, retornar 409 (máximo 1 canje por sesión, OQ-1201).
- Verificar que `customer_accounts.loyalty_points >= redeemableItem.points_cost`. Usar valor de `loyalty:balance:{customerId}` si está en cache; si no, leer de PostgreSQL. Si insuficiente, retornar 422.
- Los puntos **NO** se descuentan de `customer_accounts` en este momento.
- Generar `reservationId = gen_random_uuid()`.
- Persistir en Redis:
  - Key: `loyalty:pending_redemption:{sessionId}`
  - Value (JSON serializado):
    ```json
    {
      "reservationId": "<uuid>",
      "customerId": "<uuid>",
      "tenantId": "<uuid>",
      "redeemableItemId": "<uuid>",
      "catalogItemId": "<string>",
      "itemNameSnapshot": "<string>",
      "pointsCost": 200,
      "reservedAt": "2026-06-07T20:00:00Z"
    }
    ```
  - TTL: ninguno explícito; la key se elimina al cancelar (DELETE /redeem) o al confirmar pago (confirm-redemption).
- Emitir evento SignalR `loyalty.item_reserved` a los participantes de la sesión via SDD-007: `{ sessionId, itemNameSnapshot, pointsCost }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `redeemableItemId` ausente o no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=customer` |
| 403 | La sesión no pertenece al cliente autenticado |
| 404 | `sessionId` no existe |
| 404 | `redeemableItemId` no existe o `is_active = false` |
| 409 | Ya existe una reserva de canje pendiente para esta sesión |
| 409 | El contrato del tenant tiene `status = 'paused'` |
| 422 | La sesión no está en estado `open` |
| 422 | El cliente no tiene suficientes puntos (`loyaltyPoints < pointsCost`) |

**Observabilidad**
- Log: `"loyalty.redeem.reserved"` `{ reservationId, customerId, sessionId, tenantId, redeemableItemId, pointsCost, customerBalance }`
- Log (422 puntos): `"loyalty.redeem.insufficient_points"` `{ customerId, sessionId, required, available }`
- Métrica: `loyalty.redeem_reserved` (counter)
- Métrica: `loyalty.redeem_insufficient_points` (counter)

**Casos de Aceptación**
- [ ] Cliente con 1450 pts reserva ítem de 200 pts: retorna 201, key en Redis, puntos no descuentados aún
- [ ] Segunda llamada en la misma sesión: retorna 409
- [ ] Contrato del tenant `paused`: retorna 409
- [ ] Puntos insuficientes (balance 150, ítem 200 pts): retorna 422
- [ ] `redeemableItemId` con `is_active = false`: retorna 404
- [ ] Sesión en estado `paid`: retorna 422
- [ ] Sesión no pertenece al cliente autenticado: retorna 403
- [ ] `sessionId` inexistente: retorna 404
- [ ] Token ausente: retorna 401

---

### DELETE /api/v1/loyalty/sessions/{sessionId}/redeem

**Objetivo**
Cancela la reserva de canje pendiente de la sesión indicada antes del pago, sin afectar el balance de puntos del cliente.

**Auth**
Customer JWT (Bearer token con `role=customer`, emitido por SDD-001)

**Request**
- Path params:
  - `sessionId` UUID — identificador de la sesión activa
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "sessionId": "sess-aaaa-bbbb-cccc-dddddddddddd",
  "cancelledReservationId": "rsrv-7b3c9f12-aabb-ccdd-eeff-001122334455",
  "status": "cancelled",
  "message": "Reserva de canje cancelada. Los puntos no fueron descontados."
}
```

**Validaciones**
- `sessionId`: debe ser UUID v4 válido

**Reglas de Negocio**
- Verificar que `sessionId` pertenece al cliente autenticado (`sessions.customer_id = sub` del JWT). Si no, retornar 403.
- Verificar que la sesión tiene `status = 'open'`. Si está pagada, retornar 422 (el canje ya fue confirmado y los puntos descontados).
- Leer `loyalty:pending_redemption:{sessionId}` de Redis. Si no existe, retornar 404.
- Verificar que `customerId` en la reserva Redis coincide con el `sub` del JWT. Si no, retornar 403.
- Eliminar la key `loyalty:pending_redemption:{sessionId}` de Redis con `DEL`.
- Los puntos **no** fueron descuentados previamente, por lo que no se opera sobre `customer_accounts`.
- No se crea ningún registro en `loyalty_transactions` (no hubo movimiento de puntos).
- Emitir evento SignalR `loyalty.redemption_cancelled` a los participantes de la sesión via SDD-007: `{ sessionId }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `sessionId` no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=customer` |
| 403 | La sesión no pertenece al cliente autenticado |
| 403 | La reserva en Redis pertenece a un `customerId` distinto |
| 404 | `sessionId` no existe |
| 404 | No existe reserva de canje pendiente para esta sesión |
| 422 | La sesión ya fue pagada (`status != 'open'`) |

**Observabilidad**
- Log: `"loyalty.redeem.cancelled"` `{ customerId, sessionId, tenantId, cancelledReservationId, redeemableItemId, pointsCost }`
- Métrica: `loyalty.redeem_cancelled` (counter)

**Casos de Aceptación**
- [ ] Reserva existente y sesión abierta: retorna 200, key eliminada de Redis
- [ ] Sin reserva pendiente para la sesión: retorna 404
- [ ] Sesión ya pagada (`status='paid'`): retorna 422
- [ ] Sesión no pertenece al cliente autenticado: retorna 403
- [ ] Token ausente: retorna 401

---

### POST /api/v1/loyalty/earn

**Objetivo**
Acredita puntos al cliente tras la confirmación de un pago; endpoint interno llamado exclusivamente por SDD-005 (Payments).

**Auth**
Llamada interna de servicio a servicio. Header requerido: `X-Internal-Secret: {LOYALTY_INTERNAL_SECRET}` (variable de entorno). Sin JWT de usuario.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "orderId": "eeee1111-aaaa-bbbb-cccc-dddddddd0001",
  "sessionPaymentId": "pppp1111-aaaa-bbbb-cccc-dddddddd0001",
  "subtotalCop": 82000
}
```

> `subtotalCop` es calculado por SDD-005 como: suma de `item_price_snapshot_cop * quantity` de todos los `order_items` donde `is_courtesy = false` AND `loyalty_redeemed = false`, excluida la propina.

**Response 200**
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "pointsEarned": 123,
  "newBalance": 1573,
  "transactionId": "txn-7b3c9f12-1234-4abc-9def-001122334455"
}
```

**Validaciones**
- `customerId`: requerido, UUID v4 válido
- `tenantId`: requerido, UUID v4 válido
- `orderId`: requerido, UUID v4 válido
- `sessionPaymentId`: requerido, UUID v4 válido
- `subtotalCop`: requerido, entero ≥ 0
- Header `X-Internal-Secret`: requerido; debe coincidir con valor de `LOYALTY_INTERNAL_SECRET` en entorno

**Reglas de Negocio**
- **Idempotencia**: verificar si ya existe `loyalty_transactions WHERE order_id = :orderId AND type = 'EARNED'`. Si existe, retornar 200 con los datos de la transacción existente sin duplicar puntos. Log `loyalty.earn.idempotent`.
- Si `subtotalCop = 0`: retornar 200 con `pointsEarned: 0`, `transactionId: null`. No crear transacción.
- Obtener contrato: leer de `loyalty:contract:{tenantId}` en Redis. Si cache miss, leer `loyalty_contracts WHERE tenant_id = :tenantId`. Si no existe contrato o `status != 'active'`: retornar 200 con `pointsEarned: 0`, `transactionId: null`. Log `loyalty.earn.skipped`.
- **Fórmula**: `pointsEarned = floor(subtotalCop / 1000 * contract.points_rate)`
  - Ejemplo: `floor(82000 / 1000 * 1.5) = floor(123.0) = 123`
- Si `pointsEarned = 0` (subtotal muy bajo, e.g. < `1000 / points_rate`): retornar 200 con `pointsEarned: 0`, `transactionId: null`. No crear transacción. Log `loyalty.earn.skipped`.
- Ejecutar en **transacción PostgreSQL atómica**:
  1. `UPDATE customer_accounts SET loyalty_points = loyalty_points + :pointsEarned WHERE id = :customerId RETURNING loyalty_points AS new_balance`
  2. `INSERT INTO loyalty_transactions (id, customer_id, tenant_id, order_id, session_payment_id, type, points, balance_after, description, created_at) VALUES (gen_random_uuid(), :customerId, :tenantId, :orderId, :sessionPaymentId, 'EARNED', :pointsEarned, :newBalance, 'Puntos por pago · ' || :tenantName, NOW())`
- `DEL loyalty:balance:{customerId}` en Redis para invalidar cache.
- Emitir evento SignalR `loyalty.points_earned` via SDD-007 a los participantes de la sesión: `{ customerId, sessionId, pointsEarned, newBalance, tenantName }`.
  - `sessionId` se obtiene de `session_payments WHERE id = :sessionPaymentId`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Cualquier campo requerido ausente, tipo incorrecto o UUID inválido |
| 400 | `subtotalCop` es negativo o no es entero |
| 401 | Header `X-Internal-Secret` ausente o no coincide con `LOYALTY_INTERNAL_SECRET` |
| 404 | `customerId` no existe en `customer_accounts` |

**Observabilidad**
- Log: `"loyalty.earn.credited"` `{ customerId, tenantId, orderId, sessionPaymentId, subtotalCop, pointsEarned, newBalance, transactionId }`
- Log: `"loyalty.earn.skipped"` `{ customerId, tenantId, orderId, reason }` — `reason` in `["zero_subtotal", "no_contract", "contract_paused", "zero_points_calculated"]`
- Log: `"loyalty.earn.idempotent"` `{ customerId, orderId, existingTransactionId }`
- Métrica: `loyalty.points_earned` (counter)
- Métrica: `loyalty.earn_skipped` (counter)

**Casos de Aceptación**
- [ ] `subtotalCop=82000`, `points_rate=1.5`: `pointsEarned = floor(123.0) = 123`, balance incrementado, transacción creada, cache invalidado
- [ ] Llamada idempotente con mismo `orderId`: retorna 200 con datos de transacción existente, balance no alterado
- [ ] Contrato con `status='paused'`: retorna 200 con `pointsEarned: 0`, sin transacción
- [ ] Sin contrato para el tenant: retorna 200 con `pointsEarned: 0`, sin transacción
- [ ] `subtotalCop=0`: retorna 200 con `pointsEarned: 0`, sin transacción
- [ ] `subtotalCop=500`, `points_rate=1.5`: `floor(0.75) = 0` → retorna 200 con `pointsEarned: 0`
- [ ] `X-Internal-Secret` incorrecto: retorna 401
- [ ] `X-Internal-Secret` ausente: retorna 401
- [ ] Cache `loyalty:balance:{customerId}` eliminado tras acreditación exitosa
- [ ] `customerId` inexistente en `customer_accounts`: retorna 404

---

### POST /api/v1/loyalty/confirm-redemption

**Objetivo**
Finaliza el descuento de puntos de un canje pendiente al confirmarse el pago; endpoint interno llamado exclusivamente por SDD-005 (Payments).

**Auth**
Llamada interna de servicio a servicio. Header requerido: `X-Internal-Secret: {LOYALTY_INTERNAL_SECRET}` (variable de entorno). Sin JWT de usuario.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "sessionId": "sess-aaaa-bbbb-cccc-dddddddddddd",
  "orderId": "eeee1111-aaaa-bbbb-cccc-dddddddd0001",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "itemValueCop": 14000,
  "billingPeriod": "2026-06"
}
```

> `itemValueCop` es el precio real del ítem canjeado (tomado de `order_items.item_price_cop_original` o del catálogo vigente al momento del pago), calculado por SDD-005. `billingPeriod` es el mes de pago en formato `YYYY-MM`.

**Response 200**
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "pointsDeducted": 200,
  "newBalance": 1250,
  "transactionId": "txn-aabb1234-ccdd-eeff-0011-223344556677",
  "redemptionId": "rdmp-5566-aabb-ccdd-eeff-001122334455"
}
```

**Validaciones**
- `sessionId`: requerido, UUID v4 válido
- `orderId`: requerido, UUID v4 válido
- `customerId`: requerido, UUID v4 válido
- `tenantId`: requerido, UUID v4 válido
- `itemValueCop`: requerido, entero ≥ 0
- `billingPeriod`: requerido, formato `YYYY-MM` (regex: `^\d{4}-(0[1-9]|1[0-2])$`)
- Header `X-Internal-Secret`: requerido

**Reglas de Negocio**
- **Sin reserva pendiente**: leer `loyalty:pending_redemption:{sessionId}` de Redis. Si la key no existe, retornar 200 con `pointsDeducted: 0`, `transactionId: null`, `redemptionId: null`. Log `loyalty.redemption.no_pending`. Esta es la ruta normal cuando la sesión no tenía canje activo.
- **Idempotencia**: si la key existe en Redis pero ya hay `loyalty_transactions WHERE order_id = :orderId AND type = 'REDEEMED'`, retornar 200 con datos de la transacción existente sin duplicar. Log `loyalty.redemption.idempotent`.
- Verificar que `customerId` en la reserva Redis coincide con el `customerId` del body. Si difieren, retornar 409 con descripción de inconsistencia.
- Obtener `pointsCost` de la reserva Redis (`reservation.pointsCost`).
- Ejecutar en **transacción PostgreSQL atómica**:
  1. `UPDATE customer_accounts SET loyalty_points = loyalty_points - :pointsCost WHERE id = :customerId AND loyalty_points >= :pointsCost RETURNING loyalty_points AS new_balance`
     — la condición `AND loyalty_points >= :pointsCost` es salvaguarda contra race condition.
  2. Si el UPDATE afecta 0 filas (balance insuficiente en race condition): hacer `ROLLBACK` y retornar 409. Log `loyalty.redemption.insufficient_balance`.
  3. `INSERT INTO loyalty_transactions (id, customer_id, tenant_id, order_id, session_payment_id, type, points, balance_after, description, created_at) VALUES (gen_random_uuid(), ..., 'REDEEMED', -:pointsCost, :newBalance, 'Canje: ' || :itemNameSnapshot, NOW())` — `session_payment_id` se obtiene de `session_payments WHERE session_id = :sessionId AND status = 'confirmed' ORDER BY created_at DESC LIMIT 1`.
  4. `INSERT INTO loyalty_redemptions (id, transaction_id, order_id, tenant_id, catalog_item_id, item_value_cop, billing_period, settled) VALUES (gen_random_uuid(), :transactionId, :orderId, :tenantId, :catalogItemId, :itemValueCop, :billingPeriod, false)`
- `DEL loyalty:pending_redemption:{sessionId}` de Redis.
- `DEL loyalty:balance:{customerId}` de Redis para invalidar cache.
- Emitir evento SignalR `loyalty.points_redeemed` via SDD-007: `{ customerId, sessionId, pointsDeducted, newBalance, itemNameSnapshot }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Cualquier campo requerido ausente, tipo incorrecto o UUID inválido |
| 400 | `billingPeriod` no tiene formato `YYYY-MM` |
| 400 | `itemValueCop` es negativo |
| 401 | Header `X-Internal-Secret` ausente o incorrecto |
| 404 | `customerId` no existe en `customer_accounts` |
| 409 | `customerId` en reserva Redis no coincide con `customerId` del body |
| 409 | Balance insuficiente al ejecutar la deducción atómica (race condition) |

**Observabilidad**
- Log: `"loyalty.redemption.confirmed"` `{ customerId, sessionId, orderId, tenantId, pointsDeducted, newBalance, transactionId, redemptionId, billingPeriod, itemValueCop }`
- Log: `"loyalty.redemption.no_pending"` `{ sessionId, orderId }` — sin reserva, flujo normal
- Log: `"loyalty.redemption.idempotent"` `{ customerId, orderId, existingTransactionId }`
- Log error: `"loyalty.redemption.insufficient_balance"` `{ customerId, required, available }` — race condition
- Log error: `"loyalty.redemption.customer_mismatch"` `{ sessionId, reservationCustomerId, requestCustomerId }`
- Métrica: `loyalty.points_redeemed` (counter)
- Métrica: `loyalty.redemption_no_pending` (counter)

**Casos de Aceptación**
- [ ] Reserva válida de 200 pts, cliente con 1450 pts: descuenta 200, `newBalance=1250`, crea `loyalty_transactions` y `loyalty_redemptions` con `settled=false`
- [ ] Sin reserva pendiente en Redis: retorna 200 con `pointsDeducted: 0`, sin escrituras en DB
- [ ] Llamada idempotente con mismo `orderId` y reserva aún en Redis: retorna 200 con datos de transacción existente
- [ ] `X-Internal-Secret` incorrecto: retorna 401
- [ ] Cache `loyalty:balance:{customerId}` y `loyalty:pending_redemption:{sessionId}` eliminados tras deducción exitosa
- [ ] `loyalty_redemptions` creado con `settled=false` y `billingPeriod` correcto
- [ ] Race condition — balance insuficiente en UPDATE atómico: retorna 409

---

### GET /api/v1/loyalty/contract/{tenantId}

**Objetivo**
Retorna el contrato de fidelidad vigente del propio restaurante, incluyendo todos sus ítems canjeables activos e inactivos.

**Auth**
Restaurant Admin JWT (Bearer token con `role=restaurant_admin` y `tenantId` del JWT coincidiendo con el path param)

**Request**
- Path params:
  - `tenantId` UUID — identificador del restaurante
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "id": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "tenantName": "La Candelaria",
  "pointsRate": 1.50,
  "status": "active",
  "activatedAt": "2026-01-15T10:00:00Z",
  "notes": "Contrato inicial negociado en enero 2026.",
  "redeemableItems": [
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000001",
      "catalogItemId": "mongo_item_id_001",
      "itemNameSnapshot": "Limonada de Coco",
      "pointsCost": 200,
      "isActive": true
    },
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000002",
      "catalogItemId": "mongo_item_id_002",
      "itemNameSnapshot": "Café Americano",
      "pointsCost": 1600,
      "isActive": false
    }
  ]
}
```

**Validaciones**
- `tenantId`: debe ser UUID v4 válido
- El `tenantId` embebido en el JWT (`claims.tenantId`) debe coincidir exactamente con el `tenantId` del path param

**Reglas de Negocio**
- Un Restaurant Admin solo puede consultar el contrato de su propio tenant; no puede acceder al de otros.
- Consultar `loyalty:contract:{tenantId}` en Redis. Si no existe, leer `loyalty_contracts WHERE tenant_id = :tenantId` y poblar cache TTL 10min.
- Retornar **todos** los ítems de `loyalty_redeemable_items` del contrato (activos e inactivos), ordenados por `created_at ASC`.
- El campo `activatedBy` (UUID del platform admin que activó el contrato) **no** se expone en esta respuesta por privacidad operacional.
- Si no existe contrato para el `tenantId`: retornar 404.

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=restaurant_admin` |
| 403 | El `tenantId` del JWT no coincide con el `tenantId` del path |
| 404 | No existe contrato de fidelidad para ese `tenantId` |

**Observabilidad**
- Log: `"loyalty.contract.fetched_by_admin"` `{ tenantId, adminUserId, contractStatus, itemCount, cacheHit }`
- Métrica: `loyalty.contract_fetched_by_admin` (counter)

**Casos de Aceptación**
- [ ] Admin de "La Candelaria" consulta su propio contrato: retorna 200 con contrato activo e ítems (activos e inactivos)
- [ ] Admin intenta consultar contrato de otro tenant con `tenantId` distinto al del JWT: retorna 403
- [ ] Sin contrato para el tenant: retorna 404
- [ ] Cache hit en Redis: `cacheHit=true` en log
- [ ] JWT con `role=customer` intentando este endpoint: retorna 403
- [ ] Token ausente: retorna 401

---

### GET /api/v1/platform/loyalty-contracts

**Objetivo**
Lista todos los contratos de fidelidad de la plataforma con soporte de filtrado y paginación, para administradores de Mesto.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params: Ninguno
- Query params:
  - `page` INT — número de página (default: 1, min: 1)
  - `limit` INT — registros por página (default: 20, min: 1, max: 100)
  - `status` STRING — opcional; `active` o `paused`
  - `search` STRING — opcional; búsqueda parcial por nombre del tenant, máx 100 caracteres
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "id": "ctrct-1234-abcd-efgh-ijkl-000000000001",
      "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
      "tenantName": "La Candelaria",
      "pointsRate": 1.50,
      "status": "active",
      "activatedAt": "2026-01-15T10:00:00Z",
      "activeRedeemableItemsCount": 2
    },
    {
      "id": "ctrct-5678-efgh-ijkl-mnop-000000000002",
      "tenantId": "a1b2c3d4-0002-0002-0002-000000000002",
      "tenantName": "El Patio Grill",
      "pointsRate": 2.00,
      "status": "paused",
      "activatedAt": "2026-03-01T08:00:00Z",
      "activeRedeemableItemsCount": 1
    }
  ],
  "meta": {
    "page": 1,
    "limit": 20,
    "total": 2,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPrevPage": false
  }
}
```

**Validaciones**
- `page`: entero ≥ 1
- `limit`: entero entre 1 y 100
- `status`: si presente, debe ser `active` o `paused`
- `search`: si presente, máx 100 caracteres; se aplica `ILIKE` contra `tenants.name`

**Reglas de Negocio**
- `SELECT lc.*, t.name AS tenant_name, COUNT(lri.id) AS active_redeemable_items_count FROM loyalty_contracts lc JOIN tenants t ON t.id = lc.tenant_id LEFT JOIN loyalty_redeemable_items lri ON lri.contract_id = lc.id AND lri.is_active = true`.
- Aplicar `WHERE lc.status = :status` si se recibió el parámetro.
- Aplicar `AND t.name ILIKE '%' || :search || '%'` si se recibió el parámetro.
- `GROUP BY lc.id, t.name`.
- `ORDER BY lc.activated_at DESC`.
- Paginación offset-based.
- Esta operación no usa cache Redis.

**Errores**
| Código | Condición |
|---|---|
| 400 | `page` < 1 o no es entero |
| 400 | `limit` < 1, > 100 o no es entero |
| 400 | `status` con valor distinto a `active` o `paused` |
| 400 | `search` supera 100 caracteres |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |

**Observabilidad**
- Log: `"loyalty.contracts.listed"` `{ platformAdminId, page, limit, status, search, total }`
- Métrica: `loyalty.contracts_listed` (counter)

**Casos de Aceptación**
- [ ] Plataforma con 2 contratos sin filtros: retorna ambos con metadatos de paginación correctos
- [ ] Filtro `status=paused`: retorna solo contratos pausados
- [ ] Filtro `search=candelaria` (case-insensitive): retorna solo "La Candelaria"
- [ ] `limit=0`: retorna 400
- [ ] `status=inactive`: retorna 400
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401

---

### POST /api/v1/platform/loyalty-contracts

**Objetivo**
Crea un nuevo contrato de fidelidad para un tenant, activando la red de lealtad Mesto para ese restaurante.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "a1b2c3d4-0003-0003-0003-000000000003",
  "pointsRate": 1.50,
  "notes": "Contrato negociado en feria Alimentec 2026."
}
```

**Response 201**
```json
{
  "id": "ctrct-9999-abcd-1234-5678-000000000003",
  "tenantId": "a1b2c3d4-0003-0003-0003-000000000003",
  "tenantName": "Sazón de la Sabana",
  "pointsRate": 1.50,
  "status": "active",
  "activatedAt": "2026-06-07T15:30:00Z",
  "activatedBy": "plat-admin-uuid-001",
  "notes": "Contrato negociado en feria Alimentec 2026.",
  "redeemableItems": []
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido
- `pointsRate`: requerido, número decimal; entre 0.01 y 99.99 inclusive; se almacena con precisión `DECIMAL(5,2)`
- `notes`: opcional, string; máx 500 caracteres

**Reglas de Negocio**
- Verificar que el `tenantId` existe en la tabla `tenants`. Si no existe, retornar 404.
- Verificar unicidad: `loyalty_contracts.tenant_id` tiene constraint UNIQUE. Si ya existe contrato para ese tenant (en cualquier estado), retornar 409.
- `status` se establece como `'active'` en la creación.
- `activated_at = NOW()`.
- `activated_by = sub` del JWT del platform admin.
- Generar `id = gen_random_uuid()`.
- Los ítems canjeables se agregan posteriormente mediante `POST /platform/loyalty-contracts/{id}/items`.
- Invalidar `loyalty:contract:{tenantId}` en Redis si existiese por alguna razón.

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` ausente o no es UUID válido |
| 400 | `pointsRate` ausente, no es número, < 0.01 o > 99.99 |
| 400 | `notes` supera 500 caracteres |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | El `tenantId` no existe en la tabla `tenants` |
| 409 | Ya existe un contrato de fidelidad para ese `tenantId` |

**Observabilidad**
- Log: `"loyalty.contract.created"` `{ contractId, tenantId, tenantName, pointsRate, activatedBy }`
- Métrica: `loyalty.contract_created` (counter)

**Casos de Aceptación**
- [ ] Creación exitosa con `pointsRate=1.5` y `notes`: retorna 201 con `status='active'` y `redeemableItems: []`
- [ ] Tenant ya tiene contrato (cualquier estado): retorna 409
- [ ] `tenantId` no existe en `tenants`: retorna 404
- [ ] `pointsRate=0.00`: retorna 400
- [ ] `pointsRate=100.00`: retorna 400
- [ ] `notes` de 501 caracteres: retorna 400
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401

---

### GET /api/v1/platform/loyalty-contracts/{id}

**Objetivo**
Retorna el detalle completo de un contrato de fidelidad incluyendo todos sus ítems canjeables activos e inactivos.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params:
  - `id` UUID — identificador del contrato de fidelidad
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "id": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "tenantName": "La Candelaria",
  "pointsRate": 1.50,
  "status": "active",
  "activatedAt": "2026-01-15T10:00:00Z",
  "activatedBy": "plat-admin-uuid-001",
  "activatedByName": "Ana Gómez",
  "notes": "Contrato inicial negociado en enero 2026.",
  "redeemableItems": [
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000001",
      "catalogItemId": "mongo_item_id_001",
      "itemNameSnapshot": "Limonada de Coco",
      "pointsCost": 200,
      "isActive": true,
      "createdAt": "2026-01-20T09:00:00Z"
    },
    {
      "id": "9fbd1234-aaaa-bbbb-cccc-000000000002",
      "catalogItemId": "mongo_item_id_002",
      "itemNameSnapshot": "Café Americano",
      "pointsCost": 1600,
      "isActive": false,
      "createdAt": "2026-02-01T11:00:00Z"
    }
  ]
}
```

**Validaciones**
- `id`: debe ser UUID v4 válido

**Reglas de Negocio**
- JOIN `loyalty_contracts` con `tenants` para obtener `tenantName`.
- JOIN con `platform_users` para obtener `activatedByName` a partir de `activated_by`.
- Incluir **todos** los `loyalty_redeemable_items` del contrato (activos e inactivos), ordenados por `created_at ASC`.
- Esta operación no usa cache Redis.

**Errores**
| Código | Condición |
|---|---|
| 400 | `id` no es UUID válido |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | No existe contrato con ese `id` |

**Observabilidad**
- Log: `"loyalty.contract.detail_fetched"` `{ contractId, tenantId, platformAdminId, itemCount }`
- Métrica: `loyalty.contract_detail_fetched` (counter)

**Casos de Aceptación**
- [ ] Contrato existente con 2 ítems (1 activo, 1 inactivo): retorna 200 con todos los ítems y `activatedByName` resuelto
- [ ] `id` inexistente: retorna 404
- [ ] `id=not-a-uuid`: retorna 400
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401

---

### PATCH /api/v1/platform/loyalty-contracts/{id}

**Objetivo**
Modifica la tasa de puntos, el estado (activo/pausado) o las notas de un contrato de fidelidad existente.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params:
  - `id` UUID — identificador del contrato de fidelidad
- Query params: Ninguno
- Body:
```json
{
  "pointsRate": 2.00,
  "status": "paused",
  "notes": "Pausado por renegociación de condiciones comerciales Q3 2026."
}
```
> Todos los campos son opcionales pero al menos uno debe estar presente.

**Response 200**
```json
{
  "id": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "tenantName": "La Candelaria",
  "pointsRate": 2.00,
  "status": "paused",
  "activatedAt": "2026-01-15T10:00:00Z",
  "notes": "Pausado por renegociación de condiciones comerciales Q3 2026.",
  "updatedAt": "2026-06-07T16:00:00Z"
}
```

**Validaciones**
- `id`: debe ser UUID v4 válido
- Body: al menos uno de `pointsRate`, `status`, `notes` debe estar presente; body vacío `{}` retorna 400
- `pointsRate`: si presente, decimal entre 0.01 y 99.99
- `status`: si presente, debe ser `active` o `paused`
- `notes`: si presente, máx 500 caracteres; se acepta string vacío `""` para limpiar las notas

**Reglas de Negocio**
- Solo se actualizan los campos presentes en el body (PATCH parcial). Campos ausentes no se modifican.
- Si `status` cambia de `active` a `paused`: las nuevas solicitudes de `POST /loyalty/sessions/{sessionId}/redeem` y el endpoint `GET /catalog/{tenantId}/redeemable` retornarán la condición de contrato pausado desde ese momento. Las reservas pendientes en Redis (`loyalty:pending_redemption:{sessionId}`) que aún no hayan pasado por `confirm-redemption` serán honradas (el pago ya fue procesado o está en curso); no se cancelan retroactivamente.
- Si `status` cambia de `paused` a `active`: los clientes pueden volver a acumular y canjear de inmediato.
- Tras cualquier actualización exitosa: `DEL loyalty:contract:{tenantId}` en Redis.
- `updatedAt = NOW()` (campo `updated_at TIMESTAMPTZ` en `loyalty_contracts`; si no existe aún en el schema, este endpoint lo utiliza y debe añadirse en la migración correspondiente).

**Errores**
| Código | Condición |
|---|---|
| 400 | `id` no es UUID válido |
| 400 | Body vacío o sin campos válidos reconocidos |
| 400 | `pointsRate` presente pero fuera de rango (< 0.01 o > 99.99) o no es número |
| 400 | `status` presente pero valor distinto a `active` o `paused` |
| 400 | `notes` supera 500 caracteres |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | No existe contrato con ese `id` |

**Observabilidad**
- Log: `"loyalty.contract.updated"` `{ contractId, tenantId, platformAdminId, changedFields, previousStatus, newStatus, previousRate, newRate }`
- Métrica: `loyalty.contract_updated` (counter)
- Métrica: `loyalty.contract_paused` (counter) — solo cuando `status` cambia de `active` a `paused`
- Métrica: `loyalty.contract_activated` (counter) — solo cuando `status` cambia de `paused` a `active`

**Casos de Aceptación**
- [ ] Pausar contrato activo enviando solo `status=paused`: retorna 200 con `status=paused`, cache Redis invalidado
- [ ] Reactivar contrato pausado: retorna 200 con `status=active`
- [ ] Actualizar solo `pointsRate=2.0`: retorna 200 con nueva tasa, `status` sin cambios
- [ ] Actualizar solo `notes`: retorna 200 con nuevas notas, demás campos sin cambios
- [ ] Body `{}` vacío: retorna 400
- [ ] `pointsRate=0`: retorna 400
- [ ] `status=suspended`: retorna 400
- [ ] Contrato inexistente: retorna 404
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401

---

### POST /api/v1/platform/loyalty-contracts/{id}/items

**Objetivo**
Agrega un nuevo ítem canjeable al catálogo de un contrato de fidelidad, vinculando un ítem del catálogo MongoDB con un costo en puntos.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params:
  - `id` UUID — identificador del contrato de fidelidad
- Query params: Ninguno
- Body:
```json
{
  "catalogItemId": "mongo_item_id_003",
  "itemNameSnapshot": "Agua Mineral 500ml",
  "pointsCost": 80
}
```

**Response 201**
```json
{
  "id": "9fbd5678-aaaa-bbbb-cccc-000000000003",
  "contractId": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "catalogItemId": "mongo_item_id_003",
  "itemNameSnapshot": "Agua Mineral 500ml",
  "pointsCost": 80,
  "isActive": true,
  "createdAt": "2026-06-07T16:30:00Z"
}
```

**Validaciones**
- `catalogItemId`: requerido, string no vacío, máx 255 caracteres
- `itemNameSnapshot`: requerido, string no vacío, máx 150 caracteres
- `pointsCost`: requerido, entero > 0

**Reglas de Negocio**
- Verificar que el contrato con `id` existe en `loyalty_contracts`. Si no, retornar 404.
- Validar que `catalogItemId` existe en la colección MongoDB `catalogs` del tenant asociado al contrato (`loyalty_contracts.tenant_id`). Si no existe en MongoDB, retornar 422.
- Verificar unicidad dentro del contrato: `SELECT id FROM loyalty_redeemable_items WHERE contract_id = :id AND catalog_item_id = :catalogItemId AND is_active = true`. Si existe un registro activo con el mismo `catalogItemId`, retornar 409. (Se permite agregar el mismo ítem si la entrada anterior fue desactivada; en ese caso se crea una nueva fila.)
- `is_active = true` en la creación.
- `item_name_snapshot` se persiste tal como se recibe en el body; no se consulta MongoDB para resolverlo. La responsabilidad del nombre correcto es del caller (platform admin).
- `id = gen_random_uuid()`.
- Invalidar `loyalty:contract:{tenantId}` en Redis tras la inserción.

**Errores**
| Código | Condición |
|---|---|
| 400 | `catalogItemId` ausente o supera 255 caracteres |
| 400 | `itemNameSnapshot` ausente, vacío o supera 150 caracteres |
| 400 | `pointsCost` ausente, no es entero o ≤ 0 |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | No existe contrato con ese `id` |
| 409 | Ya existe un ítem activo con el mismo `catalogItemId` en este contrato |
| 422 | `catalogItemId` no existe en el catálogo MongoDB del tenant |

**Observabilidad**
- Log: `"loyalty.item.created"` `{ itemId, contractId, tenantId, catalogItemId, itemNameSnapshot, pointsCost, platformAdminId }`
- Métrica: `loyalty.redeemable_item_created` (counter)

**Casos de Aceptación**
- [ ] Ítem nuevo con `catalogItemId` válido en MongoDB: retorna 201 con `isActive=true`
- [ ] `catalogItemId` no existe en MongoDB del tenant: retorna 422
- [ ] `catalogItemId` ya existe como ítem activo en el contrato: retorna 409
- [ ] `catalogItemId` existía pero fue desactivado previamente: se permite, retorna 201 con nueva fila activa
- [ ] `pointsCost=0`: retorna 400
- [ ] `itemNameSnapshot` de 151 caracteres: retorna 400
- [ ] Contrato inexistente: retorna 404
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Cache `loyalty:contract:{tenantId}` invalidado tras creación exitosa
- [ ] Token ausente: retorna 401

---

### PATCH /api/v1/platform/loyalty-contracts/{id}/items/{itemId}

**Objetivo**
Activa o desactiva un ítem canjeable de un contrato de fidelidad.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params:
  - `id` UUID — identificador del contrato de fidelidad
  - `itemId` UUID — identificador del ítem canjeable
- Query params: Ninguno
- Body:
```json
{
  "isActive": false
}
```

**Response 200**
```json
{
  "id": "9fbd1234-aaaa-bbbb-cccc-000000000001",
  "contractId": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "catalogItemId": "mongo_item_id_001",
  "itemNameSnapshot": "Limonada de Coco",
  "pointsCost": 200,
  "isActive": false,
  "updatedAt": "2026-06-07T17:00:00Z"
}
```

**Validaciones**
- `id`: debe ser UUID v4 válido
- `itemId`: debe ser UUID v4 válido
- `isActive`: requerido, booleano (`true` o `false`); ausencia o tipo incorrecto retorna 400

**Reglas de Negocio**
- Verificar que el contrato con `id` existe. Si no, retornar 404.
- Verificar que `itemId` existe en `loyalty_redeemable_items` y que su `contract_id = :id`. Si no pertenece a ese contrato, retornar 404.
- Si el valor de `isActive` coincide con el estado actual: operación idempotente. Retornar 200 con los datos actuales sin ejecutar UPDATE. No invalida cache.
- Si el valor de `isActive` es distinto: ejecutar `UPDATE loyalty_redeemable_items SET is_active = :isActive, updated_at = NOW() WHERE id = :itemId`.
- Tras la actualización (si hubo cambio real): `DEL loyalty:contract:{tenantId}` en Redis.
- Las reservas pendientes en Redis (`loyalty:pending_redemption:{sessionId}`) que ya referencien este ítem no se cancelan automáticamente al desactivarlo; el `confirm-redemption` en curso las procesará normalmente. Nuevas solicitudes de canje no podrán seleccionar el ítem desactivado.

**Errores**
| Código | Condición |
|---|---|
| 400 | `id` o `itemId` no son UUID válidos |
| 400 | `isActive` ausente en body, o valor no es booleano |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | No existe contrato con ese `id` |
| 404 | No existe ítem con ese `itemId`, o no pertenece al contrato `id` |

**Observabilidad**
- Log: `"loyalty.item.updated"` `{ itemId, contractId, tenantId, previousIsActive, newIsActive, platformAdminId, changed }`
- Métrica: `loyalty.redeemable_item_deactivated` (counter) — solo cuando `isActive` cambia de `true` a `false`
- Métrica: `loyalty.redeemable_item_activated` (counter) — solo cuando `isActive` cambia de `false` a `true`

**Casos de Aceptación**
- [ ] Desactivar ítem activo: retorna 200 con `isActive=false`, `updatedAt` actualizado, cache Redis invalidado
- [ ] Activar ítem inactivo: retorna 200 con `isActive=true`, cache Redis invalidado
- [ ] Desactivar ítem ya inactivo: retorna 200 idempotente con `isActive=false`, sin UPDATE en DB, sin invalidación de cache
- [ ] Activar ítem ya activo: retorna 200 idempotente, sin cambios
- [ ] `itemId` no pertenece al contrato `id`: retorna 404
- [ ] `isActive` ausente en body: retorna 400
- [ ] `isActive="false"` (string en vez de booleano): retorna 400
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401

---

### GET /api/v1/platform/loyalty-contracts/{id}/settlement

**Objetivo**
Retorna el historial de canjes de un contrato agrupado por período de facturación, con detalle de ítems individuales y estado de settlement para el proceso de billing mensual.

**Auth**
Platform Admin JWT (Bearer token con `role=platform_admin`, emitido por SDD-001)

**Request**
- Path params:
  - `id` UUID — identificador del contrato de fidelidad
- Query params:
  - `period` STRING — opcional; filtra por período de facturación, formato `YYYY-MM` (regex: `^\d{4}-(0[1-9]|1[0-2])$`)
  - `settled` BOOLEAN — opcional; `true` o `false` como string en query param
  - `page` INT — número de página para la lista de redemptions individuales (default: 1, min: 1)
  - `limit` INT — registros por página (default: 20, min: 1, max: 100)
- Body: Ninguno

**Response 200**
```json
{
  "contractId": "ctrct-1234-abcd-efgh-ijkl-000000000001",
  "tenantId": "a1b2c3d4-0001-0001-0001-000000000001",
  "tenantName": "La Candelaria",
  "summaryByPeriod": [
    {
      "billingPeriod": "2026-05",
      "totalRedemptions": 34,
      "totalItemValueCop": 850000,
      "settled": true
    },
    {
      "billingPeriod": "2026-06",
      "totalRedemptions": 12,
      "totalItemValueCop": 290000,
      "settled": false
    }
  ],
  "redemptions": [
    {
      "id": "rdmp-5566-aabb-ccdd-eeff-001122334455",
      "transactionId": "txn-aabb1234-ccdd-eeff-0011-223344556677",
      "orderId": "eeee1111-aaaa-bbbb-cccc-dddddddd0001",
      "catalogItemId": "mongo_item_id_001",
      "itemValueCop": 14000,
      "billingPeriod": "2026-06",
      "settled": false,
      "createdAt": "2026-06-05T19:45:12Z"
    }
  ],
  "meta": {
    "page": 1,
    "limit": 20,
    "total": 12,
    "totalPages": 1,
    "hasNextPage": false,
    "hasPrevPage": false
  }
}
```

**Validaciones**
- `id`: debe ser UUID v4 válido
- `period`: si presente, formato `YYYY-MM` estricto (regex: `^\d{4}-(0[1-9]|1[0-2])$`); valores como `2026-13` o `2026-00` retornan 400
- `settled`: si presente, debe ser el string `"true"` o `"false"` (query params son strings)
- `page`: entero ≥ 1
- `limit`: entero entre 1 y 100

**Reglas de Negocio**
- Verificar que el contrato con `id` existe. Si no, retornar 404.
- **`summaryByPeriod`**: `SELECT billing_period, COUNT(*) AS total_redemptions, SUM(item_value_cop) AS total_item_value_cop, settled FROM loyalty_redemptions WHERE tenant_id = :tenantId GROUP BY billing_period, settled ORDER BY billing_period DESC`. Si `period` presente, agregar `AND billing_period = :period`. Si `settled` presente, agregar `AND settled = :settled`. El agrupamiento por `settled` implica que puede haber dos filas para el mismo período (ej. algunas settled y otras no); se consolidan en el cliente o se agrupan por período asumiendo que un período queda 100% settled o no. En el MVP, el job de settlement marca todas las filas de un período/tenant en una sola operación, por lo que settled es homogéneo por período.
- **`redemptions`** (lista paginada individual): `SELECT * FROM loyalty_redemptions WHERE tenant_id = :tenantId ORDER BY billing_period DESC, created_at DESC`, con filtros opcionales `AND billing_period = :period` y `AND settled = :settled`. `OFFSET = (page - 1) * limit`, `LIMIT = limit`.
- La `meta.total` corresponde al total de filas en `redemptions` con los filtros aplicados.
- Esta operación no usa cache Redis.

**Errores**
| Código | Condición |
|---|---|
| 400 | `id` no es UUID válido |
| 400 | `period` presente pero no cumple formato `YYYY-MM` o mes fuera de rango 01–12 |
| 400 | `settled` presente pero valor distinto a `"true"` o `"false"` |
| 400 | `page` < 1 o `limit` fuera de rango 1–100 |
| 401 | Token ausente, inválido o expirado |
| 403 | El JWT no tiene `role=platform_admin` |
| 404 | No existe contrato con ese `id` |

**Observabilidad**
- Log: `"loyalty.settlement.fetched"` `{ contractId, tenantId, platformAdminId, period, settled, totalRedemptions, totalItemValueCop }`
- Métrica: `loyalty.settlement_fetched` (counter)

**Casos de Aceptación**
- [ ] Contrato con redemptions en 2 períodos: `summaryByPeriod` tiene 2 entradas con totales correctos; `redemptions` paginada
- [ ] Filtro `period=2026-05`: `summaryByPeriod` y `redemptions` solo muestran mayo; `meta.total` refleja el filtro
- [ ] Filtro `settled=false`: retorna solo redemptions pendientes de settlement
- [ ] `period=2026-13`: retorna 400 (mes inválido)
- [ ] `period=2026-00`: retorna 400 (mes inválido)
- [ ] `settled=maybe`: retorna 400
- [ ] Contrato sin redemptions: retorna 200 con `summaryByPeriod: []`, `redemptions: []`, `total: 0`
- [ ] Contrato inexistente: retorna 404
- [ ] JWT con `role=restaurant_admin`: retorna 403
- [ ] Token ausente: retorna 401
