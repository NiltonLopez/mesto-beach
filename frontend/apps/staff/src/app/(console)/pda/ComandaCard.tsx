"use client";

import type { OrderSummaryDto, OrderItemStatus } from "@mestobeach/api-client";

const STATUS_LABEL: Record<OrderItemStatus, { label: string; cls: string }> = {
  HELD: { label: "En espera", cls: "bg-amber-500/10 text-amber-700" },
  // Marchado: ya está en la cola de su estación, esperando la orden de preparación.
  QUEUED: { label: "En cola", cls: "bg-oro/[0.14] text-brand-700" },
  IN_PREPARATION: { label: "En preparación", cls: "bg-orange-500/10 text-orange-700" },
  READY: { label: "En el pase", cls: "bg-emerald-500/10 text-emerald-700" },
  IN_DELIVERY: { label: "En reparto", cls: "bg-sky-500/10 text-sky-700" },
  DELIVERED: { label: "Entregado", cls: "bg-oro/[0.08] text-muted" },
  RETURNED: { label: "Devuelto", cls: "bg-rose-500/10 text-rose-600" },
  CANCELLED: { label: "Anulado", cls: "bg-oro/[0.08] text-muted" },
};

function euros(cents: number): string {
  return new Intl.NumberFormat("es-ES", { style: "currency", currency: "EUR" }).format(cents / 100);
}

interface Props {
  order: OrderSummaryDto;
  busyItem: string | null;
  onMarchar: (itemId: string) => void;
}

/**
 * Una comanda de la mesa, vista por el PDA: cada plato con su estado y, si está EN ESPERA, el
 * botón "Marchar" — potestad exclusiva del PDA (ADR-026). Los anulados no se listan.
 */
export default function ComandaCard({ order, busyItem, onMarchar }: Props) {
  const items = order.items.filter((i) => i.status !== "CANCELLED");
  if (items.length === 0) return null;

  return (
    <div className="rounded-xl border border-hair/40 bg-paper2 p-3">
      {order.observacion && (
        <div className="mb-2 rounded-lg bg-oro/[0.12] px-2.5 py-1.5 text-sm text-brand-700">
          <span className="mr-1.5 text-[10px] font-bold uppercase tracking-wide text-brand-700/70">
            Observación
          </span>
          <span className="font-semibold">{order.observacion}</span>
        </div>
      )}
      <ul className="space-y-2">
        {items.map((item) => {
          const status = (item.status ?? "IN_PREPARATION") as OrderItemStatus;
          const meta = STATUS_LABEL[status];
          const held = status === "HELD";
          const busy = busyItem === item.id;
          return (
            <li key={item.id} className="flex items-start justify-between gap-3">
              <div className="min-w-0 flex-1">
                <p className="font-display text-lg text-ink">
                  {item.quantity}× {item.itemNameSnapshot}
                </p>
                {item.selectedModifiers && (
                  <p className="text-sm font-medium text-brand-700">{item.selectedModifiers}</p>
                )}
                {item.notes && <p className="text-sm italic text-amber-700">{item.notes}</p>}
                <span
                  className={`mt-1 inline-flex rounded-full px-2 py-0.5 text-xs font-medium tracking-[0.03em] ${meta.cls}`}
                >
                  {meta.label}
                </span>
              </div>

              {held ? (
                <button
                  onClick={() => onMarchar(item.id)}
                  disabled={busy}
                  className="min-h-[44px] shrink-0 rounded-lg bg-brand-500 px-4 text-sm font-extrabold text-white transition hover:bg-brand-600 disabled:opacity-50"
                >
                  {busy ? "…" : "Marchar"}
                </button>
              ) : (
                <span className="shrink-0 text-xs tabular-nums text-muted">
                  {euros(item.itemPriceSnapshotCents * item.quantity)}
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
