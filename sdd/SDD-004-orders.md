# SDD-004 — Orders

> **v2.2 — Octubre 2026 (diseño PRD-019) — Mesto Beach Night: estaciones por modo, barra de
> 2 estados y reservados primero.** *(diseñado — pendiente de implementación, PRD-019)*. El local
> tiene un **modo** (`RESTAURANT` | `NIGHT`, ADR-036) que Orders lee por `IVenueModeProvider`
> (SDD-001 §7) y que cambia tres cosas de este módulo: (1) las **estaciones** ganan `is_active`,
> `flow_profile` (`FULL_PASS` | `BAR`) y `active_in[]`, y «encendida» pasa a calcularse — una
> comanda **no se enruta a una estación apagada** (`409 STATION_CLOSED`, ADR-037); (2) el perfil
> **`BAR`** permite **`QUEUED → READY`** sin pasar por los fuegos (`ReadyFromQueue()`, ADR-037);
> (3) cada pedido **sella** `priority` (`1` si nace en NIGHT desde una mesa `RESERVADO`) y `venue_mode`, y las
> colas del KDS y del runner salen por `priority DESC` (ADR-038). Además: el enrutado admite el
> **override por mesa** desde `table_layouts.station_id` del modo activo, el carrito se valida
> contra la **carta visible en el modo** (`409 ITEM_NOT_IN_MODE`, ADR-041), en Night un comensal
> (sin token de personal) sin nombre no puede pedir (`422 NAME_REQUIRED`), el manifiesto del runner lleva
> `customerName` y `priority` por plato, y `OrdersHub` difunde **`VENUE_MODE_CHANGED`**.
> Migraciones `AddKdsStationProfileAndModes` y `AddOrderVenueModeAndPriority`.

> **v2.1 — Agosto 2026 — La cola del pase y la observación de la comanda.** Marchar y encender los
> fuegos dejan de ser el mismo gesto: entre `HELD` e `IN_PREPARATION` aparece **`QUEUED`** ("en
> cola"). **Marchar** mete el plato en la cola de su estación —lo pueden hacer **cocina y PDA**— y
> la **orden de preparación** (`POST /orders/items/{itemId}/start-preparation`) es **solo de la
> estación**; un plato pedido "ahora" **nace en cola**, no en fuegos (ADR-032). Cada transición
> sella su hora (`queued_at`, `preparation_started_at`, `preparation_ordered_by`) y la comanda gana
> una **observación** libre (`orders.observacion`) que se captura en el menú y en el PDA y viaja a
> KDS, PDA, caja, salón y reparto. En el mismo pase se saldan tres deudas del contrato con el código:
> **`GET /delivery-queue`** estrena sección propia, y `cancel` y `kds-stations/{id}` pasan a
> documentarse con su método real (`DELETE /orders/{orderId}` y `PUT`).

> **v2.0 — Agosto 2026 — Ciclo de vida por ítem.** El estado deja de vivir en el pedido y baja al
> plato (ADR-025), lo que habilita el coursing (ADR-026), los viajes de runner con reclamo atómico
> (ADR-027) y la visibilidad cruzada entre estaciones (ADR-029). El estado del pedido pasa a
> derivarse de sus ítems. Diseñado en **PRD-018**.

> **v1.x — Junio 2026** — **Tablero KDS con consistencia autoritativa**: `GET /api/v1/orders/active` (snapshot + delta por `since` + `activeIds` para podar), columna `orders.updated_at` como cursor, y resiliencia de UI (banner "sin sincronizar" + pedidos demorados). SignalR es solo *hint*; el sondeo es la verdad.

## Servicio
**Módulo**: Orders  
**Base path**: `/api/v1`  
**Responsabilidad**: Creación y gestión del ciclo de vida de pedidos. Fuente de verdad de todos los pedidos. Dispara eventos SignalR para actualización en tiempo real en KDS, app del mesero y app del cliente.

**PRDs de referencia**: PRD-002 (Flujo cliente — §Pedido), PRD-003 (KDS/Staff), **PRD-018** (Mesto Beach — coursing, runners, estaciones), **PRD-019** (Mesto Beach Night — modo del local, barra de 2 estados, reservados primero; *diseñado, pendiente de implementación*)

**ADRs que gobiernan este módulo**: ADR-022 (sondeo autoritativo), ADR-025 (ciclo por ítem), ADR-026 (coursing), ADR-027 (viajes de runner), ADR-029 (visibilidad cruzada), **ADR-032** (la cola del pase — v2.1); y *(diseñados — pendientes de implementación, PRD-019)* **ADR-036** (modo del local y transición cerrada), **ADR-037** (perfil de flujo y encendido por modo), **ADR-038** (distribución, prioridad y enrutado por modo), **ADR-040** (PIN de noche y dispositivo), **ADR-041** (carta única con visibilidad por modo)

**Dependencias**:
- PostgreSQL: `orders`, `order_items` (+ ciclo de vida propio), **`delivery_trips`** (v2.0), `kds_stations`, `session_participants` (*(v2.2 — diseñado)* incluida `session_participants.pin_validated`, en solo lectura — ADR-040), `sessions`, y `tables` en **solo lectura** solo para `table_code` y `kind` (prioridad), de Sessions — `ExcludeFromMigrations`; *(v2.2 — diseñado)* `table_layouts` (de Sessions, `ExcludeFromMigrations`) pasa a ser la fuente de `floor_id/pos_x/pos_y` de la cola y del manifiesto, además de `station_id/active` para el enrutado; siempre se lee la fila del modo activo
- MongoDB: `catalogs` (para validar ítems y capturar snapshots)
- Redis: `catalog:{tenantId}` (cache catálogo — *(v2.2)* **sin filtrar por modo**: el filtro `visibleIn` se aplica al validar, ADR-041), `pin_validated:{tenantId}:{sessionId}:{participantId}` (*(v2.2 — diseñado, ADR-040)* es **caché**: la verdad es `session_participants.pin_validated`); *(v2.2 — diseñado)* `venue:{tenantId}:mode`, **solo a través de `IVenueModeProvider`** (SDD-001 §7) — Orders nunca lee ni escribe esa clave por su cuenta
- SDD-001 Identity: JWT validation; *(v2.2 — diseñado)* `IVenueModeProvider` (`MestoBeach.Shared`) y `VenueModeChangedNotification` (MediatR, `MestoBeach.Shared`), cuyo handler de difusión vive en este módulo
- SDD-003 Sessions: session and participant validation
- SDD-007 Notifications: SignalR real-time broadcast

---

## Ciclo de vida por ítem *(v2.0 — ADR-025 · v2.1 — ADR-032)*

Hasta v1.x el estado vivía en el **pedido**. En Mesto Beach no encaja: una misma comanda mezcla platos
que salen "ahora" con platos retenidos, sus ítems se reparten entre **tres estaciones**, y un runner
entrega **platos sueltos** de varias comandas en una bandeja. El estado pasa a `order_items`.

### Estados

| Estado | Significado | Quién lo provoca |
|---|---|---|
| `HELD` | En espera. El comensal lo pidió "luego de mi instrucción" | Se crea así si `timing = LATER` |
| `QUEUED` *(v2.1)* | **En cola** en su estación: ya está marchado y espera la orden de preparación | Cocina o PDA al **marchar**; o se crea así si `timing = NOW` |
| `IN_PREPARATION` | En los fuegos de su estación | **Solo la estación**, al dar la orden de preparación (ADR-032) |
| `READY` | Listo en el pase, esperando runner | Estación — desde `IN_PREPARATION` o `RETURNED`; en estaciones de perfil `BAR` también **desde `QUEUED`** *(v2.2 — diseñado, ADR-037)* |
| `IN_DELIVERY` | Reclamado por un runner, en su bandeja | Runner (reclamo atómico) |
| `DELIVERED` | Entregado en el puesto | Runner al cerrar la ruta |
| `RETURNED` | Devuelto: no se pudo entregar | Runner al desmarcar |
| `CANCELLED` | Anulado | Staff |

### Transiciones permitidas

```
   [creación] ── timing = LATER ──►  HELD
                                       │ marchar  (cocina o PDA — ADR-032)
   [creación] ── timing = NOW ───────► ▼
                            ┌────── QUEUED
                            │          │ orden de preparación  (SOLO la estación — ADR-032)
     listo desde la cola    │          ▼
     (SOLO estaciones BAR   │  IN_PREPARATION
      — v2.2, ADR-037)      │          │ listo (estación)
                            │          ▼
                            └─────► READY ◄──── listo ──── RETURNED
                                    │   ▲                     ▲
                          reclamar  │   │ liberar             │ devolver
                          (atómico) ▼   │ (viaje abandonado)  │
                               IN_DELIVERY ──────────────────┘
                                       │ entregar
                                       ▼
                                   DELIVERED

   CANCELLED: alcanzable desde HELD, QUEUED, IN_PREPARATION, READY y RETURNED — nunca desde
              IN_DELIVERY (primero se cierra o se libera el viaje).
   DELIVERED y CANCELLED son terminales.
```

**Quién dispara cada transición.** La política de la API es la puerta (403 sin tocar la base) y el
handler vuelve a comprobar el rol, porque la regla es del módulo y no del borde. El alias heredado
`Mesero` se traduce a `PDA` en el borde (`StaffRoleNames.ExpandirAlias`).

| Transición | Endpoint | Quién puede (política) |
|---|---|---|
| `HELD → QUEUED` — **marchar** | `POST /orders/items/{itemId}/march` | Cocina (`KDS`), `PDA` (alias `Mesero`) o `Admin` — `StaffPdaKdsOrAdmin` |
| `QUEUED → IN_PREPARATION` — **orden de preparación** | `POST /orders/items/{itemId}/start-preparation` | **Solo la estación**: `KDS` o `Admin` — `StaffKdsOrAdmin` |
| `QUEUED → READY` — **listo desde la cola** *(v2.2 — diseñado, ADR-037)* | `POST /orders/items/{itemId}/ready` | **Solo la estación y solo si su `flow_profile = BAR`**: `KDS` o `Admin` — `StaffKdsOrAdmin`, y la estación del token debe ser la del plato |
| `IN_PREPARATION → READY` y `RETURNED → READY` | `POST /orders/items/{itemId}/ready` | `KDS` o `Admin` — `StaffKdsOrAdmin`, y la estación del token debe ser la del plato |
| `READY → IN_DELIVERY` | `POST /delivery-trips` | `Runner` o `Admin` — `StaffRunnerOrAdmin` |
| `IN_DELIVERY → DELIVERED` / `RETURNED` | `POST /delivery-trips/{tripId}/complete` | `Runner` o `Admin` — `StaffRunnerOrAdmin` |
| `IN_DELIVERY → READY` — **liberar** | `POST /delivery-trips/{tripId}/release` | `Runner` o `Admin` — `StaffRunnerOrAdmin` |

> **Marchar ya no enciende fuegos (ADR-032).** ADR-026 dejaba el marchado en manos exclusivas del
> PDA porque ese único gesto metía el plato en preparación. Partida la transición en dos, la cocina
> entra en el marchado —el pase adelanta lo que ve venir— y a cambio **nadie fuera de la estación
> puede encender los fuegos**. El comensal sigue sin poder marchar nada.

Un `RETURNED` **vuelve a la cola de su estación de origen**: la estación decide si lo repone en el
pase (`RETURNED → READY`, por el mismo endpoint `/ready`) o lo anula. El motivo **no se captura en
la app** — el runner lo explica en persona (ADR-027).

### Perfil de flujo `BAR`: listo desde la cola *(diseñado — pendiente de implementación, PRD-019)*

En Night las barras no tienen «fuegos»: un gin tonic se sirve en el acto, y obligar a la barra a
pulsar dos veces (orden de preparación y luego listo) es un paso vacío que nadie daría. Por eso la
estación gana un **perfil de flujo** (`kds_stations.flow_profile`, ADR-037):

| Perfil | Columnas del tablero | Transiciones del plato |
|---|---|---|
| `FULL_PASS` (por defecto) | Esperando orden · En cola · En preparación · En el pase | Las de siempre: `QUEUED → IN_PREPARATION → READY` |
| `BAR` | **Pedidos** (acción *Listo*) · **Listo** | **`QUEUED → READY`** directo. `IN_PREPARATION` sigue siendo un estado válido (un plato puede llegar ahí por `/start-preparation`), y el tablero reparte con la regla `columnOf(status, 'BAR')`: `QUEUED` → «Pedidos» (acción Listo); `IN_PREPARATION` → «Pedidos» (acción Listo; llega por un cambio de perfil o por `/start-preparation`; el tablero BAR nunca ofrece «¡A preparar!»); `RETURNED` → «Pedidos» (acción Listo) con su pastilla «Devuelto — rehacer»; `READY` → «Listo» (sin acción); `HELD` → fuera de las columnas (todavía no es trabajo de la barra; lo marcha la PDA o el salón que lo pidió, o Admin, con `StaffPdaKdsOrAdmin`, y entra en «Pedidos»; hoy solo PDA/salón crean platos `LATER`); `IN_DELIVERY`, `DELIVERED`, `CANCELLED` → fuera; un plato heredado sin estado por ítem → «Pedidos» |

Reglas de la transición nueva:
- Solo existe a través de **`OrderItem.ReadyFromQueue()`**, que el handler de
  `POST /orders/items/{itemId}/ready` invoca **únicamente** cuando la estación del plato tiene
  `flow_profile = BAR`. `MarkReady()` (desde `IN_PREPARATION` / `RETURNED`) no cambia. Un plato
  `QUEUED` en una estación `FULL_PASS` sigue recibiendo `409 INVALID_TRANSITION`.
  `ReadyFromQueue(Guid staffId, DateTime now)` lleva su propia guarda (`Status != Queued` →
  `InvalidOperationException`) y **no** añade la arista `Queued → Ready` a `AllowedTransitions`;
  así `MarkReady()` y `CanTransitionTo` no cambian y «solo existe a través de `ReadyFromQueue()`»
  es literalmente cierto en el dominio. En persistencia, la regla la aplica el `ExecuteUpdate`
  condicional de la **mecánica única** (que la replica en SQL) y la entidad se **recarga** después;
  los tests de dominio cubren `ReadyFromQueue()` y los de integración, el UPDATE.
- `ReadyFromQueue()` sella **`preparation_started_at = ready_at`** (la misma marca) además de
  `ready_at`, y deja `preparation_ordered_by` con el staff que pulsó. Así el recorrido del modo foco
  y la analítica de cocina (`ready_at − preparation_started_at`) no se rompen: el tiempo de fuegos de
  una barra es **cero, no nulo**, y la espera en cola (`preparation_started_at − queued_at`) sigue
  midiendo lo único que una barra tarda.
- **Mecánica única** (en `POST /orders/items/{itemId}/ready`): 1) el handler carga el pedido como
  hoy (`GetOrderByItemIdAsync`, con tracking) y hace las comprobaciones previas de 404, 403 y 409 de
  perfil (sirven para los mensajes; la verdad la fija el UPDATE). 2) Ruta BAR con el plato
  `QUEUED`, dentro de `BeginTransactionAsync`: a) `ExecuteUpdate` condicional `SET status='READY',
  ready_at=@now, preparation_started_at=COALESCE(preparation_started_at,@now),
  preparation_ordered_by=COALESCE(preparation_ordered_by,@staff), updated_at=@now WHERE id=@id AND
  tenant_id=@tenant AND status IN ('QUEUED','IN_PREPARATION')`; b) si afecta 0 filas: rollback, log
  `order_item_ready_race`, relectura con `AsNoTracking` (o `Entry(item).ReloadAsync()`) para el
  `detail` y `409 INVALID_TRANSITION`; c) si afecta 1 fila: `await db.Entry(item).ReloadAsync()` dentro de
  la misma transacción, de modo que la entidad rastreada pasa a reflejar **lo que quedó en la fila**
  (`status`, `ready_at` y el `preparation_started_at` real). **No** se vuelve a llamar a
  `item.ReadyFromQueue()` sobre la entidad: se cargó antes del UPDATE y, si entre la carga y el
  UPDATE entró un `/start-preparation` concurrente, la entidad en memoria seguiría en `QUEUED` y
  pisaría su sello con `@now` al guardar, cuando el `COALESCE` del UPDATE lo había conservado.
  Después, `order.RecalculateStatusFromItems()` y `SaveChanges` (solo escribe
  `orders.status/updated_at/ready_at`; el ítem ya no tiene cambios pendientes), y commit; d) `ITEM_READY`, la respuesta y
  `prepSeconds` se construyen después del commit, desde la entidad ya coherente. En una barra con
  dos tablets el doble clic es la norma, no la excepción.
- `RETURNED → READY` no cambia: en `BAR` también vale, por el mismo endpoint.
  `IN_PREPARATION → READY` y `RETURNED → READY` siguen como hoy: `MarkReady()` y `SaveChanges`.
- Qué perfil tiene la estación se resuelve **en el handler, leyendo `kds_stations`**, no en el
  token: una estación puede cambiar de perfil sin que nadie vuelva a entrar.

**Caso de aceptación** *(v2.2 — diseñado)*:
- [ ] En Barra 2 (`BAR`), un plato `RETURNED` y otro `IN_PREPARATION` salen en «Pedidos» con la
      acción *Listo* y `/ready` los lleva a `READY`; un `HELD` no aparece hasta que la PDA lo marcha

### Estado del pedido, derivado

`orders.status` deja de escribirse a mano y se **deriva** de sus ítems, en este orden de prioridad:

| # | Condición sobre los ítems (ignorando `CANCELLED`) | `orders.status` |
|---|---|---|
| 1 | No queda ninguna línea viva (todas anuladas) | `CANCELLED` |
| 2 | Todas `DELIVERED` y ninguna `HELD` ni `QUEUED` | `FINALIZED` |
| 3 | Alguna en `IN_PREPARATION` o `RETURNED` (hay trabajo en la estación) | `IN_PREPARATION` |
| 4 | Alguna en `READY` o `IN_DELIVERY` (nada en fuegos; hay platos en el pase o en ruta) | `READY` |
| 5 | En otro caso — solo quedan `HELD` / `QUEUED`, con o sin entregadas | `CONFIRMED` |

Se evalúa **en orden**: la regla 3 pesa más que la 4 a propósito. No se deriva nada mientras el
pedido esté `PENDING` (se confirma en el mismo acto de crearse) ni una vez `CANCELLED` en bloque
(es terminal). Los sellos `confirmed_at` / `ready_at` solo se ponen la primera vez: el pedido puede
ir y volver —un plato devuelto lo baja de `READY` a `IN_PREPARATION`— y no se reescriben.

> **Auto-finalización (ADR-025):** la comanda pasa a `FINALIZED` sola cuando su último ítem queda
> `DELIVERED` y **no quedan ítems `HELD` ni `QUEUED`**. Una mesa con platos retenidos o en cola
> nunca se finaliza aunque lo servido esté entregado.

### Enrutado a estación

El enrutado deja de ser del pedido y pasa al ítem: al crear la comanda, cada ítem recibe su
`station_id` según la categoría de su producto en el catálogo. Un pedido **ya no tiene una única
estación**; `orders.station_id` queda obsoleto y se retira.

#### Enrutado por modo *(diseñado — pendiente de implementación, PRD-019 — ADR-037 · ADR-038 · ADR-041)*

El enrutado de hoy (estación fijada en el plato → estación de su categoría → `null`) se mantiene y
gana cuatro pasos dentro de `CreateOrderHandler`. Las letras son las del diseño; el **orden de
ejecución** en el handler es: modo (0) → nombre (d) → catálogo y `ITEM_NOT_IN_MODE` → estaciones
encendidas (a) → override por mesa (b) → snapshots (c).

| # | Paso | Resultado |
|---|---|---|
| 0 | Se lee el modo del local **una vez por comanda** con `IVenueModeProvider.GetAsync(tenantId)` (SDD-001 §7). Si el proveedor lanza `VenueModeUnavailableException`, la comanda responde `503 VENUE_MODE_UNAVAILABLE` sin crearse | `mode ∈ {RESTAURANT, NIGHT}` (o `503 VENUE_MODE_UNAVAILABLE`) |
| a | **Solo estaciones encendidas.** (1) Cada plato se resuelve contra TODAS las estaciones del tenant, igual que hoy: la estación fijada en el plato si existe; si no, la de su categoría (única por `CATEGORY_CONFLICT`; ante duplicados heredados, la más antigua); si ninguna, `null`. (2) Si la estación resuelta existe y `!IsOnIn(mode)` (`IsOnIn(mode) = is_active && active_in ∋ mode`) → se rechaza la comanda entera con `409 STATION_CLOSED` (`stationId` en la raíz del ProblemDetails, `detail` con nombre y modo), tanto si viene de la fijada como de la categoría. No se busca otra estación ni se aplica el override (b): una estación apagada nunca se «rescata» con la barra de la mesa. (3) Si resuelve a `null`, sigue al paso (b). Al paso (b) solo llegan ítems con una estación encendida o con `null`, y el override solo actúa sobre los de una `BAR` encendida o `null` | `409 STATION_CLOSED` con `detail` legible: `La estación «Parrilla» está cerrada en modo Night.` |
| b | **Override por mesa.** Si `table_layouts(session.table_id, mode).station_id` apunta a una estación **encendida** y el plato ha resuelto a una estación de perfil **`BAR`** o a **ninguna** (`null`) → gana la barra de la mesa (es el «mesa → barra configurable» de la distribución Night). Un plato que resuelve a una `FULL_PASS` (cocina, parrilla) **nunca** se desvía a una barra | `station_id` = barra de la mesa |
| c | **Snapshots.** `orders.priority` = `1` si `mode = NIGHT` y `tables.kind = RESERVADO`; `0` en cualquier otro caso (de día manda el orden de llegada); `orders.venue_mode` = `mode`. Se sellan al crear y **no se recalculan** si la mesa cambia de tipo o el local de modo | columnas nuevas de `orders` |
| d | **Nombre obligatorio en Night.** Si `mode = NIGHT`, el llamante no trae token de personal (`CallerTenantId == Guid.Empty`: comensal sin token o con Customer JWT, que no lleva `tenantId`) y `participant.DisplayName` está vacío → `422 NAME_REQUIRED`, con o sin `customerAccountId`. Los pedidos con Staff JWT (PDA, Caja) quedan exentos: la PDA es el respaldo de los reservados y sus participantes (`pda-{staffId}`, `staff-{ts}`) no tienen nombre. Se evalúa justo después de resolver el participante, antes del PIN y del catálogo. | `422 NAME_REQUIRED` |

Dónde se lee cada cosa: `SessionReadModel` gana `TableId`. `TableLayoutReadModel` se re-mapea a
`table_layouts(table_id, mode, floor_id, pos_x, pos_y, station_id, active)` con clave
`(table_id, mode)`: la misma proyección sirve al override por mesa y a las coordenadas de la cola y
del manifiesto. `tables.kind` sale de una proyección mínima de `tables` (id, table_code, kind).
Ninguna proyección de Orders mapea `tables.floor_id/pos_x/pos_y`. Todo en solo lectura, fuera de
las migraciones de Orders; las tablas las mantiene Sessions (SDD-003). **El modo se lee una sola
vez por comanda**: si el local cambia de modo a mitad de la petición, la comanda entera se enruta
con el modo con el que empezó, nunca medio y medio.

> **Por qué (a) es un 409 y no un `null`.** Dejar `station_id = null` «silencioso» mandaría la
> comanda a ningún tablero: nadie la vería y el comensal esperaría eternamente. Con el 409 el menú
> explica el problema, y la carta por modo (`visibleIn`, ADR-041) hace que en la práctica no pase:
> un plato de parrilla no se puede pedir de noche porque no está en la carta de noche. La regla
> queda como **red de seguridad** para configuraciones a medias (plato visible de noche cuya
> estación nadie encendió).

### Visibilidad cruzada *(ADR-029)*

El tablero de una estación recibe **la comanda entera**, con todos sus platos en una sola lista
(`items`), cada uno con su `station_id` y su estado. El cliente separa:
- **Accionables**: los ítems cuya `station_id` es la suya. Llevan los botones.
- **Ajenos**: los demás ítems de la misma comanda, **sin acciones**, pero con **el color de su
  estación** y su **estado en vivo** [v2.1] — no en gris: en el pase hay que ver de un vistazo si
  lo que falta para que la mesa salga completa está en cola, en fuegos o ya listo.

No hay platos compuestos ni estación de ensamblaje: cada ítem pertenece a una sola estación.

> **Sin ETA estimado.** ADR-029 retiró del alcance el cálculo de tiempo estimado de salida y
> ADR-035 lo confirma: el sistema no tiene con qué estimar, y un ETA que se incumple es peor que
> ninguno. Lo que se publica son los **sellos por transición**, y con ellos cada interfaz dice
> *desde cuándo* un plato está donde está ("En preparación · desde hace 8 min"), nunca *cuándo
> saldrá*.

---

## Observación de la comanda *(v2.1)*

Las `notes` son **del plato** ("sin sal"); la **observación** es **de la comanda entera** y no cabía
en ninguna línea: alergias de la mesa, "todo para llevar", "servir junto". Vive en
`orders.observacion` (máx. 500 caracteres, nullable).

**Dónde se captura**: en el carrito del menú del comensal y en el PDA, ambos por el mismo campo
`observacion` del cuerpo de `POST /api/v1/orders`.

**Por dónde viaja**: se devuelve en **todos** los DTO de lectura del módulo, para que la misma frase
llegue a las cinco pantallas que la necesitan sin que ninguna tenga que ir a buscarla:

| DTO | Endpoint | Quién lo lee |
|---|---|---|
| `OrderDetailDto` | `GET /orders/{orderId}` | Menú ("Mis pedidos"), caja |
| `OrderSummaryDto` | `GET /orders?sessionId=` | Menú, caja, salón |
| `KdsOrderDto` | `GET /orders/active` | Tablero KDS de cocina |
| `DeliveryQueueItemDto` | `GET /delivery-queue?stationId=` | Cola del pase del runner |
| `DeliveryTripStopItemDto` | `POST /delivery-trips` y `GET /delivery-trips/mine` | Manifiesto de ruta del runner |

En los cinco es un campo **opcional** al final del contrato (`null` cuando no hay observación), para
no romper a los clientes que aún no lo conocen.

---

## Esquema PostgreSQL *(v2.0 · v2.1 · v2.2 diseñado)*

### `order_items` — columnas nuevas

| Columna | Tipo | Notas |
|---|---|---|
| `status` | varchar(20) | Estado del ítem (tabla de arriba), en `SCREAMING_SNAKE`. Índice `(tenant_id, status)` |
| `timing` | varchar(10) | `NOW` \| `LATER` — lo que eligió el comensal al añadirlo |
| `station_id` | uuid null | Estación destino, resuelta al crear |
| `marched_at` | timestamptz null | Cuándo se marchó (null si nació `NOW`) |
| `marched_by` | uuid null | Staff que marchó — trazabilidad de ADR-026 |
| `queued_at` | timestamptz null | *(v2.1)* Cuándo entró en la **cola** de su estación. Se sella al marchar y también **al crear** el plato si `timing = NOW` |
| `preparation_started_at` | timestamptz null | *(v2.1)* Cuándo la estación dio la **orden de preparación** |
| `preparation_ordered_by` | uuid null | *(v2.1)* Staff que dio la orden — trazabilidad de ADR-032 |
| `ready_at` | timestamptz null | Cuándo la estación lo dio por listo |
| `delivery_trip_id` | uuid null FK | Viaje que lo transporta (null si no está reclamado) |
| `delivered_at` | timestamptz null | Cuándo se entregó |
| `updated_at` | timestamptz | **Cursor del delta del KDS** (ADR-022 baja a nivel de ítem) |

Migración de las tres columnas de v2.1: **`AddItemQueuedStateAndStamps`** (`queued_at` y
`preparation_started_at` como `timestamp with time zone` nullable, `preparation_ordered_by` como
`uuid` nullable). El ciclo por ítem completo vino antes en `AddItemLifecycle`.

**Sellos por transición.** Cada entrada de estado deja su marca, así que el tramo de un paso es la
diferencia con el sello siguiente y el del paso actual se calcula en vivo contra "ahora". De ahí
salen sin instrumentación extra la espera en cola (`preparation_started_at − queued_at`), el tiempo
de fuegos (`ready_at − preparation_started_at`) y el recorrido que el KDS pinta en el modo foco —
también el de los platos de la otra estación, vía ADR-029.

Índice adicional `(tenant_id, station_id, status)` — es la consulta que hacen el tablero de estación
y la cola del runner.

### `orders` — columna nueva *(v2.1)*

| Columna | Tipo | Notas |
|---|---|---|
| `observacion` | varchar(500) null | Observación libre de **toda la comanda** (alergias, "todo para llevar"…). Migración **`AddOrderObservacion`** |

Se normaliza al escribir (`Order.SetObservacion`): se recorta y una cadena en blanco se guarda como
`null`, para que la interfaz no tenga que distinguir entre `""` y "sin observación".

### `orders` — columnas nuevas *(v2.2 — diseñado, pendiente de implementación, PRD-019)*

| Columna | Tipo | Notas |
|---|---|---|
| `priority` | int not null default 0 | Snapshot al crear: `1` si el pedido nace en NIGHT desde una mesa `RESERVADO`, resto `0`. Es un entero y no un booleano para poder añadir niveles (VIP, consumo mínimo) sin migrar. Ordena el tablero KDS y la cola del pase (`priority DESC`, y luego el sello de siempre) — ADR-038 |
| `venue_mode` | varchar(16) not null default 'RESTAURANT' | Modo del local en el que se creó la comanda (`RESTAURANT` \| `NIGHT`). Base de los reportes por modo (SDD-006 `?mode=`). **No se recalcula** al cambiar de modo |

Migración **`AddOrderVenueModeAndPriority`**. Índice `(tenant_id, venue_mode, created_at)` para los
cortes por modo de Analytics. Los `default` dejan a los pedidos históricos como `RESTAURANT` con
prioridad `0`, que es la verdad: todos se hicieron de día.

### `kds_stations` — esquema completo *(v2.2 — diseñado, pendiente de implementación, PRD-019)*

La tabla existe desde v1.0 pero no tenía bloque propio en este SDD; se documenta entera porque v2.2
la cambia (ADR-037).

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid | Índice |
| `name` | varchar(100) not null | Único por tenant (case-insensitive trim, validado en el handler) |
| `category_ids` | jsonb | Lista de GUID (string) de las categorías del catálogo que enrutan aquí |
| `is_active` | bool not null default true | *(v2.2)* Interruptor general. Apagada no recibe comandas **en ningún modo**; es la alternativa a borrarla cuando tiene histórico |
| `flow_profile` | varchar(16) not null default 'FULL_PASS' | *(v2.2)* `FULL_PASS` \| `BAR`. Decide las columnas del tablero y si se permite `QUEUED → READY`. Supone OQ-1904 = sí; si la respuesta es no, pasa a jsonb por modo (`{"RESTAURANT":…,"NIGHT":…}`, `ProfileIn(mode)`) antes de la migración (ADR-037) |
| `active_in` | jsonb not null default '["RESTAURANT","NIGHT"]' | *(v2.2)* Modos en los que se enciende. Nunca vacía (`ACTIVE_IN_EMPTY`) |
| `created_at` | timestamptz | |
| `updated_at` | timestamptz | |

Migración **`AddKdsStationProfileAndModes`** (las tres columnas con `default`, así las estaciones
existentes —Cocina, Parrilla, Barra— siguen encendidas en ambos modos y con el flujo completo hasta
que Administración diga otra cosa). **«Encendida» no es una columna**: se calcula en el dominio,
`KdsStation.IsOnIn(mode) => IsActive && ActiveIn.Contains(mode)`, y viaja en los DTO como `isOn`
(evaluado con el modo activo en el momento de responder). Guardar el derivado invitaría a que se
desincronizara al cambiar de modo.

### `delivery_trips` — tabla nueva

| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | |
| `tenant_id` | uuid FK | |
| `runner_id` | uuid FK | Staff con rol Runner |
| `station_id` | uuid FK | La estación a la que está adscrito (ADR-027) |
| `status` | text | `OPEN` \| `COMPLETED` \| `RELEASED` |
| `created_at` | timestamptz | |
| `completed_at` | timestamptz null | |

Índice único parcial: **un solo viaje `OPEN` por runner** (`WHERE status = 'OPEN'`).

---

## Eventos SignalR del módulo — `VENUE_MODE_CHANGED` *(diseñado — pendiente de implementación, PRD-019)*

Hub `OrdersHub` en `/hubs/orders`; método de cliente **`OrderEvent`**; grupo **`tenant:{tenantId}`**
(se entra con `JoinTenantGroup`, y el tenant se toma de las claims, no del parámetro). Hasta v2.1 el
hub solo transporta eventos del ciclo por ítem; v2.2 añade **uno que no nace en Orders**: el cambio
de modo del local lo decide Identity (`POST /api/v1/venue/mode`, SDD-001 §7) y lo publica como
`VenueModeChangedNotification` (MediatR, `MestoBeach.Shared`); el handler
`VenueModeChangedBroadcastHandler` (`MestoBeach.Orders.Application`) de **este módulo** lo convierte
en un `OrderEvent` y lo difunde por `IOrderSignalRNotifier.NotifyTenantAsync`. Se reutiliza
`OrdersHub` y no `/hubs/realtime` porque es el hub del personal que ya está en uso: hoy se conectan
a él KDS, Caja y Salón (`ORDERS_HUB_URL`), ya tiene `JoinTenantGroup` y
`IOrderSignalRNotifier.NotifyTenantAsync`, y ninguna vista usa `/hubs/realtime`. Las demás vistas
no lo usan hoy: PDA solo usa `SessionsHub`, y Runner, Recepción y el admin no usan SignalR. v2.2
les añade la conexión desde el `VenueProvider` del `(console)/layout.tsx` de staff y del
`(dashboard)/layout.tsx` de admin.

| Evento | Grupo | Payload | Lo emite |
|---|---|---|---|
| `VENUE_MODE_CHANGED` | `tenant:{tenantId}` | `{ "type": "VENUE_MODE_CHANGED", "mode": "NIGHT", "previous": "RESTAURANT", "changedAt": "2026-10-10T22:00:04Z" }` — objeto **plano**, como el resto de eventos del hub (`ITEMS_CLAIMED`, `order.created`): sin sobre `payload` ni `tenantId` (el grupo ya es del tenant). Son los campos de `POST /venue/mode` menos `changed`. `mode` y `previous` son strings en mayúsculas (`mode.ToString()`; SignalR no convierte enums) | `VenueModeChangedBroadcastHandler` (`MestoBeach.Orders.Application`), handler de `VenueModeChangedNotification`, tras el `200` de `POST /venue/mode` |

**Quién lo escucha**
- **Staff** (`VenueProvider` del `(console)/layout.tsx`): KDS, Salón, PDA, Runner, Recepción y Caja. Al
  recibirlo cambian `data-venue`, disparan la transición y **vuelven a pedir** `GET /api/v1/venue`
  (el evento es un aviso; la verdad se relee — ADR-022). El KDS además recalcula el `isOn` de su
  estación y muestra «Cerrada en Night» si procede.
- **Admin** (`(dashboard)/layout.tsx`): conexión propia al hub con `JoinTenantGroup`; sirve para
  que un segundo administrador vea el cambio sin recargar y para cerrar la animación cuando el
  cambio lo hizo otro.
- **Menú del comensal: no.** Es anónimo y no se conecta a `OrdersHub` (`[Authorize]`); sondea
  `GET /api/v1/venue/{tenantId}` cada 30 s y al volver visible. Un cambio de modo con comensales
  pidiendo no debería ocurrir —la transición está cerrada: `409 VENUE_NOT_IDLE`—, así que la
  latencia del sondeo no afecta a nadie con una comanda a medias.

Es **best-effort**, como el resto del hub: si la difusión falla se registra
`venue_mode.signalr_broadcast_failed` (warning) y el cambio de modo ya está persistido; el sondeo
de `GET /venue` sana a los clientes en ≤ 30 s. No se emite cuando el `POST` fue idempotente
(`changed: false`).

---

## Endpoints

### POST /api/v1/orders

**Objetivo**
Crea un pedido nuevo a partir del carrito confirmado por el cliente, valida los ítems contra el catálogo MongoDB, enruta ítems a estaciones KDS y dispara eventos SignalR.

**Auth**
Customer JWT (participant token con claim `participantId` y `sessionId`)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "participantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "items": [
    {
      "catalogItemId": "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b6",
      "quantity": 2,
      "notes": "sin sal por favor",
      "loyaltyRedeemed": false,
      "selectedOptionIds": ["opt-uuid-termino-3-4", "opt-uuid-extra-queso"]
    },
    {
      "catalogItemId": "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b7",
      "quantity": 1,
      "notes": null,
      "loyaltyRedeemed": true
    }
  ],
  "observacion": "Alergia al marisco en toda la mesa"
}
```

**Response 201**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "participantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "PENDING",
  "stationId": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "cancellationReason": null,
  "createdAt": "2026-01-15T12:34:56Z",
  "confirmedAt": null,
  "observacion": "Alergia al marisco en toda la mesa",
  "items": [
    {
      "id": "d4e5f6a7-b8c9-0123-defa-234567890123",
      "catalogItemId": "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b6",
      "itemNameSnapshot": "Hamburguesa Clásica",
      "itemPriceSnapshotCents": 1450,
      "quantity": 2,
      "notes": "sin sal por favor",
      "loyaltyRedeemed": false,
      "selectedModifiers": "Tres cuartos · + Queso"
    },
    {
      "id": "e5f6a7b8-c9d0-1234-efab-345678901234",
      "catalogItemId": "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b7",
      "itemNameSnapshot": "Jugo Natural",
      "itemPriceSnapshotCents": 650,
      "quantity": 1,
      "notes": null,
      "loyaltyRedeemed": true
    }
  ]
}
```

**Validaciones**
- `sessionId`: UUID v4, requerido
- `participantId`: UUID v4, requerido
- `items`: array, requerido, mínimo 1 elemento, máximo 30 ítems distintos por pedido
- `items[].catalogItemId`: string, requerido, GUID del ítem del catálogo
- `items[].quantity`: integer, requerido, mínimo 1, máximo 50
- `items[].notes`: string, opcional, máximo 300 caracteres (solo se persiste si el item tiene `allowNotes = true`)
- `items[].loyaltyRedeemed`: boolean, opcional, default `false`
- `items[].selectedOptionIds`: array de UUID, opcional — ids de opciones de modificador elegidas por el cliente
- `items[].timing`: string, opcional — `NOW` \| `LATER` (case-insensitive). Ausente equivale a `NOW`
- `observacion`: string, opcional, máximo 500 caracteres — observación libre de toda la comanda [v2.1]
- *(v2.2 — diseñado, PRD-019)* Comensal sin token de personal (`CallerTenantId == Guid.Empty`) cuyo participante no tiene `DisplayName`, en NIGHT → `422 NAME_REQUIRED`; Staff JWT exento (fila (d) de §«Enrutado por modo»). Se comprueba justo después de resolver el participante, antes del PIN y del catálogo

**Reglas de Negocio**
- **Modo del local** [v2.2 — diseñado, PRD-019 — ADR-036/037/038/041]: el handler lee el modo
  **una sola vez** por comanda (`IVenueModeProvider.GetAsync`, SDD-001 §7) y con él:
  - **Carta por modo** (`ITEM_NOT_IN_MODE`): `CatalogValidationService.ValidateAsync` recibe el modo
    y aplica la regla única: **pedible en el modo M ⇔ `item.visibleIn ∋ M` ∧
    `category(item.categoryId).visibleIn ∋ M`**; si falta o es null en el ítem o en la categoría,
    vale ambos modos; si la categoría no se encuentra, vale ambos modos. Un ítem no pedible en el
    modo cuenta como tal aunque exista y esté `available`. La caché `catalog:{tenantId}` se guarda
    **sin filtrar** (ADR-041) y el filtro se aplica aquí y en `GET /catalog/{tenantId}` (SDD-002).
    Respuesta `409 ITEM_NOT_IN_MODE` con `itemIds` en la raíz del ProblemDetails y un `detail` con
    los nombres («Chuletón» no está en la carta de noche), sin crear la orden. Es 409 y no 422
    porque el carrito era válido cuando se armó: lo que cambió es el estado del local.
    - **Validación** *(v2.2 — diseñado)*: Orders no usa los modelos de Catalog, así que su read
      model se amplía en los dos caminos: L1 `CatalogCacheDto` gana `Categories: [{ Id, VisibleIn }]`
      e `Items[].VisibleIn` (la caché ya trae `Categories`, porque es el `TenantCatalog`
      serializado); L2 `CatalogReadDocument` gana `categories[] { _id (string), visibleIn }` e
      `items[].visibleIn`. En ambos el default `["RESTAURANT","NIGHT"]` se aplica cuando el campo
      falta o es null, nunca `[]`. L2 sigue sin escribir la caché.
    - **Contrato**: `ValidateAsync(tenantId, ids, mode, ct)` devuelve
      `CatalogValidationResult(ValidItems, InvalidItemIds, NotInModeItems: [{Id, Name}])`: no existe
      o tiene `deletedAt` → `InvalidItemIds`; existe pero no es pedible en el modo (aunque esté
      agotado) → `NotInModeItems`; pedible pero `available=false` → `InvalidItemIds`. En el handler,
      `NotInModeItems` no vacío → `409 ITEM_NOT_IN_MODE` (`itemIds` en la raíz del ProblemDetails,
      `detail` con los nombres), evaluado antes del `422 INVALID_ITEMS`, sin crear la orden.
  - **Estaciones encendidas, override por mesa y snapshots**: los pasos (a), (b) y (c) de
    §«Enrutado por modo». Un plato que resuelve a una estación apagada en este modo →
    `409 STATION_CLOSED` (ningún ítem se crea); la barra configurada para la mesa en la
    distribución del modo gana sobre la estación de categoría si el plato es `BAR` o sin estación;
    `orders.priority` y `orders.venue_mode` se sellan al crear.
  - `priority` y `venueMode` **no** se añaden al cuerpo del `201`: son datos del lado de lectura y
    viajan en `KdsOrderDto`, `DeliveryQueueItemDto` y `DeliveryTripStopItemDto`.
- Si `sessions.pin_required = true` (se sella al abrir la sesión: según la mesa y, en NIGHT, forzado; SDD-003 §POST /sessions) se comprueba el PIN con `PinValidationService` *(v2.2 — diseñado, PRD-019 — ADR-040)*: si la clave Redis `pin_validated:{t}:{s}:{p}` existe, pasa. Si no existe, o Redis falla, se usa `session_participants.pin_validated` del participante que el handler ya cargó (`SessionParticipantReadModel` y `ParticipantInfo` ganan `PinValidated`, mapeado a `pin_validated`, solo lectura, `ExcludeFromMigrations`). Si es true, se re-siembra la clave con `SET … "1" EX 12h` (best-effort: si Redis falla, warning y no se bloquea el pedido; log `order.pin_reseeded` {tenantId, sessionId, participantId}) y pasa. Si es false → 403 `PIN_REQUIRED` sin crear la orden. Postgres es la verdad.
- La sesión indicada debe existir en PostgreSQL y estar en estado `OPEN`; el `participantId` debe pertenecer a esa sesión
- Cada `catalogItemId` se valida contra el catálogo MongoDB del tenant: primero se consulta Redis `catalog:{tenantId}`; si hay cache hit, se valida en memoria; si hay cache miss, se consulta MongoDB directamente
- Si Redis miss y MongoDB no responde en 2 000 ms → retornar 503 con `{ "code": "CATALOG_UNAVAILABLE" }` sin crear la orden
- Los snapshots `itemNameSnapshot` e `itemPriceSnapshotCents` se capturan del catálogo en el momento de creación; cambios posteriores al catálogo no afectan órdenes existentes
- **Modificadores / pricing autoritativo** [v1.x]: el `CatalogValidationService` resuelve los `modifierGroups` del item (embebidos en el read-model de catálogo) y por cada línea:
  - Valida que cada `selectedOptionIds` pertenezca a un grupo del item y esté `available` (anti-tampering); selección ajena → 422 `INVALID_MODIFIERS`.
  - Valida cardinalidad: SINGLE = a lo sumo 1 (y exactamente 1 si `required`); MULTI respeta `minSelect`/`maxSelect`/`required`; REMOVE sin restricción.
  - **Recalcula** `itemPriceSnapshotCents = base + Σ priceDeltaCents` de las opciones elegidas (REMOVE aporta 0). El precio **nunca** se toma del cliente.
  - Construye `selectedModifiers`, un resumen legible ("Tres cuartos · sin Cebolla · + Michelada") que se persiste como snapshot en `order_items.selected_modifiers` y viaja en los DTOs (KDS, cashdesk, "Mi cuenta").
- Enrutamiento a estación KDS: para cada ítem, buscar en `kds_stations` donde `category_ids @> '["{categoryId}"]'::jsonb` para el `tenantId` del tenant; si hay coincidencia, asignar `station_id` de esa estación; si múltiples estaciones coinciden (misconfiguration), usar la primera por `created_at ASC`; si ninguna coincide, `station_id = null`
- Una sola orden tiene un único `stationId` (el del primer ítem que coincida); si hay ítems en diferentes categorías de estaciones distintas, la orden queda asignada a la estación del primer ítem con match; esto es una limitación de diseño actual — revisar en versión 2
- **Atribución al cliente** [v1.x]: al crear, se denormalizan en `orders` las columnas `customer_account_id` (nullable; del participante si está logueado) y `customer_name` (nombre del participante). Permite que el **KDS/mesero muestren quién pidió** y que el **CRM de clientes** (SDD-009) agregue gasto/visitas por cliente cross-tenant.
- **Estado con el que nace cada plato** [v2.1 — ADR-032]: `timing = LATER` → `HELD`; cualquier otro caso —incluido el campo ausente— → **`QUEUED`**, con `queued_at` sellado en el acto. Un plato pedido "ahora" **entra en la cola de su estación, no en los fuegos**: nada entra en preparación sin que la estación dé la orden.
- **Observación de la comanda** [v2.1]: `observacion` se guarda recortada; en blanco se persiste como `null`. Es de la comanda entera, no de una línea (para eso están `items[].notes`).
- **La orden se crea y se auto-confirma a `CONFIRMED` en la misma operación** [v1.1]. En el flujo de QR-ordering el acto del cliente de confirmar su carrito **es** la confirmación del pedido — no hay paso de aprobación de staff (ver PRD-003: "Cliente confirma pedido → Nuevo", y RF "aparece en el KDS <2s después de ser confirmado por el cliente"). El handler ejecuta `order.Advance(Confirmed)` antes de persistir, dejando `confirmed_at = NOW()`. Esto evita que la orden quede atascada en `PENDING` (estado que el KDS filtra y por tanto nunca mostraría). `PENDING` queda como estado transitorio interno; la transición manual `PENDING → CONFIRMED` por staff (más abajo) sigue existiendo por compatibilidad pero ya no es necesaria en el flujo normal.
- Después de insertar exitosamente en PostgreSQL, llamar a SDD-007 `POST /api/v1/notifications/broadcast` con:
  - Grupo `station:{tenantId}:{stationId}` con evento `order.created` (si `stationId != null`)
  - Grupo `tenant:{tenantId}` con evento `order.created`
  - Si `stationId = null`: solo al grupo `tenant:{tenantId}`
- Los ítems con `loyaltyRedeemed = true` no contribuyen al cálculo de puntos de fidelidad (el cálculo ocurre en SDD-005 al pagar)

**Errores**
| Código | Condición |
|---|---|
| 400 | Body mal formado o campos obligatorios ausentes |
| 400 | `items` es un array vacío |
| 401 | Token JWT ausente o inválido |
| 403 | PIN no validado para este participante (`code: "PIN_REQUIRED"`) |
| 404 | `sessionId` no existe o no pertenece al tenant del JWT |
| 404 | `participantId` no pertenece a la sesión indicada |
| 422 | Sesión no está en estado `OPEN` |
| 422 | Uno o más `catalogItemId` no existen en el catálogo del tenant (respuesta incluye `invalidItemIds: [...]`) |
| 422 | Selección de modificadores inválida — opción ajena/no disponible o no cumple required/min/max (`code: "INVALID_MODIFIERS"`) |
| 422 | *(v2.2 — diseñado)* Modo `NIGHT`, comensal sin token de personal y participante sin nombre (`title: "NAME_REQUIRED"`, `detail`: «Dinos tu nombre para poder llevarte el pedido a la mesa») |
| 409 | *(v2.2 — diseñado)* Uno o más ítems no están en la carta del modo activo (`title: "ITEM_NOT_IN_MODE"`, `itemIds: [...]` en la raíz) |
| 409 | *(v2.2 — diseñado)* Un ítem resuelve a una estación apagada en el modo activo (`title: "STATION_CLOSED"`, `stationId` en la raíz, `detail` con el nombre de la estación y el modo) |
| 503 | Catálogo no disponible — Redis miss + MongoDB timeout (`code: "CATALOG_UNAVAILABLE"`) |
| 503 | *(v2.2 — diseñado)* No se puede saber el modo — Redis y Postgres caídos (`title: "VENUE_MODE_UNAVAILABLE"`) |

*(v2.2 — diseñado)* Ejemplos de los errores nuevos:

```http
HTTP/1.1 409 Conflict

{
  "title": "ITEM_NOT_IN_MODE",
  "status": 409,
  "detail": "«Chuletón» no está en la carta de noche.",
  "itemIds": ["<catalogItemId>"]
}
```

```http
HTTP/1.1 409 Conflict

{
  "title": "STATION_CLOSED",
  "status": 409,
  "detail": "La estación «Parrilla» está cerrada en modo Night.",
  "stationId": "<uuid>"
}
```

```http
HTTP/1.1 422 Unprocessable Entity

{
  "title": "NAME_REQUIRED",
  "status": 422,
  "detail": "Dinos tu nombre para poder llevarte el pedido a la mesa."
}
```
(`ProblemDetails` de `ApiController.Problem`: `title` = código, `detail` = mensaje y las `extensions`
del `Error` al nivel raíz, igual que `VENUE_NOT_IDLE` en SDD-001 §7 y `TABLE_CLOSED_IN_MODE` en
SDD-003.)

> **Nota de implementación (v2.2).** `ApiController.Problem` mapea `ErrorType.Validation` a **400**
> y `ErrorType.Conflict` a **409**; no existe hoy un tipo que produzca **422**. Para que
> `NAME_REQUIRED` salga como 422 —y para que `INVALID_MODIFIERS` y «sesión no `OPEN`», documentados
> arriba como 422, dejen de salir como 400— hace falta un `ErrorType.UnprocessableEntity` en
> `MestoBeach.Shared` con su rama en el mapeo. Los dos 409 nuevos encajan en `ErrorType.Conflict`
> con `Extensions`, como `plan_limit_exceeded`.

**Observabilidad**
- Log: `"order.created"` `{ orderId, sessionId, tenantId, participantId, itemCount, stationId, durationMs }` — *(v2.2)* gana `venueMode` y `priority`
- Log: `"order.pin_required"` `{ sessionId, tenantId, participantId }`
- Log: `"order.catalog_unavailable"` `{ tenantId, catalogLookupDurationMs }`
- Log: `"order.invalid_items"` `{ tenantId, sessionId, invalidItemIds }`
- *(v2.2 — diseñado)* Log: `"order.item_not_in_mode"` `{ tenantId, sessionId, mode, itemIds }`
- *(v2.2 — diseñado)* Log: `"order.station_closed"` `{ tenantId, sessionId, mode, stationId, stationName }` — si se repite, hay un plato visible de noche cuya estación nadie encendió
- *(v2.2 — diseñado)* Log: `"order.name_required"` `{ tenantId, sessionId, participantId }`
- *(v2.2 — diseñado)* Log: `"order.table_station_override"` `{ orderId, tableId, stationId }` (debug) cuando gana la barra de la mesa
- Métrica: `orders.created` (counter) — *(v2.2)* tag `venue_mode`
- Métrica: `orders.catalog_validation_duration_ms` (histogram)
- Métrica: `orders.pin_required_blocks` (counter)
- *(v2.2 — diseñado)* Métrica: `orders.rejected_by_mode` (counter, tag `reason = item_not_in_mode | station_closed | name_required`)

**Casos de Aceptación**
- [ ] Cliente envía carrito válido con ítems existentes → 201 con snapshots correctos, status **`CONFIRMED`** + `confirmedAt`, eventos SignalR disparados, visible de inmediato en el KDS
- [ ] Ítem sin `timing` o con `timing = NOW` → nace en **`QUEUED`** con `queuedAt` sellado, y aparece en la columna "En cola" del KDS, no en "En preparación"
- [ ] Ítem con `timing = LATER` → nace en `HELD`, sin `queuedAt`
- [ ] Comanda con `observacion` → 201 con el campo devuelto y visible en KDS, PDA, caja, salón y reparto
- [ ] `observacion` con solo espacios → se persiste como `null`
- [ ] `observacion` de más de 500 caracteres → 400
- [ ] Sesión con `pin_required=true` y PIN validado en Redis → orden creada exitosamente
- [ ] Sesión con `pin_required=true` y PIN NO validado → 403 con `code: "PIN_REQUIRED"`, orden NO creada
- [ ] *(v2.2 — diseñado)* Participante con `pin_validated = true` en Postgres y la clave Redis borrada → 201 y la clave reaparece con TTL ≈ 12 h; con `pin_validated = false` → 403 `PIN_REQUIRED`
- [ ] Ítem cuya categoría está mapeada a una estación KDS → `stationId` asignado correctamente en la orden
- [ ] Ítem cuya categoría no está mapeada a ninguna estación → `stationId = null`, orden creada
- [ ] Al menos un `catalogItemId` inexistente → 422 con `invalidItemIds` listados
- [ ] Sesión en estado `CLOSED` → 422
- [ ] `participantId` no pertenece a la sesión → 404
- [ ] Redis miss + MongoDB timeout → 503 con `code: "CATALOG_UNAVAILABLE"`
- [ ] Token JWT inválido o expirado → 401
- [ ] `items` vacío → 400
- [ ] *(v2.2 — diseñado)* En `NIGHT`, un comensal sin token de personal y sin nombre → 422 `NAME_REQUIRED`, sin tocar el catálogo; con nombre → 201
- [ ] *(v2.2 — diseñado)* En `NIGHT`, PDA con Staff JWT y participante sin nombre → 201 con `customerName` null
- [ ] *(v2.2 — diseñado)* Participante con `customerAccountId` y sin nombre, sin token de personal → 422
- [ ] *(v2.2 — diseñado)* En `NIGHT`, «Chuletón» (`visibleIn: ["RESTAURANT"]`) → 409 `ITEM_NOT_IN_MODE` con su id en `itemIds`; «Gin tonic» (ambos modos) → 201
- [ ] *(v2.2 — diseñado)* Con la caché fría (`DEL catalog:{t}`, camino L2) y el local en `NIGHT`, un ítem con `visibleIn` ausente cuya categoría tiene `["RESTAURANT"]` → 409 `ITEM_NOT_IN_MODE`
- [ ] *(v2.2 — diseñado)* Con la caché fría, un ítem y una categoría sin `visibleIn` (documento antiguo) → 201 en los dos modos
- [ ] *(v2.2 — diseñado)* Con la caché caliente (camino L1) y el local en `NIGHT`, un ítem con `visibleIn` ausente cuya categoría tiene `["RESTAURANT"]` → 409 `ITEM_NOT_IN_MODE`
- [ ] *(v2.2 — diseñado)* Con la caché caliente, un ítem y una categoría sin `visibleIn` (documento antiguo) → 201 en los dos modos
- [ ] *(v2.2 — diseñado)* En `NIGHT`, un plato cuya estación fijada es Parrilla (`activeIn: ["RESTAURANT"]`) → 409 `STATION_CLOSED` con «Parrilla» y «Night» en el `detail`; ningún ítem creado
- [ ] *(v2.2 — diseñado)* En `NIGHT`, un plato con Parrilla fijada (apagada en NIGHT) cuya categoría enruta a otra estación **encendida** → 409 `STATION_CLOSED` igualmente: manda la fijada y no se busca otra
- [ ] *(v2.2 — diseñado)* Mesa R1 con `table_layouts(R1, NIGHT).stationId = Barra 2` y un gin tonic cuya categoría enruta a Barra 1 (`BAR`) → el ítem nace con `stationId = Barra 2`; un plato de la misma comanda que resuelve a una estación `FULL_PASS` encendida en NIGHT **no** se desvía; si su estación es Cocina apagada en NIGHT, la comanda entera da `409 STATION_CLOSED`
- [ ] *(v2.2 — diseñado)* En `NIGHT`, pedido desde una mesa `kind = RESERVADO` → `orders.priority = 1`; desde `HIGH_TABLE` → `0`; mesa `RESERVADO` en `RESTAURANT` → `priority = 0`; `orders.venue_mode` = modo vigente al crear
- [ ] *(v2.2 — diseñado)* Test unitario con un `IVenueModeProvider` falso que devuelve `NIGHT` en la primera llamada y `RESTAURANT` en las siguientes: `GetAsync` se invoca exactamente una vez por comanda, y `ITEM_NOT_IN_MODE`, el enrutado a estaciones encendidas, el override de barra por mesa y `orders.venue_mode` usan todos `NIGHT`

---

### GET /api/v1/orders/{orderId}

**Objetivo**
Retorna los detalles completos de una orden individual incluyendo todos sus ítems, estado actual y timestamps.

**Auth**
Staff JWT (rol Cajero, Admin, Mesero, Cocina) o Customer JWT (participante que creó la orden) o KDS Device Token

**Request**
- Path params: `{orderId}` — UUID v4 de la orden
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "participantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "IN_PREPARATION",
  "stationId": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "cancellationReason": null,
  "createdAt": "2026-01-15T12:34:56Z",
  "confirmedAt": "2026-01-15T12:35:10Z",
  "customerName": "Lucía",
  "observacion": "Alergia al marisco en toda la mesa",
  "items": [
    {
      "id": "d4e5f6a7-b8c9-0123-defa-234567890123",
      "catalogItemId": "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b6",
      "itemNameSnapshot": "Hamburguesa Clásica",
      "itemPriceSnapshotCents": 1450,
      "quantity": 2,
      "notes": "sin cebolla",
      "loyaltyRedeemed": false,
      "status": "IN_PREPARATION",
      "timing": "NOW",
      "stationId": "c3d4e5f6-a7b8-9012-cdef-123456789012",
      "queuedAt": "2026-01-15T12:35:10Z",
      "preparationStartedAt": "2026-01-15T12:38:02Z",
      "readyAt": null,
      "deliveredAt": null
    }
  ]
}
```
> Cada ítem viaja con **su** estado, su `timing`, su estación y sus **sellos por transición**
> [v2.1]. Es lo que permite al menú del comensal decir "En preparación · desde hace 8 min" sin
> inventarse un ETA: el sistema **no** calcula tiempo estimado de salida (ADR-029 / ADR-035); solo
> publica desde cuándo el plato está en el estado en que está.

**Validaciones**
- `orderId`: UUID v4, formato válido; debe existir en la base de datos

**Reglas de Negocio**
- Staff JWT: puede ver cualquier orden que pertenezca a su `tenantId`; no puede ver órdenes de otros tenants
- Customer JWT: puede ver solo órdenes donde `participantId` del JWT coincide con `participantId` de la orden; si la orden existe pero pertenece a otro participante (aunque sea de la misma sesión) → 403
- KDS Device Token: puede ver cualquier orden de su `tenantId`; no tiene restricción por participante

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente o inválido |
| 403 | Customer JWT intenta ver orden de otro participante |
| 403 | Staff o KDS intenta ver orden de un tenant diferente |
| 404 | Orden con `orderId` no existe |

**Observabilidad**
- Log: `"order.fetched"` `{ orderId, tenantId, requestedBy, role }`
- Métrica: `orders.get_by_id` (counter)

**Casos de Aceptación**
- [ ] Staff con JWT válido recupera orden de su tenant → 200 con todos los campos
- [ ] Cliente recupera su propia orden → 200
- [ ] Cliente intenta recuperar orden de otro participante en la misma sesión → 403
- [ ] `orderId` inexistente → 404
- [ ] Staff intenta ver orden de otro tenant → 403
- [ ] Token JWT inválido → 401

---

### GET /api/v1/orders

**Objetivo**
Retorna todas las órdenes de una sesión dada, con filtros opcionales por estado y estación KDS.

**Auth**
`AllowAnonymous`. Staff JWT, Customer JWT, o cliente anónimo de la mesa.

> **[v1.x] Cuenta compartida:** el listado **no se filtra por participante** — devuelve **todas** las órdenes de la sesión (la sesión es compartida; cada comensal ve la cuenta de toda la mesa, con `items` y `customerName` por orden). El menú ("Mi cuenta y pedidos") prioriza/destaca las del participante actual en el cliente. **Fix:** antes, un Customer JWT logueado mandaba el `customer_account_id` como `CallerParticipantId` y el handler lo rechazaba (`403 No es participante`); ahora ese gate no aplica al listado. El gate por participante **sí** sigue en `GET /orders/{orderId}` (orden individual).

**Request**
- Path params: Ninguno
- Query params:
  - `sessionId` (UUID, **requerido**) — filtra órdenes por sesión
  - `status` (string, opcional) — filtra por estado: `PENDING` | `CONFIRMED` | `IN_PREPARATION` | `READY` | `DELIVERED` | `CANCELLED`
  - `stationId` (UUID, opcional) — filtra por estación KDS; usar `stationId=null` para obtener órdenes sin estación asignada
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "participantId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "status": "PENDING",
      "stationId": "c3d4e5f6-a7b8-9012-cdef-123456789012",
      "cancellationReason": null,
      "createdAt": "2026-01-15T12:34:56Z",
      "confirmedAt": null,
      "itemCount": 3,
      "totalCents": 3550,
      "items": [ … ],
      "customerName": "Lucía",
      "observacion": "Alergia al marisco en toda la mesa"
    }
  ],
  "total": 1
}
```
> `items` (con el estado y los sellos de cada plato) y `observacion` [v2.1] viajan también en el
> listado: el salón y la caja pintan la comanda completa sin una segunda llamada por pedido.

**Validaciones**
- `sessionId`: UUID v4, requerido en query string
- `status`: si se provee, debe ser uno de los valores válidos del ENUM
- `stationId`: si se provee, debe ser UUID v4 válido o el literal `"null"`

**Reglas de Negocio**
- Staff JWT: puede listar órdenes de cualquier sesión que pertenezca a su `tenantId`
- Customer JWT: puede listar órdenes solo de sesiones donde participa; puede ver las órdenes de todos los participantes de la sesión (no solo las propias). El resumen **sí** trae `items` —con el estado y los sellos de cada plato— y `observacion` [v2.1]
- KDS Device Token: puede listar órdenes de su tenant; cuando usa `stationId` que coincide con el suyo, solo ve las órdenes asignadas a esa estación; las órdenes con `stationId = null` son visibles para todos los KDS
- `totalCents` se calcula como `SUM(itemPriceSnapshotCents * quantity)` para todos los ítems no cancelados de la orden
- Resultados ordenados por `createdAt DESC`
- No se aplica paginación en esta versión; si la sesión tiene más de 200 órdenes, retornar 200 con los primeros 200 y header `X-Truncated: true`

**Errores**
| Código | Condición |
|---|---|
| 400 | `sessionId` no está presente en el query string |
| 400 | `status` con valor fuera del ENUM |
| 401 | Token ausente o inválido |
| 403 | Customer JWT no es participante de la sesión solicitada |
| 403 | Staff solicita sesión de otro tenant |
| 404 | `sessionId` no existe |

**Observabilidad**
- Log: `"orders.listed"` `{ sessionId, tenantId, requestedBy, role, filters, resultCount }`
- Métrica: `orders.list` (counter)

**Casos de Aceptación**
- [ ] Staff lista todas las órdenes de una sesión → 200 con array completo ordenado por `createdAt DESC`
- [ ] Filtro por `status=PENDING` devuelve solo órdenes pendientes
- [ ] KDS filtra por `stationId` propio → solo órdenes de esa estación
- [ ] KDS consulta sin filtro de `stationId` → órdenes de su estación + órdenes con `stationId=null`
- [ ] Cliente lista órdenes de su sesión → 200 con todas las órdenes, sus `items` (estado + sellos) y su `observacion`
- [ ] `sessionId` ausente en query → 400
- [ ] Cliente intenta listar sesión donde no participa → 403
- [ ] Sesión con 0 órdenes → 200 con `data: [], total: 0`

---

### PATCH /api/v1/orders/{orderId}/status

> **Obsoleto en v2.0.** El estado del pedido pasa a **derivarse** de sus ítems (ADR-025), así que
> ya no se escribe a mano: el avance ocurre en `/orders/items/{itemId}/march`,
> `/start-preparation`, `/ready` y el cierre de viaje. Se conserva documentado porque es el contrato **vigente en el código actual** y seguirá
> vivo hasta que se implemente el ciclo por ítem; al migrar, este endpoint se retira.

**Objetivo**
Avanza el estado de una orden a través del flujo definido y transmite el cambio vía SignalR a la sesión y al tenant.

**Auth**
Staff JWT (rol Cajero, Admin, Mesero, Cocina) o KDS Device Token

**Request**
- Path params: `{orderId}` — UUID v4 de la orden
- Query params: Ninguno
- Body:
```json
{
  "status": "IN_PREPARATION"
}
```

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "IN_PREPARATION",
  "confirmedAt": null,
  "updatedAt": "2026-01-15T12:36:00Z"
}
```
> `confirmedAt` se setea y se devuelve solo cuando la transición destino es `CONFIRMED`.

**Validaciones**
- `status`: string, requerido; valores aceptados: `CONFIRMED` | `IN_PREPARATION` | `READY` | `DELIVERED`
- `CANCELLED` no es un valor válido en este endpoint; usar `POST /orders/{orderId}/cancel`

**Reglas de Negocio**
Transiciones permitidas (estrictamente secuenciales, sin saltos ni retrocesos):
| Desde | Hacia | Quién puede ejecutar |
|---|---|---|
| `PENDING` | `CONFIRMED` | Staff (Cajero, Admin, Mesero) |
| `CONFIRMED` | `IN_PREPARATION` | Staff (todos los roles) o KDS Device Token |
| `IN_PREPARATION` | `READY` | Staff (todos los roles) o KDS Device Token |
| `READY` | `DELIVERED` | Staff (Cajero, Admin, Mesero) |

- KDS Device Token: solo puede ejecutar `CONFIRMED → IN_PREPARATION` e `IN_PREPARATION → READY`; intentar `PENDING → CONFIRMED` o `READY → DELIVERED` → 403
- Cuando la transición destino es `CONFIRMED`: setear `confirmed_at = NOW()` en la tabla `orders`
- La orden debe pertenecer al tenant del token autenticado
- Después de actualizar en PostgreSQL, llamar a SDD-007 `POST /api/v1/notifications/broadcast`:
  - Grupo `session:{tenantId}:{sessionId}` con evento `order.status_changed { orderId, status, tableCode }`
  - Grupo `tenant:{tenantId}` con evento `order.status_changed { orderId, status, tableCode }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `status` ausente o valor fuera del conjunto permitido |
| 401 | Token ausente o inválido |
| 403 | KDS intenta transición `PENDING → CONFIRMED` o `READY → DELIVERED` |
| 403 | Token pertenece a un tenant diferente al de la orden |
| 404 | Orden con `orderId` no existe |
| 409 | Transición inválida (salto o retroceso de estado) |
| 422 | Orden en estado `CANCELLED` — inmutable |

**Observabilidad**
- Log: `"order.status_changed"` `{ orderId, tenantId, sessionId, fromStatus, toStatus, changedBy, role }`
- Métrica: `orders.status_transitions` (counter, labels: `from_status`, `to_status`)

**Casos de Aceptación**
- [ ] Cajero avanza `PENDING → CONFIRMED` → 200 con `confirmedAt` seteado, SignalR disparado a sesión y tenant
- [ ] KDS avanza `CONFIRMED → IN_PREPARATION` → 200, SignalR disparado
- [ ] KDS avanza `IN_PREPARATION → READY` → 200, SignalR disparado
- [ ] Cajero marca `READY → DELIVERED` → 200, SignalR disparado
- [ ] Transición con salto `PENDING → IN_PREPARATION` → 409
- [ ] Transición inversa `READY → IN_PREPARATION` → 409
- [ ] KDS intenta `READY → DELIVERED` → 403
- [ ] KDS intenta `PENDING → CONFIRMED` → 403
- [ ] Orden en estado `CANCELLED` → 422
- [ ] `orderId` de otro tenant → 403
- [ ] Token inválido → 401

---

### DELETE /api/v1/orders/{orderId}

> **Ruta y auth reales** (`OrdersController.CancelOrder`, `[HttpDelete("{orderId:guid}")]` +
> `[AllowAnonymous]`). El SDD lo documentaba como `POST /orders/{orderId}/cancel` con política de
> Cajero/Admin; en el código **no hay política**: el reparto de permisos lo decide el handler según
> el `callerType` (`anon` / `customer` / `staff` / `other`). El contrato de cuerpo y respuesta que
> sigue es el vigente.

**Objetivo**
Cancela una orden con un motivo obligatorio y notifica a los participantes de la sesión vía SignalR.

**Auth**
`[AllowAnonymous]` en la ruta; el handler resuelve quién puede cancelar por `callerType`.

**Request**
- Path params: `{orderId}` — UUID v4 de la orden
- Query params: Ninguno
- Body:
```json
{
  "reason": "El cliente solicitó cancelación antes de la preparación"
}
```

**Response 200**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "CANCELLED",
  "cancellationReason": "El cliente solicitó cancelación antes de la preparación",
  "cancelledAt": "2026-01-15T12:40:00Z",
  "cancelledBy": "f7a8b9c0-d1e2-3456-fabc-678901234567"
}
```

**Validaciones**
- `reason`: string, requerido, mínimo 5 caracteres, máximo 300 caracteres
- `orderId`: UUID v4, debe existir

**Reglas de Negocio**
- Solo los roles `Cajero` y `Admin` pueden cancelar; los roles `Mesero`, `Cocina` y el KDS Device Token no tienen permiso
- Las órdenes en estado `READY` o `DELIVERED` no pueden cancelarse (la comida ya fue preparada o entregada) → 409
- Las órdenes ya en estado `CANCELLED` → 422 (idempotencia no aplicable en cancel; se interpreta como error de duplicado)
- La orden debe pertenecer al `tenantId` del staff autenticado
- Se almacena el `cancelledBy` (userId del staff) en la columna `cancellation_reason` junto con el motivo; `cancelledAt` se asigna como `NOW()`
- Después de actualizar en PostgreSQL, llamar a SDD-007 `POST /api/v1/notifications/broadcast`:
  - Grupo `session:{tenantId}:{sessionId}` con evento `order.cancelled { orderId, reason }`
  - Grupo `tenant:{tenantId}` con evento `order.cancelled { orderId, reason }`

**Errores**
| Código | Condición |
|---|---|
| 400 | `reason` ausente o menor a 5 caracteres |
| 401 | Token ausente o inválido |
| 403 | Rol del staff no es `Cajero` ni `Admin` |
| 403 | Orden pertenece a otro tenant |
| 404 | Orden con `orderId` no existe |
| 409 | Orden en estado `READY` o `DELIVERED` — no cancelable |
| 422 | Orden ya está en estado `CANCELLED` |

**Observabilidad**
- Log: `"order.cancelled"` `{ orderId, tenantId, sessionId, cancelledBy, fromStatus, reason }`
- Métrica: `orders.cancelled` (counter)

**Casos de Aceptación**
- [ ] Cajero cancela orden en estado `PENDING` → 200 con status `CANCELLED`, SignalR disparado a sesión y tenant
- [ ] Cajero cancela orden en estado `IN_PREPARATION` → 200 con status `CANCELLED`
- [ ] Cajero cancela orden en estado `CONFIRMED` → 200 con status `CANCELLED`
- [ ] Intento de cancelar orden en estado `READY` → 409
- [ ] Intento de cancelar orden en estado `DELIVERED` → 409
- [ ] Intento de cancelar orden ya `CANCELLED` → 422
- [ ] Staff con rol `Mesero` intenta cancelar → 403
- [ ] KDS Device Token intenta cancelar → 403
- [ ] `reason` vacía o menor a 5 chars → 400
- [ ] `orderId` de otro tenant → 403

---

### GET /api/v1/orders/active — tablero autoritativo del KDS [v1.x → v2.0]

> **Cómo bajó al ítem, de verdad (ADR-025 y ADR-029).** El diseño de v2.0 preveía partir la
> respuesta en dos listas (`items` / `otherItems`) y cambiar el conjunto autoritativo por
> `activeItemIds`. **No es lo que se construyó**: el sobre sigue siendo por pedido —hasta v2.1 el
> cursor `since` compara solo `orders.updated_at` (*(v2.2 — diseñado)* también el `updated_at` de
> sus platos; ver «Cursor del delta» abajo) y `activeIds` son ids de **pedido**— y lo que baja al plato
> es el **contenido**: cada elemento de `items` viaja con su `status`, su `timing`, su `stationId`
> y sus sellos, y el cliente hace el reparto. Un pedido entra en el tablero si su estado derivado
> es `CONFIRMED`, `IN_PREPARATION` o `READY`. Sale más barato y da lo mismo: con el `stationId` de
> cada plato, la estación distingue lo suyo de lo ajeno sin que el servidor se lo separe.

**Objetivo**: fuente de verdad del tablero KDS. Una sola consulta devuelve los pedidos **activos** del tenant (`PENDING`/`CONFIRMED`/`IN_PREPARATION`/`READY`) con el `tableCode` resuelto. El KDS lo **sondea** como autoridad; SignalR es solo un *hint* de baja latencia para refrescar antes.

**Auth**: Staff JWT (cualquier rol staff).

**Request** (query): `stationId?` (UUID, `null` para sin estación, u omitir = todas) · `since?` (ISO-8601 UTC — cursor de **delta sync**, P2).

**Response 200**:
```json
{
  "data": [ { "id":"…","status":"IN_PREPARATION","stationId":null,"tableCode":"12",
              "createdAt":"…","confirmedAt":"…","readyAt":null,"updatedAt":"…",
              "items":[ { "id":"…","itemNameSnapshot":"Espeto de sardinas","quantity":2,
                          "status":"QUEUED","timing":"NOW","stationId":"…",
                          "queuedAt":"…","preparationStartedAt":null,
                          "readyAt":null,"deliveredAt":null } ],
              "customerName":null,
              "observacion":"Alergia al marisco en toda la mesa",
              "priority":1, "venueMode":"NIGHT" } ],
  "total": 3,
  "activeIds": ["…","…","…"],
  "serverTime": "2026-06-14T23:00:24.028Z"
}
```
> **Las cuatro columnas del tablero salen de aquí** [v2.1]: el KDS reparte los `items` por su
> `status` (`HELD` → *Esperando orden*, `QUEUED` → *En cola*, `IN_PREPARATION` → *En preparación*,
> `READY` → *En el pase*) y usa los sellos para pintar cuánto lleva cada plato en su paso. Los
> platos de otras estaciones vienen en la misma lista con su `stationId` y su estado en vivo, que
> es lo que permite pintarlos con el color de **su** estación en vez de en gris (ADR-029).
> `observacion` es de la comanda entera y se pinta en la cabecera de la tarjeta.

> **Reservados primero y columnas por perfil** *(v2.2 — diseñado, pendiente de implementación,
> PRD-019 — ADR-037/038)*: cada pedido trae **`priority`** (snapshot al crear: `1` si nació en
> NIGHT desde una mesa `RESERVADO`) y **`venueMode`**, los dos al final del contrato y con valor por defecto
> (`0` / `"RESTAURANT"`) para no romper a los clientes que no los conocen. `data` sale ordenado por
> **`priority DESC, created_at ASC`**: la comanda de un reservado se pinta antes aunque la mesa
> alta pidiera primero; a igual prioridad sigue el FIFO de siempre. El cliente respeta ese orden y
> lo re-aplica al mezclar deltas (`KdsComandaCard` ordena por prioridad). Las estaciones de perfil
> **`BAR`** reparten los mismos `items` en **dos columnas** (`kdsColumnsFor(profile)`) con la regla
> `columnOf(status, 'BAR')` de §«Perfil de flujo `BAR`»: `QUEUED` → «Pedidos» (acción Listo);
> `IN_PREPARATION` → «Pedidos» (acción Listo; llega por un cambio de perfil o por
> `/start-preparation`; el tablero BAR nunca ofrece «¡A preparar!»); `RETURNED` → «Pedidos»
> (acción Listo) con su pastilla «Devuelto — rehacer»; `READY` → «Listo» (sin acción); `HELD` →
> fuera de las columnas (todavía no es trabajo de la barra; lo marcha la PDA o el salón que lo
> pidió, o Admin, con `StaffPdaKdsOrAdmin`, y entra en «Pedidos»; hoy solo PDA/salón crean platos
> `LATER`); `IN_DELIVERY`, `DELIVERED`, `CANCELLED` → fuera; un plato heredado sin estado por ítem
> → «Pedidos». El `flowProfile` y el `isOn` de la
> estación salen de `GET /kds-stations` o de `GET /api/v1/venue` (SDD-001 §7), no de este sobre.

**Reglas de consistencia (KDS):**
- **Socket = hint, poll = verdad.** SignalR es *at-most-once* (sin replay); si se pierde un evento, el sondeo autoritativo sana el tablero en ≤1 ciclo. El cliente refresca también al reconectar, al volver visible y al recuperar red.
- **Delta sync (P2):** sin `since` → snapshot completo (`data` = todos los activos). Con `since` → `data` trae solo los pedidos con `updated_at > since` (*(v2.2 — diseñado)* o con algún plato con `updated_at > since`; ver «Cursor del delta»); `activeIds` es el **conjunto autoritativo** de ids activos para que el cliente **pode** lo que ya salió del tablero (entregado/cancelado) aunque se perdiera el evento. El cliente usa `serverTime` como `since` del siguiente sondeo. `serverTime` se captura **antes** de leer (un solape re-enviado es inofensivo: el upsert es idempotente).
- **Cursor del delta.** Hasta v2.1, `orders.updated_at` solo se mueve cuando cambia el estado **derivado** del pedido (`Order.RecalculateStatusFromItems`, que sale sin tocarlo si el estado no cambia). Por eso un cambio de plato que no altera el derivado no llegaba por delta a las otras tablets ni a las otras estaciones: un bug latente (solo la tablet que pulsa se sana, porque recarga entera). *(v2.2 — diseñado)* Un pedido entra en `data` si `orders.updated_at > since` **o** si alguno de sus platos tiene `order_items.updated_at > since` (`r.Order.UpdatedAt > since || r.Order.Items.Any(i => i.UpdatedAt > since)` en `ListActiveOrdersHandler`). Se filtra en memoria sobre los activos ya cargados con `Include(Items)` y no cambia el contrato. Todas las transiciones de plato ya sellan `order_items.updated_at`, también el `ExecuteUpdate` de `ReadyFromQueue` y el del reclamo de viajes. Índice `(tenant_id, updated_at)`.
- *(v2.2 — diseñado)* **Orden del sobre**: `ORDER BY priority DESC, created_at ASC` en `OrderRepository.ListActiveByTenantAsync` (hoy solo `created_at`). La prioridad es un snapshot y no mueve ningún `updated_at`; `activeIds` no cambia.
- **Frontend (P1):** banner "⚠ Sin sincronizar" si no hay un sondeo exitoso en >25 s (el tablero puede estar desactualizado); y resaltado de **pedidos demorados** (CONFIRMED > 10 min, IN_PREPARATION > 15 min).

**Casos de Aceptación:**
- [ ] Sin `since` → `data` = todos los activos + `activeIds` + `serverTime`.
- [ ] `since` = `serverTime` previo sin cambios → `data` vacío, `activeIds` intacto.
- [ ] Avanzar un activo → aparece en `data` con su nuevo estado y sigue en `activeIds`.
- [ ] Entregar/cancelar → desaparece de `activeIds` (el cliente lo poda) y no viene en `data`.
- [ ] *(v2.2 — diseñado)* Un reservado (`priority: 1`) que pidió **después** de una mesa alta va **antes** en `data`; a igual prioridad manda `createdAt`.
- [ ] *(v2.2 — diseñado)* Un pedido creado en Night trae `venueMode: "NIGHT"`; los históricos salen con `venueMode: "RESTAURANT"` y `priority: 0`.
- [ ] *(v2.2 — diseñado)* Comanda con 2 copas en una estación `BAR`. La tablet A da por listas las dos, una tras otra; la tablet B, con un sondeo delta normal (sin full), recibe el pedido en `data` y ve las dos copas en *Listo*.
- [ ] *(v2.2 — diseñado)* Comanda con 2 platos en `FULL_PASS`: el segundo pasa `QUEUED → IN_PREPARATION`; otra tablet de la misma estación lo recibe por delta aunque el pedido siga en `IN_PREPARATION`.

---

### POST /api/v1/orders/items/{itemId}/march *(v2.0 — ADR-026 · v2.1 — ADR-032)*

**Objetivo**
Marcha un ítem retenido: lo pasa de `HELD` a **`QUEUED`** y lo hace aparecer en la **cola** de su estación, a la espera de la orden de preparación.

**Auth**
Staff JWT con rol **KDS**, **PDA** —o su alias heredado `Mesero`— o **Admin** (política `StaffPdaKdsOrAdmin`). El comensal nunca marcha: desde el móvil solo puede **pedir** su siguiente plato (ADR-026).

> **Cambio en v2.1 (ADR-032).** Marchar ya **no** enciende fuegos: solo mete el plato en la cola.
> Por eso entra la cocina junto al PDA —el pase adelanta lo que ve venir—; el `IN_PREPARATION`
> pasó a `/start-preparation`, que sigue siendo exclusivo de la estación.

**Request**
- Path params: `{itemId}` — UUID del ítem
- Body: ninguno

**Response 200**
```json
{
  "itemId": "7c1f9a02-55d1-4b0e-9a44-2f9d3c6a1b77",
  "orderId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "QUEUED",
  "stationId": "f1a2490e-e8a3-43da-97be-97f9eaeedd34",
  "marchedAt": "2026-08-20T13:41:07Z"
}
```

**Validaciones**
- `{itemId}`: UUID no vacío; el ítem existe y pertenece al tenant del token
- El ítem está en `HELD`; cualquier otro estado es transición inválida
- La sesión de la comanda sigue abierta

**Reglas de Negocio**
- El rol se comprueba **antes de leer nada**: quien no puede marchar tampoco averigua si el plato existe
- Sella `marched_at`, `marched_by` y **`queued_at`** (los tres con la misma marca de tiempo)
- Recalcula el estado derivado del pedido
- Emite `ITEM_MARCHED` a la estación del plato, al grupo del tenant y al de la sesión (el comensal ve que su plato entró en la cola). Si SignalR falla, se registra y **no** se deshace la transición: el sondeo autoritativo sana el tablero

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `ITEM_NOT_FOUND` | 404 | El ítem no existe o es de otro tenant |
| `INVALID_TRANSITION` | 409 | El ítem no está en `HELD` |
| `SESSION_CLOSED` | 409 | La sesión de la mesa ya se cerró |
| `FORBIDDEN_ROLE` | 403 | El token no tiene rol KDS, PDA ni Admin |

**Observabilidad**
- `order_item_marched` con `itemId`, `orderId`, `tenantId`, `stationId`, `marchedBy`, `heldForSeconds`, `orderStatusChanged`
- `order_item.signalr_march_failed` cuando el aviso no sale (warning; la transición ya está persistida)
- Métrica: tiempo medio en `HELD` por sector — indica si los PDA atienden a tiempo

**Casos de Aceptación**
- [ ] Un PDA marcha un ítem `HELD` y aparece en la columna **"En cola"** de su estación en < 2 s
- [ ] La **cocina** puede marchar el mismo plato (v2.1); un token de **Runner** devuelve 403
- [ ] Marchar deja el plato en `QUEUED`, **no** en `IN_PREPARATION`: sigue sin estar en fuegos
- [ ] Marchar dos veces el mismo ítem devuelve 409 la segunda vez
- [ ] El comensal ve el cambio de estado en su móvil sin recargar

---

### POST /api/v1/orders/items/{itemId}/start-preparation *(v2.1 — ADR-032)*

**Objetivo**
La estación da la **orden de preparación**: el plato sale de la cola y entra en los fuegos (`QUEUED → IN_PREPARATION`). Es el "¡a preparar!" del pase.

**Auth**
Staff JWT con rol **KDS** o **Admin** (política `StaffKdsOrAdmin`). Ni el PDA ni el Runner ni el comensal encienden fuegos: el ritmo lo marca quien cocina.

> **La otra mitad de `/march`.** Marchar mete el plato en la cola y lo puede hacer cocina o PDA;
> la orden de preparación es **solo de la estación**. Un plato pedido "ahora" nace ya en `QUEUED`,
> así que este endpoint es el único camino a `IN_PREPARATION` — nada entra en fuegos sin que la
> cocina lo lance.

**Request**
- Path params: `{itemId}` — UUID del ítem
- Query params: ninguno
- Body: ninguno

**Response 200**
```json
{
  "itemId": "7c1f9a02-55d1-4b0e-9a44-2f9d3c6a1b77",
  "orderId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "IN_PREPARATION",
  "stationId": "f1a2490e-e8a3-43da-97be-97f9eaeedd34",
  "preparationStartedAt": "2026-08-25T13:44:19Z"
}
```

**Validaciones**
- `{itemId}`: UUID no vacío; el ítem existe y pertenece al tenant del token (`tenantId` tampoco puede venir vacío: sin tenant resuelto → 400)
- El ítem está en **`QUEUED`**; cualquier otro estado es transición inválida
- Si el token trae estación (`CallerStationId`), tiene que ser la del plato — salvo `Admin`
- La sesión de la comanda sigue abierta

**Reglas de Negocio**
- El rol se comprueba **antes de leer nada**; después va la estación, después la sesión y por último el estado del plato
- Cada plato pertenece a **una sola** estación (ADR-029): quien no sea `Admin` solo lanza lo suyo
- Sella `preparation_started_at` y `preparation_ordered_by` (trazabilidad: quién dio la orden y cuándo)
- Recalcula el estado derivado del pedido (`RecalculateStatusFromItems`)
- Emite **`ITEM_PREPARATION_STARTED`** a la estación del plato, al grupo del tenant y al de la sesión. Lo consumen el tablero de la estación, **las demás estaciones de esa misma comanda** —que lo ven entrar en fuegos en vivo, ADR-029— y el móvil del comensal, que pasa a mostrar "En preparación · desde hace N min"
- Si el aviso SignalR falla, se registra un warning y la transición **se mantiene**: el sondeo autoritativo del KDS sana el tablero en ≤ 1 ciclo (ADR-022)

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `ITEM_NOT_FOUND` | 404 | El ítem no existe o es de otro tenant |
| `WRONG_STATION` | 403 | El token pertenece a otra estación |
| `SESSION_CLOSED` | 409 | La sesión de la mesa ya se cerró |
| `INVALID_TRANSITION` | 409 | El ítem no está en `QUEUED` (mensaje con el estado real: "no está en cola: está en espera") |
| `FORBIDDEN_ROLE` | 403 | El token no tiene rol KDS ni Admin |
| — | 400 | Tenant no resuelto en la petición |

**Observabilidad**
- `order_item_preparation_started` con `itemId`, `orderId`, `tenantId`, `stationId`, `orderedBy`, **`queuedForSeconds`** y `orderStatusChanged`
- `order_item.signalr_start_preparation_failed` cuando el aviso no sale (warning)
- Métrica: `queuedForSeconds` es el **ritmo del pase** — cuánto espera un plato entre que se marcha y que la estación lo lanza. Con `ready_at − preparation_started_at` (tiempo de fuegos) cierra la analítica de servicio sin instrumentación adicional

**Casos de Aceptación**
- [ ] La cocina lanza un plato `QUEUED` → 200, pasa a `IN_PREPARATION` y cambia de columna en < 2 s
- [ ] Un token de **PDA** o de **Runner** recibe 403: marchar sí, encender fuegos no
- [ ] Una estación no puede lanzar un plato de otra estación → 403 `WRONG_STATION`
- [ ] Lanzar un plato que sigue en `HELD` (sin marchar) → 409 `INVALID_TRANSITION`
- [ ] Lanzarlo dos veces → 409 la segunda vez
- [ ] El comensal ve "En preparación" en su móvil sin recargar, con el tiempo transcurrido desde el sello
- [ ] La **otra estación** de la misma comanda ve el plato entrar en fuegos con el color de la estación dueña

---

### POST /api/v1/orders/items/{itemId}/ready *(v2.0 · v2.2 diseñado — perfil `BAR`)*

**Objetivo**
La estación da un ítem por listo: pasa de `IN_PREPARATION` (o `RETURNED`) a `READY` y entra en la cola de runners de esa estación. *(v2.2 — diseñado, PRD-019)* En estaciones de perfil **`BAR`** también **desde `QUEUED`**, sin pasar por los fuegos (ADR-037).

**Auth**
Staff JWT con rol **KDS** o **Admin**, o KDS Device Token. El token debe corresponder a la estación del ítem (o ser Admin).

**Request**
- Path params: `{itemId}`
- Body: ninguno

**Response 200**
```json
{
  "itemId": "7c1f9a02-55d1-4b0e-9a44-2f9d3c6a1b77",
  "status": "READY",
  "readyAt": "2026-08-20T13:52:44Z"
}
```

**Validaciones**
- El ítem está en `IN_PREPARATION` o `RETURNED`; *(v2.2 — diseñado)* o en `QUEUED` **si y solo si** la estación del ítem tiene `flow_profile = BAR`
- La estación del token coincide con `station_id` del ítem (salvo Admin)

**Reglas de Negocio**
- Sella `ready_at`; recalcula el estado derivado del pedido
- Emite `ITEM_READY` al grupo del tenant: lo consumen la cola de runners de esa estación y las demás estaciones, que lo ven actualizado **con el color de esta estación** (ADR-029)
- **Listo desde la cola (`BAR`)** *(v2.2 — diseñado, PRD-019 — ADR-037)*: el handler carga la
  estación del plato (`kds_stations`, una lectura) y, si su `flow_profile = BAR` y el plato está
  `QUEUED`, llama a `OrderItem.ReadyFromQueue()` en vez de `MarkReady()`. Esa ruta sella
  `preparation_started_at = ready_at` y `preparation_ordered_by = staff`, y se persiste con la
  **mecánica única** de §«Perfil de flujo `BAR`» (comprobaciones previas; `ExecuteUpdate`
  condicional dentro de `BeginTransactionAsync`; con 0 filas, rollback, `order_item_ready_race` y
  `409 INVALID_TRANSITION`; con 1 fila, `Entry(item).ReloadAsync()` —no se re-aplica
  `ReadyFromQueue()` a la entidad cargada antes del UPDATE, que pisaría el sello de un
  `/start-preparation` concurrente—, `RecalculateStatusFromItems()`, `SaveChanges` y commit;
  `ITEM_READY` después del commit).
  `IN_PREPARATION → READY` y `RETURNED → READY` siguen como hoy (`MarkReady()` y `SaveChanges`).
  En `FULL_PASS` nada cambia: un plato `QUEUED` sigue necesitando la orden de preparación.
- El evento `ITEM_READY` no cambia de forma; el payload lleva `status: "READY"` igual que hoy

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `ITEM_NOT_FOUND` | 404 | No existe o es de otro tenant |
| `INVALID_TRANSITION` | 409 | El ítem no está en `IN_PREPARATION` ni `RETURNED`; *(v2.2)* o está en `QUEUED` pero su estación es `FULL_PASS` (`detail`: «El plato “…” está en cola: en esta estación primero se da la orden de preparación»); o la escritura atómica no afectó ninguna fila (otro terminal se adelantó o el plato se anuló) |
| `WRONG_STATION` | 403 | El token pertenece a otra estación |

**Observabilidad**
- `order_item_ready` con `itemId`, `stationId`, `prepSeconds` = `ready_at − (marched_at ?? order.confirmed_at ?? order.created_at)`, es decir, desde que el plato entra en la cola de su estación (cola + preparación; NO se mide desde `IN_PREPARATION`). *(v2.2)* gana `fromStatus` (`QUEUED` \| `IN_PREPARATION` \| `RETURNED`) y `flowProfile`. La fórmula de `prepSeconds` no cambia con el perfil: en `BAR` (donde `preparation_started_at = ready_at`) ese mismo tramo es toda la espera de la barra; en `FULL_PASS` sigue siendo cola + preparación, igual que hoy
- *(v2.2 — diseñado)* `order_item_ready_race {ItemId} {StationId}` (información) cuando la actualización atómica no afecta filas — si es frecuente, hay dos tablets pulsando lo mismo
- Métrica: tiempo de preparación por estación y por plato — base de la analítica de cocina; *(v2.2)* tag `flow_profile`

**Casos de Aceptación**
- [ ] Un ítem listo desaparece de la cola de preparación y aparece en la de runners de su estación
- [ ] Una estación no puede dar por listo un plato de otra estación (403)
- [ ] Las demás estaciones de la misma comanda ven el cambio con el color de esta estación (ADR-029)
- [ ] *(v2.2 — diseñado)* En Barra 2 (`BAR`), `/ready` sobre un plato `QUEUED` → 200 `READY`, con `preparationStartedAt == readyAt` y `preparationOrderedBy` = quien pulsó
- [ ] *(v2.2 — diseñado)* En Parrilla (`FULL_PASS`), `/ready` sobre un plato `QUEUED` → 409 `INVALID_TRANSITION`
- [ ] *(v2.2 — diseñado)* Dos tablets de la misma barra pulsan *Listo* a la vez sobre el mismo plato → una recibe 200 y la otra 409; un solo `ITEM_READY`
- [ ] *(v2.2 — diseñado)* Cambiar el perfil de la estación a `BAR` con platos ya `IN_PREPARATION` → `/ready` sigue funcionando sobre ellos

---

### GET /api/v1/delivery-queue *(v2.1 — ADR-027)*

> Documentado en v2.1: el endpoint estaba **construido desde v2.0 pero sin contrato propio** en este
> SDD (el propio `DeliveryQueueController` lo advertía). Aquí queda, incluido el paso de `stationId`
> de obligatorio a **opcional**.

**Objetivo**
Devuelve **la cola del pase** de una estación: los ítems en `READY` que ningún runner ha reclamado
todavía. Es lo que el runner mira antes de armar la bandeja con `POST /delivery-trips`.

**Auth**
`StaffRunnerOrAdmin` — Staff JWT con rol **Runner** o **Admin**.

**Request**
- **Path params**: ninguno.
- **Query params**: `stationId` — UUID, **opcional**. El runner adscrito no lo necesita: la estación
  sale de `ICurrentUser.AssignedStationId`, que v2.2 resuelve de la base en `JwtValidationMiddleware`
  (SDD-001 §7). Existe para Administración, que no tiene estación adscrita y debe decir de qué pase
  lee. *(v2.2, PRD-019)* La app de staff no envía `stationId` para un usuario que no es Admin.
- **Body**: ninguno.

```http
GET /api/v1/delivery-queue?stationId=64b1f2c3-e4b0-a1d2-e3f4-a5b60000000a
Authorization: Bearer <staff-jwt>
```

**Response 200**
```json
{
  "data": [
    {
      "itemId": "8f1c…", "orderId": "a1b2…", "sessionId": "3f1a…",
      "stationId": "64b1…",
      "name": "Pulpo a la brasa", "quantity": 2,
      "notes": "sin pimentón", "selectedModifiers": "[{\"name\":\"Extra limón\"}]",
      "readyAt": "2026-08-25T13:41:07Z",
      "tableCode": "P07", "floorId": "77aa…", "posX": 412.0, "posY": 268.5,
      "customerName": "Ana", "observacion": "Van con prisa, salen a las 15:00",
      "priority": 0
    }
  ],
  "total": 1,
  "serverTime": "2026-08-25T13:41:22Z"
}
```
*(v2.2 — diseñado, PRD-019)* `priority` (int, al final del contrato) es el snapshot de la comanda
(`orders.priority`); la app pinta la pastilla «Reservado» cuando es `> 0`.

`posX`/`posY` y `floorId` vienen del plano para que el runner pueda resaltar el puesto sobre el
mapa del sector sin un segundo viaje. *(v2.2 — diseñado, PRD-019)* `floorId/posX/posY` se resuelven
por `order.session_id → sessions.table_id → table_layouts(table_id, modo activo leído una vez por
petición con IVenueModeProvider)`. Se usa el modo activo (el mismo que pinta `GET /floorplan` sin
`mode`), no `orders.venue_mode`; ya no se busca por `TableCode` contra `tables`. Una mesa sin fila
en ese modo, o con `active = false`, sale con los tres campos a null, igual que hoy un puesto sin
colocar.

**Validaciones**
- `stationId`, si viene, debe ser un **UUID válido** → 400 (validación del controlador, antes del
  handler).
- `GetDeliveryQueueValidator`: `tenantId` y `stationId` no vacíos una vez resuelta la estación.

**Reglas de Negocio**
- **Manda el token, no la query.** Si el token trae estación adscrita y el llamante **no** es Admin,
  pedir otra estación es `403 WRONG_STATION`.
- **Administración puede mirar cualquier pase**: para un Admin la estación efectiva es
  `stationId` de la query y, si no la manda, su adscrita.
- Si no hay estación por ninguna de las dos vías (token sin adscripción y sin `stationId`) → 400.
- El filtro es exactamente `tenant_id` + `station_id` + `status = READY`, **ordenado FIFO por
  `ready_at`**: el plato que lleva más rato en el pase se está enfriando y sale primero. No hace
  falta excluir los ya reclamados porque reclamar cambia el estado a `IN_DELIVERY`
  (`POST /delivery-trips`); liberar el viaje (`/{tripId}/release`) los devuelve a `READY` y por
  tanto a esta cola. *(v2.2 — diseñado, ADR-038)* El orden pasa a **`priority DESC, ready_at ASC`**
  (`OrderRepository.ListReadyItemsByStationAsync`): los platos de un reservado salen primero; a
  igual prioridad, lo más frío primero, como hasta ahora.
- **`serverTime` se captura antes de leer**, igual que en el tablero KDS: la cola también se sondea
  y SignalR es solo un aviso de baja latencia (ADR-022).

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| — | 400 | `stationId` no es un UUID, tenant no resuelto, o no hay estación por token ni por query |
| `WRONG_STATION` | 403 | Un runner adscrito pide la cola de otra estación |
| `FORBIDDEN_ROLE` | 403 | El token no es `Runner` ni `Admin` (política) |

**Observabilidad**
- `delivery_queue.listed {TenantId} {StationId} {ItemCount}`

**Casos de Aceptación**
- [ ] Un runner sin `stationId` en la query recibe la cola de su estación adscrita
- [ ] Un runner que pide `stationId` de otra estación recibe 403 `WRONG_STATION`
- [ ] Un Admin sin estación adscrita y sin `stationId` recibe 400
- [ ] Un Admin con `stationId` recibe la cola de esa estación
- [ ] Un ítem reclamado en un viaje abierto desaparece de la cola; al liberar el viaje, vuelve
- [ ] Cada ítem trae `tableCode`, la `observacion` de su comanda y, si el puesto está colocado en el
      plano, `floorId` + `posX`/`posY`; un puesto sin colocar se lista igual con esos tres en `null`
- [ ] La cola sale ordenada por `readyAt` ascendente (lo más frío, primero)
- [ ] *(v2.2 — diseñado)* Con `?stationId=Barra2`, los platos de R1 (`priority: 1`) salen antes que los de H1 aunque H1 estuviera lista antes; entre los de H1 sigue mandando `readyAt`
- [ ] *(v2.2 — diseñado)* Cada ítem trae `priority`; los históricos de día lo traen a `0`
- [ ] *(v2.2 — diseñado)* En NIGHT, si la Host mueve R1 a otro sector o posición, `/delivery-queue` devuelve el `floorId/posX/posY` de la fila NIGHT y la fila RESTAURANT queda intacta
- [ ] *(v2.2 — diseñado)* Ninguna consulta de Orders lee `tables.floor_id/pos_x/pos_y`, de modo que `DropLegacyTablePosition` no rompe la cola ni el manifiesto

---

### POST /api/v1/delivery-trips *(v2.0 — ADR-027)*

**Objetivo**
El runner arma su bandeja: reclama de forma atómica los ítems `READY` que se lleva, creando su viaje de entrega.

**Auth**
Staff JWT con rol **Runner** (o Admin). La estación sale de la asignación del runner, no del cuerpo de la petición.

**Request**
```json
{
  "itemIds": [
    "7c1f9a02-55d1-4b0e-9a44-2f9d3c6a1b77",
    "9d3e1b55-2c77-4a10-88bf-51a0e4c7d922"
  ]
}
```

**Response 201**
```json
{
  "tripId": "3f7b1e90-6d22-4c31-9f0a-77b2c4e18d55",
  "stationId": "f1a2490e-e8a3-43da-97be-97f9eaeedd34",
  "status": "OPEN",
  "stops": [
    {
      "tableCode": "H03",
      "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca",
      "posX": 190.0,
      "posY": 120.0,
      "items": [
        { "itemId": "7c1f9a02-…", "orderId": "a1b2c3d4-…", "name": "Espeto de sardinas",
          "quantity": 2, "notes": null, "selectedModifiers": null, "status": "IN_DELIVERY",
          "observacion": "Alergia al marisco en toda la mesa" }
      ]
    },
    {
      "tableCode": "H08",
      "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca",
      "posX": 450.0,
      "posY": 280.0,
      "items": [
        { "itemId": "9d3e1b55-…", "orderId": "b2c3d4e5-…", "name": "Pulpo a la brasa",
          "quantity": 1, "notes": "sin pimentón", "selectedModifiers": null,
          "status": "IN_DELIVERY", "observacion": null }
      ]
    }
  ]
}
```
> La respuesta viene **agrupada por puesto** y con sus coordenadas: es exactamente lo que pinta el mapa-manifiesto del runner, sin que el cliente tenga que cruzar datos. Cada plato lleva la **observación de su comanda** [v2.1], que es lo que el runner necesita leer antes de dejarlo en la mesa; *(hasta v2.1)* las paradas salen ordenadas por código de puesto para que la lista sea estable entre recargas; *(v2.2)* `priority DESC`, luego `tableCode` — el orden del recorrido lo decide el runner.

*(v2.2 — diseñado, PRD-019)* El 201 trae `customerName` y `priority` en cada plato y las paradas
salen por `priority DESC` y después `tableCode`; las coordenadas de cada parada salen de
`table_layouts` del modo activo (como en `/delivery-queue`). Contrato de los dos campos en
§`GET /delivery-trips/mine`.

**Validaciones**
- `itemIds`: array requerido, entre 1 y 30 elementos, sin repetidos
- Todos los ítems pertenecen al tenant y están en `READY`
- Todos son de la **estación asignada al runner** (ADR-027)
- El runner no tiene ya un viaje `OPEN` (índice único parcial)

**Reglas de Negocio**
- **Reclamo atómico**: la transición se hace con `UPDATE order_items SET status='IN_DELIVERY', delivery_trip_id=… WHERE id = ANY(...) AND status='READY'`. Si el número de filas afectadas **no coincide** con el de `itemIds`, se revierte la transacción entera y se devuelve 409: otro runner se adelantó
- Todo o nada: no se crea un viaje parcial
- Emite `ITEMS_CLAIMED` al tenant, para que esos platos desaparezcan de la cola del resto de runners

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `ITEM_ALREADY_CLAIMED` | 409 | Al menos un ítem ya no estaba `READY` (otro runner se adelantó) |
| `WRONG_STATION` | 403 | Algún ítem no es de la estación del runner |
| `TRIP_ALREADY_OPEN` | 409 | El runner ya tiene un viaje sin cerrar |
| `TOO_MANY_ITEMS` | 422 | Más de 30 ítems |

**Observabilidad**
- `delivery_trip_created` con `tripId`, `runnerId`, `stationId`, `itemCount`, `stopCount`
- `delivery_claim_conflict` cuando dos runners compiten — si es frecuente, la cola necesita repensarse

**Casos de Aceptación**
- [ ] Dos runners reclaman el mismo plato a la vez: uno recibe 201 y el otro 409, y el plato queda en un solo viaje
- [ ] Un runner de Parrilla no puede reclamar un plato de Cocina (403)
- [ ] Los platos reclamados desaparecen de la cola de los demás runners en < 2 s
- [ ] La respuesta agrupa por puesto y trae coordenadas para el mapa
- [ ] *(v2.2 — diseñado)* El 201 trae `customerName` y `priority` en cada plato, y una bandeja con R1 (reservado) y H1 lista R1 primero, igual que `GET /mine` para el mismo viaje; el cuerpo del 201 y el de `GET /mine` justo después del reclamo son idénticos

---

### GET /api/v1/delivery-trips/mine *(v2.0 · v2.2 diseñado)*

**Objetivo**
Devuelve el viaje abierto del runner autenticado, para que la app se recupere si se cierra o se pierde la red a mitad de ruta.

**Auth**
Staff JWT con rol **Runner**

**Response 200**
Mismo cuerpo que `POST /delivery-trips`. Si no hay viaje abierto: `204 No Content`.

*(v2.2 — diseñado, PRD-019 — ADR-027 ampliado por ADR-038)* Cada plato de cada parada
(`DeliveryTripStopItemDto`, compartido con el `201` de `POST /delivery-trips`) gana dos campos **al
final del contrato**:

| Campo | Tipo | Para qué |
|---|---|---|
| `customerName` | string \| null | Nombre del comensal que lo pidió (`orders.customer_name`). En Night el runner entrega en mesa **preguntando por el nombre** (PRD-019); `null` solo si lo pidió el personal (PDA/Caja), en cualquier modo, o es un invitado sin nombre de día. En Night un pedido del menú nunca sale con null. La app agrupa los platos con null bajo «Comanda de sala · {tableCode}» |
| `priority` | int | Snapshot de la comanda (`orders.priority`). Las paradas con algún plato de `priority > 0` se marcan «Reservado» en la app |

```json
{
  "tripId": "3f7b1e90-6d22-4c31-9f0a-77b2c4e18d55",
  "stationId": "f6a7b8c9-0d1e-4f23-a456-7890abcdef01",
  "status": "OPEN",
  "stops": [
    {
      "tableCode": "R1", "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca", "posX": 190.0, "posY": 120.0,
      "items": [
        { "itemId": "7c1f9a02-…", "orderId": "a1b2c3d4-…", "name": "Gin tonic", "quantity": 2,
          "notes": null, "selectedModifiers": null, "status": "IN_DELIVERY", "observacion": null,
          "customerName": "Marta", "priority": 1 },
        { "itemId": "9d3e1b55-…", "orderId": "c0ffee00-…", "name": "Shot de tequila", "quantity": 4,
          "notes": null, "selectedModifiers": null, "status": "IN_DELIVERY", "observacion": null,
          "customerName": "Jorge", "priority": 1 }
      ]
    },
    {
      "tableCode": "H1", "floorId": "579f4bd6-33a0-45a7-ba3f-2b4639e696ca", "posX": 450.0, "posY": 280.0,
      "items": [
        { "itemId": "2e4d6f80-…", "orderId": "d1e2f3a4-…", "name": "Gin tonic", "quantity": 1,
          "notes": null, "selectedModifiers": null, "status": "IN_DELIVERY", "observacion": null,
          "customerName": "Lucía", "priority": 0 }
      ]
    }
  ]
}
```

**Reglas de Negocio**
- Es la fuente de verdad de la ruta: la app **no** guarda el manifiesto solo en memoria (ADR-024: la red se cae)
- *(v2.2 — diseñado)* `customerName` y `priority` salen de la misma consulta del manifiesto
  (`ListManifestAsync` ya une `orders`: solo falta proyectar `customer_name` y `priority`), sin
  viajes extra. `TripManifestRow` gana `CustomerName` y `Priority`, y el armado de las paradas se
  **unifica en un único método** (hoy hay dos copias: `CreateDeliveryTripHandler.BuildStops`, que
  alimenta el 201 de `POST /delivery-trips`, y `DeliveryTripMapping.ToDto`, que alimenta
  `GET /mine`; el comentario de `DeliveryTripMapping` que dice que la usa `POST` es inexacto). Una
  opción es que `ToDto` llame a `BuildStops`; se unifica solo el armado de las paradas (el
  `stationId` del viaje conserva su origen actual en cada endpoint). Ese único método proyecta
  `customerName` y `priority` y aplica el orden `priority DESC` y luego `tableCode`
- *(v2.2 — diseñado)* **Orden de las paradas**: `priority DESC` (la mayor prioridad entre los platos
  de la parada) y luego `tableCode`, en vez de solo `tableCode`. Las paradas de reservados van
  primero en el manifiesto; dentro de la parada, la app agrupa los platos **por `customerName`**
  («Pregunta por: Marta — 2 gin tonic»); los platos con `customerName` null se agrupan bajo
  «Comanda de sala · {tableCode}». El orden del recorrido lo sigue decidiendo el runner

**Casos de Aceptación**
- [ ] Cerrar y reabrir la app a mitad de ruta devuelve el mismo manifiesto
- [ ] Sin viaje abierto devuelve 204, no un 404
- [ ] *(v2.2 — diseñado)* Cada plato de cada parada trae `customerName` y `priority`; dos comensales de la misma mesa salen con nombres distintos y la app los agrupa
- [ ] *(v2.2 — diseñado)* Una bandeja con platos de R1 (reservado) y H1 lista R1 primero aunque H1 vaya antes alfabéticamente
- [ ] *(v2.2 — diseñado)* En NIGHT, una comanda de la PDA sin nombre → 201 con `customerName` null, y la parada la muestra como «Comanda de sala · {tableCode}»

---

### POST /api/v1/delivery-trips/{tripId}/complete *(v2.0 — ADR-027)*

**Objetivo**
Cierra la ruta: confirma lo entregado y devuelve a su estación lo que no se pudo entregar.

**Auth**
Staff JWT del **runner dueño del viaje** (o Admin)

**Request**
```json
{
  "returnedItemIds": ["9d3e1b55-2c77-4a10-88bf-51a0e4c7d922"]
}
```
> Los ítems del viaje **se dan por entregados por defecto**; solo se enumeran las excepciones. Es el "todo marcado, desmarca lo que falló" de la interfaz.

**Response 200**
```json
{
  "tripId": "3f7b1e90-6d22-4c31-9f0a-77b2c4e18d55",
  "status": "COMPLETED",
  "delivered": 1,
  "returned": 1,
  "finalizedOrderIds": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"]
}
```

**Validaciones**
- El viaje está `OPEN` y pertenece al runner del token
- `returnedItemIds` ⊆ ítems del viaje

**Reglas de Negocio**
- Los no listados pasan a `DELIVERED` con `delivered_at`; los listados pasan a `RETURNED`, se les limpia `delivery_trip_id` y **reaparecen en la cola de su estación de origen** marcados como devueltos
- Recalcula el estado derivado de cada pedido afectado y **auto-finaliza** los que corresponda (ADR-025); devuelve esos ids en `finalizedOrderIds`
- Emite `ITEMS_DELIVERED`, `ITEM_RETURNED` (por cada uno) y `ORDER_FINALIZED` cuando aplique. El comensal ve "entregado" en su móvil
- El viaje pasa a `COMPLETED`: es histórico, no se reabre

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `TRIP_NOT_FOUND` | 404 | No existe o no es del runner |
| `TRIP_NOT_OPEN` | 409 | Ya se cerró |
| `ITEM_NOT_IN_TRIP` | 422 | Un id devuelto no pertenece al viaje |

**Observabilidad**
- `delivery_trip_completed` con `tripId`, `delivered`, `returned`, `durationSeconds`
- Métricas objetivo de PRD-018: **devoluciones / entregas** (OBJ-02) y duración media de ruta

**Casos de Aceptación**
- [ ] Cerrar sin desmarcar nada entrega todos los platos del viaje
- [ ] Un plato desmarcado vuelve a la cola de su estación como devuelto
- [ ] La comanda se finaliza sola si era su último plato y no quedaban retenidos
- [ ] Una comanda con platos `HELD` **no** se finaliza aunque lo servido esté entregado

---

### POST /api/v1/delivery-trips/{tripId}/release *(v2.0)*

**Objetivo**
Libera un viaje abandonado: devuelve sus ítems a `READY` para que otro runner pueda llevarlos.

**Auth**
Staff JWT con rol **Admin** o el propio runner

**Reglas de Negocio**
- Cubre el caso que abre ADR-027: un runner termina su turno —o pierde el móvil— con platos reclamados que quedarían invisibles para el resto
- Los ítems vuelven a `READY` con `delivery_trip_id = null`; el viaje queda `RELEASED`
- Emite `ITEMS_RELEASED` para que reaparezcan en la cola de la estación

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `TRIP_NOT_FOUND` | 404 | No existe |
| `TRIP_NOT_OPEN` | 409 | Ya estaba cerrado o liberado |

**Casos de Aceptación**
- [ ] Al liberar, los platos vuelven a estar disponibles para cualquier runner de esa estación
- [ ] Un runner no puede liberar el viaje de otro (403)

---

### POST /api/v1/orders/{orderId}/request-next *(v2.0 — ADR-026)*

> **Diseñado, no implementado (agosto 2026).** No existe ninguna ruta `request-next` en el código.
> Lo que sí está construido es el aviso por **sesión**, en SDD-003:
> `POST /api/v1/sessions/{sessionId}/ask-eta` (`AllowAnonymous`, `201`, motivo `ETA` junto a
> `WAITER`/`PIN`/`BILL`, evento `ETA_REQUESTED`), que llega a la sala —hoy solo `/salon`— y **nunca** a
> cocina (**ADR-035**). Mientras tanto se conserva la invariante que importa: el comensal no marcha
> nada.

**Objetivo**
El comensal pide su siguiente plato desde el móvil. **No marcha nada**: genera un aviso para el PDA del sector.

**Auth**
Session token del comensal (`AllowAnonymous` con `deviceId`, como el resto de acciones del menú)

**Response 202**
```json
{ "notified": true, "pendingItems": 2 }
```

**Reglas de Negocio**
- Reutiliza la mecánica de llamados de Sessions (`call-waiter` con `reason = NEXT_COURSE`), no un canal nuevo
- Es **idempotente por sesión** durante 60 s: pulsar tres veces no genera tres avisos
- Si la comanda no tiene ítems `HELD`, responde 202 igualmente pero con `pendingItems: 0` y **no** avisa: evita ruido al PDA

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `SESSION_CLOSED` | 409 | La sesión ya se cerró |

**Casos de Aceptación**
- [ ] El aviso llega al PDA del sector en < 2 s
- [ ] Pulsar repetidamente no multiplica los avisos
- [ ] El plato **no** cambia de estado hasta que el PDA lo marcha

---

### GET /api/v1/kds-stations

**Objetivo**
Retorna la lista de estaciones KDS configuradas para un tenant, ordenadas por fecha de creación.

**Auth**
Staff JWT (cualquier rol) o KDS Device Token

**Request**
- Path params: Ninguno
- Query params:
  - `tenantId` (UUID, **requerido**) — tenant cuyas estaciones se quieren listar
- Body: Ninguno

**Response 200**
```json
{
  "data": [
    {
      "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
      "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "name": "Cocina Principal",
      "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b0", "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b1"],
      "createdAt": "2026-01-01T00:00:00Z",
      "isActive": true, "flowProfile": "FULL_PASS", "activeIn": ["RESTAURANT"], "isOn": true
    },
    {
      "id": "d4e5f6a7-b8c9-0123-defa-234567890123",
      "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
      "name": "Barra de Bebidas",
      "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b2"],
      "createdAt": "2026-01-02T00:00:00Z",
      "isActive": true, "flowProfile": "BAR", "activeIn": ["RESTAURANT", "NIGHT"], "isOn": true
    }
  ],
  "total": 2
}
```
*(v2.2 — diseñado, pendiente de implementación, PRD-019 — ADR-037)* `KdsStationDto` gana
`isActive`, `flowProfile`, `activeIn` y **`isOn`**, los cuatro al final del contrato. `isOn` es
**calculado** para el modo activo en el momento de responder (`isActive && activeIn ∋ mode`); en el
ejemplo el local está en `RESTAURANT`, por eso la cocina está encendida. En `NIGHT` la misma
respuesta diría `isOn: false` para la cocina y `true` para la barra.

**Validaciones**
- `tenantId`: UUID v4, requerido; debe coincidir con el `tenantId` del token autenticado

**Reglas de Negocio**
- Staff JWT: puede listar estaciones solo de su propio `tenantId`; si `tenantId` en query difiere del `tenantId` del JWT → 403
- KDS Device Token: puede listar estaciones solo de su propio `tenantId`
- Resultados ordenados por `createdAt ASC` (orden de creación preservado)
- *(v2.2 — diseñado)* `isOn` se calcula por estación con el modo actual, leído **una vez por petición** con `IVenueModeProvider` (SDD-001 §7). **No se filtra**: las apagadas se listan igual, para que Administración y el KDS puedan mostrar «Cerrada en Night» en vez de hacerlas desaparecer
- *(v2.2 — diseñado)* El KDS persiste la estación elegida en `localStorage kds_station:{tenantId}` y arranca con la `assignedStationId` del token si no hay nada guardado; este endpoint es el que le dice el perfil (`flowProfile`) con el que montar el tablero

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` ausente en query string |
| 401 | Token ausente o inválido |
| 403 | `tenantId` en query no coincide con el `tenantId` del token |
| 404 | Tenant no existe |

**Observabilidad**
- Log: `"kds_stations.listed"` `{ tenantId, count, requestedBy, role }`
- Métrica: `kds_stations.list` (counter)

**Casos de Aceptación**
- [ ] Admin lista estaciones de su tenant → 200 con array ordenado
- [ ] *(v2.2 — diseñado)* En modo `NIGHT`, una estación con `activeIn: ["RESTAURANT"]` se lista con `isOn: false`; una con `isActive: false` sale con `isOn: false` en cualquier modo; una `BAR` con ambos modos sale `isOn: true`
- [ ] *(v2.2 — diseñado)* Las estaciones creadas antes de la migración salen con `isActive: true`, `flowProfile: "FULL_PASS"`, `activeIn: ["RESTAURANT","NIGHT"]`
- [ ] Tenant sin ninguna estación configurada → 200 con `data: [], total: 0`
- [ ] `tenantId` de otro tenant → 403
- [ ] `tenantId` ausente en query → 400
- [ ] Token inválido → 401

---

### POST /api/v1/kds-stations

**Objetivo**
Crea una nueva estación KDS con nombre y mapeo de categorías para enrutamiento de pedidos.

**Auth**
Staff JWT (rol Admin únicamente)

**Request**
- Path params: Ninguno
- Query params: Ninguno
- Body:
```json
{
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "name": "Parrilla",
  "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b3", "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b4"],
  "isActive": true,
  "flowProfile": "FULL_PASS",
  "activeIn": ["RESTAURANT"]
}
```
*(v2.2 — diseñado, PRD-019)* `isActive`, `flowProfile` y `activeIn` son **opcionales**; ausentes
valen `true`, `FULL_PASS` y `["RESTAURANT","NIGHT"]` (los mismos `default` de la migración), así
el cuerpo de v1.0 sigue siendo válido.

**Response 201**
```json
{
  "id": "e5f6a7b8-c9d0-1234-efab-345678901234",
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "name": "Parrilla",
  "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b3", "64b1f2c3-e4b0-41d2-a3f4-a5b6000000b4"],
  "createdAt": "2026-01-15T10:00:00Z",
  "isActive": true, "flowProfile": "FULL_PASS", "activeIn": ["RESTAURANT"], "isOn": true
}
```

**Validaciones**
- `tenantId`: UUID v4, requerido; debe coincidir con el `tenantId` del JWT del admin
- `name`: string, requerido, mínimo 2 caracteres, máximo 100 caracteres
- `categoryIds`: array de strings, requerido (puede ser array vacío `[]`); cada elemento debe ser un GUID de categoría (formato 8-4-4-4-12); máximo 50 elementos. *(v2.2)* La unicidad (`CATEGORY_CONFLICT`) entre todas las estaciones del tenant **no** se relaja
- *(v2.2 — diseñado)* `flowProfile`: string, opcional; `FULL_PASS` \| `BAR` (case-insensitive, se guarda en mayúsculas) → `400 INVALID_FLOW_PROFILE` en otro caso
- *(v2.2 — diseñado)* `activeIn`: array de `RESTAURANT` \| `NIGHT`, opcional; sin repetidos; **mínimo 1 elemento** → `400 ACTIVE_IN_EMPTY` si viene `[]`; un valor desconocido → `400 INVALID_MODE`
- *(v2.2 — diseñado)* `isActive`: boolean, opcional

**Reglas de Negocio**
- Solo el rol `Admin` puede crear estaciones KDS
- *(v2.2 — diseñado)* El límite de estaciones del plan (`plan_limit_exceeded`, 403) sigue en el código tal cual, pero no afecta a Mesto Beach, que está en `pro` (`MaxKdsStations = null`): las cuatro estaciones de PRD-019 no requieren cambio de plan. No hay endpoint para cambiar de plan (Platform retirado, ADR-023). `PlanCatalog` no cambia.
- *(v2.2 — diseñado)* `isOn` de la respuesta se calcula con el modo actual, como en `GET /kds-stations`. Una estación creada con `activeIn: ["NIGHT"]` en pleno día nace con `isOn: false` y no recibe comandas hasta que el local cambie de modo
- El `name` debe ser único dentro del tenant (case-insensitive trim)
- Cada `categoryId` en el array debe ser único dentro del tenant entre todas las estaciones; si algún `categoryId` ya está asignado a otra estación del mismo tenant → 422 con lista de IDs en conflicto
- `categoryIds = []` está permitido; esa estación no recibirá ítems por enrutamiento automático (los ítems sin categoría mapeada quedan con `stationId = null`, no en esta estación)
- La estación queda disponible para enrutamiento de órdenes inmediatamente tras la creación

**Errores**
| Código | Condición |
|---|---|
| 400 | Body mal formado o `name` / `tenantId` ausentes |
| 401 | Token ausente o inválido |
| 403 | Rol insuficiente — no es `Admin` |
| 403 | `tenantId` del body no coincide con el del JWT |
| 409 | `name` ya existe para una estación de este tenant |
| 422 | Uno o más `categoryIds` ya están asignados a otra estación del mismo tenant |
| 400 | *(v2.2 — diseñado)* `flowProfile` fuera de `FULL_PASS` \| `BAR` (`code: "INVALID_FLOW_PROFILE"`) |
| 400 | *(v2.2 — diseñado)* `activeIn` vacío (`code: "ACTIVE_IN_EMPTY"`) o con un modo desconocido (`code: "INVALID_MODE"`) |

**Observabilidad**
- Log: `"kds_station.created"` `{ stationId, tenantId, name, categoryCount, createdBy }` — *(v2.2)* gana `flowProfile`, `activeIn`, `isActive`
- Métrica: `kds_stations.created` (counter)

**Casos de Aceptación**
- [ ] Admin crea estación con categorías válidas → 201 con `id` asignado y datos completos
- [ ] Admin crea estación con `categoryIds = []` → 201 sin conflictos
- [ ] Nombre duplicado (case-insensitive) en el mismo tenant → 409
- [ ] Al menos un `categoryId` ya asignado a otra estación del tenant → 422 con `conflictingCategoryIds`
- [ ] Rol `Cajero` intenta crear estación → 403
- [ ] `tenantId` del body difiere del JWT → 403
- [ ] *(v2.2 — diseñado)* Cuerpo de v1.0 (sin los tres campos nuevos) → 201 con `isActive: true`, `flowProfile: "FULL_PASS"`, `activeIn: ["RESTAURANT","NIGHT"]`
- [ ] *(v2.2 — diseñado)* `flowProfile: "GRILL"` → 400 `INVALID_FLOW_PROFILE`; `activeIn: []` → 400 `ACTIVE_IN_EMPTY`; `activeIn: ["DAY"]` → 400 `INVALID_MODE`
- [ ] *(v2.2 — diseñado)* Crear «Barra 2» con `flowProfile: "BAR"`, `activeIn: ["NIGHT"]` de día → 201 con `isOn: false`; tras `POST /venue/mode NIGHT`, `GET /kds-stations` la devuelve `isOn: true`

---

### PUT /api/v1/kds-stations/{stationId}

> **Método real: `PUT`** (`KdsStationsController.Update`, `[HttpPut("{stationId:guid}")]`). El SDD lo
> documentaba como `PATCH`; el cuerpo y el comportamiento parcial descritos abajo son los del código.

**Objetivo**
Actualiza el nombre o la lista de categorías de enrutamiento de una estación KDS existente.

**Auth**
Staff JWT (rol Admin únicamente)

**Request**
- Path params: `{stationId}` — UUID v4 de la estación KDS
- Query params: Ninguno
- Body (al menos uno de los campos requerido — dos en v1.0, cinco desde v2.2):
```json
{
  "name": "Cocina Fría",
  "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b5"],
  "isActive": true,
  "flowProfile": "BAR",
  "activeIn": ["RESTAURANT", "NIGHT"]
}
```

**Response 200**
```json
{
  "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
  "tenantId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "name": "Cocina Fría",
  "categoryIds": ["64b1f2c3-e4b0-41d2-a3f4-a5b6000000b5"],
  "updatedAt": "2026-01-15T11:00:00Z",
  "isActive": true, "flowProfile": "BAR", "activeIn": ["RESTAURANT", "NIGHT"], "isOn": true
}
```

**Validaciones**
- `stationId`: UUID v4, debe existir y pertenecer al tenant del JWT
- `name`: string, opcional; si se provee: mínimo 2 chars, máximo 100 chars
- `categoryIds`: array de strings, opcional; si se provee: reemplaza completamente el array existente (no es aditivo); cada elemento debe ser un GUID de categoría (formato 8-4-4-4-12); máximo 50 elementos. *(v2.2)* La unicidad (`CATEGORY_CONFLICT`) entre todas las estaciones del tenant **no** se relaja
- *(v2.2 — diseñado)* `flowProfile`: opcional; `FULL_PASS` \| `BAR` → `400 INVALID_FLOW_PROFILE`
- *(v2.2 — diseñado)* `activeIn`: opcional; si se provee **reemplaza** la lista; mínimo 1 elemento → `400 ACTIVE_IN_EMPTY`; valor desconocido → `400 INVALID_MODE`
- *(v2.2 — diseñado)* `isActive`: boolean, opcional
- Al menos uno de `name`, `categoryIds` *(v2.2)* `isActive`, `flowProfile` o `activeIn` debe estar presente en el body

**Reglas de Negocio**
- Solo el rol `Admin` puede editar estaciones KDS
- Los campos omitidos en el body no se modifican
- Si `categoryIds` se provee, reemplaza el array completo (no se hace merge)
- Unicidad de `name` se verifica contra otras estaciones del mismo tenant (excluyendo la estación actual)
- Unicidad de `categoryIds` se verifica contra otras estaciones del mismo tenant (excluyendo la estación actual)
- Los cambios afectan el enrutamiento de órdenes futuras únicamente; las órdenes existentes con el `stationId` antiguo no se modifican
- *(v2.2 — diseñado, ADR-037)* **Apagar** una estación (`isActive: false`, o quitar de `activeIn` el modo vigente) **se permite aunque tenga platos vivos**: esos platos conservan su `station_id`, siguen en su tablero (que muestra «Cerrada») y se terminan con normalidad; lo único que cambia es que las comandas **nuevas** la rechazan (`409 STATION_CLOSED`). No se exige vaciarla porque en el momento en que de verdad importa —el cambio de modo— el pre-flight de `POST /venue/mode` garantiza que no queda ninguna mesa abierta ni bandeja en ruta; los platos de mesas ya cerradas se terminan en su tablero
- *(v2.2 — diseñado)* Cambiar `flowProfile` tiene efecto inmediato en el tablero (el KDS lo relee con `GET /kds-stations` o `GET /venue`) y en `/ready`: a `BAR`, los platos `QUEUED` pasan a poder darse por listos; a `FULL_PASS`, los `IN_PREPARATION` siguen siendo válidos. Nada se re-estampa
- *(v2.2 — diseñado)* Es la alternativa a `DELETE` para una estación con histórico: `isActive: false` la saca de juego sin borrar nada

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío — ningún campo modificable presente |
| 400 | *(v2.2 — diseñado)* `flowProfile` fuera de `FULL_PASS` \| `BAR` (`code: "INVALID_FLOW_PROFILE"`) |
| 400 | *(v2.2 — diseñado)* `activeIn` vacío (`code: "ACTIVE_IN_EMPTY"`) o con un modo desconocido (`code: "INVALID_MODE"`) |
| 401 | Token ausente o inválido |
| 403 | Rol insuficiente — no es `Admin` |
| 403 | Estación pertenece a otro tenant |
| 404 | `stationId` no existe |
| 409 | `name` ya existe en otra estación del mismo tenant |
| 422 | Uno o más `categoryIds` ya están asignados a otra estación del mismo tenant |

**Observabilidad**
- Log: `"kds_station.updated"` `{ stationId, tenantId, changedFields, updatedBy }` — *(v2.2)* `changedFields` incluye `isActive`, `flowProfile`, `activeIn` cuando cambian
- *(v2.2 — diseñado)* Log: `"kds_station.turned_off"` `{ stationId, tenantId, liveItems, updatedBy }` (información) cuando se apaga con platos vivos
- Métrica: `kds_stations.updated` (counter)

**Casos de Aceptación**
- [ ] Admin actualiza solo `name` → 200 con nuevo nombre, `categoryIds` intactos
- [ ] Admin actualiza solo `categoryIds` → 200 con nueva lista completa, `name` intacto
- [ ] Admin actualiza ambos campos → 200 con los dos campos actualizados
- [ ] `categoryIds` nuevo en conflicto con otra estación del tenant → 422
- [ ] `name` duplicado en otra estación del mismo tenant → 409
- [ ] Body completamente vacío `{}` → 400
- [ ] *(v2.2 — diseñado)* `{ "flowProfile": "BAR" }` solo → 200 con el perfil nuevo y el resto intacto; un plato `QUEUED` de esa estación acepta `/ready` acto seguido
- [ ] *(v2.2 — diseñado)* `{ "activeIn": ["RESTAURANT"] }` en modo `NIGHT` con platos vivos → 200 con `isOn: false`; los platos vivos siguen en el tablero; una comanda nueva a esa estación → 409 `STATION_CLOSED`
- [ ] *(v2.2 — diseñado)* `{ "activeIn": [] }` → 400 `ACTIVE_IN_EMPTY`; `{ "flowProfile": "x" }` → 400 `INVALID_FLOW_PROFILE`
- [ ] `stationId` de estación de otro tenant → 403
- [ ] `stationId` inexistente → 404
- [ ] Rol `Cajero` intenta editar → 403

---

### DELETE /api/v1/kds-stations/{stationId}

**Objetivo**
Elimina una estación KDS; las órdenes históricas asignadas a ella retienen el `stationId` para auditoría pero el enrutamiento futuro de las categorías de esa estación pasará a `stationId = null`.

**Auth**
Staff JWT (rol Admin únicamente)

**Request**
- Path params: `{stationId}` — UUID v4 de la estación KDS
- Query params: Ninguno
- Body: Ninguno

**Response 204**
(Sin cuerpo)

**Validaciones**
- `stationId`: UUID v4, debe existir y pertenecer al tenant del JWT autenticado

**Reglas de Negocio**
- Solo el rol `Admin` puede eliminar estaciones KDS
- Antes de eliminar, verificar si existen órdenes ACTIVAS con `station_id = stationId` y estado en `('PENDING', 'CONFIRMED', 'IN_PREPARATION', 'READY')`; si existen → 409 con `{ "activeOrderCount": N }` para forzar resolución manual antes de borrar
- Si no existen órdenes activas → hard delete del registro en `kds_stations`
- Las órdenes históricas (estado `DELIVERED` o `CANCELLED`) que referencian el `stationId` eliminado conservan el valor; no hay FK constraint enforced en `orders.station_id` para permitir esto
- Tras la eliminación, las categorías que antes enrutaban a esta estación dejarán de tener match; las nuevas órdenes con esas categorías recibirán `stationId = null`
- *(v2.2 — diseñado, PRD-019)* Para dejar de usar una estación **con histórico** la alternativa es `PUT` con `isActive: false`, que la apaga sin borrar nada; las filas de `table_layouts` (SDD-003) que apunten a una estación borrada se tratan como «sin barra» en el enrutado por mesa

**Errores**
| Código | Condición |
|---|---|
| 401 | Token ausente o inválido |
| 403 | Rol insuficiente — no es `Admin` |
| 403 | Estación pertenece a otro tenant |
| 404 | `stationId` no existe |
| 409 | Existen órdenes activas asignadas a esta estación (`activeOrderCount: N`) |

**Observabilidad**
- Log: `"kds_station.deleted"` `{ stationId, tenantId, deletedBy }`
- Métrica: `kds_stations.deleted` (counter)

**Casos de Aceptación**
- [ ] Admin elimina estación sin órdenes activas → 204
- [ ] Admin intenta eliminar estación con 2 órdenes `IN_PREPARATION` → 409 con `activeOrderCount: 2`
- [ ] Admin intenta eliminar estación con solo órdenes `DELIVERED` y `CANCELLED` → 204 (no son activas)
- [ ] `stationId` inexistente → 404
- [ ] `stationId` de estación de otro tenant → 403
- [ ] Rol `Cajero` intenta eliminar → 403
