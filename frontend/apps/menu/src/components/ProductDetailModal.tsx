"use client";

import { useMemo, useState } from "react";
import type { PublicCatalogItemDto, PublicModifierGroupDto } from "@mestobeach/api-client";
import { formatEur } from "@/lib/api";
import type { CartLineInput } from "@/lib/stores";
import ImageCarousel from "./ImageCarousel";

interface Props {
  item: PublicCatalogItemDto;
  onClose: () => void;
  /** `origen` es el recuadro del botón: de ahí sale el plato que vuela al carrito. */
  onAdd: (line: CartLineInput, quantity: number, origen: DOMRect) => void;
}

/** Builds the "3/4 · sin Cebolla · + Michelada" summary from the current selection. */
function buildSummary(groups: PublicModifierGroupDto[], selected: Set<string>): string {
  const parts: string[] = [];
  for (const g of groups) {
    for (const o of g.options) {
      if (!selected.has(o.id)) continue;
      if (g.type === "REMOVE") parts.push(`sin ${o.name}`);
      else if (g.type === "SINGLE") parts.push(o.name);
      else parts.push(`+ ${o.name}`);
    }
  }
  return parts.join(" · ");
}

/**
 * Full product detail sheet: an auto-playing image gallery up top, the full
 * description, and (when the product has them) the customization options — so a
 * customer can browse photos and read while choosing, then add to the order.
 */
export default function ProductDetailModal({ item, onClose, onAdd }: Props) {
  const groups = item.modifierGroups ?? [];
  const images = item.images?.length ? item.images : item.imageUrl ? [item.imageUrl] : [];

  const [selected, setSelected] = useState<Set<string>>(() => {
    const s = new Set<string>();
    for (const g of groups) {
      if (g.type === "SINGLE") {
        const def = g.options.find((o) => o.isDefault && o.available);
        if (def) s.add(def.id);
      }
    }
    return s;
  });
  const [notes, setNotes] = useState("");
  const [qty, setQty] = useState(1);

  function chooseSingle(group: PublicModifierGroupDto, optionId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      for (const o of group.options) next.delete(o.id);
      next.add(optionId);
      return next;
    });
  }

  function toggleMulti(group: PublicModifierGroupDto, optionId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(optionId)) {
        next.delete(optionId);
      } else {
        const count = group.options.filter((o) => next.has(o.id)).length;
        if (group.maxSelect != null && count >= group.maxSelect) return prev;
        next.add(optionId);
      }
      return next;
    });
  }

  const unitPrice = useMemo(() => {
    let price = item.priceCents;
    for (const g of groups) {
      if (g.type === "REMOVE") continue;
      for (const o of g.options) if (selected.has(o.id)) price += o.priceDeltaCents;
    }
    return price;
  }, [item.priceCents, groups, selected]);

  const missing = useMemo(() => {
    const out: string[] = [];
    for (const g of groups) {
      const count = g.options.filter((o) => selected.has(o.id)).length;
      if (g.type === "SINGLE") {
        if (g.required && count < 1) out.push(g.name);
      } else if (g.type === "MULTI") {
        const min = g.required ? Math.max(1, g.minSelect) : g.minSelect;
        if (count < min) out.push(g.name);
      }
    }
    return out;
  }, [groups, selected]);

  const outOfStock = item.stockEnabled && item.stockQty === 0;
  const unavailable = !item.available || outOfStock;

  function handleAdd(origen: DOMRect) {
    if (missing.length > 0 || unavailable) return;
    const summary = buildSummary(groups, selected);
    const trimmedNotes = notes.trim();
    onAdd(
      {
        catalogItemId: item.id,
        name: item.name,
        priceCents: unitPrice,
        selectedOptionIds: [...selected],
        ...(summary ? { modifiersSummary: summary } : {}),
        ...(trimmedNotes ? { notes: trimmedNotes } : {}),
      },
      qty,
      origen
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex flex-col justify-end bg-ink/40 min-[560px]:items-center min-[560px]:justify-center min-[560px]:p-4">
      <div className="absolute inset-0" onClick={onClose} />

      <div
        className="relative flex max-h-[94vh] w-full flex-col overflow-hidden rounded-t-3xl min-[560px]:max-h-[90vh] min-[560px]:max-w-md min-[560px]:rounded-3xl min-[560px]:shadow-warm-lg"
        style={{ background: "var(--m-surface)", color: "var(--m-text)" }}
      >
        {/* Close button floats over the gallery */}
        <button
          onClick={onClose}
          aria-label="Cerrar"
          className="absolute right-3 top-3 z-10 flex h-10 w-10 items-center justify-center rounded-full bg-black/45 text-lg text-white backdrop-blur active:scale-95"
        >
          ✕
        </button>

        {/* Scrollable content */}
        <div className="flex-1 overflow-y-auto overscroll-contain">
          {/* Image gallery */}
          <ImageCarousel images={images} alt={item.name} className="aspect-square w-full bg-arena" />

          <div className="px-5 pb-4 pt-4">
            {/* Title + price */}
            <div className="flex items-start justify-between gap-3">
              <h2 className="m-head text-2xl leading-tight" style={{ color: "var(--m-text)" }}>
                {item.name}
              </h2>
              <p className="m-accent-text flex-shrink-0 pt-1 text-xl font-semibold">
                {groups.length > 0 && <span className="mr-1 text-xs font-medium" style={{ color: "var(--m-muted)" }}>desde</span>}
                {formatEur(item.priceCents)}
              </p>
            </div>

            {outOfStock && (
              <span className="mt-2 inline-block rounded-full bg-rose-500/10 px-2.5 py-1 text-xs font-medium tracking-[0.03em] text-rose-600">
                Agotado por ahora
              </span>
            )}

            {item.description && (
              <p className="mt-3 text-[15px] leading-relaxed" style={{ color: "var(--m-muted)" }}>
                {item.description}
              </p>
            )}

            {/* Modifier groups */}
            <div className="mt-5 space-y-5">
              {groups.map((g) => {
                const count = g.options.filter((o) => selected.has(o.id)).length;
                return (
                  <div key={g.id}>
                    <div className="mb-2 flex items-baseline justify-between">
                      <h3 className="text-base font-semibold" style={{ color: "var(--m-text)" }}>{g.name}</h3>
                      <span className="text-xs font-medium" style={{ color: "var(--m-muted)" }}>
                        {g.required ? "Obligatorio" : "Opcional"}
                        {g.type === "MULTI" && g.maxSelect != null ? ` · hasta ${g.maxSelect}` : ""}
                      </span>
                    </div>
                    <div className="space-y-2">
                      {g.options.map((o) => {
                        const isSel = selected.has(o.id);
                        const disabled = !o.available;
                        return (
                          <button
                            key={o.id}
                            type="button"
                            disabled={disabled}
                            onClick={() => (g.type === "SINGLE" ? chooseSingle(g, o.id) : toggleMulti(g, o.id))}
                            className={`flex min-h-[52px] w-full items-center justify-between rounded-2xl border-2 px-4 py-3 text-left transition ${
                              disabled ? "cursor-not-allowed opacity-40" : "active:scale-[0.99]"
                            }`}
                            style={{
                              borderColor: isSel ? "var(--m-accent)" : "var(--m-border)",
                              background: isSel ? "color-mix(in srgb, var(--m-accent) 10%, var(--m-surface))" : "var(--m-surface)",
                            }}
                          >
                            <span className="flex items-center gap-3">
                              <span
                                className={`flex h-6 w-6 flex-shrink-0 items-center justify-center text-xs ${
                                  g.type === "SINGLE" ? "rounded-full" : "rounded-md"
                                }`}
                                style={{
                                  border: `2px solid ${isSel ? "var(--m-accent)" : "var(--m-border)"}`,
                                  background: isSel ? "var(--m-accent)" : "transparent",
                                  color: isSel ? "var(--m-accent-ink)" : "transparent",
                                }}
                              >
                                {g.type === "SINGLE" ? "●" : "✓"}
                              </span>
                              <span className="text-[15px] font-medium" style={{ color: "var(--m-text)" }}>
                                {g.type === "REMOVE" ? `Sin ${o.name}` : o.name}
                              </span>
                            </span>
                            {g.type !== "REMOVE" && o.priceDeltaCents > 0 && (
                              <span className="text-sm font-semibold" style={{ color: "var(--m-muted)" }}>
                                +{formatEur(o.priceDeltaCents)}
                              </span>
                            )}
                          </button>
                        );
                      })}
                    </div>
                    {g.required && count === 0 && (
                      <p className="mt-1.5 text-xs text-amber-600">Elige una opción.</p>
                    )}
                  </div>
                );
              })}

              {/* Free comment */}
              {item.allowNotes && (
                <div>
                  <h3 className="mb-2 text-base font-bold" style={{ color: "var(--m-text)" }}>Comentario</h3>
                  <textarea
                    className="input"
                    rows={2}
                    maxLength={300}
                    placeholder="Ej: sin sal, término medio, alérgico a…"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                  />
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Sticky footer */}
        <div
          className="flex items-center gap-3 border-t px-5 py-4"
          style={{ borderColor: "var(--m-border)", paddingBottom: "calc(1rem + env(safe-area-inset-bottom))" }}
        >
          {/* Quantity stepper */}
          <div className="flex flex-shrink-0 items-center gap-1 rounded-full border-2 p-1" style={{ borderColor: "var(--m-border)" }}>
            <button
              onClick={() => setQty((q) => Math.max(1, q - 1))}
              className="flex h-10 w-10 items-center justify-center rounded-full text-2xl font-bold active:scale-90"
              style={{ color: "var(--m-text)" }}
              aria-label="Menos"
            >
              −
            </button>
            <span className="w-6 text-center text-lg font-bold">{qty}</span>
            <button
              onClick={() => setQty((q) => q + 1)}
              className="flex h-10 w-10 items-center justify-center rounded-full text-2xl font-bold active:scale-90"
              style={{ color: "var(--m-text)" }}
              aria-label="Más"
            >
              +
            </button>
          </div>

          <button
            onClick={(e) => handleAdd(e.currentTarget.getBoundingClientRect())}
            disabled={missing.length > 0 || unavailable}
            className="btn-primary flex-1 justify-between text-base disabled:opacity-50"
          >
            <span>{unavailable ? "No disponible" : missing.length > 0 ? `Falta: ${missing.join(", ")}` : "Agregar"}</span>
            {!unavailable && missing.length === 0 && <span>{formatEur(unitPrice * qty)}</span>}
          </button>
        </div>
      </div>
    </div>
  );
}
