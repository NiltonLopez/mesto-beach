import type { OrderStatus } from "@mestobeach/api-client";
import type { CSSProperties } from "react";

/**
 * Chip de estado del pedido — dorado como hilo conductor (marca).
 * Los estados de negocio (confirmado / en preparación / listo) se pintan con el
 * acento del tenant; los terminales reposan (entregado en muted, cancelado en
 * rosa semántico legible). Sin bold gritón: font-medium con tracking sutil.
 */
const tint = (pct: number): CSSProperties => ({
  background: `color-mix(in srgb, var(--m-accent) ${pct}%, transparent)`,
  color: "var(--m-accent)",
});

const STATUS_CONFIG: Record<OrderStatus, { label: string; style: CSSProperties }> = {
  PENDING: {
    label: "Pendiente",
    style: { background: "color-mix(in srgb, var(--m-accent) 8%, transparent)", color: "var(--m-muted)" },
  },
  CONFIRMED: { label: "Confirmado", style: tint(12) },
  IN_PREPARATION: { label: "En preparación", style: tint(14) },
  READY: { label: "Listo", style: { background: "var(--m-accent)", color: "var(--m-accent-ink)" } },
  DELIVERED: {
    label: "Entregado",
    style: { background: "color-mix(in srgb, var(--m-muted) 12%, transparent)", color: "var(--m-muted)" },
  },
  CANCELLED: { label: "Cancelado", style: { background: "rgb(225 29 72 / 0.10)", color: "rgb(225 29 72)" } },
};

export default function StatusBadge({ status }: { status: OrderStatus }) {
  const cfg = STATUS_CONFIG[status] ?? STATUS_CONFIG.PENDING;
  return (
    <span
      className="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium tracking-[0.03em]"
      style={cfg.style}
    >
      {cfg.label}
    </span>
  );
}
