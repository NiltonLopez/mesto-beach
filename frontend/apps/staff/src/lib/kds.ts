// ─── Lenguaje del tablero de cocina (ADR-025 + el estado QUEUED) ───────────────
// El plato es la unidad de estado, no la comanda. Su viaje son cuatro pasos que se
// leen de izquierda a derecha en el KDS:
//
//   HELD → QUEUED → IN_PREPARATION → READY → IN_DELIVERY → DELIVERED
//   esperando   en cola   en preparación   en el pase
//
// Un plato pedido "ahora" nace en QUEUED; uno "para luego" nace en HELD y alguien
// (cocina o PDA) lo marcha. La orden de preparación la da SIEMPRE la estación.

import type {
  KdsFlowProfile,
  KdsStationDto,
  OrderItemDto,
  OrderItemStatus,
} from "@mestobeach/api-client";

// ─── Columnas ─────────────────────────────────────────────────────────────────

export type KdsColumnKey = "HELD" | "QUEUED" | "IN_PREPARATION" | "READY";

/** Las tres acciones que la cocina puede ejecutar sobre un plato. */
export type KdsActionKind = "MARCH" | "START" | "READY";

export interface KdsColumnDef {
  key: KdsColumnKey;
  label: string;
  hint: string;
  /** Píldora del contador en la cabecera de la columna. */
  pill: string;
  /** Filo izquierdo de las tarjetas que viven en esta columna. */
  edge: string;
  /** Acción que ofrece la columna; null = solo mirar (el pase). */
  action: KdsActionKind | null;
}

export const KDS_COLUMNS: readonly KdsColumnDef[] = [
  {
    key: "HELD",
    label: "Esperando orden",
    hint: "Para luego · marcha cocina o PDA",
    pill: "bg-amber-500/10 text-amber-700",
    edge: "border-l-amber-500",
    action: "MARCH",
  },
  {
    key: "QUEUED",
    label: "En cola",
    hint: "Listo para arrancar · lo lanza el encargado",
    pill: "bg-oro/[0.16] text-brand-700",
    edge: "border-l-brand-500",
    action: "START",
  },
  {
    key: "IN_PREPARATION",
    label: "En preparación",
    hint: "Cocinando · da por listo al salir",
    pill: "bg-orange-500/10 text-orange-700",
    edge: "border-l-orange-500",
    action: "READY",
  },
  {
    key: "READY",
    label: "En el pase",
    hint: "Listo · esperando al runner",
    pill: "bg-emerald-500/10 text-emerald-700",
    edge: "border-l-emerald-500",
    action: null,
  },
] as const;

/**
 * Perfil BAR (ADR-037, SDD-004): dos columnas. «Pedidos» reúne todo lo que aún no está listo
 * (usa la clave QUEUED) y su acción es Listo directamente; «Listo» (clave READY) es el pase.
 */
export const KDS_COLUMNS_BAR: readonly KdsColumnDef[] = [
  {
    key: "QUEUED",
    label: "Pedidos",
    hint: "Prepara y da por listo",
    pill: "bg-oro/[0.16] text-brand-700",
    edge: "border-l-brand-500",
    action: "READY",
  },
  {
    key: "READY",
    label: "Listo",
    hint: "Listo · esperando al runner",
    pill: "bg-emerald-500/10 text-emerald-700",
    edge: "border-l-emerald-500",
    action: null,
  },
] as const;

/** Columnas del tablero según el perfil de flujo de la estación (ADR-037). */
export function columnsFor(profile: KdsFlowProfile): readonly KdsColumnDef[] {
  return profile === "BAR" ? KDS_COLUMNS_BAR : KDS_COLUMNS;
}

/** En qué columna cae un plato. null = ya no es asunto de la cocina. */
export function columnOf(
  status: OrderItemStatus | null | undefined,
  profile: KdsFlowProfile = "FULL_PASS"
): KdsColumnKey | null {
  if (profile === "BAR") {
    switch (status) {
      case "HELD":
      case "IN_DELIVERY":
      case "DELIVERED":
      case "CANCELLED":
        return null; // HELD no es de la barra hasta que alguien lo marche
      case "READY":
        return "READY";
      default:
        // QUEUED, IN_PREPARATION, RETURNED y los sin sello: todo es «Pedidos».
        return "QUEUED";
    }
  }
  switch (status) {
    case "HELD":
      return "HELD";
    case "QUEUED":
      return "QUEUED";
    case "IN_PREPARATION":
    // Un devuelto se rehace: vuelve a los fuegos junto a lo que se está cocinando.
    case "RETURNED":
      return "IN_PREPARATION";
    case "READY":
      return "READY";
    case "IN_DELIVERY":
    case "DELIVERED":
    case "CANCELLED":
      return null;
    default:
      // Comandas antiguas sin sello por ítem: se asumen en fuegos para no perderlas.
      return "IN_PREPARATION";
  }
}

export function columnDef(key: KdsColumnKey, profile: KdsFlowProfile = "FULL_PASS"): KdsColumnDef {
  const cols = columnsFor(profile);
  return cols.find((c) => c.key === key) ?? (cols[0] as KdsColumnDef);
}

// ─── Acciones ─────────────────────────────────────────────────────────────────

export const KDS_ACTION_LABEL: Record<KdsActionKind, string> = {
  MARCH: "Marchar",
  START: "¡A preparar!",
  READY: "Listo",
};

/** El botón estrella (dar la orden) es dorado sólido; marchar es secundario; listo, terracota. */
export const KDS_ACTION_CLASS: Record<KdsActionKind, string> = {
  MARCH:
    "border border-oro/55 bg-paper text-brand-700 hover:bg-oro/[0.12] active:bg-oro/[0.18]",
  // brand-600 en vez de 500: sobre el dorado claro el texto blanco se quedaba en 3.4:1 y a
  // dos metros de la pantalla de cocina eso se lee mal. Con 600 sube a ~4.9:1 (AA).
  START: "bg-brand-600 text-white hover:bg-brand-700 active:bg-brand-800",
  READY: "bg-fuego-500 text-white hover:bg-fuego-600 active:bg-fuego-700",
};

/** Estado en el que queda el plato tras la acción (feedback optimista). */
export const KDS_ACTION_NEXT: Record<KdsActionKind, OrderItemStatus> = {
  MARCH: "QUEUED",
  START: "IN_PREPARATION",
  READY: "READY",
};

export const KDS_ACTION_ERROR: Record<KdsActionKind, string> = {
  MARCH: "No se pudo marchar el plato.",
  START: "No se pudo dar la orden de preparación.",
  READY: "No se pudo dar el plato por listo.",
};

// ─── Estaciones ───────────────────────────────────────────────────────────────

// Paleta fija de la casa: dorado, terracota, salvia y azul-pizarra. El color de una
// estación sale de su posición en la lista, así es estable entre pantallas y recargas.
// Son variables (--est-N en globals.css): en Night se aclaran para leerse sobre fondo oscuro.
const STATION_PALETTE = [
  "rgb(var(--est-1))",
  "rgb(var(--est-2))",
  "rgb(var(--est-3))",
  "rgb(var(--est-4))",
] as const;
const STATION_FALLBACK = "rgb(var(--est-1))";

export function stationColor(
  stationId: string | null | undefined,
  stations: KdsStationDto[]
): string {
  const idx = stationId ? stations.findIndex((s) => s.id === stationId) : -1;
  if (idx < 0) return STATION_FALLBACK;
  return STATION_PALETTE[idx % STATION_PALETTE.length] ?? STATION_FALLBACK;
}

export function stationName(
  stationId: string | null | undefined,
  stations: KdsStationDto[]
): string {
  return (stationId && stations.find((s) => s.id === stationId)?.name) || "Sin estación";
}

/** Tinte muy tenue del color de una estación, para el fondo de las tiras cross-station. */
export function tint(color: string, alpha = "14"): string {
  // `alpha` sigue siendo el byte hexadecimal de antes ("14" = 8%); ahora el color es una variable.
  const pct = Math.round((parseInt(alpha, 16) / 255) * 100);
  return `color-mix(in srgb, ${color} ${pct}%, transparent)`;
}

// ─── Estado en vivo, en palabras ──────────────────────────────────────────────

export const ITEM_STATE_LABEL: Record<OrderItemStatus, string> = {
  HELD: "esperando orden",
  QUEUED: "en cola",
  IN_PREPARATION: "en preparación",
  READY: "en el pase",
  IN_DELIVERY: "en reparto",
  DELIVERED: "entregado",
  RETURNED: "devuelto — rehacer",
  CANCELLED: "anulado",
};

export function stateLabel(status: OrderItemStatus | null | undefined): string {
  return (status && ITEM_STATE_LABEL[status]) || ITEM_STATE_LABEL.IN_PREPARATION;
}

// ─── Tiempos ──────────────────────────────────────────────────────────────────

/** `45s` · `8 min` · `1h 05`. En compacto los minutos van pegados: `2m`. */
export function formatDur(ms: number, compact = false): string {
  const secs = Math.max(0, Math.floor(ms / 1000));
  if (secs < 60) return `${secs}s`;
  const mins = Math.floor(secs / 60);
  if (mins < 60) return compact ? `${mins}m` : `${mins} min`;
  return `${Math.floor(mins / 60)}h ${String(mins % 60).padStart(2, "0")}`;
}

/** Cronómetro de la comanda: `08:04`, o `1:02:33` cuando pasa de la hora. */
export function clockLabel(iso: string, now: number): string {
  const total = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const mm = String(m).padStart(2, "0");
  const ss = String(s).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** Urgencia de la comanda por el reloj: gris <5 min, terracota 5–10, rojo pasados los 10. */
export function urgencyClass(iso: string, now: number): string {
  const mins = (now - new Date(iso).getTime()) / 60_000;
  if (mins >= 10) return "text-rose-600";
  // Terracota del tema, no un hex arbitrario: Tailwind no genera las clases que solo existen
  // dentro de un string compuesto, y el color acababa sin aplicarse.
  if (mins >= 5) return "text-fuego-500";
  return "text-muted";
}

// ─── Recorrido del plato ──────────────────────────────────────────────────────

export interface RecorridoStep {
  key: KdsColumnKey;
  label: string;
  state: "done" | "current" | "future";
  /** Tiempo ya formateado: duración del tramo, o el que lleva ahí si es el actual. */
  time: string;
}

/** Un paso del recorrido: su columna, su etiqueta y el sello donde empieza (0 = creación de la comanda). */
interface StepDef {
  key: KdsColumnKey;
  label: string;
  stampIndex: number;
}

const FULL_STEPS: readonly StepDef[] = [
  { key: "HELD", label: "Esperando orden", stampIndex: 0 },
  { key: "QUEUED", label: "En cola", stampIndex: 1 },
  { key: "IN_PREPARATION", label: "En preparación", stampIndex: 2 },
  { key: "READY", label: "En el pase", stampIndex: 3 },
];

// En la barra el viaje son dos pasos (ADR-037): pedido → listo.
const BAR_STEPS: readonly StepDef[] = [
  { key: "QUEUED", label: "Pedido", stampIndex: 0 },
  { key: "READY", label: "Listo", stampIndex: 3 },
];

/** Paso en el que está el plato ahora. steps.length = ya salió del tablero; -1 = anulado. */
function currentStepIndex(
  status: OrderItemStatus | null | undefined,
  profile: KdsFlowProfile
): number {
  if (status === "CANCELLED") return -1;
  if (status === "IN_DELIVERY" || status === "DELIVERED") return profile === "BAR" ? 2 : 4;
  if (profile === "BAR") return status === "READY" ? 1 : 0;
  switch (status) {
    case "HELD":
      return 0;
    case "QUEUED":
      return 1;
    case "READY":
      return 3;
    default:
      return 2; // IN_PREPARATION, RETURNED y los sin sello
  }
}

/**
 * Los pasos del plato con el tiempo de cada uno (cuatro en FULL_PASS, dos en BAR). Los tramos salen
 * de los sellos (createdAt → queuedAt → preparationStartedAt → readyAt → deliveredAt); el tramo del
 * paso actual se mide contra `now`, así late con el ticker de la página.
 */
export function buildRecorrido(
  item: OrderItemDto,
  orderCreatedAt: string,
  now: number,
  profile: KdsFlowProfile = "FULL_PASS"
): RecorridoStep[] {
  const stamps: (string | null | undefined)[] = [
    orderCreatedAt,
    item.queuedAt,
    item.preparationStartedAt,
    item.readyAt,
    item.deliveredAt,
  ];
  const ms = stamps.map((s) => {
    if (!s) return null;
    const t = new Date(s).getTime();
    return Number.isNaN(t) ? null : t;
  });
  const steps = profile === "BAR" ? BAR_STEPS : FULL_STEPS;
  const current = currentStepIndex(item.status, profile);

  return steps.map((step, i) => {
    const { key, label } = step;
    const start = ms[step.stampIndex] ?? null;
    // El fin del tramo es el primer sello que exista desde el inicio del paso siguiente
    // (un plato puede saltarse pasos).
    const from = steps[i + 1]?.stampIndex ?? 4;
    let end: number | null = null;
    for (let j = from; j < ms.length; j++) {
      const v = ms[j];
      if (v != null) {
        end = v;
        break;
      }
    }

    if (i < current) {
      return {
        key,
        label,
        state: "done" as const,
        time: start != null && end != null ? formatDur(end - start, true) : "—",
      };
    }
    if (i === current) {
      return {
        key,
        label,
        state: "current" as const,
        time: start != null ? formatDur(now - start) : "—",
      };
    }
    return { key, label, state: "future" as const, time: "—" };
  });
}

// ─── Densidad: la tarjeta se adapta a la carga de su columna ──────────────────
//
// Con servicio lleno el problema no es que falte sitio, es que el cocinero NO HACE SCROLL:
// tiene las manos ocupadas y lo que queda debajo del pliegue, sencillamente, no existe. Así
// que cuando la cola crece, la tarjeta se comprime en vez de esconderse: primero pierde aire,
// luego se reduce a una línea. El detalle completo nunca se pierde — sigue a un toque, en el
// modo foco.

export type KdsDensity = "comfy" | "compact" | "dense" | "row";

/**
 * Cuántas comandas caben antes de apretar. Los cortes salen de medir la tarjeta real en la
 * pantalla de cocina (1920×1080, columna útil ~850px): cómoda ~200px, compacta ~150px,
 * densa ~130px y en fila ~64px.
 */
export function densityFor(count: number): KdsDensity {
  if (count <= 4) return "comfy";
  if (count <= 8) return "compact";
  if (count <= 12) return "dense";
  // Servicio desbordado: cada comanda baja a una fila para que la cola entera quepa de un vistazo.
  return "row";
}

// ─── Orden de la cola (ADR-038) ───────────────────────────────────────────────

/** Reservados primero (priority desc) y, a igual prioridad, la más antigua arriba (FIFO). */
export function byPriorityThenAge(
  a: { priority?: number | undefined; createdAt: string },
  b: { priority?: number | undefined; createdAt: string }
): number {
  const dp = (b.priority ?? 0) - (a.priority ?? 0);
  return dp !== 0 ? dp : a.createdAt.localeCompare(b.createdAt);
}
