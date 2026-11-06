# PRD-010 — Seguridad y Cumplimiento

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
| **ID** | PRD-010 |
| **Versión** | 2.1 |
| **Estado** | Aprobado — v2.1 |
| **Fecha** | Junio 2026 |
| **Apps relacionadas** | Todas |
| **Módulos de API** | Identity, Sessions, todos los módulos (cross-cutting) |
| **Depende de** | PRD-001 (modelo de negocio), PRD-007 (arquitectura técnica), PRD-011 (modos de operación y PIN) |
| **PRDs que dependen de este** | Todos |

---

## 1. Propósito y Objetivos

### Propósito

Definir los requisitos de seguridad y cumplimiento normativo de Mesto. La plataforma maneja datos de transacciones financieras, datos personales de clientes y credenciales de staff de múltiples restaurantes, lo cual exige un modelo de seguridad sólido desde el diseño.

### Objetivos

| # | Objetivo | Métrica |
|---|---|---|
| OBJ-01 | Ningún tenant puede acceder a datos de otro tenant | 0 incidentes de cross-tenant data leak |
| OBJ-02 | Los datos personales se manejan conforme a la Ley 1581 de 2012 (Colombia) | Auditoría legal aprobada antes del lanzamiento |
| OBJ-03 | Las contraseñas y secretos nunca se almacenan en texto plano | 100% de credenciales hasheadas o en Key Vault |
| OBJ-04 | El sistema resiste los vectores de ataque del OWASP Top 10 | Pen test externo antes del lanzamiento v1.0 |

---

## 2. Modelo de Multi-Tenancy y Aislamiento

### 2.1 Principio de aislamiento
Cada restaurante es un `tenant` con un `tenantId` único. **Todo** acceso a datos en la base de datos incluye un filtro por `tenantId`. No existe ningún endpoint o consulta que no esté filtrado por `tenantId`.

### 2.2 Cómo se aplica

| Capa | Mecanismo |
|---|---|
| **API** | El `tenantId` se extrae del JWT (claim `tid`) — nunca del body del request |
| **PostgreSQL** | Cada tabla con datos de tenant tiene columna `tenant_id NOT NULL`. Las queries usan siempre `WHERE tenant_id = @tenantId` |
| **MongoDB** | Cada colección de catálogo tiene campo `tenantId` indexado. El Catalog module aplica el filtro automáticamente |
| **Redis** | Todas las keys incluyen `{tenantId}` en el prefijo: `table:{tenantId}:{tableCode}:active` |
| **SignalR** | Los grupos incluyen el `tenantId`: `tenant:{tenantId}:station:{stationId}` |

### 2.3 Defensa en profundidad
- La capa de aplicación aplica el filtro de `tenantId`
- Los tests de integración incluyen casos de "acceso cross-tenant" que deben retornar 403
- Las revisiones de código (PR reviews) incluyen una checklist de seguridad que verifica el filtrado por `tenantId`

---

## 3. Autenticación y Autorización

### 3.1 Staff (JWT)
- JWT firmado con RS256 (clave privada en Azure Key Vault)
- Claims: `sub` (userId), `tid` (tenantId), `role` (cajero | cocinero | mesero | admin | owner)
- Expiración: 8 horas (un turno) — **todos los roles**
- Contraseña hasheada con bcrypt (cost factor 12)
- **Modelo de refresh híbrido** (detalle en PRD-014):
  - Mesero + Admin: refresh token JWT RS256, 7 días, `HttpOnly; SameSite=Strict; Secure` cookie
  - Kitchen / Bar (KDS fijo): Device Token JWT RS256, 30 días rolling, `localStorage` del dispositivo KDS (única excepción documentada a la regla de "solo en memoria")
  - Cajero + PlatformAdmin: sin refresh — re-login al vencer el JWT de 8h
- La revocación de refresh tokens y Device Tokens se gestiona via Redis (ver sección 3.7)

#### 3.1.1 Contraseña temporal y cambio forzado (alta por acceso directo)
- En el alta de personal **modo acceso directo** (PRD-014 §4.1), el sistema genera una contraseña temporal (12 chars, alfabeto sin ambiguos, cumple la política: ≥8 con mayúscula, minúscula y dígito) y deja la cuenta `ACTIVE` con el flag `must_change_password = true`.
- La contraseña temporal se devuelve **una sola vez** en la respuesta del invite (texto plano); se persiste **siempre hasheada** (bcrypt cost 12), nunca en claro, y no se reenvía.
- Mientras `must_change_password = true`:
  - El login devuelve `mustChangePassword = true` y **no emite Refresh Token** (sesión de corta duración hasta el cambio).
  - El frontend **bloquea** el acceso a la app y redirige obligatoriamente a `/change-password`. El enforcement es client-side en el prototipo.
  - **Endurecimiento futuro (post-MVP):** un middleware backend que rechace toda llamada autenticada salvo `POST /auth/change-password` mientras el flag esté activo, para que el bloqueo no dependa sólo del cliente.
- `POST /auth/change-password` exige la contraseña actual, que la nueva sea distinta y que cumpla la política; al éxito limpia el flag.

### 3.2 Clientes (Session Token)
- JWT distinto firmado con clave simétrica (también en Key Vault)
- Claims: `sid` (sessionId), `tid` (tenantId), `pid` (participantId), `tbl` (tableCode)
- Expiración: 4 horas (configurable, se renueva si hay actividad)
- Al cerrar la sesión, el `sessionId` va a la Redis blacklist — todos los tokens de esa sesión quedan invalidados en O(1)
- Estructura Redis: `blacklist:{tenantId}` (SET), `SADD blacklist:{tenantId} {sessionId}`, TTL del SET = 24 horas

### 3.3 Control de acceso por rol

| Recurso | Owner | Admin | Cajero | Cocina/Barra | Mesero | Cliente |
|---|---|---|---|---|---|---|
| Admin Panel | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Gestión menú | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Vista cajero | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| KDS cocina/barra | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| App mesero | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Hacer pedidos | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Ver analytics | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Crear sesiones (mesas asignadas) | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Crear sesiones (cualquier mesa) | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Ver PIN activo de sus mesas | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Ver PIN activo de cualquier mesa | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Regenerar PIN** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Cerrar sesiones | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Generar invite link | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ (participante) |

> El rol **Mesero no puede regenerar el PIN**. Cualquier intento retorna `403 Forbidden`. Esta restricción es intencionada — solo el cajero tiene autoridad para revocar el acceso.

### 3.4 PIN de sesión — Seguridad [v3.0]

El PIN es un número de 4 dígitos generado criptográficamente al crear la sesión.

**Generación:**
- Se genera con `SecureRandom` — no es predecible
- Se almacena en Redis (nunca en la base de datos relacional): `session:{tenantId}:{sessionId}:pin`
- TTL del key = duración máxima de la sesión (por defecto 8 horas)
- Al cerrar la sesión, el key se elimina explícitamente: `DEL session:{tid}:{sid}:pin`

**Validación (brute force protection):**

| Contador | Almacenamiento | TTL |
|---|---|---|
| `pin_attempts:{tenantId}:{tableCode}:{deviceId}` | Redis | 10 minutos |

- El `deviceId` es un fingerprint generado en el frontend al primer acceso (almacenado en `sessionStorage`)
- Máximo 5 intentos fallidos por dispositivo dentro de cualquier ventana de 10 minutos
- Al superar el límite: `429 Too Many Requests` con `Retry-After: 600`
- El contador se reinicia en cero si el PIN es correcto
- El cajero puede forzar el desbloqueo de un dispositivo desde el panel (borra el key de Redis)

**Cuándo se valida el PIN:**
- Solo al confirmar el **primer pedido** del `participantId` en la sesión actual
- El campo `pin?: string` se incluye en el body de `POST /orders`
- Si `pin_required = false` en el tenant, el campo se ignora — no se valida ni se almacena
- Una vez validado, se crea el key `pin_validated:{tenantId}:{sessionId}:{participantId}` en Redis con el mismo TTL de la sesión
- Los pedidos siguientes del mismo participante no incluyen ni validan el PIN

### 3.5 Invite Link — Seguridad [v3.0]

El invite link permite unirse a una sesión sin teclear el PIN. El token es un JWT firmado RS256.

**Estructura del token:**
```json
{
  "iss": "mesto",
  "sub": "invite",
  "sid": "{sessionId}",
  "tid": "{tenantId}",
  "tbl": "{tableCode}",
  "exp": {unix_timestamp_cierre_sesión}
}
```

**Propiedades de seguridad:**
- Firmado con la clave privada RS256 almacenada en Azure Key Vault (la misma que firma JWTs de staff)
- El PIN **no** está incluido en el token — el token reemplaza la necesidad del PIN para ese participante
- Reutilizable por múltiples personas (no hay límite de usos — configurable en v2)
- Un solo token activo por sesión, almacenado en Redis: `invite:{tenantId}:{sessionId}:token`
- Expira cuando la sesión se cierra (TTL de Redis = duración de la sesión)

**Revocación:**
- Al regenerar el PIN: `DEL invite:{tenantId}:{sessionId}:token` — el siguiente intent de usar el link retorna `410 Gone`
- Al cerrar la sesión: mismo delete
- El cliente que usa un invite link inválido recibe el mensaje: "Esta invitación ya no es válida. Pide una nueva al grupo."

**Generación:**
- Solo participantes ya dentro de la sesión pueden generar el invite link (`POST /sessions/{id}/invite-link`)
- La llamada sobrescribe el token existente si lo había (siempre hay un solo link activo)

### 3.6 QR Dinámico — Seguridad [v3.0]

El QR dinámico es un token JWT firmado que contiene los datos de la sesión.

**Estructura del token:**
```json
{
  "iss": "mesto",
  "sub": "qr",
  "sid": "{sessionId}",
  "tid": "{tenantId}",
  "tbl": "{tableCode}",
  "exp": {unix_timestamp_cierre_sesión}
}
```

**Propiedades de seguridad:**
- Firmado RS256 — no se puede falsificar ni adivinar
- Almacenado en Redis: `qr:{tenantId}:{sessionId}:token` con TTL = duración de la sesión
- Al cerrar la sesión: `DEL qr:{tid}:{sid}:token`
- Si alguien intenta usar el token después del cierre: `410 Gone` con mensaje "Este QR ya no es válido"
- Una foto del QR de una sesión anterior es completamente inútil

**Generación:**
- Se genera automáticamente al crear la sesión en modo DYNAMIC (`POST /sessions`)
- El cajero puede regenerarlo si es necesario (mismo endpoint que genera el token de nuevo)
- El URL del QR es: `mesto.app/join?q={TOKEN_FIRMADO}` — el token no es legible sin la clave pública

### 3.7 Revocación de Refresh Tokens y Device Tokens [v3.0]

**Refresh Token (Mesero / Admin):**
- Almacenado como JWT en `HttpOnly cookie`. La revocación borra la cookie en el browser.
- Para logout global: `SADD auth_all_invalidated {userId}` con timestamp → el middleware rechaza JWTs emitidos antes del timestamp
- TTL del SET en Redis: 8 días (cubre el período máximo del refresh token)

**Device Token (KDS fijo):**
- Almacenado en Redis: `device:{tenantId}:{deviceId}` → `{ userId, role, lastUsed }` con TTL 30 días
- Revocación desde Admin Panel: `DELETE /identity/devices/{deviceId}` → `DEL device:{tid}:{deviceId}`
- Al revocar, el dispositivo pierde su autenticación al siguiente request y vuelve a `/login`
- El Admin Panel muestra la fecha del último uso de cada dispositivo para detectar dispositivos abandonados

**Activation Token y Reset Token (tokens opacos):**
- Almacenados en Redis con TTL explícito (48h y 1h respectivamente)
- Son de un solo uso — se borran inmediatamente al usarse
- No necesitan blacklist — la ausencia del key en Redis equivale a inválido

---

## 4. Seguridad en Tránsito y en Reposo

### 4.1 En tránsito
- Toda la comunicación API usa HTTPS (TLS 1.2 mínimo)
- SignalR usa WSS (WebSocket Secure)
- Los certificados TLS se renuevan automáticamente (Azure App Service managed certificates)
- HSTS activado en producción

### 4.2 En reposo
- Los datos en PostgreSQL y MongoDB se encriptan con encryption-at-rest del proveedor (Azure)
- Las imágenes en Azure Blob Storage usan encriptación gestionada por Azure
- Los secretos (connection strings, API keys, VAPID keys) se almacenan exclusivamente en Azure Key Vault
- **Nunca** en appsettings.json, variables de entorno del repo, o código fuente

---

## 5. Protección contra Ataques Comunes (OWASP Top 10)

| Ataque | Mitigación |
|---|---|
| SQL Injection | EF Core con queries parametrizadas; sin SQL raw |
| XSS | Next.js escapa por defecto; CSP headers en todas las respuestas |
| CSRF | SameSite=Strict en cookies de sesión; tokens CSRF en forms |
| Broken Access Control | Filtro de tenantId en todas las queries; tests de regresión de autorización |
| Broken Auth | bcrypt para contraseñas; JWT con expiración corta; blacklist de sesiones |
| Security Misconfiguration | Azure Key Vault para secretos; HTTPS obligatorio; sin puertos expuestos innecesariamente |
| Rate limiting / DoS | Rate limiting por IP y por tenant en endpoints críticos (ver sección 6) |
| SSRF | Lista blanca de URLs para webhooks de pago; no se hacen requests a URLs proporcionadas por usuarios |

---

## 6. Rate Limiting

| Endpoint | Límite | Alcance |
|---|---|---|
| `POST /sessions` (mesero/cajero crea sesión) | 10 / minuto | Por mesa (`tenantId + tableCode`) |
| `POST /sessions/join` (cliente entra via QR fijo) | 5 / minuto | Por mesa (`tenantId + tableCode`) |
| `GET /sessions/join?q={token}` (QR dinámico) | 10 / minuto | Por IP |
| `GET /sessions/join?i={token}` (invite link) | 20 / minuto | Por IP |
| `POST /orders` (con validación de PIN) | 5 intentos PIN fallidos | Por `deviceId` por sesión |
| `POST /orders` (pedidos normales) | 20 / minuto | Por sesión (`sessionId`) |
| `POST /sessions/{id}/regenerate-pin` | 10 / hora | Por sesión |
| `POST /sessions/{id}/invite-link` | 30 / hora | Por sesión |
| `POST /auth/login` | 10 / 15 minutos | Por IP |
| `POST /auth/change-password` | 5 / 15 minutos | Por usuario (`sub`) |
| `POST /auth/register` | 3 / hora | Por IP |
| Todos los demás | 100 / minuto | Por tenant |

Implementación: middleware de .NET con contador en Redis. Al superar el límite, responde `429 Too Many Requests` con header `Retry-After`.

---

## 7. Cumplimiento — Ley 1581 de 2012 (Colombia)

### 7.1 Datos personales recolectados

| Actor | Datos recolectados | Finalidad |
|---|---|---|
| Cliente | Nombre (opcional), historial de pedidos (si tiene cuenta) | Identificación en la mesa, historial personal |
| Staff | Nombre, email, rol | Autenticación, logs de auditoría |
| Dueño | Nombre, email, NIT (futuro), datos del restaurante | Facturación, comunicación |

### 7.2 Derechos del titular de datos
- **Acceso:** cualquier persona puede solicitar qué datos tiene Mesto sobre ella
- **Corrección:** los datos incorrectos se corrigen a solicitud
- **Eliminación:** al cancelar la cuenta, los datos se eliminan después de 90 días
- **Portabilidad:** los datos se pueden exportar en formato CSV/JSON desde el Admin Panel

### 7.3 Política de privacidad
- Visible en todo momento en el footer de la app del cliente y del Admin Panel
- El cliente debe aceptarla al crear una cuenta voluntaria (los pedidos anónimos no requieren aceptación)
- La versión de la política aceptada por cada usuario queda registrada

---

## 8. Logs de Auditoría

Todos los eventos sensibles se registran con: `timestamp`, `tenantId`, `userId`, `action`, `resourceType`, `resourceId`, `ipAddress`.

| Evento auditado |
|---|
| Login exitoso / fallido |
| Cambio de contraseña |
| Creación / modificación / eliminación de usuarios de staff |
| Apertura / cierre de sesiones de mesa |
| **Regeneración de PIN** (quién, cuándo, qué mesa) |
| **Generación de invite link** (quién, cuándo, qué sesión) |
| **Generación de QR dinámico** (quién, cuándo, qué sesión) |
| **Intento fallido de PIN** (deviceId anonimizado, mesa) |
| **Desbloqueo manual de PIN por cajero** |
| Cancelación de pedidos |
| Cambio de modo de operación del restaurante |
| Cambio de qrMode o pinRequired del restaurante |
| Asignación / desasignación de mesas a meseros |
| Suspensión / cancelación de cuenta (Mesto Ops) |
| Acceso a datos de exportación |

Los logs se almacenan en PostgreSQL (tabla `audit_log`) y se retienen por 12 meses. No se pueden eliminar desde el Admin Panel.

---

## 9. Respuesta a Incidentes

| Severidad | Definición | SLA de respuesta |
|---|---|---|
| P1 — Crítico | Acceso cross-tenant, robo de credenciales, datos expuestos públicamente | < 1 hora |
| P2 — Alto | Vulnerabilidad explotada pero sin datos expuestos, downtime de producción | < 4 horas |
| P3 — Medio | Vulnerabilidad detectada no explotada, degradación de servicio | < 24 horas |

En caso de brecha de datos que afecte datos personales, se notifica a la Superintendencia de Industria y Comercio (SIC) dentro de las 72 horas requeridas por la Ley 1581.

---

## 10. Historias de Usuario

```
US-1001
Como dueño de restaurante,
quiero saber que los datos de mis clientes y pedidos están seguros y separados de otros restaurantes,
para cumplir con mis obligaciones de confidencialidad y proteger mi negocio.

Criterio de aceptación:
- El sistema garantiza que ningún restaurante puede acceder a datos de otro
- Hay una política de privacidad accesible desde cualquier parte del sistema
- Puedo exportar todos mis datos en cualquier momento

US-1002
Como cliente que pidió con una cuenta registrada,
quiero poder solicitar la eliminación de mis datos personales,
para ejercer mi derecho al olvido.

Criterio de aceptación:
- En la configuración de mi cuenta hay una opción "Eliminar mi cuenta"
- Al confirmar, mis datos personales se anonimizar en 30 días (el historial de pedidos queda sin nombre)
- Recibo un email de confirmación con el detalle de lo que será eliminado
```

---

## 11. Criterios de Aceptación

- [ ] Un usuario con JWT de tenant A no puede obtener datos de tenant B en ningún endpoint
- [ ] Las contraseñas se almacenan hasheadas con bcrypt (verificable en la base de datos)
- [ ] Los secretos no están en ningún archivo del repositorio (verificado con `git secrets` en CI)
- [ ] Al cerrar una sesión de mesa, los tokens de los participantes son rechazados inmediatamente
- [ ] El mesero recibe `403 Forbidden` al intentar regenerar el PIN
- [ ] Después de 5 intentos fallidos de PIN, el dispositivo recibe `429 Too Many Requests` con `Retry-After: 600`
- [ ] Un invite link invalidado (por regeneración de PIN o cierre de sesión) retorna `410 Gone`
- [ ] Un QR dinámico de sesión cerrada retorna `410 Gone`
- [ ] Los eventos de regeneración de PIN y generación de invite link quedan en el log de auditoría
- [ ] Existe una política de privacidad accesible y aceptada por los usuarios con cuenta
- [ ] Los logs de auditoría registran todos los eventos sensibles correctamente

---

*PRD-010 — Seguridad y Cumplimiento*
*Versión 2.1 — Junio 2026 — §3.1.1 contraseña temporal y cambio forzado (alta por acceso directo, PRD-014 §4.1); rate limiting de `/auth/change-password`. Base v2.0: PIN brute force, invite link JWT, QR dinámico, tabla de roles actualizada, rate limiting v3, modelo híbrido de refresh tokens, Device Token revocación*
