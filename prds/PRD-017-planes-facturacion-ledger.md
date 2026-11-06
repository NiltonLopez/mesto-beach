# PRD-017 — Planes, Facturación SaaS y Ledger Contable

> **📜 Documento heredado de Mesto.** Se escribió para la plataforma SaaS multi-tenant de la que
> Mesto Beach se separó en agosto de 2026 (**ADR-023**), y su contenido sigue vigente salvo en
> tres puntos que **manda la documentación nueva**:
> - **Moneda**: los importes de este documento están en pesos colombianos. Hoy todo entero monetario
>   son **céntimos de euro** (**ADR-031**); los nombres de campo llevan el sufijo `Cents`.
> - **Facturación**: la emite el TPV del local, no nosotros (**ADR-028**). Ignora aquí cualquier
>   referencia a pasarelas o autoridades fiscales colombianas.
> - **Ejemplos**: los platos, precios y direcciones son de la época de Mesto y no reflejan la carta
>   ni el local de Playa Demo.
>
> Lo específico de este cliente está en **PRD-018** y en los **ADR-023…035**.

## Metadata

| Campo | Valor |
|---|---|
| **ID** | PRD-017 |
| **Versión** | 1.1 |
| **Estado** | Implementado |
| **Fecha** | Junio 2026 |
| **Apps relacionadas** | `platform.mesto.app` (Mesto Ops — facturación, ledger, planes), `admin.mesto.app` (dueño — ve su plan/límites y estado de cuenta) |
| **Módulos de API** | Platform (planes + facturación), **Ledger** (nuevo — contabilidad), Identity/Sessions/Catalog (enforcement de límites), Orders (base de comisión), Loyalty (settlement) |
| **Depende de** | PRD-001 (visión/modelo de negocio), PRD-005 (pagos y comisión), PRD-009 (onboarding/creación de tenant), PRD-015 (portal back-office Ops) |
| **PRDs que dependen de este** | PRD-004 (configuración — los límites de plan gatean mesas/pisos/estaciones), PRD-012 (fidelización — settlement entra al ledger) |

---

## 1. Propósito y Objetivos

### Propósito

Definir cómo **Mesto le cobra al restaurante (tenant)** por usar la plataforma: el **catálogo de planes** (con sus límites y funciones), la **facturación mensual** según el plan, y un **módulo de ledger contable de doble partida** que lleve las cuentas por cobrar y por pagar del negocio (Mesto como empresa).

> **Alcance — qué NO es esto.** Esto es **billing B2B SaaS** (Mesto → restaurante). Es distinto de:
> - El módulo **Payments** (PRD-005): cobro al **cliente final** en la mesa (efectivo/digital, turnos de caja).
> - La **facturación electrónica DIAN** al consumidor final (fuera de alcance, futuro).
> El **único punto de contacto** con Payments/Orders es que el plan **comisión/híbrido** calcula un % sobre las ventas (subtotales de Orders).

### Objetivos

| # | Objetivo | Métrica de éxito |
|---|---|---|
| OBJ-01 | Todo tenant tiene un plan claro con límites y precio definidos al crearse | 100% de tenants con `planCode` válido |
| OBJ-02 | La factura mensual se genera automáticamente y es **inmutable y auditable** | Factura por tenant/período con numeración consecutiva e IVA |
| OBJ-03 | Los límites del plan se **hacen cumplir** (no solo se documentan) | Crear un recurso por encima del límite → rechazado con upsell |
| OBJ-04 | Todas las cuentas del negocio quedan en un **ledger balanceado** (D=C) | Σ débitos = Σ créditos en todo momento; estado de cuenta por tenant cuadra |
| OBJ-05 | Ops puede ver quién debe, cuánto, y registrar pagos | Lista de facturación + estado de cuenta por tenant |

---

## 2. Contexto y Problema que Resuelve

Hoy el "plan" del tenant es un **modo de cobro libre** (Ops teclea monto/comisión por tenant), no hay **catálogo** de planes, las facturas **no se persisten** (se calculan al vuelo, sin IVA ni numeración), **no se hacen cumplir** los límites, y **no existe contabilidad**: no hay forma de saber cuánto debe cada tenant en libros, ni registrar lo que Mesto debe (settlement de fidelización, comisiones de pasarela). Este PRD cierra esos vacíos.

| Actor | Qué hace |
|---|---|
| **Mesto Ops** | Define planes, factura, registra pagos de tenants, consulta el ledger y los estados de cuenta |
| **Dueño del restaurante** | Elige plan al onboarding; ve su plan, límites y estado de cuenta; recibe sus facturas |
| **Sistema** | Genera facturas mensuales + asientos contables; hace cumplir límites; expira trials |

---

## 3. Catálogo de Planes

Tres planes estándar de **suscripción fija** + el modo a medida para Enterprise. La **fidelización está en todos** (es marketing de adquisición para Mesto). Precios provisionales (ajustables según costo de infraestructura).

| Función / Límite | **Básico** | **Pro** | **Enterprise** |
|---|---|---|---|
| Pisos del local (`maxFloors`) | **1** | ilimitados | ilimitados |
| Estaciones KDS (`maxKdsStations`) | **2** (cocina + barra) | ilimitadas | ilimitadas |
| Mesas (`maxTables`) | 20 | ilimitadas | ilimitadas |
| Staff / usuarios (`maxStaff`) | 5 | ilimitado | ilimitado |
| Caja + KDS + Salón + Menú QR | ✅ | ✅ | ✅ |
| **Fidelización (loyalty)** | ✅ | ✅ | ✅ |
| Analítica (`analytics`) | básica | avanzada | avanzada |
| Branding del menú (`branding`) | — | ✅ | ✅ |
| Soporte / SLA | estándar | prioritario | dedicado |
| **Modo de cobro** | suscripción fija | suscripción fija | a medida (fijo / comisión / híbrido) |
| **Precio/mes (COP)** | $59.000 | $119.000 | a medida |

**Trial:** al crear el tenant arranca en **trial = nivel Pro, 14 días** (configurable). Al vencer: elige plan o pasa a `suspended`. El trial **no factura**.

**Modos de cobro** (se conservan del modelo actual; el plan define cuál aplica):
- `subscription` — mensualidad fija (Básico/Pro).
- `commission` — sin fijo, % sobre ventas digitales (Orders). Para casos de alta rotación.
- `hybrid` — fijo + comisión (Enterprise a medida).

---

## 4. Entitlements y Enforcement

Cada plan se traduce a un conjunto de **entitlements** (capacidades/límites). Es la **fuente de verdad** que el resto de módulos consulta.

```
PlanEntitlements {
  planCode: "basic" | "pro" | "enterprise"
  maxFloors: int | null      // null = ilimitado
  maxKdsStations: int | null
  maxTables: int | null
  maxStaff: int | null
  loyalty: bool
  branding: bool
  analytics: "basic" | "advanced"
}
```

**Enforcement (bloqueante):** al intentar crear/activar un recurso que excede el plan, el backend **rechaza** (HTTP 403 con código `plan_limit_exceeded` + el límite y el plan sugerido). El frontend muestra un **upsell** ("Sube a Pro para más pisos"). Recursos gateados:
- **Pisos** (Sessions): crear piso → si `floorsActuales >= maxFloors` → rechazo.
- **Estaciones KDS** (Catalog/Orders): crear estación → contra `maxKdsStations`.
- **Mesas** (Sessions): crear mesa → contra `maxTables`.
- **Staff** (Identity): invitar/activar staff → contra `maxStaff`.
- **Branding / Analítica avanzada / Loyalty**: la UI oculta/gatea la función según el entitlement (loyalty siempre on).

> El override de Enterprise puede ampliar/levantar límites por tenant sin cambiar el `planCode` base.

---

## 5. Facturación

### Modelo

Al **cierre de cada mes** se emite una **factura inmutable** por tenant activo (no trial), con:
- **Numeración consecutiva** (serie por entorno, p. ej. `MESTO-2026-000123`).
- **Líneas**: suscripción del plan (+ comisión del período si aplica) − descuentos/créditos.
- **Subtotal**, **IVA 19%**, **total**.
- **Período** (`YYYY-MM`), fecha de emisión, fecha de vencimiento.
- **Estado**: `issued` → `paid` / `partial` / `overdue` / `void`.

Una factura emitida **no se edita**; correcciones via **nota crédito** (descuento/crédito del período siguiente) o `void` con motivo (auditable).

### Cobro

- **V1: manual por Ops** — registra el pago recibido (referencia, monto, fecha) contra la factura. Reusa `RegisterPayment` (extendido para ligarse a una factura).
- **V2 (futuro):** pasarela (Wompi / Mercado Pago) para débito automático (tarjeta/PSE).

### Historias

```
US-1701 — Como Ops, al crear un tenant elijo su plan; el sistema fija sus límites y su precio.
US-1702 — Como sistema, al cierre de mes genero la factura de cada tenant activo con IVA y numeración.
US-1703 — Como Ops, veo la lista de facturación (quién debe, cuánto, estado) y el estado de cuenta por tenant.
US-1704 — Como Ops, registro el pago de un tenant y la factura pasa a pagada/parcial.
US-1705 — Como dueño, veo mi plan, mis límites y mi estado de cuenta en admin.
US-1706 — Como dueño, al intentar crear un 2º piso en Básico, el sistema me lo impide y me ofrece subir a Pro.
```

---

## 6. Ledger Contable (Doble Partida)

Módulo nuevo **`Ledger`** que registra **asientos** (journal entries) balanceados (Σ débitos = Σ créditos) sobre un **plan de cuentas mínimo**, alimentado por eventos de facturación, pagos, descuentos y settlement.

### Plan de cuentas mínimo (orientado a los flujos SaaS de Mesto)

| Código | Cuenta | Tipo | Naturaleza |
|---|---|---|---|
| 1110 | Bancos / Caja | Activo | Débito |
| 1305 | Cuentas por cobrar — tenants | Activo | Débito |
| 2335 | Cuentas por pagar (settlement loyalty / pasarela) | Pasivo | Crédito |
| 2408 | IVA por pagar | Pasivo | Crédito |
| 4170 | Ingresos por suscripción | Ingreso | Crédito |
| 4175 | Ingresos por comisión | Ingreso | Crédito |
| 5299 | Descuentos y cortesías | Gasto/contra-ingreso | Débito |
| 5305 | Gasto por fidelización (settlement) | Gasto | Débito |

### Asientos automáticos (todos D=C)

| Evento | Débito | Crédito |
|---|---|---|
| **Emitir factura** | 1305 CxC (total) | 4170/4175 Ingresos (subtotal) + 2408 IVA (impuesto) |
| **Pago del tenant** | 1110 Bancos | 1305 CxC |
| **Descuento / crédito** | 5299 Descuentos | 1305 CxC |
| **Anular factura (void)** | reverso del asiento de emisión | |
| **Settlement loyalty (Mesto debe al tenant)** | 5305 Gasto fidelización | 2335 CxP |
| **Pago del settlement** | 2335 CxP | 1110 Bancos |

### Reglas

- **Idempotencia:** un asiento por evento (clave: tipo+referencia). Re-procesar no duplica.
- **Inmutabilidad:** los asientos no se editan; se corrigen con asientos de reverso.
- **Multi-moneda:** no (solo **COP**).
- **Salidas:**
  - **Estado de cuenta por tenant**: movimientos (CxC) + saldo.
  - **Balances globales**: total CxC, total CxP, ingresos del período, IVA por pagar, descuentos.
  - **Comprobación de cuadre**: Σ débitos = Σ créditos (invariante verificable).

### Historias

```
US-1707 — Como sistema, cada factura/pago/descuento/settlement genera un asiento balanceado en el ledger.
US-1708 — Como Ops/contabilidad, consulto el estado de cuenta de un tenant (movimientos + saldo).
US-1709 — Como Ops/contabilidad, consulto balances globales (CxC, CxP, ingresos, IVA) por período.
US-1710 — Como sistema, garantizo que el ledger siempre cuadra (Σ D = Σ C) e idempotencia por evento.
```

---

## 7. Automatización

| Job | Frecuencia | Qué hace |
|---|---|---|
| **Cierre de facturación** | mensual (inicio de mes) | Genera factura del mes anterior por tenant activo + su asiento de emisión |
| **Expiración de trial** | diario | Tenants con `trialEndsAt < hoy` y sin plan pago → `suspended` (avisa al dueño) |
| **Vencimiento de factura** | diario | Facturas `issued` pasadas de su fecha de vencimiento → `overdue` |

---

## 8. Requisitos Funcionales (RF) y Técnicos (RT)

| ID | Requisito |
|---|---|
| RF-1701 | Catálogo de planes con `planCode`, precio, modo de cobro y entitlements; tenant referencia un `planCode`. |
| RF-1702 | Enforcement bloqueante de `maxFloors`/`maxKdsStations`/`maxTables`/`maxStaff` (403 `plan_limit_exceeded`). |
| RF-1703 | Generación mensual de factura inmutable con numeración consecutiva, líneas, IVA 19% y estado. |
| RF-1704 | Registro de pago de tenant ligado a factura; recalcular estado paid/partial. |
| RF-1705 | Módulo Ledger: asientos balanceados por evento (factura/pago/descuento/settlement), idempotentes. |
| RF-1706 | Estado de cuenta por tenant + balances globales por período. |
| RF-1707 | Override de límites/precio para Enterprise sin cambiar `planCode`. |
| RT-1701 | Ledger y facturas en PostgreSQL; el módulo maneja sus migraciones EF. |
| RT-1702 | Entitlements expuestos en el JWT/estado del tenant para que el front gatee sin round-trip extra. |
| RT-1703 | Reconciliar `TenantPlan` (Identity) + `plan` string (Platform) en el catálogo (`planCode`). |
| RT-1704 | La base de comisión sale de Orders (`SumSubtotalByTenantAndPeriod`), sin acoplar Payments. |

---

## 9. Criterios de Aceptación

- [x] Cada tenant referencia un `planCode` válido (default `pro`); sus entitlements se resuelven vía `IPlanEntitlementsReader`.
- [x] Crear un 2º piso en Básico → 403 `plan_limit_exceeded`; en Pro → OK. *(verificado E2E)*
- [x] El cierre mensual emite una factura con IVA 19% y número consecutivo (`MESTO-YYYY-NNNNNN`); inmutable tras emitir. *(verificado E2E)*
- [x] Registrar un pago la marca `paid`/`partial` y genera el asiento D Bancos / C CxC.
- [x] El estado de cuenta del tenant cuadra con la suma de sus facturas − pagos − descuentos (`GET /billing/me/plan` y `/ledger/.../statement`).
- [x] En todo momento, Σ débitos = Σ créditos del ledger (validado en `JournalEntry.Create`). *(verificado E2E)*
- [ ] Un settlement de fidelización genera CxP hacia el tenant y, al pagarse, lo salda. *(pendiente: Loyalty aún no invoca `ILedgerPostingService`)*
- [x] El trial vencido sin plan pago pasa a `suspended` y se avisa al dueño. *(verificado E2E)*

---

## 10. Fuera de Alcance (esta versión)

- Pasarela de cobro automático al tenant (Wompi/Mercado Pago) — V2.
- Facturación electrónica DIAN (al consumidor final y/o al tenant) — futuro.
- Multi-moneda; prorrateo intra-mes; planes anuales.
- Estados financieros completos (balance general / P&L formal) — el ledger deja la base, pero el reporting contable formal es posterior.

---

*Versión 1.1 — Junio 2026 — **Implementado** (catálogo de planes + entitlements + enforcement bloqueante, facturación mensual con IVA + numeración, módulo Ledger de doble partida, jobs programados y UI Ops/admin). Único pendiente: el settlement de fidelización (Loyalty) hacia el ledger. Define el modelo de negocio B2B de Mesto; reemplaza el "plan libre por tenant" y extiende PRD-005. Detalle técnico en SDD-009 (Platform) y SDD-010 (Ledger).*
