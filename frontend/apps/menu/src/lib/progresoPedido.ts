import type { OrderItemDto, OrderStatus, OrderSummaryDto } from "@mestobeach/api-client";

/* ──────────────────────────────────────────────────────────────────────────
   Cómo va el pedido, contado en voz baja.

   El sistema NO calcula un tiempo estimado de salida. Aquí, por tanto, nunca
   se promete un ETA ("sale en 5 min" sería mentira): solo se dice desde cuándo
   el pedido está en el paso en el que está. Es información verdadera, y suele
   bastar para que el comensal no tenga que preguntar nada.
   ────────────────────────────────────────────────────────────────────────── */

/** Pedidos todavía en marcha: los únicos que muestran tiempo y botón de "¿Cuándo sale?". */
const EN_CURSO: ReadonlySet<OrderStatus> = new Set<OrderStatus>([
  "PENDING",
  "CONFIRMED",
  "IN_PREPARATION",
  "READY",
]);

/** ¿Sigue vivo el pedido? Entregado y cancelado ya no cuentan tiempos. */
export function pedidoEnCurso(status: OrderStatus): boolean {
  return EN_CURSO.has(status);
}

type CampoSello = "queuedAt" | "preparationStartedAt" | "readyAt";

function aMilisegundos(iso: string | null | undefined): number | null {
  if (!iso) return null;
  const ms = new Date(iso).getTime();
  return Number.isNaN(ms) ? null : ms;
}

/** Sello más temprano de los ítems: el pedido entra en un paso cuando entra su primer plato. */
function selloMasTemprano(items: readonly OrderItemDto[], campo: CampoSello): number | null {
  let mejor: number | null = null;
  for (const item of items) {
    const ms = aMilisegundos(item[campo]);
    if (ms === null) continue;
    if (mejor === null || ms < mejor) mejor = ms;
  }
  return mejor;
}

/** Sello más tardío de los ítems: el pedido está listo cuando lo está su último plato. */
function selloMasTardio(items: readonly OrderItemDto[], campo: CampoSello): number | null {
  let mejor: number | null = null;
  for (const item of items) {
    const ms = aMilisegundos(item[campo]);
    if (ms === null) continue;
    if (mejor === null || ms > mejor) mejor = ms;
  }
  return mejor;
}

/**
 * Cuándo entró el pedido en su estado ACTUAL, en milisegundos.
 *
 * Se usa el sello de la transición correspondiente (ADR-025, sellos por ítem) y,
 * si esa marca no viniera —cliente antiguo, pedido creado antes de los sellos—,
 * se cae hacia atrás hasta `createdAt` en vez de romperse.
 */
export function selloDelEstadoActual(order: OrderSummaryDto): number | null {
  const items = order.items ?? [];
  const creado = aMilisegundos(order.createdAt);
  const confirmado = aMilisegundos(order.confirmedAt) ?? creado;

  switch (order.status) {
    case "PENDING":
      return creado;
    case "CONFIRMED":
      return selloMasTemprano(items, "queuedAt") ?? confirmado;
    case "IN_PREPARATION":
      return selloMasTemprano(items, "preparationStartedAt") ?? confirmado;
    case "READY":
      return selloMasTardio(items, "readyAt") ?? confirmado;
    default:
      // Entregado o cancelado: el pedido ya no espera nada.
      return null;
  }
}

/** Duración en palabras tranquilas: "un momento", "8 min", "1 h", "1 h 12 min". */
function duracionSerena(ms: number): string {
  const minutos = Math.floor(Math.max(0, ms) / 60_000);
  if (minutos < 1) return "un momento";
  if (minutos < 60) return `${minutos} min`;
  const horas = Math.floor(minutos / 60);
  const resto = minutos % 60;
  return resto === 0 ? `${horas} h` : `${horas} h ${resto} min`;
}

/**
 * La coletilla que acompaña al chip de estado: "desde hace 8 min", "sale para tu mesa".
 * Devuelve `null` cuando no hay nada honesto que decir (entregado, cancelado, sin sello).
 */
export function fraseDeEspera(order: OrderSummaryDto, ahora: number): string | null {
  if (!pedidoEnCurso(order.status)) return null;
  // Ya está en el pase: el tiempo transcurrido sobra, lo que importa es que sale.
  if (order.status === "READY") return "sale para tu mesa";

  const desde = selloDelEstadoActual(order);
  if (desde === null) return null;
  return `desde hace ${duracionSerena(ahora - desde)}`;
}
