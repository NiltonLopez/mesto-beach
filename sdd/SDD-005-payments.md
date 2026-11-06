# SDD-005 — Payments Module

## Metadata

| Campo | Valor |
|---|---|
| **ID** | SDD-005 |
| **Versión** | 1.0 |
| **Estado** | Aprobado |
| **Fecha** | Junio 2026 |
| **PRD de referencia** | PRD-005 — Pagos y Facturación |
| **Módulo backend** | `Mesto.Payments.*` |
| **Depende de** | SDD-003 (Sessions), SDD-004 (Orders) |

---

## 1. Alcance MVP

| Funcionalidad | MVP | v2 |
|---|---|---|
| Pago en efectivo vía cajero | ✅ | — |
| Split bill (N partes iguales o por monto) | ✅ | — |
| Propina configurable (% o monto libre) | ✅ | — |
| Estado de pagos por sesión | ✅ | — |
| Configuración de pagos por tenant | ✅ | — |
| Pago digital (Wompi / MercadoPago) | ❌ stub | ✅ |
| Webhooks de pasarela | ❌ stub 200 | ✅ |
| Facturación DIAN | ❌ | ✅ |
| Comisión Mesto (cálculo) | ✅ calculado | — |
| Comisión Mesto (transferencia) | ❌ | ✅ |

---

## 2. Modelo de Dominio

### 2.1 Entidades

#### `Payment`

| Campo | Tipo | Descripción |
|---|---|---|
| `Id` | Guid PK | UUID generado en backend |
| `TenantId` | Guid | FK implícita |
| `SessionId` | Guid | FK a sessions |
| `Method` | PaymentMethod | `Cash` \| `Digital` |
| `AmountCents` | int | Monto pagado sin propina (euros en céntimos, enteros) |
| `TipCents` | int | Propina en céntimos (0 si sin propina) |
| `TotalCents` | int | AmountCents + TipCents |
| `CommissionCents` | int | Comisión Mesto calculada en céntimos (no ejecutada en MVP) |
| `GatewayReference` | string? | Referencia externa de pasarela (null en cash) |
| `GatewayType` | string? | `"Wompi"` \| `"MercadoPago"` \| null |
| `Status` | PaymentStatus | `Pending` \| `Completed` \| `Failed` \| `Refunded` |
| `SplitIndex` | int? | 1-based. null = pago completo |
| `SplitTotal` | int? | Número total de partes del split |
| `PayerLabel` | string? | [v1.x] A quién corresponde el pago (nombre de la persona en el cobro "por persona"). Solo display. |
| `ProcessedBy` | Guid | StaffId del cajero |
| `CreatedAt` | DateTime | UTC |
| `CompletedAt` | DateTime? | UTC |

> **[v1.x] Factura visible al cliente + quién pagó:** `GET /payments/sessions/{id}/status` es `AllowAnonymous` (el cliente ve la factura compartida de su mesa: total, pagado, pendiente, `isFullyPaid` y la lista de pagos con `payerLabel`). El cobro **"por persona"** del cajero envía `payerLabel` = nombre de la persona; así el menú ("Mi cuenta") marca a cada comensal como **✓ Pagado** cuando hay un pago con su nombre (o cuando la cuenta está saldada).

**Reglas de dominio:**
- Todos los importes son **enteros en céntimos de euro** (v2.0, ADR-031; antes pesos con sufijo `Cop`). `1450` = 14,50 €
- `TotalCents = AmountCents + TipCents`
- `CommissionCents = Round(AmountCents * CommissionPct / 100)` (calculado al completar)
- Cash payments se crean en estado `Completed` directamente (sin `Pending` intermedio)
- `SplitIndex` entre 1 y `SplitTotal`. null = pago único completo
- Los pagos nunca se eliminan físicamente

#### `TenantPaymentConfig`

| Campo | Tipo | Descripción |
|---|---|---|
| `TenantId` | Guid PK | 1:1 con tenant |
| `DigitalPaymentsEnabled` | bool | Siempre `false` en MVP |
| `GatewayType` | string? | null \| `"Wompi"` \| `"MercadoPago"` |
| `GatewayPublicKey` | string? | Clave pública de la pasarela |
| `GatewayPrivateKeyRef` | string? | Nombre del secreto en Key Vault |
| `TipEnabled` | bool | Si se muestra propina al cliente |
| `TipOptions` | List\<int\> | Porcentajes, ej: `[5, 10, 15]` |
| `CommissionPct` | decimal | Porcentaje de comisión Mesto (configurable por Platform Admin) |
| `UpdatedAt` | DateTime | UTC |

### 2.2 Enums

```csharp
public enum PaymentMethod { Cash, Digital }
public enum PaymentStatus { Pending, Completed, Failed, Refunded }
```

### 2.3 Estado de la sesión vs. pagos

| Condición | Acción |
|---|---|
| **Total pagado ≥ total de la cuenta** (Σ `AmountCents` completados ≥ orders total) | Cerrar sesión automáticamente |
| Último split de N completado | Cerrar sesión automáticamente |
| Pago digital completado vía webhook | Cerrar sesión automáticamente |
| Pago fallido | No cerrar sesión; sesión permanece abierta |

> **[v1.x] Cierre por monto acumulado:** un pago cash **sin split** ya **no cierra** la sesión por sí solo; el cierre ocurre cuando el **total pagado cubre el total de la cuenta**. Esto habilita **abonos parciales** (p. ej. "cobrar por productos": el cajero selecciona ítems y cobra ese monto), dejando la sesión abierta hasta saldarla. El handler computa `Σ AmountCents (completados) ≥ ISessionOrderReader.GetTotalAsync().TotalCents`. (Antes, `IsFullPayment = SplitIndex is null` cerraba en cualquier pago sin split.) El `OrderSummaryDto` de `/orders` ahora incluye `items` para que la cuenta muestre el detalle de productos.

---

## 3. API Endpoints

### 3.1 `POST /api/v1/payments/cash`

Registra un pago en efectivo. Requiere `StaffAdminOrCajero`.

**Body:**
```json
{
  "sessionId": "uuid",
  "amountCents": 8400,
  "tipCents": 840,
  "splitIndex": null,
  "splitTotal": null
}
```

**Respuestas:**
- `201` — Pago registrado; `{ paymentId, status: "COMPLETED", sessionClosed: bool }`
- `400` — Validación fallida (sessionId vacío, amountCents <= 0, etc.)
- `409` — Sesión ya cerrada o pago duplicado (mismo splitIndex ya registrado)
- `404` — Sesión no encontrada

### 3.2 `GET /api/v1/payments/sessions/{sessionId}/status`

Devuelve el estado de pagos de una sesión. Staff o cliente de la sesión.

**Respuesta 200:**
```json
{
  "sessionId": "uuid",
  "ordersTotalCents": 8400,
  "payments": [
    { "id": "uuid", "amountCents": 8400, "tipCents": 840, "totalCents": 9240,
      "method": "CASH", "status": "COMPLETED", "splitIndex": null,
      "splitTotal": null, "processedAt": "2026-06-07T..." }
  ],
  "totalPaidCents": 9240,
  "remainingCents": 0,
  "isFullyPaid": true
}
```

### 3.3 `GET /api/v1/payments/config`

Devuelve la configuración de pagos del tenant. `StaffJwt`.

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "digitalPaymentsEnabled": false,
  "tipEnabled": true,
  "tipOptions": [5, 10, 15],
  "gatewayType": null
}
```

### 3.4 `PUT /api/v1/payments/config`

Actualiza la configuración de pagos. `StaffAdmin`.

**Body:**
```json
{
  "tipEnabled": true,
  "tipOptions": [5, 10, 15]
}
```

### 3.5 `POST /api/v1/payments/webhook/wompi` *(stub MVP)*

Responde `200 OK` con `{"message": "Digital payments available in v2."}`.

### 3.6 `POST /api/v1/payments/webhook/mercadopago` *(stub MVP)*

Responde `200 OK` con `{"message": "Digital payments available in v2."}`.

---

## 4. Cross-Module Integration

### 4.1 Cerrar sesión tras pago completado

`Payments.Application.Common.ISessionCloser` → implementado en `Payments.Infrastructure` usando `ISender` para despachar `Sessions.Application.Commands.CloseSessionCommand`.

```csharp
public interface ISessionCloser
{
    Task<bool> CloseAsync(Guid tenantId, Guid sessionId, Guid requesterId, CancellationToken ct);
}
```

- `CloseAsync` envía `CloseSessionCommand { ClosureReason = "PAYMENT_COMPLETED" }`
- Devuelve `false` si la sesión no existe o ya estaba cerrada (sin error)

### 4.2 Leer total de pedidos de la sesión

`ISessionOrderReader` en `Payments.Application` → implementado mediante proyección directa en `PaymentsDbContext`:

```csharp
public interface ISessionOrderReader
{
    Task<int> GetOrdersTotalCentsAsync(Guid sessionId, CancellationToken ct);
}
```

La implementación usa una proyección de solo lectura de la tabla `orders`+`order_items` excluida de migraciones.

### 4.3 Verificar si sesión está activa

Proyección de solo lectura de la tabla `sessions` en `PaymentsDbContext`, excluida de migraciones.

---

## 5. Persistencia

### 5.1 Tabla `payments`

```sql
CREATE TABLE payments (
  id               UUID PRIMARY KEY,
  tenant_id        UUID NOT NULL,
  session_id       UUID NOT NULL,
  method           VARCHAR(10) NOT NULL,     -- 'Cash' | 'Digital'
  amount_cents     INT NOT NULL,
  tip_cents        INT NOT NULL DEFAULT 0,
  total_cents      INT NOT NULL,
  commission_cents INT NOT NULL DEFAULT 0,
  gateway_reference VARCHAR(200),
  gateway_type     VARCHAR(30),
  status           VARCHAR(10) NOT NULL,     -- 'Completed' | 'Failed' | 'Refunded'
  split_index      INT,
  split_total      INT,
  processed_by     UUID NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  completed_at     TIMESTAMPTZ
);

CREATE INDEX idx_payments_session ON payments(session_id);
CREATE INDEX idx_payments_tenant  ON payments(tenant_id, created_at);
CREATE UNIQUE INDEX idx_payments_split ON payments(session_id, split_index)
  WHERE split_index IS NOT NULL;
```

### 5.2 Tabla `tenant_payment_configs`

```sql
CREATE TABLE tenant_payment_configs (
  tenant_id              UUID PRIMARY KEY,
  digital_payments_enabled BOOLEAN NOT NULL DEFAULT false,
  gateway_type           VARCHAR(20),
  gateway_public_key     VARCHAR(300),
  gateway_private_key_ref VARCHAR(200),
  tip_enabled            BOOLEAN NOT NULL DEFAULT true,
  tip_options            JSONB NOT NULL DEFAULT '[5, 10, 15]',
  commission_pct         DECIMAL(5,2) NOT NULL DEFAULT 3.5,
  updated_at             TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

---

## 6. Validaciones

### `RegisterCashPaymentCommand`
- `SessionId` NotEmpty
- `AmountCents` > 0
- `TipCents` >= 0
- Si `SplitIndex` != null → `SplitTotal` != null && `SplitTotal` >= 2 && `SplitIndex` between 1 and `SplitTotal`
- Un split duplicado (mismo `SessionId` + `SplitIndex`) → 409 Conflict

### `UpsertPaymentConfigCommand`
- `TipOptions` max 5 items, cada uno entre 1 y 100
- `CommissionPct` solo modificable por PlatformAdmin (ignorado si lo envía staff regular)

---

## 7. Decisiones Clave

| Decisión | Razón |
|---|---|
| Cash payment → `Completed` directamente | No hay estado intermedio para efectivo; se registra cuando el cajero confirma el cobro |
| El cajero provee `amountCents` | El cajero es actor de confianza; para MVP la validación contra total de órdenes es informativa, no bloqueante |
| Gateway webhooks como stubs | Wompi/MercadoPago en v2; evita integración prematura con pasarelas externas |
| `GatewayPrivateKeyRef` apunta a Key Vault | Nunca se almacena el secreto en PostgreSQL directamente |
| `CommissionCents` calculado pero no ejecutado | El mecanismo de transferencia a Mesto es v2 |
| Split bill via `SplitIndex`/`SplitTotal` | Modelo simple sin entidades adicionales; el índice es único por sesión |

---

*SDD-005 — Payments Module*
*Versión 1.0 — Junio 2026*
