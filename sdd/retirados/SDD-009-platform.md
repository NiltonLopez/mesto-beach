# SDD-009 — Platform

> **v1.1 — Junio 2026** — Nuevo `POST /api/v1/platform/tenants/{tenantId}/activate` (transición `PENDING → TRIAL`), que completa la acción "Activar cuenta manualmente" prevista en PRD-015 §9.
> **v1.2 — Junio 2026** — Nuevo `POST /api/v1/platform/tenants/{tenantId}/reset-admin-password` (Ops genera enlace de reset para el admin propietario de un tenant).
> **v1.3 — Junio 2026** — **Catálogo de planes + entitlements + enforcement** y **facturas mensuales inmutables (IVA 19%)** (PRD-017). El `plan` libre por tenant pasa a referenciar un `planCode` del catálogo; los asientos contables viven en el módulo **Ledger** (SDD-010).
> **v1.4 — Junio 2026 — implementado.** Planes, facturas, enforcement y **jobs de facturación** (`POST /platform/jobs/{billing-close,expire-trials,mark-overdue}`, automatizados por el `BillingScheduler`) están en código. Endpoint tenant-facing `GET /api/v1/billing/me/plan` (StaffAdmin) para que el dueño vea su plan/límites/estado de cuenta. La contabilización al Ledger es **idempotente por `(entry_type, reference)`** (no requiere transacción distribuida).

## Servicio
**Módulo**: Platform (Back-Office Ops)  
**Base path**: `/api/v1/platform`  
**Responsabilidad**: Portal de gestión interna de Mesto para el equipo Ops. Gestión de tenants, billing, CRM de trials, comunicaciones, monitoreo, equipo Ops y contratos de fidelización. Todos los endpoints requieren Platform Admin JWT.

**PRDs de referencia**: PRD-015 (Portal Back-Office), PRD-009 (Onboarding)

**Roles del equipo Ops** (acumulables, stored in `platform_user_roles`):
- `soporte`: ver tenants, CRM trials, impersonar, acciones de soporte, ver billing
- `billing`: cambiar plan, descuentos, reembolsos, registrar pagos, marcar conversión
- `admin`: todo lo anterior + gestionar equipo, mantenimiento, config, audit log global, contratos lealtad

**Dependencias**:
- PostgreSQL: `tenants`, `trial_pipeline`, `platform_payments`, `platform_discounts`, **`plans`** [v1.3] (catálogo), **`platform_invoices`** + **`platform_invoice_lines`** [v1.3], `announcements`, `platform_audit_log`, `alerts`, `platform_users`, `platform_user_roles`, **`customer_crm`** (propia: anotaciones Ops). Proyecciones cross-module (read-only): `customer_accounts`, `orders`, `order_items`, `tenants`.
- **Módulo Ledger (SDD-010)** [v1.3]: al emitir factura / registrar pago / aplicar descuento / anular, Platform invoca `ILedgerPostingService` (interfaz en `Mesto.Shared`) para postear el asiento contable tras persistir el evento; **idempotente por `(entry_type, reference)`** (re-procesar no duplica).
- Redis: `ops_roles:{opsUserId}` TTL 8h, `maintenance:*`, `alerts:*`, `impersonation:*`
- SDD-001 Identity: ImpersonationToken generation

## Clientes (CRM) — `/api/v1/platform/customers` [v1.x]

CRM **cross-tenant** de las cuentas de cliente de la red de lealtad Mesto. Auth: Platform Admin JWT (roles `soporte`/`billing`/`admin`). Métricas calculadas al vuelo desde `orders.customer_account_id` + `order_items`; las anotaciones (tags/notas) viven en `customer_crm`.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/v1/platform/customers?search&segment&sort&page&limit` | Lista con métricas y segmento. `sort` ∈ {recent, spent, visits, name}. |
| GET | `/api/v1/platform/customers/{id}` | Ficha: perfil, métricas, restaurantes favoritos, pedidos recientes, lealtad, tags, notas. |
| POST | `/api/v1/platform/customers/{id}/notes` | `{text}` — agrega nota de Ops. |
| PATCH | `/api/v1/platform/customers/{id}/tags` | `{tags:[...]}` — fija los tags. |

**Métricas por cliente**: # pedidos, gasto total (Σ `item_price_snapshot_cop × quantity` de sus pedidos), ticket promedio, visitas (sesiones distintas), última actividad, restaurantes favoritos (top por # pedidos).
**Segmento** (regla): `inactivo` si última actividad > 60 días; `vip` si gasto ≥ 200.000 COP; `recurrente` si visitas ≥ 3; si no, `nuevo`.
**`customer_crm`**: `customer_account_id` (PK), `tags` (jsonb), `notes` (jsonb `[{text, createdBy, createdAt}]`), `updated_at`. Se crea on-demand al anotar.
- SDD-007 Notifications: announcement emails
- SDD-008 Loyalty: loyalty contract management (re-exposed here)

**Tabla adicional requerida — `platform_discounts`**:
```sql
id UUID PK,
tenant_id UUID FK (tenants.id),
type ENUM('credit','discount_percentage','discount_fixed'),
amount_cop BIGINT nullable,
percentage DECIMAL(5,2) nullable,
billing_period VARCHAR(7),
reason TEXT,
expires_at TIMESTAMPTZ nullable,
applied_by UUID FK (platform_users.id),
created_at TIMESTAMPTZ
```

**Tabla adicional requerida — campos en `platform_users`**:
```sql
id UUID PK,
name VARCHAR(100),
email VARCHAR(300) UNIQUE,
password_hash VARCHAR,
is_active BOOLEAN DEFAULT true,
last_login_at TIMESTAMPTZ nullable,
deactivated_at TIMESTAMPTZ nullable,
deactivated_by UUID FK nullable,
created_at TIMESTAMPTZ,
created_by UUID FK nullable
```

**Tabla adicional requerida — `platform_user_roles`**:
```sql
user_id UUID FK (platform_users.id),
role ENUM('soporte','billing','admin'),
granted_at TIMESTAMPTZ,
granted_by UUID FK (platform_users.id),
PRIMARY KEY (user_id, role)
```

---

## Tenants

### GET /api/v1/platform/tenants

**Objetivo**
Listar todos los tenants con soporte de filtros por estado, plan, ciudad y búsqueda de texto libre.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params:
  - `status`: `PENDING` | `TRIAL` | `ACTIVE` | `SUSPENDED` | `CANCELLED` — opcional
  - `plan`: `none` | `commission` | `subscription` | `hybrid` — opcional
  - `city`: string — opcional, búsqueda exacta case-insensitive
  - `search`: string — opcional, ILIKE sobre `name`, `slug`, `owner_email`
  - `sort`: `created_at` | `name` | `status` | `trial_ends_at` — default `created_at`
  - `order`: `asc` | `desc` — default `desc`
  - `page`: integer — default `1`
  - `limit`: integer — default `20`, máximo `100`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Restaurante La Mesa",
      "slug": "la-mesa",
      "city": "Bogotá",
      "owner_email": "owner@lamesa.com",
      "status": "TRIAL",
      "plan": "none",
      "trial_ends_at": "2026-07-07T00:00:00Z",
      "nit_rut": "901234567-1",
      "nit_verified": false,
      "created_at": "2026-06-01T10:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 145,
    "total_pages": 8
  }
}
```

**Validaciones**
- `status`: debe ser un valor válido del ENUM si se proporciona
- `plan`: debe ser un valor válido del ENUM si se proporciona
- `sort`: debe ser uno de `[created_at, name, status, trial_ends_at]`
- `order`: debe ser `asc` o `desc`
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `100`

**Reglas de Negocio**
- La búsqueda `search` aplica ILIKE `%valor%` sobre `name`, `slug` y `owner_email` combinados con OR
- Cuando `status=TRIAL` y no se especifica `sort`, el orden por defecto cambia a `trial_ends_at ASC` (urgencia)
- Todos los tenants son visibles para cualquier rol permitido; no hay restricción de visibilidad por rol

**Errores**
| Código | Condición |
|---|---|
| 400 | `status` o `plan` con valor fuera del ENUM |
| 400 | `sort` con valor no permitido |
| 400 | `page` < 1 o `limit` > 100 |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |

**Observabilidad**
- Log: `"platform.tenants.list"` `{ opsUserId, filters: { status, plan, city, search }, resultCount, page }`
- Métrica: `platform.tenants_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: devuelve lista paginada con todos los campos del tenant
- [ ] `status=TRIAL` sin `sort`: resultados ordenados por `trial_ends_at ASC`
- [ ] `search=mesa`: coincide en `name`, `slug` y `owner_email` con ILIKE
- [ ] `status=INVALIDO` devuelve 400
- [ ] `limit=101` devuelve 400
- [ ] JWT ausente devuelve 401
- [ ] Rol `soporte` puede ver la lista (permitido)
- [ ] Actor sin ninguno de los roles permitidos devuelve 403

---

### POST /api/v1/platform/tenants

**Objetivo**
Crear un nuevo tenant con información básica del restaurante e iniciar el proceso de onboarding con o sin período de trial. Este endpoint debe estar expuesto en `@mesto/platform` desde la vista `/tenants` mediante un CTA visible `Crear tenant`.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "name": "Restaurante La Mesa",
  "slug": "la-mesa",
  "city": "Bogotá",
  "ownerEmail": "owner@lamesa.com",
  "nitRut": "901234567-1",
  "startTrial": true,
  "trialDays": 30
}
```

**Response 201**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "Restaurante La Mesa",
  "slug": "la-mesa",
  "city": "Bogotá",
  "ownerEmail": "owner@lamesa.com",
  "status": "TRIAL",
  "plan": "none",
  "trialEndsAt": "2026-07-07T00:00:00Z",
  "nit": "901234567-1",
  "nitVerified": false,
  "createdAt": "2026-06-07T00:00:00Z",
  "createdBy": "ops-uuid-0001",
  "ownerAdmin": {
    "staffId": "d4e5f6a7-b8c9-0123-defa-234567890123",
    "tenantId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Administrador",
    "email": "owner@lamesa.com",
    "status": "PENDING",
    "activationUrl": "https://admin.mesto.app/staff/activate?token=..."
  }
}
```

**Validaciones**
- `name`: requerido, 2–200 caracteres
- `slug`: requerido, 3–100 caracteres, regex `^[a-z0-9-]+$`, debe ser único en `tenants.slug`
- `city`: requerido, 2–100 caracteres
- `ownerEmail`: requerido, formato email válido, máximo 300 caracteres
- `nitRut`: requerido, 5–20 caracteres, alfanumérico más guiones (`^[A-Za-z0-9-]+$`)
- `startTrial`: booleano, default `false`
- `trialDays`: entero 1–90; requerido si `startTrial=true`; ignorado si `startTrial=false`

**Reglas de Negocio**
- Si `startTrial=true`: `tenants.status = 'TRIAL'`, `trial_ends_at = NOW() + trial_days`, insertar fila en `trial_pipeline` con `status='en_trial'`
- Si `startTrial=false`: `tenants.status = 'PENDING'`, no se crea fila en `trial_pipeline`
- Al crear el tenant también se crea un staff `Admin` bootstrap con `email = ownerEmail`, `status = 'PENDING'` y sin contraseña inicial
- El staff bootstrap recibe activation token de 48h y su URL se expone como `ownerAdmin.activationUrl` mientras siga pendiente
- `plan` se crea como `none` siempre; el cambio de plan ocurre en `PATCH /billing/{tenantId}`
- `nit_verified` se inicializa en `false`; la verificación manual es responsabilidad de Ops
- `created_by` = `sub` del JWT (id del Ops que ejecuta la acción)
- Se envía email de bienvenida a `owner_email` vía SDD-007 con template `tenant_welcome`
- Se envía email de activación del staff admin bootstrap vía SDD-001/SDD-007
- La unicidad de `slug` se verifica antes de la inserción con SELECT FOR UPDATE o constraint UNIQUE
- Registro en `platform_audit_log`: `action='tenant.created'`, `details={ name, slug, status, trialDays }`
- La UI de `@mesto/platform` debe redirigir a la ficha del tenant recién creado o refrescar el listado inmediatamente con el nuevo registro visible

**Errores**
| Código | Condición |
|---|---|
| 400 | Campo requerido ausente o inválido |
| 400 | `slug` no cumple regex `^[a-z0-9-]+$` |
| 400 | `trialDays` fuera de rango 1–90 cuando `startTrial=true` |
| 400 | `trialDays` presente sin `startTrial=true` (campo ignorado pero no es error; se acepta) |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 409 | `slug` ya existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.created"` `{ opsUserId, tenantId, slug, status, trialDays }`
- Métrica: `platform.tenant_created` (counter)

**Casos de Aceptación**
- [ ] Happy path con trial: tenant en `TRIAL`, fila en `trial_pipeline`, email de bienvenida enviado
- [ ] Happy path sin trial: tenant en `PENDING`, sin fila en `trial_pipeline`
- [ ] La app `@mesto/platform` expone un CTA `Crear tenant` en `/tenants` y consume este endpoint con los mismos campos camelCase del contrato
- [ ] La respuesta incluye `ownerAdmin.staffId`, `ownerAdmin.tenantId`, `ownerAdmin.status` y `ownerAdmin.activationUrl` cuando el admin bootstrap sigue pendiente
- [ ] `slug` duplicado devuelve 409
- [ ] Campo `name` ausente devuelve 400
- [ ] `slug` con mayúsculas devuelve 400
- [ ] `trial_days=0` con `start_trial=true` devuelve 400
- [ ] `trial_days=91` devuelve 400
- [ ] Rol sin permisos devuelve 403

---

### GET /api/v1/platform/tenants/{tenantId}

**Objetivo**
Obtener la ficha completa de un tenant incluyendo info general, resumen de billing, estado del pipeline de trial y métricas de actividad.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body: No aplica

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "Restaurante La Mesa",
  "slug": "la-mesa",
  "city": "Bogotá",
  "ownerEmail": "owner@lamesa.com",
  "status": "TRIAL",
  "plan": "none",
  "commissionRate": null,
  "subscriptionAmountCop": null,
  "internalNotes": "Cliente muy interesado en plan subscription",
  "nit": "901234567-1",
  "nitVerified": false,
  "trialEndsAt": "2026-07-07T00:00:00Z",
  "createdAt": "2026-06-01T10:00:00Z",
  "createdBy": {
    "id": "ops-uuid-0001",
    "name": "Ana García"
  },
  "ownerAdmin": {
    "staffId": "d4e5f6a7-b8c9-0123-defa-234567890123",
    "tenantId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Administrador",
    "email": "owner@lamesa.com",
    "status": "PENDING",
    "activationUrl": "https://admin.mesto.app/staff/activate?token=..."
  },
  "trialPipeline": {
    "status": "en_trial",
    "assignedTo": null,
    "notes": [
      {
        "text": "Primera llamada realizada, propietario interesado.",
        "createdBy": "ops-uuid-0001",
        "createdByName": "Ana García",
        "createdAt": "2026-06-03T10:00:00Z"
      }
    ],
    "convertedAt": null,
    "convertedBy": null,
    "lostAt": null,
    "lostBy": null
  },
  "billingSummary": {
    "totalPaidCop": 0,
    "lastPaymentDate": null,
    "paymentCount": 0
  },
  "activity": {
    "totalOrders": 14,
    "lastOrderAt": "2026-06-06T20:15:00Z",
    "activeSessionsNow": 2
  }
}
```

**Validaciones**
- `tenantId`: debe ser UUID válido (formato RFC 4122)

**Reglas de Negocio**
- El campo `id` es el `tenantId` canónico que usa el login del Admin Panel y cualquier endpoint staff que requiera `tenantId`
- `trial_pipeline` es `null` si el tenant nunca tuvo `status=TRIAL`
- `activity.total_orders`: COUNT de órdenes en la tabla `orders` filtradas por `tenant_id`
- `activity.active_sessions_now`: SCAN de Redis con patrón `session:{tenantId}:*` y conteo de claves activas
- `billing_summary` agregado desde `platform_payments WHERE tenant_id = tenantId`
- `created_by` resuelto via JOIN `platform_users` por `tenants.created_by`
- `ownerAdmin` se resuelve sobre el staff `Admin` del tenant; si está `PENDING` y tiene activation token activo en Redis, `activationUrl` se devuelve; si ya activó, `activationUrl = null`
- `trial_pipeline.assigned_to` incluye `{ id, name }` si hay asignación, o `null`

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es un UUID válido |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.detail"` `{ opsUserId, tenantId }`
- Métrica: `platform.tenant_detail_view` (counter)

**Casos de Aceptación**
- [ ] Happy path: ficha completa con todas las secciones pobladas
- [ ] La ficha muestra explícitamente el `tenantId` (campo `id`) y el bloque `ownerAdmin`
- [ ] Owner admin `PENDING`: `activationUrl` no nulo
- [ ] Owner admin `ACTIVE`: `activationUrl = null`
- [ ] Tenant sin órdenes: `activity.total_orders=0`, `last_order_at=null`
- [ ] Tenant sin trial: `trial_pipeline=null`
- [ ] Tenant con notas en trial: array de notas visible
- [ ] `tenantId` con formato inválido devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### PATCH /api/v1/platform/tenants/{tenantId}

**Objetivo**
Actualizar campos editables del tenant como nombre, ciudad, email del propietario, notas internas y datos de NIT/RUT.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body (todos los campos son opcionales; se requiere al menos uno):
```json
{
  "name": "Restaurante La Mesa Gourmet",
  "city": "Medellín",
  "owner_email": "nuevo@lamesa.com",
  "nit_rut": "901234567-1",
  "nit_verified": true,
  "internal_notes": "Propietario confirmó cambio de ciudad el 2026-06-07"
}
```

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "Restaurante La Mesa Gourmet",
  "slug": "la-mesa",
  "city": "Medellín",
  "owner_email": "nuevo@lamesa.com",
  "status": "TRIAL",
  "plan": "none",
  "internal_notes": "Propietario confirmó cambio de ciudad el 2026-06-07",
  "nit_rut": "901234567-1",
  "nit_verified": true,
  "updated_at": "2026-06-07T12:00:00Z",
  "updated_by": "ops-uuid-0001"
}
```

**Validaciones**
- Al menos un campo debe estar presente en el body; cuerpo vacío devuelve 400
- `name`: 2–200 caracteres si se proporciona
- `city`: 2–100 caracteres si se proporciona
- `owner_email`: formato email válido, máximo 300 caracteres si se proporciona
- `nit_rut`: 5–20 caracteres, regex `^[A-Za-z0-9-]+$` si se proporciona
- `nit_verified`: booleano si se proporciona
- `internal_notes`: máximo 5000 caracteres si se proporciona
- `status` NO es un campo válido en este endpoint; si se envía, devuelve 400 indicando que se use el endpoint específico

**Reglas de Negocio**
- `nit_verified=true` requiere que `nit_rut` sea no nulo en DB o que se proporcione en la misma solicitud; de lo contrario, devuelve 400
- Cambio de `owner_email`: el log de auditoría registra `{ oldEmail, newEmail }`
- Todos los cambios registrados en `platform_audit_log`: `action='tenant.updated'`, `details={ field: { old, new } }` por cada campo modificado
- El campo `slug` no es editable por este endpoint; requiere soporte de base de datos

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío (sin campos) |
| 400 | Campo `status` enviado en el body |
| 400 | `nit_verified=true` sin `nit_rut` existente ni proporcionado |
| 400 | Campo con formato o longitud inválida |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.updated"` `{ opsUserId, tenantId, updatedFields: ["city", "owner_email"] }`
- Métrica: `platform.tenant_updated` (counter)

**Casos de Aceptación**
- [ ] Happy path campo único: `city` actualizada, audit log con `{ old, new }`
- [ ] Múltiples campos en una sola solicitud: todos actualizados, audit log por cada campo
- [ ] `nit_verified=true` con `nit_rut` ya existente en DB: permite actualización
- [ ] `nit_verified=true` sin `nit_rut` en DB ni en body: devuelve 400
- [ ] `status` en body: devuelve 400 con mensaje de redirección al endpoint correcto
- [ ] Body vacío `{}`: devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/extend-trial

**Objetivo**
Extender el período de trial de un tenant activo en estado TRIAL, sumando días adicionales a la fecha de expiración actual.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "additional_days": 15,
  "reason": "Cliente solicitó más tiempo para evaluar el módulo de reportes antes de decidir el plan."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_trial_ends_at": "2026-07-07T00:00:00Z",
  "new_trial_ends_at": "2026-07-22T00:00:00Z",
  "extended_by_days": 15,
  "extended_by": "ops-uuid-0001",
  "extended_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `additional_days`: requerido, entero, mínimo `1`, máximo `90`
- `reason`: requerido, 10–500 caracteres
- El tenant debe tener `status = 'TRIAL'`; cualquier otro estado devuelve 400

**Reglas de Negocio**
- `new_trial_ends_at = MAX(current_trial_ends_at, NOW()) + additional_days`; si el trial ya expiró, la base de cálculo es `NOW()`
- Actualiza `tenants.trial_ends_at` con la nueva fecha calculada
- Agrega nota automática al array `trial_pipeline.notes`: `{ text: "Trial extendido {additional_days} días. Motivo: {reason}", created_by: opsUserId, created_at: NOW() }`
- Registro en `platform_audit_log`: `action='tenant.trial_extended'`, `details={ additionalDays, previousTrialEndsAt, newTrialEndsAt, reason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `additional_days` ausente, < 1, o > 90 |
| 400 | `reason` ausente o longitud fuera de rango |
| 400 | Tenant no está en `status=TRIAL` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.trial_extended"` `{ opsUserId, tenantId, additionalDays, previousTrialEndsAt, newTrialEndsAt }`
- Métrica: `platform.trial_extended` (counter)

**Casos de Aceptación**
- [ ] Happy path: trial extendido, `trial_ends_at` actualizado, nota automática en `trial_pipeline`
- [ ] Trial ya expirado (fecha pasada): extensión calculada desde `NOW()`
- [ ] `additional_days=0` devuelve 400
- [ ] `additional_days=91` devuelve 400
- [ ] Tenant con `status=ACTIVE` devuelve 400
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/suspend

**Objetivo**
Suspender un tenant activo o en trial, bloqueando el acceso de sus usuarios y opcionalmente notificando al propietario.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Falta de pago por 2 períodos consecutivos. Referencia interna: COBRANZA-2026-06.",
  "notify_owner": true
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_status": "ACTIVE",
  "new_status": "SUSPENDED",
  "reason": "Falta de pago por 2 períodos consecutivos. Referencia interna: COBRANZA-2026-06.",
  "suspended_by": "ops-uuid-0001",
  "suspended_at": "2026-06-07T12:00:00Z",
  "owner_notified": true
}
```

**Validaciones**
- `reason`: requerido, 10–500 caracteres
- `notify_owner`: booleano, default `true`
- El tenant debe tener `status = 'ACTIVE'` o `status = 'TRIAL'`; cualquier otro estado devuelve 400

**Reglas de Negocio**
- Actualiza `tenants.status = 'SUSPENDED'`
- Invalida todas las sesiones activas del tenant en Redis: DEL de todas las claves que coincidan con `session:{tenantId}:*`
- Si `notify_owner=true`: envía email de suspensión a `owner_email` vía SDD-007 con template `tenant_suspended`
- Registro en `platform_audit_log`: `action='tenant.suspended'`, `details={ reason, previousStatus, notifyOwner, sessionsInvalidated }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango |
| 400 | Tenant ya está en `SUSPENDED`, `CANCELLED` o `PENDING` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.suspended"` `{ opsUserId, tenantId, previousStatus, sessionsInvalidated, notifyOwner }`
- Métrica: `platform.tenant_suspended` (counter)

**Casos de Aceptación**
- [ ] Happy path `ACTIVE→SUSPENDED`: sesiones de Redis eliminadas, audit log creado
- [ ] Happy path `TRIAL→SUSPENDED`: funciona igual que ACTIVE
- [ ] `notify_owner=true`: email de suspensión enviado a `owner_email`
- [ ] `notify_owner=false`: no se envía email
- [ ] Tenant ya en `SUSPENDED` devuelve 400
- [ ] Tenant en `CANCELLED` devuelve 400
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/reactivate

**Objetivo**
Reactivar un tenant suspendido, restaurando su acceso completo a la plataforma y opcionalmente notificando al propietario.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Pago recibido y confirmado. Cuenta al día a partir del 2026-06-07.",
  "notify_owner": true
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_status": "SUSPENDED",
  "new_status": "ACTIVE",
  "reason": "Pago recibido y confirmado. Cuenta al día a partir del 2026-06-07.",
  "reactivated_by": "ops-uuid-0001",
  "reactivated_at": "2026-06-07T12:00:00Z",
  "owner_notified": true
}
```

**Validaciones**
- `reason`: requerido, 10–500 caracteres
- `notify_owner`: booleano, default `true`
- El tenant debe tener `status = 'SUSPENDED'`; cualquier otro estado devuelve 400

**Reglas de Negocio**
- Actualiza `tenants.status = 'ACTIVE'`
- Si `notify_owner=true`: envía email de reactivación a `owner_email` vía SDD-007 con template `tenant_reactivated`
- Registro en `platform_audit_log`: `action='tenant.reactivated'`, `details={ reason, notifyOwner }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango |
| 400 | Tenant no está en `SUSPENDED` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.reactivated"` `{ opsUserId, tenantId, notifyOwner }`
- Métrica: `platform.tenant_reactivated` (counter)

**Casos de Aceptación**
- [ ] Happy path `SUSPENDED→ACTIVE`: status actualizado, audit log creado
- [ ] `notify_owner=true`: email de reactivación enviado
- [ ] `notify_owner=false`: no se envía email
- [ ] Tenant en `ACTIVE` (no suspendido) devuelve 400
- [ ] Tenant en `CANCELLED` devuelve 400
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/activate

**Objetivo**
Activar manualmente un tenant atascado en estado `PENDING`, moviéndolo a `TRIAL` e iniciando su período de prueba. Cubre casos donde el onboarding self-service no completó la verificación de email (un tenant creado sin trial nace en `PENDING` y, sin esta acción, solo el Admin puede entrar — el resto del staff recibe "Restaurante no encontrado").

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Verificación de propiedad confirmada con el dueño por teléfono.",
  "trialDays": 30,
  "notifyOwner": true
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_status": "PENDING",
  "new_status": "TRIAL",
  "trial_ends_at": "2026-07-08T12:00:00Z",
  "reason": "Verificación de propiedad confirmada con el dueño por teléfono.",
  "activated_by": "ops-uuid-0001",
  "activated_at": "2026-06-08T12:00:00Z",
  "owner_notified": true
}
```

**Validaciones**
- `reason`: requerido, 10–500 caracteres
- `trialDays`: entero, 1–90 (default 30)
- `notifyOwner`: booleano, default `true`
- El tenant debe tener `status = 'PENDING'`; cualquier otro estado devuelve 400

**Reglas de Negocio**
- Actualiza `tenants.status = 'trial'` y `tenants.trial_ends_at = NOW() + trialDays`
- Si el tenant no tenía fila en `trial_pipeline` (los tenants nacidos en `PENDING` no la tienen), se crea una nueva entrada de pipeline asignada al actor Ops — para que aparezca en el CRM de trials
- Si `notifyOwner=true`: envía email de activación a `owner_email` vía SDD-007 con template `tenant_activated`
- Registro en `platform_audit_log`: `action='tenant.activated'`, `details={ reason, previousStatus: "PENDING", trialEndsAt, trialDays, notifyOwner }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango; `trialDays` fuera de 1–90 |
| 400 | Tenant no está en `PENDING` (INVALID_STATUS) |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.activated"` `{ opsUserId, tenantId }`
- Métrica: `platform.tenant_activated` (counter)

**Casos de Aceptación**
- [ ] Happy path `PENDING→TRIAL`: status actualizado, `trial_ends_at` seteado, pipeline de trial creado, audit log registrado
- [ ] Tras activar, un staff no-Admin (ej. Cajero) del tenant puede iniciar sesión (antes recibía "Restaurante no encontrado")
- [ ] `notifyOwner=true`: email de activación enviado a `owner_email`
- [ ] `notifyOwner=false`: no se envía email
- [ ] Tenant en `TRIAL` (ya activo) devuelve 400
- [ ] Tenant en `ACTIVE` devuelve 400
- [ ] Tenant en `SUSPENDED` devuelve 400
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `trialDays = 0` o `> 90` devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/reset-admin-password

**Objetivo**
Mesto Ops genera un **enlace de restablecimiento de contraseña** para el administrador propietario (owner Admin) de un tenant que perdió su clave. Ops comparte el enlace con el dueño, que define una nueva contraseña vía el flujo existente `POST /api/v1/identity/staff/password-reset/confirm`.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant.
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "staff_id": "fcff9fb9-e30c-43b1-8031-18a17fdb2b40",
  "email": "owner@restaurante.co",
  "reset_url": "http://localhost:3003/staff/password-reset?token=2a...",
  "expires_at": "2026-06-08T22:22:00Z"
}
```
*`reset_url` es el enlace que Ops comparte con el dueño. El email también se dispara (canal secundario, stub en dev). Token TTL de 1 hora.*

**Validaciones**
- `tenantId`: UUID válido (validación de path).

**Reglas de Negocio**
- Obtener el tenant; si no existe: 404 "Tenant no encontrado."
- Si el tenant no tiene `owner_email`: 400 `NO_OWNER`.
- Localizar el staff Admin propietario por `(email = owner_email, tenant_id)`; si no existe: 404 "No se encontró la cuenta de administrador del restaurante."
- Si el admin no está `ACTIVE`: 400 `ADMIN_NOT_ACTIVE` (si está pendiente, el camino es el enlace de activación, no el de reset).
- Generar reset token y `SET reset:staff:{token} {staffId} EX 3600` (1h).
- Construir `reset_url = {ADMIN_URL}/staff/password-reset?token={token}` y enviar email (SDD-007, stub en dev).
- Registro en `platform_audit_log`: `action='tenant.admin_password_reset'`, `details={ staffId, email }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Tenant sin `owner_email` (`NO_OWNER`), o admin no activo (`ADMIN_NOT_ACTIVE`) |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` inexistente, o no hay cuenta de admin para el owner_email |

**Observabilidad**
- Log: `"platform.tenants.admin_password_reset"` `{ opsUserId, tenantId, staffId }`
- Métrica: `platform.tenant_admin_password_reset` (counter)

**Casos de Aceptación**
- [ ] Tenant con owner Admin ACTIVE → 200 con `reset_url` + `expires_at`; queda en audit log
- [ ] El owner usa el enlace, define nueva clave (confirm 200) y hace login con ella
- [ ] Tenant cuyo owner Admin está PENDING → 400 `ADMIN_NOT_ACTIVE`
- [ ] `tenantId` inexistente → 404
- [ ] Rol sin permisos → 403

---

### POST /api/v1/platform/tenants/{tenantId}/cancel

**Objetivo**
Cancelar definitivamente la cuenta de un tenant requiriendo confirmación de texto explícita y motivo detallado, con cierre completo de sesiones activas.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Cierre voluntario solicitado por el propietario vía llamada telefónica el 2026-06-07. Propietario confirmó cierre del negocio. No hay deuda pendiente.",
  "confirmation_text": "CANCELAR",
  "notify_owner": true
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_status": "ACTIVE",
  "new_status": "CANCELLED",
  "reason": "Cierre voluntario solicitado por el propietario...",
  "cancelled_by": "ops-uuid-0001",
  "cancelled_at": "2026-06-07T12:00:00Z",
  "owner_notified": true
}
```

**Validaciones**
- `reason`: requerido, 20–1000 caracteres
- `confirmation_text`: requerido, debe ser exactamente la cadena `"CANCELAR"` (case-sensitive, sin espacios)
- `notify_owner`: booleano, default `true`
- El tenant no debe estar ya en `CANCELLED`

**Reglas de Negocio**
- Actualiza `tenants.status = 'CANCELLED'`
- Invalida todas las sesiones activas en Redis: DEL `session:{tenantId}:*`
- Si el tenant estaba en `trial_pipeline` con `status` distinto de `convertido` o `perdido`, actualiza `trial_pipeline.status = 'perdido'`, `lost_at = NOW()`, `lost_by = opsUserId`
- Si `notify_owner=true`: envía email de cancelación a `owner_email` vía SDD-007 con template `tenant_cancelled`
- Los datos del tenant NO se eliminan (soft cancel); `status=CANCELLED` es el marcador permanente
- Registro en `platform_audit_log`: `action='tenant.cancelled'`, `details={ reason, previousStatus, notifyOwner }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango (< 20 chars) |
| 400 | `confirmation_text` ausente |
| 400 | `confirmation_text` no es exactamente `"CANCELAR"` (case-sensitive) |
| 400 | Tenant ya en `CANCELLED` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.tenants.cancelled"` `{ opsUserId, tenantId, previousStatus, notifyOwner }`
- Métrica: `platform.tenant_cancelled` (counter)

**Casos de Aceptación**
- [ ] Happy path: `status=CANCELLED`, sesiones invalidadas, audit log creado
- [ ] Tenant en `TRIAL`: `trial_pipeline.status` actualizado a `perdido`
- [ ] `confirmation_text="cancelar"` (minúscula) devuelve 400
- [ ] `confirmation_text="CANCELAR "` (con espacio) devuelve 400
- [ ] `confirmation_text` ausente devuelve 400
- [ ] `reason` de 19 caracteres devuelve 400
- [ ] Tenant ya en `CANCELLED` devuelve 400
- [ ] `notify_owner=false`: no se envía email
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

## Acciones de Soporte

### POST /api/v1/platform/tenants/{tenantId}/support/close-session

**Objetivo**
Forzar el cierre de una sesión de mesa específica que esté atascada o en estado inválido, sin necesidad de impersonar al tenant.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "session_id": "sess-uuid-0001",
  "reason": "Sesión atascada reportada por el cliente. Sin actividad registrada desde hace más de 6 horas."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "session_id": "sess-uuid-0001",
  "closed_by": "ops-uuid-0001",
  "closed_at": "2026-06-07T12:00:00Z",
  "reason": "Sesión atascada reportada por el cliente. Sin actividad registrada desde hace más de 6 horas."
}
```

**Validaciones**
- `session_id`: requerido, UUID válido
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- Verificar que la sesión pertenece al tenant: la clave de Redis tiene formato `session:{tenantId}:{sessionId}` o la fila en tabla `sessions` tiene `tenant_id = tenantId`
- Paso 1: intentar DEL `session:{tenantId}:{sessionId}` en Redis
- Paso 2: si no existe en Redis, buscar en tabla `sessions` WHERE `id=sessionId AND tenant_id=tenantId`; si existe y `status != 'closed'`, actualizar `status='force_closed'`, `closed_at=NOW()`
- Si no se encuentra en ninguno, devolver 404
- Registro en `platform_audit_log`: `action='support.close_session'`, `details={ sessionId, reason, source: 'redis'|'db' }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `session_id` ausente o no es UUID válido |
| 400 | `reason` ausente o longitud fuera de rango |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |
| 404 | `sessionId` no encontrado en Redis ni en DB para este tenant |

**Observabilidad**
- Log: `"platform.support.session_closed"` `{ opsUserId, tenantId, sessionId, source }`
- Métrica: `platform.support_session_closed` (counter)

**Casos de Aceptación**
- [ ] Happy path Redis: clave eliminada, audit log con `source='redis'`
- [ ] Sesión solo en DB: estado actualizado a `force_closed`, audit log con `source='db'`
- [ ] `session_id` no encontrado en ninguna fuente devuelve 404
- [ ] Sesión de otro tenant devuelve 404
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/support/regenerate-pin

**Objetivo**
Regenerar el PIN de acceso de una mesa específica del tenant, invalidando cualquier sesión activa en esa mesa.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "table_id": "table-uuid-0042",
  "reason": "PIN comprometido reportado por el propietario. Posible acceso no autorizado."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "table_id": "table-uuid-0042",
  "new_pin": "7842",
  "regenerated_by": "ops-uuid-0001",
  "regenerated_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `table_id`: requerido, UUID válido
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- Verificar que la mesa pertenece al tenant: `SELECT id FROM tables WHERE id=tableId AND tenant_id=tenantId`; si no existe, devolver 404
- Generar PIN de 4 dígitos numérico usando fuente criptográficamente aleatoria (`crypto.randomInt(1000, 9999)` o equivalente)
- Actualizar `tables.pin = new_pin` en DB
- Invalidar sesiones activas en esa mesa: DEL claves con patrón `session:{tenantId}:{tableId}:*` en Redis
- El nuevo PIN se devuelve en texto plano en la respuesta (Ops lo comunica al propietario)
- Registro en `platform_audit_log`: `action='support.pin_regenerated'`, `details={ tableId, reason, sessionsInvalidated }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `table_id` ausente o no es UUID válido |
| 400 | `reason` ausente o longitud fuera de rango |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |
| 404 | `tableId` no existe o no pertenece al tenant |

**Observabilidad**
- Log: `"platform.support.pin_regenerated"` `{ opsUserId, tenantId, tableId }`
- Métrica: `platform.support_pin_regenerated` (counter)

**Casos de Aceptación**
- [ ] Happy path: PIN regenerado, sesiones de esa mesa eliminadas de Redis, audit log creado
- [ ] Mesa sin sesiones activas: PIN regenerado igual, `sessionsInvalidated=0`
- [ ] `tableId` pertenece a tenant distinto: devuelve 404
- [ ] `tableId` no existente devuelve 404
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/support/close-all-sessions

**Objetivo**
Forzar el cierre masivo de todas las sesiones activas de un tenant en Redis, útil ante emergencias o mantenimiento del local.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Solicitud del propietario por mantenimiento de emergencia del local. Confirmado por llamada."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "sessions_closed": 7,
  "closed_by": "ops-uuid-0001",
  "closed_at": "2026-06-07T12:00:00Z",
  "reason": "Solicitud del propietario por mantenimiento de emergencia del local."
}
```

**Validaciones**
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- SCAN Redis con patrón `session:{tenantId}:*` y DEL todas las claves encontradas (pipeline/batch delete)
- Registrar en tabla `sessions` (WHERE `tenant_id=tenantId AND status NOT IN ('closed','force_closed')`): actualizar `status='force_closed'`, `closed_at=NOW()`
- `sessions_closed` = número de claves de Redis eliminadas (si Redis retorna 0, también devolver 0 sin error)
- Registro en `platform_audit_log`: `action='support.close_all_sessions'`, `details={ reason, sessionsClosed }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.support.all_sessions_closed"` `{ opsUserId, tenantId, sessionsClosed }`
- Métrica: `platform.support_all_sessions_closed` (counter)

**Casos de Aceptación**
- [ ] Happy path: todas las sesiones eliminadas de Redis, count correcto, audit log creado
- [ ] Tenant sin sesiones activas: `sessions_closed=0`, respuesta 200 sin error
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/tenants/{tenantId}/support/invalidate-catalog-cache

**Objetivo**
Invalidar todas las claves de caché de catálogo en Redis de un tenant para forzar la recarga desde base de datos en la próxima solicitud.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "reason": "Cliente reporta que precios actualizados no se reflejan en la app de los comensales."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "keys_invalidated": 12,
  "invalidated_by": "ops-uuid-0001",
  "invalidated_at": "2026-06-07T12:00:00Z",
  "reason": "Cliente reporta que precios actualizados no se reflejan en la app de los comensales."
}
```

**Validaciones**
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- SCAN Redis con patrones `catalog:{tenantId}:*`, `menu:{tenantId}:*` y `category:{tenantId}:*`; DEL todas las claves encontradas en un pipeline
- `keys_invalidated` = total de claves eliminadas en los tres patrones
- Si no hay claves, devolver `keys_invalidated=0` sin error
- Registro en `platform_audit_log`: `action='support.catalog_cache_invalidated'`, `details={ reason, keysInvalidated }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o longitud fuera de rango |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.support.catalog_cache_invalidated"` `{ opsUserId, tenantId, keysInvalidated }`
- Métrica: `platform.support_catalog_cache_invalidated` (counter)

**Casos de Aceptación**
- [ ] Happy path: claves eliminadas, count correcto, audit log creado
- [ ] Caché ya vacío: `keys_invalidated=0`, respuesta 200 sin error
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

## Planes y Entitlements [v1.3]

El `plan` libre por tenant (`SubscriptionAmountCop`/`CommissionRate` tecleados) se reemplaza por un **catálogo** (`planCode`) con entitlements. Catálogo definido en código (no tabla editable en v1) y sembrado en `plans`:

| planCode | Nombre | Modo | Precio/mes COP | maxFloors | maxKdsStations | maxTables | maxStaff | loyalty | branding | analytics |
|---|---|---|---|---|---|---|---|---|---|---|
| `basic` | Básico | subscription | 59000 | 1 | 2 | 20 | 5 | ✅ | ✗ | basic |
| `pro` | Pro | subscription | 119000 | null | null | null | null | ✅ | ✅ | advanced |
| `enterprise` | Enterprise | hybrid (a medida) | custom | null | null | null | null | ✅ | ✅ | advanced |

> `null` = ilimitado. **Trial** = entitlements de `pro` por 14 días (`trialEndsAt`); no factura. `enterprise` admite **override** por tenant (`SubscriptionAmountCop`/`CommissionRate` + límites) sin cambiar `planCode`.

**Tabla `tenants`** (extensión): la columna `plan` pasa a `plan_code VARCHAR(20)` (FK lógica al catálogo); se conservan `subscription_amount_cop`/`commission_rate` como override Enterprise. El enum `TenantPlan` (Identity) se mantiene como **modo de cobro** (`Subscription`/`Commission`/`Hybrid`/`None`), derivado del plan.

**Entitlements en el token/estado del tenant:** el estado del tenant (y el JWT staff) exponen los entitlements resueltos para que el front gatee sin round-trip. La fuente de verdad es `IPlanCatalog.EntitlementsFor(planCode, override)`.

### Enforcement (bloqueante) — responsabilidad cruzada

El backend valida el límite **antes de crear** el recurso y responde **403** con `{ "code":"plan_limit_exceeded", "detail":"…", "limit":1, "current":1, "suggestedPlan":"pro" }`:

| Recurso | Módulo | Regla |
|---|---|---|
| Crear piso | Sessions | `floors(tenant) >= maxFloors` → 403 |
| Crear estación KDS | Catalog/Orders | `stations(tenant) >= maxKdsStations` → 403 |
| Crear mesa | Sessions | `tables(tenant) >= maxTables` → 403 |
| Invitar/activar staff | Identity | `staff(tenant) >= maxStaff` → 403 |
| Branding / analítica avanzada | Catalog/Analytics | gateado por entitlement (loyalty siempre on) |

> Cada módulo consulta los entitlements del tenant (servicio compartido `IPlanEntitlementsReader`). El catálogo es de Platform; el chequeo vive en el módulo dueño del recurso.

---

## Facturas [v1.3]

Al **cierre de mes** (job) se emite una **factura inmutable** por tenant activo (no trial). Persistida en `platform_invoices` + `platform_invoice_lines`.

**`platform_invoices`**
```sql
id              UUID PK,
tenant_id       UUID NOT NULL,
number          VARCHAR(24) NOT NULL UNIQUE,   -- consecutivo, ej 'MESTO-2026-000123'
period          CHAR(7) NOT NULL,              -- 'YYYY-MM'
subtotal_cop    BIGINT NOT NULL,               -- suscripción + comisión − descuentos
vat_cop         BIGINT NOT NULL,               -- IVA 19% sobre subtotal
total_cop       BIGINT NOT NULL,               -- subtotal + IVA
status          VARCHAR(12) NOT NULL,          -- 'issued'|'paid'|'partial'|'overdue'|'void'
issued_at       TIMESTAMPTZ NOT NULL,
due_at          TIMESTAMPTZ NOT NULL,
voided_reason   VARCHAR(300),
UNIQUE (tenant_id, period)                      -- una factura por tenant/período
```
`platform_invoice_lines(invoice_id, concept, kind['subscription'|'commission'|'discount'], amount_cop)`.

**Reglas:** inmutable tras emitir (correcciones via descuento del período siguiente o `void` con motivo → reverso en el ledger). Estado `paid`/`partial` se deriva de `platform_payments` ligados (`invoice_id`). IVA = `round(subtotal * 0.19)`.

**Integración con Ledger (SDD-010):** emitir factura → `INVOICE_ISSUED` (D 1305 / C 4170/4175 + C 2408); registrar pago → `PAYMENT_RECEIVED`; descuento → `DISCOUNT_APPLIED`; `void` → `INVOICE_VOIDED`. El asiento se postea en la **misma transacción**.

### GET /api/v1/platform/invoices?tenantId&period&status&page&pageSize
Lista de facturas (Ops). Auth `billing`/`admin`. Response: `{ items:[{ id,tenantId,number,period,subtotalCop,vatCop,totalCop,status,issuedAt,dueAt }], total }`.

### GET /api/v1/platform/invoices/{invoiceId}
Detalle con líneas + pagos aplicados.

### POST /api/v1/platform/invoices/issue  [v1.4]
Emite la factura de un tenant para un período (`{ tenantId, period }`). Idempotente: una factura por `(tenant, period)` (re-emitir → 409). Construye líneas (suscripción + comisión − descuentos), calcula IVA 19%, asigna número consecutivo y postea el asiento `INVOICE_ISSUED`. Auth `billing`/`admin`.

### POST /api/v1/platform/invoices/{invoiceId}/void
Anula una factura (motivo requerido). Genera asiento de reverso en el Ledger. Auth `admin`.

## Jobs de facturación [v1.4]

Automatizados por el `BillingScheduler` (`BackgroundService`): corre diario a `BillingJobs:RunHourUtc` (default 08:00 UTC) — expira trials + marca vencidas; el **cierre mensual** corre solo el día `BillingJobs:CloseDay` (default 1) y factura el mes anterior. Todos idempotentes. Config: `BillingJobs:{Enabled,RunOnStartup,RunHourUtc,CloseDay}`. También se disparan manualmente (Auth `billing`/`admin`; `expire-trials` solo `admin`):

### POST /api/v1/platform/jobs/billing-close
Emite la factura de cada **tenant activo** para el período (`{ period? }`, default mes anterior). Response: `{ period, considered, issued, skipped, failed }`. Omite ya-facturados y sin-cargos.

### POST /api/v1/platform/jobs/expire-trials
Tenants en `trial` con `trialEndsAt < hoy` → `suspended` + email al dueño + pipeline a Expirado. Response: `{ expired }`.

### POST /api/v1/platform/jobs/mark-overdue
Facturas `issued` con `dueAt < hoy` → `overdue`. Response: `{ markedOverdue }`.

## Plan del dueño (tenant-facing) [v1.4]

### GET /api/v1/billing/me/plan
**Fuera del base path Platform** (`BillingSelfController`, Auth **StaffAdmin** — el tenant sale del JWT). Devuelve el plan, los entitlements resueltos y el estado de cuenta del propio tenant: `{ planCode, planName, billingMode, priceCop, entitlements{ maxFloors,maxKdsStations,maxTables,maxStaff,loyalty,branding,analytics }, account{ outstandingCop, invoices[] } }`. Lo consume la página **Mi Plan** del admin (`/plan`).

---

## Billing

### GET /api/v1/platform/billing

**Objetivo**
Listar el resumen de facturación de todos los tenants con plan activo para un período contable dado, incluyendo totales globales.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params:
  - `period`: string `YYYY-MM` — requerido
  - `plan`: `commission` | `subscription` | `hybrid` — opcional
  - `status`: `ACTIVE` | `SUSPENDED` | `TRIAL` — opcional
  - `payment_status`: `paid` | `partial` | `pending` — opcional
  - `page`: integer — default `1`
  - `limit`: integer — default `20`, máximo `100`
- Body: No aplica

**Response 200**
```json
{
  "period": "2026-06",
  "summary": {
    "total_tenants_billed": 45,
    "total_amount_cop": 18500000,
    "total_paid_cop": 15200000,
    "total_pending_cop": 3300000
  },
  "data": [
    {
      "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "tenant_name": "Restaurante La Mesa",
      "tenant_status": "ACTIVE",
      "plan": "subscription",
      "subscription_amount_cop": 350000,
      "commission_rate": null,
      "estimated_commission_cop": null,
      "amount_owed_cop": 350000,
      "amount_paid_cop": 350000,
      "balance_cop": 0,
      "payment_status": "paid"
    },
    {
      "tenant_id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "tenant_name": "Pizzería Don Mario",
      "tenant_status": "ACTIVE",
      "plan": "commission",
      "subscription_amount_cop": null,
      "commission_rate": 3.50,
      "estimated_commission_cop": 245000,
      "amount_owed_cop": 245000,
      "amount_paid_cop": 0,
      "balance_cop": 245000,
      "payment_status": "pending"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 45,
    "total_pages": 3
  }
}
```

**Validaciones**
- `period`: requerido, formato `YYYY-MM`; no puede ser un mes futuro más allá del mes actual + 1
- `plan`: valor válido del ENUM si se proporciona
- `status`: valor válido del ENUM si se proporciona
- `payment_status`: valor válido si se proporciona
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `100`

**Reglas de Negocio**
- Tenants con `plan='none'` son excluidos del listado de billing
- `estimated_commission_cop` = SUM(`orders.total`) WHERE `tenant_id=X AND DATE_TRUNC('month', created_at) = period` × `commission_rate / 100`; si no hay plan de comisión, es `null`
- `payment_status`: `'paid'` si `amount_paid >= amount_owed`; `'partial'` si `0 < amount_paid < amount_owed`; `'pending'` si `amount_paid = 0`
- El bloque `summary` se calcula sobre todos los tenants del filtro (no solo la página actual)

**Errores**
| Código | Condición |
|---|---|
| 400 | `period` ausente o con formato inválido |
| 400 | `period` más de un mes en el futuro |
| 400 | Parámetro de query con valor fuera del ENUM |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |

**Observabilidad**
- Log: `"platform.billing.list"` `{ opsUserId, period, filters, resultCount }`
- Métrica: `platform.billing_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: devuelve lista con summary y todos los campos calculados
- [ ] Tenant con plan `commission`: `estimated_commission_cop` calculado correctamente
- [ ] Filtro `payment_status=pending`: solo tenants sin pagos registrados
- [ ] Filtro `plan=subscription`: solo tenants con ese plan
- [ ] `period` ausente devuelve 400
- [ ] `period=2025/06` (formato incorrecto) devuelve 400
- [ ] Rol sin permisos devuelve 403

---

### GET /api/v1/platform/billing/{tenantId}

**Objetivo**
Obtener el detalle completo de facturación de un tenant, incluyendo el historial de pagos y el saldo pendiente por período.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params:
  - `from_period`: string `YYYY-MM` — opcional, default: 6 meses antes del mes actual
  - `to_period`: string `YYYY-MM` — opcional, default: mes actual
- Body: No aplica

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "tenant_name": "Restaurante La Mesa",
  "plan": "subscription",
  "subscription_amount_cop": 350000,
  "commission_rate": null,
  "nit_rut": "901234567-1",
  "nit_verified": true,
  "billing_history": [
    {
      "period": "2026-06",
      "amount_owed_cop": 350000,
      "amount_paid_cop": 350000,
      "balance_cop": 0,
      "payment_status": "paid",
      "payments": [
        {
          "id": "pay-uuid-0001",
          "amount_cop": 350000,
          "payment_date": "2026-06-03",
          "reference": "PSE-20260603-001",
          "notes": "Transferencia bancaria confirmada con extracto.",
          "recorded_by": "ops-uuid-0001",
          "created_at": "2026-06-03T14:00:00Z"
        }
      ]
    },
    {
      "period": "2026-05",
      "amount_owed_cop": 350000,
      "amount_paid_cop": 175000,
      "balance_cop": 175000,
      "payment_status": "partial",
      "payments": [
        {
          "id": "pay-uuid-0002",
          "amount_cop": 175000,
          "payment_date": "2026-05-15",
          "reference": "PSE-20260515-002",
          "notes": "Abono parcial.",
          "recorded_by": "ops-uuid-0001",
          "created_at": "2026-05-15T10:00:00Z"
        }
      ]
    }
  ],
  "total_paid_cop": 1925000,
  "outstanding_cop": 175000
}
```

**Validaciones**
- `tenantId`: UUID válido
- `from_period`: formato `YYYY-MM` si se proporciona
- `to_period`: formato `YYYY-MM` si se proporciona; debe ser `>= from_period`

**Reglas de Negocio**
- `billing_history` ordenado por `period DESC`
- `amount_owed_cop` por período calculado según el plan vigente en ese período
- `outstanding_cop` = suma de `balance_cop` positivos en todos los períodos del rango
- Descuentos (`platform_discounts`) aplicados en el cálculo de `amount_owed_cop` si aplican al período

**Errores**
| Código | Condición |
|---|---|
| 400 | `from_period` o `to_period` con formato inválido |
| 400 | `to_period` < `from_period` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.billing.tenant_detail"` `{ opsUserId, tenantId, fromPeriod, toPeriod }`
- Métrica: `platform.billing_tenant_detail` (counter)

**Casos de Aceptación**
- [ ] Happy path: historial completo con pagos anidados por período
- [ ] Tenant con abono parcial: `payment_status='partial'`, `balance_cop > 0`
- [ ] Tenant sin pagos en período: `amount_paid_cop=0`, `payment_status='pending'`
- [ ] Filtro de períodos aplicado correctamente
- [ ] `to_period` anterior a `from_period` devuelve 400
- [ ] `tenantId` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### PATCH /api/v1/platform/billing/{tenantId}

**Objetivo**
Cambiar el plan de facturación de un tenant, estableciendo la tasa de comisión, el monto de suscripción o ambos según el plan seleccionado.

**Auth**
Platform Admin JWT required. Roles permitidos: `billing`, `admin`. Rol `soporte` **no** tiene acceso.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "plan": "subscription",
  "subscription_amount_cop": 350000,
  "commission_rate": null,
  "effective_from": "2026-07-01",
  "reason": "Migración de trial a plan subscription acordado en llamada del 2026-06-07."
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_plan": "none",
  "new_plan": "subscription",
  "previous_subscription_amount_cop": null,
  "new_subscription_amount_cop": 350000,
  "previous_commission_rate": null,
  "new_commission_rate": null,
  "effective_from": "2026-07-01",
  "changed_by": "ops-uuid-0001",
  "changed_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `plan`: requerido, ENUM `none | commission | subscription | hybrid`
- `subscription_amount_cop`: entero > 0, máximo `50_000_000`; requerido si `plan = 'subscription'` o `'hybrid'`; debe ser `null` si `plan = 'none'` o `'commission'`
- `commission_rate`: DECIMAL, rango `0.01`–`50.00` (porcentaje); requerido si `plan = 'commission'` o `'hybrid'`; debe ser `null` si `plan = 'none'` o `'subscription'`
- `effective_from`: requerido, fecha ISO `YYYY-MM-DD`, debe ser `>= hoy`
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- Plan `commission` o `hybrid` requiere `tenants.nit_verified = true`; si es `false`, devolver 409 con mensaje indicando que se debe verificar el NIT primero
- Actualiza `tenants.plan`, `tenants.commission_rate`, `tenants.subscription_amount_cop` de forma atómica
- Registro en `platform_audit_log`: `action='billing.plan_changed'`, `details={ previousPlan, newPlan, subscriptionAmountCop, commissionRate, effectiveFrom, reason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `plan` ausente o valor inválido |
| 400 | `subscription_amount_cop` ausente cuando `plan=subscription` o `hybrid` |
| 400 | `commission_rate` ausente cuando `plan=commission` o `hybrid` |
| 400 | `commission_rate` fuera del rango `0.01`–`50.00` |
| 400 | `subscription_amount_cop` <= 0 |
| 400 | `effective_from` en el pasado |
| 400 | `reason` ausente o longitud fuera de rango |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol es `soporte` (no tiene permiso para cambiar planes) |
| 403 | Rol no es `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |
| 409 | `plan=commission` o `hybrid` con `nit_verified=false` |

**Observabilidad**
- Log: `"platform.billing.plan_changed"` `{ opsUserId, tenantId, previousPlan, newPlan, effectiveFrom }`
- Métrica: `platform.billing_plan_changed` (counter)

**Casos de Aceptación**
- [ ] Happy path `plan=subscription`: `tenants.subscription_amount_cop` actualizado, audit log creado
- [ ] Happy path `plan=commission` con `nit_verified=true`: `commission_rate` actualizado
- [ ] `plan=commission` con `nit_verified=false` devuelve 409
- [ ] `plan=hybrid` requiere ambos `subscription_amount_cop` y `commission_rate`
- [ ] `commission_rate=0` devuelve 400
- [ ] `commission_rate=50.01` devuelve 400
- [ ] `effective_from` ayer devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] `tenantId` no existente devuelve 404

---

### POST /api/v1/platform/billing/{tenantId}/payment

**Objetivo**
Registrar un pago manual recibido de un tenant con referencia bancaria y período de facturación correspondiente.

**Auth**
Platform Admin JWT required. Roles permitidos: `billing`, `admin`. Rol `soporte` **no** tiene acceso.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "amount_cop": 350000,
  "billing_period": "2026-06",
  "payment_date": "2026-06-03",
  "reference": "PSE-20260603-001",
  "notes": "Transferencia bancaria recibida. Confirmada con extracto bancario del 2026-06-03."
}
```

**Response 201**
```json
{
  "id": "pay-uuid-0001",
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "amount_cop": 350000,
  "billing_period": "2026-06",
  "payment_date": "2026-06-03",
  "reference": "PSE-20260603-001",
  "notes": "Transferencia bancaria recibida. Confirmada con extracto bancario del 2026-06-03.",
  "recorded_by": "ops-uuid-0001",
  "created_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `amount_cop`: requerido, entero > 0, máximo `500_000_000`
- `billing_period`: requerido, formato `YYYY-MM`
- `payment_date`: requerido, fecha ISO `YYYY-MM-DD`, no puede ser fecha futura
- `reference`: requerido, 3–200 caracteres
- `notes`: opcional, máximo 1000 caracteres

**Reglas de Negocio**
- Insertar en `platform_payments`; `recorded_by = JWT.sub`
- Detección de duplicados: verificar que no exista ya un pago con `(tenant_id, reference)` idénticos; si existe, devolver 409
- Registro en `platform_audit_log`: `action='billing.payment_registered'`, `details={ amountCop, billingPeriod, reference }`

**Errores**
| Código | Condición |
|---|---|
| 400 | Campo requerido ausente o inválido |
| 400 | `amount_cop` <= 0 |
| 400 | `payment_date` en el futuro |
| 400 | `billing_period` con formato inválido |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol es `soporte` (no tiene permiso) |
| 403 | Rol no es `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |
| 409 | Pago con la misma `reference` ya existe para este tenant |

**Observabilidad**
- Log: `"platform.billing.payment_registered"` `{ opsUserId, tenantId, amountCop, billingPeriod, reference }`
- Métrica: `platform.billing_payment_registered` (counter)

**Casos de Aceptación**
- [ ] Happy path: pago registrado, audit log creado
- [ ] `reference` duplicada para mismo tenant devuelve 409
- [ ] Misma `reference` para tenant diferente es permitido (no es duplicado)
- [ ] `payment_date` mañana devuelve 400
- [ ] `amount_cop=0` devuelve 400
- [ ] `billing_period="2026/06"` devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] `tenantId` no existente devuelve 404

---

### POST /api/v1/platform/billing/{tenantId}/discount

**Objetivo**
Aplicar un descuento o crédito a la cuenta de facturación de un tenant con justificación documentada, por ejemplo como compensación por incidentes.

**Auth**
Platform Admin JWT required. Roles permitidos: `billing`, `admin`. Rol `soporte` **no** tiene acceso.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "type": "credit",
  "amount_cop": 175000,
  "billing_period": "2026-06",
  "reason": "Compensación acordada por incidente de plataforma del 2026-05-28 (Ticket #INC-2451). Downtime de 4 horas durante hora pico.",
  "expires_at": null
}
```

**Response 201**
```json
{
  "id": "disc-uuid-0001",
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "type": "credit",
  "amount_cop": 175000,
  "percentage": null,
  "billing_period": "2026-06",
  "reason": "Compensación acordada por incidente de plataforma...",
  "expires_at": null,
  "applied_by": "ops-uuid-0001",
  "created_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `type`: requerido, ENUM `credit | discount_percentage | discount_fixed`
- `amount_cop`: requerido si `type = 'credit'` o `'discount_fixed'`; entero > 0
- `percentage`: requerido si `type = 'discount_percentage'`; DECIMAL `1.00`–`100.00`; mutuamente excluyente con `amount_cop`
- `billing_period`: requerido, formato `YYYY-MM`
- `reason`: requerido, 20–1000 caracteres
- `expires_at`: ISO datetime o `null`; si se proporciona, debe ser fecha futura

**Reglas de Negocio**
- Insertar en `platform_discounts`; `applied_by = JWT.sub`
- Para `type=discount_percentage`: `amount_cop` se deja `null` en DB; el monto se calcula en tiempo de facturación aplicando el porcentaje sobre `amount_owed`
- Para `type=credit` o `discount_fixed`: `amount_cop` se resta directamente del `amount_owed` en el período indicado
- Registro en `platform_audit_log`: `action='billing.discount_applied'`, `details={ type, amountCop, percentage, billingPeriod, reason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `type` ausente o valor inválido |
| 400 | `amount_cop` ausente cuando `type` lo requiere, o `amount_cop` <= 0 |
| 400 | `percentage` ausente cuando `type=discount_percentage`, o fuera de rango |
| 400 | `reason` ausente o longitud fuera de rango (< 20 chars) |
| 400 | `billing_period` con formato inválido |
| 400 | `expires_at` en el pasado |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol es `soporte` (no tiene permiso) |
| 403 | Rol no es `billing` ni `admin` |
| 404 | `tenantId` no existe en `tenants` |

**Observabilidad**
- Log: `"platform.billing.discount_applied"` `{ opsUserId, tenantId, type, amountCop, percentage, billingPeriod }`
- Métrica: `platform.billing_discount_applied` (counter)

**Casos de Aceptación**
- [ ] Happy path `type=credit`: descuento creado, se resta de `amount_owed` en el período
- [ ] Happy path `type=discount_percentage`: `percentage` almacenado, `amount_cop=null`, cálculo diferido
- [ ] `reason` de 19 caracteres devuelve 400
- [ ] `percentage=0` devuelve 400
- [ ] `percentage=100.01` devuelve 400
- [ ] `amount_cop=0` con `type=credit` devuelve 400
- [ ] `expires_at` en el pasado devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] `tenantId` no existente devuelve 404

---

## CRM Trials

### GET /api/v1/platform/trials

**Objetivo**
Listar el pipeline de trials con filtros por estado, agente asignado y urgencia de expiración, ordenado por defecto por urgencia.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params:
  - `status`: `en_trial` | `expirado` | `en_negociacion` | `convertido` | `perdido` — opcional
  - `assigned_to`: UUID del Ops user — opcional
  - `expiring_in_days`: integer `1`–`90` — opcional, filtra trials que expiran en los próximos N días
  - `page`: integer — default `1`
  - `limit`: integer — default `20`, máximo `100`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "tenant_name": "Restaurante La Mesa",
      "tenant_city": "Bogotá",
      "owner_email": "owner@lamesa.com",
      "trial_status": "en_trial",
      "trial_ends_at": "2026-06-14T00:00:00Z",
      "days_remaining": 7,
      "assigned_to": {
        "id": "ops-uuid-0001",
        "name": "Ana García"
      },
      "notes_count": 2,
      "last_note_at": "2026-06-05T10:00:00Z",
      "total_orders_in_trial": 14,
      "tenant_created_at": "2026-06-01T00:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 32,
    "total_pages": 2
  }
}
```

**Validaciones**
- `status`: valor válido del ENUM si se proporciona
- `assigned_to`: UUID válido si se proporciona
- `expiring_in_days`: entero `1`–`90` si se proporciona
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `100`

**Reglas de Negocio**
- Ordenamiento por defecto: `days_remaining ASC` (los más urgentes primero); trials ya expirados (`trial_ends_at < NOW()`) aparecen primero con `days_remaining = 0` o negativo
- `expiring_in_days` aplica filtro: `trial_ends_at BETWEEN NOW() AND NOW() + N days`
- `notes_count` = `JSON_ARRAY_LENGTH(trial_pipeline.notes)`
- `last_note_at` = `MAX(note.created_at)` dentro del array JSONB
- `total_orders_in_trial` = COUNT de órdenes en la tabla `orders` con `tenant_id=X` durante el período en que `tenants.status='TRIAL'`

**Errores**
| Código | Condición |
|---|---|
| 400 | `status` con valor fuera del ENUM |
| 400 | `expiring_in_days` fuera de rango `1`–`90` |
| 400 | `assigned_to` con formato UUID inválido |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |

**Observabilidad**
- Log: `"platform.trials.list"` `{ opsUserId, filters: { status, assignedTo, expiringInDays }, resultCount }`
- Métrica: `platform.trials_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: lista ordenada por urgencia con todos los campos calculados
- [ ] `expiring_in_days=7`: solo trials que vencen en los próximos 7 días
- [ ] Filtro `assigned_to`: solo trials asignados al agente especificado
- [ ] `status=convertido`: incluye trials ya convertidos con `days_remaining` irrelevante
- [ ] `status=INVALIDO` devuelve 400
- [ ] `expiring_in_days=0` devuelve 400
- [ ] Rol sin permisos devuelve 403

---

### PATCH /api/v1/platform/trials/{tenantId}

**Objetivo**
Actualizar el estado del pipeline de un trial y/o reasignar el agente de ventas responsable, con restricciones de rol para marcar conversiones.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`. Marcar `convertido` requiere rol `billing` o `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body (al menos un campo requerido):
```json
{
  "status": "en_negociacion",
  "assigned_to_ops_user_id": "ops-uuid-0002"
}
```

**Response 200**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "previous_status": "en_trial",
  "new_status": "en_negociacion",
  "assigned_to_ops_user_id": "ops-uuid-0002",
  "updated_by": "ops-uuid-0001",
  "updated_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `status`: valor válido del ENUM `en_trial | expirado | en_negociacion | convertido | perdido` si se proporciona
- `assigned_to_ops_user_id`: UUID válido referenciando un `platform_user` activo si se proporciona
- Al menos un campo debe estar presente

**Reglas de Negocio**
- Si `status='convertido'` y rol del actor es `soporte`: devolver 403 indicando que se requiere rol `billing` o `admin`
- Si `status='convertido'`: actualizar `trial_pipeline.converted_at = NOW()`, `converted_by = opsUserId`; además, si `tenants.status != 'ACTIVE'`, actualizar `tenants.status = 'ACTIVE'`
- Si `status='perdido'`: actualizar `trial_pipeline.lost_at = NOW()`, `lost_by = opsUserId`
- `assigned_to_ops_user_id` debe referenciar un usuario con `platform_users.is_active = true`; si está inactivo, devolver 404
- Registro en `platform_audit_log`: `action='trial.status_updated'`, `details={ previousStatus, newStatus, assignedTo }`

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío (sin campos) |
| 400 | `status` con valor fuera del ENUM |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol no es `soporte`, `billing` ni `admin` |
| 403 | Rol es `soporte` intentando establecer `status='convertido'` |
| 404 | `tenantId` no encontrado en `trial_pipeline` |
| 404 | `assigned_to_ops_user_id` no existe o está inactivo |

**Observabilidad**
- Log: `"platform.trials.updated"` `{ opsUserId, tenantId, previousStatus, newStatus, assignedTo }`
- Métrica: `platform.trial_status_updated` (counter)

**Casos de Aceptación**
- [ ] Happy path cambio de estado: audit log creado con old/new status
- [ ] `status=convertido` por rol `billing`: `tenants.status=ACTIVE`, `converted_at` poblado
- [ ] `status=convertido` por rol `soporte`: devuelve 403
- [ ] `status=perdido`: `lost_at` y `lost_by` poblados
- [ ] Solo reasignación sin cambio de estado: `assigned_to` actualizado
- [ ] `assigned_to_ops_user_id` de usuario inactivo devuelve 404
- [ ] Body vacío devuelve 400
- [ ] `tenantId` sin fila en `trial_pipeline` devuelve 404
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/trials/{tenantId}/notes

**Objetivo**
Agregar una nota de contacto al historial del trial de forma append-only; las notas son inmutables una vez guardadas.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `tenantId`: UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "text": "Llamada realizada con el propietario. Muy interesado en plan subscription. Solicitó demo del módulo de reportes. Acordamos próximo contacto en 3 días para resolver dudas sobre integración con POS."
}
```

**Response 201**
```json
{
  "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "note": {
    "text": "Llamada realizada con el propietario. Muy interesado en plan subscription...",
    "created_by": "ops-uuid-0001",
    "created_by_name": "Ana García",
    "created_at": "2026-06-07T12:00:00Z"
  },
  "total_notes": 3
}
```

**Validaciones**
- `text`: requerido, 10–2000 caracteres

**Reglas de Negocio**
- Construir objeto de nota: `{ text, created_by: opsUserId, created_by_name: opsUserName, created_at: NOW() }`
- Hacer append al array JSONB `trial_pipeline.notes` mediante: `UPDATE trial_pipeline SET notes = notes || :newNote::jsonb WHERE tenant_id = :tenantId`
- Las notas son inmutables: no existe endpoint de edición o eliminación de notas
- `total_notes` = longitud del array después de la inserción
- Registro en `platform_audit_log`: `action='trial.note_added'`, `details={ tenantId, noteLength: text.length }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `text` ausente, < 10 caracteres o > 2000 caracteres |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `tenantId` no encontrado en `trial_pipeline` |

**Observabilidad**
- Log: `"platform.trials.note_added"` `{ opsUserId, tenantId, noteLength, totalNotes }`
- Métrica: `platform.trial_note_added` (counter)

**Casos de Aceptación**
- [ ] Happy path: nota agregada al array JSONB, `total_notes` incrementado
- [ ] Segunda nota: `total_notes=2`, notas previas intactas
- [ ] `text` de 9 caracteres devuelve 400
- [ ] `text` de 2001 caracteres devuelve 400
- [ ] Tenant existente pero sin fila en `trial_pipeline` devuelve 404
- [ ] Rol sin permisos devuelve 403

---

## Comunicaciones

### GET /api/v1/platform/announcements

**Objetivo**
Listar el historial de anuncios enviados y programados con filtros de estado y tipo de destinatarios.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params:
  - `status`: `sent` | `scheduled` | `draft` — opcional
  - `recipients`: `all` | `active` | `trial` | `specific` — opcional
  - `page`: integer — default `1`
  - `limit`: integer — default `20`, máximo `100`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "id": "ann-uuid-0001",
      "subject": "Nueva funcionalidad: reportes de comisiones disponibles",
      "body_preview": "Estimados restaurantes, nos complace anunciar el lanzamiento del nuevo módulo de reportes...",
      "recipients": "active",
      "recipient_tenant_id": null,
      "scheduled_at": null,
      "sent_at": "2026-06-01T14:00:00Z",
      "sent_by": {
        "id": "ops-uuid-0001",
        "name": "Ana García"
      },
      "recipient_count": 38,
      "status": "sent"
    },
    {
      "id": "ann-uuid-0002",
      "subject": "Mantenimiento programado este domingo",
      "body_preview": "Estimados restaurantes, les informamos que realizaremos mantenimiento programado...",
      "recipients": "all",
      "recipient_tenant_id": null,
      "scheduled_at": "2026-06-08T07:00:00Z",
      "sent_at": null,
      "sent_by": {
        "id": "ops-uuid-0001",
        "name": "Ana García"
      },
      "recipient_count": 0,
      "status": "scheduled"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 15,
    "total_pages": 1
  }
}
```

**Validaciones**
- `status`: valor válido del derivado si se proporciona
- `recipients`: valor válido del ENUM si se proporciona
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `100`

**Reglas de Negocio**
- `body_preview` = primeros 150 caracteres del `body`, con sufijo `"..."` si fue truncado
- `status` derivado: `sent_at IS NOT NULL → 'sent'`; `scheduled_at > NOW() AND sent_at IS NULL → 'scheduled'`; resto → `'draft'`
- Ordenado por `sent_at DESC` para enviados, `scheduled_at ASC` para programados, mezclados al final con enviados primero

**Errores**
| Código | Condición |
|---|---|
| 400 | `status` o `recipients` con valor inválido |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |

**Observabilidad**
- Log: `"platform.announcements.list"` `{ opsUserId, filters, resultCount }`
- Métrica: `platform.announcements_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: lista con `body_preview` truncado y `status` derivado correcto
- [ ] `status=scheduled`: solo anuncios futuros pendientes de envío
- [ ] `recipients=specific`: solo anuncios dirigidos a un tenant concreto
- [ ] `status=sent`: solo anuncios ya enviados
- [ ] Valor de `status` inválido devuelve 400
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/announcements

**Objetivo**
Crear y enviar inmediatamente o programar un anuncio para un grupo de tenants o para un tenant específico.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "subject": "Mantenimiento programado este domingo 8 de junio",
  "body": "Estimado restaurante,\n\nLes informamos que realizaremos mantenimiento programado el domingo 8 de junio de 2026 entre las 2:00 AM y 4:00 AM COT. Durante ese período la plataforma estará no disponible.\n\nDisculpen los inconvenientes.\n\nEquipo Mesto",
  "recipients": "active",
  "recipient_tenant_id": null,
  "scheduled_at": "2026-06-07T18:00:00Z"
}
```

**Response 201**
```json
{
  "id": "ann-uuid-0003",
  "subject": "Mantenimiento programado este domingo 8 de junio",
  "recipients": "active",
  "recipient_tenant_id": null,
  "scheduled_at": "2026-06-07T18:00:00Z",
  "sent_at": null,
  "recipient_count": 38,
  "status": "scheduled",
  "created_by": "ops-uuid-0001",
  "created_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `subject`: requerido, 5–300 caracteres
- `body`: requerido, 20–10000 caracteres
- `recipients`: requerido, ENUM `all | active | trial | specific`
- `recipient_tenant_id`: UUID requerido si `recipients='specific'`; debe ser `null` si `recipients != 'specific'`
- `scheduled_at`: ISO datetime o `null`; si se proporciona, debe ser un momento futuro (> NOW())

**Reglas de Negocio**
- Si `scheduled_at=null`: enviar inmediatamente vía SDD-007 con template `platform_announcement`; establecer `sent_at=NOW()`; `recipient_count` calculado en el momento del envío
- Si `scheduled_at` es futuro: encolar tarea programada (cron/job); `sent_at=null`; `recipient_count` calculado en el momento del envío real, no en la creación
- Para `recipients='specific'`: verificar que `recipient_tenant_id` exista en `tenants`
- Registro en `platform_audit_log`: `action='announcement.created'`, `details={ subject, recipients, recipientTenantId, scheduledAt }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `subject` o `body` ausentes o longitud fuera de rango |
| 400 | `recipients` ausente o valor inválido |
| 400 | `recipients='specific'` sin `recipient_tenant_id` |
| 400 | `recipients != 'specific'` con `recipient_tenant_id` no nulo |
| 400 | `scheduled_at` en el pasado |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `recipient_tenant_id` no existe en `tenants` (cuando `recipients='specific'`) |

**Observabilidad**
- Log: `"platform.announcements.created"` `{ opsUserId, announcementId, recipients, recipientTenantId, scheduledAt, immediate: scheduledAt == null }`
- Métrica: `platform.announcement_created` (counter)

**Casos de Aceptación**
- [ ] Happy path envío inmediato: `sent_at` poblado, emails despachados, `recipient_count` registrado
- [ ] Happy path programado: `status='scheduled'`, `sent_at=null`, tarea encolada
- [ ] `recipients='specific'` con tenant válido: solo ese tenant recibe el email
- [ ] `recipients='specific'` sin `recipient_tenant_id` devuelve 400
- [ ] `recipient_tenant_id` no nulo con `recipients='active'` devuelve 400
- [ ] `scheduled_at` hace 5 minutos devuelve 400
- [ ] `recipient_tenant_id` no existente devuelve 404
- [ ] Rol sin permisos devuelve 403

---

## Gestión del Equipo

### GET /api/v1/platform/team

**Objetivo**
Listar los miembros del equipo Ops con sus roles asignados, estado de actividad y fecha del último acceso.

**Auth**
Platform Admin JWT required. Rol requerido: `admin` únicamente.

**Request**
- Path params: Ninguno
- Query params:
  - `active_only`: booleano — default `true`
  - `role`: `soporte` | `billing` | `admin` — opcional, filtra por rol asignado
  - `page`: integer — default `1`
  - `limit`: integer — default `20`, máximo `100`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "id": "ops-uuid-0001",
      "name": "Ana García",
      "email": "ana@mesto.app",
      "roles": ["soporte", "billing"],
      "is_active": true,
      "created_at": "2026-01-15T00:00:00Z",
      "last_login_at": "2026-06-07T09:00:00Z"
    },
    {
      "id": "ops-uuid-0002",
      "name": "Carlos Rodríguez",
      "email": "carlos@mesto.app",
      "roles": ["soporte"],
      "is_active": true,
      "created_at": "2026-03-01T00:00:00Z",
      "last_login_at": "2026-06-06T17:30:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 8,
    "total_pages": 1
  }
}
```

**Validaciones**
- `role`: valor válido del ENUM si se proporciona
- `active_only`: booleano si se proporciona
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `100`

**Reglas de Negocio**
- `roles` construido mediante JOIN `platform_user_roles` agrupado por `user_id`
- Si `active_only=true` (default): filtra `platform_users.is_active = true`
- Si `active_only=false`: incluye miembros desactivados; `roles` puede estar vacío para desactivados
- `role` filter: filtra users que tengan al menos ese rol asignado en `platform_user_roles`

**Errores**
| Código | Condición |
|---|---|
| 400 | `role` con valor fuera del ENUM |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `admin` |

**Observabilidad**
- Log: `"platform.team.list"` `{ opsUserId, filters, resultCount }`
- Métrica: `platform.team_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: lista de miembros activos con roles y último acceso
- [ ] `active_only=false`: incluye miembros desactivados
- [ ] `role=billing`: solo miembros con rol `billing`
- [ ] `role=INVALIDO` devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] Rol `billing` devuelve 403
- [ ] JWT ausente devuelve 401

---

### POST /api/v1/platform/team

**Objetivo**
Crear un nuevo miembro del equipo Ops con roles iniciales y enviarle una invitación de acceso por correo electrónico.

**Auth**
Platform Admin JWT required. Rol requerido: `admin` únicamente.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "name": "Carlos Rodríguez",
  "email": "carlos@mesto.app",
  "roles": ["soporte"]
}
```

**Response 201**
```json
{
  "id": "ops-uuid-0002",
  "name": "Carlos Rodríguez",
  "email": "carlos@mesto.app",
  "roles": ["soporte"],
  "is_active": true,
  "created_by": "ops-uuid-0001",
  "created_at": "2026-06-07T12:00:00Z",
  "invitation_sent": true
}
```

**Validaciones**
- `name`: requerido, 2–100 caracteres
- `email`: requerido, formato email válido, máximo 300 caracteres; debe terminar en el dominio ops configurado (ej. `@mesto.app`)
- `roles`: requerido, array no vacío; cada elemento debe ser `soporte`, `billing` o `admin`

**Reglas de Negocio**
- `email` debe ser único en `platform_users`; si ya existe, devolver 409
- Insertar en `platform_users` con `is_active=true`, `created_by=JWT.sub`
- Insertar cada rol en `platform_user_roles`
- DEL `ops_roles:{newUserId}` en Redis si existiera (precaución contra colisiones de UUID)
- Enviar email de invitación vía SDD-007 con template `ops_invite` que incluye enlace de activación con token temporal (TTL 48h)
- Registro en `platform_audit_log`: `action='team.member_created'`, `details={ name, email, roles }`

**Errores**
| Código | Condición |
|---|---|
| 400 | Campo requerido ausente o inválido |
| 400 | `roles` es array vacío |
| 400 | Valor de rol inválido en el array |
| 400 | Email con dominio fuera del dominio ops configurado |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `admin` |
| 409 | Email ya registrado en `platform_users` |

**Observabilidad**
- Log: `"platform.team.member_created"` `{ adminUserId, newUserId, name, email, roles }`
- Métrica: `platform.team_member_created` (counter)

**Casos de Aceptación**
- [ ] Happy path: usuario creado, roles asignados, email de invitación enviado
- [ ] Email duplicado devuelve 409
- [ ] `roles=[]` devuelve 400
- [ ] `roles=["superadmin"]` (valor inválido) devuelve 400
- [ ] Email con dominio externo (ej. `@gmail.com`) devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] Rol `billing` devuelve 403

---

### PATCH /api/v1/platform/team/{userId}

**Objetivo**
Editar el nombre de un miembro del equipo Ops o desactivar su acceso a la plataforma con razón documentada.

**Auth**
Platform Admin JWT required. Rol requerido: `admin` únicamente.

**Request**
- Path params:
  - `userId`: UUID del miembro del equipo
- Query params: Ninguno
- Body (al menos un campo requerido):
```json
{
  "name": "Carlos R. Martínez",
  "is_active": false,
  "deactivation_reason": "Renuncia voluntaria efectiva el 2026-06-07. Proceso de offboarding completado."
}
```

**Response 200**
```json
{
  "id": "ops-uuid-0002",
  "name": "Carlos R. Martínez",
  "email": "carlos@mesto.app",
  "roles": ["soporte"],
  "is_active": false,
  "deactivated_at": "2026-06-07T12:00:00Z",
  "deactivated_by": "ops-uuid-0001"
}
```

**Validaciones**
- Al menos un campo debe estar presente
- `name`: 2–100 caracteres si se proporciona
- `is_active`: booleano si se proporciona
- `deactivation_reason`: requerido si `is_active=false`; 10–500 caracteres

**Reglas de Negocio**
- No se puede desactivar la propia cuenta (`userId == JWT.sub`): devolver 400
- Si `is_active=false`: DEL `ops_roles:{userId}` en Redis; invalidar cualquier sesión de impersonación activa creada por este usuario (DEL `impersonation:{userId}:*` en Redis); actualizar `platform_users.is_active=false`, `deactivated_at=NOW()`, `deactivated_by=JWT.sub`
- No es posible cambiar el email por este endpoint
- Registro en `platform_audit_log`: `action='team.member_updated'`, `details={ changes, deactivationReason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío |
| 400 | `is_active=false` sin `deactivation_reason` |
| 400 | `userId` es el mismo que el actor (auto-desactivación) |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `admin` |
| 404 | `userId` no existe en `platform_users` |

**Observabilidad**
- Log: `"platform.team.member_updated"` `{ adminUserId, targetUserId, isActive, changes }`
- Métrica: `platform.team_member_updated` (counter)

**Casos de Aceptación**
- [ ] Happy path `name` update: nombre actualizado, audit log creado
- [ ] Desactivar usuario: `is_active=false`, Redis roles eliminado, impersonaciones invalidadas
- [ ] Auto-desactivación (`userId == JWT.sub`) devuelve 400
- [ ] `is_active=false` sin `deactivation_reason` devuelve 400
- [ ] `deactivation_reason` de 9 caracteres devuelve 400
- [ ] Body vacío devuelve 400
- [ ] `userId` no existente devuelve 404
- [ ] Rol `soporte` devuelve 403

---

### PATCH /api/v1/platform/team/{userId}/roles

**Objetivo**
Reemplazar completamente la lista de roles de un miembro del equipo Ops, forzando la expiración inmediata del caché de roles en Redis.

**Auth**
Platform Admin JWT required. Rol requerido: `admin` únicamente.

**Request**
- Path params:
  - `userId`: UUID del miembro del equipo
- Query params: Ninguno
- Body:
```json
{
  "roles": ["soporte", "billing"],
  "reason": "Ascenso: Carlos asume responsabilidad de billing además de soporte a partir del 2026-07-01."
}
```

**Response 200**
```json
{
  "id": "ops-uuid-0002",
  "name": "Carlos Rodríguez",
  "email": "carlos@mesto.app",
  "previous_roles": ["soporte"],
  "new_roles": ["soporte", "billing"],
  "changed_by": "ops-uuid-0001",
  "changed_at": "2026-06-07T12:00:00Z"
}
```

**Validaciones**
- `roles`: requerido, array no vacío; cada elemento debe ser `soporte`, `billing` o `admin`; mínimo 1 rol
- `reason`: requerido, 10–500 caracteres

**Reglas de Negocio**
- No se pueden modificar los propios roles (`userId == JWT.sub`): devolver 400
- Reemplazar en transacción: DELETE `platform_user_roles WHERE user_id=userId`, luego INSERT nuevos roles con `granted_by=JWT.sub`, `granted_at=NOW()`
- DEL `ops_roles:{userId}` en Redis para forzar recarga en la próxima petición del usuario
- Registro en `platform_audit_log`: `action='team.roles_changed'`, `details={ previousRoles, newRoles, reason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `roles` ausente o array vacío |
| 400 | Valor de rol inválido en el array |
| 400 | `reason` ausente o longitud fuera de rango |
| 400 | `userId` es el mismo que el actor (auto-modificación) |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `admin` |
| 404 | `userId` no existe en `platform_users` |

**Observabilidad**
- Log: `"platform.team.roles_changed"` `{ adminUserId, targetUserId, previousRoles, newRoles }`
- Métrica: `platform.team_roles_changed` (counter)

**Casos de Aceptación**
- [ ] Happy path: roles reemplazados, Redis key eliminado, audit log creado
- [ ] Degradación de `admin` a `soporte`: funciona correctamente
- [ ] `roles=[]` devuelve 400
- [ ] `roles=["superadmin"]` devuelve 400
- [ ] `reason` de 9 caracteres devuelve 400
- [ ] Auto-modificación de roles devuelve 400
- [ ] `userId` no existente devuelve 404
- [ ] Rol `billing` devuelve 403

---

## Audit Log

### GET /api/v1/platform/audit

**Objetivo**
Consultar el log de auditoría de acciones en la plataforma; los administradores ven todos los registros, el resto solo ve sus propias acciones.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`. El rol `admin` tiene visibilidad global; los demás roles solo ven sus propias acciones (backend-enforced).

**Request**
- Path params: Ninguno
- Query params:
  - `tenant_id`: UUID — opcional, filtra por tenant afectado
  - `actor_id`: UUID — opcional; solo `admin` puede especificar un `actor_id` diferente al propio; no-admins reciben 403 si especifican otro `actor_id`
  - `action`: string — opcional, coincidencia parcial (`ILIKE '%valor%'`)
  - `from`: ISO datetime — opcional
  - `to`: ISO datetime — opcional; debe ser `>= from` si se especifican ambos
  - `page`: integer — default `1`
  - `limit`: integer — default `50`, máximo `200`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "id": "audit-uuid-0001",
      "actor_id": "ops-uuid-0001",
      "actor_name": "Ana García",
      "actor_type": "platform_admin",
      "tenant_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "tenant_name": "Restaurante La Mesa",
      "action": "tenant.suspended",
      "details": {
        "reason": "Falta de pago por 2 períodos",
        "previousStatus": "ACTIVE",
        "notifyOwner": true
      },
      "created_at": "2026-06-07T12:00:00Z"
    },
    {
      "id": "audit-uuid-0002",
      "actor_id": "ops-uuid-0001",
      "actor_name": "Ana García",
      "actor_type": "platform_admin",
      "tenant_id": null,
      "tenant_name": null,
      "action": "team.member_created",
      "details": {
        "name": "Carlos Rodríguez",
        "email": "carlos@mesto.app",
        "roles": ["soporte"]
      },
      "created_at": "2026-06-07T12:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 50,
    "total": 2847,
    "total_pages": 57
  }
}
```

**Validaciones**
- `tenant_id`: UUID válido si se proporciona
- `actor_id`: UUID válido si se proporciona
- `from`, `to`: ISO datetime válido si se proporciona; `to >= from` si se especifican ambos
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `200`

**Reglas de Negocio**
- Roles no-`admin`: WHERE `actor_id = JWT.sub` aplicado en backend; el parámetro `actor_id` del query es ignorado si coincide con el propio `JWT.sub`, y rechazado con 403 si es diferente
- Rol `admin`: acceso sin restricción de actor; puede filtrar por cualquier `actor_id`
- `tenant_name` resuelto vía JOIN `tenants`; es `null` si `tenant_id IS NULL` en la entrada
- Resultados ordenados por `created_at DESC`

**Errores**
| Código | Condición |
|---|---|
| 400 | UUID inválido en `tenant_id` o `actor_id` |
| 400 | Formato de datetime inválido en `from` o `to` |
| 400 | `to` < `from` |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol no es `soporte`, `billing` ni `admin` |
| 403 | No-admin especificando `actor_id` de otro usuario |

**Observabilidad**
- Log: `"platform.audit.queried"` `{ opsUserId, filters: { tenantId, actorId, action, from, to }, resultCount, isAdminView: role=='admin' }`
- Métrica: `platform.audit_log_queried` (counter)

**Casos de Aceptación**
- [ ] Admin happy path: log global sin restricción de actor
- [ ] Admin filtra por `actor_id` de otro usuario: funciona correctamente
- [ ] No-admin: solo ve sus propias acciones, independientemente de otros filtros
- [ ] No-admin especifica `actor_id` de otro usuario: devuelve 403
- [ ] Filtro `action='tenant.suspended'`: solo entradas de suspensión
- [ ] `to` anterior a `from` devuelve 400
- [ ] Rol sin permisos devuelve 403

---

## Monitoreo y Alertas

### GET /api/v1/platform/monitoring

**Objetivo**
Obtener el dashboard de monitoreo de la plataforma con métricas de salud de API, actividad de órdenes, sesiones activas y KPIs de tenants en tiempo real.

**Auth**
Platform Admin JWT required. Rol requerido: `admin` únicamente.

**Request**
- Path params: Ninguno
- Query params:
  - `window`: `1h` | `6h` | `24h` | `7d` — default `1h`
- Body: No aplica

**Response 200**
```json
{
  "generated_at": "2026-06-07T12:00:00Z",
  "window": "1h",
  "health": {
    "api_latency_p95_ms": 187,
    "api_latency_p99_ms": 340,
    "error_rate_5xx_percent": 0.12,
    "uptime_percent_30d": 99.94
  },
  "orders": {
    "total_in_window": 1842,
    "avg_per_minute": 30.7,
    "peak_per_minute": 78
  },
  "sessions": {
    "active_now": 312,
    "stuck_count": 2,
    "avg_duration_min": 47
  },
  "tenants": {
    "total": 145,
    "active": 89,
    "trial": 23,
    "suspended": 4,
    "cancelled": 12,
    "pending": 17
  },
  "trials": {
    "expiring_in_7d": 5,
    "expired_unconverted": 3
  },
  "alerts_summary": {
    "critical": 1,
    "warning": 3,
    "info": 2
  }
}
```

**Validaciones**
- `window`: debe ser uno de `[1h, 6h, 24h, 7d]`

**Reglas de Negocio**
- `health.api_latency_p95_ms` y `p99_ms`: consultados desde sistema de métricas (InfluxDB/Prometheus) para el `window` dado
- `health.error_rate_5xx_percent`: porcentaje de respuestas 5xx en el `window`
- `sessions.active_now`: COUNT de claves Redis con patrón `session:*:*` (o contador dedicado `sessions:active:count`)
- `sessions.stuck_count`: sesiones donde `last_activity_at < NOW() - 5h`; consultado desde Redis metadata o tabla `sessions`
- `trials.expiring_in_7d`: COUNT WHERE `tenants.status='TRIAL' AND trial_ends_at BETWEEN NOW() AND NOW()+7d`
- `trials.expired_unconverted`: COUNT WHERE `tenants.status='TRIAL' AND trial_ends_at < NOW()` y `trial_pipeline.status NOT IN ('convertido','perdido')`
- `alerts_summary`: COUNT por `severity` WHERE `acknowledged_at IS NULL` desde tabla `alerts`
- `tenants.*`: conteos en tiempo real desde `tenants` agrupados por `status`

**Errores**
| Código | Condición |
|---|---|
| 400 | `window` con valor fuera de los permitidos |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `admin` |

**Observabilidad**
- Log: `"platform.monitoring.dashboard_viewed"` `{ opsUserId, window }`
- Métrica: `platform.monitoring_dashboard_viewed` (counter)

**Casos de Aceptación**
- [ ] Happy path `window=1h`: todas las secciones pobladas con datos del último hora
- [ ] `window=7d`: métricas de mayor ventana, `orders.total` considerablemente mayor
- [ ] Sin sesiones activas: `sessions.active_now=0`, `sessions.stuck_count=0`
- [ ] Sin alertas activas: `alerts_summary` todos en 0
- [ ] `window=30m` (valor inválido) devuelve 400
- [ ] Rol `soporte` devuelve 403
- [ ] Rol `billing` devuelve 403

---

### GET /api/v1/platform/alerts

**Objetivo**
Listar las alertas activas de la plataforma filtradas por tipo, severidad y estado de reconocimiento.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params: Ninguno
- Query params:
  - `type`: `trial_expiring` | `stuck_session` | `api_degradation` | `payment_error` | `zero_activity` — opcional
  - `severity`: `info` | `warning` | `critical` — opcional
  - `acknowledged`: booleano — default `false` (solo alertas no reconocidas)
  - `page`: integer — default `1`
  - `limit`: integer — default `50`, máximo `200`
- Body: No aplica

**Response 200**
```json
{
  "data": [
    {
      "id": "alert-uuid-0001",
      "type": "trial_expiring",
      "resource_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "resource_name": "Restaurante La Mesa",
      "message": "Trial expira en 3 días sin plan asignado.",
      "severity": "warning",
      "created_at": "2026-06-07T05:00:00Z",
      "acknowledged_at": null,
      "acknowledged_by": null,
      "acknowledged_by_name": null
    },
    {
      "id": "alert-uuid-0002",
      "type": "stuck_session",
      "resource_id": "sess-uuid-0099",
      "resource_name": "Restaurante La Mesa — Mesa 5",
      "message": "Sesión activa sin actividad desde hace 6.2 horas.",
      "severity": "warning",
      "created_at": "2026-06-07T06:00:00Z",
      "acknowledged_at": null,
      "acknowledged_by": null,
      "acknowledged_by_name": null
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 50,
    "total": 6,
    "total_pages": 1
  }
}
```

**Validaciones**
- `type`: valor válido del ENUM si se proporciona
- `severity`: valor válido del ENUM si se proporciona
- `acknowledged`: booleano si se proporciona
- `page`: mínimo `1`
- `limit`: mínimo `1`, máximo `200`

**Reglas de Negocio**
- Ordenamiento por defecto: `severity DESC` (critical → warning → info), luego `created_at ASC` (más antigua primero)
- `resource_name` resuelto según `type`: para alertas de tenant, JOIN `tenants.name`; para `stuck_session`, nombre del tenant + número de mesa desde `sessions`
- Cuando `acknowledged=false` (default): WHERE `acknowledged_at IS NULL`
- Cuando `acknowledged=true`: WHERE `acknowledged_at IS NOT NULL`

**Errores**
| Código | Condición |
|---|---|
| 400 | `type` o `severity` con valor fuera del ENUM |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |

**Observabilidad**
- Log: `"platform.alerts.list"` `{ opsUserId, filters: { type, severity, acknowledged }, resultCount }`
- Métrica: `platform.alerts_list` (counter)

**Casos de Aceptación**
- [ ] Happy path: alertas no reconocidas ordenadas por severity DESC
- [ ] `acknowledged=true`: historial de alertas reconocidas
- [ ] `severity=critical`: solo alertas críticas
- [ ] `type=stuck_session`: solo alertas de sesiones atascadas
- [ ] Sin alertas activas: `data=[]`, `total=0`, status 200
- [ ] `type=INVALIDO` devuelve 400
- [ ] Rol sin permisos devuelve 403

---

### POST /api/v1/platform/alerts/{alertId}/ack

**Objetivo**
Marcar una alerta como reconocida por un operador Ops, indicando que está siendo atendida y eliminando la clave de Redis correspondiente.

**Auth**
Platform Admin JWT required. Roles permitidos: `soporte`, `billing`, `admin`.

**Request**
- Path params:
  - `alertId`: UUID de la alerta
- Query params: Ninguno
- Body:
```json
{
  "notes": "Contactado cliente. Trial extendido 7 días. Seguimiento programado para el 2026-06-10."
}
```

**Response 200**
```json
{
  "id": "alert-uuid-0001",
  "type": "trial_expiring",
  "resource_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "severity": "warning",
  "acknowledged_at": "2026-06-07T12:00:00Z",
  "acknowledged_by": "ops-uuid-0001",
  "acknowledged_by_name": "Ana García",
  "notes": "Contactado cliente. Trial extendido 7 días. Seguimiento programado para el 2026-06-10."
}
```

**Validaciones**
- `alertId`: UUID válido (path param)
- `notes`: opcional, máximo 500 caracteres

**Reglas de Negocio**
- UPDATE `alerts SET acknowledged_at=NOW(), acknowledged_by=JWT.sub` WHERE `id=alertId AND acknowledged_at IS NULL`
- Si la alerta ya estaba reconocida (`acknowledged_at IS NOT NULL`): devolver 409 con `acknowledged_at` y `acknowledged_by` del reconocimiento anterior
- DEL clave Redis `alerts:{type}:{resourceId}` correspondiente a la alerta
- Registro en `platform_audit_log`: `action='alert.acknowledged'`, `details={ alertId, alertType, resourceId, notes }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `alertId` no es UUID válido |
| 400 | `notes` excede 500 caracteres |
| 401 | JWT ausente, expirado o malformado |
| 403 | Rol del actor no es `soporte`, `billing` ni `admin` |
| 404 | `alertId` no existe en `alerts` |
| 409 | Alerta ya reconocida; respuesta incluye `acknowledged_at` y `acknowledged_by` previos |

**Observabilidad**
- Log: `"platform.alerts.acknowledged"` `{ opsUserId, alertId, alertType, resourceId }`
- Métrica: `platform.alert_acknowledged` (counter)

**Casos de Aceptación**
- [ ] Happy path: `acknowledged_at` y `acknowledged_by` poblados, clave Redis eliminada, audit log creado
- [ ] Con `notes`: notas persistidas en la respuesta y audit log
- [ ] Sin `notes` (body vacío `{}`): reconocimiento exitoso, `notes=null`
- [ ] Alerta ya reconocida devuelve 409 con datos del reconocimiento previo
- [ ] `alertId` con formato UUID inválido devuelve 400
- [ ] `alertId` no existente devuelve 404
- [ ] `notes` de 501 caracteres devuelve 400
- [ ] Rol sin permisos devuelve 403
