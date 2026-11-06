import type { PublicCatalogItemDto } from "@mestobeach/api-client";
import { formatEur } from "@/lib/api";

interface Props {
  item: PublicCatalogItemDto;
  cartQty: number;
  customizable: boolean;
  /** Tap anywhere on the card — opens the full product detail. */
  onOpen: () => void;
  /**
   * The round "+" — quick-adds (or opens detail when the product is customizable).
   * Recibe el recuadro del propio botón: de ahí sale el plato que vuela al carrito.
   */
  onAdd: (origen: DOMRect) => void;
  onRemove: () => void;
}

export default function ProductCard({ item, cartQty, customizable, onOpen, onAdd, onRemove }: Props) {
  const outOfStock = item.stockEnabled && item.stockQty === 0;
  const unavailable = !item.available || outOfStock;
  const imageCount = item.images?.length ?? (item.imageUrl ? 1 : 0);

  return (
    <div className={`m-card relative flex flex-col overflow-hidden rounded-2xl shadow-[0_1px_2px_rgba(78,58,38,0.05),0_10px_28px_-14px_rgba(78,58,38,0.18)] ${unavailable ? "opacity-60" : ""}`}>
      {/* Image */}
      <div className="relative">
        <button
          type="button"
          onClick={unavailable ? undefined : onOpen}
          disabled={unavailable}
          className="relative block aspect-square w-full overflow-hidden"
          style={{ background: "var(--m-border)" }}
        >
          {item.imageUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={item.imageUrl} alt={item.name} className="absolute inset-0 h-full w-full object-cover" />
          ) : (
            <div className="m-accent-text absolute inset-0 flex items-center justify-center text-3xl opacity-40">◆</div>
          )}

          {outOfStock && (
            <span className="absolute left-2 top-2 rounded-full bg-rose-600 px-2 py-0.5 text-[10px] font-medium tracking-[0.03em] text-white shadow-warm-sm">
              Agotado
            </span>
          )}
          {!unavailable && customizable && (
            <span className="m-accent-text absolute left-2 top-2 rounded-full bg-white/92 px-2 py-0.5 text-[10px] font-semibold tracking-[0.04em] shadow-sm">
              Personalizar
            </span>
          )}
          {imageCount > 1 && (
            <span className="absolute bottom-2 left-2 rounded-full bg-black/45 px-2 py-0.5 text-[10px] font-semibold text-white backdrop-blur">
              +{imageCount - 1}
            </span>
          )}
        </button>

        {/* Add / quantity control — bottom-right of the image */}
        {!unavailable && (
          <div className="absolute bottom-2 right-2">
            {cartQty > 0 ? (
              <div className="flex items-center gap-0.5 rounded-full bg-white/95 p-0.5 shadow-warm-sm">
                <button
                  onClick={onRemove}
                  aria-label="Quitar"
                  className="flex h-9 w-9 items-center justify-center rounded-full text-xl font-semibold active:brightness-95"
                  style={{ color: "var(--m-text)" }}
                >
                  −
                </button>
                <span className="w-5 text-center text-sm font-semibold" style={{ color: "var(--m-text)" }}>{cartQty}</span>
                <button
                  onClick={(e) => onAdd(e.currentTarget.getBoundingClientRect())}
                  aria-label={customizable ? "Personalizar otro" : "Agregar"}
                  className="m-accent-bg flex h-9 w-9 items-center justify-center rounded-full text-xl font-semibold active:brightness-90"
                >
                  +
                </button>
              </div>
            ) : (
              <button
                onClick={(e) => onAdd(e.currentTarget.getBoundingClientRect())}
                aria-label={customizable ? "Personalizar" : "Agregar"}
                className="m-accent-bg flex h-11 w-11 items-center justify-center rounded-full text-2xl font-semibold shadow-warm-sm active:brightness-90"
              >
                +
              </button>
            )}
          </div>
        )}
      </div>

      {/* Content */}
      <button
        type="button"
        onClick={unavailable ? undefined : onOpen}
        disabled={unavailable}
        className="flex flex-1 flex-col gap-0.5 px-2.5 pb-2.5 pt-2 text-left"
      >
        <p className="m-head line-clamp-2 text-[15px] leading-snug" style={{ color: "var(--m-text)" }}>
          {item.name}
        </p>
        {item.description && (
          <p className="line-clamp-1 text-xs" style={{ color: "var(--m-muted)" }}>
            {item.description}
          </p>
        )}
        <p className="mt-1 text-[15px] font-semibold" style={{ color: "var(--m-text)" }}>
          {customizable && <span className="mr-1 text-[10px] font-medium" style={{ color: "var(--m-muted)" }}>desde</span>}
          {formatEur(item.priceCents)}
        </p>
      </button>
    </div>
  );
}
