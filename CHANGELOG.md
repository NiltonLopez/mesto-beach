# Changelog

Qué ha cambiado en el sistema, por qué, y qué se decidió al hacerlo.

Una entrada por PR, la más reciente arriba. Se conservan **al menos las tres últimas**: es el sitio
donde mirar para ponerse al día sin reconstruirlo del historial de git.

---
## Transición de modo rehecha y ajustes de interfaz

**Rama:** `feat/transicion-night` · octubre de 2026

- La transición «agujero negro» cae en espiral hacia un anillo que se cierra, muestra la marca del
  modo de destino y reabre el local girando al revés. Solo anima `transform` y `opacity` en la caída:
  el desenfoque a pantalla completa daba tirones.
- Gana siempre el último destino pedido: un segundo cambio a mitad de animación ya no deja el modo
  equivocado.
- Admin: interruptor en dos líneas, sin historial en el diálogo (sigue auditado en el backend) y
  aviso de distribución como franja. Admin y Staff: cajón móvil con entrada y salida animadas.

---
## Staff en modo Night: venue en vivo, KDS por perfil de flujo y runner con avisos (PR8)

**Rama:** `feat/staff-night` · octubre de 2026

PRD-019 (US-1905, US-1906, US-1907, US-1913; RF-192x, RF-195x), ADR-037 y ADR-038. Solo `frontend/apps/staff`.

### Qué cambia

- **VenueProvider conectado al backend** (`lib/venue.tsx`): `GET /api/v1/venue` al montar (modo +
  estaciones con `flowProfile`/`isOn`, expuestas como `stations`) y conexión al OrdersHub con
  `JoinTenantGroup`; un `OrderEvent` `VENUE_MODE_CHANGED` lanza `runTransition` con la relectura bajo el
  velo. Si falla la red se conserva el último modo; `localStorage` queda como caché anti-destello.
- **KDS por perfil** (`lib/kds.ts`, `board`, tarjeta y foco): `FULL_PASS` mantiene las cuatro columnas;
  `BAR` tiene «Pedidos» (acción Listo directa) y «Listo», con `columnOf(status, 'BAR')` y recorrido de dos
  pasos. Estación elegida persistida en `kds_station:{tenantId}` (por defecto la asignada). Estación
  apagada: aviso «Cerrada en este modo — no acepta pedidos nuevos» y se sigue viendo lo que le quede.
- **Reservados**: pastilla «Reservado» (`ReservadoPill`) y orden por `priority` desc + `createdAt` en cliente.
- **Alerta de pedido nuevo**: borde dorado fijo durante 3 s (sin parpadeo) y contador «sin atender».
- **Runner**: platos de cada parada agrupados por comensal («Pregunta por: …» o «Comanda de sala · …»),
  pastilla Reservado y, en NIGHT, panel de avisos de mesa en vivo (SessionsHub + `JoinWaiterGroup`).
- **PDA**: faltaba `JoinWaiterGroup` tras conectar al SessionsHub; sin él no llegaban avisos en vivo.
- `AvisosPanel` y su CSS pasan a `components/` (scope `.avisos-root`); `pitido` a `lib/avisos.ts`; nuevo
  hook `lib/useAvisos.ts`.

---
## Prioridad de reservados y nombre en el manifiesto del runner (PR5a)

**Rama:** `feat/prioridad-y-nombre-runner` · octubre de 2026

PRD-019, ADR-038; SDD-004 v2.2 y SDD-003 (§`GET /sessions/calls`).

### Qué cambia

- **Migración Orders `AddOrderVenueModeAndPriority`** (generada, **no aplicada**): `orders.priority int
  default 0`, `orders.venue_mode varchar(16) default 'RESTAURANT'` e índice `(tenant_id, venue_mode,
  created_at)`. Los históricos quedan `RESTAURANT` / `0`.
- **Snapshot al crear la comanda** (`CreateOrderHandler`): `priority = 1` solo si el modo es `NIGHT` y la
  mesa es `RESERVADO` (nuevo read model `tables.kind` en Orders, excluido de migraciones); `venue_mode` con
  la única lectura de modo que ya hacía el handler. No se recalculan. El log `order.created` gana
  `VenueMode` y `Priority`.
- **Colas por prioridad**: `GET /orders/active` sale por `priority DESC, created_at ASC` y trae `priority` y
  `venueMode`; `GET /delivery-queue` por `priority DESC, ready_at ASC` y trae `priority`.
- **Manifiesto del runner**: `TripManifestRow` / `ListManifestAsync` / `DeliveryTripStopItemDto` ganan
  `customerName` y `priority`; las paradas salen por `priority DESC` y luego `tableCode`. El armado de
  paradas se unifica en `CreateDeliveryTripHandler.BuildStops` (201 de `POST /delivery-trips` y
  `GET /delivery-trips/mine`). La app del runner rotula cada plato «Pregunta por: {nombre}» o, si es null,
  «Comanda de sala · {tableCode}».
- **Avisos**: `GET /sessions/calls` trae `tableKind` (resuelto al listar desde `tables.kind`); solo en
  `NIGHT` la lista sale con los reservados primero y luego por `createdAt`. `AvisosPanel` (PDA/Runner)
  aplica la misma precedencia solo en `NIGHT`.
- **`@mestobeach/api-client`** actualizado a mano (campos opcionales al final).

### Qué se decidió

- Si el modo no se puede leer al listar avisos, se sirve el orden por llegada (la lista no se cae).
- La clase de mesa solo se consulta de noche: de día no hay consulta extra al crear la comanda.

---


## PR7: panel de administración para Night

**Rama:** `feat/admin-night` · octubre de 2026

Frontend de `apps/admin` para PRD-019 (US-1901, US-1902, US-1910; RF-1902..1907, RF-1917, RF-1918, RF-1931..1938)
sobre los endpoints ya hechos. `@mestobeach/api-client` ya tenía todos los métodos: no se tocó.

### Qué cambia

- **Interruptor de modo** (`VenueSwitch`) al pie de la barra lateral, en lugar del botón de demo. Diálogo propio:
  pre-flight (`GET /venue/preflight`) con mesas abiertas y platos huérfanos, cuenta atrás de 5 s que habilita
  «Cambiar», `POST /venue/mode` + animación, e historial. Solo para Admin.
- **`VenueProvider`** lee el modo real (`GET /venue`) al montar y al volver a primer plano; ante error o 503
  conserva el último modo conocido. `localStorage` queda como caché del anti-destello.
- **Mesas**: segmentado «Restaurante | Night» independiente del modo activo (pastilla «activo ahora»), borrador
  por modo con aviso propio, «En servicio en este modo», barra por mesa en Night (solo estaciones BAR encendidas
  en Night), tipo de mesa e interruptor «visible de noche» por piso.
- **Apariencia**: pestaña «Día | Night» con la vista previa existente y «Heredar del día» por campo; fuente
  `mestobeach` y acento #AA8453.
- **Catálogo**: chips «Día / Noche» por producto y categoría y filtro «Ver: Todo | Día | Noche».
- **Estaciones KDS**: perfil de flujo, activa y modos en que se enciende.

### Decisiones

- Sin tiempo real del cambio de modo en admin (sin SignalR todavía): se relee al enfocar la pestaña.
- El pre-flight de un 409 `VENUE_NOT_IDLE` sustituye al mostrado en el diálogo.

---

## Menú: modo del local en vivo, visita por dispositivo y errores de modo

**Rama:** `feat/menu-night` · octubre de 2026

PR9 de PRD-019 (US-1903, US-1904, US-1912, RF-191x, RF-194x; ADR-040/041). Solo `frontend/apps/menu`.

### Qué cambia

- **`VenueProvider` conectado al backend**: sondea `GET /api/v1/venue/{tenantId}` cada 30 s y al volver a la pestaña. El último modo se guarda en `mestobeach_venue_mode:{tenantId}` (más la clave global, que lee el script anti-destello). Al cambiar: `runTransition` -> recarga del catálogo -> `applyMenuBranding` con el branding que ya resolvió el servidor -> el carrito pierde lo que ya no está visible (toast).
- **Visita por dispositivo** (`lib/visit.ts`): `localStorage mestobeach_visit:{tenantId}:{tableCode}`. `storeSession`/`isIdentified`/`markIdentified`/`getDisplayName` mantienen su API y pasan a esa capa. En `/mesa`, si el join devuelve la misma sesión y participante se salta la identificación; si falta lo local se rehidrata el nombre con `GET /sessions/{id}?deviceId=`.
- **Errores del backend** (`lib/errors.ts`, ProblemDetails con el código en `title`): `TABLE_CLOSED_IN_MODE` («Esta zona está cerrada esta noche; acércate a Recepción», sin la frase de abrir la mesa), `ITEM_NOT_IN_MODE` (quita esos `itemIds` del carrito), `NAME_REQUIRED` (abre la identificación), `VENUE_MODE_UNAVAILABLE`.
- **«Repetir lo último»** en la carta: re-añade el último pedido del participante, solo lo visible y disponible.
- **PinModal**: el keyframe `slideUp` no existía; ahora es `m-slide-up` y respeta `prefers-reduced-motion`.

### Qué se decidió

- De noche el nombre es obligatorio antes del primer pedido: se comprueba en cliente y el servidor lo confirma con `NAME_REQUIRED`.
- El PIN sigue siendo autoridad del servidor: `pinValidated` del join se guarda en la visita, pero el modal solo se abre ante `PIN_REQUIRED`.
- «Repetir lo último» omite los productos con grupos de opciones (el pedido solo guarda el resumen en texto, no los ids de opción) y lo cuenta en el toast.

---

## Responsividad móvil P1 y P2 en staff y admin

**Rama:** `fix/responsive-movil` · octubre de 2026

Primer bloque del workstream de responsividad de PRD-019 (RF-1981, RF-1982, RT-1921, ARCHITECTURE §15.5).
Solo frontend; no cambia contratos ni el aspecto de escritorio salvo lo necesario.

### Qué cambia

- **`StaffShell`**: `h-[100dvh]`; el cajón móvil se cierra con Esc, recibe el foco en el botón de cierre
  (44 px) y respeta safe-area.
- **`runner`**: la barra de acción pasa de `fixed` a `sticky` dentro de la columna de contenido (ya no tapa
  el pie ni invade la barra lateral en md+), con safe-area y select de estación de 44 px.
- **`pda` + `ComandaDrawer`**: bajo `lg` el panel de mesa es un bottom-sheet (`max-h-[85dvh]`); botones de
  44 px; pie del cajón más compacto (observación en una fila, total y botón en la misma línea).
- **`admin` layout**: el aside se oculta bajo `md`; hamburguesa y cajón con Esc/foco, mismo patrón que staff.
- **`mesas`**: panel lateral como bottom-sheet bajo `lg`; paleta y controles de 44 px. **`catalog`**: `Modal`
  con `max-h-[90dvh]` y scroll; los botones de reordenar pasan a 44 px en táctil.
- **`analytics` y `payments`**: tablas con `overflow-x-auto`.

### Qué se decidió

- Los botones de reordenar categorías ya existían; solo se agranda el área táctil con `pointer:coarse`
  para no cambiar el escritorio.
- Quedan para P2/P3 pendientes: pinch-zoom de `ScaledFloorMap` y `Dialog`.

---

## Night PR10: animación «agujero negro» entre modos (solo frontend)

**Rama:** `feat/animacion-agujero-negro` (desde `feat/tema-night-tokens`) · octubre de 2026

### Qué cambia

- **`VenueTransition`** por app (staff, admin, menu), pintada por portal en `document.body`. El proveedor envuelve la app en `#venue-root` y expone `runTransition(nextMode, work?)`, `phase` y `transitioning` (ya real).
- Tres fases (ADR-039): colapso 420 ms (`clip-path` a `circle(0%)`, desenfoque 6px, brillo .4, escala .92 y giro -2deg; velo oscuro en los últimos 150 ms), singularidad de mín. 600 ms y tope 4 s para `work` (punto dorado #AA8453 con halo; aquí cambia `data-venue`) y emersión 600 ms con 16 partículas radiales. Keyframes en `globals.css`, sin librerías.
- `prefers-reduced-motion`: fundido de 200 ms sin desenfoque, punto ni partículas.
- **Admin**: botón TEMPORAL «Probar transición Night» al pie de la barra lateral (`// TEMPORAL hasta PR7`), alterna el modo con el stub de `localStorage`.

### Decisiones

- Si ya hay una transición en curso, o no existe el root, `runTransition` aplica el modo sin animar.
- Tres copias del componente (deuda hacia `packages/ui`, como ADR-039).

---

## Night PR6: tema por tokens, marca y proveedor de modo (solo frontend)

**Rama:** `feat/tema-night-tokens` (desde `docs/prd-019-night`) · octubre de 2026

### Qué cambia

- **Tema por tokens** en staff, admin y menu: bloque `html[data-venue="night"]` en `globals.css` que redefine canvas, paper, paper-2, ink, muted, hair, oro, arena, piedra y sombra. `brand-*` (y `fuego-*` en staff) pasan a variables CSS (`token("--brand-N")`) con los valores actuales en `:root` y la escala invertida en Night (500 = #AA8453). Tailwind `darkMode: ["selector", '[data-venue="night"]']`.
- **Anti-destello**: script inline en el `<head>` de cada `layout.tsx` que fija `dataset.venue` desde `localStorage mestobeach_venue_mode`; `suppressHydrationWarning` y `viewport.themeColor`. El menú retira `maximum-scale=1`.
- **`src/lib/venue.tsx`** por app: `VenueProvider { mode, setMode, transitioning }` con stub en `localStorage` (sin backend; lo conecta el PR1). Montado en staff `(console)/layout`, admin `(dashboard)/layout` y menu `app/layout`.
- **`BrandMark`** por app (`night?`, `size?`, `className?`) con «Night» en Cormorant Garamond itálica real; sustituye los wordmarks de StaffNav, StaffShell, login de staff y sidebar de admin.
- **Hex P1 migrados**: `ScaledFloorMap` (staff y admin), `staff/src/lib/kds.ts` (paleta de estaciones y `tint`) y `salon.css`.

### Por qué

ADR-039 / RT-1912…1914: el tema Night se aplica cambiando solo lo que resuelven los tokens, y el aspecto de día no debe cambiar (todos los valores de día son los de antes).

### Decisiones

- Tokens añadidos que el ADR no nombra, porque el plano y el KDS usaban grises/colores propios: `--paper-3`, `--map-ink`, `--map-sec`, `--occ-ink`, `--est-1..4` (y `--salon-faint` en salon.css).
- `mode` usa los valores de la API (`RESTAURANT` | `NIGHT`); `data-venue` es `night` | `restaurant`.
- `BrandMark` sin `night` muestra «Night» por CSS (`dark:`), sin depender del contexto; con `true`/`false` se fuerza.
- Pendientes: P2/P3 de hex, `VenueTransition`, wordmark del título de login de admin y `.mark` del salón (no encajan en `BrandMark`), y `themeColor` previo a hidratar.

---

## La estación del runner se lee de la base: cambiarlo de barra ya no le cierra la sesión (PR 5c)

**Rama:** `feat/estacion-runner-desde-bd` · octubre de 2026

En Night los runners cambian de barra durante la noche. Hasta ahora la estación viajaba fija en el
JWT (8 h) y reasignarla obligaba a invalidar el token y a que el runner volviera a entrar con la
bandeja a medias. Implementa SDD-001 v2.1 §7 y ADR-038:

- **`IStaffStationResolver`** (Identity.Application) + `StaffStationResolver` (Identity.Infrastructure):
  Redis `staff_station:{tenantId}:{userId}` 60 s (uuid o centinela `-`); en *miss*,
  `user_memberships.assigned_station_id` de la membresía `ACTIVE`. Redis caído → Postgres + warning;
  Postgres caído → se conserva la claim del token + error. Nunca lanza. Métrica
  `identity.staff_station_lookups` (`source = redis | postgres`).
- **`JwtValidationMiddleware`**, rama `staff`: tras blacklist e invalidación global sustituye
  `ICurrentUser.AssignedStationId` y `HttpContext.Items["station_id"]` por la estación resuelta (los
  vacía con el centinela). Device Tokens y demás tipos no cambian.
- **`PATCH /staff/{userId}/assignment`**: si cambia solo la estación ya no invalida el token, solo
  hace `DEL` de la caché tras el commit; cambiar el sector sigue invalidando. `409
  RUNNER_HAS_OPEN_TRIP` se mantiene. `PATCH /roles` y `DELETE /staff/{userId}` también borran la clave.
- **App del runner** (`staff/runner`): quien no es Admin pide la cola sin `stationId`; la etiqueta de
  la estación se refresca con `GET /identity/auth/me` al cargar, ante un 403 y cuando se rechaza
  armar una bandeja.

---

## Mesto Beach Night, PR5b: PIN de noche

**Rama:** `feat/pin-noche` · octubre de 2026

PIN de noche según SDD-003 (§Redis Keys, §POST /sessions, §Invite link, §Eventos SignalR),
SDD-004 (§POST /orders, fila d de «Enrutado por modo») y ADR-040. Usa `IVenueModeProvider` del PR1.

### Qué cambia

- **PIN único entre sesiones abiertas**: hash Redis `pins_in_use:{tenantId}` (PIN → `sessionId`).
  Abrir y regenerar sortean con `HSETNX` (máx. 20 intentos; si se agotan, 500 `PIN_POOL_EXHAUSTED`
  y log `sessions.pin.pool_exhausted`), con autorreparación si el dueño ya no está `OPEN` en
  Postgres. Se libera **por `sessionId`** (script Lua `HGETALL` + `HDEL`) al cerrar y al regenerar.
- **TTL 12 h**: constante propia `PinTimings.PinTtl` para `session:{t}:{s}:pin` y
  `pin_validated:{t}:{s}:{p}` (abrir, validar, regenerar con clave caducada, join por invitación,
  alta de participante desde staff). `SessionTtl` (4 h) queda solo para `table:{t}:{c}:active`.
- **PIN obligatorio en NIGHT** en todas las mesas: `pinRequired = venueMode == NIGHT ||
  (FIXED && requiresPin)`, sin tocar el preset de la mesa. `POST /sessions` responde
  `503 VENUE_MODE_UNAVAILABLE` si no se puede leer el modo.
- **`sessions.venue_mode`**: snapshot del modo al abrir; migración `AddSessionVenueMode`
  (generada, **sin aplicar**).
- **Invitación en NIGHT**: `inviteGrantsPin = isInviteJoin && PinRequired && mode != NIGHT` y la
  respuesta de un participante nuevo, `pinValidated = isInviteJoin && mode != NIGHT`.
- **Postgres manda sobre Redis**: `AddParticipant` persiste `pin_validated` (también en la rama
  idempotente y en la de carrera); `GET /sessions/{id}`, `join`, `join-by-table` y `pin/validate`
  leen la columna y re-siembran la clave con 12 h si falta.
- **Orders**: `PinValidationService` cae a `session_participants.pin_validated` (read model gana
  `PinValidated`) cuando la clave falta o Redis falla, re-siembra best-effort con 12 h y loguea
  `order.pin_reseeded`. En NIGHT, comensal sin token de personal y sin nombre →
  `422 NAME_REQUIRED` (log `order.name_required`), antes del PIN y del catálogo.
- **Shared**: `ErrorType.UnprocessableEntity` (422) y `ErrorType.ServiceUnavailable` (503),
  mapeados en `ApiController`.
- **`SessionsHub` pasa a `[Authorize]`** y `JoinWaiterGroup` solo une al grupo del tenant del
  token. Caja, Salón y PDA ya mandaban `?access_token=`; el Runner aún no se conecta a este hub.
- **api-client**: tipos `NameRequiredProblem` y `VenueModeUnavailableProblem`.

### Pendiente / fuera de alcance

- El menú todavía no pide nombre + PIN en el registro de noche (PR de frontend del menú); hoy el
  422 se muestra como toast con su `detail`.

---

## Mesto Beach Night, PR4: distribución de mesas por modo

**Rama:** `feat/distribucion-por-modo` · octubre de 2026

Cuarto PR de código de Night (PRD-019, ADR-038, SDD-003 y SDD-004). Cada mesa tiene una posición
por modo, los pisos declaran en qué modos existen y la barra que sirve una mesa de noche se elige en
el plano.

### Qué cambia

- **Sessions, esquema** (migración `AddTableLayoutsPerModeAndKind`, generada, **sin aplicar**):
  tabla `table_layouts` (único `(table_id, mode)`, índice `(tenant_id, mode)`, FK a `tables` en
  cascada y a `floors` con `SET NULL`, `station_id` sin FK), `floors.visible_in` (jsonb, default
  `["RESTAURANT","NIGHT"]`) y `tables.kind` (`TABLE` | `HIGH_TABLE` | `RESERVADO`, default `TABLE`).
  **Copia de datos** en la misma migración: por mesa, fila `RESTAURANT` con su posición actual y
  clon `NIGHT` idéntico (`active = true`, sin barra). `tables.floor_id/pos_x/pos_y` quedan
  deprecadas: Sessions ya no las lee ni las escribe.
- **`GET /floorplan`**: `?mode` (default, el activo vía `IVenueModeProvider`), `?includeHidden`
  (solo Admin) e `?includeInactive` (Host y Admin; Host solo en el modo activo). Devuelve `mode`,
  `activeMode`, `floors[].visibleIn` y `tables[].kind/active/activeGlobal/stationId` de la fila del
  modo. Errores `INVALID_MODE` y `FORBIDDEN_ROLE`.
- **`GET /tables`**: solo las mesas en servicio en el modo activo (global + piso visible + fila activa).
- **`PATCH /tables/layout`**: cuerpo `{mode, tables:[{tableId, floorId?, posX?, posY?, active?, stationId?}]}`
  (el antiguo `{placements}` deja de aceptarse); UPSERT en `table_layouts`, respuesta
  `{mode, applied, skipped}`. Valida `FLOOR_NOT_FOUND`, `STATION_NOT_FOUND`, `STATION_NOT_BAR` (barra
  encendida en ese modo, leída de `kds_stations` en solo lectura) y `TABLE_HAS_ACTIVE_SESSION` al
  apagar una mesa ocupada en el modo activo. Emite `MAP_UPDATED` si el modo es el activo.
- **`PATCH /floors/{id}`**: acepta `visibleIn` (`VISIBLE_IN_EMPTY`, `INVALID_MODE`, normalizado y
  deduplicado); `409 FLOOR_HAS_ACTIVE_SESSIONS` si se oculta el modo activo con mesas ocupadas;
  `FLOOR_UPDATED` si cambia la visibilidad del modo activo.
- **`PATCH /tables/{id}`**: acepta `kind` (`INVALID_TABLE_KIND`) y rechaza `floorId/posX/posY` con 400.
- **Altas de mesas**: `POST /tables` acepta `mode` (default, el activo) y crea las dos filas (activa
  en `mode`, inactiva y sin barra en el otro); la mesa ad-hoc de `POST /sessions` nace con las dos
  filas activas y sin piso.
- **`POST /sessions` y `POST /sessions/join-by-table`**: `409 TABLE_CLOSED_IN_MODE` (`tableCode` y
  `mode` en la raíz) si el piso no es visible en el modo activo o la fila está apagada; en
  join-by-table la comprobación va antes de buscar la sesión. `503 VENUE_MODE_UNAVAILABLE` si no se
  puede leer el modo (nuevo `ErrorType.ServiceUnavailable`).
- **QR fijo**: `{MENU_PUBLIC_URL}/mesa?tenant={tenantId}&table={tableCode}` (fallback
  `Frontend:BaseUrl`). **Hay que regenerar e imprimir los QR.**
- **Orders**: `SessionReadModel` gana `TableId`; `TableLayoutReadModel` se re-mapea a
  `table_layouts` (clave `(table_id, mode)`) y la cola del pase y el manifiesto del viaje resuelven
  coordenadas por `sessions.table_id` → fila del modo activo (sin fila o apagada → sin chincheta).
  **Enrutado, paso (b)**: en NIGHT, si la fila de la mesa apunta a una barra encendida y el plato
  resolvió a una estación `BAR` o a ninguna, gana la barra de la mesa; un `FULL_PASS` nunca se
  desvía y una estación apagada sigue dando `STATION_CLOSED` (nunca se rescata).
- **`@mestobeach/api-client`** (a mano): `getFloorPlan(params)`, `updateTablesLayout(mode, tables)`,
  `updateFloor`, `kind`/`mode` en altas y ediciones, tipos `TableKind`, `TableLayoutPlacement`,
  `FloorPlanParams`. El editor de admin carga con `includeHidden` y guarda la distribución del modo
  activo; el editor con pestañas por modo queda para el PR de frontend.

### Pendiente

- Aplicar la migración en cada entorno y comprobar que `mode=NIGHT` y `mode=RESTAURANT` devuelven
  las mismas posiciones recién migrado.
- El snapshot de EF de Orders sigue describiendo la proyección antigua de `tables` (excluida de
  migraciones, sin efecto en el esquema); se regenerará con la próxima migración de Orders.

---

## Mesto Beach Night, PR3: estaciones con perfil de barra y encendido por modo

**Rama:** `feat/estaciones-perfil-barra` · octubre de 2026

Tercer PR de código de Night (PRD-019, ADR-037, SDD-004). Las estaciones KDS se pueden apagar sin
borrarlas, se encienden por modo y ganan un perfil de flujo `BAR` en el que la barra da una copa por
lista directamente desde la cola.

### Qué cambia

- **Orders, `kds_stations`**: columnas `is_active` (default `true`), `flow_profile`
  (`FULL_PASS` | `BAR`, default `FULL_PASS`) y `active_in` (jsonb, default `["RESTAURANT","NIGHT"]`);
  migración `AddKdsStationProfileAndModes` (generada, **sin aplicar**). Con los default, Cocina,
  Parrilla y Barra siguen como hoy hasta que Administración las configure.
- **Dominio**: `KdsStation.IsOnIn(mode) = IsActive && ActiveIn ∋ mode` («encendida» se calcula,
  no se guarda) y `OrderItem.ReadyFromQueue(staffId, now)`, con guarda propia y sin tocar
  `AllowedTransitions`; sella `preparation_started_at = ready_at`.
- **`POST /orders/items/{id}/ready`**: si la estación del plato es `BAR` (leído de `kds_stations`,
  no del token) y el plato está `QUEUED`, aplica la mecánica única: transacción, `ExecuteUpdate`
  condicional (`status IN ('QUEUED','IN_PREPARATION')`, sellos con `COALESCE`), con 0 filas
  rollback + `order_item_ready_race` + `409 INVALID_TRANSITION`, con 1 fila
  `Entry(item).ReloadAsync()`, estado derivado del pedido y commit. En `FULL_PASS`, `QUEUED`
  sigue siendo `409`. El log `order_item_ready` gana `fromStatus` y `flowProfile`.
- **`POST /orders`**: un plato que resuelve (fijada o por categoría) a una estación apagada en el
  modo vigente rechaza la comanda entera con `409 STATION_CLOSED` (`stationId` en la raíz,
  `detail` «La estación «Parrilla» está cerrada en modo Night.»); log `order.station_closed` y
  métrica `orders.rejected_by_mode{reason=station_closed}`.
- **`/kds-stations`**: `isActive`, `flowProfile`, `activeIn` opcionales en POST/PUT (validaciones
  `INVALID_FLOW_PROFILE`, `ACTIVE_IN_EMPTY`, `INVALID_MODE`) y `isOn` en las respuestas, calculado
  con `IVenueModeProvider`. Apagar una estación con platos vivos está permitido.
- **`GET /api/v1/venue`** (autenticado) devuelve `stations[]` `{id, name, flowProfile, isOn}`,
  todas, por `created_at`, con el mismo modo de la respuesta. La anónima no cambia.
- **Delta del tablero**: `GET /orders/active?since=` también devuelve un pedido si alguno de sus
  platos tiene `order_items.updated_at > since` (cura el bug latente de cambios de plato que no
  movían el estado derivado).
- **`@mestobeach/api-client`**: `KdsFlowProfile`, campos nuevos en `KdsStationDto` /
  `UpdateKdsStationDto`, `KdsStationProfileInput`, `StationClosedProblem`, `VenueStationDto` y
  `VenueStateDto.stations`.

### Decisiones

- **OQ-1904 se asume «sí»** (como fija el SDD): un único `flow_profile` por estación, sin perfil
  por modo. Si el cliente responde «no», hay que pasar a jsonb por modo antes de aplicar la migración.
- Los default de columna viven solo en la migración: el modelo EF no declara `HasDefaultValue` para
  que un `isActive: false` explícito no se pierda por el *sentinel* de los bool.
- El reparto mesa → barra (`table_layouts`, paso b del enrutado) y el orden por prioridad del
  tablero quedan fuera de este PR.

---

## Mesto Beach Night, PR2: carta por modo y branding Night

**Rama:** `feat/catalog-por-modo` · octubre de 2026

Segundo PR de Night (PRD-019, ADR-041 y ADR-039; SDD-002 y la parte de SDD-004 que le toca). Una sola
carta para los dos negocios: cada categoría e ítem dice en qué modos se ve, el catálogo público sale
filtrado por el modo activo y con el branding resuelto, y Orders rechaza lo que no es de este modo.
Sin migración: Mongo materializa el default al leer.

### Qué cambia

- **Catalog, `visibleIn`** en categoría e ítem (`["RESTAURANT","NIGHT"]` por defecto). En dominio con
  setter privado y `[JsonInclude]`, para que sobreviva al viaje por la caché `catalog:{t}`; en Mongo
  con `[BsonIgnoreIfNull]` y default al leer. POST/PATCH de categorías e ítems lo aceptan y lo
  normalizan (mayúsculas, sin duplicados, orden canónico); vacío → `400 VISIBLE_IN_EMPTY`, valor
  desconocido → `400 INVALID_MODE`. Los listados del admin lo devuelven.
- **`GET /api/v1/catalog/{tenantId}`** lee el modo con `IVenueModeProvider` **después** de la caché
  (que se sigue guardando sin filtrar), descarta categorías e ítems fuera de modo y las categorías sin
  ítems visibles (en los dos modos; antes viajaban las vacías), añade `venueMode`, devuelve el
  branding resuelto, va con `Cache-Control: no-store` y responde `503 VENUE_MODE_UNAVAILABLE` si no
  hay modo. El log `catalog.public.get` gana `venueMode` y `hiddenByMode`.
- **`branding.night`**: overrides (null = hereda; `backgroundColor` nunca hereda) con defaults de
  noche `theme: "night"` y `primaryColor: "#AA8453"`; `enabled` ausente = true. Nuevo tema `night`
  en el validador (día y noche) y en `THEMES` del menú y del admin.
- **Endpoints nuevos** (Admin): `GET/PUT /branding/night` (`night` crudo + `effective`) y
  `POST/DELETE /branding/night/{logo|hero}/image`, con el mismo procesado que el día y blobs
  `branding/{t}/night-{kind}-{guid}.webp`.
- **Escrituras disjuntas**: el branding de día pasa a `$set` campo a campo y nunca toca `night`;
  Night escribe solo `branding.night` (`UpdateNightBrandingAsync`).
- **Orders**: `CatalogValidationService.ValidateAsync` recibe el modo; `CatalogCacheDto` (L1) y
  `CatalogReadDocument` (L2) leen `visibleIn` de ítems y categorías. Lo no pedible en el modo →
  `409 ITEM_NOT_IN_MODE` con `itemIds` en la raíz, antes de `422 INVALID_ITEMS` y sin crear la
  orden. `CreateOrderHandler` lee el modo una vez por comanda.
- **api-client**: `VisibleIn`, `venueMode`, tema `night`, `NightBrandingDto`/`Response` y los cuatro
  métodos de Night.

### Decisiones

- Al crear, `visibleIn` solo se escribe en Mongo si restringe un modo: «ambos» se materializa al
  leer, igual que en los datos anteriores.
- Antes de un `$set branding.*` se quita un `branding: null` heredado, porque Mongo no crea campos
  dentro de un null.
- `ITEM_NOT_IN_MODE` se evalúa en el paso del catálogo, después del PIN: la comprobación de nombre
  en Night (paso d) es de otro PR.

---

## Mesto Beach Night, PR1: el local ya tiene modo

**Rama:** `feat/venue-mode` · octubre de 2026

Primer PR de código de Night (PRD-019, ADR-036). El local gana un modo, `RESTAURANT` o `NIGHT`,
que el Admin cambia por API con una transición cerrada y auditada, y que el resto de módulos puede
leer sin depender de Identity. Todavía no cambia nada visible: carta, estaciones, distribución y
tema leen el modo en los PR siguientes.

### Qué cambia

- **`MestoBeach.Shared/Venue`**: enum `VenueMode`, `IVenueModeProvider`,
  `VenueModeUnavailableException` y `VenueModeChangedNotification` (MediatR).
- **Identity**: columnas `tenants.venue_mode` (default `RESTAURANT`) y `venue_mode_changed_at`,
  tabla `venue_mode_changes` con índice `(tenant_id, changed_at DESC)`; migración
  `AddTenantVenueModeAndChanges` (generada, **sin aplicar**). `VenueModeProvider` (Scoped) lee
  Redis `venue:{tenantId}:mode` (TTL 10 min), re-siembra desde Postgres con `SET NX` y, con Redis
  caído, lee Postgres sin escribir; si fallan ambos, `VenueModeUnavailableException`.
- **`/api/v1/venue`** (`VenueController`): `GET /{tenantId}` anónimo, `GET /` para personal o KDS
  (política nueva `StaffOrDeviceJwt`), `GET /preflight`, `POST /mode` y `GET /history` (Admin).
  Las lecturas del modo van con `Cache-Control: no-store` y devuelven `503 VENUE_MODE_UNAVAILABLE`
  si no se puede leer.
- **Pre-flight**: sesiones abiertas, platos vivos de mesas abiertas y bandejas en ruta bloquean;
  `orphanItems` (platos de mesas ya cerradas) solo avisa. Se lee con proyecciones
  `ExcludeFromMigrations` de `sessions`, `orders` y `order_items`.
- **Difusión**: `VenueModeChangedBroadcastHandler` (Orders.Application) manda `VENUE_MODE_CHANGED`
  al grupo `tenant:{t}` de `OrdersHub`; `VenueModeChangedCacheHandler` (Catalog.Application)
  borra `catalog:{t}`.
- **`@mestobeach/api-client`**: tipos `VenueMode`, `VenueStateDto`, `VenuePreflightDto`,
  `ChangeVenueModeResponse`, `VenueNotIdleProblem`, `VenueHistoryDto`, `VenueModeChangedEvent` y
  `createVenueApi`.

### Por qué

Todo lo demás de Night (carta por modo, barras, distribución, PIN, tema) necesita saber en qué modo
está el local. Este PR pone la fuente de verdad única y la puerta para leerla antes que nada.

### Decisiones

- **Postgres manda, Redis acelera.** El proveedor nunca se inventa un modo para un tenant que
  existe; el TTL de 10 min acota cualquier clave vieja que no se pudiera pisar en el cambio.
- **Transición en una transacción con `SELECT … FOR UPDATE`.** Dos administradores a la vez se
  serializan: uno cambia y el otro recibe `changed: false`. El pre-flight se repite dentro.
- **`409 VENUE_NOT_IDLE` sin `force`**, con los contadores y las mesas en la raíz del
  `ProblemDetails`, como `plan_limit_exceeded`.
- **`mode` se valida en el handler** (`INVALID_MODE`) contra `Enum.GetNames`, sin
  FluentValidation, para que `ValidationBehavior` no lo tape.
- **Handlers de la notificación en las capas Application** de Orders y Catalog: cada módulo solo
  registra ese ensamblado en MediatR; en Infrastructure no se ejecutarían. Son best-effort.
- **Persistencia detrás de puertos** (`IVenueStore`, `IVenueModeCache`) en
  `Identity.Application/Venue`, igual que los demás handlers de Identity que no tocan el DbContext.
- **Fuera de este PR**: `stations`/`isOn` en `GET /venue` (PR3, necesita las columnas de
  `AddKdsStationProfileAndModes`) y la estación del runner leída de BD (`IStaffStationResolver`,
  PR 5c).

---

## Mesto Beach Night: el modo fiesta queda diseñado antes de escribir una línea de código

**Rama:** `docs/prd-019-night` · octubre de 2026

Playa Demo es dos negocios en una sala: restaurante de día y fiesta de noche. El cliente quiere
un interruptor en el panel que cambie todo el local a **«Mesto Beach Night»**, y es lo que falta
para que apruebe el proyecto. Este PR deja el diseño cerrado y revisable, para enseñarlo en la demo
visual (H0) y desarrollarlo integrado en el sistema ya entregado. No toca código: la regla del repo
es PRD → ADR → SDD → código.

### Qué cambia

- **`prds/PRD-019-modo-night.md`**, nuevo: qué pasa de noche en cada app, trece historias
  (US-1901…1913), requisitos por bloques (RF-19xx, RT-1901…1923), criterios de aceptación
  automatizables y las decisiones cerradas con el cliente (D-01…D-08) y con Nilton (D-09…D-12).
- **`ARCHITECTURE.md` 4.2**: seis ADR nuevos (ADR-036…041), el esquema previsto en §5, el evento
  `VENUE_MODE_CHANGED` en §6, las claves Redis en §9.10 y un §15.5 con los PR, el paralelismo y los
  hitos. Las revisiones previstas de ADR-026, 027, 029, 030 y 032 se anotan sin reescribirlos.
- **SDD-001, 002, 003, 004 y 006** ganan sus secciones de diseño: `/api/v1/venue/*`, `visibleIn` y
  `branding.night`, `table_layouts` y el PIN de noche, estaciones con perfil de flujo y colas por
  prioridad, reportes por modo. Todo marcado *(diseñado — pendiente de implementación, PRD-019)*;
  nada nuevo se marca como implementado.
- De paso, los SDD se alinean con el código en lo que ya estaba desfasado *(actualizado)*: la
  precedencia real de los avisos (`PIN 3 · BILL 2 · WAITER 1 · ETA 0`), que `BILL` sí persiste,
  `sessions.guest_count`, `branding.backgroundColor`, el filtro de cancelados de Analytics que no
  filtraba nada (`'Cancelled'` frente a `'CANCELLED'`) y `prep-time` agrupando por una columna
  obsoleta.
- `README.md`, `PROCESS.md` y `PRD-018` referencian el nuevo PRD.

### Por qué

Porque Night es lo que falta para la aprobación del cliente y se va a desarrollar integrado en el
sistema, y antes de tocar código el alcance tiene que estar escrito. Y porque al explorar el código
aparecieron cuatro límites que había que decidir antes de tocar nada:

- **El PIN caduca a las 4 horas.** `pin_validated` vive en Redis con TTL fijo; una noche entera se
  rompería a mitad de servicio.
- **El manifiesto del runner no trae el nombre.** De noche entrega en mesa preguntando por quien
  pidió, y hoy solo sabe la mesa y el plato.
- **La PDA conecta al hub de sesiones pero no se une al grupo.** Los avisos de mesa no le llegan en
  vivo; se corrige en el mismo PR que lleva los avisos al runner.
- **La estación del runner viaja fija en el JWT.** Reasignarlo de barra le cierra la sesión en
  mitad del turno.

### Decisiones

- **ADR-036 — Modo del local y transición cerrada.** `VenueMode = RESTAURANT | NIGHT` en
  `tenants.venue_mode` con copia en Redis; el cambio es manual, idempotente, auditado y se bloquea
  con `409 VENUE_NOT_IDLE` mientras quede una mesa abierta; no hay `force`.
- **ADR-037 — Perfil de flujo y encendido por modo.** Las estaciones ganan `is_active`,
  `active_in[]` y `flow_profile`; «encendida» se calcula, y una barra (`BAR`) sirve en dos estados
  (`QUEUED → READY`) sellando `preparation_started_at = ready_at`.
- **ADR-038 — Distribución, prioridad y enrutado por modo.** Una fila de `table_layouts` por mesa y
  modo, pisos visibles por modo, `tables.kind` congelado en `orders.priority` (reservados primero,
  solo en Night) y la barra de la mesa como destino de los platos `BAR`; la estación del runner se
  lee de BD.
- **ADR-039 — Identidad Night y tema por tokens.** `<html data-venue="night">` redefine los tokens
  en las tres apps; «Night» en Cormorant Garamond itálica dorada, Gilda nunca inclinada; animación
  «agujero negro» con fundido bajo movimiento reducido.
- **ADR-040 — PIN de noche y dispositivo.** PIN obligatorio y único entre sesiones abiertas, TTL de
  12 h con Postgres como verdad, sin atajo de invitación, nombre obligatorio (`422 NAME_REQUIRED`) y
  visita recordada por dispositivo.
- **ADR-041 — Carta única con visibilidad por modo.** `visibleIn[]` en ítem y categoría, caché sin
  filtrar y filtro al servir y al validar (`409 ITEM_NOT_IN_MODE`); `branding.night` como overrides
  que la API devuelve resueltos.
- **Quedan al negocio** OQ-1903 (qué mesas atiende cada barra de noche; el reparto es configurable,
  se pide el inicial), OQ-1904 (si la barra de día pasa también a dos estados; cerrar antes de PR3),
  OQ-1906 (el púrpura `#5B4E96` como acento secundario; se valida en la demo visual), OQ-1905
  (precios, snacks y modificadores de noche; fuera de alcance), OQ-1907 (límite por pedido; futura),
  OQ-1908 (inventario de dispositivos de noche) y OQ-1909 (hora máxima de cierre de Night, que fija
  el corte del día de negocio). Ninguna bloquea el arranque del backend.
- **Hitos**: H0 demo visual con tema, marca y animación sobre un stub, sin backend (~2 semanas desde
  la aprobación); H1 núcleo operativo (~5–6 semanas); H2 noche completa (~7–9 semanas); H3
  responsividad y PWA, en paralelo desde el inicio.

---

---

## Los avisos de mesa llegan al PDA y al Salón, y aprietan solos con el tiempo

**Rama:** `fix/avisos-de-mesa-en-el-pda` · agosto de 2026

### El fallo

Los botones de «llamar al camarero» y «pedir la cuenta» del menú no se veían por ninguna parte. Eran
**dos fallos distintos**:

- **La cuenta no se guardaba.** WAITER, PIN y ETA se persisten en Redis y salen por
  `GET /sessions/calls`; BILL solo emitía el evento en vivo. Si en ese instante no había una pantalla
  conectada —o alguien recargaba— la petición se evaporaba sin dejar rastro.
- **El PDA no mostraba llamados.** Los avisos existían y se veían en Salón, pero el PDA —la tablet
  de quien está en sala, que es quien puede ir a la mesa— no tenía nada.

### Qué cambia

**La cuenta se persiste** como los demás motivos, así que sobrevive a una recarga y aparece en la
lista de llamados.

**El PDA estrena un panel «Avisos de mesa»** sobre el plano, con una tarjeta por llamado que
**escala sola con el tiempo**: oro y latido lento al llegar, ámbar y más rápido al minuto, rojo con
temblor pasados tres. El punto late a la par, la cabecera cuenta cuántos llevan sin atender y al
cruzar a urgente vuelve a sonar una sola vez. Cada tarjeta dice de qué se trata, su código de mesa
la selecciona en el mapa, y los llamados de PIN traen un «Ver PIN» que lo descubre ahí mismo. Todo
llega por SignalR, sin esperar al sondeo.

**El Salón escala igual.** Sus avisos no decían cuánto llevaban esperando; ahora lo dicen y aprietan
con la misma cadencia: halo que late alrededor de la tarjeta, aro respirando más deprisa y, pasados
los tres minutos, la tarjeta teñida y temblando. Su reloj latía cada 30 s y ahora late cada segundo
mientras haya un llamado pendiente, que es lo que pide contar el primer minuto en segundos.

**El Salón llamaba «Te llaman» a quien pedía la cuenta.** Daba igual mientras BILL no llegaba a esa
lista; desde que se persiste, sí llega — y mandaba al camarero a la mesa sin saber que lo que
querían era pagar. Ahora tiene nombre e icono propios.

**Caja deja de recibirlos.** El cajero no puede ir a la mesa; el aviso es de sala.

### Decisiones

- **BILL pesa más que WAITER y menos que PIN** en la precedencia de motivos. Es una petición
  concreta y la mesa no rota hasta cumplirla, mientras que una llamada genérica ni dice de qué va;
  el PIN sigue mandando porque sin él el comensal no puede ni pedir.
- **Un PDA descarta solo lo que sabe de otro sector.** Filtrar por pertenencia al sector activo
  escondía las mesas sin sector asignado en *todos* los PDA: quedarse sin dueño no puede significar
  quedarse sin nadie que las atienda.
- **La animación de entrada vive en un envoltorio, no en la tarjeta.** La forma corta `animation`
  reemplaza la declaración entera: encima del latido le dejaba `iteration-count: 1`, y las tarjetas
  latían una sola vez —la recién llegada, ninguna—. Justo lo contrario de lo que se busca, y en una
  captura se veía perfecto.
- **Los umbrales viven en un solo sitio** (`apps/staff/src/lib/avisos.ts`), del que tiran las dos
  pantallas. Copiados en dos ficheros, el día que alguien afinara uno se separarían sin que fallara
  nada: un aviso en rojo en el PDA y en ámbar en el Salón, y nadie se entera hasta que dos camareros
  discuten.
- **En el Salón el halo va en `::after`, no en la tarjeta.** `.now` ya lleva `animation: sl-rise …`
  en forma corta y declarar ahí otra la habría borrado — el mismo fallo, otra vez. De paso, su
  guardarraíl de `prefers-reduced-motion` cubría `::before` pero no `::after`, así que el halo
  habría seguido latiendo justo para quien pide que no lo haga.
- **El color del Salón sigue diciendo QUÉ piden**; lo que sube con el tiempo es la urgencia, y va
  aparte. Solo al llegar a urgente manda sobre el motivo, que es cuando deja de importar por qué
  llamaron.
- **Los avisos siguen en Salón.** Solo se retiran de caja.

---

## La carta del comensal: tres ventanas, una sola lista y el plato que vuela al carrito

**Rama:** `feat/menu-tres-ventanas-y-carta-continua` · agosto de 2026

### Qué cambia

**1. La carta es un solo hilo.** Antes los chips de categoría *filtraban*: pulsabas «Del Mar» y el
resto desaparecía. Ahora la lista muestra **todas** las categorías seguidas, con su encabezado, y
pulsar un chip **salta** a esa sección. Al leer, el chip encendido se mueve solo y se centra en su
tira, así que siempre se ve dónde se está y qué viene después.

**2. Tres ventanas con barra flotante**: Carta · Carrito · Cuenta. La pastilla se desliza hasta la
pestaña activa y se pasa de una a otra **deslizando el dedo**; la pista sigue la mano y en los
extremos frena. El carrito deja de ser un cajón (`CartDrawer`, retirado) y `/session/{id}/orders`
se queda como puerta de redirección para los enlaces ya repartidos por WhatsApp.

**3. Llamar al camarero, siempre a mano.** Arriba a la derecha sobre la portada; en cuanto la barra
de categorías se pega arriba, se encoge —con una campana dibujada a mano— y se posa en una banda
propia sobre ella. También está en el carrito, que es donde surge la duda justo antes de mandar la
comanda. **Pedir la cuenta** sale de la portada y vive solo en Cuenta, en grande y con el importe
pendiente.

**4. El plato vuela al carrito.** Al añadirlo, su foto sale del botón que lo añadió, describe un
arco y aterriza en la pestaña «Carrito», que da un latido. Sale igual del `+` de la tarjeta que del
«Agregar» del detalle.

### Decisiones

- **Los chips saltan, nunca filtran.** Con el filtrado, el chip y el contenido eran dos cosas
  distintas y el seguimiento del scroll no tenía sentido. El chip «Todos» desaparece porque ya no
  significa nada: la lista siempre lo muestra todo.
- **`position: fixed` no vale dentro del carrusel.** Un ancestro con `transform` ancla el `fixed` a
  él, así que el botón de camarero y el plato que vuela se pintan **fuera** de la pista; dentro,
  acababan al final de las tres ventanas en vez de en la pantalla.
- **La banda del botón se reserva subiendo el `top` de la barra, no con relleno.** Si la barra
  creciera al pegarse, cambiaría de alto y toda la carta se recolocaría: el contenido daría un salto
  justo en el instante de la transición.
- **Los umbrales se miden, no se fijan.** Tanto «la barra ya está pegada» como «qué categoría manda»
  salen de la propia cabecera. Con constantes, cambiar su alto dejaba el chip apuntando a la
  categoría anterior sin que nada fallara a la vista.
- **El gesto y el arrastre viven en un `ref`, no en el estado.** Al soltar hay que leer el
  desplazamiento de *ese* gesto, y en un golpe rápido el último `touchmove` aún no ha
  re-renderizado nada.
- **La campana es un SVG en línea.** El menú del comensal no usa iconos (`CLAUDE.md`); por una sola
  figura no compensa traerle la librería entera a la carta.
- **El vuelo se salta con `prefers-reduced-motion`.** Es adorno: el plato entra en el carrito igual.

---

## El catálogo del admin: interruptor de disponibilidad, estación por plato y orden arrastrable

**Rama:** `feat/catalogo-disponibilidad-kds-orden` · agosto de 2026

### Qué cambia

**1. Disponibilidad de un vistazo.** En la lista de productos, el «Sí/No» pasa a ser un
**interruptor** de verdad (`role="switch"`, se ve encendido o apagado sin leer). Al apagarlo, el
producto **baja a un apartado «Productos deshabilitados»** debajo de la tabla, con su cuenta; se
devuelve encendiéndolo otra vez. En la carta del comensal siguen apareciendo atenuados y no
pedibles: no desaparecen, que es como estaba y como se quiere.

**2. Cada plato puede fijar su estación de KDS.** Hasta ahora el ruteo era **solo por categoría**
(`KdsStation.CategoryIds`): todos los platos de «Entrantes» iban a Cocina, sin excepción. Ahora un
plato puede declarar la suya y saltarse la de su categoría —el postre flambeado que sale de
parrilla—. Se ve en una columna nueva de la tabla, que distingue la estación **propia** de la
**heredada** («Cocina · por categoría»).

**3. El orden de las categorías se arrastra.** Los tabs del catálogo se arrastran a la posición
deseada y ese orden es **el que ve el comensal** (sale de `displayOrder`, que ya recorría el
sistema entero). Como alternativa accesible hay flechas ◀ ▶ en la barra de la categoría activa. El
campo numérico «Orden de visualización» del formulario de categoría **se retira**: dos formas de
editar lo mismo se contradicen.

### Decisiones

- **El plato manda; si no dice nada, hereda.** Se descartó sustituir el ruteo por categoría: son
  decenas de platos y una configuración por categoría que ya funciona. La estación del plato es la
  excepción, no la norma, y por eso el valor por defecto es «Hereda de la categoría (X)», con el
  nombre de la que le tocaría.
- **Al pedir, una estación que ya no existe se ignora.** Si se borra una estación, los platos que
  la tuvieran fijada vuelven a la de su categoría en vez de quedarse sin tablero. Sin este
  guardarraíl la comanda se pierde de vista sin ningún error.
- **`stationId` viaja con `[JsonInclude]`.** El catálogo se cachea en Redis serializando la entidad
  de dominio; sin ese atributo el campo se pierde **solo en el acierto de caché**, que es el caso
  normal, y el fallo aparecería de forma intermitente. Mismo motivo que `allowNotes` y las
  imágenes.
- **En un PATCH, `null` es «no lo toques».** Para vaciar la estación hace falta pedirlo
  explícitamente (`clearStation: true`). Sin esa distinción, apagar el interruptor de
  disponibilidad —que manda un PATCH con un solo campo— habría borrado la estación del plato de
  paso.
- **La categoría que se arrastra vive en un `ref`, no en estado.** El manejador del soltar tiene
  que leer el valor de *ese* arrastre; con estado leía el del render anterior y el reordenar fallaba
  cuando no había re-render por medio.

---

## «Cancelar pedido» se montaba encima de «Enviar ya»

**Rama:** `fix/botones-confirmacion-pedido` · agosto de 2026

### El fallo

En la pantalla de confirmación del pedido, el botón de cancelar aparecía **pisando** al de enviar.
Con un plato en el carrito y según el móvil, los dos botones se solapaban y era fácil pulsar el
que no era, justo en el momento en que la comanda sale a cocina.

La causa: cancelar estaba anclado al fondo de la pantalla con `absolute bottom-8`, heredado del
sistema del que se copió la idea. En una pantalla alta cae en su sitio; si el contenido crece —más
platos— o el móvil es más corto, el resto baja hasta chocar con él. Al no compartir flujo, nada
impedía que se montaran.

### Qué cambia

- **Los dos botones van apilados en el flujo normal**, dentro de un contenedor con separación
  propia. Al compartir flujo no pueden solaparse en ninguna pantalla.
- **El contenido se desplaza si no cabe**, en lugar de recortarse.
- **El emblema cede tamaño en pantallas cortas** (`min(250px, 32vh)`), para no empujar los botones
  fuera de la vista.

### Decisiones

- **Se descarta anclar cancelar al fondo.** Separarlo del botón principal buscaba evitar pulsarlo
  sin querer, pero un elemento fuera del flujo no puede reservar su espacio: el precio de esa
  separación era el solape. Con los botones apilados, la distinción la hace el estilo —relleno
  sólido frente a contorno rojo— y no la posición.
- **El centrado vive en un envoltorio con `min-h-full`**, no en el contenedor que desborda. Centrar
  directamente sobre un contenedor con `overflow` recorta por arriba sin dejar subir.
- **El emblema se limita por altura de ventana, no por ancho.** Lo que aprieta en un móvil no es
  el ancho sino el alto.

### Verificado

Medidas las cajas de ambos botones en el navegador, no a ojo: en 375×812 y 320×568 —el iPhone más
pequeño en uso— no se solapan (12 px de separación) y todo cabe sin desplazar. En 320×480, más
corto que cualquier móvil actual, tampoco se solapan y el contenido se puede desplazar.

---

## Mesas que se quedaban imposibles de abrir, y abrir mesa ahora pregunta

**Rama:** `fix/mesa-bloqueada-y-confirmar-apertura` · agosto de 2026

### El fallo

Una mesa ocupada durante **más de cuatro horas** desaparecía del mapa como libre, pero al pulsarla
el backend respondía `409 · Ya existe una sesión abierta para esta mesa`. Y quedaba así **para
siempre**: libre a la vista, imposible de abrir.

La causa es que el estado vivía en dos sitios que se desincronizaban:

| | Caduca | |
|---|---|---|
| Clave `table:…:active` en Redis | **Sí, a las 4 h** (`SessionTtl`) | De ahí salía el estado libre/ocupada del mapa |
| Fila de `sessions` en Postgres | **No**, sigue `OPEN` hasta cerrarla | De ahí sale la comprobación que devuelve el 409 |

Pasadas cuatro horas, Redis olvidaba la mesa y Postgres no. El mapa la pintaba libre y el backend
se negaba a abrirla.

Había un respaldo a base de datos en las consultas, pero estaba **dentro de un `catch`**: solo
actuaba si Redis se caía, no si la clave simplemente había caducado.

### Qué cambia

- **Manda la base de datos** para saber si una mesa está ocupada, en `GetTablesQuery` y en
  `FloorPlanQueries`. La caché pasa a consultarse solo cuando la base no ve nada, por si la sesión
  acabara de crearse.
- **Pulsar una mesa del mapa ya no la abre**: solo la selecciona.
- **«Abrir mesa» pide confirmación**: «Se creará una nueva sesión para esta mesa. ¿Quieres
  abrirla?».

### Por qué así

- **Postgres es la fuente duradera de la verdad**; Redis solo acelera. Que la caché decidiera el
  estado libre/ocupada era invertir esa relación, y la caducidad de cuatro horas convertía la
  discrepancia en permanente.
- **Se corrigen las dos consultas.** La caja lee `/tables` y el plano lee `/floorplan`: arreglar
  solo una habría dejado el fallo vivo en la mitad de las pantallas.
- **Abrir es deliberado.** Un roce sobre el plano creaba la cuenta de esa mesa sin preguntar, y
  deshacerlo obliga a cerrar la sesión. Confirmar cuesta un toque; equivocarse, varios.

### Verificado

Con S03 y H01 abiertas desde hacía más de cuatro horas y sus claves de Redis ya caducadas: antes
salían «Libre» y devolvían 409 al pulsarlas; ahora `/tables` y `/floorplan` las dan por ocupadas y
la lista de la caja las muestra **OCUPADA**. Pulsar una mesa libre en el mapa solo la selecciona.
El botón abre el diálogo, «Cancelar» no crea nada y confirmar abre la sesión.

---

## Las apps avisan cuando no pueden hablar con el servidor

**Rama:** `fix/aviso-sin-conexion` · agosto de 2026

Si la API no responde, las tres apps lo dicen. Antes se quedaban en «cargando» indefinidamente y
sin explicación.

### Qué cambia

- **Barra de aviso** en `menu`, `staff` y `admin`: «Sin conexión con el servidor». Aparece cuando
  una petición no llega y **desaparece sola** en cuanto otra vuelve a responder.
- **El cliente HTTP tiene tiempo límite** (15 s). Sin él, una dirección muerta dejaba la petición
  colgada hasta que se rendía el sistema operativo.
- **`describeApiError`**: los mensajes dejan de inventarse la causa. El login decía «Credenciales
  incorrectas» cuando el servidor sencillamente no estaba.

### Por qué

Pasó de verdad: cambió la IP del equipo y las apps siguieron llamando a la dirección vieja, que
se hornea en la imagen al construir. Resultado: la caja eternamente en «Cargando caja…», el admin
en blanco y **ningún error a la vista**. El backend estaba perfectamente.

Sin respuesta no hay nada que enseñar, así que la pantalla se quedaba, legítimamente, esperando.
El fallo no era del backend sino de la interfaz: le faltaba distinguir «aún no ha llegado» de
«no va a llegar».

### Decisiones

- **Va en el cliente compartido, no en cada pantalla.** `createApiClient` es el único punto por el
  que pasan todas las llamadas de las tres apps; ponerlo ahí lo cubre todo sin repetir nada.
- **Eventos del navegador, no un callback ni un contexto de React.** El cliente HTTP vive fuera de
  React, y así el paquete `api-client` no necesita depender de React para avisar.
- **Una barra de estado, no un aviso puntual.** La condición dura mientras dura. Un toast por cada
  petición fallida sería una lluvia de avisos en las pantallas que sondean cada pocos segundos.
- **Solo se avisa cuando no hubo respuesta.** Un 4xx o un 5xx sí son respuesta, y cada pantalla ya
  enseña su mensaje; decir «sin conexión» ahí sería mentir.
- **El navegador también avisa**: se escucha su evento `offline`, que llega antes incluso de que
  falle ninguna petición.

### Verificado

Con la API apagada, en `admin` y en `staff`: sale la barra roja y el formulario dice «No se pudo
conectar con el servidor» en vez de «Credenciales incorrectas». Al levantar la API, la barra
desaparece sola y se entra con normalidad; la caja carga en lugar de quedarse en «Cargando caja…».

---

## El carrito sobrevive a recargar, y el botón dice cuánto se envía

**Rama:** `fix/carrito-persistente` · agosto de 2026

### Qué cambia

- **El carrito se guarda en `sessionStorage`.** Antes vivía solo en memoria: cualquier recarga lo
  vaciaba sin avisar.
- **El botón de enviar muestra cantidad e importe**: «Enviar 3 platos · 42,50 €» en vez de un
  «Enviar pedido» a ciegas.

### Por qué

Recargar no es un caso raro en el móvil: basta con que el navegador descarte la pestaña que quedó
en segundo plano mientras el comensal atiende un mensaje. Perdía toda su elección y tenía que
rehacerla desde cero, en la playa y con el móvil al sol.

Lo del botón es prevención: es lo último que se ve antes de mandar la comanda a cocina, y ahí es
donde conviene poder confirmar de un vistazo que van tres platos y no cuatro. Antes el total solo
aparecía en una línea aparte, encima.

### Decisiones

- **`sessionStorage`, no `localStorage`.** Coherente con el resto del menú —la sesión y el JWT del
  comensal ya viven ahí— y porque el carrito **no debe** sobrevivir al cierre de la pestaña: quien
  escanee después ese QR en el mismo dispositivo tiene que empezar de cero.
- **El carrito queda atado a su sesión.** Si lo guardado pertenece a otra —la misma pestaña
  reutilizada tras escanear el QR de otra mesa— se descarta al cargar. Heredar el carrito de la
  mesa anterior es peor que perderlo. Esto da uso por fin a `setSessionId`, que estaba en el store
  sin que nadie lo llamara.
- **Se rehidrata desde un efecto, no al crear el store** (`skipHydration`). Next renderiza esta
  pantalla también en el servidor, donde no existe `sessionStorage`; si zustand cargara el carrito
  al importar el módulo, el primer render del cliente no coincidiría con el HTML del servidor y
  React daría un error de hidratación.

### Verificado

En el navegador contra el entorno real: se añaden dos platos, se recarga y el carrito sigue ahí
(contador, total y cantidad en la tarjeta). El botón muestra «Enviar 2 platos · 29,00 €», y en
singular «Enviar 1 plato · 14,50 €». Sin errores en consola —incluida la hidratación—. Y al marcar
a mano el carrito guardado como de otra sesión, al recargar se descarta y queda atado a la actual.

---

## Confirmar el pedido: animación con ventana para deshacer

**Rama:** `feat/animacion-confirmar-pedido` · agosto de 2026

Al enviar la comanda, el menú del comensal muestra una pantalla completa con el horizonte de la
playa y **una ventana de 6 segundos para echarse atrás**. Durante ese rato el pedido **no ha
salido**.

### Qué cambia

- **`OrderSentAnimation`** (nuevo): pantalla de confirmación con el emblema animado, los platos
  rotando, una barra que se llena durante la ventana, «Enviar ya» para lanzarlo antes y
  «Cancelar pedido» para descartarlo. Tocar cualquier punto de la pantalla también lo envía.
- **`session/[sessionId]/page.tsx`**: `handlePlaceOrder` se parte en `beginOrder` (abre la
  pantalla, no envía nada) y `commitOrder` (el que sí manda la comanda). Al terminar se navega a
  **Mis pedidos**, en vez del aviso que había antes.

### Por qué

En una hamaca a treinta metros de la barra, rectificar un pedido significa levantarse o llamar al
camarero. Seis segundos de margen resuelven el arrepentimiento inmediato —el plato equivocado, la
cantidad de más— sin que nadie en cocina llegue a enterarse.

La idea viene de un sistema anterior del equipo (`kmrb-front/apps/customer`), donde ya estaba
probada; aquí se reinterpreta con el lenguaje de Mesto Beach.

### Decisiones

- **La ventana es de verdad, no cosmética.** El pedido no se envía hasta que se agota o el
  comensal lo lanza. Retrasa la comanda hasta 6 s; a cambio, cancelar no molesta a nadie ni deja
  rastro que anular después.
- **El emblema es el horizonte dentro del plato, no la olla del sistema original.** El aro sigue
  siendo un plato —mantiene la referencia a la alta cocina— pero dentro está lo que se ve desde
  la hamaca: el sol sobre el mar, la orilla y una palmera. Se descartó por el camino un espeto de
  sardinas a la brasa: la marca busca un tono sobrio, y unas sardinas sobre brasas eran justo el registro rústico que
  ese documento rechaza.
- **Solo se mueve el mar.** La palmera está quieta a propósito: si todo se moviera, la pantalla
  competiría con el texto en vez de acompañarlo.
- **La comanda se escribe sola durante la espera.** Las tres líneas del papel aparecen una a una,
  que es la metáfora de lo que está pasando de verdad: se está tomando tu pedido, todavía no ha
  salido.
- **El visto verde entra solo al confirmar.** Antes aparecía desde el principio, y un sello de
  «hecho» conviviendo con «tu pedido está a punto de salir» decía algo que no era cierto.
- **Los colores salen de las variables `--m-*`**, así que la pantalla respeta la apariencia que el
  local tenga configurada en Admin → Apariencia, en vez de fijar una paleta propia.
- **Tras el PIN no hay segunda espera.** Si el envío falla porque la mesa exige PIN, al validarlo
  se reintenta en modo inmediato (`immediate`): el comensal ya cumplió su ventana antes de que
  saltara el PIN, y hacerle esperar otra vez sería castigarle por una condición de la mesa.
- **Se respeta `prefers-reduced-motion`**: con esa preferencia las animaciones se reducen a 1 ms.

### Verificado

Recorrido completo en el navegador contra el entorno real: la comanda se crea y aterriza en Mis
pedidos, y al cancelar dentro de la ventana **no se crea ningún pedido** (comprobado contra
Postgres) y el carrito vuelve intacto con el aviso «Pedido cancelado. No se ha enviado nada».
`next build` pasa, que es lo que type-chequea la app.

---

## Modelo de ramas: `integration` recibe los PR, `main` solo versiones

**Rama:** `docs/modelo-de-ramas` · agosto de 2026

Documenta cómo se trabaja con las ramas, para que no dependa de que alguien lo cuente.

### Qué cambia

- **`docs/branching.svg`**: diagrama del modelo, pensado para exponerlo al equipo. Vectorial, así
  que escala sin pixelarse en una diapositiva o un proyector.
- **`AGENTS.md` y `CLAUDE.md`**: sección *Ramas y pull requests* con la tabla de las tres clases de
  rama y las cuatro reglas.

### Por qué

`main` pasa a ser la referencia de lo estable, e `integration` el sitio donde acumular e integrar
sin arriesgarla. Al proyecto llega gente nueva, y la convención no estaba escrita en ninguna parte:
solo existía en la cabeza de quien la decidió.

### Decisiones

- **Va en los dos ficheros, no en uno.** `AGENTS.md` es el punto de entrada declarado para quien se
  incorpora y `CLAUDE.md` el que leen los agentes de IA; dejarlo solo en uno garantizaba que la
  mitad de quienes tocan el repo no lo vieran.
- **El diagrama es SVG, no una imagen de mapa de bits.** Se ve nítido a cualquier tamaño, pesa
  poco y el diff en git sigue siendo texto.
- **Las ramas de trabajo salen de `integration`, nunca de `main`.** Salir de `main` traería a la
  rama una base más vieja que la que ya tienen los demás, y el PR llegaría con conflictos ajenos.

---

## Containerización, acceso desde el móvil e imágenes

**Rama:** `feat/docker-y-acceso-desde-movil` · agosto de 2026

Deja el sistema entero levantándose con un comando, accesible desde el móvil por la red local, y
con las imágenes de la carta cargando.

### Qué cambia

**El sistema arranca en Docker.** `docker compose --profile demo up -d --build` levanta la
infraestructura, la API y las tres apps. Nuevos `backend/Dockerfile` (multi-etapa) y
`frontend/Dockerfile` (parametrizado por `APP`, con `turbo prune`).

**Perfil `demo` con datos listos.** Un contenedor efímero `init` aplica las migraciones de los
cinco módulos, siembra el local y crea la carta de demostración. Scripts nuevos:
`scripts/docker-init.sh` (orquestador) y `scripts/seed-demo-mestobeach.sh` (equivalente del `.ps1`
para macOS y Linux, escrito para el bash 3.2 que trae macOS).

**Acceso desde el móvil.** `scripts/set-host.sh` fija `MESTOBEACH_HOST`, la única variable que
gobierna las cinco cosas que deben coincidir para que un teléfono pueda entrar por IP:
`NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_MENU_URL` (QR fijo), `Frontend:BaseUrl` (QR dinámico),
`Cors:AllowedOrigins` y `BlobStorage:BaseUrl`.

**Las imágenes vuelven a cargar**, y `scripts/fix-blob-urls.sh` repara las ya subidas.

**Documentación al día.** README reorganizado como *Mesto Beach* con diagramas,
puesta en marcha y primer acceso; `AGENTS.md` y `CLAUDE.md` alineados con la realidad.

### Por qué

El entorno solo se levantaba con un script de PowerShell, así que en macOS o Linux no había forma
sencilla de arrancarlo, y preparar los datos eran varios pasos manuales fáciles de olvidar. Y sin
acceso por IP no se podía probar lo esencial del producto: escanear el QR de una hamaca con un
móvil de verdad.

### Decisiones

- **Un perfil de Compose, no un script aparte.** Separar *levantar el sistema* de *preparar los
  datos* con `--profile demo` permite que el día a día (`docker compose up -d`) no toque nunca los
  datos. El `init` es **idempotente** —comprueba si la carta ya tiene categorías antes de sembrar—
  para poder repetir el comando sin miedo.
- **El inicializador reutiliza la etapa `build` del backend** en vez de empaquetar *migration
  bundles*. Las capas ya existen en disco, así que la imagen no ocupa nada apreciable; el coste es
  un arranque algo más lento, y solo cuando se pide el perfil.
- **`images.remotePatterns` se acota por puerto y ruta, no por host.** Azurite se sirve desde
  `localhost` o desde una IP que cambia; un patrón por host obligaría a reconstruir con cada IP.
- **No se toca el código de la aplicación para las migraciones.** Se descartó aplicarlas al
  arrancar la API: habría cambiado el comportamiento del backend en producción para una comodidad
  de desarrollo.

### Correcciones incluidas

Cinco fallos que salieron al containerizar y verificar, todos con el mismo patrón: **fallan en
mestobeach o mienten**.

| Fallo | Síntoma |
|---|---|
| `useSearchParams()` sin `<Suspense>` en cinco páginas | Rompía `next build` en producción. No era cosa de Docker: habría fallado en cualquier despliegue |
| El servicio `api` sin `target: runtime` | La API se construía como el inicializador, porque `docker build` sin `--target` toma la **última** etapa. Moría con un error que hablaba de un script inexistente |
| `localhost:3000` codificado a fuego en el QR dinámico | `SessionAccessQueries.cs` ignoraba `Frontend:BaseUrl`. El QR dinámico habría salido con `localhost` hiciera lo que hiciera |
| `BlobStorage:BaseUrl` sin el segmento del contenedor | Las imágenes daban **403** mientras el panel decía "actualizado" — porque la subida sí funcionaba, lo que estaba mal era la URL |
| `psql` sin `ON_ERROR_STOP` y `dotnet ef --verbose false` | El inicializador terminaba con código 0 dejando la base **vacía**. `--verbose` es un flag: EF tomaba `false` como nombre de migración |

### Notas para quien revise

- **La URL de una imagen se guarda entera en Mongo al subirla**, no se calcula al leer. Cambiar
  `MESTOBEACH_HOST` deja las anteriores apuntando a la dirección vieja: hay que pasar
  `scripts/fix-blob-urls.sh`.
- **El correo de activación de personal no llega a MailHog.** `Identity` usa un stub que solo
  escribe en el registro; el enlace se saca con `docker compose logs api | grep EMAIL`. Queda
  documentado, no corregido: cambiarlo es una decisión de producto.
