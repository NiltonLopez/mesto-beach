# SDD-001 — Identity

> **v2.1 — Octubre 2026 (diseño PRD-019) — Venue: el modo del local.** *(diseñado — pendiente de implementación, PRD-019)*. El tenant gana un **modo** (`RESTAURANT` | `NIGHT`, ADR-036) que el Admin cambia desde el panel con una **transición cerrada** (409 `VENUE_NOT_IDLE` mientras haya mesas abiertas, platos vivos o bandejas en ruta; sin `force`) y **auditada** (`venue_mode_changes`). Identity es la **única** fuente de verdad (`tenants.venue_mode` + Redis `venue:{tenantId}:mode`), la expone al resto de módulos por `IVenueModeProvider` (`MestoBeach.Shared`) y la publica como `VenueModeChangedNotification` (MediatR); a las apps, por `/api/v1/venue/*` (5 endpoints, §7). La estación del runner pasa a leerse de la base en cada petición (`IStaffStationResolver` dentro de `JwtValidationMiddleware`, caché `staff_station:{tenantId}:{userId}` 60 s): reasignarle solo la estación **ya no cierra su sesión**. Migración `AddTenantVenueModeAndChanges`.
> **v2.0 — Agosto 2026 — Roles de Mesto Beach y asignaciones.** Se añaden los roles **Host**, **PDA** y **Runner**, y la **asignación** que los hace operativos: un runner pertenece a una estación y un PDA a un sector (ADR-027). Diseñado en **PRD-018**; **no implementado todavía**.
> **v1.1 — Junio 2026** — Alta de personal híbrida: `POST /staff/invite` acepta `accessMode` (`ACTIVATION_LINK` | `DIRECT_ACCESS`); nuevo `POST /auth/change-password`; el login expone `staff.mustChangePassword`. Columna `staff.must_change_password` (migración `AddStaffMustChangePassword`).
> **v1.2 — Junio 2026** — Reset de contraseña iniciado por Admin: nuevo `POST /staff/{userId}/reset-password` (genera enlace para staff activo). `POST /staff/password-reset/confirm` ahora limpia `must_change_password`.
> **v1.3 — Junio 2026 — Identidad de usuario unificada (BREAKING).** Se separa la **identidad** (credenciales) de la **membresía** (acceso a un tenant). La tabla `staff` se reemplaza por dos: `users` (una cuenta global por correo, **una sola contraseña**) y `user_memberships` (vínculo usuario↔tenant con **uno o varios roles** y estado por restaurante). Migración `UnifiedUserIdentity` (drop `staff`, crea `users` + `user_memberships`). Cambios clave:
> - **Una persona = un correo + una contraseña** para todos los restaurantes donde es miembro (antes había una fila/contraseña por tenant, lo que permitía contraseñas distintas para el mismo correo y rompía el login).
> - **Descubrimiento de tenant en login**: nuevo `POST /auth/login/resolve` (email+password, sin tenantId) devuelve los restaurantes accesibles; el front auto-entra si hay uno y muestra selector si hay varios. `POST /auth/login` sigue requiriendo `tenantId` (el elegido) y valida la **membresía**.
> - **Varios roles por membresía**: el Admin puede asignar p. ej. `Cajero`+`Mesero` a la misma persona. El Staff JWT ahora lleva `roles[]` (array) además de `role` (primario, compat). Las policies `StaffAdmin`/`StaffAdminOrCajero` evalúan la lista (`HasRole`).
> - **Invitar correo existente** → acceso inmediato reusando su contraseña (`accessMode: EXISTING_USER`), sin reenviar activación.
> - **Activación/reset/cambio de contraseña son globales** (a nivel `users`). **Listar/editar roles/quitar** son a nivel membresía: nuevos `PATCH /staff/{userId}/roles` y `DELETE /staff/{userId}` (quita el acceso solo a ese restaurante; conserva la cuenta global y otras membresías).
> - `StaffMemberDto` ahora expone `userId` + `roles[]` + `status` (`PENDING|ACTIVE|INACTIVE`).

## Servicio
**Módulo**: Identity  
**Base path**: `/api/v1/identity`  
**Responsabilidad**: Autenticación y autorización de todos los actores del sistema: staff del restaurante, clientes, Platform Admin (Ops) y dispositivos KDS. Emite y valida todos los tipos de token de la plataforma.

**PRDs de referencia**: PRD-010 (Seguridad), PRD-014 (Autenticación y Login), PRD-012 §5 (Registro cliente), PRD-018 (Roles de Mesto Beach — §Roles y asignaciones), **PRD-019** (Mesto Beach Night — modo del local, §7; *diseñado, pendiente de implementación*)

**Tipos de JWT emitidos por este módulo**:
| Tipo | Algoritmo | TTL | Almacenamiento |
|---|---|---|---|
| Staff JWT | RS256 | 8h | Memory (frontend) |
| Refresh Token (Mesero/Admin) | opaque | 7d | HttpOnly cookie |
| Customer JWT | RS256 | 4h | Memory (frontend) |
| Platform Admin JWT | RS256 | 8h | Memory (frontend) |
| TOTP Challenge Token | RS256 | 5min | Memory (frontend) |
| Device Token (KDS) | RS256 | 30d rolling | localStorage |
| ImpersonationToken | RS256 | 15min | HttpOnly cookie → admin app |

**Dependencias**:
- PostgreSQL: tablas `users` + `user_memberships` (identidad global + membresías por tenant; reemplazan `staff` desde v1.3), `customer_accounts`, `platform_users`, `platform_user_roles`, `device_tokens`, `audit_log`; *(v2.1 — diseñado)* `tenants.venue_mode` / `venue_mode_changed_at` y la tabla **`venue_mode_changes`**; en **solo lectura** (`ExcludeFromMigrations`, patrón de `AssignmentReadModels.cs`) `floors`, `kds_stations`, `delivery_trips` (v2.0) y *(v2.1)* `sessions`, `orders` y `order_items` para el pre-flight del cambio de modo
- Redis: `refresh:*`, `activation:*`, `reset:staff:*`, `reset:customer:*`, `device:*`, `impersonation:*`, `login_attempts:*`, `auth_all_invalidated:*`, `blacklist:*`, `ops_roles:*`; *(v2.1 — diseñado)* **`venue:{tenantId}:mode`** (TTL 10 min) y **`staff_station:{tenantId}:{userId}`** (60 s)
- Email: servicio de notificaciones (SDD-007)
- TOTP: librería `Otp.NET` (RFC 6238)
- *(v2.1 — diseñado)* `MestoBeach.Shared`: enum `VenueMode`, interfaz `IVenueModeProvider` (implementación única en `MestoBeach.Identity.Infrastructure`) y `VenueModeChangedNotification` (MediatR `INotification`, con handlers en Orders — SDD-004 §Eventos SignalR — y Catalog — SDD-002)

---

## Roles y asignaciones *(v2.0 — PRD-018)*

A los roles heredados (`Admin`, `Cajero`, `Mesero`, `KDS`) se suman tres que describen cómo trabaja
Mesto Beach:

| Rol | Qué habilita | Asignación que necesita |
|---|---|---|
| `Host` | Asignar mesas con `pax`, ver ocupación, mover el mapa en vivo | — (ve todos los sectores) |
| `PDA` | Comandar por cualquier mesa de su sector y **marchar** platos (ADR-026) | **Un sector** (`floor_id`) |
| `Runner` | Ver la cola de su estación, armar bandeja y entregar (ADR-027) | **Una estación** (`station_id`) |

> `Mesero` queda como alias heredado de `PDA` mientras conviven ambas nomenclaturas; la decisión del
> nombre visible está abierta (OQ-09 de PRD-018).

### `user_memberships` — columnas nuevas

| Columna | Tipo | Notas |
|---|---|---|
| `assigned_floor_id` | uuid null | Sector del PDA. Obligatorio si la membresía incluye `PDA` |
| `assigned_station_id` | uuid null | Estación del Runner. Obligatoria si la membresía incluye `Runner` |

La asignación es **fija por persona y editable por Administración** (D-09 de PRD-018), no un turno
que se elige al entrar: el personal de El local tiene su puesto estable y los cambios los decide la
Administración.

El Staff JWT incorpora ambas claims cuando aplican (`floorId`, `stationId`), para que la API pueda
autorizar "este runner solo reclama platos de su estación" sin consultar la base en cada petición.

> *(v2.1 — diseñado, pendiente de implementación, PRD-019)* La claim **`stationId` deja de valer lo
> que dice el token**: `JwtValidationMiddleware` (vía `IStaffStationResolver`) la **sustituye en
> cada petición** por `user_memberships.assigned_station_id` (caché Redis
> `staff_station:{tenantId}:{userId}`, 60 s) al rellenar `ICurrentUser.AssignedStationId` y
> `HttpContext.Items["station_id"]`, de modo que reasignar un runner de barra no obliga a invalidar
> su JWT ni a que vuelva a entrar. `floorId` sigue viajando en el token. Ver §7.

---

### PATCH /api/v1/identity/staff/{userId}/assignment *(v2.0 · v2.1 diseñado)*

**Objetivo**
Fija o cambia el sector de un PDA o la estación de un Runner.

**Auth**
Staff JWT **Admin**

**Request**
```json
{ "assignedFloorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca", "assignedStationId": null }
```

**Response 200**
```json
{
  "userId": "b0f5c675-24bc-43b5-bdd1-d325b3a04140",
  "roles": ["PDA"],
  "assignedFloorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca",
  "assignedStationId": null
}
```

**Validaciones**
- El usuario tiene membresía activa en el tenant del token
- Si los roles incluyen `PDA`, `assignedFloorId` es obligatorio y debe existir
- Si incluyen `Runner`, `assignedStationId` es obligatorio y debe existir
- Un usuario puede ser PDA y Runner a la vez solo si se le dan ambas asignaciones

**Reglas de Negocio**
- Cambiar la asignación **no cierra la sesión** del usuario, pero el JWT vigente lleva la anterior: se invalida su token para forzar reemisión, igual que ya se hace al cambiar roles — *(v2.1 — diseñado, PRD-019)* **solo si cambia el sector** (`assignedFloorId`). Si cambia **solo la estación**, no se invalida nada: el handler hace `DEL staff_station:{tenantId}:{userId}` (tras el commit) y la estación nueva entra en la siguiente petición del runner (≤ 60 s en el peor caso). «Sin volver a entrar» exige además el cambio de la app del runner descrito en §7. Ver §7
- Un Runner con un **viaje abierto** no puede cambiar de estación: primero cierra o libera la ruta (SDD-004 §`/delivery-trips/{tripId}/release`)

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `ASSIGNMENT_REQUIRED` | 422 | El rol exige asignación y viene vacía |
| `FLOOR_NOT_FOUND` / `STATION_NOT_FOUND` | 404 | La referencia no existe en el tenant |
| `RUNNER_HAS_OPEN_TRIP` | 409 | Tiene una ruta sin cerrar |

**Observabilidad**
- `staff_assignment_changed` con `userId`, `roles`, `floorId`, `stationId`, `changedBy`

**Casos de Aceptación**
- [ ] Un PDA sin sector asignado no puede guardarse
- [ ] Cambiar de estación a un runner con ruta abierta devuelve 409
- [ ] Tras el cambio, el siguiente login trae las claims nuevas
- [ ] *(v2.1 — diseñado)* Cambiar **solo la estación** de un runner no lo echa de la app: su siguiente `GET /delivery-queue` ya es la de la estación nueva, sin volver a entrar; cambiar el sector de un PDA sigue invalidando el token (con el cambio de `runner/page.tsx` de §7)

---

## Convenciones de Validación de JWT

Todo endpoint que requiera un JWT debe ejecutar los siguientes pasos antes de la lógica de negocio:

1. Verificar firma RS256 con clave pública del servicio.
2. Verificar que `exp` no haya pasado; si expirado → 401.
3. Verificar que `type` del claim coincida con el tipo esperado; si no → 401.
4. Consultar Redis `blacklist:{jti}`; si existe → 401.
5. Para Staff JWT y Platform Admin JWT: consultar Redis `auth_all_invalidated:{sub}`; si existe y `iat < timestamp_almacenado` → 401.

---

## 1. Auth Staff

### POST /api/v1/identity/auth/login

**Objetivo**
Autentica a un miembro del staff (Admin, Cajero o Mesero) con email y contraseña, emite un Staff JWT y, para roles Admin y Mesero, también emite un Refresh Token como cookie HttpOnly.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "ana@restaurante.co",
  "password": "S3cur3P@ss!",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJhMWIyYzNkNCIsInRlbmFudElkIjoiM2ZhODVmNjQiLCJyb2xlIjoiTWVzZXJvIiwidHlwZSI6InN0YWZmIiwianRpIjoiZTVmNmE3YjgiLCJpYXQiOjE3MTc3NTIwMDAsImV4cCI6MTcxNzc4MDgwMH0.signature",
  "tokenType": "Bearer",
  "expiresIn": 28800,
  "staff": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Ana García",
    "email": "ana@restaurante.co",
    "roles": ["Cajero", "Mesero"],
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "mustChangePassword": false
  }
}
```

> **v1.3** — `staff.id` es el id del **usuario global**; `staff.roles[]` son los roles de la membresía en el `tenantId` indicado. El `tenantId` del body debe obtenerse primero con `POST /auth/login/resolve` (ver abajo); este endpoint valida que el usuario tenga una **membresía activa** en ese tenant.

### POST /api/v1/identity/auth/login/resolve

**Objetivo**
Paso de descubrimiento previo al login: recibe `email`+`password` (sin `tenantId`) y devuelve los restaurantes donde esas credenciales son válidas y el usuario tiene membresía activa. **No emite token.** El frontend: si hay 1 → llama directo a `/auth/login` con ese `tenantId`; si hay varios → muestra un selector. Evita que el usuario tenga que escribir el id del restaurante.

**Auth**: ninguna. **Request**: `{ "email": "...", "password": "..." }`

**Response 200**
```json
{
  "tenants": [
    { "tenantId": "3fa85f64-...", "tenantName": "Restaurante Centro", "roles": ["Admin"] },
    { "tenantId": "7bc91a02-...", "tenantName": "Restaurante Norte", "roles": ["Cajero", "Mesero"] }
  ]
}
```
- `401` si las credenciales son inválidas; `403` si no tiene acceso a ningún restaurante activo; `423` tras demasiados intentos (anti-fuerza-bruta por correo, bucket global).
- Excluye membresías **solo KDS** (esos dispositivos usan Device Token).

*`mustChangePassword`: `true` cuando el staff fue dado de alta en modo **acceso directo** y aún no ha cambiado su contraseña temporal. El frontend debe redirigir obligatoriamente a la pantalla de cambio (`/change-password`) y no permitir operar hasta que se complete `POST /auth/change-password`. Mientras el flag esté activo no se emite Refresh Token (la sesión previa al cambio es de corta duración).*

*Para roles Mesero y Admin únicamente, y sólo cuando `mustChangePassword = false` — header de respuesta adicional:*
```
Set-Cookie: refresh_token=<hex64bytes>; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/identity/auth; Max-Age=604800
```

**Validaciones**
- `email`: requerido, formato RFC 5322 válido, max 300 chars, normalizar a lowercase antes de buscar.
- `password`: requerido, string no vacío.
- `tenantId`: requerido, UUID v4 válido.

**Reglas de Negocio** *(v1.3 — identidad global `users` + `user_memberships`)*
- Verificar que el tenant `tenantId` existe en la tabla `tenants`; si no existe: 404 "Restaurante no encontrado."
- Verificar contador de bloqueo: `GET login_attempts:{email}:{tenantId}` en Redis; si el valor >= 5: 423 "Cuenta bloqueada temporalmente. Intente de nuevo en 15 minutos."
- Buscar la identidad global en `users` por `email` (lowercase). Si no existe o no tiene `password_hash`: 401 "Credenciales inválidas."
- Si `users.status = 'PENDING'`: 403 "Cuenta no activada. Revise su correo electrónico."  ·  Si `'INACTIVE'`: 403 "Cuenta desactivada. Contacte al administrador."
- Comparar `password` con `users.password_hash` (bcrypt.verify); si no coincide: `INCR login_attempts:{email}:{tenantId}`, `EXPIRE … 900`, retornar 401 "Credenciales inválidas."
- Buscar la **membresía** en `user_memberships` por `(user_id, tenant_id)`. Si no existe o `status != 'ACTIVE'`: 403 "No tiene acceso a este restaurante."
- Si `tenants.status = 'PENDING'`, solo se permite si la membresía tiene rol `Admin` (onboarding); de lo contrario 404 "Restaurante no encontrado."
- Si la membresía es **solo KDS** (`roles ⊆ {KDS}`): 403 "Los dispositivos KDS deben autenticarse mediante el flujo de Device Token."
- En login exitoso: `DEL login_attempts:{email}:{tenantId}`, `UPDATE users SET last_login = NOW() WHERE id = :userId`.
- Generar `jti` = `uuid()`.
- Emitir Staff JWT RS256, claims: `{ sub: userId, tenantId, role (primario, compat), roles: [...] (todos los de la membresía), type: "staff", jti, iat, exp: iat+28800 }`.
- **Si la membresía incluye `Admin` o `Mesero`**: generar refresh token = `hex(crypto_random_bytes(64))`. `SET refresh:{token} '{"userId":"...","tenantId":"..."}' EX 604800`. `SADD user_sessions:{userId} {token}`. Emitir cookie HttpOnly.
- **Si la membresía es solo `Cajero`**: no emitir refresh token ni cookie.
- El `tenantId` lo obtiene el frontend con `POST /auth/login/resolve` (1 restaurante → entra directo; varios → selector); el usuario nunca escribe el id del restaurante.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos fallida (campo faltante, UUID malformado, email inválido) |
| 401 | Credenciales inválidas (email no existe o password incorrecto) |
| 403 | Cuenta PENDING, INACTIVE, o rol KDS |
| 404 | Tenant no encontrado o inactivo |
| 423 | Cuenta bloqueada por 5 intentos fallidos consecutivos (bloqueo 15 min) |

**Observabilidad**
- Log: `"staff.login.success"` `{ userId, tenantId, role, ip, userAgent }`
- Log: `"staff.login.failed"` `{ email, tenantId, reason: "wrong_password|account_pending|account_inactive|kds_role", ip }`
- Log: `"staff.login.locked"` `{ email, tenantId, ip }`
- Métrica: `identity.staff_login` (counter, dims: `role`, `result: success|failure|locked`)

**Casos de Aceptación**
- [ ] Mesero con credenciales válidas → 200, Staff JWT + cookie `refresh_token` con HttpOnly/Secure
- [ ] Admin con credenciales válidas → 200, Staff JWT + cookie `refresh_token`
- [ ] Cajero con credenciales válidas → 200, Staff JWT sin cookie `refresh_token`
- [ ] Password incorrecto → 401 con mensaje genérico "Credenciales inválidas"
- [ ] Email no existe en el tenant → 401 con mismo mensaje genérico
- [ ] 4 intentos fallidos seguidos → 401; 5.º intento → 401; 6.º intento → 423
- [ ] Cuenta en estado PENDING → 403 con mensaje de activación
- [ ] Cuenta en estado INACTIVE → 403 con mensaje de administrador
- [ ] Rol KDS → 403 con instrucción de Device Token
- [ ] Tenant con UUID válido pero inexistente → 404
- [ ] `tenantId` con formato UUID inválido → 400
- [ ] Tenant `PENDING` + staff `Admin` ACTIVE → 200 (onboarding permitido)
- [ ] Tenant `PENDING` + staff no Admin → 404

---

### POST /api/v1/identity/auth/refresh

**Objetivo**
Emite un nuevo Staff JWT usando el Refresh Token almacenado en la cookie HttpOnly; exclusivo para roles Admin y Mesero.

**Auth**
Refresh Token en cookie HttpOnly `refresh_token` (no se requiere Authorization header)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJhMWIyYzNkNCIsInRlbmFudElkIjoiM2ZhODVmNjQiLCJyb2xlIjoiTWVzZXJvIiwidHlwZSI6InN0YWZmIiwianRpIjoibmV3anRpIiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3ODA4MDB9.signature",
  "tokenType": "Bearer",
  "expiresIn": 28800
}
```

**Validaciones**
- Cookie `refresh_token`: debe estar presente y ser un string hexadecimal no vacío.

**Reglas de Negocio**
- Leer cookie `refresh_token`; si ausente o vacía: 401 "Sesión no encontrada."
- `GET refresh:{token}` en Redis; si la clave no existe (token expirado o eliminado): 401 "Sesión expirada. Inicie sesión nuevamente."
- Deserializar valor Redis → `{ userId, tenantId }`.
- Consultar `users` por `id = userId`; si no existe: 401. Si `status != 'ACTIVE'`: 403 "Cuenta inactiva."
- Consultar la membresía `user_memberships(user_id, tenant_id)`; si no existe o `status != 'ACTIVE'`: 403 "No tiene acceso a este restaurante." Obtener sus `roles`.
- Generar nuevo `jti` = `uuid()`.
- Emitir nuevo Staff JWT RS256, claims: `{ sub: userId, tenantId, role (primario), roles: [...], type: "staff", jti, iat, exp: iat+28800 }`.
- **No renovar** el refresh token ni su TTL en Redis — el TTL original de 7 días permanece inalterado.

**Errores**
| Código | Condición |
|---|---|
| 401 | Cookie ausente, token no encontrado en Redis (expirado o revocado por logout/logout-all) |
| 403 | Staff existe pero `status != 'ACTIVE'` |

**Observabilidad**
- Log: `"staff.token_refresh.success"` `{ userId, tenantId, role }`
- Log: `"staff.token_refresh.failed"` `{ reason: "cookie_absent|redis_miss|staff_inactive" }`
- Métrica: `identity.staff_token_refresh` (counter, dims: `result: success|failure`)

**Casos de Aceptación**
- [ ] Cookie `refresh_token` válida y activa → 200, nuevo Staff JWT
- [ ] Cookie ausente en la request → 401
- [ ] Token presente en cookie pero key no existe en Redis (expirado) → 401
- [ ] Token revocado por logout previo (DEL en Redis) → 401
- [ ] Token revocado por logout-all (DEL en Redis + set user_sessions eliminado) → 401
- [ ] Staff con `status = 'INACTIVE'` → 403

---

### POST /api/v1/identity/auth/logout

**Objetivo**
Invalida la sesión actual: elimina el Refresh Token de Redis, añade el JWT actual a la blacklist y limpia la cookie.

**Auth**
Staff JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 204**
*(Sin cuerpo)*

*Header de respuesta:*
```
Set-Cookie: refresh_token=; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/identity/auth; Max-Age=0
```

**Validaciones**
- Authorization header `Bearer <token>` con JWT válido: requerido.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT (ver sección inicial).
- Extraer `jti`, `exp` y `sub` (userId) del JWT.
- Si la request contiene cookie `refresh_token`:
  - `DEL refresh:{tokenValue}` de Redis.
  - `SREM user_sessions:{userId} {tokenValue}`.
- Añadir JWT a blacklist: `SET blacklist:{jti} 1 EX {MAX(1, exp - unix_now())}`.
- Emitir `Set-Cookie` que expira la cookie inmediatamente.

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente, inválido, expirado o ya en blacklist |

**Observabilidad**
- Log: `"staff.logout.success"` `{ userId, tenantId, jti, hadRefreshToken: true|false }`
- Métrica: `identity.staff_logout` (counter, dims: `role`)

**Casos de Aceptación**
- [ ] JWT válido + cookie refresh (Mesero/Admin) → 204, Redis DEL refresh, JWT en blacklist, cookie eliminada
- [ ] JWT válido sin cookie (Cajero) → 204, JWT en blacklist, no error por cookie ausente
- [ ] JWT ausente → 401
- [ ] JWT ya en blacklist (doble logout) → 401
- [ ] JWT expirado → 401

---

### POST /api/v1/identity/auth/logout-all

**Objetivo**
Invalida todas las sesiones activas del staff en todos los dispositivos eliminando todos sus Refresh Tokens y estableciendo un timestamp de invalidación global para los JWTs vigentes.

**Auth**
Staff JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 204**
*(Sin cuerpo)*

*Header de respuesta:*
```
Set-Cookie: refresh_token=; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/identity/auth; Max-Age=0
```

**Validaciones**
- Authorization header `Bearer <token>` con JWT válido: requerido.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT.
- Extraer `sub` (userId), `jti`, `exp` del JWT.
- `SMEMBERS user_sessions:{userId}` → lista de todos los tokens de refresh activos del usuario.
- Por cada token en la lista: `DEL refresh:{token}`.
- `DEL user_sessions:{userId}`.
- `SET auth_all_invalidated:{userId} {unix_now()} EX 28800` (TTL = duración máxima de un JWT Staff).
- Añadir JWT actual a blacklist: `SET blacklist:{jti} 1 EX {MAX(1, exp - unix_now())}`.
- Emitir `Set-Cookie` que expira la cookie del dispositivo actual.

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente, inválido, expirado o en blacklist |

**Observabilidad**
- Log: `"staff.logout_all.success"` `{ userId, tenantId, sessionsRevoked: N, jti }`
- Métrica: `identity.staff_logout_all` (counter)

**Casos de Aceptación**
- [ ] JWT válido, múltiples sesiones activas → 204, todos los refresh tokens eliminados, clave `auth_all_invalidated` creada
- [ ] JWT válido, sin sesiones previas registradas → 204, operación idempotente sin error
- [ ] JWT del mismo usuario presentado después del logout-all → 401 (bloqueado por `auth_all_invalidated`)
- [ ] JWT ausente → 401

---

### GET /api/v1/identity/auth/me

**Objetivo**
Retorna el perfil completo del staff autenticado con datos frescos desde la base de datos.

**Auth**
Staff JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Ana García",
  "email": "ana@restaurante.co",
  "roles": ["Cajero", "Mesero"],
  "status": "ACTIVE",
  "createdAt": "2024-03-15T10:00:00Z",
  "lastLogin": "2024-06-07T08:30:00Z"
}
```

**Validaciones**
- Authorization header `Bearer <token>` con JWT válido: requerido.
- JWT `type` claim debe ser `"staff"`.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT.
- Extraer `sub` (userId) y `tenantId` del JWT.
- Consultar `users` por `id = userId`; si no existe: 404 "Usuario no encontrado."
- Si `users.status != 'ACTIVE'`: 403 "Cuenta inactiva."
- Consultar la membresía `(userId, tenantId)`; si no existe o inactiva: 403 "No tiene acceso a este restaurante." `roles` = roles de esa membresía.
- Nunca incluir `password_hash` en la respuesta.

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente, inválido, expirado, en blacklist, o invalidado por logout-all |
| 403 | Staff con `status != ACTIVE` |
| 404 | Registro de staff eliminado (inconsistencia de datos) |

**Observabilidad**
- Log: omitido (endpoint de alta frecuencia; cubrir con métricas HTTP estándar y trazas de APM).
- Métrica: cubierta por métricas HTTP estándar (`http_requests_total`, latencia).

**Casos de Aceptación**
- [ ] JWT válido, staff ACTIVE → 200, perfil completo sin `password_hash`
- [ ] JWT expirado → 401
- [ ] JWT con `type = "customer"` → 401 (type incorrecto)
- [ ] JWT en blacklist → 401
- [ ] JWT válido pero `auth_all_invalidated` presente con `iat < timestamp` → 401
- [ ] Staff con `status = INACTIVE` → 403

---

## 2. Auth Cliente

### POST /api/v1/identity/customers/register

**Objetivo**
Registra una nueva cuenta de cliente con email y contraseña y emite un Customer JWT de forma inmediata.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "carlos@ejemplo.co",
  "password": "M1Contraseña!",
  "name": "Carlos Ramírez",
  "phone": "+573001234567",
  "birthDate": "1990-05-20"
}
```

**Response 201**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJiMmMzZDRlNSIsInR5cGUiOiJjdXN0b21lciIsImp0aSI6ImY2YTdiOGM5IiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3NjYzOTl9.signature",
  "tokenType": "Bearer",
  "expiresIn": 14400,
  "customer": {
    "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "email": "carlos@ejemplo.co",
    "name": "Carlos Ramírez",
    "loyaltyPoints": 0
  }
}
```

**Validaciones**
- `email`: requerido, formato RFC 5322 válido, max 300 chars, normalizar a lowercase.
- `password`: requerido, min 8 chars, debe contener al menos 1 letra mayúscula, 1 letra minúscula y 1 dígito.
- `name`: requerido, 2–200 chars, solo letras Unicode, espacios y guiones.
- `phone`: opcional, formato E.164 (`^\+[1-9]\d{7,14}$`); si presente no puede ser vacío.
- `birthDate`: opcional, formato `YYYY-MM-DD`; si presente: no puede ser fecha futura, edad mínima 14 años al día de hoy.

**Reglas de Negocio**
- Normalizar `email` a lowercase antes de todas las búsquedas.
- Buscar `customer_accounts` por `email`. Si existe un registro con `password_hash IS NOT NULL`: 409 "Ya existe una cuenta con este correo electrónico."
- Si existe un registro con `password_hash IS NULL` (cuenta Google-only): 409 "Esta cuenta usa Google para iniciar sesión."
- Hashear `password` con bcrypt, cost factor 12.
- Insertar en `customer_accounts`: `{ id: uuid(), email, name, phone, birth_date, password_hash, loyalty_points: 0, created_at: NOW(), last_login: NOW() }`.
- Emitir Customer JWT RS256, claims: `{ sub: customerId, type: "customer", jti: uuid(), iat, exp: iat+14400 }`.
- No emitir refresh token (los clientes re-autentican al expirar el JWT de 4h).

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos (email inválido, password débil, nombre con caracteres no permitidos, birthDate inválida, menor de 14 años) |
| 409 | Email ya registrado (con password) o email vinculado a cuenta Google |

**Observabilidad**
- Log: `"customer.register.success"` `{ customerId, method: "email" }`
- Log: `"customer.register.conflict"` `{ reason: "email_exists|google_account" }` (sin email en log)
- Métrica: `identity.customer_register` (counter, dims: `method: email`, `result: success|conflict`)

**Casos de Aceptación**
- [ ] Todos los campos válidos, email nuevo → 201, Customer JWT emitido, `loyaltyPoints: 0`
- [ ] Email ya registrado con password → 409 con mensaje genérico
- [ ] Email ya registrado con Google (sin password_hash) → 409 con mensaje específico
- [ ] `password` sin mayúscula → 400
- [ ] `password` sin dígito → 400
- [ ] `password` de 7 chars → 400
- [ ] `birthDate` con edad 13 años → 400
- [ ] `birthDate` futura → 400
- [ ] `phone` con formato inválido → 400
- [ ] `phone` omitido → 201 (campo opcional)

---

### POST /api/v1/identity/customers/login

**Objetivo**
Autentica a un cliente existente con email y contraseña y emite un Customer JWT.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "carlos@ejemplo.co",
  "password": "M1Contraseña!"
}
```

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJiMmMzZDRlNSIsInR5cGUiOiJjdXN0b21lciIsImp0aSI6ImcxaDJpM2o0IiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3NjYzOTl9.signature",
  "tokenType": "Bearer",
  "expiresIn": 14400,
  "customer": {
    "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "email": "carlos@ejemplo.co",
    "name": "Carlos Ramírez",
    "loyaltyPoints": 150
  }
}
```

**Validaciones**
- `email`: requerido, formato email válido, normalizar a lowercase.
- `password`: requerido, string no vacío.

**Reglas de Negocio**
- Normalizar `email` a lowercase.
- Buscar `customer_accounts` por `email`; si no existe: 401 "Credenciales inválidas." (nunca revelar si el email existe).
- Si existe pero `password_hash IS NULL` (cuenta Google-only): 401 "Esta cuenta usa Google para iniciar sesión."
- Comparar `password` con `password_hash` (bcrypt.verify); si no coincide: 401 "Credenciales inválidas."
- `UPDATE customer_accounts SET last_login = NOW() WHERE id = :customerId`.
- Emitir Customer JWT RS256, claims: `{ sub: customerId, type: "customer", jti: uuid(), iat, exp: iat+14400 }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos |
| 401 | Credenciales inválidas o cuenta Google-only |

**Observabilidad**
- Log: `"customer.login.success"` `{ customerId }`
- Log: `"customer.login.failed"` `{ reason: "not_found|wrong_password|google_only" }` (sin email en log)
- Métrica: `identity.customer_login` (counter, dims: `method: email`, `result: success|failure`)

**Casos de Aceptación**
- [ ] Email y password correctos → 200, Customer JWT emitido, `last_login` actualizado
- [ ] Email no existe → 401 "Credenciales inválidas" (mismo mensaje que password incorrecto)
- [ ] Password incorrecto → 401 "Credenciales inválidas"
- [ ] Cuenta Google-only (sin password_hash) → 401 con mensaje específico
- [ ] `email` sin formato válido → 400

---

### POST /api/v1/identity/customers/google

**Objetivo**
Autentica o registra a un cliente usando un `id_token` emitido por Google SDK; si la cuenta no existe la crea automáticamente.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjAxYzM..."
}
```

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJiMmMzZDRlNSIsInR5cGUiOiJjdXN0b21lciIsImp0aSI6ImgxaTJqM2s0IiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3NjYzOTl9.signature",
  "tokenType": "Bearer",
  "expiresIn": 14400,
  "customer": {
    "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "email": "carlos@gmail.com",
    "name": "Carlos Ramírez",
    "loyaltyPoints": 0,
    "isNewAccount": true
  }
}
```

**Validaciones**
- `idToken`: requerido, string no vacío (JWT de Google).

**Reglas de Negocio**
- Verificar `idToken` usando `Google.Apis.Auth` (`GoogleJsonWebSignature.ValidateAsync`). Si el token es inválido, malformado o expirado: 401 "Token de Google inválido."
- Verificar que el claim `aud` del token coincida con `GOOGLE_CLIENT_ID` de variables de entorno; si no: 401 "Audiencia incorrecta."
- Extraer del payload Google: `sub` (google_id), `email`, `name`.
- Normalizar `email` a lowercase.
- Buscar en `customer_accounts` por `google_id = sub`:
  - **Encontrado**: `UPDATE customer_accounts SET last_login = NOW() WHERE id = :id`. `isNewAccount = false`.
- Si no encontrado por `google_id`, buscar por `email`:
  - **Email existe con `password_hash IS NOT NULL`**: 409 "Ya existe una cuenta con este correo. Inicia sesión con tu contraseña."
  - **Email no existe**: insertar nuevo registro `{ id: uuid(), email, name, google_id: sub, password_hash: NULL, loyalty_points: 0, created_at: NOW(), last_login: NOW() }`. `isNewAccount = true`.
- Emitir Customer JWT RS256, claims: `{ sub: customerId, type: "customer", jti: uuid(), iat, exp: iat+14400 }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `idToken` ausente o vacío |
| 401 | Token Google inválido, expirado, o `aud` no coincide con `GOOGLE_CLIENT_ID` |
| 409 | Email del token Google ya registrado con contraseña (no se vincula automáticamente) |

**Observabilidad**
- Log: `"customer.google_auth.success"` `{ customerId, isNewAccount }`
- Log: `"customer.google_auth.failed"` `{ reason: "invalid_token|audience_mismatch|email_conflict" }`
- Métrica: `identity.customer_register` (counter, dims: `method: google`, `result: success|conflict`) — para cuentas nuevas
- Métrica: `identity.customer_login` (counter, dims: `method: google`, `result: success|failure`) — para cuentas existentes

**Casos de Aceptación**
- [ ] `idToken` válido, email nuevo → 200, cuenta creada, `isNewAccount: true`
- [ ] `idToken` válido, misma cuenta Google ya existente → 200, `isNewAccount: false`, `last_login` actualizado
- [ ] `idToken` válido, email ya registrado con password → 409
- [ ] `idToken` con firma inválida → 401
- [ ] `idToken` expirado → 401
- [ ] `idToken` con `aud` distinto al `GOOGLE_CLIENT_ID` configurado → 401

---

### GET /api/v1/identity/customers/me

**Objetivo**
Retorna el perfil completo del cliente autenticado con datos frescos desde la base de datos.

**Auth**
Customer JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "email": "carlos@ejemplo.co",
  "name": "Carlos Ramírez",
  "phone": "+573001234567",
  "birthDate": "1990-05-20",
  "loyaltyPoints": 150,
  "hasPassword": true,
  "hasGoogle": false,
  "createdAt": "2024-03-15T10:00:00Z",
  "lastLogin": "2024-06-07T08:30:00Z"
}
```

**Validaciones**
- Authorization header `Bearer <token>` con JWT válido: requerido.
- JWT `type` claim debe ser `"customer"`.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT.
- Extraer `sub` (customerId) del JWT.
- Consultar `customer_accounts` por `id = customerId`; si no existe: 404 "Cuenta no encontrada."
- Calcular `hasPassword = (password_hash IS NOT NULL)`, `hasGoogle = (google_id IS NOT NULL)`.
- Nunca incluir `password_hash` ni `google_id` en la respuesta.

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente, inválido, expirado o en blacklist |
| 404 | Cuenta eliminada (inconsistencia) |

**Observabilidad**
- Log: omitido (endpoint de alta frecuencia).
- Métrica: cubierta por métricas HTTP estándar.

**Casos de Aceptación**
- [ ] Customer JWT válido → 200, perfil completo con `hasPassword` y `hasGoogle` calculados
- [ ] JWT expirado → 401
- [ ] JWT con `type = "staff"` → 401
- [ ] JWT en blacklist → 401

---

### PATCH /api/v1/identity/customers/me

**Objetivo**
Actualiza de forma parcial el perfil del cliente autenticado (nombre, teléfono o fecha de nacimiento).

**Auth**
Customer JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body (todos los campos son opcionales; solo los presentes se actualizan):
```json
{
  "name": "Carlos R. Ramírez",
  "phone": "+573009876543",
  "birthDate": "1990-05-20"
}
```

*Para eliminar el teléfono, enviar `"phone": null`.*

**Response 200**
```json
{
  "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "email": "carlos@ejemplo.co",
  "name": "Carlos R. Ramírez",
  "phone": "+573009876543",
  "birthDate": "1990-05-20",
  "loyaltyPoints": 150,
  "hasPassword": true,
  "hasGoogle": false,
  "createdAt": "2024-03-15T10:00:00Z",
  "lastLogin": "2024-06-07T08:30:00Z"
}
```

**Validaciones**
- Al menos un campo debe estar presente en el body (no acepta body vacío `{}`).
- `name`: si presente y no null, 2–200 chars, solo letras Unicode, espacios y guiones.
- `phone`: si presente y no null, formato E.164 (`^\+[1-9]\d{7,14}$`); si null, se elimina el teléfono.
- `birthDate`: si presente y no null, formato `YYYY-MM-DD`, no futura, edad mínima 14 años.
- Campos no modificables por este endpoint: `email`, `password_hash`, `google_id`, `loyalty_points`, `id`.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT.
- Extraer `sub` (customerId).
- Consultar `customer_accounts`; si no existe: 404.
- Construir cláusula `SET` dinámicamente solo con los campos presentes en el body.
- Ejecutar `UPDATE customer_accounts SET {campos_presentes} WHERE id = :customerId`.
- Retornar perfil completo actualizado (igual que `GET /customers/me`).

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío, campo con valor inválido, o intento de modificar campo no permitido |
| 401 | JWT ausente, inválido o expirado |
| 404 | Cuenta no encontrada |

**Observabilidad**
- Log: `"customer.profile.updated"` `{ customerId, updatedFields: ["name", "phone"] }`
- Métrica: `identity.customer_profile_update` (counter)

**Casos de Aceptación**
- [ ] PATCH `{ name }` válido → 200, solo el nombre actualizado
- [ ] PATCH `{ phone: null }` → 200, phone eliminado (NULL en DB)
- [ ] PATCH `{ birthDate }` con edad 13 → 400
- [ ] PATCH con body `{}` → 400 "Debe enviar al menos un campo."
- [ ] JWT expirado → 401
- [ ] PATCH intentando enviar `email` → 400 (campo no permitido)

---

### POST /api/v1/identity/customers/password-reset/request

**Objetivo**
Inicia el flujo de recuperación de contraseña para clientes generando un token y enviando email; siempre responde 200 para evitar enumeración de cuentas.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "carlos@ejemplo.co"
}
```

**Response 200**
```json
{
  "message": "Si el correo está registrado, recibirás las instrucciones en breve."
}
```

**Validaciones**
- `email`: requerido, formato email válido.

**Reglas de Negocio**
- SIEMPRE retornar HTTP 200 con el mismo mensaje, independientemente de si el email existe o no.
- Normalizar `email` a lowercase.
- Buscar `customer_accounts` por `email`:
  - Si no existe: no realizar ninguna acción adicional.
  - Si existe con `password_hash IS NULL` (cuenta Google-only): no enviar email (el usuario debe usar Google); retornar 200.
  - Si existe con `password_hash IS NOT NULL`:
    - Generar reset token: `hex(crypto_random_bytes(64))`.
    - `SET reset:customer:{token} '{"customerId":"..."}' EX 3600`.
    - Enviar email via SDD-007 con enlace: `{FRONTEND_URL}/reset-password/customer?token={token}`.
- Este endpoint no tiene límite de velocidad propio; el servicio de email (SDD-007) aplica rate limiting de envío.

**Errores**
| Código | Condición |
|---|---|
| 400 | `email` ausente o con formato inválido |

**Observabilidad**
- Log: `"customer.password_reset.requested"` `{ customerId }` (solo si cuenta con password existe; sin email en log)
- Métrica: `identity.customer_password_reset_request` (counter)

**Casos de Aceptación**
- [ ] Email registrado con password → 200, email de reset enviado
- [ ] Email no registrado → 200, sin acción (respuesta idéntica)
- [ ] Email registrado solo con Google → 200, sin email enviado
- [ ] `email` con formato inválido → 400
- [ ] Llamadas repetidas con el mismo email → 200 cada vez, sobrescribe token anterior en Redis

---

### POST /api/v1/identity/customers/password-reset/confirm

**Objetivo**
Establece una nueva contraseña para el cliente usando el token de recuperación recibido por email (uso único).

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "token": "a3f5c7e9d1b4a6c8e2f0d3b5a7c9e1f3d5b7a9c1e3f5d7b9a1c3e5f7d9b1a3c5e7f9a1b3c5d7e9f1a3b5",
  "newPassword": "NuevaContr4seña!",
  "confirmPassword": "NuevaContr4seña!"
}
```

**Response 200**
```json
{
  "message": "Contraseña actualizada exitosamente."
}
```

**Validaciones**
- `token`: requerido, string hexadecimal no vacío.
- `newPassword`: requerido, min 8 chars, al menos 1 mayúscula, 1 minúscula, 1 dígito.
- `confirmPassword`: requerido, debe ser idéntico a `newPassword`.

**Reglas de Negocio**
- Si `newPassword != confirmPassword`: 400 "Las contraseñas no coinciden."
- `GET reset:customer:{token}` en Redis; si no existe (expirado o ya usado): 400 "El enlace de recuperación es inválido o ha expirado."
- Obtener `{ customerId }` del valor Redis.
- Consultar `customer_accounts` por `id = customerId`; si no existe: 400 (tratado igual que token inválido).
- Hashear `newPassword` con bcrypt, cost factor 12.
- `UPDATE customer_accounts SET password_hash = :hash WHERE id = :customerId`.
- `DEL reset:customer:{token}` (token de uso único).

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos, `newPassword != confirmPassword`, token inválido o expirado |

**Observabilidad**
- Log: `"customer.password_reset.confirmed"` `{ customerId }`
- Log: `"customer.password_reset.invalid_token"` `{ }` (sin datos del token en log)
- Métrica: `identity.customer_password_reset_confirm` (counter, dims: `result: success|invalid_token`)

**Casos de Aceptación**
- [ ] Token válido, passwords iguales y fuertes → 200, password actualizado, token DEL de Redis
- [ ] Token expirado (no existe en Redis) → 400
- [ ] Mismo token usado dos veces → segundo intento 400 (ya fue DEL)
- [ ] `confirmPassword` no coincide → 400
- [ ] `newPassword` sin dígito → 400

---

## 3. Auth Platform Admin

### POST /api/v1/identity/platform/login

**Objetivo**
Primera fase del login de Platform Admin: valida email y contraseña y emite un TOTP Challenge Token de 5 minutos para la segunda fase.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "ops@mesto.app",
  "password": "S3cur3OpsP@ss!"
}
```

**Response 200**
```json
{
  "challengeToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjM2Q0ZTVmNiIsInR5cGUiOiJ0b3RwX2NoYWxsZW5nZSIsInNldHVwUmVxdWlyZWQiOmZhbHNlLCJqdGkiOiJpMWoyazNsNCIsImlhdCI6MTcxNzc1MjAwMCwiZXhwIjoxNzE3NzUyMzAwfQ.signature",
  "setupRequired": false,
  "expiresIn": 300
}
```

*Si el usuario no tiene TOTP configurado aún (`totp_secret IS NULL`):*
```json
{
  "challengeToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "setupRequired": true,
  "expiresIn": 300
}
```

*Claims del TOTP Challenge Token:*
```json
{
  "sub": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "type": "totp_challenge",
  "setupRequired": false,
  "jti": "i1j2k3l4-m5n6-7890-ijkl-mn1234567890",
  "iat": 1717752000,
  "exp": 1717752300
}
```

**Validaciones**
- `email`: requerido, formato email válido.
- `password`: requerido, string no vacío.

**Reglas de Negocio**
- Buscar `platform_users` por `email`; si no existe: 401 "Credenciales inválidas." (no revelar existencia).
- Si `platform_users.status = 'INACTIVE'`: 403 "Cuenta desactivada."
- Comparar `password` con `password_hash` (bcrypt.verify); si no coincide: 401 "Credenciales inválidas."
- Determinar `setupRequired = (totp_secret IS NULL)`.
- Generar `jti` = `uuid()`.
- Emitir TOTP Challenge Token RS256, claims: `{ sub: userId, type: "totp_challenge", setupRequired: bool, jti, iat, exp: iat+300 }`.
- **No** emitir Platform Admin JWT en esta fase.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos |
| 401 | Credenciales inválidas (email no existe o password incorrecto) |
| 403 | Usuario con `status = INACTIVE` |

**Observabilidad**
- Log: `"platform.login.challenge_issued"` `{ userId, setupRequired, ip }`
- Log: `"platform.login.failed"` `{ reason: "not_found|wrong_password|inactive", ip }` (sin email en log)
- Métrica: `identity.platform_login` (counter, dims: `result: challenge_issued|failure`)

**Casos de Aceptación**
- [ ] Credenciales válidas, TOTP configurado → 200, `setupRequired: false`, challenge token 5min
- [ ] Credenciales válidas, sin TOTP configurado → 200, `setupRequired: true`, challenge token 5min
- [ ] Email no existe → 401 "Credenciales inválidas"
- [ ] Password incorrecto → 401 mismo mensaje genérico
- [ ] Usuario `INACTIVE` → 403

---

### POST /api/v1/identity/platform/totp/verify

**Objetivo**
Segunda fase del login de Platform Admin: valida el código TOTP con el Challenge Token y emite el Platform Admin JWT definitivo.

**Auth**
TOTP Challenge Token (Bearer, `type: "totp_challenge"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "totpCode": "847291"
}
```

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjM2Q0ZTVmNiIsInJvbGVzIjpbImFkbWluIl0sInR5cGUiOiJwbGF0Zm9ybV9hZG1pbiIsImp0aSI6ImoxazJsM200IiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3ODA4MDB9.signature",
  "tokenType": "Bearer",
  "expiresIn": 28800,
  "user": {
    "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
    "email": "ops@mesto.app",
    "name": "Luis Operaciones",
    "roles": ["admin"]
  }
}
```

*Claims del Platform Admin JWT:*
```json
{
  "sub": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "roles": ["soporte", "billing"],
  "type": "platform_admin",
  "jti": "j1k2l3m4-n5o6-7890-jklm-no1234567890",
  "iat": 1717752000,
  "exp": 1717780800
}
```

**Validaciones**
- Authorization header `Bearer <token>` con TOTP Challenge Token: requerido.
- JWT `type` debe ser `"totp_challenge"`.
- `totpCode`: requerido, exactamente 6 dígitos numéricos (`^\d{6}$`).

**Reglas de Negocio**
- Validar JWT: RS256, `type = "totp_challenge"`, no expirado; si falla: 401.
- Si `setupRequired = true` en claims del challenge token: 400 "Debe configurar su autenticador TOTP primero. Use POST /platform/totp/setup."
- Extraer `sub` (userId).
- Consultar `platform_users` por `id = userId`; si `totp_secret IS NULL`: 400 "TOTP no configurado."
- Desencriptar `totp_secret` con AES-256-GCM usando `TOTP_ENCRYPTION_KEY` (env var).
- Validar `totpCode` con `Otp.NET`: `Totp.ComputeTotp()` con ventana de tolerancia ±1 step (30s), es decir, aceptar código del período actual y el anterior.
- Si código inválido: 401 "Código TOTP incorrecto."
- Consultar `platform_user_roles` por `user_id = userId` → lista de roles.
- Emitir Platform Admin JWT RS256, claims: `{ sub: userId, roles: [...], type: "platform_admin", jti: uuid(), iat, exp: iat+28800 }`.
- `UPDATE platform_users SET last_login = NOW() WHERE id = :userId` (si la columna existe; campo opcional en schema).

**Errores**
| Código | Condición |
|---|---|
| 400 | `totpCode` con formato incorrecto, `setupRequired = true` en el challenge token, o TOTP no configurado en DB |
| 401 | Challenge Token ausente, inválido, expirado, o código TOTP incorrecto |

**Observabilidad**
- Log: `"platform.totp_verify.success"` `{ userId, ip }`
- Log: `"platform.totp_verify.failed"` `{ userId, reason: "invalid_code|setup_required|totp_not_configured", ip }`
- Métrica: `identity.platform_totp_verify` (counter, dims: `result: success|failure`)

**Casos de Aceptación**
- [ ] Challenge Token válido + código TOTP correcto → 200, Platform Admin JWT con roles
- [ ] Código TOTP incorrecto → 401
- [ ] Código TOTP del período anterior (dentro de ventana de tolerancia) → 200
- [ ] Challenge Token expirado → 401
- [ ] Challenge Token con `setupRequired: true` → 400
- [ ] `totpCode` con 5 dígitos → 400
- [ ] `totpCode` con letras → 400

---

### POST /api/v1/identity/platform/totp/setup

**Objetivo**
Configura el autenticador TOTP para un Platform Admin: genera un nuevo secreto TOTP, retorna la URL QR y el código de emergencia de 24 dígitos (visible una sola vez).

**Auth**
Platform Admin JWT (Bearer, `type: "platform_admin"`) **O** TOTP Challenge Token con `setupRequired: true` (Bearer, `type: "totp_challenge"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "otpauthUrl": "otpauth://totp/Mesto%3Aops%40mesto.app?secret=JBSWY3DPEHPK3PXP&issuer=Mesto&algorithm=SHA1&digits=6&period=30",
  "secret": "JBSWY3DPEHPK3PXP",
  "emergencyCode": "847392016583920473819201",
  "message": "Guarda el código de emergencia en un lugar seguro. No se mostrará nuevamente."
}
```

**Validaciones**
- Authorization header con Platform Admin JWT (`type: "platform_admin"`) O con TOTP Challenge Token (`type: "totp_challenge"`, `setupRequired: true`): requerido.
- No se acepta ningún otro tipo de token.

**Reglas de Negocio**
- Determinar tipo de token presentado:
  - Si `type = "totp_challenge"` y `setupRequired = false`: 403 "Use el flujo de verificación TOTP normal (POST /totp/verify)."
  - Si `type = "totp_challenge"` y `setupRequired = true`: continuar (primer setup).
  - Si `type = "platform_admin"`: continuar (re-setup o setup adicional de dispositivo).
- Extraer `sub` (userId).
- Consultar `platform_users` por `id = userId`; si no existe: 404.
- Generar secreto TOTP: 20 bytes criptográficamente aleatorios codificados en Base32 (RFC 4648).
- Encriptar secreto con AES-256-GCM (clave `TOTP_ENCRYPTION_KEY`, IV aleatorio de 12 bytes; almacenar `{iv_hex}:{ciphertext_hex}:{tag_hex}`).
- Generar código de emergencia: 24 dígitos numéricos aleatorios (usar `crypto_random_bytes(15)` → convertir a número decimal, zero-padded a 24 dígitos).
- Hashear código de emergencia con bcrypt, cost factor 12.
- `UPDATE platform_users SET totp_secret = :encryptedSecret, emergency_code_hash = :hash WHERE id = :userId`.
- Construir `otpauthUrl` = `otpauth://totp/Mesto:{email_encoded}?secret={base32}&issuer=Mesto&algorithm=SHA1&digits=6&period=30`.
- Retornar `secret` en Base32, `otpauthUrl` y `emergencyCode` en claro — **esta es la única vez que se muestra el código de emergencia**.

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente, inválido o expirado |
| 403 | Token con `type: "totp_challenge"` pero `setupRequired: false` |
| 404 | Usuario no encontrado en DB |

**Observabilidad**
- Log: `"platform.totp_setup.success"` `{ userId }`
- Métrica: `identity.platform_totp_setup` (counter)

**Casos de Aceptación**
- [ ] Platform Admin JWT válido → 200, secreto generado, código de emergencia retornado (1 sola vez)
- [ ] TOTP Challenge Token con `setupRequired: true` → 200
- [ ] TOTP Challenge Token con `setupRequired: false` → 403
- [ ] Llamada repetida con Platform Admin JWT válido → 200, secreto anterior sobreescrito en DB
- [ ] Token expirado → 401

---

### POST /api/v1/identity/platform/totp/recover

**Objetivo**
Recupera el acceso al sistema cuando se pierde el dispositivo TOTP usando el código de emergencia; invalida el secreto TOTP actual y emite un Challenge Token para reconfigurar.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "ops@mesto.app",
  "emergencyCode": "847392016583920473819201"
}
```

**Response 200**
```json
{
  "challengeToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjM2Q0ZTVmNiIsInR5cGUiOiJ0b3RwX2NoYWxsZW5nZSIsInNldHVwUmVxdWlyZWQiOnRydWUsImp0aSI6ImwxbTJuM280IiwiaWF0IjoxNzE3NzUyMDAwLCJleHAiOjE3MTc3NTIzMDB9.signature",
  "setupRequired": true,
  "expiresIn": 300,
  "message": "Código de emergencia validado. Configure su nuevo autenticador TOTP antes de que expire este token."
}
```

**Validaciones**
- `email`: requerido, formato email válido.
- `emergencyCode`: requerido, exactamente 24 dígitos numéricos (`^\d{24}$`).

**Reglas de Negocio**
- Usar mensaje de error genérico "Código de emergencia inválido." para todos los casos de fallo (anti-enumeración).
- Buscar `platform_users` por `email`; si no existe: 400 (mensaje genérico).
- Si `emergency_code_hash IS NULL`: 400 (código ya usado o TOTP nunca configurado; mismo mensaje genérico).
- Comparar `emergencyCode` con `emergency_code_hash` (bcrypt.verify); si no coincide: 400 (mismo mensaje genérico).
- **El código de emergencia es de uso único**: `UPDATE platform_users SET totp_secret = NULL, emergency_code_hash = NULL WHERE id = :userId`.
- Emitir nuevo TOTP Challenge Token RS256, claims: `{ sub: userId, type: "totp_challenge", setupRequired: true, jti: uuid(), iat, exp: iat+300 }`.
- Registrar en `audit_log`: `{ action: "platform_totp_recovery", userId, ip, timestamp: NOW() }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Email no encontrado, código inválido, código ya usado, o formato incorrecto — siempre el mismo mensaje genérico |

**Observabilidad**
- Log: `"platform.totp_recover.success"` `{ userId, ip }`
- Log: `"platform.totp_recover.failed"` `{ reason: "user_not_found|invalid_code|code_already_used", ip }` (sin email en log)
- Métrica: `identity.platform_totp_recover` (counter, dims: `result: success|failure`)

**Casos de Aceptación**
- [ ] Email válido + código de emergencia correcto → 200, TOTP invalidado, Challenge Token con `setupRequired: true`, audit log registrado
- [ ] Código de emergencia incorrecto → 400 con mensaje genérico
- [ ] Email no existe → 400 con mismo mensaje genérico
- [ ] Código ya usado (re-llamada al endpoint) → 400 (`emergency_code_hash` es NULL tras primer uso)
- [ ] `emergencyCode` con 23 dígitos → 400
- [ ] `emergencyCode` con letras → 400

---

## 4. Gestión de Staff

### GET /api/v1/identity/staff

**Objetivo**
Lista todos los miembros del staff del tenant del Admin autenticado.

**Auth**
Staff JWT con `role: "Admin"` (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
[
  {
    "userId": "d4e5f6a7-b8c9-0123-defa-234567890123",
    "name": "Pedro Sánchez",
    "email": "pedro@restaurante.co",
    "roles": ["Cajero", "Mesero"],
    "status": "ACTIVE",
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "createdAt": "2024-06-07T10:00:00Z"
  }
]
```
*`userId` es el id de la identidad global; `roles[]` y `status` corresponden a la **membresía** en este tenant. `status` combina identidad y membresía: `PENDING` si el usuario aún no activó su cuenta, si no el estado de la membresía (`ACTIVE`|`INACTIVE`).*

**Reglas de Negocio** *(v1.3)*
- Aplicar convenciones de validación JWT; verificar `Admin` en los roles del claim; si no: 403.
- Extraer `tenantId` del JWT.
- Listar `user_memberships` filtradas por `tenant_id` y, por cada una, unir con `users` para nombre/email/estado de cuenta; sin paginación (N < 100). No incluir `password_hash`.

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente o inválido |
| 403 | JWT con rol distinto a Admin |

**Casos de Aceptación**
- [ ] Admin autenticado → 200, lista de todo el staff del tenant (incluyendo PENDING, ACTIVE, INACTIVE)
- [ ] JWT con rol Mesero → 403
- [ ] Tenant sin staff → 200, array vacío `[]`

---

### POST /api/v1/identity/staff/invite

**Objetivo**
Da de alta a un colaborador en el tenant del Admin autenticado, con **uno o varios roles**. Comportamiento según si el correo ya tiene una identidad global (`users`):
- **El correo ya existe (cualquier restaurante)** → se crea solo la **membresía** en este tenant reusando su contraseña actual; acceso **inmediato**. Respuesta `accessMode: EXISTING_USER` (sin enlace ni clave temporal). No se reenvía activación.
- **Correo nuevo** → se crea la identidad `users` + la membresía, según `accessMode`:
  - `ACTIVATION_LINK` (default): cuenta `users` en estado PENDING + enlace de activación (48h) para que defina su contraseña.
  - `DIRECT_ACCESS`: contraseña temporal, cuenta ACTIVE, `must_change_password = true`.

**Auth**
Staff JWT con `role: "Admin"` (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "name": "Pedro Sánchez",
  "email": "pedro@restaurante.co",
  "roles": ["Cajero", "Mesero"],
  "accessMode": "ACTIVATION_LINK"
}
```
*`roles`: lista no vacía con valores en `["Admin","Cajero","Mesero","KDS"]`. `accessMode` es opcional (default `ACTIVATION_LINK`); se ignora si el correo ya tiene cuenta.*

**Response 201 — correo nuevo, `accessMode: ACTIVATION_LINK`**
```json
{
  "userId": "d4e5f6a7-b8c9-0123-defa-234567890123",
  "name": "Pedro Sánchez",
  "email": "pedro@restaurante.co",
  "roles": ["Cajero", "Mesero"],
  "status": "PENDING",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "createdAt": "2024-06-07T10:00:00Z",
  "accessMode": "ACTIVATION_LINK",
  "activationUrl": "http://localhost:3003/staff/activate?token=a3f5c7e9..."
}
```

**Response 201 — `accessMode: DIRECT_ACCESS`** → `status: "ACTIVE"`, `temporaryPassword` presente (en vez de `activationUrl`).

**Response 201 — correo ya existente** → `accessMode: "EXISTING_USER"`, `status` reflejando la cuenta, **sin** `activationUrl` ni `temporaryPassword` (entra con su contraseña actual).

*`activationUrl` / `temporaryPassword` son mutuamente excluyentes y sólo aparecen para correos nuevos (`WhenWritingNull`). `temporaryPassword` se devuelve **una sola vez** en texto plano y nunca se persiste sin hashear.*

**Validaciones**
- `name`: requerido, 2–200 chars.
- `email`: requerido, RFC 5322, max 300 chars, lowercase.
- `roles`: requerido, lista no vacía; cada valor en `["Admin", "Cajero", "Mesero", "KDS"]`.
- `accessMode`: opcional, `["ACTIVATION_LINK", "DIRECT_ACCESS"]`; default `ACTIVATION_LINK`.

**Reglas de Negocio** *(v1.3 — identidad global)*
- Aplicar convenciones de validación JWT; verificar `Admin` en los roles del claim; si no: 403 "Permisos insuficientes."
- Extraer `tenantId` del JWT (el body no acepta `tenantId`). Normalizar `email` a lowercase.
- Buscar identidad en `users` por `email`:
  - **Existe** → si ya hay membresía `(user, tenant)`: 409 "Este usuario ya tiene acceso a este restaurante." Si no, insertar `user_memberships { id, user_id, tenant_id, roles, status: "ACTIVE" }` y responder `accessMode: EXISTING_USER`. **No** se toca su contraseña ni se envía email.
  - **No existe** → crear `users { id, name, email, status, ... }` + `user_memberships { ..., roles, status: "ACTIVE" }`:
    - `ACTIVATION_LINK`: `users.status = PENDING`; activation token `hex(64)`; `SET activation:{token} '{"userId":"...","tenantId":"..."}' EX 172800` + índice inverso; email con `{ADMIN_URL}/staff/activate?token={token}`. (Mismo mecanismo que el bootstrap del Admin al crear un tenant en Platform.)
    - `DIRECT_ACCESS`: contraseña temporal de 12 chars (alfabeto sin ambiguos, ≥1 mayús/minús/dígito); `users.status = ACTIVE`, `password_hash = bcrypt(temp,12)`, `must_change_password = true`. Entrega manual.
- Registrar en `audit_log`: `{ action: "staff_invited", actorId: adminId, targetId: userId, tenantId, metadata: { roles, accessMode } }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación (nombre/email/rol/`accessMode` inválidos, `roles` vacío) |
| 401 | JWT ausente o inválido |
| 403 | JWT sin rol Admin |
| 409 | El usuario ya tiene membresía en este tenant |

**Casos de Aceptación**
- [ ] Admin invita correo **nuevo** con `["Cajero","Mesero"]` (sin `accessMode`) → 201 PENDING, `activationUrl`, ambos roles en la membresía
- [ ] `accessMode: DIRECT_ACCESS` → 201 ACTIVE, `temporaryPassword`, `must_change_password = true`
- [ ] Invitar correo **ya existente** en otro restaurante → 201 `EXISTING_USER`, acceso inmediato, sin email ni clave
- [ ] Invitar a alguien que ya es miembro de este tenant → 409
- [ ] JWT con rol Cajero/Mesero (sin Admin) → 403
- [ ] `roles: []` o rol inválido → 400

---

### POST /api/v1/identity/staff/activate

**Objetivo**
Activa la cuenta de staff: establece la contraseña inicial y cambia el estado de PENDING a ACTIVE consumiendo el token de activación (uso único).

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "token": "a3f5c7e9d1b4a6c8e2f0d3b5a7c9e1f3d5b7a9c1e3f5d7b9a1c3e5f7d9b1a3c5e7f9a1b3c5d7e9f1a3b5",
  "password": "MiPrimera3Contraseña!",
  "confirmPassword": "MiPrimera3Contraseña!"
}
```

**Response 200**
```json
{
  "message": "Cuenta activada exitosamente.",
  "staff": {
    "userId": "d4e5f6a7-b8c9-0123-defa-234567890123",
    "name": "Pedro Sánchez",
    "email": "pedro@restaurante.co",
    "roles": [],
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  }
}
```
*La activación es de la **identidad global**; `tenantId` (el del enlace) le sirve al frontend para llevar al usuario al login del restaurante que lo invitó. Los `roles` efectivos se resuelven por membresía al iniciar sesión.*

**Validaciones**
- `token`: requerido, string hexadecimal no vacío.
- `password`: requerido, min 8 chars, al menos 1 mayúscula, 1 minúscula, 1 dígito.
- `confirmPassword`: requerido, debe ser idéntico a `password`.

**Reglas de Negocio**
- Si `password != confirmPassword`: 400 "Las contraseñas no coinciden."
- `GET activation:{token}` en Redis; si no existe (expirado o ya usado): 400 "El enlace de activación es inválido o ha expirado."
- Obtener `{ userId, tenantId }` del valor Redis.
- Consultar `users` por `id = userId`; si `status != 'PENDING'`: 400 "Esta cuenta ya ha sido activada o está en un estado no activable." *(La activación es de la **identidad global**; activa la cuenta para todas sus membresías.)*
- Hashear `password` con bcrypt, cost factor 12.
- `UPDATE users SET password_hash = :hash, status = 'ACTIVE' WHERE id = :userId`.
- `DEL activation:{token}` de Redis.
- `DEL activation:user:{userId}` de Redis.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos, passwords no coinciden, token inválido/expirado, o cuenta no está en PENDING |

**Observabilidad**
- Log: `"staff.activate.success"` `{ userId, tenantId, role }`
- Log: `"staff.activate.invalid_token"` `{ }`
- Log: `"staff.activate.already_active"` `{ userId }`
- Métrica: `identity.staff_activate` (counter, dims: `result: success|invalid_token|already_active`)

**Casos de Aceptación**
- [ ] Token válido, cuenta PENDING, passwords correctas → 200, status ACTIVE, ambos tokens Redis eliminados
- [ ] La respuesta de activación devuelve `staff.tenantId` para usarlo en el login del Admin Panel
- [ ] Token expirado → 400
- [ ] Token ya usado (segundo intento) → 400 (Redis ya no tiene la key)
- [ ] Cuenta ya en ACTIVE → 400
- [ ] `confirmPassword` no coincide → 400
- [ ] `password` sin mayúscula → 400
- [ ] `password` de 6 chars → 400

---

### POST /api/v1/identity/auth/change-password

**Objetivo**
Permite a un staff autenticado cambiar su propia contraseña. Es el paso obligatorio del flujo de **acceso directo**: el colaborador entra con la contraseña temporal (`must_change_password = true`) y debe definir una nueva. También sirve como cambio voluntario de contraseña para cualquier staff activo.

**Auth**
Staff JWT (Bearer) — política `StaffJwt`. El `staffId` se toma de `sub`; el body no acepta identificadores.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "currentPassword": "SSVsmna9dis4",
  "newPassword": "MiNueva3Clave",
  "confirmPassword": "MiNueva3Clave"
}
```

**Response 204**
Sin cuerpo. La contraseña queda actualizada y `must_change_password` pasa a `false`.

**Validaciones**
- `currentPassword`: requerido.
- `newPassword`: requerido, min 8 chars, al menos 1 mayúscula, 1 minúscula, 1 dígito.
- `confirmPassword`: requerido, idéntico a `newPassword`.

**Reglas de Negocio**
- Si `newPassword != confirmPassword`: 400 "Las contraseñas no coinciden."
- Consultar `users` por `id = sub`; si no existe o `status != 'ACTIVE'`: 401 "Cuenta no válida." *(El cambio de contraseña es global, afecta la única cuenta de la persona.)*
- Verificar `currentPassword` contra `password_hash` (bcrypt); si falla: 400 "La contraseña actual es incorrecta."
- Si `newPassword` es igual a la actual (bcrypt verify): 400 "La nueva contraseña debe ser distinta de la actual."
- `UPDATE users SET password_hash = bcrypt(newPassword, 12), must_change_password = false WHERE id = :sub`.
- El JWT vigente sigue siendo válido; el frontend ya tiene sesión y redirige al destino normal según rol.

**Errores**
| Código | Condición |
|---|---|
| 400 | Passwords no coinciden, política incumplida, contraseña actual incorrecta, o nueva == actual |
| 401 | JWT ausente/ inválido, o cuenta no activa |

**Observabilidad**
- Log: `"staff.change_password.success"` `{ staffId, tenantId }`
- Log: `"staff.change_password.invalid_current"` `{ staffId }`
- Métrica: `identity.staff_change_password` (counter, dims: `result: success|invalid_current|mismatch|same`)

**Casos de Aceptación**
- [ ] Staff con contraseña temporal cambia su clave → 204; nuevo login devuelve `mustChangePassword = false`
- [ ] Login con la contraseña temporal vieja tras el cambio → 401
- [ ] `currentPassword` incorrecta → 400
- [ ] `newPassword == currentPassword` → 400
- [ ] `newPassword` sin dígito → 400
- [ ] Sin JWT → 401

---

### POST /api/v1/identity/staff/{userId}/resend-invitation

**Objetivo**
Reenvía el email de invitación a un staff PENDING invalidando el token anterior y emitiendo uno nuevo.

**Auth**
Staff JWT con `role: "Admin"` (Bearer)

**Request**
- Path params: `{userId}` — UUID del miembro del staff al que re-invitar
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "staffId": "d4e5f6a7-b8c9-0123-defa-234567890123",
  "activationUrl": "http://localhost:3003/staff/activate?token=b4c6e8f0...",
  "expiresAt": "2024-06-09T10:00:00Z"
}
```

*`expiresAt` = `NOW() + 48h` en UTC ISO 8601. `activationUrl` — nuevo enlace de activación, debe mostrarse en Admin Panel.*

**Validaciones**
- `userId`: UUID v4 válido en path.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT; verificar `Admin` en los roles del claim; si no: 403.
- Extraer `tenantId` del JWT Admin.
- Verificar que exista membresía `(userId, tenantId)`; si no: 403 "No tiene permisos para gestionar este usuario." (alcance por tenant).
- Consultar `users` por `id = userId`; si no existe: 404 "Usuario no encontrado."
- Si `users.status = 'ACTIVE'`: 400 "El usuario ya ha activado su cuenta." *(La activación es global; si ya activó en otro restaurante, no necesita reenvío.)*
- Si `users.status = 'INACTIVE'`: 400 "No se puede reenviar invitación a un usuario inactivo."
- Invalidar token anterior: `GET activation:user:{userId}` → `oldToken`. Si `oldToken` existe: `DEL activation:{oldToken}`.
- Generar nuevo activation token: `hex(crypto_random_bytes(64))`.
- Almacenar en Redis:
  - `SET activation:{newToken} '{"userId":"...","tenantId":"..."}' EX 172800`
  - `SET activation:user:{userId} {newToken} EX 172800`
- Enviar email via SDD-007 con nuevo enlace de activación.
- Registrar en `audit_log`: `{ action: "staff_invite_resent", actorId: adminId, targetId: userId, tenantId }`.
- Calcular `expiresAt = NOW() + 172800s`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Staff ya ACTIVE o ya INACTIVE |
| 401 | JWT ausente o inválido |
| 403 | JWT con rol distinto a Admin, o Admin intentando gestionar staff de otro tenant |
| 404 | Staff no encontrado |

**Observabilidad**
- Log: `"staff.resend_invite.success"` `{ adminId, userId, tenantId }`
- Métrica: `identity.staff_resend_invite` (counter)

**Casos de Aceptación**
- [ ] Staff PENDING del mismo tenant → 200, token anterior invalidado, nuevo token generado, email enviado
- [ ] Staff ACTIVE → 400
- [ ] Staff INACTIVE → 400
- [ ] `userId` pertenece a otro tenant → 403
- [ ] `userId` con formato UUID inválido → 400 (middleware de validación de path)
- [ ] `userId` inexistente → 404

---

### POST /api/v1/identity/staff/{userId}/reset-password

**Objetivo**
Un Admin genera un **enlace de restablecimiento de contraseña** para un colaborador **activo** de su tenant que perdió su clave. El colaborador abre el enlace y define una nueva contraseña mediante el flujo existente `POST /staff/password-reset/confirm`. Es el equivalente, iniciado por el Admin, del "olvidé mi contraseña" self-service (que depende de email).

**Auth**
Staff JWT con `role: "Admin"` (Bearer).

**Request**
- Path params:
  - `userId`: UUID del colaborador a restablecer.
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "staffId": "d4e5f6a7-b8c9-0123-defa-234567890123",
  "email": "pedro@restaurante.co",
  "resetUrl": "http://localhost:3003/staff/password-reset?token=4053e2...",
  "expiresAt": "2026-06-08T22:22:00Z"
}
```
*`resetUrl` es el enlace que el Admin comparte con el colaborador. El email también se dispara (canal secundario; en dev es stub). El token de reset tiene TTL de 1 hora.*

**Validaciones**
- `userId`: UUID válido (validación de path).

**Reglas de Negocio**
- Verificar JWT y `Admin` en los roles del claim; si no: 403.
- Verificar que exista membresía `(userId, tenantId)` del Admin; si no: 403 "No tiene permisos para gestionar este usuario." (alcance por tenant).
- Buscar `users` por `id = userId`; si no existe: 404 "Usuario no encontrado."
- Si `users.status = 'PENDING'`: 400 `NOT_ACTIVATED` — la cuenta aún no se activó; el Admin debe usar "Reenviar enlace" de invitación.
- El reset es **global** (afecta la única contraseña del usuario). Generar reset token `hex(crypto_random_bytes(64))` y `SET reset:staff:{token} {userId} EX 3600` (1h).
- Construir `resetUrl = {ADMIN_URL}/staff/password-reset?token={token}` y enviar email (SDD-007, stub en dev).
- El colaborador completa con `POST /staff/password-reset/confirm`, que actualiza el hash y **limpia `must_change_password`** (define su propia contraseña, no una temporal).

**Errores**
| Código | Condición |
|---|---|
| 400 | Cuenta en PENDING (`NOT_ACTIVATED`) o INACTIVE |
| 401 | JWT ausente o inválido |
| 403 | Rol distinto a Admin, o `userId` de otro tenant |
| 404 | `userId` inexistente |

**Observabilidad**
- Log: `"staff.reset_password.link_generated"` `{ adminId, targetUserId, tenantId }`
- Métrica: `identity.staff_admin_reset` (counter, dims: `result: success|not_activated|inactive|forbidden`)

**Casos de Aceptación**
- [ ] Admin genera reset para staff ACTIVE del mismo tenant → 200 con `resetUrl` + `expiresAt`
- [ ] El colaborador usa el enlace, define nueva clave (confirm 200) y hace login con ella → `mustChangePassword = false`
- [ ] Staff PENDING → 400 `NOT_ACTIVATED`
- [ ] Staff INACTIVE → 400
- [ ] `userId` de otro tenant → 403
- [ ] JWT no-Admin → 403
- [ ] `userId` inexistente → 404

---

### POST /api/v1/identity/staff/password-reset/request

**Objetivo**
Inicia el flujo de recuperación de contraseña para staff; siempre responde 200 para evitar enumeración de cuentas.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "email": "pedro@restaurante.co",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response 200**
```json
{
  "message": "Si el correo está registrado, recibirás las instrucciones en breve."
}
```

**Validaciones**
- `email`: requerido, formato email válido.
- `tenantId`: requerido, UUID v4 válido.

**Reglas de Negocio**
- SIEMPRE retornar HTTP 200 con el mismo mensaje.
- Normalizar `email` a lowercase.
- Buscar `staff` por `email + tenant_id`:
  - Si no existe: no acción.
  - Si `status = 'PENDING'`: no enviar email (cuenta no activada).
  - Si `status = 'INACTIVE'`: no enviar email.
  - Si `status = 'ACTIVE'`:
    - Generar reset token: `hex(crypto_random_bytes(64))`.
    - `SET reset:staff:{token} '{"userId":"..."}' EX 3600` (1h).
    - Enviar email via SDD-007 con enlace `{FRONTEND_URL}/reset-password/staff?token={token}`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `email` con formato inválido o `tenantId` no es UUID válido |

**Observabilidad**
- Log: `"staff.password_reset.requested"` `{ userId, tenantId }` (solo si staff ACTIVE existe)
- Métrica: `identity.staff_password_reset_request` (counter)

**Casos de Aceptación**
- [ ] Email de staff ACTIVE → 200, email enviado, reset token en Redis
- [ ] Email no existe en tenant → 200, sin acción
- [ ] Staff con status PENDING → 200, sin email
- [ ] Staff con status INACTIVE → 200, sin email
- [ ] `tenantId` UUID inválido → 400
- [ ] Múltiples llamadas con mismo email → 200 cada vez, token anterior sobreescrito

---

### POST /api/v1/identity/staff/password-reset/confirm

**Objetivo**
Establece una nueva contraseña para el staff usando el token de recuperación recibido por email (uso único) e invalida todas las sesiones activas.

**Auth**
Sin autenticación requerida

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "token": "b4c6e8f0a2d4b6c8e0f2a4d6b8c0e2f4a6d8b0c2e4f6a8d0b2c4e6f8a0d2b4c6e8f0a2d4b6c8e0f2a4d6",
  "newPassword": "NuevaContr4seña!",
  "confirmPassword": "NuevaContr4seña!"
}
```

**Response 200**
```json
{
  "message": "Contraseña actualizada exitosamente. Por seguridad, todas las sesiones activas han sido cerradas."
}
```

**Validaciones**
- `token`: requerido, string hexadecimal no vacío.
- `newPassword`: requerido, min 8 chars, al menos 1 mayúscula, 1 minúscula, 1 dígito.
- `confirmPassword`: requerido, debe ser idéntico a `newPassword`.

**Reglas de Negocio**
- Si `newPassword != confirmPassword`: 400 "Las contraseñas no coinciden."
- `GET reset:staff:{token}` en Redis; si no existe: 400 "El enlace de recuperación es inválido o ha expirado."
- Obtener `{ userId }`.
- Consultar `users` por `id = userId`; si no existe o `status != 'ACTIVE'`: 400 "El enlace de recuperación es inválido." (no revelar detalles).
- Hashear `newPassword` con bcrypt, cost factor 12.
- `UPDATE users SET password_hash = :hash, must_change_password = false WHERE id = :userId` — el usuario define su propia contraseña (global), así que se limpia cualquier cambio forzado pendiente (p. ej. si la cuenta venía de un alta por acceso directo o de un reset iniciado por Admin/Ops).
- `DEL reset:staff:{token}` (uso único).
- Invalidar todas las sesiones activas del usuario (mismo mecanismo que logout-all sin JWT):
  - `SMEMBERS user_sessions:{userId}` → por cada token: `DEL refresh:{token}`.
  - `DEL user_sessions:{userId}`.
  - `SET auth_all_invalidated:{userId} {unix_now()} EX 28800`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos, passwords no coinciden, token inválido/expirado, o staff no ACTIVE |

**Observabilidad**
- Log: `"staff.password_reset.confirmed"` `{ userId, tenantId }`
- Log: `"staff.password_reset.invalid_token"` `{ }`
- Métrica: `identity.staff_password_reset_confirm` (counter, dims: `result: success|invalid_token`)

**Casos de Aceptación**
- [ ] Token válido, staff ACTIVE, passwords correctas → 200, password actualizado, sesiones invalidadas
- [ ] Token expirado → 400
- [ ] Mismo token usado dos veces → segundo intento 400
- [ ] `confirmPassword` no coincide → 400
- [ ] `newPassword` sin dígito → 400
- [ ] Staff INACTIVE (token válido en Redis pero cuenta inactiva) → 400

---

### PATCH /api/v1/identity/staff/{userId}

**Objetivo** *(v1.3)*
Actualiza la **membresía** de un miembro en este tenant: sus `roles` y/o su `status` (activar/desactivar en este restaurante). El `name` es de la identidad global (`users`).

> **v1.3** — la edición de roles se expone también como `PATCH /api/v1/identity/staff/{userId}/roles` (body `{ "roles": [...] }`) y la baja del restaurante como `DELETE /api/v1/identity/staff/{userId}` (elimina **solo la membresía** de este tenant; conserva la cuenta global y las demás membresías). Esta sección describe el PATCH combinado equivalente.

**Auth**
Staff JWT con rol `Admin` (Bearer)

**Request**
- Path params: `{userId}` — UUID de la **identidad** del miembro
- Body (campos opcionales; solo los presentes se actualizan):
```json
{
  "name": "Pedro Alberto Sánchez",
  "roles": ["Cajero"],
  "status": "INACTIVE"
}
```

**Response 200**
```json
{
  "userId": "d4e5f6a7-b8c9-0123-defa-234567890123",
  "name": "Pedro Alberto Sánchez",
  "email": "pedro@restaurante.co",
  "roles": ["Cajero"],
  "status": "INACTIVE",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "createdAt": "2024-06-07T10:00:00Z"
}
```

**Validaciones**
- `userId`: UUID v4 válido en path.
- Al menos un campo presente.
- `name`: si presente, 2–200 chars (actualiza `users.name`).
- `roles`: si presente, lista no vacía con valores en `["Admin", "Cajero", "Mesero", "KDS"]`.
- `status`: si presente, `["ACTIVE", "INACTIVE"]` (no se asigna `"PENDING"` manualmente; ese estado es de la cuenta global).

**Reglas de Negocio** *(v1.3)*
- Aplicar convenciones de validación JWT; verificar `Admin` en los roles del claim; si no: 403.
- Extraer `tenantId` y `sub` (adminId) del JWT.
- Buscar membresía `(userId, tenantId)`; si no existe: 404. (El alcance es por tenant; el Admin solo gestiona membresías de su restaurante.)
- Si `userId == adminId` y `status = 'INACTIVE'`: 400 "No puede desactivar su propia cuenta."
- Protección del único Admin: contar membresías con rol `Admin` y `status = 'ACTIVE'` en el tenant; si = 1 y la operación quitaría ese último Admin (desactivar o sacar `Admin` de sus roles): 400 "No puede modificar el único Administrador activo del restaurante."
- Aplicar: `name` → `UPDATE users`; `roles`/`status` → `UPDATE user_memberships`.
- Si `status` pasó a `'INACTIVE'`: invalidar sesiones del usuario en este tenant (`SMEMBERS user_sessions:{userId}` → `DEL refresh:{token}`, `DEL user_sessions:{userId}`, `SET auth_all_invalidated:{userId} {unix_now()} EX 28800`).
- Cambio de roles (`PATCH /staff/{userId}/roles`, `UpdateStaffRolesHandler`): *(v2.1 — diseñado, PRD-019)* Tras el commit, además de `InvalidateAllAsync`, hace `DEL staff_station:{tenantId}:{userId}`: `SetRoles` deja la estación a null al quitar el rol Runner.
- Baja (`DELETE /staff/{userId}`, `RemoveStaffHandler`): *(v2.1 — diseñado, PRD-019)* Tras el commit hace `DEL staff_station:{tenantId}:{userId}`.
- `audit_log`: `{ action: "staff_membership_updated", actorId: adminId, targetId: userId, tenantId, metadata: { changedFields, newValues } }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío, campo inválido, auto-desactivación, o último Admin activo del tenant |
| 401 | JWT ausente o inválido |
| 403 | JWT sin rol Admin |
| 404 | El usuario no tiene membresía en este tenant |

**Casos de Aceptación**
- [ ] Admin actualiza nombre → 200 (`users.name`)
- [ ] Admin cambia roles a `["Cajero","Mesero"]` → 200, membresía con ambos roles
- [ ] Admin desactiva una membresía → 200 INACTIVE, sesiones invalidadas; el usuario conserva acceso a otros restaurantes
- [ ] `DELETE /staff/{userId}` → quita la membresía de este tenant (no borra la cuenta)
- [ ] Admin intenta desactivarse a sí mismo → 400
- [ ] Último Admin activo intenta quitarse el rol Admin → 400
- [ ] `userId` sin membresía en este tenant → 404
- [ ] Body `{}` vacío → 400

---

### GET /api/v1/identity/staff

**Objetivo**
Lista los miembros del staff de un tenant con filtros opcionales y paginación.

**Auth**
Staff JWT con `role: "Admin"` (Bearer) **O** Platform Admin JWT (Bearer)

**Request**
- Path params: Ninguno
- Query params:
  - `tenantId`: requerido si el token es Platform Admin JWT; ignorado si es Staff JWT Admin (se usa el del JWT).
  - `status`: opcional, filtrar por `PENDING | ACTIVE | INACTIVE`.
  - `role`: opcional, filtrar por `Admin | Cajero | Mesero | KDS`.
  - `page`: opcional, entero >= 1, default `1`.
  - `limit`: opcional, entero 1–100, default `20`.
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "userId": "d4e5f6a7-b8c9-0123-defa-234567890123",
      "name": "Pedro Sánchez",
      "email": "pedro@restaurante.co",
      "roles": ["Cajero", "Mesero"],
      "status": "ACTIVE",
      "createdAt": "2024-06-07T10:00:00Z"
    },
    {
      "userId": "e5f6a7b8-c9d0-1234-efab-345678901234",
      "name": "Lucía Cajera",
      "email": "lucia@restaurante.co",
      "roles": ["Cajero"],
      "status": "ACTIVE",
      "createdAt": "2024-05-01T08:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 8,
    "totalPages": 1
  }
}
```

**Validaciones**
- Si es Platform Admin JWT: `tenantId` query param requerido y debe ser UUID v4 válido.
- `status`: si presente, valor en `["PENDING", "ACTIVE", "INACTIVE"]`.
- `role`: si presente, valor en `["Admin", "Cajero", "Mesero", "KDS"]`.
- `page`: entero positivo; si no es número válido → 400.
- `limit`: entero entre 1 y 100 inclusive; si fuera de rango → 400.

**Reglas de Negocio**
- Si el token es Staff JWT Admin: `tenantId` efectivo = `tenantId` del JWT (el query param `tenantId` es ignorado por seguridad).
- Si el token es Platform Admin JWT: `tenantId` efectivo = query param `tenantId`.
- Construir query *(v1.3 — join identidad + membresía)*:
  ```sql
  SELECT u.id AS user_id, u.name, u.email, m.roles, m.status, m.created_at
  FROM user_memberships m
  JOIN users u ON u.id = m.user_id
  WHERE m.tenant_id = :effectiveTenantId
    [AND m.status = :status]
    [AND m.roles LIKE '%' || :role || '%']   -- filtra por un rol dentro de la lista
  ORDER BY m.created_at DESC
  LIMIT :limit OFFSET (:page - 1) * :limit
  ```
- `roles` se devuelve como array; `status` es el de la membresía (o `PENDING` si la cuenta `users` aún no se activó). Ejecutar COUNT separado para `total`. Nunca retornar `password_hash`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `page`/`limit` inválidos, `status`/`role` con valor no permitido, o `tenantId` ausente/inválido cuando el token es Platform Admin |
| 401 | JWT ausente o inválido |
| 403 | JWT con tipo válido pero rol distinto a Admin (para Staff JWT) o distinto a `platform_admin` |

**Observabilidad**
- Log: `"staff.list.queried"` `{ requesterId, requesterType: "admin|platform_admin", tenantId, filters: { status, role }, pagination: { page, limit } }`
- Métrica: `identity.staff_list` (counter)

**Casos de Aceptación**
- [ ] Admin JWT consulta → 200, solo staff de su propio tenant
- [ ] Admin JWT con `tenantId` de otro tenant en query → 200, retorna su propio tenant (parámetro ignorado)
- [ ] Platform Admin JWT con `tenantId` válido → 200, staff de ese tenant
- [ ] Platform Admin JWT sin `tenantId` → 400
- [ ] Filtro `?status=PENDING` → solo staff con status PENDING
- [ ] Filtro `?role=Cajero` → solo Cajeros
- [ ] `?limit=101` → 400
- [ ] JWT con rol Mesero → 403

---

## 5. Device Tokens (KDS)

### POST /api/v1/identity/device-tokens

**Objetivo**
Registra un nuevo dispositivo KDS en el tenant del Admin autenticado y emite un Device Token JWT con TTL 30 días.

**Auth**
Staff JWT con `role: "Admin"` (Bearer)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "deviceId": "kds-cocina-01",
  "name": "KDS Cocina Principal",
  "stationId": "e5f6a7b8-c9d0-1234-efab-345678901234"
}
```

**Response 201**
```json
{
  "id": "f6a7b8c9-d0e1-2345-fabc-456789012345",
  "deviceId": "kds-cocina-01",
  "name": "KDS Cocina Principal",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stationId": "e5f6a7b8-c9d0-1234-efab-345678901234",
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmNmE3YjhjOSIsInRlbmFudElkIjoiM2ZhODVmNjQiLCJzdGF0aW9uSWQiOiJlNWY2YTdiOCIsInR5cGUiOiJkZXZpY2UiLCJkZXZpY2VJZCI6Imtkcy1jb2NpbmEtMDEiLCJqdGkiOiJtMW4ybyNwNCIsImlhdCI6MTcxNzc1MjAwMCwiZXhwIjoxNzIwMzQ0MDB9.signature",
  "tokenType": "Bearer",
  "expiresIn": 2592000,
  "createdAt": "2024-06-07T10:00:00Z"
}
```

*Claims del Device Token JWT:*
```json
{
  "sub": "f6a7b8c9-d0e1-2345-fabc-456789012345",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stationId": "e5f6a7b8-c9d0-1234-efab-345678901234",
  "deviceId": "kds-cocina-01",
  "type": "device",
  "jti": "m1n2o3p4-q5r6-7890-mnop-qr1234567890",
  "iat": 1717752000,
  "exp": 1720344000
}
```

**Validaciones**
- `deviceId`: requerido, 3–100 chars, solo alfanumérico con guiones y underscores (`^[a-zA-Z0-9_-]{3,100}$`).
- `name`: requerido, 2–100 chars.
- `stationId`: opcional, UUID v4 válido si presente.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT; verificar `role = "Admin"` en claims; si no: 403.
- Extraer `tenantId` del JWT Admin.
- Verificar unicidad dentro del tenant: buscar `device_tokens` donde `device_id = deviceId AND tenant_id = tenantId AND revoked_at IS NULL`; si existe: 409 "Ya existe un dispositivo activo con este ID en el restaurante."
- Si `stationId` está presente, verificar que la estación existe y pertenece al tenant (consultar tabla `kitchen_stations`); si no: 400 "Estación de cocina no encontrada."
- Insertar en `device_tokens`: `{ id: uuid(), tenant_id, device_id: deviceId, name, station_id: stationId, created_at: NOW(), last_seen: NOW() }`.
- Almacenar en Redis: `SET device:{tenantId}:{deviceId} '{"stationId":"...","lastSeen":"..."}' EX 2592000` (30d).
- Emitir Device Token JWT RS256, claims: `{ sub: deviceTokensId, tenantId, stationId, deviceId, type: "device", jti: uuid(), iat, exp: iat+2592000 }`.
- El `token` se retorna **únicamente** en esta respuesta; el dispositivo debe almacenarlo en `localStorage`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos o `stationId` no encontrada |
| 401 | JWT ausente o inválido |
| 403 | JWT con rol distinto a Admin |
| 409 | `deviceId` ya registrado y activo en el tenant |

**Observabilidad**
- Log: `"device.register.success"` `{ adminId, deviceTokenId, deviceId, tenantId, stationId }`
- Métrica: `identity.device_register` (counter)

**Casos de Aceptación**
- [ ] Admin registra nuevo device con `stationId` → 201, Device Token 30d, Redis set
- [ ] Admin registra device sin `stationId` → 201 (campo opcional)
- [ ] `deviceId` duplicado (activo) en mismo tenant → 409
- [ ] Mismo `deviceId` en distinto tenant → 201 (unicidad es por tenant)
- [ ] JWT con rol Cajero → 403
- [ ] `deviceId` con caracteres no permitidos (e.g. espacios) → 400

---

### POST /api/v1/identity/device-tokens/refresh

**Objetivo**
Renueva el Device Token JWT del dispositivo KDS presentando el token actual, extendiendo el TTL otros 30 días (rolling renewal).

**Auth**
Device Token JWT (Bearer, `type: "device"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmNmE3YjhjOSIsInRlbmFudElkIjoiM2ZhODVmNjQiLCJzdGF0aW9uSWQiOiJlNWY2YTdiOCIsInR5cGUiOiJkZXZpY2UiLCJkZXZpY2VJZCI6Imtkcy1jb2NpbmEtMDEiLCJqdGkiOiJuZXdqdGkiLCJpYXQiOjE3MTc3NTIwMDAsImV4cCI6MTcyMDM0NDAwMH0.signature",
  "tokenType": "Bearer",
  "expiresIn": 2592000
}
```

**Validaciones**
- Authorization header `Bearer <token>` con Device Token JWT: requerido.
- JWT `type` debe ser `"device"`.

**Reglas de Negocio**
- Validar JWT: RS256, `type = "device"`, no expirado; si falla: 401.
- Extraer `sub` (deviceTokenId), `tenantId`, `deviceId`, `stationId`, `jti`.
- Consultar `device_tokens` por `id = deviceTokenId`; si no existe: 401 "Dispositivo no encontrado."
- Si `device_tokens.revoked_at IS NOT NULL`: 401 "Dispositivo revocado."
- Verificar Redis `device:{tenantId}:{deviceId}`; si no existe (TTL expirado, > 30d sin renovar): 401 "Sesión de dispositivo expirada. Registre el dispositivo nuevamente."
- Actualizar Redis: `SET device:{tenantId}:{deviceId} '{"stationId":"...","lastSeen":"..."}' EX 2592000` (renueva TTL).
- `UPDATE device_tokens SET last_seen = NOW() WHERE id = :deviceTokenId`.
- Emitir nuevo Device Token JWT RS256, claims: `{ sub: deviceTokenId, tenantId, stationId, deviceId, type: "device", jti: uuid(), iat, exp: iat+2592000 }`.
- El token antiguo no es añadido a blacklist (el JWT anterior puede coexistir hasta su expiración natural; la fuente de verdad de revocación es la tabla `device_tokens.revoked_at` y la clave Redis).

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente, inválido, expirado, dispositivo revocado en DB, o clave Redis expirada |

**Observabilidad**
- Log: `"device.token_refresh.success"` `{ deviceTokenId, deviceId, tenantId }`
- Log: `"device.token_refresh.failed"` `{ reason: "jwt_invalid|jwt_expired|revoked|redis_expired|not_found" }`
- Métrica: `identity.device_token_refresh` (counter, dims: `result: success|failure`)

**Casos de Aceptación**
- [ ] Device Token válido, dispositivo activo, Redis key presente → 200, nuevo token 30d, Redis TTL renovado
- [ ] Device Token expirado → 401
- [ ] Dispositivo revocado (`revoked_at IS NOT NULL`) → 401
- [ ] Redis key expirada (más de 30 días sin llamar a este endpoint) → 401
- [ ] JWT con `type = "staff"` → 401

---

### DELETE /api/v1/identity/device-tokens/{tokenId}

**Objetivo**
Revoca permanentemente un dispositivo KDS impidiendo que su token actual y futuros refresh sean aceptados.

**Auth**
Staff JWT con `role: "Admin"` (Bearer)

**Request**
- Path params: `{tokenId}` — UUID del registro en la tabla `device_tokens` (campo `id`)
- Query params: Ninguno
- Body: Ninguno

**Response 204**
*(Sin cuerpo)*

**Validaciones**
- `tokenId`: UUID v4 válido en path.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT; verificar `role = "Admin"` en claims; si no: 403.
- Extraer `tenantId` del JWT Admin.
- Consultar `device_tokens` por `id = tokenId`; si no existe: 404 "Dispositivo no encontrado."
- Verificar `device_tokens.tenant_id = tenantId` del JWT; si no: 403 "No tiene permisos para gestionar este dispositivo."
- Si `device_tokens.revoked_at IS NOT NULL`: 400 "El dispositivo ya está revocado."
- `UPDATE device_tokens SET revoked_at = NOW() WHERE id = :tokenId`.
- `DEL device:{tenantId}:{deviceId}` de Redis (invalidar clave de sesión rolling).
- Registrar en `audit_log`: `{ action: "device_revoked", actorId: adminId, targetId: tokenId, tenantId, metadata: { deviceId, name } }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Dispositivo ya estaba revocado |
| 401 | JWT ausente o inválido |
| 403 | JWT con rol distinto a Admin, o Admin gestionando dispositivo de otro tenant |
| 404 | Dispositivo no encontrado |

**Observabilidad**
- Log: `"device.revoke.success"` `{ adminId, tokenId, deviceId, tenantId }`
- Métrica: `identity.device_revoke` (counter)

**Casos de Aceptación**
- [ ] Admin revoca dispositivo de su tenant → 204, `revoked_at` set, Redis key DEL
- [ ] Intentar revocar dispositivo ya revocado → 400
- [ ] `tokenId` de otro tenant → 403
- [ ] `tokenId` inexistente → 404
- [ ] Device Token del dispositivo revocado usado en POST /device-tokens/refresh → 401 (por `revoked_at` en DB)

---

## 6. Impersonación (Ops)

### POST /api/v1/identity/impersonate

**Objetivo**
Genera un ImpersonationToken que permite a un Platform Admin acceder temporalmente al panel de administración de un tenant específico; registra la acción en el audit log.

**Auth**
Platform Admin JWT (Bearer, `type: "platform_admin"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "reason": "Soporte técnico: ticket #4521 — cliente reporta error en cierre de caja"
}
```

**Response 200**
```json
{
  "impersonationToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjM2Q0ZTVmNiIsInJvbGUiOiJwbGF0Zm9ybV9hZG1pbiIsImltcGVyc29uYXRpbmciOiIzZmE4NWY2NCIsInR5cGUiOiJpbXBlcnNvbmF0aW9uIiwianRpIjoicDFxMnIzczQiLCJpYXQiOjE3MTc3NTIwMDAsImV4cCI6MTcxNzc1MjkwMH0.signature",
  "expiresIn": 900,
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "redirectUrl": "https://admin.mesto.app/impersonate?token=eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

*Claims del ImpersonationToken:*
```json
{
  "sub": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "role": "platform_admin",
  "impersonating": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "impersonation",
  "jti": "p1q2r3s4-t5u6-7890-pqrs-tu1234567890",
  "iat": 1717752000,
  "exp": 1717752900
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido.
- `reason`: requerido, string entre 5 y 500 chars.

**Reglas de Negocio**
- Aplicar convenciones de validación JWT; verificar `type = "platform_admin"`; si no: 403.
- Extraer `sub` (opsUserId) y `roles` del JWT.
- Verificar que el tenant `tenantId` existe y está activo; si no: 404 "Tenant no encontrado."
- Verificar que no existe sesión activa: `GET impersonation:{opsUserId}:{tenantId}` en Redis; si existe: 409 "Ya existe una sesión de impersonación activa para este tenant. Use POST /impersonate/renew o finalícela primero."
- Generar `jti` = `uuid()`.
- Emitir ImpersonationToken RS256, claims: `{ sub: opsUserId, role: "platform_admin", impersonating: tenantId, type: "impersonation", jti, iat, exp: iat+900 }`.
- Almacenar en Redis: `SET impersonation:{opsUserId}:{tenantId} '{"jti":"...","reason":"..."}' EX 900`.
- Construir `redirectUrl` = `{ADMIN_APP_URL}/impersonate?token={impersonationToken}`.
- Registrar en `audit_log`: `{ action: "impersonation_started", actorId: opsUserId, targetTenantId: tenantId, reason, ip, timestamp: NOW(), jti }`.

**Errores**
| Código | Condición |
|---|---|
| 400 | Validación de campos (`tenantId` inválido, `reason` muy corta o muy larga) |
| 401 | JWT ausente, inválido o expirado |
| 403 | JWT con tipo distinto a `platform_admin` |
| 404 | Tenant no encontrado o inactivo |
| 409 | Ya existe sesión de impersonación activa para este opsUser+tenant |

**Observabilidad**
- Log: `"impersonation.started"` `{ opsUserId, tenantId, reason, ip, jti }` *(audit-level)*
- Métrica: `identity.impersonation_start` (counter)

**Casos de Aceptación**
- [ ] Platform Admin impersona tenant activo con `reason` → 200, token emitido, Redis key creada, audit log registrado, `redirectUrl` con token
- [ ] Intentar segunda impersonación al mismo tenant → 409
- [ ] Tenant inexistente → 404
- [ ] JWT con `type = "staff"` → 403
- [ ] `reason` con 4 chars → 400
- [ ] `reason` ausente → 400

---

### POST /api/v1/identity/impersonate/renew

**Objetivo**
Renueva el ImpersonationToken emitiendo uno nuevo y actualizando el TTL en Redis; descarta el token anterior.

**Auth**
ImpersonationToken (Bearer, `type: "impersonation"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "impersonationToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjM2Q0ZTVmNiIsInJvbGUiOiJwbGF0Zm9ybV9hZG1pbiIsImltcGVyc29uYXRpbmciOiIzZmE4NWY2NCIsInR5cGUiOiJpbXBlcnNvbmF0aW9uIiwianRpIjoibmV3anRpIiwiaWF0IjoxNzE3NzUyNTAwLCJleHAiOjE3MTc3NTM0MDB9.signature",
  "expiresIn": 900
}
```

**Validaciones**
- Authorization header `Bearer <token>` con ImpersonationToken: requerido.
- JWT `type` debe ser `"impersonation"`.

**Reglas de Negocio**
- Validar JWT: RS256, `type = "impersonation"`, no expirado; si falla: 401.
- Extraer `sub` (opsUserId), `impersonating` (tenantId), `jti` (oldJti).
- `GET impersonation:{opsUserId}:{tenantId}` en Redis; si no existe: 401 "Sesión de impersonación no encontrada o expirada."
- Deserializar valor Redis → `{ jti: storedJti, reason }`. Verificar que `storedJti == oldJti`; si no coincide: 401 "Token de impersonación inválido." (puede indicar que ya fue renovado con el mismo token o un intento de replay).
- Generar nuevo `jti` = `uuid()`.
- Emitir nuevo ImpersonationToken RS256, TTL 15 min, mismos claims excepto nuevo `jti`, `iat` y `exp`.
- Actualizar Redis: `SET impersonation:{opsUserId}:{tenantId} '{"jti":"newJti","reason":"..."}' EX 900` (renueva TTL).
- Registrar en `audit_log`: `{ action: "impersonation_renewed", actorId: opsUserId, targetTenantId: tenantId, oldJti, newJti }`.

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente, inválido, expirado, sesión Redis no encontrada, o `jti` no coincide con Redis |

**Observabilidad**
- Log: `"impersonation.renewed"` `{ opsUserId, tenantId, oldJti, newJti }`
- Métrica: `identity.impersonation_renew` (counter)

**Casos de Aceptación**
- [ ] ImpersonationToken válido con `jti` correcto en Redis → 200, nuevo token, Redis TTL renovado, audit log
- [ ] Token expirado → 401
- [ ] Redis key expirada (sesión de 15 min abandonada sin renovar) → 401
- [ ] `jti` del token no coincide con el almacenado en Redis → 401
- [ ] Reutilizar un ImpersonationToken ya renovado (el viejo `jti` ya no está en Redis) → 401

---

### POST /api/v1/identity/impersonate/end

**Objetivo**
Finaliza explícitamente la sesión de impersonación: elimina el token de Redis, lo añade a la blacklist y registra el evento en el audit log.

**Auth**
ImpersonationToken (Bearer, `type: "impersonation"`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body: Ninguno

**Response 204**
*(Sin cuerpo)*

**Validaciones**
- Authorization header `Bearer <token>` con ImpersonationToken: requerido.
- JWT `type` debe ser `"impersonation"`.

**Reglas de Negocio**
- Validar JWT: RS256, `type = "impersonation"`, no expirado; si falla: 401.
- Extraer `sub` (opsUserId), `impersonating` (tenantId), `jti`, `exp`.
- `DEL impersonation:{opsUserId}:{tenantId}` de Redis (operación idempotente, no falla si la key ya no existe).
- Añadir token a blacklist: `SET blacklist:{jti} 1 EX {MAX(1, exp - unix_now())}`.
- Registrar en `audit_log`: `{ action: "impersonation_ended", actorId: opsUserId, targetTenantId: tenantId, jti, ip, timestamp: NOW() }`.

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente, inválido o expirado |

**Observabilidad**
- Log: `"impersonation.ended"` `{ opsUserId, tenantId, jti, ip }` *(audit-level)*
- Métrica: `identity.impersonation_end` (counter)

**Casos de Aceptación**
- [ ] ImpersonationToken válido → 204, Redis DEL, token en blacklist, audit log registrado
- [ ] Token expirado → 401
- [ ] Llamar POST /end dos veces con el mismo token → primer intento 204; segundo intento 401 (token ya en blacklist, validado en convenciones de JWT)
- [ ] Redis key ya había expirado (sesión expirada naturalmente) → 204 (DEL es idempotente; token sigue siendo válido hasta `exp`, por eso se añade a blacklist)

---

## 7. Venue — modo del local *(diseñado — pendiente de implementación, PRD-019)*

Playa Demo es dos negocios en el mismo local: restaurante de día y fiesta de noche. El cambio
entre uno y otro —«Mesto Beach» ↔ «Mesto Beach Night»— es **una decisión del administrador,
manual, para todo el local y auditada** (decisión cerrada con el cliente, PRD-019), y el sistema
entero la obedece: carta (SDD-002), distribución y PIN (SDD-003), estaciones y colas (SDD-004),
reportes (SDD-006). Identity es el dueño del dato porque el modo es **un atributo del tenant**, no
de ningún módulo operativo, y porque el único rol que puede cambiarlo es el `Admin` del tenant, que
ya vive aquí (ADR-036).

**Dónde vive**: controlador `MestoBeach.API/Controllers/Identity/VenueController.cs`, base path
**`/api/v1/venue`** (fuera de `/api/v1/identity` a propósito: lo consumen el menú anónimo, el
personal y el panel, y la ruta corta es la que sondean las tres apps). Handlers en
`MestoBeach.Identity.Application/Venue/`. Enum **`VenueMode { RESTAURANT, NIGHT }`** en
`MestoBeach.Shared` (`MestoBeach.Shared/Venue/VenueMode.cs`), porque lo serializan Catalog, Sessions,
Orders y Analytics y ninguno puede depender de Identity. En el wire siempre es string en
mayúsculas. Los DTOs de respuesta (`mode`, `previous`, `fromMode`, `toMode`) son `string` y se
rellenan con `mode.ToString()` (ya en mayúsculas porque los miembros del enum lo están), siguiendo
el patrón `Wire()` de Orders. No se añade un `JsonStringEnumConverter` global. Vale lo mismo para
el objeto `VENUE_MODE_CHANGED` que Orders difunde por `OrdersHub` (SDD-004 §Eventos SignalR),
porque SignalR tampoco tiene conversor de enums. Los campos nulos (`changedAt`, `changedByName`)
**se omiten** por `DefaultIgnoreCondition = WhenWritingNull`.

### Esquema PostgreSQL

#### `tenants` — columnas nuevas

| Columna | Tipo | Notas |
|---|---|---|
| `venue_mode` | varchar(16) not null default 'RESTAURANT' | `RESTAURANT` \| `NIGHT`. **Fuente de verdad** del modo; Redis es caché |
| `venue_mode_changed_at` | timestamptz null | Último cambio efectivo. `null` = nunca ha salido de `RESTAURANT` |

#### `venue_mode_changes` — tabla nueva (auditoría del cambio)

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid not null | FK `tenants(id)`; índice `(tenant_id, changed_at DESC)` — es la consulta del historial |
| `from_mode` | varchar(16) not null | Modo anterior |
| `to_mode` | varchar(16) not null | Modo nuevo. `from_mode <> to_mode` siempre: un cambio idempotente no deja fila |
| `changed_by` | uuid not null | `users.id` del Admin que lo hizo (sin FK: la cuenta puede borrarse y la auditoría se queda) |
| `changed_at` | timestamptz not null | |
| `preflight` | jsonb not null | Foto del pre-flight en el momento del cambio: `{ "openSessions": 0, "activeItems": 0, "openTrips": 0, "orphanItems": 0, "tables": [] }`. Con la transición cerrada los contadores que bloquean siempre son ceros; se guarda igual por si un día se admite un `force`. `orphanItems` puede ser mayor que 0: los platos de mesas ya cerradas no bloquean |

Migración **`AddTenantVenueModeAndChanges`** (Identity). Se escribe **además** la fila habitual de
`audit_log` (`venue_mode_changed`): la tabla propia existe para que el historial del panel sea una
consulta barata y tipada, no un `LIKE` sobre el log general.

### Redis

| Clave | Valor | TTL | Reglas |
|---|---|---|---|
| `venue:{tenantId}:mode` | `RESTAURANT` \| `NIGHT` | **10 min** (se renueva en cada `SET`) | La escribe `ChangeVenueModeHandler` (`SET … EX 600` tras confirmar la transacción, también en la rama idempotente) y la re-siembra del proveedor. Si falta, el proveedor lee `tenants.venue_mode` y la re-siembra con `SET … NX EX 600`, para no pisar un `SET` del handler; si el tenant no tiene fila en `tenants`, devuelve `RESTAURANT` y **no escribe la clave** (no se registra como `venue_mode.redis_unavailable`; solo se escribe cuando se ha leído una fila real). Si Redis no responde (error o timeout), el proveedor lee `tenants.venue_mode` de Postgres **sin** `SET` y registra `venue_mode.redis_unavailable` (warning). Postgres es la verdad: el proveedor nunca se inventa un modo. Si Redis y Postgres fallan a la vez, lanza `VenueModeUnavailableException` |
| `staff_station:{tenantId}:{userId}` | uuid de la estación, o `-` (centinela «sin estación») | **60 s** | Caché de `user_memberships.assigned_station_id` para `IStaffStationResolver` (abajo). La borran, siempre tras el commit, `PATCH /staff/{userId}/assignment`, `PATCH /staff/{userId}/roles` y `DELETE /staff/{userId}`; el centinela `-` evita ir a la base en cada petición de quien no es runner (o no tiene membresía activa) |

> **Por qué un TTL largo y no corto (ni ninguno).** Sin TTL, una clave vieja que no se pudo pisar
> (Redis inalcanzable justo en el cambio: fallan a la vez el `SET` y el `DEL`, y la clave sobrevive
> al volver Redis, incluso a un reinicio con RDB) dejaría a Catalog, Orders, Sessions y el menú en
> el modo anterior indefinidamente, y reintentar el `POST` no la arreglaría. Con 10 min el coste es
> una lectura por PK por tenant cada 10 min —despreciable: `GET /venue/{tenantId}` ya hace una cada
> 30 s por dispositivo— y el TTL acota la vida de cualquier valor desfasado. La re-siembra usa
> `SET NX` para que un lector que leyó Postgres antes del `COMMIT` no pise el `SET` del handler.

### `IVenueModeProvider` y `VenueModeChangedNotification`

```csharp
// MestoBeach.Shared/Venue/IVenueModeProvider.cs
public interface IVenueModeProvider
{
    /// Modo vigente del local. Redis → (miss) Postgres + SET NX EX 600 (solo si el tenant existe) → (Redis caído) Postgres sin SET. Lanza VenueModeUnavailableException solo si fallan Redis y Postgres; nunca devuelve un modo por defecto para un tenant existente.
    Task<VenueMode> GetAsync(Guid tenantId, CancellationToken ct = default);
}

// MestoBeach.Shared/Venue/VenueModeUnavailableException.cs
public sealed class VenueModeUnavailableException : Exception
{
    public VenueModeUnavailableException(Guid tenantId, Exception inner)
        : base($"No se pudo leer el modo del tenant {tenantId}: Redis y Postgres fallaron.", inner) { }
}

// MestoBeach.Shared/Venue/VenueModeChangedNotification.cs
public sealed record VenueModeChangedNotification(
    Guid TenantId, VenueMode Mode, VenueMode Previous, DateTime ChangedAt, Guid ChangedBy)
    : INotification;
```

- **Una sola implementación**, `VenueModeProvider` en `MestoBeach.Identity.Infrastructure/Venue/`,
  registrada en `IdentityModule` como **Scoped**
  (`services.AddScoped<MestoBeach.Shared.Venue.IVenueModeProvider, Venue.VenueModeProvider>()`), igual
  que `IPlanEntitlementsReader` → `PlanEntitlementsReader`: inyecta `IdentityDbContext` (scoped) e
  `IConnectionMultiplexer` (singleton). El resto de módulos la inyectan por la interfaz desde sus
  handlers y controladores, que son scoped; no debe consumirla ningún servicio singleton (si alguno
  la necesitara, que abra su propio scope con `IServiceScopeFactory`). Lectura: Redis → (*miss*)
  Postgres + `SET NX EX 600` (sin `SET` si no hay fila) → (Redis caído) Postgres sin `SET` +
  warning → (ambos caídos) `VenueModeUnavailableException`. Los consumidores la traducen a
  `503 VENUE_MODE_UNAVAILABLE`: Orders en el POST de comanda, Sessions en open/join, Catalog en el
  GET público (nunca la carta sin filtrar), y este módulo en `GET /venue/{tenantId}` y `GET /venue`.
  Ningún otro módulo lee `tenants.venue_mode` ni la clave de Redis por su cuenta.
- La **notificación** vive en `MestoBeach.Shared` y no en Identity para que Orders y Catalog puedan
  implementar `INotificationHandler<VenueModeChangedNotification>` sin referenciar Identity.
  Cada `*Module.cs` llama a `AddMediatR` registrando **solo su ensamblado Application**
  (`RegisterServicesFromAssembly(typeof(<Comando>).Assembly)`). Por eso los handlers de la
  notificación viven en la capa Application de Orders y de Catalog, igual que el resto de handlers
  del repo. Un handler puesto en Infrastructure no se registraría, y `Publish` no avisaría: ni error
  ni handler ejecutado. Con un solo contenedor, `IPublisher.Publish` resuelve todos los handlers
  **registrados**. El test de integración del PR1 comprueba, contra el contenedor real de la API,
  que `IEnumerable<INotificationHandler<VenueModeChangedNotification>>` resuelve exactamente 2
  handlers, además de publicar → caché invalidada + evento en el hub.
- Handlers:

  | Módulo | Handler | Qué hace |
  |---|---|---|
  | Orders | `VenueModeChangedBroadcastHandler` (`MestoBeach.Orders.Application`) | `IOrderSignalRNotifier.NotifyTenantAsync(tenantId, {type:"VENUE_MODE_CHANGED", mode, previous, changedAt})` al grupo `tenant:{t}` (SDD-004 §Eventos SignalR) |
  | Catalog | `VenueModeChangedCacheHandler` (`MestoBeach.Catalog.Application`) | `ICatalogCacheService.InvalidateAsync(tenantId)` (`DEL catalog:{t}`). **Defensivo**: la caché se guarda sin filtrar por modo (ADR-041) y el filtro se aplica al servir, así que no haría falta; se invalida igual para que un `visibleIn` editado durante el modo anterior nunca sobreviva al cambio |

- Los handlers son **best-effort**: `Publish` va dentro de un `try/catch` en el handler del comando
  y cada handler atrapa lo suyo; un fallo se registra (`venue_mode.notify_failed {Handler}`) y
  **no** deshace el cambio, que ya está en Postgres y en Redis.

---

### GET /api/v1/venue/{tenantId}

**Objetivo**
Dice en qué modo está el local, sin autenticación: es lo que el menú del comensal sondea para cambiar de carta y de tema.

**Auth**
Ninguna (`[AllowAnonymous]`), igual que `GET /api/v1/catalog/{tenantId}`.

**Request**
- Path params: `{tenantId}` — UUID; la ruta es `[HttpGet("{tenantId:guid}")]`, igual que `CatalogPublicController`
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "mode": "NIGHT",
  "changedAt": "2026-10-10T22:00:04Z"
}
```
Cabecera **`Cache-Control: no-store`** (y `Pragma: no-cache`): ni el navegador ni un proxy pueden servir un modo viejo — la app lo pregunta cada 30 s y al volver visible (`visibilitychange`), y una respuesta cacheada cinco minutos la dejaría de día en plena noche.

**Validaciones**
- un `tenantId` que no es UUID no casa la ruta → 404

**Reglas de Negocio**
- Orden fijo: primero la lectura por PK de `tenants`; si no hay fila → `404` **sin** llamar a `IVenueModeProvider`. Si la hay, `mode` sale del proveedor (Redis; con *miss* re-siembra; con Redis caído lee Postgres —puede reutilizar esta misma lectura por PK para que `mode` y `changedAt` salgan de la misma fila—), la **misma** fuente que ven Orders y Catalog.
- `changedAt` sale de `venue_mode_changed_at` de esa misma fila y **se omite** si nunca cambió (`WhenWritingNull`). Es una consulta por PK cada 30 s por dispositivo: asumible.
- No revela nada más del tenant: ni nombre, ni plan, ni si está suspendido. Un tenant inexistente es `404`.
- Sin rate limit propio: el coste es una lectura de Redis más una por PK, y el menú ya sondea otros endpoints anónimos.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `NOT_FOUND` | 404 | UUID mal formado o tenant inexistente |
| `VENUE_MODE_UNAVAILABLE` | 503 | Redis y Postgres inaccesibles |

**Observabilidad**
- Sin log por petición (es un sondeo); `venue_mode.redis_unavailable {TenantId}` (warning) cuando hubo que leer de Postgres por Redis caído
- Métrica: `venue.mode_reads` (counter, tag `source = redis | postgres | unavailable`)

**Casos de Aceptación**
- [ ] Sin token → 200 con `mode` y `changedAt`; cabecera `Cache-Control: no-store`
- [ ] Tras `POST /venue/mode NIGHT`, la siguiente lectura devuelve `NIGHT` (sin esperar a ningún TTL)
- [ ] Con la clave de Redis borrada a mano, la lectura devuelve el modo de Postgres y vuelve a dejar la clave
- [ ] Con Redis caído y el local en NIGHT → 200 `NIGHT` (leído de Postgres), sin `SET`, con warning; nunca RESTAURANT por defecto
- [ ] Con Redis y Postgres caídos → 503 `VENUE_MODE_UNAVAILABLE`
- [ ] Tenant inexistente → 404
- [ ] UUID aleatorio → 404 y en Redis no queda ninguna clave `venue:{uuid}:mode`
- [ ] Tenant que nunca cambió → 200 sin `changedAt`

---

### GET /api/v1/venue

**Objetivo**
La misma respuesta que la anónima, más el estado de las estaciones en este modo: es lo que el personal relee al recibir `VENUE_MODE_CHANGED` y al arrancar.

**Auth**
Staff JWT de cualquier rol o KDS Device Token, con la política nueva **`StaffOrDeviceJwt`** (`IsAuthenticated && UserType is "staff" or "device"`), declarada en `MestoBeach.API/Middleware/AuthorizationPolicies.cs`. Va como un único `[Authorize(Policy = AuthorizationPolicies.StaffOrDeviceJwt)]`: si se apilan `StaffJwt` y `DeviceJwt`, ASP.NET exige las dos y responde 403. El tenant sale de `HttpContext.Items["tenant_id"]`, que el middleware rellena desde el claim `tenantId` en ambos tipos de token. No cambian en este PR las políticas de `GET /kds-stations` ni de `/items/{id}/ready`.

**Request**
- Path params / Query params / Body: Ninguno

**Response 200**
```json
{
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "mode": "NIGHT",
  "changedAt": "2026-10-10T22:00:04Z",
  "stations": [
    { "id": "c3d4e5f6-…", "name": "Cocina",   "flowProfile": "FULL_PASS", "isOn": false },
    { "id": "d4e5f6a7-…", "name": "Parrilla", "flowProfile": "FULL_PASS", "isOn": false },
    { "id": "e5f6a7b8-…", "name": "Barra 1",  "flowProfile": "BAR",       "isOn": true  },
    { "id": "f6a7b8c9-…", "name": "Barra 2",  "flowProfile": "BAR",       "isOn": true  }
  ]
}
```

**Validaciones**
- Ninguna de entrada; `tenantId` de las claims obligatorio (401 si el token no lo trae)

**Reglas de Negocio**
- **Por PR:** en PR1 el endpoint devuelve solo `{tenantId, mode, changedAt}` con `Cache-Control: no-store`; PR3 (`feat/estaciones-perfil-barra`, una vez existe la migración de Orders `AddKdsStationProfileAndModes`) añade `stations`. `stations` sale de la proyección `KdsStationReadModel` que Identity ya tiene de `kds_stations` (`AssignmentReadModels.cs`, `ExcludeFromMigrations`), ampliada en PR3 (`KdsStationReadModel` / `KdsStationReadModelConfiguration`) con `is_active`, `flow_profile`, `active_in` (jsonb) y `created_at` (esta última ya existe en `kds_stations`; solo falta mapearla en el read model, sin migración). `isOn = is_active && active_in ∋ mode`, calculado aquí con el mismo modo que se devuelve — una sola lectura, sin carrera entre modo y estaciones (ADR-037).
- Devuelve **todas** las estaciones, encendidas o no, ordenadas por `created_at`: el KDS necesita saber que la suya está «Cerrada en Night» para decirlo en pantalla, no para desaparecer.
- `Cache-Control: no-store`, como la anónima.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| — | 401 | Token ausente, inválido o sin `tenantId` |
| `VENUE_MODE_UNAVAILABLE` | 503 | Redis y Postgres inaccesibles |

**Observabilidad**
- Sin log por petición; misma métrica `venue.mode_reads`

**Casos de Aceptación**
- [ ] *(PR3)* Un KDS con token de dispositivo recibe el modo y ve su estación con el `isOn` correcto
- [ ] *(PR3)* Estación con `activeIn: ["RESTAURANT"]` en modo `NIGHT` → `isOn: false`; con `isActive: false` → `isOn: false` en cualquier modo
- [ ] Un token de otro tenant nunca ve estas estaciones (el tenant sale de las claims)
- [ ] Sin token → 401
- [ ] Con Redis caído en NIGHT → `mode: NIGHT` y `isOn` de las barras correcto

---

### GET /api/v1/venue/preflight

**Objetivo**
Dice si el local está vacío y, si no, qué lo bloquea: es lo que el diálogo del panel enseña antes de habilitar «Cambiar».

**Auth**
Staff JWT **Admin** (`StaffAdmin`)

**Request**
- Path params / Query params / Body: Ninguno

**Response 200**
```json
{
  "ready": false,
  "openSessions": 1,
  "activeItems": 2,
  "openTrips": 0,
  "orphanItems": 0,
  "tables": ["T1"]
}
```

**Validaciones**
- Ninguna

**Reglas de Negocio**
- Cuatro recuentos (tres bloquean y uno es solo aviso) sobre tablas de otros módulos, leídas por proyecciones `ExcludeFromMigrations` en Identity (nuevos `SessionReadModel`, `OrderReadModel` (id, session_id, tenant_id; `ExcludeFromMigrations`) y `OrderItemReadModel`, más el `DeliveryTripReadModel` que ya existe):

  | Campo | Consulta | Por qué bloquea |
  |---|---|---|
  | `openSessions` | `sessions WHERE tenant_id = @t AND status = 'OPEN'` | Hay gente sentada con la carta de un modo que va a dejar de existir |
  | `activeItems` | `order_items i JOIN orders o ON o.id = i.order_id JOIN sessions s ON s.id = o.session_id WHERE i.tenant_id = @t AND s.status = 'OPEN' AND i.status NOT IN ('DELIVERED','CANCELLED')` | Hay platos vivos de mesas abiertas. Con esta definición, `activeItems > 0` implica `openSessions > 0` |
  | `openTrips` | `delivery_trips WHERE tenant_id = @t AND status = 'OPEN'` | Hay bandejas en ruta |
  | `orphanItems` | la misma consulta con `s.status <> 'OPEN'` (o sin sesión) | **No bloquea** (no entra en `ready`): lo `HELD`/`QUEUED` de una mesa cerrada no puede avanzar (march y start-preparation devuelven `SESSION_CLOSED`) y lo `IN_PREPARATION`/`READY` sigue en su tablero o pase y se termina aunque la estación esté apagada (ADR-037). El panel lo muestra como aviso: «N platos de mesas ya cerradas; no bloquean» |

- `tables` = códigos de mesa (`sessions.table_code`) de las sesiones abiertas, sin repetir y ordenados: es la lista que el administrador lee para ir a cerrarlas en caja.
- `ready = openSessions == 0 && activeItems == 0 && openTrips == 0`. La misma función la ejecuta `POST /venue/mode` dentro de su transacción; este endpoint es solo la vista previa y **no bloquea nada**.
- Es una foto: entre el pre-flight y el `POST` puede abrirse una mesa; por eso el `POST` lo repite y puede devolver `409` aunque el diálogo dijera «listo».

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| — | 401 / 403 | Sin token o sin rol `Admin` |

**Observabilidad**
- `venue_mode.preflight {TenantId} {Ready} {OpenSessions} {ActiveItems} {OpenTrips} {OrphanItems}` (información)

**Casos de Aceptación**
- [ ] Local vacío → `ready: true`, los tres contadores a cero y `tables: []`
- [ ] Una sesión abierta en T1 con un pedido en cola → `ready: false`, `openSessions: 1`, `activeItems: 1`, `tables: ["T1"]`
- [ ] Una bandeja reclamada y nada más → `ready: false`, `openTrips: 1`
- [ ] Sesión cerrada con un plato HELD (pedido «luego» nunca marchado) y nada más → `ready: true`, `activeItems: 0`, `orphanItems: 1`, `tables: []`, y POST /venue/mode → 200
- [ ] Sesión cerrada con un plato READY → `ready: true`, `orphanItems: 1`
- [ ] Rol `Cajero` → 403

---

### POST /api/v1/venue/mode

**Objetivo**
Cambia el modo de todo el local —solo si está vacío—, deja rastro de quién y cuándo, y avisa a todas las pantallas.

**Auth**
Staff JWT **Admin** (`StaffAdmin`)

**Request**
```json
{ "mode": "NIGHT" }
```

**Response 200**
```json
{
  "mode": "NIGHT",
  "previous": "RESTAURANT",
  "changed": true,
  "changedAt": "2026-10-10T22:00:04Z"
}
```
Si el local **ya estaba** en ese modo: `200` con `changed: false`, `previous = mode` y el `changedAt` del último cambio real. Idempotente a propósito: el panel reintenta tras un corte de red y dos administradores pueden pulsar a la vez.

**Validaciones**
- `mode` llega como **string**. El DTO de request no se tipa como `VenueMode` (no hay `JsonStringEnumConverter` y `[ApiController]` devolvería su ValidationProblemDetails automático). Se valida **en `ChangeVenueModeHandler`**, como `INVALID_ROLE`/`INVALID_ACCESS_MODE` en StaffManagementCommands, comparando contra `Enum.GetNames<VenueMode>()` con `StringComparison.OrdinalIgnoreCase` (no `Enum.TryParse` a secas, que acepta "1" o "5"). Si falla: `Error.Validation("INVALID_MODE", …)`. **No** se registra un validador FluentValidation para este comando: `ValidationBehavior` respondería antes con `VALIDATION_ERROR` y taparía `INVALID_MODE`.

**Reglas de Negocio**
Secuencia de `ChangeVenueModeHandler`, en este orden:
1. **Idempotencia.** Abre una transacción y carga el tenant **con bloqueo de fila** (`SELECT … FOR UPDATE`). Si `tenants.venue_mode == mode` → confirma la transacción vacía, hace `SET venue:{tenantId}:mode <tenants.venue_mode> EX 600` (best-effort, sin evento ni auditoría; así reintentar el POST repara una clave vieja) y devuelve `200 { changed: false }`. Sin fila de auditoría, sin evento.
2. **Pre-flight** (la misma función de `GET /venue/preflight`), **dentro de la misma transacción**, para que dos cambios simultáneos se serialicen por el bloqueo del paso 1. Si no está vacío → revierte y devuelve `409 VENUE_NOT_IDLE` con los contadores y las mesas en `extensions`. **No hay `force`** (decisión cerrada): el administrador cierra las mesas en caja y vuelve a intentarlo; los platos de mesas ya cerradas (`orphanItems`) no bloquean.
3. **Transacción de escritura.** `UPDATE tenants SET venue_mode = @mode, venue_mode_changed_at = @now, updated_at = @now` + `INSERT venue_mode_changes (from_mode, to_mode, changed_by, changed_at, preflight)`. `COMMIT`.
4. **Redis.** `SET venue:{tenantId}:mode @mode EX 600` (incondicional). Si falla, se registra `venue_mode.redis_set_failed` (error) y **se sigue**: la próxima lectura con *miss* re-siembra desde Postgres, que ya tiene el valor nuevo. Lo único peligroso es una clave **vieja** que no se pudo pisar, por eso un `SET` fallido se remata con un `DEL` (también *best-effort*). La garantía ante un fallo es el TTL: si Redis no responde, la clave vieja caduca en ≤ 10 min.
5. **Notificación.** `IPublisher.Publish(new VenueModeChangedNotification(tenantId, mode, previous, changedAt, adminId))` en `try/catch`: Orders difunde `VENUE_MODE_CHANGED`, Catalog invalida `catalog:{t}`. Un fallo aquí no revierte nada.
6. **Auditoría y log.** `audit_log` (`venue_mode_changed`, actor = admin, `{ from, to, preflight }`) y `venue_mode.changed` estructurado. La respuesta `200` sale después de este paso.

Qué **no** hace: no cierra sesiones, no cancela platos, no toca estaciones ni distribución (cada módulo lee el modo nuevo cuando le toca: Catalog al servir, Sessions al abrir, Orders al crear). No notifica al menú del comensal (sondea). La cuenta atrás de 5 s es del panel, no del servidor.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_MODE` | 400 | `mode` ausente o fuera de `RESTAURANT` \| `NIGHT` |
| `VENUE_NOT_IDLE` | 409 | Hay sesiones abiertas, platos vivos o bandejas en ruta. `extensions: { "openSessions": 1, "activeItems": 2, "openTrips": 0, "orphanItems": 0, "tables": ["T1"] }`; `detail`: «Hay 1 mesa abierta (T1). Ciérrala en caja antes de cambiar de modo» |
| — | 401 / 403 | Sin token o sin rol `Admin` |

```json
{
  "title": "VENUE_NOT_IDLE",
  "status": 409,
  "detail": "Hay 1 mesa abierta (T1). Ciérrala en caja antes de cambiar de modo.",
  "openSessions": 1, "activeItems": 2, "openTrips": 0, "orphanItems": 0, "tables": ["T1"]
}
```
(`ProblemDetails` con las `extensions` al nivel raíz, como ya hace `ApiController.Problem` con `plan_limit_exceeded`; el error se construye con `ErrorType.Conflict` + `Extensions`.)

**Observabilidad**
- `venue_mode.changed {TenantId} {From} {To} {ChangedBy} {DurationMs}` (información) — solo cuando `changed: true`
- `venue_mode.rejected_not_idle {TenantId} {OpenSessions} {ActiveItems} {OpenTrips} {OrphanItems}` (información)
- `venue_mode.redis_set_failed {TenantId}` (error) · `venue_mode.notify_failed {TenantId} {Handler}` (warning)
- `audit_log`: `venue_mode_changed`
- Métricas: `venue.mode_changes` (counter, tags `from`, `to`), `venue.mode_change_rejected` (counter)

**Casos de Aceptación**
- [ ] Local vacío, `{ "mode": "NIGHT" }` → 200 `changed: true`, `previous: "RESTAURANT"`; `tenants.venue_mode = NIGHT`, una fila en `venue_mode_changes`, clave de Redis actualizada, `VENUE_MODE_CHANGED` recibido por un cliente SignalR del tenant, `catalog:{t}` borrada
- [ ] Repetir la misma petición → 200 `changed: false`, sin fila nueva, sin evento
- [ ] Sesión abierta en T1 con un pedido → 409 `VENUE_NOT_IDLE` con `tables: ["T1"]`; cerrar la sesión y repetir → 200; `GET /venue/history` muestra una fila
- [ ] Dos administradores pulsan a la vez → uno recibe `changed: true` y el otro `changed: false`; una sola fila de auditoría
- [ ] `{ "mode": "FIESTA" }` → 400 `INVALID_MODE`
- [ ] Redis inalcanzable durante el POST (fallan `SET` y `DEL`) → 200; al volver Redis la clave vieja caduca en ≤ 10 min, y repetir el POST con el mismo modo la corrige al instante
- [ ] Rol `Cajero` → 403

---

### GET /api/v1/venue/history?limit=

**Objetivo**
Los últimos cambios de modo del local, para el historial del panel.

**Auth**
Staff JWT **Admin** (`StaffAdmin`)

**Request**
- Query params: `limit` — entero, opcional, 1–100, por defecto 20
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "id": "0b7e1c2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d",
      "fromMode": "RESTAURANT",
      "toMode": "NIGHT",
      "changedBy": "b0f5c675-24bc-43b5-bdd1-d325b3a04140",
      "changedByName": "Nilton López",
      "changedAt": "2026-10-10T22:00:04Z",
      "preflight": { "openSessions": 0, "activeItems": 0, "openTrips": 0, "orphanItems": 0, "tables": [] }
    }
  ],
  "total": 1
}
```

**Validaciones**
- `limit`: entero entre 1 y 100 → 400 fuera de rango

**Reglas de Negocio**
- `venue_mode_changes` del tenant de las claims, `ORDER BY changed_at DESC LIMIT @limit`. `total` es el recuento completo del tenant (para el «ver más» del panel).
- `changedByName` se resuelve contra `users.name` por `changed_by`; si la cuenta ya no existe, `changedByName` **se omite** (`WhenWritingNull`), y `changedBy` se conserva.
- Sin paginación por cursor: un local cambia de modo una o dos veces al día.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| — | 400 | `limit` fuera de 1–100 |
| — | 401 / 403 | Sin token o sin rol `Admin` |

**Observabilidad**
- Sin log por petición

**Casos de Aceptación**
- [ ] Tras un cambio → una fila con `fromMode`, `toMode`, `changedBy`, `changedByName`, `changedAt` y `preflight`
- [ ] `limit=1` con tres cambios → `data` de 1, `total: 3`
- [ ] Un idempotente (`changed: false`) no aparece en el historial
- [ ] Rol `Cajero` → 403

---

### Resolución de la estación del runner en `JwtValidationMiddleware` — la estación se lee de la base

**Problema**: el Staff JWT lleva `stationId` fijo (v2.0) y dura 8 h; reasignar un runner obligaba a
invalidar su token (`auth_all_invalidated:{sub}`) y a que volviera a entrar, con la bandeja a medias.
En Night los runners se mueven entre barras y un runner (en su móvil o en una tablet del local)
cambia de barra sin cerrar sesión (PRD-019, «móvil personal»).

**Diseño** (`MestoBeach.API/Middleware/JwtValidationMiddleware.cs` + puerto `IStaffStationResolver`
en `MestoBeach.Identity.Application/Common/IServices.cs`, junto a `IJwtBlacklistService` e
`IGlobalInvalidationService`; implementación `StaffStationResolver` en Identity.Infrastructure).
**No** se usa `IClaimsTransformation`: solo tocaría `HttpContext.User`, y nadie lee la estación del
principal —el middleware vuelve a parsear el token crudo y copia `stationId` a
`ICurrentUser.AssignedStationId` y `HttpContext.Items["station_id"]`, que es de donde leen
`DeliveryQueueController`, `DeliveryTripsController` y `OrderItemsController.CallerStationId`—.
- Dentro del middleware, en la rama `type == "staff"`, después de blacklist y
  `auth_all_invalidated` y una vez conocido `tenantId` (mover el bloque tras su parseo o leer
  `claims["tenantId"]` dentro de la rama), llama a `IStaffStationResolver.ResolveAsync(userId,
  tenantId)`. Device Tokens (`type = "device"`, conservan su `stationId`), Customer, Platform e
  Impersonation no cambian.
- El resolver lee Redis `staff_station:{tenantId}:{userId}`; *miss* → `SELECT assigned_station_id
  FROM user_memberships WHERE user_id = @u AND tenant_id = @t AND status = 'ACTIVE'` → `SET … EX 60`
  con el uuid o el centinela `-` (sin fila = centinela). Redis caído → consulta directa a Postgres y
  warning.
- Con el resultado se fijan **juntos** `currentUser.AssignedStationId` y `ctx.Items["station_id"]`;
  con el centinela se vacían ambos aunque el token traiga `stationId`.
- El resolver **nunca lanza** al middleware: si también falla Postgres, se conserva el `stationId`
  del token y se registra un error (el catch general del middleware dejaría al usuario autenticado
  sin `tenant_id`).
- `floorId` no cambia de mecanismo: sigue en el JWT y cambiar el sector sigue invalidando el token.
- Coste: una lectura de Redis por petición de staff, del mismo orden que la comprobación de
  `blacklist:{jti}`.

**Efecto en `PATCH /staff/{userId}/assignment`** (`StaffAssignmentCommands`): si cambia **solo**
`assignedStationId`, el handler **ya no** llama a `InvalidateAllAsync`; hace `DEL
staff_station:{tenantId}:{userId}` tras el commit y la estación nueva entra en la siguiente petición
del runner (≤ 60 s en el peor caso, si otra instancia de la API tenía la clave cacheada). Si cambia
`assignedFloorId` (o los roles, por `PATCH /roles`), todo sigue como en v2.0 y además se borra
`staff_station:{tenantId}:{userId}`. La regla «un runner con viaje abierto no cambia de estación»
(`409 RUNNER_HAS_OPEN_TRIP`) se mantiene: es la que evita que la estación nueva deje platos
reclamados sin dueño. `InvalidateAllAsync` (StaffAssignmentCommands) solo se retira, para el caso
«cambia solo la estación», en el mismo PR que introduce la resolución en el middleware (5c); hasta
entonces se mantiene.

**Cambio en la app del runner** (`frontend/apps/staff/src/app/(console)/runner/page.tsx`, dentro de
PR 5c): un usuario sin el rol `Admin` llama a `ordersApi.getDeliveryQueue()` **sin** `stationId` (la
estación la pone el servidor); solo Admin sigue enviando `pickedStation`. La etiqueta de la estación
deja de salir de la copia del login (`getStoredStaff().assignedStationId`) y se refresca con
`identityApi.getStaffProfile()` (`GET /api/v1/identity/auth/me`, que lee
`user_memberships.assigned_station_id`) al cargar la página y cada vez que llega un
`403 WRONG_STATION` o se rechaza un `POST /delivery-trips`; el perfil fresco se guarda con
`setAuth`. El 403 deja de tragarse: se limpia la cola vieja y se recarga el perfil.

**Observabilidad**
- `staff_station.cache_miss {UserId} {TenantId}` (debug) · `staff_station.redis_unavailable {UserId}` (warning, una vez por petición)
- Métrica: `identity.staff_station_lookups` (counter, tag `source = redis | postgres`)

**Casos de Aceptación**
- [ ] Reasignar a un runner de Barra 1 a Barra 2 sin viaje abierto y sin volver a entrar → su siguiente `GET /delivery-queue` (sin `stationId`) devuelve la cola de Barra 2; `POST /delivery-trips` y `/ready` de Barra 2 → 200; `GET /delivery-queue?stationId=<Barra 1>` → 403 `WRONG_STATION`
- [ ] Con un viaje abierto → 409 `RUNNER_HAS_OPEN_TRIP`, como hasta ahora
- [ ] Un token de dispositivo KDS conserva su `stationId` aunque exista una membresía con otra estación
- [ ] Quitar el rol Runner (PATCH /roles) o dar de baja al usuario borra `staff_station:{tenantId}:{userId}`
- [ ] Un usuario con membresías en dos tenants recibe en cada uno su estación (la clave lleva el tenant)
- [ ] Con Redis caído, el runner sigue operando (la estación sale de Postgres) y el log avisa
- [ ] Cambiar el sector de un PDA sigue invalidando su token (comportamiento v2.0)
