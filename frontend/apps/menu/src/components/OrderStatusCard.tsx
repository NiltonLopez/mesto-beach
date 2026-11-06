import type { OrderSummaryDto, OrderItemDto } from "@mestobeach/api-client";
import StatusBadge from "./StatusBadge";
import { formatEur } from "@/lib/api";

interface Props {
  order: OrderSummaryDto & { items?: OrderItemDto[] };
  onCancel?: (orderId: string) => void;
}

const ACTIVE_STATUSES = new Set(["PENDING", "CONFIRMED", "IN_PREPARATION"]);

export default function OrderStatusCard({ order, onCancel }: Props) {
  const canCancel = ACTIVE_STATUSES.has(order.status);

  return (
    <div className="card p-4">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="text-xs" style={{ color: "var(--m-muted)" }}>
            Orden #{order.id.slice(-6).toUpperCase()}
          </p>
          <p className="mt-0.5 text-sm font-semibold" style={{ color: "var(--m-text)" }}>
            {order.itemCount} {order.itemCount === 1 ? "ítem" : "ítems"} ·{" "}
            {formatEur(order.totalCents)}
          </p>
        </div>
        <StatusBadge status={order.status} />
      </div>

      {order.status === "READY" && (
        <div
          className="mt-3 rounded-xl p-3"
          style={{ background: "color-mix(in srgb, var(--m-accent) 10%, transparent)" }}
        >
          <p className="m-head text-[15px] leading-snug" style={{ color: "var(--m-accent)" }}>
            Tu pedido está listo — el camarero lo lleva a tu mesa.
          </p>
        </div>
      )}

      {order.cancellationReason && (
        <p className="mt-2 text-xs text-rose-600">Motivo: {order.cancellationReason}</p>
      )}

      {canCancel && onCancel && (
        <button
          onClick={() => onCancel(order.id)}
          className="mt-3 text-xs font-medium text-rose-600 underline"
        >
          Cancelar pedido
        </button>
      )}
    </div>
  );
}
