# SDD-006 — Analytics Module

## Metadata

| Campo | Valor |
|---|---|
| **ID** | SDD-006 |
| **Versión** | 1.1 |
| **Estado** | Aprobado — v1.0 · v1.1 *(diseñado — pendiente de implementación, PRD-019)* |
| **Fecha** | Octubre 2026 (v1.0: Junio 2026) |
| **PRD de referencia** | PRD-006 — Analytics y Reportes · PRD-019 — Mesto Beach Night (§Reportes) |
| **Módulo backend** | `MestoBeach.Analytics.*` |
| **Depende de** | SDD-003 (Sessions), SDD-004 (Orders), SDD-005 (Payments) |

> **v1.1 — Octubre 2026 — Reportes por modo y día de negocio a las 06:00.** *(diseñado — pendiente de
> implementación; filtro `mode=`: PRD-019 — ADR-036; día de negocio: PRD-019 RF-1962 (revisión prevista de
> ADR-031))*. Todos los endpoints aceptan `mode=RESTAURANT|NIGHT|ALL` (default
> `ALL`) y filtran por `orders.venue_mode`; el **día de negocio** empieza a las **06:00 Europe/Madrid**
> (`VenueTime.BusinessDayStartHour = 6`), de modo que la noche del sábado se cuenta entera en sábado. En el
> mismo pase se documentan dos correcciones del código *(actualizado)*: las consultas excluyen `'Cancelled'`
> pero la base guarda `'CANCELLED'` (el filtro no filtra nada), y `prep-time` agrupa por el
> `orders.station_id` obsoleto en vez de por `order_items.station_id`. Ver §5.3 y §7.

---

## 1. Alcance MVP

| Funcionalidad | MVP | v2 |
|---|---|---|
| Resumen de ventas por período (día/semana/mes) | ✅ | — |
| Top productos más pedidos | ✅ | — |
| Promedios de sesión (ticket, duración, órdenes) | ✅ | — |
| Mapa de calor por hora del día | ✅ | — |
| Tiempo promedio de preparación por estación KDS | ✅ | — |
| Exportación CSV/Excel | ❌ | ✅ |
| DB analítica separada (read replica / data warehouse) | ❌ | ✅ |
| Aggregation jobs en background | ❌ | ✅ |
| Dashboard en tiempo real con SignalR | ❌ | ✅ |
| Reporte de margen bruto (con inventario) | ❌ | ✅ |
| Tasa de conversión QR → pedido | ❌ | ✅ |
| Reportes separados por modo (`mode=RESTAURANT|NIGHT|ALL`) y día de negocio a las 06:00 *(diseñado — PRD-019)* | — | ✅ v1.1 |

**Decisión MVP:** Todas las queries corren directamente sobre PostgreSQL mediante proyecciones de solo lectura. No hay tablas propias de Analytics — cero migraciones. Sin caché; aceptable para volúmenes de MVP.

---

## 2. Modelo de Dominio

Analytics es un módulo **solo lectura**: no tiene entidades de escritura ni agregados de dominio. La capa de dominio define únicamente los DTOs de resultado y las interfaces de repositorio.

### 2.1 Proyecciones de lectura (cross-module, ExcludeFromMigrations)

| Tabla fuente | Columnas leídas | Módulo dueño |
|---|---|---|
| `sessions` | `id`, `tenant_id`, `status`, `created_at`, `closed_at`; **`venue_mode`** *(diseñado — PRD-019)* | Sessions |
| `orders` | `id`, `tenant_id`, `session_id`, `status`, `created_at`, `confirmed_at`, `ready_at`; **`venue_mode`** *(diseñado — PRD-019)*. `station_id` **deja de leerse** (obsoleta desde ADR-029: la estación es del plato) | Orders |
| `order_items` | `id`, `order_id`, `item_name_snapshot`, `item_price_snapshot_cents`, `quantity`; **`station_id`, `queued_at`, `ready_at`** *(diseñado — PRD-019, para `prep-time`)* | Orders |
| `payments` | `id`, `tenant_id`, `session_id`, `total_cents`, `status`, `completed_at` | Payments |
| `kds_stations` | `id`, `tenant_id`, `name` | Orders |

> `orders.ready_at` (Order.cs, al pasar a READY) y `order_items.queued_at`/`ready_at` (OrderItem.cs, ADR-032) ya existen en el schema de Orders. El código actual mide con `orders.ready_at`; el diseño PRD-019 mide por plato con `order_items` (§3.5).

> **Unidad monetaria:** todas las columnas `*_cents` y todos los campos `*Cents` de las respuestas son **enteros en céntimos de euro** (`1450` = 14,50 €). Nunca decimales, nunca euros.

### 2.2 Filtros comunes

Todos los endpoints aceptan los siguientes query params:

| Param | Tipo | Requerido | Descripción |
|---|---|---|---|
| `from` | `date` (ISO 8601, solo fecha) | ✅ | Inicio del período inclusive. v1.0: `from` 00:00:00 hora del local. *(diseñado — PRD-019)*: `from` **06:00:00** hora del local |
| `to` | `date` (ISO 8601, solo fecha) | ✅ | Fin del período inclusive. v1.0: `to` 23:59:59. *(diseñado — PRD-019)*: hasta `to + 1 día` **06:00:00**, exclusivo |
| `mode` | `RESTAURANT` \| `NIGHT` \| `ALL` | ❌ (default `ALL`) | *(diseñado — PRD-019)* Modo del local cuyos datos se agregan; `ALL` no filtra. Case-insensitive. La respuesta lo ecoa |

**Zona horaria:** `Europe/Madrid`. El día de negocio se corta en hora del local, no en UTC (v1.0: a medianoche; *(diseñado — PRD-019)*: a las **06:00**, ver más abajo). España aplica horario de verano, así que **el desfase no es fijo** (UTC+1 en invierno, UTC+2 en verano) y no puede restarse a mano.

La conversión la centraliza `MestoBeach.Shared.Time.VenueTime`:

- `VenueTime.TimeZoneId` = `"Europe/Madrid"` — el mismo identificador IANA que se usa en el SQL (`AT TIME ZONE`). Si la máquina no resuelve identificadores IANA, `VenueTime` cae al equivalente Windows (`Romance Standard Time`) antes que degradar a UTC en silencio.
- `VenueTime.ToUtcRange(from, to)` traduce el par de fechas naturales del local al rango UTC `[fromUtc, toExclusive)` — extremo superior **exclusivo**. v1.0: de `from` 00:00 a `to + 1 día` 00:00 locales. *(diseñado — PRD-019)*: de `from` **06:00** a `to + 1 día` **06:00** locales, con la constante **`VenueTime.BusinessDayStartHour = 6`** como único punto donde vive el corte. La conversión usa `TimeZoneInfo.ConvertTimeToUtc`, de modo que el DST se resuelve por fecha. *(diseñado — PRD-019)* El cambio se hace en el método compartido; no se crea otro. `ToUtcRange` también lo usa Payments en `GET /api/v1/payments` (`PaymentQueries.cs`, página Pagos del admin), que pasa a cortar a las 06:00 en el mismo PR 11. Así Pagos e Informes cuentan el mismo «sábado», y la noche, que se liquida después de medianoche, cae en su día. El código de Payments no cambia, solo su comentario. (SDD-005 debe recoger esta nota en un PR aparte.)

Los repositorios de Analytics filtran siempre por ese rango UTC (`created_at >= @fromUtc AND created_at < @toExclusive`), lo que mantiene utilizables los índices sobre `created_at`. `AT TIME ZONE 'Europe/Madrid'` se usa únicamente para **agrupar** (por hora o por bucket de la serie temporal), no para filtrar.

**Día de negocio a las 06:00** *(diseñado — pendiente de implementación, PRD-019)*

El local opera de día como restaurante y de noche como fiesta, y la fiesta del sábado termina de madrugada del
domingo. Con el corte a medianoche, media noche caía en el domingo y los informes de Night no cuadraban ni con
la parte de la caja que pasa por Mesto (la venta de barra cara a cara vive en el TPV) ni con lo que el cliente
llama «el sábado». El corte cae en el hueco entre el cierre de Night y la apertura del restaurante: Night
termina antes de las 06:00 y el restaurante abre después, así que a esa hora no hay servicio. *Supuesto
pendiente de confirmar con el cliente (PRD-019 OQ-1909): hora máxima de cierre de Night.*

- El día de negocio `D` va de `D 06:00` a `D+1 06:00` hora de Madrid. `from`/`to` se interpretan como días de
  negocio: `?from=2026-10-10&to=2026-10-10` cubre `[10 oct 06:00, 11 oct 06:00)` — la noche del sábado
  **entera** en sábado.
- Para **agrupar** por día de negocio se resta el corte antes de truncar:
  `DATE_TRUNC('day', (created_at AT TIME ZONE 'Europe/Madrid') - make_interval(hours => @businessDayStartHour))`,
  con `@businessDayStartHour` pasado como parámetro desde `VenueTime.BusinessDayStartHour` (así la constante es
  de verdad el único punto del corte), y `series[].period` devuelve la fecha del día de negocio (un pedido a
  las 02:30 del domingo cuenta en `2026-10-10`). `week` y `month` truncan sobre ese mismo valor desplazado.
- El **heatmap horario no se desplaza**: `hour` sigue siendo la hora real del reloj (0–23); lo que cambia es
  qué pedidos entran en el rango. Las barras de 0–5 h de un día de negocio son su madrugada.
- Aplica a **todos los modos**, también a `RESTAURANT` y `ALL`: un corte único evita que
  `?mode=NIGHT` + `?mode=RESTAURANT` ≠ `?mode=ALL`.
- Es un cambio de contrato respecto de v1.0: los informes históricos de un mismo rango pueden variar en el tramo
  00:00–06:00 (que en Mesto Beach está vacío).

**Filtro por modo** *(diseñado — pendiente de implementación, PRD-019)*

- `mode=RESTAURANT|NIGHT` añade `AND o.venue_mode = @mode` sobre **`orders.venue_mode`** (sellado por Orders al
  crear la comanda con el modo vigente leído por `IVenueModeProvider`, SDD-004 §Enrutado por modo paso c;
  coincide con `sessions.venue_mode` porque el cambio de modo exige local vacío, SDD-001). `ALL` (default) no
  añade filtro, así `ALL` es exactamente la suma de los otros dos.
- Cuando la tabla base no es `orders`: las métricas de **sesiones** filtran por `sessions.venue_mode` (el mismo
  valor, congelado al abrir) y los **pagos** por el `venue_mode` de su sesión (`payments.session_id → sessions`).
  Una sesión nunca cambia de modo a medias (el cambio exige local vacío, SDD-001), así que los tres filtros son
  consistentes.
- `mode` se valida como enum case-insensitive; valor desconocido → `400`. La respuesta **ecoa** `mode`.
- Índice `orders(tenant_id, venue_mode, created_at)`: lo crea la migración `AddOrderVenueModeAndPriority` de Orders
  (SDD-004 §Esquema PostgreSQL); Analytics sigue sin migraciones propias (§5.1).

**Rango máximo:** 366 días. Si `to - from > 366` → `400 Bad Request`.

**`tenantId`:** siempre extraído del JWT. Nunca provisto como query param para Analytics.

---

## 3. API Endpoints

### 3.1 `GET /api/v1/analytics/sales/summary`

Resumen de ingresos por período con serie temporal para gráficas.

**Auth:** `StaffJwt` (cualquier rol)

**Query params adicionales:**

| Param | Tipo | Default | Descripción |
|---|---|---|---|
| `granularity` | `day` \| `week` \| `month` | `day` | Agrupación de la serie temporal |

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "from": "2026-06-01",
  "to": "2026-06-30",
  "granularity": "day",
  "mode": "ALL",
  "totalRevenueCents": 574050,
  "totalOrderCount": 214,
  "totalSessionCount": 89,
  "averageTicketCents": 6450,
  "series": [
    { "period": "2026-06-01", "revenueCents": 19350, "orderCount": 7, "sessionCount": 3 },
    { "period": "2026-06-02", "revenueCents": 26400, "orderCount": 11, "sessionCount": 4 }
  ]
}
```

**Reglas de cálculo:**
- `totalRevenueCents` = `SUM(total_cents)` de pagos con `status = 'Completed'` en el período.
- `totalOrderCount` = `COUNT(DISTINCT orders.id)` no cancelados (status ≠ `CANCELLED`) en sesiones del tenant. *(actualizado)* El SQL actual escribe `status NOT IN ('Cancelled')` y la base guarda `'CANCELLED'`: hoy **no excluye nada**; pasa a `UPPER(status) <> 'CANCELLED'` (§5.3).
- *(diseñado — PRD-019)* Con `mode ≠ ALL`, cada CTE filtra por su `venue_mode` (`orders`, `sessions`, y `payments` vía `sessions`); `series[].period` es el **día de negocio** (06:00–06:00), no el natural.
- `totalSessionCount` = `COUNT(DISTINCT session_id)` de pagos completados (sesiones que efectivamente pagaron).
- `averageTicketCents` = `totalRevenueCents / totalSessionCount` (0 si no hay sesiones).
- `series[].period` = fecha de inicio del período (día, lunes de la semana, o 1ro del mes según `granularity`), calculada con `DATE_TRUNC(granularity, created_at AT TIME ZONE 'Europe/Madrid')`.
- Períodos sin actividad se omiten del array `series` (no se rellenan con ceros).

**Errores:**
| Código | Condición |
|---|---|
| 400 | `from` o `to` ausente o formato inválido |
| 400 | `from` > `to` |
| 400 | Rango > 366 días |
| 400 | `granularity` con valor desconocido |
| 400 | `mode` con valor desconocido *(diseñado — PRD-019)* |
| 401 | JWT ausente o inválido |

---

### 3.2 `GET /api/v1/analytics/products/top`

Productos más pedidos en el período, ordenados por cantidad total.

**Auth:** `StaffJwt` (cualquier rol)

**Query params adicionales:**

| Param | Tipo | Default | Descripción |
|---|---|---|---|
| `limit` | int | 10 | Máximo de productos a retornar (1–50) |
| `sort` | `quantity` \| `revenue` | `quantity` | Criterio de ordenamiento |

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "from": "2026-06-01",
  "to": "2026-06-30",
  "sort": "quantity",
  "mode": "ALL",
  "products": [
    {
      "catalogItemId": "64b1f2c3e4b0a1d2e3f4a5b6",
      "itemNameSnapshot": "Hamburguesa Clásica",
      "totalQuantity": 87,
      "totalRevenueCents": 108750,
      "orderCount": 74
    },
    {
      "catalogItemId": "64b1f2c3e4b0a1d2e3f4a5b7",
      "itemNameSnapshot": "Jugo Natural",
      "totalQuantity": 65,
      "totalRevenueCents": 39000,
      "orderCount": 58
    }
  ]
}
```

**Reglas de cálculo:**
- Agrupa `order_items` por `(catalog_item_id, item_name_snapshot)`.
- Solo incluye ítems de órdenes con status ≠ `CANCELLED` (*(actualizado)* hoy el filtro `'Cancelled'` no excluye nada; ver §5.3).
- *(diseñado — PRD-019)* Con `mode ≠ ALL`, solo ítems de órdenes con `o.venue_mode = @mode`: «Chuletón» no aparece en `mode=NIGHT` y «Shot» no aparece en `mode=RESTAURANT`.
- `totalRevenueCents` = `SUM(item_price_snapshot_cents * quantity)`.
- `orderCount` = `COUNT(DISTINCT order_id)` (pedidos distintos donde aparece el producto).
- `itemNameSnapshot` es el último valor registrado para ese `catalog_item_id` en el período (pueden existir variaciones de nombre; se toma el más reciente por `order.created_at DESC`).

**Errores:**
| Código | Condición |
|---|---|
| 400 | `limit` fuera de rango (< 1 o > 50) |
| 400 | `sort` con valor desconocido |
| 400/401 | (mismos que 3.1) |

---

### 3.3 `GET /api/v1/analytics/sessions/averages`

Métricas promedio de las sesiones en el período.

**Auth:** `StaffJwt` (cualquier rol)

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "from": "2026-06-01",
  "to": "2026-06-30",
  "mode": "ALL",
  "totalSessions": 89,
  "totalOrders": 214,
  "totalPaidSessions": 76,
  "averageTicketCents": 6450,
  "averageSessionDurationMin": 52,
  "averageOrdersPerSession": 2.4,
  "averageItemsPerOrder": 1.8
}
```

**Reglas de cálculo:**
- `totalSessions` = sesiones del tenant con `created_at` en el período (status cualquiera).
- `totalPaidSessions` = sesiones que tienen al menos un pago con `status = 'Completed'`.
- `averageSessionDurationMin` = `AVG(EXTRACT(EPOCH FROM (closed_at - created_at)) / 60)` solo para sesiones con `status = 'CLOSED'` y `closed_at IS NOT NULL`.
- `averageOrdersPerSession` = `totalOrders / totalSessions` (2 decimales).
- `averageItemsPerOrder` = `SUM(quantity) / COUNT(DISTINCT order_id)` (2 decimales, órdenes no canceladas — *(actualizado)* hoy los cancelados entran; ver §5.3).
- *(diseñado — PRD-019)* Con `mode ≠ ALL`: `totalSessions`/`averageSessionDurationMin` sobre `sessions.venue_mode = @mode`, `totalOrders`/`averageItemsPerOrder` sobre `orders.venue_mode = @mode`, `totalPaidSessions`/`averageTicketCents` sobre pagos de sesiones de ese modo.

---

### 3.4 `GET /api/v1/analytics/heatmap/hourly`

Distribución de pedidos por hora del día, agregada sobre el período. Útil para identificar horas pico.

**Auth:** `StaffJwt` (cualquier rol)

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "from": "2026-06-01",
  "to": "2026-06-30",
  "mode": "ALL",
  "timezone": "Europe/Madrid",
  "hours": [
    { "hour": 0,  "orderCount": 0,  "revenueCents": 0 },
    { "hour": 1,  "orderCount": 0,  "revenueCents": 0 },
    { "hour": 11, "orderCount": 43, "revenueCents": 111800 },
    { "hour": 12, "orderCount": 67, "revenueCents": 174200 },
    { "hour": 13, "orderCount": 58, "revenueCents": 150800 }
  ]
}
```

**Reglas de cálculo:**
- El array `hours` siempre contiene las 24 horas (0–23); horas sin actividad retornan `orderCount: 0, revenueCents: 0`.
- `hour` = `EXTRACT(HOUR FROM orders.created_at AT TIME ZONE 'Europe/Madrid')` — hora local del local, con el desfase que corresponda a esa fecha (CET o CEST).
- `orderCount` = pedidos no cancelados en esa hora, sumados sobre todos los días del período.
- `revenueCents` = `SUM(item_price_snapshot_cents * quantity)` de ítems de esos pedidos.
- *(diseñado — PRD-019)* `hour` **no se desplaza** con el día de negocio: un pedido a las 02:30 del domingo entra en el rango del sábado (06:00–06:00) y aparece en `hour: 2`. Con `mode=NIGHT` el mapa muestra la curva de la fiesta (típicamente 23–5 h); con `mode=RESTAURANT`, la de los servicios de día.
- *(actualizado)* El DTO actual devuelve el literal `"America/Bogota"` en `timezone` (`AnalyticsRepository.cs:304`) aunque agrupe en `Europe/Madrid`; se corrige a `VenueTime.TimeZoneId` en el mismo pase (§5.3).

---

### 3.5 `GET /api/v1/analytics/prep-time`

Tiempo promedio de preparación por estación KDS (desde creación de la orden hasta que pasa a `READY`).

**Auth:** `StaffJwt` (cualquier rol)

**Respuesta 200:**
```json
{
  "tenantId": "uuid",
  "from": "2026-06-01",
  "to": "2026-06-30",
  "mode": "ALL",
  "stations": [
    {
      "stationId": "uuid",
      "stationName": "Cocina Principal",
      "avgPrepTimeMin": 8,
      "p90PrepTimeMin": 14,
      "sampleCount": 142
    },
    {
      "stationId": null,
      "stationName": "Sin estación",
      "avgPrepTimeMin": 6,
      "p90PrepTimeMin": 10,
      "sampleCount": 31
    }
  ]
}
```

**Reglas de cálculo:**
- *(actualizado)* **Código actual**: agrupa por **`orders.station_id`** y mide `orders.ready_at - orders.confirmed_at` (`AnalyticsRepository.cs:318-331`), con `COALESCE(ks.name, 'General')`. Esa columna quedó **obsoleta** con ADR-029 (la estación es del plato, no del pedido) y ADR-025 (el estado baja al ítem): un pedido con platos de Cocina y Parrilla cuenta entero en una sola estación —o en «General»— y su tiempo es el del último plato, no el de cada cocina.
- *(diseñado — pendiente de implementación, PRD-019)* Agrupa por **`order_items.station_id`** (`JOIN order_items oi ON oi.order_id = o.id`, `LEFT JOIN kds_stations ks ON ks.id = oi.station_id`) y mide **por plato**: `oi.ready_at - COALESCE(oi.queued_at, o.confirmed_at)` (sellos de ADR-032; el `COALESCE` cubre platos anteriores a `queued_at`). Solo ítems con `oi.ready_at IS NOT NULL` y no cancelados; el rango del período se aplica sobre `oi.ready_at`; con `mode ≠ ALL`, `o.venue_mode = @mode`.
- `avgPrepTimeMin` = `AVG(prep_seconds / 60)`, redondeado a entero; `p90PrepTimeMin` = `percentile_disc(0.9) WITHIN GROUP (ORDER BY prep_seconds)`.
- `sampleCount` = número de **platos** usados en el cálculo *(diseñado; hoy, pedidos)*.
- Estaciones sin platos en el período se omiten del array.
- Si `oi.station_id IS NULL`, retornar una entrada con `stationId: null, stationName: "Sin estación"`.
- *(diseñado — PRD-019)* Con `mode=NIGHT`, las barras de perfil `BAR` (que saltan `IN_PREPARATION`, SDD-004) miden cola → listo, que es su único tramo; el informe compara Barra 1 con Barra 2.

---

## 4. Cross-Module Integration

Analytics es un módulo **consumidor puro** — solo lee, nunca escribe ni envía comandos a otros módulos.

### 4.1 Proyecciones en `AnalyticsDbContext`

```csharp
// Todas las proyecciones excluidas de migraciones
public DbSet<SessionProjection> Sessions  => Set<SessionProjection>();
public DbSet<OrderProjection> Orders      => Set<OrderProjection>();
public DbSet<OrderItemProjection> OrderItems => Set<OrderItemProjection>();
public DbSet<PaymentProjection> Payments  => Set<PaymentProjection>();
public DbSet<KdsStationProjection> KdsStations => Set<KdsStationProjection>();
```

Configuración en EF:
```csharp
b.ToTable("sessions",    t => t.ExcludeFromMigrations());
b.ToTable("orders",      t => t.ExcludeFromMigrations());
b.ToTable("order_items", t => t.ExcludeFromMigrations());
b.ToTable("payments",    t => t.ExcludeFromMigrations());
b.ToTable("kds_stations",t => t.ExcludeFromMigrations());
```

### 4.2 Sin eventos ni dependencias circulares

Analytics no registra handlers de MediatR para comandos de otros módulos en MVP. En v2 se podrán agregar handlers de `OrderStatusChangedEvent` para actualizar materializaciones incrementales.

---

## 5. Persistencia

**No hay tablas propias de Analytics en MVP.** Cero migraciones.

Todas las queries se ejecutan como `AsNoTracking()` en `AnalyticsDbContext`. El `DbContext` se registra con la misma connection string que los demás módulos (`ConnectionStrings:Postgres`).

### 5.1 Índices existentes aprovechados

| Tabla | Índice | Usado por |
|---|---|---|
| `orders` | `IX_orders_tenant_id_created_at` | Todos los endpoints |
| `payments` | `IX_payments_tenant_id_created_at` | 3.1 (sales/summary) |
| `payments` | `IX_payments_session_id` | 3.3 (averages) |
| `order_items` | `IX_order_items_order_id` (PK implícita del join) | 3.2 (products/top) |

Si en algún entorno el volumen de datos hace lenta la query de `products/top`, se puede agregar sin riesgo:
```sql
CREATE INDEX CONCURRENTLY idx_order_items_catalog_item
  ON order_items(order_id, item_price_snapshot_cents, quantity);
```
Esto es opt-in y no bloquea tablas.

### 5.2 Zona horaria en PostgreSQL

```sql
-- Conversión UTC → hora del local para agrupar por hora
EXTRACT(HOUR FROM created_at AT TIME ZONE 'Europe/Madrid')

-- Truncar a día del local para la serie temporal
DATE_TRUNC('day', created_at AT TIME ZONE 'Europe/Madrid')

-- El filtro del período, en cambio, va en UTC puro con los límites
-- que devuelve VenueTime.ToUtcRange (superior exclusivo):
WHERE created_at >= @fromUtc AND created_at < @toExclusive

-- *(diseñado — PRD-019)* Día de negocio a las 06:00: se resta el corte ANTES de truncar.
-- (El INTERVAL aquí no es un desfase de zona horaria —eso sigue haciéndolo AT TIME ZONE—,
--  es el corte del día de negocio, y es el mismo todo el año.)
DATE_TRUNC('day', (created_at AT TIME ZONE 'Europe/Madrid') - make_interval(hours => @businessDayStartHour))

-- *(diseñado — PRD-019)* Filtro por modo (se omite cuando mode = ALL)
AND o.venue_mode = @mode

-- *(actualizado)* Exclusión de cancelados: la base guarda 'CANCELLED' (ver §5.3)
AND UPPER(o.status) <> 'CANCELLED'
```

PostgreSQL resuelve `AT TIME ZONE 'Europe/Madrid'` contra la tzdata del servidor, así que el cambio de hora (CET ↔ CEST) se aplica por fila según su fecha. Por eso **no** se resta un desfase fijo en ningún punto — ni en SQL (`INTERVAL`) ni en C#.

EF Core no soporta `AT TIME ZONE` directamente en LINQ; las queries que necesiten conversión de zona horaria se escriben como **raw SQL** (`SqlQueryRaw`) dentro de `AnalyticsRepository`, con los límites del rango siempre como parámetros (`@fromUtc`, `@toExclusive`) y nunca interpolados. `@mode` *(diseñado — PRD-019)* es también parámetro, nunca interpolado, y lo mismo `@businessDayStartHour` *(diseñado — PRD-019)*, que se pasa desde `VenueTime.BusinessDayStartHour` para que la constante sea de verdad el único punto del corte.

### 5.3 Correcciones al código actual *(actualizado)*

Tres discrepancias entre este documento y `AnalyticsRepository.cs`, detectadas al diseñar PRD-019. Se corrigen en el PR de reportes por modo (PRD-019 PR11), pero son bugs **independientes** del modo:

| Dónde | Qué hace hoy | Qué debe hacer | Efecto del bug |
|---|---|---|---|
| Filtro de cancelados (`AnalyticsRepository.cs:57,141,193,233,283`) | `status NOT IN ('Cancelled')` | `UPPER(status) <> 'CANCELLED'` | Orders persiste el enum en screaming-snake (`EntityConfigurations.cs:26`: `'CANCELLED'`). El literal `'Cancelled'` **nunca coincide**, así que ningún pedido cancelado se excluye: inflan `totalOrderCount`, `products/top`, el heatmap y `averageItemsPerOrder`. `UPPER` cubre además cualquier fila heredada con otra capitalización |
| Agrupación de `prep-time` (`AnalyticsRepository.cs:318-331`) | `GROUP BY o.station_id` sobre `orders` | `GROUP BY oi.station_id` sobre `order_items`, midiendo por plato (§3.5) | `orders.station_id` quedó obsoleta con ADR-029/ADR-025 (el propio `EntityConfigurations.cs:28` la marca `CS0618`); el informe atribuye el pedido entero a una estación o a «General» |
| `timezone` del heatmap (`AnalyticsRepository.cs:304`) | Literal `"America/Bogota"` | `VenueTime.TimeZoneId` (`"Europe/Madrid"`) | Solo cosmético: el SQL ya agrupa en Madrid, pero el front muestra una zona que no es |

---

## 6. Validaciones

### Filtros de fecha (todos los endpoints)
- `from` y `to` requeridos; formato `yyyy-MM-dd`
- `from` ≤ `to`
- `to - from` ≤ 366 días
- Fechas futuras están permitidas (retornarán datos vacíos)

### `mode` (todos los endpoints) *(diseñado — PRD-019)*
- `mode` ∈ `{ "RESTAURANT", "NIGHT", "ALL" }`, case-insensitive, default `ALL`; otro valor → 400

### `GET /analytics/sales/summary`
- `granularity` ∈ `{ "day", "week", "month" }`, case-insensitive

### `GET /analytics/products/top`
- `limit` ∈ [1, 50], default 10
- `sort` ∈ `{ "quantity", "revenue" }`, case-insensitive

---

## 7. Decisiones Clave

| Decisión | Razón |
|---|---|
| Sin tablas propias en MVP | El volumen de datos de un restaurante (cientos de pedidos/día) no justifica un pipeline analítico. EF queries directas son suficientes. |
| v1.0: Día de negocio en `Europe/Madrid` | El local está en España: el día se corta a medianoche de Madrid. El sistema heredado tenía cableado el desfase de Colombia (UTC-5) y los informes cortaban el día cinco horas tarde. Ver ADR-031. |
| Conversión con `TimeZoneInfo`, no con un desfase fijo | España aplica horario de verano; un `INTERVAL` fijo estaría mal media parte del año. `VenueTime.ToUtcRange` convierte con `TimeZoneInfo` y PostgreSQL agrupa con `AT TIME ZONE`, así que el DST lo resuelve la tzdata en ambos lados. |
| Raw SQL para zona horaria | EF LINQ no expone `AT TIME ZONE` de PostgreSQL. Se prefiere SQL explícito a funciones de conversión en memoria que rompan el plan de ejecución. |
| `tenantId` siempre del JWT | Nunca de query string — evita IDOR. Admin ve solo su tenant; PlatformAdmin (v2) podrá pasar `tenantId` explícito. |
| `ready_at` vs. `confirmed_at` | *(histórico v1.0, resuelto)* `orders.ready_at` se añadió en la migración de MestoBeach.Orders.Infrastructure y es lo que mide el código actual; PRD-019 lo sustituye por la medición por plato (`order_items.ready_at - COALESCE(queued_at, orders.confirmed_at)`), ver la fila «prep-time por order_items.station_id». |
| Percentil 90 en PostgreSQL | `percentile_disc` es más preciso que calcular en memoria. Se usa raw SQL para este cálculo. |
| Períodos vacíos omitidos en `series` | Simplifica el consumo del cliente; el frontend rellena los huecos visualmente. |
| Sin caché en MVP | Acceptable para MVP; en v2 se puede agregar Redis con TTL de 5 min por `(tenantId, endpoint, params)`. |
| Día de negocio a las **06:00** *(diseñado — PRD-019)* | La noche del sábado termina de madrugada del domingo y el cliente la llama «el sábado»; el corte cae entre el cierre de Night y la apertura del restaurante (supuesto, ver PRD-019 OQ-1909). Una sola constante (`VenueTime.BusinessDayStartHour = 6`) para filtro y agrupación, en todos los modos, para que `NIGHT + RESTAURANT = ALL`. |
| `mode` filtra por `orders.venue_mode`, no por hora *(diseñado — PRD-019)* | El modo es una decisión del admin (ADR-036), no una franja horaria: un sábado de lluvia el restaurante puede cerrar a las 18:00 y Night abrir a las 19:00. La columna congelada en la comanda es la única verdad; `ALL` es la suma exacta de los dos. |
| `UPPER(status) <> 'CANCELLED'` *(actualizado)* | El SQL actual filtra `'Cancelled'` y Orders persiste `'CANCELLED'`: el filtro no excluía nada (§5.3). `UPPER` cubre cualquier fila heredada con otra capitalización sin tocar datos. |
| `prep-time` por `order_items.station_id` *(actualizado)* | `orders.station_id` quedó obsoleta con ADR-029/ADR-025; agrupar por ella atribuye el pedido entero a una estación. Por plato, cada barra mide lo suyo y `mode=NIGHT` compara Barra 1 con Barra 2 (§3.5, §5.3). |

---

## 8. Observabilidad

| Log / Métrica | Evento | Payload |
|---|---|---|
| Log | `analytics.sales_summary.queried` | `{ tenantId, from, to, granularity, durationMs }` |
| Log | `analytics.products_top.queried` | `{ tenantId, from, to, limit, sort, resultCount, durationMs }` |
| Log | `analytics.sessions_averages.queried` | `{ tenantId, from, to, durationMs }` |
| Log | `analytics.heatmap.queried` | `{ tenantId, from, to, durationMs }` |
| Log | `analytics.prep_time.queried` | `{ tenantId, from, to, stationCount, durationMs }` |
| Métrica | `analytics.query_duration_ms` | Histogram, label: `endpoint` |
| Métrica | `analytics.slow_queries` | Counter, threshold > 3s |

*(diseñado — PRD-019)* Todos los logs añaden `mode` al payload y `analytics.query_duration_ms` gana la label `mode`; `analytics.prep_time.queried` añade `sampleCount`.

---

## 9. Seguridad

- Todos los endpoints requieren `StaffJwt` (autenticación de staff del tenant).
- El `tenantId` se extrae del JWT mediante `HttpContext.Items["tenant_id"]`; no se acepta `tenantId` en query string.
- Roles permitidos en MVP: cualquier rol de staff (Admin, Cajero, Mesero, Cocina). En v2 se puede restringir a Admin únicamente si se define como política.
- Las queries siempre incluyen `WHERE tenant_id = @tenantId` — imposible ver datos de otro tenant.

---

## 10. Casos de Aceptación

- [ ] Admin consulta `sales/summary` para junio 2026 con granularidad `day` → retorna `series` con entradas solo para días con actividad, `totalRevenueCents` coherente con la suma de pagos completados.
- [ ] Rango > 366 días → 400 Bad Request.
- [ ] `from` > `to` → 400 Bad Request.
- [ ] `products/top` con `limit=5` retorna exactamente 5 productos ordenados por `totalQuantity DESC`.
- [ ] `products/top` con `sort=revenue` retorna productos ordenados por `totalRevenueCents DESC`.
- [ ] Ítems de órdenes canceladas **no** aparecen en `products/top`.
- [ ] `sessions/averages` retorna `averageSessionDurationMin` solo calculado sobre sesiones cerradas.
- [ ] `heatmap/hourly` retorna exactamente 24 entradas (horas 0–23); horas sin actividad tienen `orderCount: 0`.
- [ ] `heatmap/hourly` usa hora local del local (un pedido a las 20:30 UTC del 15 de julio = 22:30 en Madrid, CEST, aparece en `hour: 22`).
- [ ] El desfase no es fijo: ese mismo pedido a las 20:30 UTC pero del 15 de enero = 21:30 en Madrid (CET) y aparece en `hour: 21`.
- [ ] Un pedido del 30 de junio a las 23:30 hora de Madrid entra en el período `to = 2026-06-30` y **no** en el del 1 de julio (el corte del día es local, no UTC).
- [ ] `prep-time` excluye órdenes sin `ready_at` (pendientes de llegar a READY).
- [ ] `prep-time` incluye entrada `stationId: null` si existen órdenes sin estación asignada.
- [ ] Mesero sin rol Admin puede consultar analytics (retorna 200).
- [ ] Token de otro tenant → datos del tenant del token, no del tenant solicitado en query (no existe ese param).
- [ ] JWT ausente → 401.
- [ ] *(diseñado — PRD-019)* Para el mismo rango, cada total de `?mode=NIGHT` + el de `?mode=RESTAURANT` = el de `?mode=ALL` (pedidos, ingresos, sesiones, productos).
- [ ] *(diseñado — PRD-019)* Un pedido a las 02:30 del domingo 11 de octubre entra en `?from=2026-10-10&to=2026-10-10` y **no** en `?from=2026-10-11&to=2026-10-11`; en `heatmap/hourly` aparece en `hour: 2`.
- [ ] *(diseñado — PRD-019)* `?mode=night` (minúsculas) → 200 como `NIGHT`; `?mode=PARTY` → 400.
- [ ] *(diseñado — PRD-019)* `products/top?mode=NIGHT` no lista «Chuletón» (`visibleIn: ["RESTAURANT"]`) aunque se haya pedido de día.
- [ ] *(actualizado)* Un pedido cancelado **no** suma en `totalOrderCount`, `products/top`, `heatmap/hourly` ni `averageItemsPerOrder` (hoy sí suma: filtro `'Cancelled'` vs `'CANCELLED'`).
- [ ] *(actualizado)* Un pedido con un plato de Cocina y otro de Parrilla aporta **una muestra a cada estación** en `prep-time` (hoy aporta una al `orders.station_id` obsoleto o a «General»).
- [ ] *(actualizado)* `heatmap/hourly` devuelve `"timezone": "Europe/Madrid"` (hoy `"America/Bogota"`).

---

*SDD-006 — Analytics Module*  
*Versión 1.1 — Octubre 2026 (v1.1 diseñada, pendiente de implementación — PRD-019; v1.0 — Junio 2026)*
