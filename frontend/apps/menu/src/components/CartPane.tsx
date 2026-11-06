"use client";

import { useCartStore } from "@/lib/stores";
import { formatEur } from "@/lib/api";
import EmptyState from "@/components/EmptyState";
import BotonCamarero from "@/components/BotonCamarero";

interface Props {
  onOrder: () => void;
  loading: boolean;
  /** Devuelve al comensal a la carta cuando el carrito está vacío. */
  onIrALaCarta: () => void;
  onCallWaiter: () => void;
  calling: boolean;
  callSent: boolean;
}

/**
 * El carrito como ventana propia. Antes era un cajón sobre la carta; con la barra
 * flotante abajo los dos se estorbaban, y como ventana además se llega deslizando.
 */
export default function CartPane({
  onOrder, loading, onIrALaCarta, onCallWaiter, calling, callSent,
}: Props) {
  const { items, updateQuantity, totalCents, totalItems, observacion, setObservacion } =
    useCartStore();

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex items-start justify-between gap-3 px-5 pb-3 pt-6">
        <div className="min-w-0">
          <h1 className="m-head text-3xl" style={{ color: "var(--m-text)" }}>Mi pedido</h1>
          <p className="mt-1 text-sm" style={{ color: "var(--m-muted)" }}>
            {items.length === 0
              ? "Todavía no has añadido nada."
              : "Revisa antes de mandarlo a cocina."}
          </p>
        </div>
        {/* Llamar al camarero también desde aquí: es donde se duda —una alergia,
            un cambio— justo antes de mandar la comanda. */}
        <BotonCamarero
          compacto
          variante="solido"
          llamando={calling}
          avisado={callSent}
          onClick={onCallWaiter}
          className="mt-1 shrink-0"
        />
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto overscroll-contain px-5 pb-4">
        {items.length === 0 ? (
          <div className="flex flex-col items-center pt-10">
            <EmptyState title="Tu pedido aún no tiene platos" />
            <button onClick={onIrALaCarta} className="btn-primary mt-2">Ver la carta</button>
          </div>
        ) : (
          items.map((item) => (
            <div key={item.lineId} className="flex items-start gap-3">
              <div className="flex-1">
                <p className="m-head text-[15px] leading-snug" style={{ color: "var(--m-text)" }}>{item.name}</p>
                {item.modifiersSummary && (
                  <p className="text-xs" style={{ color: "var(--m-accent)" }}>{item.modifiersSummary}</p>
                )}
                {item.notes && (
                  <p className="text-xs italic" style={{ color: "var(--m-muted)" }}>“{item.notes}”</p>
                )}
                <p className="text-sm" style={{ color: "var(--m-muted)" }}>{formatEur(item.priceCents)}</p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => updateQuantity(item.lineId, item.quantity - 1)}
                  aria-label="Quitar uno"
                  className="flex h-7 w-7 items-center justify-center rounded-full text-lg transition active:scale-90"
                  style={{ background: "color-mix(in srgb, var(--m-accent) 8%, transparent)", color: "var(--m-text)" }}
                >
                  −
                </button>
                <span className="w-5 text-center text-sm font-semibold" style={{ color: "var(--m-text)" }}>{item.quantity}</span>
                <button
                  onClick={() => updateQuantity(item.lineId, item.quantity + 1)}
                  aria-label="Agregar uno"
                  className="m-accent-bg flex h-7 w-7 items-center justify-center rounded-full text-lg transition active:scale-90"
                >
                  +
                </button>
              </div>
              <p className="w-16 text-right text-sm font-semibold" style={{ color: "var(--m-text)" }}>
                {formatEur(item.priceCents * item.quantity)}
              </p>
            </div>
          ))
        )}
      </div>

      {items.length > 0 && (
        <div
          className="space-y-3 border-t px-5 pt-4"
          style={{
            borderColor: "var(--m-border)",
            background: "var(--m-bg)",
            // Deja libre la altura de la barra flotante, que va por encima.
            paddingBottom: "calc(env(safe-area-inset-bottom) + 5rem)",
          }}
        >
          <div>
            <label
              htmlFor="cart-observacion"
              className="mb-1 block text-sm font-semibold"
              style={{ color: "var(--m-text)" }}
            >
              Observación{" "}
              <span className="font-normal" style={{ color: "var(--m-muted)" }}>(opcional)</span>
            </label>
            <textarea
              id="cart-observacion"
              className="input"
              rows={2}
              maxLength={500}
              placeholder="Algo para toda la mesa: alergias, todo para llevar, sin picante…"
              value={observacion}
              onChange={(e) => setObservacion(e.target.value)}
            />
          </div>
          <div className="flex items-center justify-between text-base font-semibold" style={{ color: "var(--m-text)" }}>
            <span>Total</span>
            <span>{formatEur(totalCents())}</span>
          </div>
          {/* El botón repite cuánto y por cuánto: es lo último que se ve antes de
              mandar la comanda, y así nadie envía de más sin darse cuenta. */}
          {/* `text-sm`: con el tamaño base la línea entera —«Enviar comanda · N
              productos · 00,00 €»— no cabe en un móvil estrecho y el botón se
              parte en dos. `flex-wrap` se queda de red para los carritos largos. */}
          <button onClick={onOrder} disabled={loading} className="btn-primary w-full text-sm">
            {loading ? (
              "Enviando comanda…"
            ) : (
              <span className="flex flex-wrap items-center justify-center gap-x-2">
                <span>Enviar comanda</span>
                <span aria-hidden style={{ opacity: 0.5 }}>·</span>
                <span>{totalItems()} {totalItems() === 1 ? "producto" : "productos"}</span>
                <span aria-hidden style={{ opacity: 0.5 }}>·</span>
                <span className="tabular-nums">{formatEur(totalCents())}</span>
              </span>
            )}
          </button>
        </div>
      )}
    </div>
  );
}
