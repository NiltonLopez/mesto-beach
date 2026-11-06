# SDD-007 — Notifications

## Servicio
**Módulo**: Notifications  
**Base path**: `/api/v1/notifications`, `/hubs/realtime`  
**Responsabilidad**: Entrega de notificaciones en tiempo real (SignalR) y transaccionales (email). Actúa como servicio de infraestructura consumido por todos los demás módulos.

**PRDs de referencia**: PRD-008 (Notificaciones y Comunicación), PRD-011 (OQ-1103: PIN regenerado → SignalR al mesero)

**Dependencias**:
- Redis: `maintenance:*`
- PostgreSQL: `email_templates`, `announcements`, `audit_log`
- Azure Email Communication Services / SendGrid: envío de emails
- ASP.NET Core SignalR: hubs y grupos

---

## Referencia de Grupos SignalR

| Grupo | Miembros | Descripción |
|---|---|---|
| `tenant:{tenantId}` | Todo el staff conectado | Cajeros, meseros, admin, cocina — reciben todos los eventos del tenant |
| `session:{tenantId}:{sessionId}` | Participantes de la sesión | App de menú del cliente — actualizaciones de estado de sus órdenes |
| `station:{tenantId}:{stationId}` | Dispositivo KDS de esa estación | Recibe solo las órdenes enrutadas a esa estación |
| `staff:{userId}` | Miembro del staff individual | Notificaciones personales (e.g. PIN regenerado) |

## Catálogo de Eventos SignalR

| Evento | Payload | Grupos destino |
|---|---|---|
| `order.created` | `{ orderId, sessionId, tableCode, participantId, items[], stationId }` | `station:{tenantId}:{stationId}`, `tenant:{tenantId}` |
| `order.status_changed` | `{ orderId, status, tableCode }` | `session:{tenantId}:{sessionId}`, `tenant:{tenantId}` |
| `order.cancelled` | `{ orderId, reason }` | `session:{tenantId}:{sessionId}`, `tenant:{tenantId}` |
| `session.opened` | `{ sessionId, tableCode, waiterId }` | `tenant:{tenantId}` |
| `session.participant_joined` | `{ sessionId, participantCount }` | `tenant:{tenantId}` |
| `session.closed` | `{ sessionId }` | `tenant:{tenantId}`, `session:{tenantId}:{sessionId}` |
| `pin.regenerated` | `{ sessionId, tableCode, newPin }` | `staff:{waiterId}` |
| `maintenance.updated` | `{ message, apps[], activeFrom, activeUntil }` | Todos los clientes conectados (broadcast sin grupo) |
| `loyalty.points_earned` | `{ customerId, points, balanceAfter, tenantName }` | `session:{tenantId}:{sessionId}` |
| `loyalty.points_redeemed` | `{ customerId, points, itemName, tenantName }` | `session:{tenantId}:{sessionId}` |

---

## Endpoints

### GET /hubs/realtime

**Objetivo**
Endpoint de upgrade WebSocket para negociación SignalR; al conectar exitosamente une al cliente a los grupos adecuados según los claims de su JWT.

**Auth**
Staff JWT, Customer JWT o KDS Device Token (el token se pasa como `Authorization: Bearer {token}` o como query param `access_token={token}`)

**Request**
- Path params: Ninguno
- Query params:
  - `access_token` (string, opcional) — Bearer token alternativo para clientes SignalR basados en browser que no pueden setear headers HTTP customizados; equivalente al header `Authorization`
- Body: Ninguno (WebSocket upgrade — protocolo HTTP/1.1 → WebSocket)

**Response 101 Switching Protocols**
(Conexión WebSocket establecida; la comunicación posterior sigue el protocolo SignalR JSON/MessagePack)

Payload de confirmación enviado al cliente tras asignar grupos:
```json
{
  "type": "connected",
  "connectionId": "abc123xyz456",
  "groups": [
    "tenant:b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "staff:f7a8b9c0-d1e2-3456-fabc-678901234567"
  ]
}
```

**Validaciones**
- El token (header `Authorization` o query param `access_token`) debe ser un JWT válido y no expirado emitido por SDD-001 Identity
- El token debe contener los claims `tenantId` y `role` (para Staff JWT) o `tenantId`, `sessionId` y `participantId` (para Customer JWT) o `tenantId`, `stationId` y `deviceType` (para KDS Device Token)
- Si ambos `Authorization` header y `access_token` query param están presentes → usar el header; ignorar el query param

**Reglas de Negocio**
Asignación de grupos al conectar:

| Tipo de token | Grupos asignados |
|---|---|
| Staff JWT (Cajero / Mesero / Admin / Cocina) | `tenant:{tenantId}`, `staff:{userId}` |
| Customer JWT | `session:{tenantId}:{sessionId}` |
| KDS Device Token | `station:{tenantId}:{stationId}`, `tenant:{tenantId}` |

- Un cliente puede pertenecer a múltiples grupos simultáneamente
- Al desconectarse (cierre de tab, pérdida de red, timeout): SignalR lo remueve automáticamente de todos los grupos — no requiere acción explícita del cliente
- La desconexión no elimina datos de sesión ni invalida el JWT
- El mismo usuario puede tener múltiples conexiones activas simultáneas (e.g. dos tabs abiertas); cada conexión tiene su propio `connectionId` y está en los mismos grupos
- Reconnect automático: el cliente SignalR reintenta la conexión con backoff exponencial (gestionado por la librería cliente de SignalR); al reconectar, el servidor repite el proceso de asignación de grupos
- Si el token está en el query param `access_token`, el servidor lee el token de ahí y lo valida igual que un Bearer token; esto es el comportamiento estándar documentado para ASP.NET Core SignalR con autenticación JWT en browsers

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente, inválido o expirado |
| 403 | Token válido pero sin los claims necesarios para asignar grupos (`tenantId`, `role`, o `sessionId` según el tipo) |

**Observabilidad**
- Log: `"signalr.connected"` `{ connectionId, tenantId, userId, role, groups, tokenType }`
- Log: `"signalr.disconnected"` `{ connectionId, tenantId, userId, durationSeconds, reason }`
- Log: `"signalr.reconnected"` `{ connectionId, tenantId, userId, previousConnectionId }`
- Métrica: `notifications.signalr_connections_active` (gauge — incrementar en connect, decrementar en disconnect)
- Métrica: `notifications.signalr_connected_total` (counter)
- Métrica: `notifications.signalr_disconnected_total` (counter, labels: `reason`)

**Casos de Aceptación**
- [ ] Staff JWT válido → 101, WebSocket establecido, cliente unido a `tenant:{tenantId}` y `staff:{userId}`
- [ ] Customer JWT válido → 101, WebSocket establecido, cliente unido a `session:{tenantId}:{sessionId}`
- [ ] KDS Device Token válido → 101, WebSocket establecido, cliente unido a `station:{tenantId}:{stationId}` y `tenant:{tenantId}`
- [ ] Token pasado como `access_token` query param → comportamiento idéntico al header
- [ ] Token expirado → 401, WebSocket no establecido
- [ ] Sin token → 401
- [ ] JWT válido pero sin claim `tenantId` → 403
- [ ] Múltiples conexiones del mismo usuario → cada conexión independiente en los mismos grupos

---

### GET /api/v1/platform/maintenance

**Objetivo**
Retorna el estado actual del banner de mantenimiento para una app específica o global, sin requerir autenticación.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params:
  - `app` (string, opcional) — nombre de la app destino: `menu` | `waiter` | `kds` | `admin` | `ops`; si se omite, retorna el estado global
- Body: Ninguno

**Response 200** — Mantenimiento activo
```json
{
  "active": true,
  "message": "Estamos realizando mantenimiento programado. Estaremos de vuelta a las 4:00 AM.",
  "apps": ["menu", "waiter"],
  "activeFrom": "2026-01-15T02:00:00Z",
  "activeUntil": "2026-01-15T04:00:00Z"
}
```

**Response 200** — Sin mantenimiento activo
```json
{
  "active": false,
  "message": null,
  "apps": [],
  "activeFrom": null,
  "activeUntil": null
}
```

**Validaciones**
- `app`: si se provee, debe ser exactamente uno de: `menu`, `waiter`, `kds`, `admin`, `ops`

**Reglas de Negocio**
Orden de lookup en Redis (con cortocircuito):
1. Si `app` fue provisto: consultar clave `maintenance:{app}` en Redis
2. Si la clave app-específica no existe: consultar clave `maintenance:global`
3. Si ninguna clave existe → retornar `active: false`

- Si la clave existe pero `activeUntil` ya pasó respecto a `NOW()` → retornar `active: false` (defensa contra TTL inaccurado por clock skew; no eliminar la clave en este endpoint — dejar que el TTL natural la expire)
- Si `app` no fue provisto en el query string: consultar solo `maintenance:global`
- Response header: `Cache-Control: public, max-age=30` — el cliente y CDN pueden cachear hasta 30 segundos
- No se realizan lecturas a PostgreSQL en este endpoint; toda la data proviene de Redis

**Errores**
| Código | Condición |
|---|---|
| 400 | `app` tiene un valor que no está en el conjunto permitido |

**Observabilidad**
- Log: `"maintenance.checked"` `{ app, active, source }` — donde `source` es `"app_specific"` | `"global"` | `"none"`
- Métrica: `notifications.maintenance_check_total` (counter, labels: `app`, `active`)

**Casos de Aceptación**
- [ ] Sin mantenimiento activo en Redis → 200 con `active: false` y todos los campos null
- [ ] Mantenimiento global activo en Redis → 200 con `active: true`, mensaje y rango de fechas
- [ ] Mantenimiento activo para app específica (`maintenance:menu`) → 200 con datos del override de app
- [ ] Clave `maintenance:{app}` existe y clave global también → retorna la clave app-específica (tiene precedencia)
- [ ] `activeUntil` en el pasado aunque la clave Redis exista → `active: false`
- [ ] `app=ops` válido → retorna estado para ops
- [ ] `app=pos` (valor inválido) → 400
- [ ] Sin query param `app` → consulta solo `maintenance:global`

---

### PUT /api/v1/platform/maintenance

**Objetivo**
Activa o desactiva el banner de mantenimiento para una o todas las apps, con persistencia en Redis y broadcast SignalR a todos los clientes conectados.

**Auth**
Platform Admin JWT (claim `role: "PlatformAdmin"` — personal de Ops de Mesto, no tenants de restaurante)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body — activar mantenimiento:
```json
{
  "active": true,
  "message": "Actualizando el sistema de pagos. Regresamos en 2 horas.",
  "apps": ["menu", "waiter"],
  "activeFrom": "2026-01-15T02:00:00Z",
  "activeUntil": "2026-01-15T04:00:00Z"
}
```
- Body — desactivar mantenimiento:
```json
{
  "active": false,
  "apps": ["menu", "waiter"]
}
```
> Para desactivar mantenimiento global: `"apps": []` o `"apps": ["menu","waiter","kds","admin","ops"]`

**Response 200**
```json
{
  "active": true,
  "message": "Actualizando el sistema de pagos. Regresamos en 2 horas.",
  "apps": ["menu", "waiter"],
  "activeFrom": "2026-01-15T02:00:00Z",
  "activeUntil": "2026-01-15T04:00:00Z",
  "activatedBy": "f7a8b9c0-d1e2-3456-fabc-678901234567"
}
```

**Validaciones**
- `active`: boolean, requerido
- `message`: string, requerido si `active = true`; máximo 500 caracteres; ignorado si `active = false`
- `apps`: array de strings, requerido; valores válidos por elemento: `"menu"`, `"waiter"`, `"kds"`, `"admin"`, `"ops"`; array vacío `[]` equivale a global
- `activeFrom`: string ISO 8601, requerido si `active = true`; debe ser ≤ `NOW() + 24h`; ignorado si `active = false`
- `activeUntil`: string ISO 8601, requerido si `active = true`; debe ser estrictamente mayor a `activeFrom`; máximo 24 horas después de `activeFrom`; ignorado si `active = false`

**Reglas de Negocio**
Cuando `active = true`:
- Si `apps = []` o `apps` contiene los 5 valores → escribir solo la clave `maintenance:global` en Redis
- Si `apps` contiene un subconjunto → escribir una clave `maintenance:{app}` por cada app en el array
- Payload de Redis: `{ message, apps, activeFrom, activeUntil, activatedBy: userId }`
- TTL de Redis: `(activeUntil - NOW()) + 60 segundos` (60 s de buffer para clock skew)
- Después de escribir Redis → llamar a `POST /api/v1/notifications/broadcast` con `{ "broadcast": true, "event": "maintenance.updated", "payload": { message, apps, activeFrom, activeUntil } }`

Cuando `active = false`:
- Si `apps = []` o contiene los 5 valores → DELETE `maintenance:global` de Redis
- Si `apps` es subconjunto → DELETE `maintenance:{app}` por cada app en el array; NO eliminar `maintenance:global` a menos que esté en el array
- No se escribe ningún registro en Redis (no hay record `active: false`)
- Después de eliminar claves Redis → broadcast `maintenance.updated` con `{ "active": false, apps }` a todos los clientes

Siempre:
- Escribir en tabla `audit_log`: `{ action: "maintenance.set", performedBy: userId, timestamp: NOW(), payload: requestBody }`

**Errores**
| Código | Condición |
|---|---|
| 400 | Body mal formado o campos requeridos ausentes |
| 400 | `apps` contiene un valor fuera del conjunto permitido |
| 400 | `message` ausente cuando `active = true` |
| 401 | Token ausente o inválido |
| 403 | Rol del token no es `PlatformAdmin` |
| 422 | `activeUntil` no es mayor que `activeFrom` |
| 422 | `activeUntil` más de 24 horas después de `activeFrom` |
| 422 | `activeFrom` más de 24 horas en el futuro desde `NOW()` |

**Observabilidad**
- Log: `"maintenance.activated"` `{ apps, activeFrom, activeUntil, activatedBy, redisKeys }`
- Log: `"maintenance.deactivated"` `{ apps, deactivatedBy, keysDeleted }`
- Métrica: `notifications.maintenance_set_total` (counter, labels: `active`)

**Casos de Aceptación**
- [ ] PlatformAdmin activa mantenimiento global (`apps: []`) → 200, clave `maintenance:global` en Redis con TTL correcto, broadcast a todos los clientes
- [ ] PlatformAdmin activa mantenimiento para `["menu", "waiter"]` → 200, claves `maintenance:menu` y `maintenance:waiter` en Redis, NO se afecta `maintenance:global`
- [ ] PlatformAdmin desactiva (`active: false`) para `["menu"]` → 200, clave `maintenance:menu` eliminada, `maintenance:global` intacta
- [ ] PlatformAdmin desactiva con `apps: []` → 200, clave `maintenance:global` eliminada
- [ ] `activeUntil` antes de `activeFrom` → 422
- [ ] `activeUntil` 25 horas después de `activeFrom` → 422
- [ ] `message` ausente con `active: true` → 400
- [ ] `apps` contiene `"pos"` (inválido) → 400
- [ ] Rol no es `PlatformAdmin` → 403
- [ ] Token ausente → 401

---

### POST /api/v1/notifications/send-email

**Objetivo**
Envía un email transaccional usando un template almacenado en base de datos; endpoint interno consumido exclusivamente por otros módulos del backend mediante autenticación servicio-a-servicio.

**Auth**
Internal — header `X-Service-Token: {serviceSecret}` (secreto compartido almacenado en Vault; NO es un JWT de usuario)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "templateId": "staff_activation",
  "recipient": {
    "email": "carlos.perez@restaurante.com",
    "name": "Carlos Pérez"
  },
  "variables": {
    "activationLink": "https://app.mesto.co/activate?token=eyJhbGciOi...",
    "restaurantName": "Restaurante Don Pepito"
  },
  "locale": "es"
}
```

**Response 202**
```json
{
  "messageId": "azure-msg-id-0a1b2c3d4e5f",
  "templateId": "staff_activation",
  "recipient": "carlos.perez@restaurante.com",
  "status": "QUEUED",
  "enqueuedAt": "2026-01-15T10:00:00Z"
}
```

**Validaciones**
- `templateId`: string, requerido; debe existir en la tabla `email_templates` como `id`
- `recipient.email`: string, requerido; formato de email válido (RFC 5321); máximo 254 caracteres
- `recipient.name`: string, opcional; máximo 100 caracteres
- `variables`: objeto, requerido si el template tiene placeholders; todas las variables marcadas como requeridas en `email_templates.required_variables` (JSONB) deben estar presentes
- `locale`: string, opcional; valores soportados: `"es"`, `"en"`; default `"es"`

**Reglas de Negocio**
- Validar el header `X-Service-Token` contra el valor almacenado en Vault (variable de entorno `INTERNAL_SERVICE_TOKEN`); si no coincide → 401
- Obtener el template de la tabla `email_templates` filtrando por `id = templateId` y `locale = locale`; si no existe para el locale solicitado → fallback a `locale = "es"`; si tampoco existe en `"es"` → 404
- Verificar que todas las claves de `email_templates.required_variables` estén presentes en `variables` del request; si falta alguna → 422 con `missingVariables: [...]`
- Interpolar las `variables` en el `body` y `subject` del template usando el patrón `{{variableName}}`
- Intentar envío vía Azure Email Communication Services (primario); si la llamada a Azure falla o devuelve error → reintentar una vez; si falla de nuevo → fallback a SendGrid
- El envío es asíncrono: encolar en job queue interno y retornar 202 inmediatamente; el `messageId` proviene del proveedor (Azure o SendGrid) al momento de encolar
- Si ambos proveedores están caídos → 503; NO encolar el mensaje si no se puede obtener confirmación del proveedor
- Escribir en tabla `audit_log`: `{ action: "email.queued", templateId, recipientEmail: recipient.email, callerService: derivedFromServiceToken, enqueuedAt }`
- El endpoint no está expuesto externamente; el API Gateway bloquea tráfico externo a `/api/v1/notifications/*` a menos que provenga de un servicio interno

Templates registrados en `email_templates`:
| `id` | Descripción |
|---|---|
| `staff_activation` | Invitación de activación de cuenta para staff |
| `staff_password_reset` | Reset de contraseña para staff |
| `customer_password_reset` | Reset de contraseña para clientes del programa de lealtad |
| `announcement` | Anuncio de Ops a dueños de restaurante |
| `loyalty_points_alert` | Alerta cuando cliente tiene suficientes puntos para canjear |

**Errores**
| Código | Condición |
|---|---|
| 400 | Body mal formado o campos obligatorios ausentes |
| 401 | Header `X-Service-Token` ausente o no coincide con el secreto almacenado |
| 404 | `templateId` no existe en la tabla `email_templates` (ni en el locale solicitado ni en fallback `"es"`) |
| 422 | Una o más variables requeridas por el template están ausentes en el body (respuesta incluye `missingVariables: [...]`) |
| 503 | Tanto Azure Email como SendGrid no están disponibles |

**Observabilidad**
- Log: `"email.queued"` `{ templateId, recipientEmail, locale, messageId, provider, callerService }`
- Log: `"email.locale_fallback"` `{ templateId, requestedLocale, fallbackLocale: "es" }`
- Log: `"email.provider_fallback"` `{ templateId, azureError, fallbackProvider: "sendgrid" }`
- Log: `"email.send_failed"` `{ templateId, recipientEmail, azureError, sendgridError }`
- Métrica: `notifications.email_queued_total` (counter, labels: `template_id`, `provider`)
- Métrica: `notifications.email_provider_errors_total` (counter, labels: `provider`)
- Métrica: `notifications.email_provider_fallback_total` (counter)

**Casos de Aceptación**
- [ ] Template válido con todas las variables requeridas y locale `"es"` → 202 con `messageId` y `status: "QUEUED"`
- [ ] Template solicitado en locale `"en"` no existe → fallback a `"es"`, 202
- [ ] Template no existe en ningún locale → 404
- [ ] Variable requerida por el template ausente en `variables` → 422 con `missingVariables: ["activationLink"]`
- [ ] `templateId` inexistente en la tabla → 404
- [ ] `X-Service-Token` ausente → 401
- [ ] `X-Service-Token` incorrecto → 401
- [ ] Azure Email falla → intento con SendGrid, 202
- [ ] Azure Email y SendGrid ambos caídos → 503
- [ ] `recipient.email` con formato inválido → 400

---

### POST /api/v1/notifications/broadcast

**Objetivo**
Transmite un evento SignalR a un grupo específico o a todos los clientes conectados; endpoint interno consumido exclusivamente por otros módulos del backend.

**Auth**
Internal — header `X-Service-Token: {serviceSecret}` (mismo secreto compartido que `send-email`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body — broadcast a un grupo específico:
```json
{
  "group": "session:b2c3d4e5-f6a7-8901-bcde-f12345678901:3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "event": "order.status_changed",
  "payload": {
    "orderId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "READY",
    "tableCode": "M-12"
  }
}
```
- Body — broadcast a todos los clientes conectados (solo para `maintenance.updated`):
```json
{
  "broadcast": true,
  "event": "maintenance.updated",
  "payload": {
    "message": "Mantenimiento finalizado. El sistema está disponible.",
    "apps": ["menu", "waiter"],
    "activeFrom": "2026-01-15T02:00:00Z",
    "activeUntil": "2026-01-15T04:00:00Z"
  }
}
```

**Response 200**
```json
{
  "event": "order.status_changed",
  "group": "session:b2c3d4e5-f6a7-8901-bcde-f12345678901:3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deliveredAt": "2026-01-15T12:36:05Z",
  "recipientCount": 3
}
```
> `recipientCount` es una estimación basada en la membresía del grupo SignalR en memoria; puede ser `0` si no hay clientes conectados — esto no es un error.

**Validaciones**
- `group`: string, requerido a menos que `broadcast: true` esté presente; debe coincidir con uno de los patrones:
  - `tenant:{uuid}`
  - `session:{uuid}:{uuid}`
  - `station:{uuid}:{uuid}`
  - `staff:{uuid}`
- `broadcast`: boolean, opcional; si es `true` el campo `group` debe estar ausente o ser `null`
- `event`: string, requerido; debe ser uno de los eventos definidos en el catálogo de eventos de este SDD
- `payload`: objeto, requerido; debe contener exactamente los campos definidos para el tipo de evento indicado (validación estructural mínima)

**Reglas de Negocio**
- Validar `X-Service-Token` igual que en `send-email`
- Si `group` está presente → `hub.Clients.Group(group).SendAsync(event, payload)` (SignalR `SendGroupAsync`)
- Si `broadcast: true` está presente y `group` ausente → `hub.Clients.All.SendAsync(event, payload)` (SignalR `SendAllAsync`); reservado únicamente para el evento `maintenance.updated`
- Si `broadcast: true` se intenta con un evento distinto a `maintenance.updated` → 400 (prevenir broadcast masivo inadvertido de otros eventos)
- El evento se despacha de forma síncrona al hub SignalR (ASP.NET Core SignalR maneja el fan-out en memoria); no hay cola para eventos SignalR
- `recipientCount = 0` es una respuesta válida — fire and forget; el caller no debe reintentar por recipientCount bajo
- Los tipos de evento desconocidos se rechazan con 400 para prevenir errores silenciosos por typos en el caller
- El payload no se valida en profundidad campo por campo (validación mínima de presencia de claves top-level); la responsabilidad de la corrección del payload recae en el módulo caller

Payloads requeridos por evento:
| Evento | Campos mínimos requeridos en `payload` |
|---|---|
| `order.created` | `orderId`, `sessionId`, `tableCode`, `participantId`, `items`, `stationId` |
| `order.status_changed` | `orderId`, `status`, `tableCode` |
| `order.cancelled` | `orderId`, `reason` |
| `session.opened` | `sessionId`, `tableCode`, `waiterId` |
| `session.participant_joined` | `sessionId`, `participantCount` |
| `session.closed` | `sessionId` |
| `pin.regenerated` | `sessionId`, `tableCode`, `newPin` |
| `maintenance.updated` | `message`, `apps`, `activeFrom`, `activeUntil` |
| `loyalty.points_earned` | `customerId`, `points`, `balanceAfter`, `tenantName` |
| `loyalty.points_redeemed` | `customerId`, `points`, `itemName`, `tenantName` |

**Errores**
| Código | Condición |
|---|---|
| 400 | `event` desconocido (no está en el catálogo de eventos) |
| 400 | Ni `group` ni `broadcast: true` proporcionados |
| 400 | `broadcast: true` con un evento diferente a `maintenance.updated` |
| 400 | `payload` no contiene los campos mínimos requeridos para el evento indicado |
| 401 | Header `X-Service-Token` ausente o inválido |
| 422 | `group` con formato inválido (no coincide con ningún patrón permitido) |

**Observabilidad**
- Log: `"signalr.broadcast"` `{ event, group, broadcast, recipientCount, callerService, durationMs }`
- Log: `"signalr.broadcast_no_recipients"` `{ event, group }` — cuando `recipientCount = 0`
- Métrica: `notifications.signalr_events_sent_total` (counter, labels: `event_type`)
- Métrica: `notifications.signalr_recipients` (histogram, labels: `event_type`) — distribución de destinatarios por evento

**Casos de Aceptación**
- [ ] Broadcast a grupo de sesión con clientes conectados → 200 con `recipientCount > 0`, clientes reciben el evento
- [ ] Broadcast a grupo de tenant → 200, todo el staff conectado recibe el evento
- [ ] Broadcast a grupo de estación KDS → 200, dispositivo KDS recibe el evento
- [ ] Broadcast a grupo de staff individual (`staff:{userId}`) → 200, solo ese staff recibe el evento
- [ ] Broadcast global (`broadcast: true`, evento `maintenance.updated`) → 200, todos los clientes conectados reciben el evento
- [ ] Grupo sin clientes conectados → 200 con `recipientCount: 0` (no es error)
- [ ] `broadcast: true` con evento `order.created` → 400 (solo `maintenance.updated` permite broadcast global)
- [ ] Evento desconocido `"orden.creada"` → 400
- [ ] `group` con formato `"tenants:uuid"` (patrón inválido) → 422
- [ ] Ni `group` ni `broadcast` presentes → 400
- [ ] `payload` sin campo requerido para el evento → 400
- [ ] `X-Service-Token` inválido → 401
