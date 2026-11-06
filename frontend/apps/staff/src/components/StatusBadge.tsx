import type { OrderStatus } from "@mestobeach/api-client";

// Estados de negocio en claro: los hues de flujo (pendiente/confirmado/en preparación)
// se mantienen legibles; "Listo" es estado de negocio y tira a dorado (joyería); cancelado va a rose.
const STATUS_CONFIG: Record<OrderStatus, { label: string; cls: string }> = {
  PENDING: { label: "Pendiente", cls: "bg-amber-500/10 text-amber-700" },
  CONFIRMED: { label: "Confirmado", cls: "bg-sky-500/10 text-sky-700" },
  IN_PREPARATION: { label: "En preparación", cls: "bg-orange-500/10 text-orange-700" },
  READY: { label: "Listo", cls: "bg-oro/[0.12] text-brand-700" },
  DELIVERED: { label: "Entregado", cls: "bg-paper2 text-muted" },
  CANCELLED: { label: "Cancelado", cls: "bg-rose-500/10 text-rose-600" },
};

export default function StatusBadge({ status }: { status: OrderStatus }) {
  const cfg = STATUS_CONFIG[status] ?? STATUS_CONFIG.PENDING;
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium tracking-[0.03em] ${cfg.cls}`}>
      {cfg.label}
    </span>
  );
}
