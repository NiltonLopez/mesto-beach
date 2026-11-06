# SDD-003 — Sessions

> **v2.1 — Octubre 2026 — Modo Night: distribución por modo, PIN de noche y hub cerrado.**
> *(diseñado — pendiente de implementación, PRD-019)*. El local pasa a tener un **modo**
> (`VenueMode = RESTAURANT | NIGHT`; fuente de verdad en Identity —SDD-001 §Venue— y leído aquí por
> `IVenueModeProvider`). Cada mesa gana **una posición por modo** (`table_layouts`, con `active` por modo y
> la barra `station_id` que la sirve de noche), los pisos declaran en qué modos se ven (`floors.visible_in`),
> las mesas tienen `kind` (`TABLE | HIGH_TABLE | RESERVADO`) y la sesión congela el modo en que se abrió
> (`sessions.venue_mode`). El PIN pasa a ser **único entre sesiones abiertas** (`pins_in_use:{tenantId}`, hash PIN → `sessionId`),
> dura **12 h**, Postgres manda sobre Redis y el enlace de invitación **no exime del PIN en Night**.
> `GET /floorplan` acepta `?mode&includeHidden&includeInactive`, `PATCH /tables/layout` escribe por modo, abrir o unirse
> a una mesa cerrada en el modo activo devuelve `409 TABLE_CLOSED_IN_MODE`, `SessionsHub` pasa a
> `[Authorize]` y se corrige la URL del QR. En el mismo pase se alinean con el código tres deudas
> *(actualizado)*: `sessions.guest_count`, la precedencia real de los avisos (`PIN 3 · BILL 2 · WAITER 1 ·
> ETA 0`) y que `BILL` **sí** persiste en Redis.

## Servicio
**Módulo**: Sessions  
**Base path**: `/api/v1`  
**Responsabilidad**: Gestión del ciclo de vida de sesiones de mesa (apertura, unión de participantes, PIN, modos QR/servicio) y configuración de mesas.

**PRDs de referencia**: PRD-002 (Flujo cliente), PRD-003 (KDS/Staff), PRD-011 (Modos de operación), **PRD-019** (Mesto Beach Night — distribución por modo, PIN de noche)

**Dependencias**:
- PostgreSQL: `tables`, `floors`, `sessions`, `session_participants`; **`table_layouts`** *(diseñado — pendiente de implementación, PRD-019)*
- Redis: `session:*`, `pin_attempts:*`, `pin_validated:*`, `table:*`, `invite:*`, `qr:*`, `ratelimit:sessions:*`, `waiter_call:*`; **`pins_in_use:*`** *(diseñado — PRD-019)*
- SDD-001 Identity: JWT validation; **`IVenueModeProvider`** (modo activo del local: `tenants.venue_mode` + `venue:{tenantId}:mode`) *(diseñado — PRD-019)*
- SignalR: `SessionsHub` propio del módulo, montado en `/hubs/sessions`; difunde al grupo `waiter-{tenantId}` (ver §Avisos de mesa)
- SDD-007 Notifications: SignalR broadcast on PIN regeneration

---

## PostgreSQL Schema

**`tables`**
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
tenant_id       UUID NOT NULL REFERENCES tenants(id),
table_code      VARCHAR(50) NOT NULL,
zone            VARCHAR(100),
active          BOOLEAN NOT NULL DEFAULT true,
-- [v1.x] Map layout (nullable: ad-hoc tables tienen placement = null)
-- *(diseñado — PRD-019)* floor_id/pos_x/pos_y quedan DEPRECADAS: la posición vive en table_layouts (una
-- fila por modo). AddTableLayoutsPerModeAndKind las copia. Sessions deja de leerlas y de escribirlas; Orders
-- (TableLayoutReadModel) pasa a leer table_layouts del modo activo (SDD-004). DropLegacyTablePosition las
-- borrará cuando ningún módulo las mapee.
floor_id        UUID REFERENCES floors(id) ON DELETE SET NULL,
pos_x           DOUBLE PRECISION,
pos_y           DOUBLE PRECISION,
seats           INTEGER,
shape           VARCHAR(16),   -- 'SQUARE' | 'ROUND' (default SQUARE)
-- *(diseñado — PRD-019)* Clase de puesto: Orders la copia a orders.priority (RESERVADO primero, solo en NIGHT) y el
-- Salón/Runner la rotulan. Migración AddTableLayoutsPerModeAndKind. 'SUNBED' queda reservado a §Sectores.
kind            VARCHAR(16) NOT NULL DEFAULT 'TABLE',   -- 'TABLE' | 'HIGH_TABLE' | 'RESERVADO'
-- [v2.x] Tipo de QR/acceso por mesa (migración AddTableQrConfig)
qr_mode         VARCHAR(16) NOT NULL DEFAULT 'FIXED',   -- 'FIXED' | 'DYNAMIC'
requires_pin    BOOLEAN NOT NULL DEFAULT true,          -- forzado false si qr_mode = DYNAMIC
created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
UNIQUE (tenant_id, table_code)
```

> **Tipo de QR por mesa** [v2.x]: el administrador elige en `admin → Mesas` uno de tres presets que mapean a `(qr_mode, requires_pin)`: **QR Fijo (con PIN)** → `(FIXED, true)`; **QR Dinámico** → `(DYNAMIC, false)`; **Sin seguridad** → `(FIXED, false)` (acceso abierto, sin PIN). `DiningTable.SetQrConfig(qrMode, requiresPin)` fuerza `requires_pin = false` cuando `qr_mode = DYNAMIC`. Estas columnas, **no la config del tenant**, determinan el acceso de la sesión (ver `POST /api/v1/sessions`).

**`floors`** [v1.x] — un nivel del local con su layout (mapa de mesas)
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
tenant_id       UUID NOT NULL,
name            VARCHAR(100) NOT NULL,   -- "Planta baja", "Terraza", "Piso 2"
sort_order      INTEGER NOT NULL,
width           INTEGER NOT NULL,        -- dimensiones del rectángulo (px de layout)
height          INTEGER NOT NULL,
walls           JSONB NOT NULL,          -- [{x1,y1,x2,y2}, ...] segmentos de muro
-- *(diseñado — PRD-019)* Modos en los que el piso existe para el personal y el cliente.
-- '["RESTAURANT"]' = la Playa cierra de noche. Migración AddTableLayoutsPerModeAndKind.
visible_in      JSONB NOT NULL DEFAULT '["RESTAURANT","NIGHT"]',
created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
```
> Coordenadas (`pos_x/pos_y` de mesas y `walls`) viven en el espacio del piso `0..width / 0..height`; el front escala para ajustar. Son datos de **layout puro**: no afectan sesiones ni pedidos. Al borrar un piso, sus mesas quedan con `floor_id = null` ("sin ubicar"), no se borran.

**`table_layouts`** *(diseñado — pendiente de implementación, PRD-019 — ADR-038)* — la posición de una mesa **por modo**
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
tenant_id       UUID NOT NULL,
table_id        UUID NOT NULL REFERENCES tables(id) ON DELETE CASCADE,
mode            VARCHAR(16) NOT NULL,                         -- 'RESTAURANT' | 'NIGHT'
floor_id        UUID REFERENCES floors(id) ON DELETE SET NULL, -- null = sin ubicar en ese modo
pos_x           DOUBLE PRECISION,
pos_y           DOUBLE PRECISION,
active          BOOLEAN NOT NULL DEFAULT true,                -- false = la mesa no está en servicio en ese modo
station_id      UUID,                                         -- barra que la sirve (kds_stations; sin FK: la tabla es de Orders). Solo tiene efecto en NIGHT
updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
UNIQUE (table_id, mode)
-- INDEX (tenant_id, mode)
```

> **Qué fila manda.** `GET /floorplan` y Orders leen la fila del **modo activo** (`IVenueModeProvider`).
> `tables.active` sigue siendo el interruptor **global** de la mesa y `table_layouts.active` el **por modo**:
> una mesa está en servicio si ambos son `true`. Una mesa **sin fila** para un modo se trata como
> `active = false` en ese modo (no aparece en el plano ni acepta sesiones). Es solo una defensa ante datos
> inconsistentes, no un caso normal: la migración y todas las altas de mesas (`POST /tables`,
> `POST /tables/batch` y la mesa ad-hoc de `POST /sessions`) crean las dos filas (ver §Cambios en endpoints
> existentes). Una mesa con `floor_id = null` en la fila del modo (sin ubicar o ad-hoc) solo depende de
> `tables.active` y `table_layouts.active`: la condición de piso visible (`floors.visible_in`) no se le
> aplica. La **barra** de cada mesa vive solo en la fila `NIGHT`: Orders la usa para enrutar a esa barra los
> platos `BAR`/sin estación de la mesa
> (ADR-038; SDD-004 §Enrutado). `station_id` no lleva FK porque `kds_stations` la posee Orders; el handler la
> valida contra las estaciones del tenant con perfil `BAR` leyendo la tabla en solo lectura.
>
> **Migraciones** *(diseñado — PRD-019)*:
> - `AddTableLayoutsPerModeAndKind`: crea `table_layouts` (único `(table_id, mode)`, índice `(tenant_id, mode)`),
>   `floors.visible_in` y `tables.kind`. **Copia de datos en la misma migración**: por cada mesa, una fila
>   `RESTAURANT` con sus `floor_id/pos_x/pos_y` actuales y un **clon** `NIGHT` idéntico (`active = true`,
>   `station_id = null`), para que el plano de noche arranque igual que el de día y el admin solo ajuste.
>   `tables.floor_id/pos_x/pos_y` **no se borran aún**: quedan deprecadas; Sessions deja de leerlas y de
>   escribirlas. El único lector externo es Orders (`TableLayoutReadModel`: coordenadas del puesto en la cola
>   del runner y en el manifiesto del viaje), que pasa a leer `table_layouts` del modo activo (SDD-004).
> - `DropLegacyTablePosition` (limpieza posterior: solo cuando ningún módulo mapee esas columnas —con la
>   proyección de Orders ya migrada a `table_layouts` y su snapshot de EF regenerado— y verificado el plano
>   por modo en producción): elimina `tables.floor_id/pos_x/pos_y`.
> - `AddSessionVenueMode`: `sessions.venue_mode VARCHAR(16) NOT NULL DEFAULT 'RESTAURANT'`.

**`sessions`**
```sql
id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
tenant_id       UUID NOT NULL REFERENCES tenants(id),
table_id        UUID NOT NULL REFERENCES tables(id),
table_code      VARCHAR(50) NOT NULL,
status          VARCHAR(20) NOT NULL CHECK (status IN ('OPEN','CLOSED')),
qr_mode         VARCHAR(20) NOT NULL CHECK (qr_mode IN ('FIXED','DYNAMIC')),
service_mode    VARCHAR(20) NOT NULL CHECK (service_mode IN ('STAFFED','SELF_SERVICE')),
pin_required    BOOLEAN NOT NULL,
waiter_id       UUID REFERENCES staff(id),
created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
opened_by       UUID REFERENCES staff(id),
closed_at       TIMESTAMPTZ,
closed_by       UUID REFERENCES staff(id),
shift_id        UUID,
guest_count     INTEGER,                                   -- *(actualizado)* comensales que registra la Host al sentar (1–999; migración AddSessionGuestCount). Es el `pax` de §Sectores
venue_mode      VARCHAR(16) NOT NULL DEFAULT 'RESTAURANT'   -- *(diseñado — PRD-019)* modo del local al abrir: 'RESTAURANT' | 'NIGHT' (migración AddSessionVenueMode)
```

**`session_participants`**
```sql
id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
session_id          UUID NOT NULL REFERENCES sessions(id),
device_id           VARCHAR(100) NOT NULL,
customer_account_id UUID REFERENCES customer_accounts(id),  -- se llena al identificarse con cuenta
display_name        VARCHAR(120),                           -- [v1.x] nombre mostrado al staff/KDS
is_host             BOOLEAN NOT NULL DEFAULT false,
joined_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
pin_validated       BOOLEAN NOT NULL DEFAULT false,
invite_token        VARCHAR,
UNIQUE (session_id, device_id)   -- [fix] un participante por (sesión, dispositivo)
```

> **[fix] Join idempotente y a prueba de concurrencia:** la constraint **`UNIQUE (session_id, device_id)`** + un *get-or-create atómico* en el repositorio (`AddOrGetParticipantAsync`: ante violación `23505` reintenta y devuelve el participante ganador) evitan que el **mismo dispositivo abriendo el invite en varias pestañas a la vez** cree participantes duplicados. Aplica a `join`, `join-by-table` y `participants` (walk-in).

> **Identificación del cliente [v1.x]:** al entrar al menú, el cliente se identifica (pantalla
> nombre / iniciar sesión / crear cuenta — PRD-002). El endpoint
> `POST /api/v1/sessions/{sessionId}/participants/{participantId}/identify` `[AllowAnonymous]`
> con `{displayName}` setea `display_name` y, si el request lleva el **JWT de cliente** (type `customer`),
> vincula `customer_account_id` (tomado de `ICurrentUser.UserId`). La auth de cuentas reside en Identity (SDD-001).

## Redis Keys

| Clave | Tipo | Valor | TTL |
|---|---|---|---|
| `session:{tenantId}:{sessionId}:pin` | String | PIN 4 dígitos | 4 h fijas *(actualizado)* → **12 h** *(diseñado — PRD-019)* |
| `pin_attempts:{tenantId}:{tableCode}:{deviceId}` | String (int) | Contador de intentos fallidos | 15 min |
| `pin_validated:{tenantId}:{sessionId}:{participantId}` | String | `"1"` — **caché** de `session_participants.pin_validated` *(diseñado — PRD-019)* | 4 h (12 h en el join por invitación) *(actualizado)* → **12 h** *(diseñado — PRD-019)* |
| `table:{tenantId}:{tableCode}:active` | String | `sessionId` | Duración de sesión |
| `invite:{tenantId}:{sessionId}:token` | String | JWT RS256 payload serializado | Duración de sesión |
| `qr:{tenantId}:{sessionId}:token` | String | JWT RS256 payload serializado | Duración de sesión |
| `ratelimit:sessions:{tenantId}:{tableCode}` | String (int) | Contador de requests | 1 min |
| `waiter_call:{tenantId}:{sessionId}` | String (JSON) | `WaiterCallInfo` serializado: `{ tenantId, sessionId, tableCode, createdAt, reason }` | 4 h |
| `pins_in_use:{tenantId}` | Hash | campo = PIN, valor = `sessionId` de la sesión **abierta** que lo usa *(diseñado — PRD-019)* | Sin TTL |

> **PIN de noche** *(diseñado — pendiente de implementación, PRD-019 — ADR-040)*
> - **`pins_in_use:{tenantId}`** (Hash PIN → `sessionId`): **reservar** (abrir sesión y regenerar) = sortear
>   un PIN y `HSETNX pins_in_use:{t} {pin} {sessionId}`; si devuelve `1`, queda reservado. Máx. 20
>   reintentos; si se agotan, 500 y log `sessions.pin.pool_exhausted`. **Autorreparación:** si `HSETNX`
>   devuelve `0`, se lee el `sessionId` dueño; si esa sesión ya no está `OPEN` en Postgres, se reclama el PIN
>   con `HSET` y se usa. **Liberar por `sessionId`, nunca leyendo `session:{t}:{s}:pin`** (que caduca): al
>   cerrar, `HGETALL` y `HDEL` de los campos cuyo valor es ese `sessionId` (o un script Lua equivalente); al
>   regenerar, lo mismo con el PIN anterior y después `HSETNX` del nuevo. Así **dos mesas abiertas nunca
>   comparten PIN**. El PIN nunca se guarda en Postgres (PRD-010).
> - **TTL 12 h** en `session:{t}:{s}:pin` y `pin_validated:{t}:{s}:{p}` (hoy 4 h: el PIN se guarda al abrir
>   con `SessionTtl` = 4 h —`SessionCommands.cs:78`, usado en `:251`— y, al regenerarlo con la clave ya
>   caducada, con 4 h fijas en `SessionPinService.cs:55`; `pin_validated` se escribe con 4 h al validar
>   (`PinCommands.cs:123`) y al añadir un participante desde staff (`SessionCommands.cs:543`, usado en
>   `:608`), y con 12 h en el join por invitación (`SessionCommands.cs:415`)). Una noche dura más de 4 h y
>   el PIN caducaba a mitad de fiesta. El PIN pasa a 12 h con una constante propia (p. ej. `PinTtl`),
>   separada de `SessionTtl`. `table:{t}:{c}:active` (`SessionCommands.cs:235`) se queda en 4 h: todas sus
>   lecturas caen a Postgres y, si se alargara, una clave colgada tras un fallo al cerrar bloquearía la
>   reapertura de la mesa (409) durante 12 h.
> - **`pin_validated` deja de ser la única verdad**: la columna `session_participants.pin_validated`
>   **manda**. Si la clave Redis falta, Orders (`PinValidationService`) consulta Postgres y **re-siembra** la
>   clave con TTL fijo de 12 h (Postgres no guarda caducidad; la clave es solo caché y el cierre de la sesión
>   ya lo controla `sessions.status`) en vez de devolver `PIN_REQUIRED`; `GET /sessions/{id}` y
>   `join-by-table` aplican la misma regla. Redis es caché, no estado. Todo lo que hoy marca `pin_validated`
>   solo en Redis debe persistir también la columna: `POST /sessions/{id}/participants`
>   (`AddParticipantHandler`) llama a `MarkPinValidated()` sobre el candidato antes de
>   `AddOrGetParticipantAsync`, como ya hace el join por invitación (`SessionCommands.cs:406`); en la rama
>   idempotente marca y guarda el participante existente si aún no estaba validado; y siembra la clave con
>   12 h.

## Reglas de Negocio Globales

**Modos QR**
- `FIXED`: QR impreso permanentemente en la mesa. El QR nunca cambia. URL *(actualizado)*: hoy el backend codifica `http://localhost:3000/{tenantSlug}?t={tableCode}` con el host **fijo en código** (`TableCommands.cs:321`), una ruta que el menú no sirve. *(diseñado — PRD-019)*: pasa a **`{MENU_PUBLIC_URL}/mesa?tenant={tenantId}&table={tableCode}`**, la ruta real del menú; hay que **reimprimir** los QR tras el cambio (ver `GET /tables/{tableId}/qr`).
- `DYNAMIC`: QR generado por sesión con JWT en la URL. Siempre implica `serviceMode = STAFFED`. La combinación `DYNAMIC + SELF_SERVICE` es inválida → 400.

**PIN**
- PIN de 4 dígitos generado con `SecureRandom` al abrir cada sesión (si `pinRequired = true` para la sesión: según la mesa, o siempre en `NIGHT`).
- El PIN se valida únicamente cuando el participante confirma su primer pedido (no al unirse a la sesión). Salvo en `NIGHT`, donde el menú lo pide en el registro inicial junto al nombre (ADR-040, revisión de ADR-014); el gate de Orders al pedir se mantiene como red de seguridad.
- Tras validación exitosa, el participante queda como "trusted" para el resto de la sesión (`pin_validated = true` en DB y clave Redis).
- Máximo 5 intentos fallidos por dispositivo por mesa en una ventana de 15 minutos → 423 Locked.
- El Cajero puede regenerar el PIN en cualquier momento durante la sesión abierta.
- *(diseñado — PRD-019 — ADR-040)* El PIN es **único entre las sesiones abiertas del tenant** (`pins_in_use:{tenantId}`), dura **12 h** y su validación se guarda en Postgres como verdad (Redis es caché; Orders, `GET /sessions/{id}` y `join-by-table` re-siembran con TTL 12 h). En **`NIGHT`** el PIN es obligatorio en todas las mesas, sea cual sea su tipo de QR (`pinRequired` se fuerza a `true` al abrir la sesión; ver §POST /sessions). Además, el enlace de invitación **no exime del PIN** (`inviteGrantsPin = false`) y el PIN se recuerda **por dispositivo** toda la noche: el menú guarda `pinValidated` en `localStorage mestobeach_visit:{tenantId}:{tableCode}` y lo rehidrata con `GET /sessions/{id}?deviceId=`; otro navegador es otro dispositivo. El nombre obligatorio al pedir de noche (`422 NAME_REQUIRED`) lo impone Orders (SDD-004).

**Modo del local** *(diseñado — pendiente de implementación, PRD-019 — ADR-036)*
- `VenueMode = RESTAURANT | NIGHT` vive en Identity (`tenants.venue_mode`, caché `venue:{tenantId}:mode`, SDD-001 §Venue). Sessions lo lee por `IVenueModeProvider` y **nunca lo escribe**.
- El modo decide qué fila de `table_layouts` manda, qué pisos se ven (`floors.visible_in`) y si una mesa acepta sesiones: abrir (`POST /sessions`) o unirse (`POST /sessions/join-by-table`) a una mesa cuyo piso no incluye el modo activo, o cuya fila del modo tiene `active = false`, devuelve `409 TABLE_CLOSED_IN_MODE`.
- Cada sesión **congela** el modo en que se abrió (`sessions.venue_mode`). El cambio de modo exige local vacío (pre-flight `VENUE_NOT_IDLE`, SDD-001), así que una sesión nunca cambia de modo a medias; Orders sella en `orders.venue_mode` el modo vigente al crear la comanda (`IVenueModeProvider`, SDD-004 §Enrutado por modo paso c), que coincide con el de la sesión porque el cambio exige local vacío, y Analytics separa los informes por él (SDD-006).
- Si el proveedor lanza `VenueModeUnavailableException` (Redis y Postgres caídos), `POST /sessions` y `POST /sessions/join-by-table` responden `503 VENUE_MODE_UNAVAILABLE` (fila en sus tablas de errores).

**Apertura de sesión**
- Modo `STAFFED`: el mesero abre la sesión explícitamente.
- Modo `SELF_SERVICE`: el cliente abre la sesión al escanear el QR fijo.
- Solo puede existir 1 sesión con `status = OPEN` por mesa a la vez. Se verifica con la clave Redis `table:{tenantId}:{tableCode}:active`.

**Invite Link**
- Generado por el mesero para una sesión ya abierta.
- JWT RS256: `{ sessionId, tenantId, type: "invite", exp: <timestamp cierre de sesión> }`.
- Sin límite de participantes.

---

## Avisos de mesa: motivos, precedencia y eventos SignalR *(v2.0)*

Todo lo que el comensal "pide" desde el móvil sin pasar por cocina —atención, el PIN, la cuenta o
saber cuándo sale su plato— entra por el mismo carril: un **aviso de mesa**. Hay **un solo aviso
pendiente por sesión**, guardado en `waiter_call:{tenantId}:{sessionId}` con TTL de 4 horas
(`WaiterCallService`), y se cierra con `POST /sessions/{sessionId}/call-waiter/acknowledge`, que
borra la clave.

### Los cuatro motivos

| Motivo | Lo dispara | Endpoint | Evento | ¿Persiste en Redis? | Quién lo ve hoy |
|---|---|---|---|---|---|
| `WAITER` | El comensal pulsa "llamar al camarero" | `POST /sessions/{id}/call-waiter` | `WAITER_CALLED` | Sí | Salón (`/salon`) y Caja (`/cashdesk`) |
| `PIN` | El comensal necesita el PIN de la mesa para poder pedir | `POST /sessions/{id}/request-pin` | `PIN_REQUESTED` | Sí | Salón y PDA; en Night también el Runner |
| `BILL` | El comensal pide la cuenta | `POST /sessions/{id}/request-bill` | `BILL_REQUESTED` | **Sí** *(actualizado)* | Salón y PDA (`AvisosPanel`); la caja ya no atiende avisos, solo rehidrata el mapa con los eventos de ciclo |
| `ETA` | El comensal pregunta "¿cuándo sale?" por su pedido | `POST /sessions/{id}/ask-eta` | `ETA_REQUESTED` | Sí | Salón |

> **`BILL` ya no es la excepción** *(actualizado)*: `CreateBillRequestHandler` **sí** pasa por
> `IWaiterCallService.CreateOrGetAsync(..., "BILL")` (`WaiterCallCommands.cs:133`), igual que los otros
> tres motivos. Una petición de cuenta **persiste** en `waiter_call:{tenantId}:{sessionId}`, **aparece** en
> `GET /sessions/calls`, se cierra con `acknowledge` y **entra en la precedencia** (asciende un `WAITER`,
> es ascendida por un `PIN`). La versión anterior de este documento la describía como alerta efímera de
> caja; hoy la caja **no atiende avisos** (`cashdesk/page.tsx:254`): viven en el PDA y en el Salón, que son
> quienes están en sala.

### Precedencia (un ETA no pisa un PIN) *(actualizado)*

`WaiterCallService.CreateOrGetAsync` ordena los motivos por urgencia y **solo asciende** el aviso
pendiente si el motivo nuevo tiene precedencia **estrictamente mayor**; nunca lo degrada:

| Motivo | Precedencia | Por qué |
|---|---|---|
| `PIN` | 3 | Sin el PIN el comensal **no puede ni pedir**: es lo más bloqueante |
| `BILL` | 2 | Pide la cuenta: petición concreta y accionable, y la mesa **no rota** hasta cumplirla. Pesa más que una llamada genérica, cuyo motivo ni se conoce |
| `WAITER` | 1 | Quiere atención presencial, motivo desconocido |
| `ETA` (y cualquier motivo futuro sin urgencia propia) | 0 | Es una **pregunta**, no una urgencia |

> Valores reales de `WaiterCallService.Precedencia` (`WaiterCallService.cs:31-37`). La tabla anterior de
> este documento (PIN 2 · WAITER 1 · ETA 0, sin `BILL`) estaba desfasada.

Consecuencias implementadas:
- Un `ETA` que llega sobre un `PIN`, un `BILL` o un `WAITER` pendiente **no los pisa**: se conserva el
  aviso previo intacto. Si el camarero perdiera el motivo real, iría a la mesa a responder "cuándo sale"
  mientras el comensal sigue sin poder pedir.
- Un `PIN` **sí asciende** un `WAITER` o un `BILL` pendiente (era genérico, o la mesa quería irse; ahora se
  sabe que ni siquiera puede pedir). Un `BILL` asciende un `WAITER`.
- Al ascender se **conserva el `createdAt` original**, para no rejuvenecer el aviso en la cola del
  Salón (`ListWaiterCallsQuery` ordena por `createdAt` ascendente).
- Un aviso repetido del mismo motivo **no reinicia** la antigüedad: devuelve el pendiente tal cual.
- **El handler devuelve el motivo realmente persistido** (`call.Reason`), no el que se pidió: si
  había un `PIN` pendiente y llega un `ETA`, la respuesta `201` dice `"reason": "PIN"`. Vale para
  los handlers de `WAITER`, `PIN` y `ETA`; devolver un `"ETA"` fijo mentiría sobre el estado guardado.
- `GET /sessions/calls` devuelve `reason` en cada `WaiterCallDto`, y el Salón lo usa para etiquetar
  y sonar distinto cada aviso.

### Eventos SignalR del módulo

Hub `SessionsHub` en `/hubs/sessions`; método de cliente **`SessionEvent`**; grupo
**`waiter-{tenantId}`** (se entra invocando `JoinWaiterGroup(tenantId)`, se sale con
`LeaveWaiterGroup`). Todo el módulo difunde por `ISignalRNotifier.NotifyWaitersAsync`, es decir
**siempre a ese grupo**: cualquier vista del personal suscrita los recibe. Hoy se suscriben la vista
**Salón** y la **Caja**; el KDS no se conecta a este hub.

> **`SessionsHub` pasa a `[Authorize]`** *(diseñado — pendiente de implementación, PRD-019 — ADR-040)*.
> Hoy es la **única** puerta SignalR anónima del backend (`SessionsHub.cs:9`, sin atributo; `OrdersHub` y
> `RealtimeHub` ya llevan `[Authorize]`): cualquier WebSocket podía unirse a `waiter-{tenantId}` y escuchar
> los avisos de todas las mesas del local. Con `[Authorize]` el token viaja por `?access_token=` como en los
> otros hubs (ARCHITECTURE §tiempo real; `JoinWaiterGroup` además comprobará que el `tenantId` pedido sea el
> del token). **Verificación de clientes**: Caja (`cashdesk/page.tsx:245`), Salón (`salon/page.tsx:214`) y
> PDA (`pda/page.tsx:211`) **ya** construyen la URL con `access_token`, así que no se rompen; el **Runner**
> no se conecta hoy a este hub y lo hará en Night (`AvisosPanel`), ya con token. Deuda vista de paso: la PDA
> conecta pero **no invoca `JoinWaiterGroup`** (`pda/page.tsx:240`), por lo que no recibe los avisos en vivo;
> se corrige en el mismo PR.

Los cuatro eventos del **ciclo de sesión** (`SESSION_OPENED`, `SESSION_CLOSED`, `PIN_REGENERATED`,
`WAITER_REASSIGNED`) difunden en *best-effort*: van envueltos en `try/catch`, y si el hub falla se
registra un warning (`sessions.signalr.*_notify_failed`) y la operación HTTP se confirma igual. Los
**avisos de mesa** no llevan ese `try/catch`: un fallo del hub sí se propaga al request.

| Evento | Payload | Lo emite |
|---|---|---|
| `SESSION_OPENED` | `{ type, sessionId, tableCode, waiterId, serviceMode, qrMode, pinRequired }` | `POST /sessions` |
| `SESSION_CLOSED` | `{ type, sessionId, tableCode }` | `POST /sessions/{id}/close` |
| `PIN_REGENERATED` | `{ type, sessionId, tableCode }` — **sin el PIN** | `POST /sessions/{id}/pin/regenerate` |
| `WAITER_REASSIGNED` | `{ type, sessionId, tableCode, newWaiterId }` | `PATCH /sessions/{id}` |
| `WAITER_CALLED` | `{ type, sessionId, tableCode, reason, createdAt }` | `POST /sessions/{id}/call-waiter` |
| `PIN_REQUESTED` | `{ type, sessionId, tableCode, reason, createdAt }` | `POST /sessions/{id}/request-pin` |
| `BILL_REQUESTED` | `{ type, sessionId, tableCode, createdAt }` | `POST /sessions/{id}/request-bill` |
| `ETA_REQUESTED` | `{ type, sessionId, tableCode, createdAt }` | `POST /sessions/{id}/ask-eta` |
| `WAITER_CALL_ACKNOWLEDGED` | `{ type, sessionId, tableCode, acknowledgedBy }` | `POST /sessions/{id}/call-waiter/acknowledge` |
| `MAP_UPDATED` *(diseñado — PRD-019)* | `{ type, mode, tableIds[] }` | `PATCH /tables/layout` (solo si `mode` es el activo) y, cuando exista, `/floorplan/live` |
| `FLOOR_UPDATED` *(diseñado — PRD-019)* | `{ type, floorId, visibleIn[] }` | `PATCH /floors/{floorId}` (solo si cambió la visibilidad del modo activo) |

> `WAITER_CALLED` y `PIN_REQUESTED` llevan `reason` en el payload —el **persistido**, no el pedido—;
> `BILL_REQUESTED` y `ETA_REQUESTED` no lo llevan, porque el consumidor reacciona al `type` y
> rehidrata la cola con `GET /sessions/calls`.

> **`MAP_UPDATED` y `FLOOR_UPDATED`** *(diseñado — PRD-019)* se difunden como `SessionEvent` con `type` al
> grupo `waiter-{tenantId}` (`ISignalRNotifier.NotifyWaitersAsync`), en *best-effort* con `try/catch`, como
> los eventos de ciclo. Consumo: Caja (añadir los dos `type` al filtro de `cashdesk/page.tsx`), PDA, Host y
> Runner (que se conecta a este hub en Night) vuelven a pedir `GET /floorplan`. El sondeo actual (5-6 s) se
> mantiene como respaldo; el objetivo «< 2 s» solo se cumple con el evento.
>
> El grupo `waiter-{tenantId}` y `GET /sessions/calls` siguen siendo de todo el local; el alcance por barra
> del runner en Night es un filtro de cliente en `runner/page.tsx`, igual que el filtro por sector de la
> PDA. Sin cambios de backend.

---

## Endpoints — Table Management

---

### GET /api/v1/tables

**Objetivo**
Lista todas las mesas configuradas para un tenant con su estado y configuración.

**Auth**
Política `StaffJwt`: **cualquier JWT de personal del tenant**, sea cual sea su rol (`Admin`,
`Cajero`, `KDS`, `Host`, `PDA`/`Mesero`, `Runner`).

> La política exige `userType == "staff"`, así que **un token de Platform Admin no pasa hoy** por
> ella, aunque el controlador tenga después una comprobación de tenant que sí lo contemplaría.

**Request**
- Path params: Ninguno
- Query params:
  - `tenantId` (requerido) — UUID del tenant
  - `active` (opcional) — `true`|`false`: filtra por estado activo/inactivo
  - `zone` (opcional) — string: filtra por zona
- Body: Ninguno

**Response 200**
```json
{
  "tables": [
    {
      "id": "table-uuid-1",
      "tenantId": "tenant-uuid",
      "tableCode": "M01",
      "zone": "Terraza",
      "active": true,
      "qrMode": "FIXED",
      "requiresPin": true,
      "hasActiveSession": true,
      "activeSessionId": "session-uuid-1",
      "createdAt": "2025-10-01T09:00:00Z",
      "updatedAt": "2025-10-01T09:00:00Z"
    },
    {
      "id": "table-uuid-2",
      "tenantId": "tenant-uuid",
      "tableCode": "M02",
      "zone": "Interior",
      "active": true,
      "qrMode": "DYNAMIC",
      "requiresPin": false,
      "hasActiveSession": false,
      "activeSessionId": null,
      "createdAt": "2025-10-01T09:05:00Z",
      "updatedAt": "2025-10-01T09:05:00Z"
    }
  ],
  "total": 2
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido
- El JWT debe pertenecer al mismo `tenantId` o ser Platform Admin
- `active`: si se provee, debe ser `"true"` o `"false"`

**Reglas de Negocio**
- `hasActiveSession` se resuelve consultando la clave Redis `table:{tenantId}:{tableCode}:active`; si la clave existe, hay sesión activa
- Si Redis no responde, se hace fallback consultando `sessions` en PostgreSQL (`status = 'OPEN'`)
- Ordenadas por `zone` ASC, luego `table_code` ASC
- Mientras no exista un modelo formal de asignación de mesas a mesero en backend, la respuesta devuelve todas las mesas en servicio en el modo activo para cualquier staff del tenant.

> **[PRD-019] (diseñado)** `GET /tables` devuelve solo las mesas en servicio en el modo activo. El handler
> resuelve el modo con `IVenueModeProvider` en cada petición y conserva una mesa si `tables.active = true`,
> su piso tiene el modo en `floors.visible_in` (o la fila tiene `floor_id = null`) y su fila de
> `table_layouts` para ese modo tiene `active = true`; una mesa sin fila queda fuera. El filtro `active`
> mantiene su significado sobre `tables.active`, y `zone` también. Sin parámetro nuevo: el admin, que
> necesita las cerradas, usa `GET /floorplan?mode&includeHidden=true`.

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` ausente o no es UUID v4 válido |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero pertenece a otro tenant y no es Platform Admin |
| 404 | Tenant no encontrado |

**Observabilidad**
- Log: `"sessions.admin.tables.list"` `{ tenantId, filters: { active, zone }, total, requesterId }`
- Métrica: `sessions.admin_tables_list` (counter)

**Casos de Aceptación**
- [ ] Retorna lista completa de mesas con estado de sesión activa para cualquier staff del tenant
- [ ] Filtro `active=false` retorna solo mesas inactivas
- [ ] Filtro `zone=Terraza` retorna solo mesas de esa zona
- [ ] *(diseñado — PRD-019)* Local en NIGHT con Playa `visibleIn [RESTAURANT]`: `GET /tables?active=true` omite H001 y toda mesa con `table_layouts.active = false` en NIGHT; en RESTAURANT las devuelve como hoy
- [ ] `hasActiveSession` refleja correctamente la clave Redis
- [ ] Retorna 401 si no hay JWT
- [ ] Retorna 403 si el JWT es de otro tenant

---

## Endpoints suplementarios para staff

### GET /api/v1/sessions/{sessionId}/pin

**Objetivo**
Retornar el PIN activo de una sesión abierta para que mesero o cajero puedan verlo nuevamente sin reabrir la mesa.

**Auth**
JWT Staff del mismo tenant.

**Response 200**
```json
{
  "sessionId": "session-uuid",
  "tableCode": "M01",
  "pin": "6653"
}
```

### GET /api/v1/sessions/{sessionId}/join-link?type=DYNAMIC_QR|INVITE

**Objetivo**
Generar un token de unión reutilizable por staff para mostrar QR dinámico o compartir invite link al cliente.

**Auth**
JWT Staff del mismo tenant.

**Response 200**
```json
{
  "sessionId": "session-uuid",
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "tokenType": "DYNAMIC_QR",
  "token": "jwt-rs256",
  "joinUrl": "http://localhost:3000/join?tenantId=...&sessionId=...&token=...&type=DYNAMIC_QR"
}
```

### GET /api/v1/sessions/calls?tenantId={tenantId}

**Objetivo**
Listar llamados de mesero pendientes del tenant para hidratar la app `waiter` al cargar o reconectar.

**Auth**
JWT Staff del mismo tenant.

**Response 200** — lista de `WaiterCallDto` (orden: ver abajo). *(diseñado — PRD-019)*: cada
aviso gana **`tableKind`**:
```json
{ "tenantId": "9c2b…", "sessionId": "3f1a…", "tableCode": "R01", "tableKind": "RESERVADO", "createdAt": "2026-10-10T23:41:07Z", "reason": "WAITER" }
```
- `tableKind` (`TABLE` | `HIGH_TABLE` | `RESERVADO`) se **resuelve al listar** desde `tables.kind` por
  `tableCode` del tenant; **no** se guarda en `WaiterCallInfo`, así un cambio de clase se refleja sin
  reemitir. Salón y Runner lo usan para la pastilla «Reservado». Los eventos `*_REQUESTED` no lo llevan: el
  consumidor rehidrata con este endpoint.
- **Orden** *(diseñado — PRD-019)*: con el local en `NIGHT` (modo leído con `IVenueModeProvider`), el
  servidor ordena la lista por **`tableKind = RESERVADO` primero** y después por **`createdAt` ascendente**
  (la misma idea que las comandas de noche, ADR-038): el aviso de un reservado sale delante aunque haya
  llegado después. En `RESTAURANT`, solo `createdAt` ascendente, como hoy. `ListWaiterCallsHandler`
  (`WaiterCallQueries.cs`) ordena después de resolver `tableKind`; `WaiterCallService.ListByTenantAsync`
  sigue devolviendo por `createdAt`. La precedencia por motivo (PIN > BILL > WAITER > ETA) solo decide qué
  aviso sobrevive dentro de una misma sesión y no entra en el orden de la lista. Los clientes respetan este
  orden: el Salón ya pinta en el orden del servidor; `AvisosPanel` (hoy `pda/AvisosPanel.tsx`, que reordena
  solo por antigüedad) pasa a ordenar, en `NIGHT`, reservados primero y luego el más antiguo, con lo que
  cubre la PDA y el Runner.

**Casos de Aceptación**
- [ ] *(diseñado — PRD-019)* En NIGHT, T1 avisa a las 23:40 y R1 (RESERVADO) a las 23:45 → `GET /sessions/calls` devuelve R1 antes que T1; entre dos avisos de la misma clase, el más antiguo primero; en PDA y Runner se ve R1 arriba. En RESTAURANT, el orden es solo por llegada

### POST /api/v1/sessions/{sessionId}/call-waiter

**Objetivo**
Registrar un llamado de mesero para una sesión abierta y emitir evento real-time al grupo de meseros del tenant.

**Auth**
Sin auth obligatoria. Si el caller envía JWT válido en un endpoint `AllowAnonymous`, se procesa de forma opcional.

### POST /api/v1/sessions/{sessionId}/call-waiter/acknowledge

**Objetivo**
Marcar como atendido un llamado de mesero pendiente y removerlo de la lista activa del tenant.

**Auth**
JWT Staff del mismo tenant.

### POST /api/v1/sessions/{sessionId}/invite-link  [v1.x]

**Objetivo**
Un participante genera un **link de invitación** (token JWT firmado, `INVITE`) para compartir la sesión (p. ej. por WhatsApp). Body `{ tenantId }`; reusa `GenerateSessionJoinTokenQuery(..., "INVITE")` y guarda el token en Redis. **Auth:** `AllowAnonymous` (lo dispara el cliente desde el menú).

**Acceso sin PIN:** quien abra el link entra por `POST /sessions/join` y el handler marca al nuevo participante como **PIN validado** (`SetPinValidatedAsync` + `MarkPinValidated`), de modo que ordena **sin teclear el PIN** (el token firmado ES la prueba de acceso; el PIN nunca viaja en la URL — implementa ARCHITECTURE §9.7). El menú arma la URL `…/join?tenantId&token&type=INVITE`.

> *(diseñado — pendiente de implementación, PRD-019 — ADR-040)* **En `NIGHT` el enlace NO exime del PIN**.
> El cambio toca dos líneas de `SessionCommands.cs`: la 399 pasa a
> `inviteGrantsPin = isInviteJoin && session.PinRequired && mode != NIGHT` (controla si se persiste el PIN)
> y la 418 (respuesta de un participante nuevo) pasa a `pinValidated = isInviteJoin && mode != NIGHT`. Hoy
> la 418 es `pinValidated = isInviteJoin`, así que cambiar solo la 399 devolvería `pinValidated: true` a un
> invitado de noche sin PIN guardado. En `RESTAURANT` la respuesta no cambia; los joins de participantes que
> vuelven (420-425) ya leen el estado guardado. El invitado entra en la sesión, pero se le pide el PIN en el
> registro (nombre + PIN), como a cualquiera: de noche, nombre + PIN son la identidad de la mesa y un enlace
> reenviado por WhatsApp no debe saltársela. En `RESTAURANT` no cambia nada.

> **[fix] `tokenType` robusto:** `POST /sessions/join` ya **no rechaza** un `tokenType` distinto de `DYNAMIC_QR`/`INVITE`. El token es **self-describing**: el handler valida la firma y lo coteja contra su copia en Redis probando ambas variantes (invite/dynamic), usando `tokenType` solo como pista de orden. Así un link compartido cuyo `type` llega alterado (p. ej. en minúscula por algún cliente de mensajería) **igual se une** (antes daba `400 "tokenType debe ser DYNAMIC_QR o INVITE"`).

### POST /api/v1/sessions/{sessionId}/request-pin

**Objetivo**
El cliente pide al mesero el PIN de su mesa (QR fijo que exige PIN). Emite `PIN_REQUESTED` al grupo `waiter-{tenantId}` y persiste el llamado en Redis para meseros que se reconecten.

**Auth**
`AllowAnonymous` (lo dispara el cliente desde el menú).

**Detalle de implementación**
- `CreatePinRequestCommand` reusa `IWaiterCallService.CreateOrGetAsync(..., reason: "PIN")`. El `WaiterCallInfo` lleva un campo `Reason` (`"WAITER"` | `"PIN"` | `"BILL"` | `"ETA"`, default `"WAITER"`, retro-compatible con entradas viejas en Redis). Un llamado genérico pendiente se "promociona" a `PIN` si luego piden el PIN; un PIN no se degrada. Ver §Avisos de mesa para la tabla completa de precedencias.
- `GET /sessions/calls` devuelve `reason` en cada `WaiterCallDto`; la vista **Salón** de `staff` (`/salon`) lo traduce a su tarjeta —"Pide el PIN · Toca para dictar el PIN de la mesa", icono de llave— y suena con `beep("aviso")`.
- El evento real-time `PIN_REQUESTED` se documenta en PRD-008 §3.1.
- El cliente ingresa el PIN en el modal del menú (PRD-002 US-119) y el pedido se reintenta contra `/pin/validate` + `POST /orders`.

### POST /api/v1/sessions/{sessionId}/request-bill

**Objetivo**
El comensal "pide la cuenta". Emite `BILL_REQUESTED` al grupo `waiter-{tenantId}`; lo recoge la caja
(`/cashdesk`). Devuelve `201` con un `WaiterCallDto` de `reason: "BILL"`.

**Auth**
`AllowAnonymous` (lo dispara el cliente desde el menú).

> *(actualizado)* **Sí persiste en Redis**: pasa por `IWaiterCallService.CreateOrGetAsync(..., "BILL")`
> como los demás motivos, aparece en `GET /sessions/calls`, se cierra con `acknowledge` y tiene
> precedencia 2 (asciende un `WAITER`; un `PIN` lo asciende a él). El `201` devuelve el motivo realmente
> persistido, no un `"BILL"` fijo (ver §Avisos de mesa). Lo atienden Salón y PDA; la caja ya no.

---

### POST /api/v1/sessions/{sessionId}/ask-eta *(v2.0 — ADR-035)*

#### Objetivo
El comensal pregunta **"¿cuándo sale?"** por su pedido: deja un aviso de mesa con motivo `ETA` y
avisa en vivo a la sala para que un camarero se acerque a responder **en persona**.

#### Request
- **Auth**: `[AllowAnonymous]`. Lo dispara el propio comensal desde "Mis pedidos" del menú, que no
  tiene JWT de personal. No se exige participación en la sesión: el `sessionId` de la URL es la
  única prueba, igual que en `call-waiter`, `request-pin` y `request-bill`.
- **Headers**: ninguno obligatorio.
- **Path params**: `{sessionId}` — UUID de la sesión (restricción de ruta `:guid`).
- **Query params**: ninguno.
- **Body**: ninguno.

```http
POST /api/v1/sessions/3f1a…/ask-eta
```

#### Response
`201 Created` con el `WaiterCallDto` **realmente persistido**:

```json
{
  "tenantId": "9c2b…",
  "sessionId": "3f1a…",
  "tableCode": "H014",
  "createdAt": "2026-08-25T13:41:07Z",
  "reason": "ETA"
}
```

> El `Location` va vacío: el controlador base responde con `Created(string.Empty, valor)`. El aviso
> no es un recurso direccionable — se consulta en la cola con `GET /sessions/calls`.

#### Validaciones
- `sessionId`: debe ser un UUID (lo impone la ruta; si no lo es, no hay match → `404`).
- **No hay `AbstractValidator`** para `CreateEtaRequestCommand`: al no haber body, no hay nada más
  que validar. La única comprobación es de estado, en el handler (sesión existente y abierta).

#### Reglas de Negocio
- El handler carga la sesión y exige `IsOpen`; si no existe o está cerrada → `404`.
- Persiste el aviso con `IWaiterCallService.CreateOrGetAsync(..., reason: "ETA")` en
  `waiter_call:{tenantId}:{sessionId}` (TTL 4 h). **Es durable a propósito**: un `ETA` tiene que
  sobrevivir a la recarga o reconexión del Salón, aparecer en `GET /sessions/calls` y poder darse por
  atendido con `POST /call-waiter/acknowledge`.
- **Precedencia** *(actualizado)*: `ETA` es el motivo **menos urgente** (0). Si ya había un `PIN` (3), un
  `BILL` (2) o un `WAITER` (1) pendiente, **no lo pisa**: el servicio devuelve el aviso previo intacto, con
  su `createdAt` original. Ver §Avisos de mesa.
- **La respuesta devuelve `call.Reason`, no un `"ETA"` fijo**: si el aviso pendiente era un `PIN`,
  el `201` dice `"reason": "PIN"`. Devolver `"ETA"` mentiría sobre el estado guardado.
- Emite `ETA_REQUESTED` `{ type, sessionId, tableCode, createdAt }` al grupo `waiter-{tenantId}`
  del hub `/hubs/sessions`. **Nunca llega a cocina**: el KDS no está en ese grupo y no existe
  emisión al hub de órdenes. Un plato no se adelanta porque pregunten por él (ADR-032: quien dicta
  el ritmo del pase es la estación).
- **No calcula ni promete un ETA.** ADR-029 retiró del alcance el tiempo estimado de preparación al
  caer los platos compuestos. Lo que el menú muestra es la **antigüedad real** del pedido en su
  estado actual ("En preparación · desde hace 8 min"), leída de los **sellos por transición** de
  `order_items` (`queued_at`, `preparation_started_at`, `ready_at` — ADR-032), con retroceso a
  `confirmedAt`/`createdAt` si el sello falta. En `READY` no se dice el tiempo, se dice "sale para
  tu mesa". El sistema informa, no orquesta.
- **Antirrebote en el cliente**: el menú deshabilita el botón durante `ETA_COOLDOWN_MS = 60_000`
  (1 min) tras un envío correcto. En el backend no hay rate limit propio para este endpoint; la
  idempotencia de `CreateOrGetAsync` ya evita que repetir la pregunta duplique avisos o rejuvenezca
  la cola.
- Consumidor actual: la vista **Salón** (`/salon`) de `staff`, que rehidrata la cola, pinta el aviso
  con su copy propio ("Pregunta por su plato · ¿Cuándo sale? — quiere saber cómo va su pedido",
  icono de reloj, acción "Le informo") y suena con `beep("suave")` — más grave y más bajo que un
  llamado, porque es una pregunta, no una urgencia. En el plano, un `ETA` **no pisa** un badge de
  "listo", de `PIN` ni de llamada ya marcado en la mesa: misma precedencia que en el backend.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `NOT_FOUND` | 404 | La sesión no existe, o existe pero su `status` ya es `CLOSED` |
| — | 404 | `{sessionId}` no es un UUID: la ruta no hace match |

> No hay `401`/`403`: el endpoint es anónimo por diseño. Tampoco `409`: pedir dos veces no es un
> conflicto, es el mismo aviso.

#### Observabilidad
- **Hoy el handler no emite log estructurado ni métrica propios.** La traza real es la clave
  `waiter_call:{tenantId}:{sessionId}` en Redis (con su `reason` y su `createdAt`), el evento
  `ETA_REQUESTED` y el log de request estándar de ASP.NET.
- Pendiente, para alinearlo con el resto del módulo: log `"sessions.client.eta_requested"`
  `{ tenantId, sessionId, tableCode, reasonPersisted }` y métrica `sessions.eta_requested`
  (counter, label `reasonPersisted`) — mide cuántas preguntas se quedan bajo un aviso más urgente.

#### Casos de Aceptación
- [ ] Con sesión abierta y sin aviso previo, devuelve `201` con `reason: "ETA"` y crea la clave en Redis
- [ ] El aviso aparece en `GET /sessions/calls` y se cierra con `POST /call-waiter/acknowledge`
- [ ] Con un `PIN` pendiente, el `201` devuelve `reason: "PIN"` y el aviso guardado **no cambia** (ni su `createdAt`)
- [ ] Con un `WAITER` pendiente, el `201` devuelve `reason: "WAITER"` y el aviso guardado no cambia
- [ ] Con un `BILL` pendiente, el `201` devuelve `reason: "BILL"` y el aviso guardado no cambia *(actualizado)*
- [ ] El Salón recibe `ETA_REQUESTED` en vivo; **el KDS no recibe nada** y ningún plato cambia de estado
- [ ] Sesión cerrada o inexistente → `404 NOT_FOUND`
- [ ] Repetir la llamada no duplica avisos ni rejuvenece la posición en la cola

---

### POST /api/v1/tables

**Objetivo**
Crea una nueva mesa para el tenant con su código único y configuración inicial.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "tenant-uuid",
  "tableCode": "M05",
  "zone": "Terraza",
  "active": true,
  "qrMode": "FIXED",
  "requiresPin": true,
  "mode": "NIGHT"
}
```
> `mode` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 201**
```json
{
  "id": "table-uuid-new",
  "tenantId": "tenant-uuid",
  "tableCode": "M05",
  "zone": "Terraza",
  "active": true,
  "qrMode": "FIXED",
  "requiresPin": true,
  "createdAt": "2025-12-01T10:00:00Z",
  "updatedAt": "2025-12-01T10:00:00Z"
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido; debe coincidir con el tenant del JWT (o ser Platform Admin)
- `tableCode`: requerido, string, 1–50 caracteres, alfanumérico con guiones permitidos; debe ser único por tenant (constraint DB)
- `zone`: opcional, string, 1–100 caracteres
- `active`: opcional, boolean, default `true`
- `qrMode`: opcional, `FIXED` o `DYNAMIC` (case-insensitive), default `FIXED`
- `requiresPin`: opcional, boolean, default `true`; se fuerza a `false` cuando `qrMode = DYNAMIC` (ver `SetQrConfig`)
- `mode` *(diseñado — PRD-019)*: opcional, `RESTAURANT` | `NIGHT`; por defecto, el modo activo de `IVenueModeProvider`. El editor envía siempre el de su pestaña. Un valor inválido → `400 INVALID_MODE`

**Reglas de Negocio**
- Se genera un UUID v4 nuevo para `id`
- Si `tableCode` ya existe para el tenant, la constraint UNIQUE de PostgreSQL genera un error que se traduce a 409
- `createdAt` y `updatedAt` se fijan al timestamp actual UTC
- *(diseñado — PRD-019)* En la misma transacción que el `INSERT` en `tables`, crea las dos filas de `table_layouts`: la del `mode` recibido con `floorId/posX/posY` del body y `active = true`; la del otro modo con la misma `floorId/posX/posY`, `active = false` y `station_id = null`. No escribe `tables.floor_id/pos_x/pos_y`. La respuesta devuelve `floorId/posX/posY` tal como se recibieron

**Errores**
| Código | Condición |
|---|---|
| 400 | `tableCode` ausente, vacío, o excede 50 caracteres |
| 400 | `tableCode` contiene caracteres no permitidos |
| 400 | `tenantId` ausente o no es UUID v4 válido |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero pertenece a otro tenant y no es Platform Admin |
| 409 | Ya existe una mesa con ese `tableCode` para el tenant |
| 400 | *(diseñado — PRD-019)* `mode` fuera de `RESTAURANT` \| `NIGHT` (`INVALID_MODE`) |

**Observabilidad**
- Log: `"sessions.admin.table.created"` `{ tenantId, tableId, tableCode, zone, requesterId }`
- Métrica: `sessions.table_created` (counter)

**Casos de Aceptación**
- [ ] Crea mesa correctamente y retorna 201 con el objeto creado
- [ ] *(diseñado — PRD-019)* Crear con `mode=NIGHT` → aparece en `GET /floorplan?mode=NIGHT` y no en el de RESTAURANT sin `includeHidden`
- [ ] Retorna 409 si `tableCode` ya existe para el tenant
- [ ] Retorna 400 si `tableCode` está vacío
- [ ] Retorna 401/403 según corresponda

---

### PATCH /api/v1/tables/{tableId}

**Objetivo**
Actualiza el nombre de zona, el estado activo/inactivo o el código de una mesa existente.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tableId}` — UUID de la mesa
- Query params: Ninguno
- Body (todos los campos son opcionales; se aplica patch parcial):
```json
{
  "tableCode": "M05-B",
  "zone": "Jardín",
  "active": false,
  "qrMode": "DYNAMIC",
  "requiresPin": false,
  "kind": "RESERVADO"
}
```
> `kind` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 200**
```json
{
  "id": "table-uuid-1",
  "tenantId": "tenant-uuid",
  "tableCode": "M05-B",
  "zone": "Jardín",
  "active": false,
  "qrMode": "DYNAMIC",
  "requiresPin": false,
  "kind": "RESERVADO",
  "createdAt": "2025-10-01T09:00:00Z",
  "updatedAt": "2025-12-01T11:00:00Z"
}
```

**Validaciones**
- `tableId`: UUID v4 válido, debe existir en la tabla `tables` para el tenant del JWT
- `tableCode`: si se provee, string, 1–50 caracteres, único por tenant
- `zone`: si se provee, string, 1–100 caracteres; enviar `null` para eliminar la zona
- `active`: si se provee, boolean
- `qrMode`: si se provee, `FIXED` o `DYNAMIC`
- `requiresPin`: si se provee, boolean; se fuerza a `false` cuando `qrMode = DYNAMIC`
- `kind` *(diseñado — PRD-019)*: si se provee, `TABLE` | `HIGH_TABLE` | `RESERVADO` (case-insensitive); otro valor → `400 INVALID_TABLE_KIND`
- *(diseñado — PRD-019)* Deja de aceptar `floorId/posX/posY`: si llegan, `400 Validation` con el mensaje «la posición se edita por modo con PATCH /tables/layout». Hoy ningún cliente los manda (el editor solo envía `shape/seats`); se quitan del tipo `updateTable` de `@mestobeach/api-client`. `active` sigue siendo el interruptor global (`tables.active`)
- Al menos un campo debe estar presente en el body

**Reglas de Negocio**
- Si se cambia `active` a `false` y la mesa tiene una sesión abierta, la operación se bloquea con 409; la sesión debe cerrarse primero
- Si se cambia `tableCode`, se verifica unicidad en PostgreSQL; en conflicto retorna 409
- *(diseñado — PRD-019)* `kind` se escribe en `tables.kind`; **no** reescribe la prioridad de comandas ya creadas (Orders copió `priority` al crearlas) y sí afecta a las siguientes (la siguiente comanda de esa mesa creada en Night nace con priority de reservado; de día siempre 0) y a `tableKind` en `GET /sessions/calls`. Se permite con sesión abierta.
- `updated_at` se actualiza al timestamp actual UTC

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío |
| 400 | `tableCode` excede 50 caracteres o contiene caracteres no permitidos |
| 400 | `zone` excede 100 caracteres |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero la mesa pertenece a otro tenant y no es Platform Admin |
| 404 | `tableId` no encontrado para el tenant |
| 409 | Cambio de `tableCode` conflicta con uno existente |
| 409 | Se intenta desactivar una mesa con sesión `OPEN` |
| 400 | *(diseñado — PRD-019)* `kind` fuera de `TABLE` \| `HIGH_TABLE` \| `RESERVADO` (`INVALID_TABLE_KIND`) |
| 400 | *(diseñado — PRD-019)* El body trae `floorId`, `posX` o `posY` (`Validation`: «la posición se edita por modo con PATCH /tables/layout») |

**Observabilidad**
- Log: `"sessions.admin.table.updated"` `{ tenantId, tableId, changedFields, requesterId }`
- Métrica: `sessions.table_updated` (counter)

**Casos de Aceptación**
- [ ] Actualiza zona y retorna 200 correctamente
- [ ] *(diseñado — PRD-019)* `{ "kind": "RESERVADO" }` → 200; la siguiente comanda de esa mesa creada en Night nace con `priority` de reservado (de día siempre 0) y `GET /sessions/calls` la rotula `tableKind: "RESERVADO"`
- [ ] *(diseñado — PRD-019)* `{ "kind": "SOFA" }` → 400 `INVALID_TABLE_KIND`
- [ ] Desactiva mesa sin sesión abierta retorna 200
- [ ] Retorna 409 al intentar desactivar mesa con sesión `OPEN`
- [ ] Retorna 409 si el nuevo `tableCode` ya existe en el tenant
- [ ] Retorna 400 si el body está vacío
- [ ] Retorna 401/403 según corresponda

---

### GET /api/v1/tables/{tableId}/qr

**Objetivo**
Genera y retorna el PNG del código QR para una mesa, con el logo del restaurante superpuesto si está configurado.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tableId}` — UUID de la mesa
- Query params: Ninguno
- Body: Ninguno

**Response 200**
- Content-Type: `image/png`
- Body: stream binario PNG 1000×1000 px
- Headers:
  - `Content-Disposition: attachment; filename="qr-{tableCode}.png"`
  - `Cache-Control: no-store`

**Validaciones**
- `tableId`: UUID v4 válido, debe existir y pertenecer al tenant del JWT
- La mesa debe estar activa (`active = true`)

**Reglas de Negocio**
- *(actualizado)* Hoy el QR codifica **`http://localhost:3000/{tenantSlug}?t={tableCode}`** con el host **fijo en código** (`TableCommands.cs:321`); `tenantSlug` sale de `ITenantSettings` (o el `tenantId` si no hay slug). Es una URL que el menú **no sirve**: su entrada real es `/mesa`
- *(diseñado — pendiente de implementación, PRD-019)* Pasa a codificar **`{MENU_PUBLIC_URL}/mesa?tenant={tenantId}&table={tableCode}`**. `MENU_PUBLIC_URL` es variable de entorno de la API (sin barra final; en dev `http://localhost:3000`); si falta, se usa `Frontend:BaseUrl`, que ya lee `MenuUrlProvider`. El slug desaparece de la URL: el menú resuelve por `tenantId`. Tras desplegar hay que **regenerar e imprimir** los QR
- Si existe el blob `branding/{tenantId}/logo.png` en Azure Blob Storage, se superpone centrado sobre el QR (con margen de error de corrección nivel H para soportar la superposición)
- El QR se genera on-demand con la librería `QRCoder` (.NET); no se persiste el PNG
- Si el logo no existe en Azure Blob, se genera el QR sin logo (no es un error)
- Modo QR de la mesa no afecta la generación del QR en este endpoint (siempre se genera el QR fijo basado en `tableCode`)

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero la mesa pertenece a otro tenant y no es Platform Admin |
| 404 | `tableId` no encontrado para el tenant |
| 400 | La mesa está inactiva (`active = false`) |
| 502 | Fallo al consultar Azure Blob para el logo (la generación continúa sin logo con log de warning) |

**Observabilidad**
- Log: `"sessions.admin.table.qr_generated"` `{ tenantId, tableId, tableCode, hasLogo, requesterId }`
- Log (warning): `"sessions.admin.table.qr_logo_fetch_failed"` `{ tenantId, tableId, error }`
- Métrica: `sessions.qr_generated` (counter)

**Casos de Aceptación**
- [ ] Retorna PNG válido con logo superpuesto cuando el logo existe en Azure Blob
- [ ] Retorna PNG válido sin logo cuando `branding/{tenantId}/logo.png` no existe (sin error)
- [ ] El QR codifica `{MENU_PUBLIC_URL}/mesa?tenant={tenantId}&table={tableCode}` y escanearlo abre `/mesa` del menú *(diseñado — PRD-019; hoy codifica `localhost:3000/{slug}?t=`)*
- [ ] Retorna 400 si la mesa está inactiva
- [ ] Retorna 404 si `tableId` no existe
- [ ] Retorna 401/403 según corresponda

---

## Endpoints — Floors & Floor Plan (mapa de mesas) [v1.x]

> **v2.0:** esta sección sigue vigente. Lo que añade Mesto Beach (escala real, plano de fondo, alta en
> lote de hamacas, edición en vivo de la Host y ocupación) va en la sección siguiente, para no
> mezclar lo implementado con lo diseñado.

Tenant se resuelve del JWT staff (`HttpContext.Items["tenant_id"]`). **Mutaciones** de pisos y mesas
= `StaffAdmin`. En **lectura** hay dos puertas distintas: `GET /floors` sigue en
`StaffAdminOrCajero`, y **`GET /floorplan` pasó a `StaffJwt`** [v2.0] — cualquier miembro del
personal autenticado puede leer el plano.

> **Por qué `GET /floorplan` es `StaffJwt`** [v2.0]: el plano dejó de ser cosa del editor del admin
> y del mapa de la caja. Hoy lo consumen también **Recepción, PDA, Runner** y el **KDS** (que lo usa
> solo para rotular el sector de cada mesa en la comanda) — roles `Host`, `PDA`/`Mesero`, `Runner` y
> `KDS`, ninguno de los cuales pasa `StaffAdminOrCajero`. Recibían `403` y la
> interfaz lo traducía a "pídele al administrador que diseñe el mapa", con el mapa ya diseñado
> delante. Es una consulta de **layout puro y de solo lectura**, del propio tenant: no expone PIN,
> ni importes, ni datos del comensal. La **edición** del plano no se movió: sigue en `StaffAdmin`,
> y la frontera Host/Administración de ADR-030 queda intacta.

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| GET | `/api/v1/floors` | Admin/Cajero | Lista pisos (config: nombre, orden, canvas, walls), ordenados por `sort_order` y luego `created_at`. |
| GET | `/api/v1/floorplan` | **Staff (cualquier rol)** | Pisos + **todas** las mesas (posición, forma, sillas, **`qrMode` + `requiresPin`**) + estado `hasActiveSession`/`activeSessionId` + **`guestCount`** de la sesión activa (null si la mesa está libre o se abrió sin registrar comensales). Sectores en el mismo orden que `GET /floors`. Lo consumen el editor del admin, el mapa del cajero (usa `qrMode` para abrir sin preguntar), Recepción, PDA, Runner y el KDS (solo el nombre del sector por mesa). **Salón (`/salon`) no lo usa**: se dibuja con `GET /tables`, que con PRD-019 aplica el mismo filtro de modo activo (ver §Cambios en endpoints existentes). **[PRD-019]** acepta `?mode=&includeHidden=` y devuelve `mode`, `floors[].visibleIn` y `tables[].kind/active/stationId` del modo pedido — ver §Distribución por modo. |
| POST | `/api/v1/floors` | Admin | Crea piso `{name}`; canvas default 1000×700, `sort_order` = max+1. |
| PATCH | `/api/v1/floors/reorder` | Admin | `{orderedFloorIds:[...]}` reasigna `sort_order`. |
| PATCH | `/api/v1/floors/{id}` | Admin | Renombra `{name}`. **[PRD-019]** también `visibleIn` (ver §Distribución por modo). |
| PATCH | `/api/v1/floors/{id}/layout` | Admin | `{width?,height?,walls?:[{x1,y1,x2,y2}]}` (jsonb). |
| DELETE | `/api/v1/floors/{id}` | Admin | Borra piso; sus mesas pasan a `floor_id = null` (no se borran). |
| POST | `/api/v1/tables` | Admin | Crea mesa; acepta `floorId, posX, posY, seats, shape` y también `qrMode, requiresPin` (defaults: `FIXED`, `true`). **[PRD-019]** acepta `mode` y crea las dos filas de `table_layouts` (activa en `mode`, inactiva en el otro) en vez de escribir `tables.floor_id/pos_*` — ver §POST /api/v1/tables. |
| PATCH | `/api/v1/tables/{id}` | Admin | Patch parcial sobre el JSON recibido: `tableCode, zone` (null = borrar), `active`, `floorId` (null = quitar del mapa), `posX, posY, seats, shape, qrMode, requiresPin`; **[PRD-019]** `kind`, y deja de aceptar `floorId/posX/posY` (400: la posición se edita por modo con `PATCH /tables/layout`) — ver §PATCH /api/v1/tables/{tableId}. Body vacío → 400. |
| PATCH | `/api/v1/tables/layout` | Admin | Guardado masivo del drag: `{placements:[{tableId,floorId,posX,posY}]}`. **[PRD-019]** el cuerpo pasa a `{mode, tables:[{tableId,floorId,posX,posY,active,stationId}]}` y escribe `table_layouts` (ver §Distribución por modo). |
| DELETE | `/api/v1/tables/{id}` | Admin | Elimina mesa; bloquea (409) si tiene sesión abierta. El historial de sesiones cerradas sobrevive (no hay FK `sessions.table_id → tables`; `table_code` está denormalizado). |

**Reglas**
- `shape` ∈ {`SQUARE`,`ROUND`}; `tableCode` sigue siendo alfanumérico + guiones (se usa en la URL del QR).
- `pin_required` no cambia aquí (ver §POST /sessions). El plano es layout puro.
- El `guestCount` que devuelve `/floorplan` sale de la columna implementada `sessions.guest_count`
  (§Sectores la llama `pax` en su diseño).

**El editor de sala del admin** [v2.0] (`admin → Mesas`) no estrena API: se apoya en estos mismos
endpoints, sobre el mismo plano a escala que ve el equipo.

| Gesto del admin | Endpoint | Detalle |
|---|---|---|
| Abrir el editor | `GET /api/v1/floorplan` | Un solo viaje: sectores + puestos + ocupación |
| Añadir un puesto | `POST /api/v1/tables` | Presets con la huella de su clase: en **Playa** el único es **Hamaca** (cuadrada, 2 plazas); en el resto, Redonda 2/4/6 y Cuadrada 4/6/8. El código se autogenera como *inicial del sector* + 2 dígitos (`S01`, `T01`, `P01`…), tomando el primer hueco libre; `zone` = nombre del sector, `qrMode: FIXED` y `requiresPin: false` **solo en Playa**. **[PRD-019]** crea las dos filas de `table_layouts` (activa en la pestaña del editor, inactiva en la otra) |
| Cambiar forma o plazas | `PATCH /api/v1/tables/{id}` | Body `{shape}` y/o `{seats}` |
| Eliminar | `DELETE /api/v1/tables/{id}` | 409 si el puesto tiene sesión abierta |
| Guardar el arrastre | `PATCH /api/v1/tables/layout` | En **lote y solo los movidos**: el editor acumula los puestos sucios y los envía juntos al pulsar Guardar |

> El componente `ScaledFloorMap` es el mismo del resto de apps; el editor solo le pasa el modo
> edición (`onMove`). Lo que el admin arrastra es exactamente lo que el equipo verá.

---

## Sectores a escala, hamacas y ocupación *(v2.0 — ADR-030)*

El local sirve en tres **sectores** —Salón, Terraza y Playa, en ese orden de presentación— sobre la misma entidad `floors` que ya
existía. Tres cosas cambian respecto del mapa heredado: el plano pasa a estar **a escala real**,
aparece un tipo de puesto **hamaca** (~200 en Playa) y la **Host** puede reorganizar en vivo.

### Cambios de esquema

**`floors` — columnas nuevas**

| Columna | Tipo | Notas |
|---|---|---|
| `scale_m_per_unit` | numeric null | Metros que representa una unidad del lienzo. `null` = plano sin calibrar (comportamiento v1.x) |
| `background_url` | varchar(500) null | Imagen del plano del sector en Blob Storage |
| `background_opacity` | numeric | 0–1, por defecto `0.6`; el plano es fondo, no protagonista |

> La calibración se guarda como **una sola magnitud**, no como dos puntos: el editor pide al usuario
> marcar una distancia conocida y persiste el resultado ya resuelto. Cambiar la imagen de fondo no
> invalida las posiciones de las mesas, que viven en unidades de lienzo.

**`tables` — columnas nuevas**

| Columna | Tipo | Notas |
|---|---|---|
| `kind` | varchar(16) | `TABLE` \| `SUNBED` (hamaca). Por defecto `TABLE`. **[PRD-019]** la columna la crea `AddTableLayoutsPerModeAndKind` como `varchar(16)` con los valores `TABLE` \| `HIGH_TABLE` \| `RESERVADO` (§PostgreSQL Schema); `SUNBED` queda **reservado** para este diseño, sin colisión |
| `width` / `height` | numeric null | Tamaño en unidades de lienzo; `null` = tamaño por defecto según `kind` |
| `rotation` | numeric | Grados, por defecto 0 |

`shape` se mantiene (`SQUARE` \| `ROUND`) y se le suma el matiz de `kind`: una hamaca se dibuja con
su propio icono aunque comparta forma.

**`sessions` — columna nueva**

| Columna | Tipo | Notas |
|---|---|---|
| `pax` | int null | Comensales que registró la Host al asignar (ADR-030, D-06 de PRD-018) |

---

## Endpoints — Sectores, alta en lote y sala *(v2.0)*

> **Diseñados, NO implementados (agosto 2026).** Ninguna de las cinco rutas de esta sección existe
> hoy en `FloorsController`/`TablesController`, y los eventos `FLOOR_UPDATED` y `MAP_UPDATED` no se
> emiten en ningún punto del backend. Lo construido del mapa a escala es lo de la sección anterior
> —`GET /floorplan` (ya en `StaffJwt`), `POST/PATCH /tables` y `PATCH /tables/layout`, que es sobre
> lo que se apoya el editor de sala del admin—; la ocupación de Recepción se **deriva en el cliente**
> a partir de `GET /floorplan` sondeado, no de `GET /floorplan/occupancy`. Ver ARCHITECTURE §15.4.

### PATCH /api/v1/floors/{floorId}/scale

**Objetivo**
Calibra el sector: cuántos metros mide una unidad del lienzo, y qué imagen de plano se usa de fondo.

**Auth**
Staff JWT **Admin**

**Request**
```json
{
  "scaleMPerUnit": 0.05,
  "backgroundOpacity": 0.55
}
```

**Response 200**
```json
{
  "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca",
  "name": "Playa",
  "scaleMPerUnit": 0.05,
  "backgroundUrl": "https://…/floors/579f…/plano.webp",
  "backgroundOpacity": 0.55
}
```

**Validaciones**
- `scaleMPerUnit`: decimal > 0 y < 10; `null` admitido para descalibrar
- `backgroundOpacity`: 0 ≤ x ≤ 1

**Reglas de Negocio**
- No recalcula posiciones: las mesas siguen en unidades de lienzo y la escala es solo interpretación
- Emite `FLOOR_UPDATED` al tenant para que los mapas abiertos se refresquen

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `FLOOR_NOT_FOUND` | 404 | No existe o es de otro tenant |
| `INVALID_SCALE` | 422 | Escala fuera de rango |

**Casos de Aceptación**
- [ ] Calibrado el sector, la app muestra distancias en metros coherentes con el local
- [ ] Cambiar la imagen de fondo no mueve ninguna mesa

---

### POST /api/v1/floors/{floorId}/background

**Objetivo**
Sube la imagen del plano del sector.

**Auth**
Staff JWT **Admin** · `multipart/form-data` con campo `file`

**Reglas de Negocio**
- Mismo tratamiento que la portada del menú: se convierte a WebP hasta 2048 px de ancho (ver SDD-002)
- Se guarda en `floors/{floorId}/plano-{guid}.webp`; sustituir borra la anterior

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `UNSUPPORTED_MEDIA` | 415 | No es JPEG, PNG ni WebP |
| `FILE_TOO_LARGE` | 413 | Supera 5 MB |

**Casos de Aceptación**
- [ ] El plano aparece de fondo en el editor y en las apps del personal
- [ ] Subir uno nuevo reemplaza el anterior sin dejar huérfanos

---

### POST /api/v1/tables/batch

**Objetivo**
Da de alta puestos en lote: las ~200 hamacas de Playa no se crean una a una.

**Auth**
Staff JWT **Admin**

**Request**
```json
{
  "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca",
  "kind": "SUNBED",
  "prefix": "H",
  "from": 1,
  "to": 200,
  "padTo": 3,
  "layout": { "columns": 20, "startX": 60, "startY": 80, "stepX": 130, "stepY": 160 },
  "seats": 2,
  "qrMode": "FIXED",
  "requiresPin": false,
  "mode": "RESTAURANT"
}
```
> `mode` *(diseñado — PRD-019)*.

**Response 201**
```json
{ "created": 200, "skipped": 0, "firstCode": "H001", "lastCode": "H200" }
```

**Validaciones**
- `to - from + 1` ≤ 300 por llamada
- `prefix`: 1–4 caracteres alfanuméricos (entra en la URL del QR)
- `padTo`: 0–4; `H` + `padTo:3` produce `H001`
- El sector existe y pertenece al tenant
- `mode` *(diseñado — PRD-019)*: opcional, `RESTAURANT` | `NIGHT`, por defecto **`RESTAURANT`** (la Playa solo es visible de día); un valor inválido → `400 INVALID_MODE`

**Reglas de Negocio**
- **Idempotente por código**: los `tableCode` que ya existen se cuentan en `skipped` y no se tocan. Repetir la llamada no duplica ni sobrescribe posiciones ya ajustadas
- Coloca en cuadrícula según `layout`; la Host afinará después arrastrando (ADR-030)
- Cada puesto nace con su QR según `qrMode`/`requiresPin`
- *(diseñado — PRD-019)* Misma regla que `POST /tables`: por cada puesto creado, en la misma transacción, fila de `table_layouts` del `mode` **activa** con la posición de la cuadrícula y fila del otro modo **inactiva** (`active = false`, `station_id = null`) con la misma posición. Los códigos contados en `skipped` no tocan sus filas

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `RANGE_TOO_LARGE` | 422 | Más de 300 puestos en una llamada |
| `INVALID_PREFIX` | 422 | Prefijo no alfanumérico o demasiado largo |
| `FLOOR_NOT_FOUND` | 404 | Sector inexistente |
| `INVALID_MODE` | 400 | *(diseñado — PRD-019)* `mode` fuera de `RESTAURANT`/`NIGHT` |

**Observabilidad**
- `tables_batch_created` con `floorId`, `kind`, `created`, `skipped`

**Casos de Aceptación**
- [ ] Crear H001–H200 en una llamada deja 200 puestos colocados en cuadrícula
- [ ] Repetir la misma llamada no crea duplicados ni mueve los ya colocados
- [ ] Cada puesto imprime un QR válido con su código

---

### PATCH /api/v1/floorplan/live

**Objetivo**
La Host reorganiza la sala en vivo: mueve puestos y los activa o desactiva, con difusión inmediata.

**Auth**
Staff JWT con rol **Host** (o Admin)

**Request**
```json
{
  "moves": [ { "tableId": "…", "posX": 210.0, "posY": 140.0, "rotation": 15 } ],
  "activations": [ { "tableId": "…", "active": false } ]
}
```

**Response 200**
```json
{ "moved": 1, "activated": 0, "deactivated": 1 }
```

**Validaciones**
- Todos los puestos pertenecen al tenant
- Las coordenadas caen dentro del lienzo del sector

**Reglas de Negocio**
- **La Host no puede crear, eliminar ni renumerar** (ADR-030): esas operaciones siguen en los endpoints de Admin. El número está impreso en el QR físico y cambiarlo rompería el vínculo con el puesto real
- Desactivar un puesto **con sesión abierta** se rechaza: primero se cierra la mesa
- Emite `MAP_UPDATED` al tenant; lo consumen PDA, Runner y el mapa del cajero (< 2 s, OBJ-05 de PRD-018)
- *(diseñado — PRD-019)* Escribe sobre la fila `table_layouts` del **modo activo** (`IVenueModeProvider`), nunca sobre `tables.pos_*`: la Host mueve el plano de la noche sin tocar el del día. `activations[].active` es el `table_layouts.active` de ese modo; `rotation` sigue en `tables` (es de la mesa, no del modo). La Host vuelve a activar las mesas que apagó porque Recepción las recibe con `GET /floorplan?includeInactive=true`

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `TABLE_HAS_ACTIVE_SESSION` | 409 | Se intenta desactivar un puesto ocupado |
| `OUT_OF_CANVAS` | 422 | Coordenadas fuera del sector |
| `FORBIDDEN_ROLE` | 403 | El token no es Host ni Admin |

**Observabilidad**
- `floorplan_live_updated` con `movedCount`, `activationCount`, `actorRole`

**Casos de Aceptación**
- [ ] Un movimiento de la Host se ve en el PDA en menos de 2 segundos
- [ ] Intentar desactivar una mesa ocupada devuelve 409
- [ ] La Host no dispone de crear/eliminar/renumerar en su interfaz ni en la API

---

### GET /api/v1/floorplan/occupancy

**Objetivo**
Ocupación en vivo para la Host: personas totales y por sector, y qué puestos están libres.

**Auth**
Staff JWT (Host, Cajero o Admin)

**Response 200**
```json
{
  "totalPax": 86,
  "sectors": [
    { "floorId": "…", "name": "Terraza", "pax": 22, "occupied": 6, "free": 4 },
    { "floorId": "…", "name": "Salón",   "pax": 14, "occupied": 4, "free": 8 },
    { "floorId": "…", "name": "Playa",   "pax": 50, "occupied": 27, "free": 173 }
  ]
}
```

**Reglas de Negocio**
- `pax` sale de `sessions.pax` **registrado por la Host al asignar**, no del número de comensales que se identificaron en el móvil: la identificación es opcional y siempre iría por debajo (ADR-030, D-06)
- Una sesión sin `pax` cuenta como ocupada pero suma 0 personas; la UI lo señala para que la Host lo complete

**Casos de Aceptación**
- [ ] La ocupación cuadra con las sesiones abiertas y su `pax`
- [ ] Una sesión sin `pax` aparece marcada como incompleta

---

## Endpoints — Distribución por modo *(diseñado — pendiente de implementación, PRD-019 — ADR-038)*

> **Ninguna ruta de esta sección existe hoy con este contrato.** `GET /floorplan` y `PATCH /tables/layout`
> existen (§Floors & Floor Plan) y aquí se **reescriben** para trabajar por modo; `PATCH /floors/{id}` gana
> `visibleIn`. El modo activo lo da `IVenueModeProvider` (SDD-001 §Venue). Mutaciones = `StaffAdmin`;
> lectura = `StaffJwt`, igual que hoy. Al final, la tabla de **cambios en endpoints existentes**.

### GET /api/v1/floorplan?mode=&includeHidden=&includeInactive=

#### Objetivo
Devolver el plano **de un modo** —pisos visibles, mesas con su posición, estado por modo y barra— para las
apps del personal (modo activo) y para el editor del admin (cualquier modo, incluidas las mesas apagadas).

#### Request
- **Auth**: política `StaffJwt` (cualquier rol del tenant); `includeHidden=true` exige rol **Admin** (`StaffAdmin`); `includeInactive=true` lo pueden pedir Host y Admin.
- **Headers**: `Authorization: Bearer <staff-jwt>`.
- **Path params**: ninguno; el tenant sale del JWT.
- **Query params**:
  - `mode` (opcional) — `RESTAURANT` | `NIGHT`; **por defecto, el modo activo** del local.
  - `includeHidden` (opcional, default `false`) — `true` incluye pisos cuyo `visibleIn` no contiene `mode` y mesas con `active = false` en ese modo (solo Admin).
  - `includeInactive` (opcional, bool, default `false`) — para Host y Admin. Con `true` también viajan, con `active: false`, las mesas apagadas en el modo cuyo piso está visible en ese modo (las que no tienen fila, con `active: false, floorId: null`). No incluye pisos ocultos ni mesas apagadas en global (eso sigue siendo `includeHidden`, solo Admin). Si el rol no es Admin, solo vale para el modo activo: un `mode` distinto con `includeInactive` → `403 FORBIDDEN_ROLE`.
- **Body**: ninguno.

```http
GET /api/v1/floorplan?mode=NIGHT&includeHidden=true
```

#### Response
`200 OK`:

```json
{
  "mode": "NIGHT",
  "activeMode": "NIGHT",
  "floors": [
    { "id": "f-salon", "name": "Salón", "sortOrder": 1, "width": 1000, "height": 700, "walls": [], "visibleIn": ["RESTAURANT", "NIGHT"] },
    { "id": "f-playa", "name": "Playa", "sortOrder": 3, "width": 1600, "height": 900, "walls": [], "visibleIn": ["RESTAURANT"] }
  ],
  "tables": [
    { "id": "t-r01", "tableCode": "R01", "kind": "RESERVADO", "floorId": "f-salon", "posX": 120.0, "posY": 80.0,
      "seats": 8, "shape": "ROUND", "active": true, "activeGlobal": true, "stationId": "st-barra-2",
      "hasActiveSession": false, "activeSessionId": null, "qrMode": "FIXED", "requiresPin": true, "guestCount": null },
    { "id": "t-h001", "tableCode": "H001", "kind": "TABLE", "floorId": "f-playa", "posX": 60.0, "posY": 80.0,
      "seats": 2, "shape": "SQUARE", "active": false, "activeGlobal": true, "stationId": null,
      "hasActiveSession": false, "activeSessionId": null, "qrMode": "FIXED", "requiresPin": false, "guestCount": null }
  ]
}
```

- `mode` es el modo **servido**; `activeMode`, el del local ahora mismo (el editor pinta con él la pastilla «activo ahora»).
- `floors[].visibleIn`: modos en los que el piso existe. Sin `includeHidden`, un piso que no incluye `mode` **no viaja**, ni sus mesas.
- `tables[].floorId/posX/posY/active/stationId` salen de la fila `table_layouts` de `mode`; `kind`, `seats`, `shape`, `qrMode`, `requiresPin` de `tables`; `hasActiveSession/activeSessionId/guestCount` de la sesión abierta, como hoy.
- `tables[].active` es el **por modo** (`table_layouts.active`). Una mesa con `tables.active = false` (global) no viaja nunca sin `includeHidden`.
- `tables[].activeGlobal` es el interruptor **global** (`tables.active`). La mesa está en servicio si `active && activeGlobal`; sin `includeHidden`, `activeGlobal` siempre llega en `true` (las apagadas en global no viajan). El editor atenúa la mesa si cualquiera de los dos es `false`, pero en `PATCH /tables/layout` solo reenvía `active`. `FloorPlanTable` de `@mestobeach/api-client` se actualiza a mano con `activeGlobal`.

#### Validaciones
- `mode`: si se envía, `RESTAURANT` | `NIGHT` (case-insensitive); otro valor → `400 INVALID_MODE`.
- `includeHidden=true` con un JWT sin rol `Admin` → `403 FORBIDDEN_ROLE`.
- `includeInactive=true` con un rol distinto de Host o Admin → `403 FORBIDDEN_ROLE`; como Host, con un `mode` distinto del activo → `403 FORBIDDEN_ROLE`.

#### Reglas de Negocio
- Sin `mode`, el handler resuelve el modo activo con `IVenueModeProvider` **en cada petición** (sin caché propia): tras un cambio de modo, el siguiente sondeo ya devuelve el plano nuevo.
- Mesas sin fila en `table_layouts` para `mode` se tratan como `active = false, floorId = null` (solo viajan con `includeHidden` o `includeInactive`).
- Las mesas con fila `active = true` y `floorId = null` viajan sin `includeHidden`, como hoy viajan las mesas sin ubicar (la caja ve sus sesiones ad-hoc).
- Orden: pisos por `sort_order` y luego `created_at` (como `GET /floors`); mesas por `tableCode`.
- Lectura pura: no toca Redis salvo el fallback de sesión activa que ya existe (la base manda; ver comentario en `FloorPlanQueries.cs:96`).
- Consumidores: Recepción pide **sin `mode`** y con `includeInactive=true` (ve atenuadas las mesas que apagó para poder encenderlas otra vez); PDA, Runner, Caja y KDS piden **sin `mode`** y sin flags (ven el local tal como está); el editor del admin pide con `mode` + `includeHidden=true` y mantiene un borrador por modo (PRD-019 RF-1938).

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_MODE` | 400 | `mode` no es `RESTAURANT` ni `NIGHT` |
| — | 401 | JWT ausente o inválido |
| `FORBIDDEN_ROLE` | 403 | `includeHidden=true` sin rol Admin; `includeInactive=true` sin rol Host ni Admin, o como Host con un `mode` distinto del activo |

#### Observabilidad
- Log: `"sessions.floorplan.get"` `{ tenantId, mode, activeMode, includeHidden, floorCount, tableCount, requesterRole }`
- Métrica: `sessions.floorplan_get` (counter, labels `mode`, `includeHidden`)

#### Casos de Aceptación
- [ ] Sin `mode`, con el local en `NIGHT`, la Playa (`visibleIn: ["RESTAURANT"]`) y sus hamacas **no aparecen**
- [ ] `mode=RESTAURANT` desde el admin devuelve el plano de día aunque el local esté en `NIGHT`, con `activeMode: "NIGHT"`
- [ ] `includeHidden=true` como Admin incluye la Playa y las mesas con `active: false`; como `Host` devuelve 403
- [ ] Una mesa con `station_id` en su fila `NIGHT` viaja con `stationId` en `NIGHT` y con `stationId: null` en `RESTAURANT`
- [ ] Recién aplicada `AddTableLayoutsPerModeAndKind`, `mode=NIGHT` y `mode=RESTAURANT` devuelven las mismas posiciones hasta que el admin edite una
- [ ] La Host apaga H010 con `/floorplan/live`, sigue viéndola atenuada (`includeInactive`) y puede encenderla otra vez
- [ ] Una mesa con `tables.active=false` llega al editor con `activeGlobal:false`

---

### PATCH /api/v1/tables/layout

#### Objetivo
Guardar en lote la distribución **de un modo**: posición, piso, si la mesa está en servicio en ese modo y la
barra que la atiende.

#### Request
- **Auth**: `StaffAdmin`.
- **Path params**: ninguno; el tenant sale del JWT.
- **Body**:

```json
{
  "mode": "NIGHT",
  "tables": [
    { "tableId": "t-r01",  "floorId": "f-salon", "posX": 120.0, "posY": 80.0, "active": true,  "stationId": "st-barra-2" },
    { "tableId": "t-h001", "floorId": "f-playa", "posX": 60.0,  "posY": 80.0, "active": false, "stationId": null }
  ]
}
```

- `mode` — `RESTAURANT` | `NIGHT`, **obligatorio**. El cuerpo antiguo `{placements:[…]}` sin `mode` deja de aceptarse: el editor del admin es el único cliente y se actualiza en el mismo PR.
- `tables[]` — solo las mesas **tocadas** (el editor acumula las sucias y las manda juntas, como hoy). Por mesa: `tableId` obligatorio; `floorId` (null = sin ubicar), `posX`, `posY`, `active` (default `true`) y `stationId` (null = sin barra) opcionales; los que faltan **conservan** el valor de la fila.

#### Response
`200 OK`:

```json
{ "mode": "NIGHT", "applied": 2, "skipped": 0 }
```

- `applied` = filas escritas (`UPSERT` por `(table_id, mode)`); `skipped` = `tableId` que no son del tenant (se ignoran sin fallar, como hoy).

#### Validaciones
- `mode`: requerido, `RESTAURANT` | `NIGHT` → si no, `400 INVALID_MODE`.
- `tables`: requerido (puede ir vacío → `applied: 0`); `tableId` no vacío; `posX`/`posY` ≥ 0 si se envían.
- `floorId`: si se envía, piso del tenant → si no, `404 FLOOR_NOT_FOUND`.
- `stationId`: si se envía, estación del tenant con `flow_profile = BAR` y encendida en `mode` (`IsOnIn(mode)` de ADR-037: `is_active && active_in ∋ mode`) → si no, `400 STATION_NOT_BAR` / `404 STATION_NOT_FOUND`. `kds_stations` se consulta en solo lectura (`ExcludeFromMigrations`, como hace Orders con `tables`).

#### Reglas de Negocio
- Escribe **solo** `table_layouts` (`UPSERT`); `tables.floor_id/pos_x/pos_y` no se tocan (deprecadas).
- Es **idempotente**: repetir el mismo cuerpo no cambia nada ni duplica filas.
- Poner `active = false` a una mesa **con sesión abierta y `mode` = modo activo** se rechaza con `409 TABLE_HAS_ACTIVE_SESSION` (misma regla que hoy aplica `PATCH /tables/{id}` (`TableCommands.cs`) y que aplicará `/floorplan/live` cuando exista). Si `mode` no es el activo, se permite: surte efecto al cambiar de modo.
- Si `mode` es el activo, al terminar emite `MAP_UPDATED` (payload `{ type, mode, tableIds[] }`, ver §Eventos SignalR del módulo) al grupo `waiter-{tenantId}` de `SessionsHub` para que PDA, Runner y Caja refresquen (< 2 s). Si no, no emite nada: el plano de otro modo no está en pantalla.
- `stationId` en una fila `RESTAURANT` se **guarda pero no se usa** (el enrutado por mesa solo aplica en `NIGHT`, ADR-038); el editor no lo ofrece de día.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_MODE` | 400 | `mode` ausente o desconocido |
| `Validation` | 400 | `posX`/`posY` negativos, `tableId` vacío |
| `STATION_NOT_BAR` | 400 | `stationId` existe pero no es `BAR` o no se enciende en `mode` |
| `FLOOR_NOT_FOUND` | 404 | `floorId` no es del tenant |
| `STATION_NOT_FOUND` | 404 | `stationId` no es del tenant |
| `TABLE_HAS_ACTIVE_SESSION` | 409 | `active: false` sobre una mesa ocupada en el modo activo |
| — | 401 / 403 | Sin JWT / sin rol Admin |

#### Observabilidad
- Log: `"sessions.admin.tables.layout_saved"` `{ tenantId, mode, applied, skipped, deactivated, requesterId }` (hoy existe sin `mode`)
- Métrica: `sessions.tables_layout_saved` (counter, label `mode`)

#### Casos de Aceptación
- [ ] Guardar la distribución `NIGHT` no mueve ninguna mesa en `GET /floorplan?mode=RESTAURANT`
- [ ] `H001` con `active: false` en `NIGHT` desaparece del plano de noche y sigue en el de día
- [ ] `stationId` de una estación `FULL_PASS` → 400 `STATION_NOT_BAR`
- [ ] Desactivar en el modo activo una mesa con sesión abierta → 409; en el otro modo → 200
- [ ] Repetir el mismo cuerpo → mismo resultado, sin filas duplicadas (único `(table_id, mode)`)

---

### PATCH /api/v1/floors/{floorId}

#### Objetivo
Renombrar un piso y, nuevo, declarar **en qué modos existe** (`visibleIn`): la Playa cierra de noche.

#### Request
- **Auth**: `StaffAdmin`.
- **Path params**: `{floorId}` — UUID del piso.
- **Body** (patch parcial; al menos un campo):

```json
{ "name": "Playa", "visibleIn": ["RESTAURANT"] }
```

#### Response
`200 OK` — el `FloorDto` actualizado:

```json
{ "id": "f-playa", "name": "Playa", "sortOrder": 3, "width": 1600, "height": 900, "walls": [], "visibleIn": ["RESTAURANT"] }
```

#### Validaciones
- `name`: si se envía, 1–100 caracteres (como hoy).
- `visibleIn`: si se envía, array **no vacío** de valores dentro de `{RESTAURANT, NIGHT}` → si no, `400 VISIBLE_IN_EMPTY` / `400 INVALID_MODE`. Se normaliza a mayúsculas, se deduplica en silencio y se guarda en orden canónico `["RESTAURANT","NIGHT"]` en `floors.visible_in`.

#### Reglas de Negocio
- Escribe `floors.visible_in` (jsonb). **No toca** `table_layouts`: las mesas del piso conservan su posición en el modo oculto, por si el piso vuelve a abrirse.
- Quitar del `visibleIn` el **modo activo** con mesas del piso ocupadas se rechaza con `409 FLOOR_HAS_ACTIVE_SESSIONS` (lista de `tableCodes` en `extensions`). Si el modo no es el activo, se permite.
- Cuando el modo activo cambia y el piso deja de ser visible, sus mesas responden `409 TABLE_CLOSED_IN_MODE` a `POST /sessions` y `POST /sessions/join-by-table`: los QR físicos de la Playa siguen impresos y el menú lo traduce a «Esta zona está cerrada esta noche; acércate a Recepción» (ver join-by-table).
- Emite `FLOOR_UPDATED` al grupo `waiter-{tenantId}` solo si cambió la visibilidad del modo activo.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `VISIBLE_IN_EMPTY` | 400 | `visibleIn: []` |
| `INVALID_MODE` | 400 | Un valor fuera de `RESTAURANT`/`NIGHT` |
| `Validation` | 400 | Body vacío o `name` fuera de rango |
| `FLOOR_NOT_FOUND` | 404 | No existe o es de otro tenant |
| `FLOOR_HAS_ACTIVE_SESSIONS` | 409 | Se oculta el modo activo con mesas ocupadas |

> Un valor repetido en `visibleIn` (`["NIGHT","night"]`) **no es error**: se deduplica en silencio.

#### Observabilidad
- Log: `"sessions.admin.floor.updated"` `{ tenantId, floorId, changedFields, visibleIn, requesterId }`
- Métrica: `sessions.floor_visibility_updated` (counter) cuando cambia `visibleIn`

#### Casos de Aceptación
- [ ] `visibleIn: ["RESTAURANT"]` en Playa → en `NIGHT`, `GET /floorplan` no la devuelve y un join en `H001` da `409 TABLE_CLOSED_IN_MODE`
- [ ] Volver a `["RESTAURANT","NIGHT"]` recupera las posiciones `NIGHT` que tenían las hamacas
- [ ] `visibleIn: []` → 400 `VISIBLE_IN_EMPTY`
- [ ] Ocultar el modo activo con una hamaca ocupada → 409 con su `tableCode`

---

### Cambios en endpoints existentes *(diseñado — PRD-019)*

| Endpoint | Cambio | Detalle |
|---|---|---|
| `PATCH /api/v1/tables/{tableId}` | Acepta `kind` (`TABLE` \| `HIGH_TABLE` \| `RESERVADO`); `400 INVALID_TABLE_KIND` | §Table Management |
| `PATCH /api/v1/tables/{tableId}` | Deja de aceptar posición (`floorId/posX/posY` → 400) | §PATCH /tables/{id} |
| `POST /api/v1/tables` | Crea las dos filas de `table_layouts` (`mode` opcional) | §POST /tables |
| `POST /api/v1/tables/batch` | Ídem, `mode` por defecto `RESTAURANT` | §POST /tables/batch |
| `GET /api/v1/tables` | Filtra por el modo activo (piso visible + `table_layouts.active`), igual que `GET /floorplan` sin `includeHidden` | §GET /tables — consumidores: Salón, vista lista de Caja, admin/sessions |
| `POST /api/v1/sessions` | `409 TABLE_CLOSED_IN_MODE`; PIN único (`pins_in_use`), TTL 12 h; snapshot `sessions.venue_mode` | §Session Lifecycle |
| `POST /api/v1/sessions` (auto-alta ad-hoc en `STAFFED`) | La mesa auto-creada nace con sus dos filas (`floor_id = null`, `pos = null`, `active = true` en ambos modos) en la misma transacción | §POST /sessions |
| `POST /api/v1/sessions/join-by-table` | `409 TABLE_CLOSED_IN_MODE` (misma comprobación que `POST /sessions`, antes de buscar la sesión abierta) | abajo |
| `POST /api/v1/sessions/join` | En `NIGHT` el enlace de invitación **no concede PIN** (`inviteGrantsPin = false`) | §Session Lifecycle |
| `GET /api/v1/sessions/calls` | Cada aviso lleva `tableKind` | §Endpoints suplementarios |
| `GET /api/v1/tables/{tableId}/qr` | URL `{MENU_PUBLIC_URL}/mesa?tenant=&table=` | §Table Management |
| `POST /sessions/{id}/pin/regenerate` · `POST /sessions/{id}/close` | Mantienen `pins_in_use:{tenantId}` (hash PIN → `sessionId`: liberan por `sessionId` con `HDEL` y reservan con `HSETNX`) | §Session Lifecycle |
| `SessionsHub` | `[Authorize]` | §Eventos SignalR del módulo |

> `PATCH /floorplan/live` (PRD-018, §Sectores) aún no existe; cuando se construya, ya debe nacer escribiendo
> `table_layouts` del modo activo (ver su regla *(diseñado — PRD-019)*).

**`POST /api/v1/sessions/join-by-table` — `TABLE_CLOSED_IN_MODE`.** El endpoint (`SessionsController.cs:85`,
`[AllowAnonymous]`, body `{tenantId, tableCode, deviceId}`) es la entrada del QR fijo en `/mesa`: une el
dispositivo a la sesión abierta de la mesa (get-or-create del participante, `AddOrGetParticipantAsync`). Si
no hay ninguna, responde `404` («No hay una mesa abierta con este código…»). Nunca abre sesiones: las abre el
staff con `POST /sessions` (de noche, Recepción entrega el PIN). Antes de buscar la sesión
(`GetOpenByTableCodeAsync`), comprueba, con el modo activo, que el piso de la mesa esté en
`floors.visible_in` y que su fila `table_layouts` tenga `active = true` (la condición de piso solo se evalúa
si la fila del modo activo tiene `floor_id` no nulo); si no, responde:

```json
HTTP 409
{ "title": "TABLE_CLOSED_IN_MODE", "status": 409,
  "detail": "Esta zona está cerrada en el modo actual.",
  "tableCode": "H001", "mode": "NIGHT" }
```
(`ProblemDetails` de `ApiController.Problem`: `title` = código, `detail` = mensaje y las `extensions` al nivel
raíz, igual que `VENUE_NOT_IDLE` en SDD-001 §7.)

Menú (`/mesa`, `mesa/page.tsx`): si `title === "TABLE_CLOSED_IN_MODE"` y `mode === "NIGHT"`, muestra «Esta
zona está cerrada esta noche; acércate a Recepción»; con otro `mode` muestra el `detail`. En los dos casos
**no** pinta el texto fijo «Pide al camarero que abra tu mesa y vuelve a escanear.». El `detail` del backend
sigue siendo el texto de reserva genérico.

La comprobación va **antes** de la lectura de la sesión abierta, de modo que una mesa cerrada en el modo
responde `409 TABLE_CLOSED_IN_MODE` y no `404`. Este endpoint no tiene rate limit propio y PRD-019 no le
añade uno. Se registra como
`"sessions.session.table_closed_in_mode"` `{ tenantId, tableCode, mode }` (warning) y métrica
`sessions.table_closed_in_mode` (counter, label `mode`). Una sesión **ya abierta** en una mesa que luego
queda cerrada en su modo no se expulsa: el cambio de modo exige local vacío, y editar el modo activo con
mesas ocupadas lo bloquea `PATCH /tables/layout`.

Errores de `join-by-table`:

| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | `tenantId` vacío, `tableCode` vacío o > 50, `deviceId` vacío o > 100 (hoy, `JoinByTableValidator`) |
| `NOT_FOUND` | 404 | No hay sesión `OPEN` para ese `tableCode` (hoy) |
| `TABLE_CLOSED_IN_MODE` | 409 | *(diseñado — PRD-019)* La mesa no está en servicio en el modo activo |
| `VENUE_MODE_UNAVAILABLE` | 503 | *(diseñado — PRD-019)* `IVenueModeProvider` lanza `VenueModeUnavailableException` (Redis y Postgres caídos) |

---

## Endpoints — Session Lifecycle

---

### POST /api/v1/sessions

**Objetivo**
Abre una nueva sesión de mesa; invocado por el mesero (modo STAFFED) o por el sistema al escanear el QR (modo SELF_SERVICE).

**Auth**
- Modo `STAFFED`: JWT Staff con rol `Mesero`, `Cajero` o `Admin`
- Modo `SELF_SERVICE`: Sin autenticación requerida (request autenticada con `deviceId` del cliente)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "serviceMode": "STAFFED",
  "qrMode": "FIXED",
  "waiterJwt": "eyJhbGci...",
  "deviceId": "device-uuid-or-fingerprint",
  "shiftId": "shift-uuid-optional",
  "guestCount": 4
}
```
> `guestCount` *(actualizado)*: lo manda Recepción al sentar (RF-1840) y se guarda en `sessions.guest_count`.
> **`qrMode` [v2.x]:** para mesas existentes el campo **se ignora** — el modo lo dicta la mesa (`tables.qr_mode`). Solo se usa al **auto-crear** una mesa ad-hoc inexistente (con `requiresPin = true` por defecto). El cliente/staff ya no lo elige al abrir.

**Response 201**
```json
{
  "sessionId": "session-uuid-new",
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "tableId": "table-uuid-1",
  "status": "OPEN",
  "qrMode": "FIXED",
  "serviceMode": "STAFFED",
  "pinRequired": true,
  "pin": "4821",
  "waiterId": "staff-uuid",
  "openedBy": "staff-uuid",
  "createdAt": "2025-12-01T12:00:00Z",
  "participantId": "participant-uuid-new"
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido
- `tableCode`: requerido, string, debe existir en `tables` para el tenant y estar activo
- `serviceMode`: requerido, `STAFFED` o `SELF_SERVICE`
- `qrMode`: opcional [v2.x] — ignorado para mesas existentes (el modo lo da la mesa); solo usado al auto-crear una mesa
- `qrMode = DYNAMIC` + `serviceMode = SELF_SERVICE`: inválido, retorna 400
- `waiterJwt`: requerido si `serviceMode = STAFFED`; debe ser JWT Staff válido con rol `Mesero`, `Cajero` o `Admin`
- `deviceId`: requerido si `serviceMode = SELF_SERVICE`; string, 1–100 caracteres
- `shiftId`: opcional, UUID v4
- `guestCount` *(actualizado)*: opcional, entero 1–999 (`OpenSessionValidator`)
- *(diseñado — PRD-019)* La mesa debe estar **en servicio en el modo activo**: su piso en `floors.visible_in` y su fila `table_layouts(mode activo).active = true`; si no, `409 TABLE_CLOSED_IN_MODE` (ver §Distribución por modo)

**Reglas de Negocio**
- *(diseñado — PRD-019)* La comprobación de `TABLE_CLOSED_IN_MODE` se hace justo después de resolver la mesa por `tableCode`, antes del rate limit y de la lectura de la sesión abierta. Si la mesa no existe, no se comprueba nada: en `SELF_SERVICE` se responde `400 INVALID_TABLE` como hoy, y en `STAFFED` se auto-crea ya en servicio (con sus dos filas). La condición de piso solo aplica si la fila tiene `floor_id` no nulo
- Antes de crear, verificar `table:{tenantId}:{tableCode}:active` en Redis; si existe, retorna 409 (sesión ya abierta)
- Si Redis no responde, verificar en PostgreSQL: `SELECT id FROM sessions WHERE table_id = $1 AND status = 'OPEN'`
- Se aplica rate limiting: `ratelimit:sessions:{tenantId}:{tableCode}` — máximo 10 requests/min por clave; superar retorna 429
- **Derivación de `qrMode` y `pinRequired`** [v2.x]: ambos se derivan de **la mesa**, no del comando. `qrMode = table.QrMode`; `pinRequired = table.QrMode == FIXED && table.RequiresPin`. Es decir, las sesiones de QR **dinámico** **nunca** exigen PIN (el QR de un solo uso ya autentica la mesa) y las mesas **"sin seguridad"** (`FIXED` + `requires_pin = false`) tampoco; solo las de **QR Fijo con PIN** lo piden. (Antes [v1.3] se derivaba de `tenantSettings.PinRequired`; ahora la config es por mesa — ver ADR-018, PRD-011 v2.1.) Si la mesa no existe, se auto-crea con el `qrMode` del comando y `requiresPin = true`. Ver PRD-002 US-101e / US-119.
  *(diseñado — PRD-019 — ADR-040)* `pinRequired = venueMode == NIGHT || (table.QrMode == FIXED && table.RequiresPin)`, donde `venueMode` es el modo activo de `IVenueModeProvider`, la misma lectura que se congela en `sessions.venue_mode`. En NIGHT el PIN se exige en todas las mesas (RF-1941), también en las DYNAMIC (además del QR dinámico) y en las «sin seguridad». `tables.qr_mode` y `tables.requires_pin` no se modifican. Al abrir se genera el PIN y se reserva en `pins_in_use`, con 12 h. La regla se sostiene porque el cambio de modo exige el local vacío. Orders no cambia: ya exige el PIN cuando `sessions.pin_required = true`.
- Si `pinRequired = true`, generar PIN de 4 dígitos con `SecureRandom`; almacenar en `session:{tenantId}:{sessionId}:pin` con TTL = duración estimada de sesión (hoy 4 horas = 14400 s; *(diseñado — PRD-019)* **12 h = 43200 s**)
- *(diseñado — PRD-019 — ADR-040)* El PIN debe ser **único entre las sesiones abiertas del tenant**: se sortea hasta que `HSETNX pins_in_use:{tenantId} {pin} {sessionId}` devuelva `1` (hash PIN → `sessionId`, con la autorreparación de §Redis Keys; máx. 20 intentos; si se agotan, 500 y log `sessions.pin.pool_exhausted`)
- Si `pinRequired = false`, `pin` en la respuesta es `null`
- **Parseo de enums** [v1.3]: el wire format es screaming-snake (`SELF_SERVICE`), pero los miembros del enum son PascalCase (`SelfService`); se mapean explícitamente (un `Enum.Parse` case-insensitive lanzaba 500 en `FIXED + SELF_SERVICE` por el guion bajo).
- Insertar fila en `sessions` con `status = 'OPEN'`, `guest_count` si llegó *(actualizado)* y *(diseñado — PRD-019)* `venue_mode` = modo activo (`IVenueModeProvider`); Orders sella `orders.venue_mode` con el modo vigente al crear la comanda (SDD-004), que coincide con este
- Registrar el abridor como primer participante en `session_participants` con `is_host = true`
- Si `serviceMode = STAFFED`: `opened_by = waiterJwt.sub`, `waiter_id = waiterJwt.sub`
- Si `serviceMode = SELF_SERVICE`: `opened_by = null`, `waiter_id = null`; el `deviceId` del cliente se registra en `session_participants`
- Almacenar `table:{tenantId}:{tableCode}:active = {sessionId}` en Redis con TTL 14400s
- El PIN solo se retorna en la respuesta de apertura (para que el mesero lo comunique al cliente); no se expone en consultas posteriores de la sesión

**Errores**
| Código | Condición |
|---|---|
| 400 | `serviceMode` o `qrMode` inválidos o combinación `DYNAMIC+SELF_SERVICE` |
| 400 | `tableCode` no existe o mesa inactiva |
| 400 | `tenantId` inválido |
| 401 | `serviceMode = STAFFED` pero `waiterJwt` ausente o inválido |
| 403 | `waiterJwt` válido pero no tiene rol `Mesero`, `Cajero` o `Admin` |
| 404 | Tenant no encontrado |
| 409 | Ya existe una sesión `OPEN` para esa mesa |
| 409 | *(diseñado — PRD-019)* `TABLE_CLOSED_IN_MODE`: la mesa no está en servicio en el modo activo (piso oculto o fila del modo con `active = false`); `extensions: { tableCode, mode }` |
| 429 | Rate limit excedido (> 10 requests/min por tableCode) |
| 503 | *(diseñado — PRD-019)* `VENUE_MODE_UNAVAILABLE`: `IVenueModeProvider` lanza `VenueModeUnavailableException` (Redis y Postgres caídos) |

**Observabilidad**
- Log: `"sessions.session.opened"` `{ tenantId, sessionId, tableCode, serviceMode, qrMode, pinRequired, openedBy, durationMs }` — *(diseñado — PRD-019)* añade `venueMode`
- Log (warning) *(diseñado — PRD-019)*: `"sessions.session.table_closed_in_mode"` `{ tenantId, tableCode, mode }`
- Métrica: `sessions.session_opened` (counter, labels: `serviceMode`, `qrMode`; *(diseñado — PRD-019)* `venueMode`)
- Métrica *(diseñado — PRD-019)*: `sessions.table_closed_in_mode` (counter, label `mode`)

**Casos de Aceptación**
- [ ] Mesero abre sesión STAFFED/FIXED y recibe 201 con PIN si `pinRequired = true`
- [ ] Sistema abre sesión SELF_SERVICE/FIXED sin JWT de mesero y retorna 201
- [ ] Retorna 409 si ya hay sesión OPEN para la mesa
- [ ] Retorna 400 para combinación DYNAMIC + SELF_SERVICE
- [ ] `pin` es `null` en respuesta si la mesa es DYNAMIC o "sin seguridad" (`FIXED` + `requires_pin = false`) **y el modo es RESTAURANT**
- [ ] *(diseñado — PRD-019)* Con el local en NIGHT, abrir una mesa DYNAMIC o FIXED + requires_pin=false → 201 con `pinRequired: true` y `pin` no nulo; un pedido de un participante sin PIN validado → 403 `PIN_REQUIRED`
- [ ] *(diseñado — PRD-019)* Walk-in de caja en NIGHT con un código inexistente → 201, la mesa nace con sus dos filas activas y la siguiente apertura no da 409
- [ ] El `qrMode` enviado en el body se ignora si la mesa existe (gana `tables.qr_mode`)
- [ ] Rate limit retorna 429 tras > 10 requests/min por tableCode
- [ ] Redis `table:{tenantId}:{tableCode}:active` se popula correctamente
- [ ] *(diseñado — PRD-019)* Con el local en `NIGHT`, abrir una hamaca de Playa (`visibleIn: ["RESTAURANT"]`) → 409 `TABLE_CLOSED_IN_MODE`; en `RESTAURANT` → 201
- [ ] *(diseñado — PRD-019)* Dos sesiones abiertas a la vez nunca comparten PIN; la fila nace con `venue_mode` del modo activo y el PIN vive 12 h

---

### GET /api/v1/sessions/by-table

**Objetivo**
Obtiene la sesión activa de una mesa por su código de tabla; usado por el cliente al escanear el QR fijo (modo FIXED) para unirse a una sesión existente.

**Auth**
Sin autenticación requerida.

**Request**
- Path params: Ninguno
- Query params:
  - `tenantId` (requerido) — UUID del tenant
  - `tableCode` (requerido) — string: código de la mesa
- Body: Ninguno

**Response 200**
```json
{
  "sessionId": "session-uuid-1",
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "status": "OPEN",
  "serviceMode": "STAFFED",
  "pinRequired": true,
  "participantCount": 3,
  "createdAt": "2025-12-01T12:00:00Z"
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido
- `tableCode`: requerido, string, 1–50 caracteres

**Reglas de Negocio**
- Se consulta `table:{tenantId}:{tableCode}:active` en Redis para obtener el `sessionId` activo
- En miss de Redis se consulta PostgreSQL: `SELECT * FROM sessions WHERE table_id = (SELECT id FROM tables WHERE tenant_id=$1 AND table_code=$2) AND status='OPEN'`
- Si no hay sesión abierta, retorna 404 con un mensaje descriptivo para que la UI muestre "Escanea el QR cuando el mesero abra tu mesa"
- El PIN no se retorna en este endpoint; solo se indica si es requerido (`pinRequired`)
- *(diseñado — PRD-019)* `pinRequired` sale de `session.PinRequired`, no de `settings?.PinRequired ?? session.PinRequired` (`GetSessionByTableQuery.cs`; el tenant El local tiene `pin_required = false`)
- `participantCount` proviene de `COUNT(*)` en `session_participants` para la sesión

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` o `tableCode` ausentes o `tenantId` no es UUID v4 |
| 404 | No hay sesión `OPEN` para ese `tableCode` en el tenant |
| 404 | `tableCode` no existe para el tenant |

**Observabilidad**
- Log: `"sessions.client.session_by_table"` `{ tenantId, tableCode, sessionId, found: bool }`
- Métrica: `sessions.client_session_by_table` (counter)

**Casos de Aceptación**
- [ ] Retorna 200 con datos de sesión activa cuando existe una sesión OPEN
- [ ] Retorna 404 si no hay sesión OPEN para la mesa
- [ ] No expone el PIN en la respuesta
- [ ] Retorna 400 si `tenantId` o `tableCode` están ausentes

---

### POST /api/v1/sessions/join

**Objetivo**
Une a un participante (cliente) a una sesión existente, ya sea mediante un token de QR dinámico o mediante un token de invite link.

**Auth**
Sin autenticación requerida (la identidad del cliente se establece por `deviceId` y opcionalmente `customerAccountJwt`).

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "tenant-uuid",
  "deviceId": "device-fingerprint-string",
  "joinToken": "eyJhbGci...",
  "tokenType": "DYNAMIC_QR",
  "customerAccountJwt": "eyJhbGci...(opcional)"
}
```
- `tokenType`: `DYNAMIC_QR` o `INVITE`
- Para QR dinámico, `joinToken` es el JWT del QR almacenado en `qr:{tenantId}:{sessionId}:token`
- Para invite link, `joinToken` es el JWT almacenado en `invite:{tenantId}:{sessionId}:token`

**Response 200**
```json
{
  "participantId": "participant-uuid-new",
  "sessionId": "session-uuid-1",
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "isHost": false,
  "pinRequired": true,
  "pinValidated": false,
  "serviceMode": "STAFFED",
  "orderedItems": []
}
```

**Validaciones**
- `tenantId`: requerido, UUID v4 válido
- `deviceId`: requerido, string, 1–100 caracteres
- `joinToken`: requerido, JWT RS256 válido y no expirado
- `tokenType`: requerido, `DYNAMIC_QR` o `INVITE`
- Para `DYNAMIC_QR`: el JWT debe contener `{ sessionId, tenantId, type: "dynamic_qr" }`; verificar contra clave `qr:{tenantId}:{sessionId}:token` en Redis
- Para `INVITE`: el JWT debe contener `{ sessionId, tenantId, type: "invite" }`; verificar contra clave `invite:{tenantId}:{sessionId}:token` en Redis
- `customerAccountJwt`: opcional; si se provee, debe ser JWT válido de cuenta de cliente

**Reglas de Negocio**
- Verificar que la sesión exista y tenga `status = OPEN` en PostgreSQL
- Si el `deviceId` ya existe como participante en la sesión (join duplicado), retorna idempotentemente los datos del participante existente con 200
- Insertar nuevo registro en `session_participants` con `is_host = false`, `pin_validated = false`
- Si `customerAccountJwt` es válido, asociar `customer_account_id` al participante
- *(diseñado — PRD-019 — ADR-040)* Con token `INVITE`, el participante nuevo se marca `pin_validated = true` **solo si el modo activo no es `NIGHT`** (`inviteGrantsPin = isInviteJoin && session.PinRequired && mode != NIGHT`); en NIGHT la respuesta lleva `pinValidated: false` y el PIN se le pide en el registro (nombre + PIN), como a cualquiera (ver §Invite link)
- `pinValidated` en respuesta se obtiene de `pin_validated:{tenantId}:{sessionId}:{participantId}` en Redis — *(diseñado — PRD-019)* con fallback a `session_participants.pin_validated` y re-siembra de la clave con TTL 12 h si falta
- `orderedItems` contiene los ítems ya pedidos en la sesión (resumen desde el módulo Orders); si la sesión no tiene pedidos, retorna `[]`

**Errores**
| Código | Condición |
|---|---|
| 400 | `deviceId` ausente o excede 100 caracteres |
| 400 | `tokenType` inválido |
| 401 | `joinToken` ausente, inválido o expirado |
| 401 | Token no coincide con el valor almacenado en Redis (token revocado o reemplazado) |
| 404 | Sesión no encontrada o con `status = CLOSED` |
| 409 | Sesión cerrada (status CLOSED) |

**Observabilidad**
- Log: `"sessions.client.session_join"` `{ tenantId, sessionId, participantId, tokenType, deviceId, isDuplicate }`
- Métrica: `sessions.participant_joined` (counter, label: `tokenType`)

**Casos de Aceptación**
- [ ] Cliente se une con token DYNAMIC_QR válido y retorna 200 con `participantId`
- [ ] Cliente se une con token INVITE válido y retorna 200
- [ ] Join duplicado (mismo `deviceId`) retorna 200 idempotentemente con los datos existentes
- [ ] Retorna 401 si el `joinToken` está expirado
- [ ] Retorna 404 si la sesión tiene `status = CLOSED`
- [ ] `orderedItems` refleja los ítems ya pedidos en la sesión

---

### POST /api/v1/sessions/{sessionId}/participants

**Objetivo**
Agrega un participante a una sesión ya abierta sin requerir un token de QR, usado internamente o por staff para asociar un dispositivo adicional.

**Auth**
JWT Staff con rol `Mesero`, `Cajero` o `Admin`, o JWT Platform Admin.

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params: Ninguno
- Body:
```json
{
  "deviceId": "device-fingerprint-string",
  "customerAccountId": "customer-uuid-optional",
  "isHost": false
}
```

**Response 201**
```json
{
  "participantId": "participant-uuid-new",
  "sessionId": "session-uuid-1",
  "deviceId": "device-fingerprint-string",
  "customerAccountId": null,
  "isHost": false,
  "pinValidated": true,
  "joinedAt": "2025-12-01T12:05:00Z"
}
```

**Validaciones**
- `sessionId`: UUID v4 válido, sesión debe existir con `status = OPEN` y pertenecer al tenant del JWT
- `deviceId`: requerido, string, 1–100 caracteres
- `customerAccountId`: opcional, UUID v4 válido si se provee; debe existir en `customer_accounts`
- `isHost`: opcional, boolean, default `false`

**Reglas de Negocio**
- Si ya existe un participante con el mismo `deviceId` en la sesión, retorna 200 con los datos del participante existente (idempotente)
- Solo puede existir un participante con `is_host = true` por sesión; si se intenta crear un segundo host, retorna 400
- El Staff que invoca este endpoint puede añadir participantes sin limitación de número
- *(actualizado)* El participante creado por staff es de confianza y queda con el PIN validado; la respuesta lleva `pinValidated: true`. Hoy eso solo se guarda en Redis (4 h). *(diseñado — PRD-019 — ADR-040)* También se escribe `session_participants.pin_validated = true` y la clave pasa a durar 12 h (ver §Redis Keys, «PIN de noche»)

**Errores**
| Código | Condición |
|---|---|
| 400 | `deviceId` ausente o excede 100 caracteres |
| 400 | Se intenta asignar `isHost: true` pero ya existe un host en la sesión |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero la sesión pertenece a otro tenant |
| 404 | `sessionId` no encontrado o sesión cerrada |
| 404 | `customerAccountId` no existe |

**Observabilidad**
- Log: `"sessions.staff.participant_added"` `{ tenantId, sessionId, participantId, deviceId, requesterId }`
- Métrica: `sessions.staff_participant_added` (counter)

**Casos de Aceptación**
- [ ] Staff agrega participante correctamente y retorna 201
- [ ] Agregar el mismo `deviceId` retorna 200 idempotentemente
- [ ] Retorna 400 al intentar agregar segundo host
- [ ] Retorna 404 si la sesión está cerrada
- [ ] Retorna 401/403 según corresponda

---

### GET /api/v1/sessions/{sessionId}

**Objetivo**
Retorna los detalles completos de una sesión incluyendo participantes y resumen de órdenes, usado tanto por staff como por el cliente participante.

**Auth**
- Staff: JWT con cualquier rol del tenant (`Mesero`, `Cajero`, `Admin`) o Platform Admin
- Cliente participante: identificado por `deviceId` como query param (sin JWT); el `deviceId` debe pertenecer a un participante de la sesión

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params:
  - `deviceId` (condicional) — string: requerido si el caller no provee JWT de Staff
- Body: Ninguno

**Response 200**
```json
{
  "sessionId": "session-uuid-1",
  "tenantId": "tenant-uuid",
  "tableCode": "M01",
  "tableId": "table-uuid-1",
  "status": "OPEN",
  "qrMode": "FIXED",
  "serviceMode": "STAFFED",
  "pinRequired": true,
  "waiterId": "staff-uuid",
  "waiterName": "Carlos M.",
  "createdAt": "2025-12-01T12:00:00Z",
  "closedAt": null,
  "participants": [
    {
      "participantId": "participant-uuid-1",
      "deviceId": "device-abc",
      "isHost": true,
      "pinValidated": true,
      "joinedAt": "2025-12-01T12:00:00Z"
    },
    {
      "participantId": "participant-uuid-2",
      "deviceId": "device-def",
      "isHost": false,
      "pinValidated": false,
      "joinedAt": "2025-12-01T12:05:00Z"
    }
  ],
  "ordersSummary": {
    "totalItems": 4,
    "totalCents": 4800,
    "pendingItems": 1,
    "deliveredItems": 3
  }
}
```

**Validaciones**
- `sessionId`: UUID v4 válido
- Si no se provee JWT de Staff, `deviceId` es requerido y debe existir en `session_participants` para esta sesión

**Reglas de Negocio**
- Si el caller es cliente (autenticado por `deviceId`), se excluye el `deviceId` de otros participantes en la respuesta (privacidad); se muestra únicamente `participantId`, `isHost`, `pinValidated`, `joinedAt`
- Si el caller es Staff, se retorna la información completa incluyendo `deviceId` de todos los participantes
- `pinValidated` por participante se obtiene de `pin_validated:{tenantId}:{sessionId}:{participantId}` en Redis; *(diseñado — PRD-019)* si la clave `pin_validated` falta o Redis falla, se lee `session_participants.pin_validated`; si es true, se re-siembra la clave con TTL 12 h (best-effort)
- `ordersSummary` se obtiene consultando el módulo Orders (llamada interna al servicio Orders o query directa a la tabla `orders` si está en el mismo servicio)
- `waiterName` se resuelve con lookup en `staff` por `waiterId`

**Errores**
| Código | Condición |
|---|---|
| 400 | Ni JWT de Staff ni `deviceId` provistos |
| 401 | JWT de Staff inválido o expirado |
| 403 | JWT válido pero la sesión pertenece a otro tenant |
| 403 | `deviceId` no pertenece a ningún participante de la sesión |
| 404 | `sessionId` no encontrado |

**Observabilidad**
- Log: `"sessions.session.get"` `{ tenantId, sessionId, callerType: "staff"|"client", requesterId }`
- Métrica: `sessions.session_get` (counter, label: `callerType`)

**Casos de Aceptación**
- [ ] Staff obtiene detalles completos de la sesión incluyendo `deviceId` de participantes
- [ ] Cliente con `deviceId` válido obtiene sesión sin `deviceId` de otros participantes
- [ ] `pinValidated` refleja el estado correcto de Redis para cada participante
- [ ] Retorna 403 si el `deviceId` no pertenece a la sesión
- [ ] Retorna 404 si `sessionId` no existe
- [ ] `ordersSummary` refleja el estado actual de las órdenes

---

### POST /api/v1/sessions/{sessionId}/pin/regenerate

**Objetivo**
Regenera el PIN de una sesión activa y notifica al grupo de staff vía SignalR; solo accesible para Cajero.

**Auth**
JWT Staff con rol `Cajero` o JWT Platform Admin.

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "sessionId": "session-uuid-1",
  "newPin": "7354",
  "regeneratedAt": "2025-12-01T13:00:00Z"
}
```

**Validaciones**
- `sessionId`: UUID v4 válido, sesión debe existir con `status = OPEN` y pertenecer al tenant del JWT

**Reglas de Negocio**
- Generar nuevo PIN de 4 dígitos con `SecureRandom`
- `SET session:{tenantId}:{sessionId}:pin {newPin}` en Redis, manteniendo el TTL existente (usar `KEEPTTL` o recalcular TTL restante); si la clave ya caducó, hoy se escribe con 4 h (`SessionPinService.cs:55`) y *(diseñado — PRD-019)* pasa a `PinTtl` (12 h). *(diseñado — PRD-019)* El nuevo PIN se sortea contra `pins_in_use:{tenantId}`: libera el PIN anterior por `sessionId` en el hash `pins_in_use` (sin leer la clave del PIN, que puede haber caducado) y reserva el nuevo con `HSETNX`, para que siga siendo único entre sesiones abiertas
- No se resetea `pin_validated` para participantes que ya habían validado el PIN anterior; los ya validados permanecen validados
- Participantes que aún no habían validado deberán usar el nuevo PIN en su próxima validación
- Enviar evento SignalR al grupo `waiter-{tenantId}` con payload `{ type: "PIN_REGENERATED", sessionId, tableCode }` (no se incluye el PIN en el broadcast de SignalR para seguridad; el Cajero lo ve en la respuesta HTTP)
- El nuevo PIN se retorna únicamente en el cuerpo de esta respuesta HTTP

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero rol no es `Cajero` o Platform Admin |
| 403 | La sesión pertenece a otro tenant |
| 404 | `sessionId` no encontrado o sesión con `status = CLOSED` |
| 503 | Redis inaccesible |

**Observabilidad**
- Log: `"sessions.cajero.pin_regenerated"` `{ tenantId, sessionId, tableCode, requesterId }`
- Métrica: `sessions.pin_regenerated` (counter)

**Casos de Aceptación**
- [ ] Cajero regenera PIN y recibe nuevo PIN de 4 dígitos en respuesta 200
- [ ] El PIN en Redis se actualiza al nuevo valor
- [ ] Participantes ya validados mantienen `pin_validated = true` tras regeneración
- [ ] Se emite evento SignalR al grupo `waiter-{tenantId}` sin exponer el PIN
- [ ] Retorna 403 si el rol del JWT es `Mesero` o `Admin` (no Cajero)
- [ ] Retorna 404 si la sesión está cerrada
- [ ] Retorna 401/403 según corresponda

---

### POST /api/v1/sessions/{sessionId}/close

**Objetivo**
Cierra una sesión de mesa después del pago, marcándola como `CLOSED` y liberando la mesa.

**Auth**
JWT Staff con rol `Cajero` o JWT Platform Admin.

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params: Ninguno
- Body:
```json
{
  "closureReason": "PAYMENT_COMPLETED"
}
```
- `closureReason`: `PAYMENT_COMPLETED` | `MANUAL_CLOSE` | `SYSTEM_TIMEOUT`

**Response 200**
```json
{
  "sessionId": "session-uuid-1",
  "status": "CLOSED",
  "closedAt": "2025-12-01T14:30:00Z",
  "closedBy": "staff-uuid",
  "tableCode": "M01",
  "duration": 5400
}
```
- `duration`: segundos entre `created_at` y `closed_at`

**Validaciones**
- `sessionId`: UUID v4 válido, sesión debe existir con `status = OPEN` y pertenecer al tenant del JWT
- `closureReason`: requerido, uno de los valores enumerados

**Reglas de Negocio**
- Verificar que no haya órdenes con `status = PENDING` o `status = IN_PREPARATION` para la sesión; si las hay, retorna 409 (no se puede cerrar con pedidos activos sin confirmar)
- Actualizar `sessions` en PostgreSQL: `status = 'CLOSED'`, `closed_at = now()`, `closed_by = JWT.sub`
- Eliminar clave Redis `table:{tenantId}:{tableCode}:active`
- Eliminar clave Redis `session:{tenantId}:{sessionId}:pin`; *(diseñado — PRD-019)* `HDEL` en `pins_in_use:{tenantId}` de los campos cuyo valor es este `sessionId` (no depende de `session:{t}:{s}:pin`) para liberar el PIN
- Eliminar clave Redis `qr:{tenantId}:{sessionId}:token` (si modo DYNAMIC)
- Eliminar clave Redis `invite:{tenantId}:{sessionId}:token`
- Emitir evento SignalR al grupo `waiter-{tenantId}`: `{ type: "SESSION_CLOSED", sessionId, tableCode }`
- La eliminación de claves Redis es best-effort; si Redis no responde se hace log de warning pero el cierre en PostgreSQL se confirma

**Errores**
| Código | Condición |
|---|---|
| 400 | `closureReason` ausente o valor inválido |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero rol no es `Cajero` o Platform Admin |
| 403 | La sesión pertenece a otro tenant |
| 404 | `sessionId` no encontrado o sesión ya `CLOSED` |
| 409 | Existen órdenes activas (`PENDING` o `IN_PREPARATION`) en la sesión |

**Observabilidad**
- Log: `"sessions.cajero.session_closed"` `{ tenantId, sessionId, tableCode, closureReason, duration, closedBy }`
- Log (warning): `"sessions.cajero.session_close_redis_cleanup_failed"` `{ tenantId, sessionId, error }`
- Métrica: `sessions.session_closed` (counter, label: `closureReason`)
- Métrica: `sessions.session_duration_seconds` (histogram, label: `serviceMode`)

**Casos de Aceptación**
- [ ] Cierra sesión con `PAYMENT_COMPLETED` y retorna 200 con `status: CLOSED`
- [ ] Clave Redis `table:{tenantId}:{tableCode}:active` eliminada tras el cierre
- [ ] Evento SignalR emitido al grupo de waiters
- [ ] Retorna 409 si hay órdenes con `status = PENDING`
- [ ] Retorna 404 si la sesión ya está `CLOSED`
- [ ] Retorna 403 si el rol del JWT es `Mesero` o `Admin`
- [ ] Cierre se confirma en PostgreSQL aunque Redis falle (con log de warning)

---

### PATCH /api/v1/sessions/{sessionId}

**Objetivo**
Actualiza la asignación de mesero de una sesión activa.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params: Ninguno
- Body:
```json
{
  "waiterId": "staff-uuid-nuevo"
}
```

**Response 200**
```json
{
  "sessionId": "session-uuid-1",
  "waiterId": "staff-uuid-nuevo",
  "waiterName": "María López",
  "updatedAt": "2025-12-01T13:30:00Z"
}
```

**Validaciones**
- `sessionId`: UUID v4 válido, sesión debe existir con `status = OPEN` y pertenecer al tenant del JWT
- `waiterId`: requerido, UUID v4 válido; debe existir en `staff` con rol `Mesero` y pertenecer al mismo tenant; enviar `null` para desasignar mesero

**Reglas de Negocio**
- Actualizar `waiter_id` en la fila de `sessions` en PostgreSQL
- Emitir evento SignalR al grupo `waiter-{tenantId}`: `{ type: "WAITER_REASSIGNED", sessionId, tableCode, newWaiterId }`
- Si `waiterId = null`, se desasigna el mesero (`waiter_id = null`); `waiterName` en respuesta es `null`

**Errores**
| Código | Condición |
|---|---|
| 400 | `waiterId` no es UUID v4 válido (cuando no es null) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero rol no es `Admin` o Platform Admin |
| 403 | La sesión pertenece a otro tenant |
| 404 | `sessionId` no encontrado o sesión `CLOSED` |
| 404 | `waiterId` no existe en `staff` del tenant o no tiene rol `Mesero` |

**Observabilidad**
- Log: `"sessions.admin.waiter_reassigned"` `{ tenantId, sessionId, previousWaiterId, newWaiterId, requesterId }`
- Métrica: `sessions.waiter_reassigned` (counter)

**Casos de Aceptación**
- [ ] Admin reasigna mesero y retorna 200 con `waiterName` del nuevo mesero
- [ ] Desasignación con `waiterId: null` retorna 200 con `waiterId: null` y `waiterName: null`
- [ ] Evento SignalR emitido correctamente al grupo de waiters
- [ ] Retorna 404 si el `waiterId` no pertenece al tenant o no tiene rol `Mesero`
- [ ] Retorna 403 si el JWT es de un rol Mesero o Cajero
- [ ] Retorna 404 si la sesión está cerrada

---

## Endpoints — PIN Validation

---

### POST /api/v1/sessions/{sessionId}/pin/validate

**Objetivo**
Valida el PIN de sesión ingresado por un participante cliente antes de confirmar su primer pedido o, en NIGHT, en el registro inicial.

**Auth**
Sin autenticación requerida (identificación por `deviceId` y `participantId`).

**Request**
- Path params: `{sessionId}` — UUID de la sesión
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "tenant-uuid",
  "participantId": "participant-uuid",
  "deviceId": "device-fingerprint-string",
  "pin": "4821"
}
```

**Response 200**
```json
{
  "validated": true,
  "participantId": "participant-uuid",
  "sessionId": "session-uuid-1"
}
```

**Response 200 (validación fallida — no lanza excepción HTTP para no exponer lógica de intentos)**
```json
{
  "validated": false,
  "remainingAttempts": 3,
  "participantId": "participant-uuid",
  "sessionId": "session-uuid-1"
}
```

**Validaciones**
- `sessionId`: UUID v4 válido, sesión debe existir con `status = OPEN`
- `tenantId`: requerido, UUID v4 válido; debe coincidir con el `tenant_id` de la sesión
- `participantId`: requerido, UUID v4 válido; debe existir en `session_participants` para esta sesión
- `deviceId`: requerido, string, 1–100 caracteres; debe coincidir con el `device_id` del participante en DB
- `pin`: requerido, string de exactamente 4 dígitos numéricos

**Reglas de Negocio**
- Antes de validar, verificar contador de intentos: `INCR pin_attempts:{tenantId}:{tableCode}:{deviceId}`; si el resultado > 5, retornar 423 inmediatamente sin verificar el PIN
- Si el intento es el primero (clave no existía), establecer TTL de 900 segundos (15 min) sobre la clave con `EXPIRE`
- Obtener el PIN correcto de `session:{tenantId}:{sessionId}:pin` en Redis
- Comparar con tiempo constante (evitar timing attacks): `CryptographicOperations.FixedTimeEquals`
- Si el PIN es correcto:
  - `SET pin_validated:{tenantId}:{sessionId}:{participantId} "1"` manteniendo TTL de sesión (hoy 4 h; *(diseñado — PRD-019)* 12 h)
  - Actualizar `session_participants` en PostgreSQL: `pin_validated = true` — *(diseñado — PRD-019)* esta columna es la **verdad**; si la clave Redis caduca o se pierde, Orders, `GET /sessions/{id}` y `join-by-table` la releen y re-siembran la clave con TTL fijo de 12 h en vez de volver a pedir el PIN
  - Responder con `{ validated: true }`
  - No resetear el contador de intentos (se puede dejar expirar naturalmente)
- Si el PIN es incorrecto:
  - No actualizar DB ni Redis (excepto el INCR del contador que ya ocurrió)
  - Responder con `{ validated: false, remainingAttempts: 5 - currentCount }`
  - Si `remainingAttempts = 0` (intento 5 fallido), responder 423 en la siguiente request

**Errores**
| Código | Condición |
|---|---|
| 400 | `pin` ausente, no es string de 4 dígitos, o contiene no-numéricos |
| 400 | `deviceId` no coincide con el del participante en DB |
| 400 | `tenantId` no coincide con el tenant de la sesión |
| 404 | `sessionId` no encontrado o sesión `CLOSED` |
| 404 | `participantId` no existe en la sesión |
| 422 | PIN ya fue validado para este participante (`pin_validated = true`) |
| 423 | Límite de 5 intentos fallidos alcanzado; bloqueado por 15 min |

**Observabilidad**
- Log: `"sessions.client.pin_validated"` `{ tenantId, sessionId, participantId, success: true }`
- Log: `"sessions.client.pin_failed"` `{ tenantId, sessionId, participantId, attempt: N }`
- Log: `"sessions.client.pin_locked"` `{ tenantId, sessionId, tableCode, deviceId }`
- Métrica: `sessions.pin_validated` (counter)
- Métrica: `sessions.pin_failed` (counter)
- Métrica: `sessions.pin_locked` (counter)

**Casos de Aceptación**
- [ ] Cliente ingresa PIN correcto y recibe `{ validated: true }` con 200
- [ ] `pin_validated` en Redis y PostgreSQL se marcan como `true` tras validación exitosa
- [ ] Cliente ingresa PIN incorrecto y recibe `{ validated: false, remainingAttempts: N }` con 200
- [ ] Al quinto intento fallido, la siguiente request retorna 423 sin verificar el PIN
- [ ] Bloqueo expira en 15 min y el participante puede reintentar
- [ ] Retorna 422 si el participante ya validó el PIN previamente
- [ ] Retorna 404 si la sesión está cerrada
- [ ] Comparación de PIN usa tiempo constante (sin vulnerabilidad de timing)
