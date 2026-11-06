# SDD-010 — Ledger (Contabilidad)

> **v1.0 — Junio 2026 — implementado.** Módulo nuevo. Libro contable de **doble partida** del negocio Mesto: cuentas por cobrar (tenants) y por pagar (settlement de fidelización / pasarela), alimentado por eventos de facturación, pagos, descuentos y void. Schema `ledger` (`ledger_accounts` con seed de 8 cuentas, `journal_entries`, `journal_lines`) y 4 endpoints GET en código; el balance Σ D=C se valida en `JournalEntry.Create`. Pendiente: el settlement de Loyalty aún no invoca el ledger. Diseñado en PRD-017.

## Servicio
**Módulo**: Ledger (Contabilidad interna de Mesto)
**Base path**: `/api/v1/ledger`
**Responsabilidad**: Registrar **asientos contables balanceados** (Σ débitos = Σ créditos) sobre un plan de cuentas mínimo, y exponer **estado de cuenta por tenant** + **balances globales**. Es la fuente de verdad contable; recibe eventos del módulo Platform (facturas/pagos/descuentos) y de Loyalty (settlement). Solo lectura para Ops; las escrituras las dispara el sistema, no el usuario.

**PRDs de referencia**: PRD-017 (Planes, Facturación SaaS y Ledger)

**Auth**: Platform Admin JWT, roles `billing` o `admin` (lectura). No hay endpoints de escritura pública: los asientos se crean vía `ILedgerPostingService` (interfaz en `Mesto.Shared`) invocado por handlers de Platform/Loyalty tras persistir el evento de negocio, **idempotente por `(entry_type, reference)`**.

**Dependencias**:
- PostgreSQL (propio): `ledger_accounts`, `journal_entries`, `journal_lines`. El módulo maneja sus migraciones EF (`Mesto.Ledger.Infrastructure`).
- Proyección read-only cross-module: `tenants` (nombre/slug para el estado de cuenta), `ExcludeFromMigrations`.
- No usa Redis. Moneda única: **COP** (enteros).

---

## PostgreSQL Schema

**`ledger_accounts`** — plan de cuentas (semilla fija, ver §Plan de cuentas)
```sql
code            VARCHAR(8) PRIMARY KEY,        -- '1305', '2408', ...
name            VARCHAR(120) NOT NULL,
type            VARCHAR(16) NOT NULL,          -- 'ASSET' | 'LIABILITY' | 'INCOME' | 'EXPENSE'
normal_side     VARCHAR(6) NOT NULL,           -- 'DEBIT' | 'CREDIT'
active          BOOLEAN NOT NULL DEFAULT true
```

**`journal_entries`** — cabecera del asiento (un evento de negocio = un asiento)
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
tenant_id       UUID,                          -- nullable: asientos no ligados a un tenant
entry_type      VARCHAR(32) NOT NULL,          -- 'INVOICE_ISSUED' | 'PAYMENT_RECEIVED' | 'DISCOUNT_APPLIED' | 'INVOICE_VOIDED' | 'LOYALTY_SETTLEMENT' | 'SETTLEMENT_PAID'
reference       VARCHAR(80) NOT NULL,          -- idempotencia: id de factura/pago/descuento/settlement
period          CHAR(7),                       -- 'YYYY-MM' (nullable)
description     VARCHAR(300),
amount_cop      BIGINT NOT NULL,               -- monto "principal" del asiento (para listados); el detalle está en las líneas
posted_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
created_by      UUID,                          -- ops user o null (sistema)
UNIQUE (entry_type, reference)                 -- idempotencia: un asiento por evento
```

**`journal_lines`** — líneas del asiento (≥ 2; suman 0 entre débitos y créditos)
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
entry_id        UUID NOT NULL REFERENCES journal_entries(id) ON DELETE CASCADE,
account_code    VARCHAR(8) NOT NULL REFERENCES ledger_accounts(code),
debit_cop       BIGINT NOT NULL DEFAULT 0,
credit_cop      BIGINT NOT NULL DEFAULT 0,
CHECK (debit_cop >= 0 AND credit_cop >= 0),
CHECK ( (debit_cop = 0) <> (credit_cop = 0) )  -- cada línea es débito XOR crédito
```
Índices: `journal_entries(tenant_id, posted_at)`, `journal_entries(period)`, `journal_lines(entry_id)`, `journal_lines(account_code)`.

> **Invariante (verificado al postear):** para cada asiento, `Σ debit_cop = Σ credit_cop`. El `ILedgerPostingService` rechaza un asiento desbalanceado (no se persiste).

### Plan de cuentas (semilla)

| code | name | type | normal_side |
|---|---|---|---|
| 1110 | Bancos / Caja | ASSET | DEBIT |
| 1305 | Cuentas por cobrar — tenants | ASSET | DEBIT |
| 2335 | Cuentas por pagar | LIABILITY | CREDIT |
| 2408 | IVA por pagar | LIABILITY | CREDIT |
| 4170 | Ingresos por suscripción | INCOME | CREDIT |
| 4175 | Ingresos por comisión | INCOME | CREDIT |
| 5299 | Descuentos y cortesías | EXPENSE | DEBIT |
| 5305 | Gasto por fidelización (settlement) | EXPENSE | DEBIT |

---

## Posting — `ILedgerPostingService` (interno, no HTTP)

Lo invocan handlers de Platform/Loyalty **dentro de la misma transacción** del evento de negocio. Idempotente por `(entry_type, reference)`: si ya existe, no-op (evita doble asiento al reprocesar).

```csharp
Task PostAsync(LedgerEntry entry, CancellationToken ct);
// LedgerEntry { TenantId?, EntryType, Reference, Period?, Description, Lines: [{AccountCode, Debit, Credit}] }
// Valida: ≥2 líneas, Σdébitos==Σcréditos>0, cuentas existen. Lanza si no balancea.
```

**Asientos por evento** (todos balanceados):

| EntryType | Débito | Crédito | Disparado por |
|---|---|---|---|
| `INVOICE_ISSUED` | 1305 CxC (total c/IVA) | 4170/4175 Ingresos (subtotal) **+** 2408 IVA | cierre mensual de facturación (Platform) |
| `PAYMENT_RECEIVED` | 1110 Bancos | 1305 CxC | `RegisterPayment` (Platform) |
| `DISCOUNT_APPLIED` | 5299 Descuentos | 1305 CxC | `ApplyDiscount` (Platform) |
| `INVOICE_VOIDED` | reverso de `INVOICE_ISSUED` | reverso | anular factura (Platform) |
| `LOYALTY_SETTLEMENT` | 5305 Gasto fidelización | 2335 CxP | settlement de contrato (Loyalty) |
| `SETTLEMENT_PAID` | 2335 CxP | 1110 Bancos | pago del settlement (Platform/Loyalty) |

---

## Endpoints (lectura)

### GET /api/v1/ledger/accounts
**Objetivo**: Plan de cuentas (para UI/reportes).
**Auth**: Platform Admin (`billing`/`admin`).
**Response 200**: `{ "accounts": [{ "code","name","type","normalSide" }] }`

---

### GET /api/v1/ledger/tenants/{tenantId}/statement
**Objetivo**: **Estado de cuenta** de un tenant — movimientos de su CxC (1305) y saldo.
**Auth**: Platform Admin (`billing`/`admin`).
**Request**: path `{tenantId}`; query `from` (`YYYY-MM`, opc.), `to` (opc.).
**Response 200**
```json
{
  "tenantId": "…", "tenantName": "Restaurante X",
  "openingBalanceCop": 0,
  "movements": [
    { "entryId":"…", "type":"INVOICE_ISSUED", "reference":"MESTO-2026-000123",
      "period":"2026-05", "description":"Factura mayo 2026",
      "debitCop": 119000, "creditCop": 0, "balanceCop": 119000, "postedAt":"…" },
    { "entryId":"…", "type":"PAYMENT_RECEIVED", "reference":"pay-abc",
      "debitCop": 0, "creditCop": 119000, "balanceCop": 0, "postedAt":"…" }
  ],
  "closingBalanceCop": 0
}
```
**Reglas de Negocio**: solo movimientos que tocan la cuenta `1305` del tenant; `balanceCop` corrido; `closingBalanceCop` = lo que el tenant **debe** (positivo) en libros.
**Errores**: 400 tenantId inválido · 401/403 · 404 tenant no encontrado.

---

### GET /api/v1/ledger/balances
**Objetivo**: **Balances globales** del negocio por período (tablero contable de Ops).
**Auth**: Platform Admin (`billing`/`admin`).
**Request**: query `period` (`YYYY-MM`, default mes actual).
**Response 200**
```json
{
  "period": "2026-05",
  "accountsReceivableCop": 1830000,   // 1305 saldo (lo que deben los tenants)
  "accountsPayableCop": 240000,       // 2335 saldo (lo que Mesto debe)
  "incomeSubscriptionCop": 1190000,   // 4170 del período
  "incomeCommissionCop": 320000,      // 4175 del período
  "vatPayableCop": 286900,            // 2408 del período
  "discountsCop": 50000,              // 5299 del período
  "balanced": true                    // Σ débitos == Σ créditos (invariante)
}
```
**Reglas de Negocio**: agrega `journal_lines` por cuenta; `balanced` = verificación global Σ D == Σ C.
**Errores**: 400 period inválido · 401/403.

---

### GET /api/v1/ledger/entries
**Objetivo**: Diario — lista paginada de asientos (auditoría).
**Auth**: Platform Admin (`billing`/`admin`).
**Request**: query `tenantId?`, `type?`, `period?`, `page`, `pageSize`.
**Response 200**: `{ "items": [{ "id","tenantId","entryType","reference","period","description","amountCop","postedAt","lines":[{ "accountCode","debitCop","creditCop" }] }], "total","page","pageSize" }`

---

## Casos de Aceptación

- [ ] El plan de cuentas se siembra en la primera migración (8 cuentas).
- [ ] Postear una factura genera un asiento `INVOICE_ISSUED` con D 1305 = C(4170/4175)+C 2408 y balancea.
- [ ] Re-postear el mismo `(entry_type, reference)` no duplica (idempotente).
- [ ] Un asiento desbalanceado es rechazado (no se persiste) y el negocio falla con error claro.
- [ ] El estado de cuenta de un tenant cuadra: saldo = Σ facturas − Σ pagos − Σ descuentos.
- [ ] `GET /balances` reporta `balanced=true` en todo momento (Σ D == Σ C global).
- [ ] Un `LOYALTY_SETTLEMENT` crea CxP (2335) y un `SETTLEMENT_PAID` lo salda contra Bancos.

---

## Notas de implementación

- **Transaccionalidad:** el asiento se postea en la **misma transacción** que el evento de negocio (factura/pago) para no quedar inconsistente. Si el módulo no comparte DbContext, se usa el patrón outbox o un `TransactionScope`/UoW compartido; a definir en desarrollo.
- **Idempotencia** por `UNIQUE(entry_type, reference)` — clave para reprocesos del cron de facturación.
- **Sin edición:** correcciones via asiento de reverso (`INVOICE_VOIDED`), nunca update/delete de líneas posteadas.
- **Reportes formales** (balance general / P&L) quedan fuera de esta versión; el ledger deja la base de datos para construirlos después.
