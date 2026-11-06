"use client";

import { useEffect, useMemo, useState } from "react";
import { catalogApi, ordersApi } from "@/lib/api";
import { useDialog } from "@/components/Dialog";
import type {
  PublicCatalogItemDto,
  PublicCatalogCategoryDto,
  PublicModifierGroupDto,
} from "@mestobeach/api-client";

type Timing = "NOW" | "LATER";

interface CartLine {
  key: string;
  item: PublicCatalogItemDto;
  quantity: number;
  selectedOptionIds: string[];
  summary: string;
  timing: Timing;
  notes?: string;
  unitPriceCents: number;
}

interface Props {
  tenantId: string;
  sessionId: string;
  participantId: string;
  tableCode: string;
  onClose: () => void;
  onSubmitted: () => void;
}

function euros(cents: number): string {
  return new Intl.NumberFormat("es-ES", { style: "currency", currency: "EUR" }).format(cents / 100);
}

/**
 * Toma de comandas del PDA (E3): carta con buscador, personalizaciones y elección ahora/luego,
 * un carrito, y envío como una sola comanda. Cada línea puede ir "ahora mismo" o "luego de mi
 * instrucción" (ADR-026); las de "luego" nacen retenidas y las marcha el propio PDA más tarde.
 */
export default function ComandaDrawer({
  tenantId,
  sessionId,
  participantId,
  tableCode,
  onClose,
  onSubmitted,
}: Props) {
  const [categories, setCategories] = useState<PublicCatalogCategoryDto[]>([]);
  const [items, setItems] = useState<PublicCatalogItemDto[]>([]);
  const [activeCat, setActiveCat] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [configuring, setConfiguring] = useState<PublicCatalogItemDto | null>(null);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [observacion, setObservacion] = useState("");
  const [sending, setSending] = useState(false);
  const dialog = useDialog();

  useEffect(() => {
    catalogApi
      .getPublicCatalog(tenantId)
      .then(({ data }) => {
        setCategories([...data.categories].sort((a, b) => a.displayOrder - b.displayOrder));
        setItems(data.items);
      })
      .catch(() => {});
  }, [tenantId]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items
      .filter((i) => i.available)
      .filter((i) => (q ? i.name.toLowerCase().includes(q) : activeCat ? i.categoryId === activeCat : true))
      .sort((a, b) => a.displayOrder - b.displayOrder);
  }, [items, search, activeCat]);

  const total = cart.reduce((s, l) => s + l.unitPriceCents * l.quantity, 0);

  function addLine(line: CartLine) {
    setCart((c) => [...c, line]);
    setConfiguring(null);
  }

  async function enviar() {
    if (cart.length === 0) return;
    setSending(true);
    try {
      await ordersApi.createOrder({
        sessionId,
        participantId,
        ...(observacion.trim() ? { observacion: observacion.trim() } : {}),
        items: cart.map((l) => ({
          catalogItemId: l.item.id,
          quantity: l.quantity,
          selectedOptionIds: l.selectedOptionIds,
          timing: l.timing,
          ...(l.notes ? { notes: l.notes } : {}),
        })),
      });
      onSubmitted();
    } catch (err: unknown) {
      const d =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "No se pudo enviar la comanda.";
      dialog.toast(
        d === "PIN_REQUIRED" ? "La mesa pide PIN y no se pudo validar. Pídeselo al comensal." : d,
        { tone: "error" }
      );
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-canvas text-ink">
      {/* Cabecera */}
      <header className="flex items-center justify-between border-b border-hair/40 bg-paper px-4 py-3 pt-[max(0.75rem,env(safe-area-inset-top))]">
        <div>
          <h2 className="font-display text-lg font-normal text-ink">Nueva comanda · {tableCode}</h2>
          <p className="text-xs text-muted">Carta, personaliza y elige ahora o luego</p>
        </div>
        <button
          onClick={onClose}
          className="min-h-[44px] rounded-lg border border-hair/55 px-4 py-1.5 text-sm font-bold text-muted hover:bg-paper2"
        >
          Cancelar
        </button>
      </header>

      <div className="flex min-h-0 flex-1 flex-col lg:flex-row">
        {/* Carta */}
        <div className="flex min-h-0 flex-1 flex-col border-b border-hair/40 lg:border-b-0 lg:border-r">
          <div className="space-y-2 p-3">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Buscar plato…"
              className="w-full rounded-lg border border-hair/55 bg-paper px-3 py-2 text-sm text-ink placeholder:text-muted"
            />
            {!search && (
              <div className="flex flex-wrap gap-2">
                <Chip active={activeCat === null} onClick={() => setActiveCat(null)}>
                  Todo
                </Chip>
                {categories.map((c) => (
                  <Chip key={c.id} active={activeCat === c.id} onClick={() => setActiveCat(c.id)}>
                    {c.name}
                  </Chip>
                ))}
              </div>
            )}
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto p-3 pt-0">
            <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {filtered.map((item) => (
                <li key={item.id}>
                  <button
                    onClick={() => setConfiguring(item)}
                    className="flex w-full items-center justify-between gap-3 rounded-xl border border-hair/50 bg-paper px-4 py-3 text-left transition hover:border-brand-500 hover:shadow-warm-sm"
                  >
                    <span className="min-w-0">
                      <span className="block truncate font-display text-base text-ink">
                        {item.name}
                      </span>
                      {item.modifierGroups.length > 0 && (
                        <span className="text-xs text-muted">Personalizable</span>
                      )}
                    </span>
                    <span className="shrink-0 text-sm font-bold tabular-nums text-brand-700">
                      {euros(item.priceCents)}
                    </span>
                  </button>
                </li>
              ))}
              {filtered.length === 0 && (
                <li className="col-span-full py-10 text-center text-sm text-muted">
                  Sin platos que coincidan.
                </li>
              )}
            </ul>
          </div>
        </div>

        {/* Carrito */}
        <aside className="flex min-h-0 w-full flex-col lg:w-96">
          <div className="min-h-0 flex-1 overflow-y-auto p-3">
            <p className="mb-2 text-xs uppercase tracking-wide text-muted">
              Comanda · {cart.length} línea{cart.length === 1 ? "" : "s"}
            </p>
            {cart.length === 0 ? (
              <p className="py-8 text-center text-sm text-muted">
                Añade platos desde la carta.
              </p>
            ) : (
              <ul className="space-y-2">
                {cart.map((l, idx) => (
                  <li key={l.key} className="rounded-xl border border-hair/40 bg-paper p-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-display text-base text-ink">
                          {l.quantity}× {l.item.name}
                        </p>
                        {l.summary && <p className="text-xs text-brand-700">{l.summary}</p>}
                        {l.notes && <p className="text-xs italic text-amber-700">{l.notes}</p>}
                        <span
                          className={`mt-1 inline-flex rounded-full px-2 py-0.5 text-[11px] font-medium tracking-[0.03em] ${
                            l.timing === "LATER"
                              ? "bg-amber-500/10 text-amber-700"
                              : "bg-emerald-500/10 text-emerald-700"
                          }`}
                        >
                          {l.timing === "LATER" ? "Luego" : "Ahora"}
                        </span>
                      </div>
                      <div className="flex flex-col items-end gap-1">
                        <span className="text-sm font-bold tabular-nums text-ink">
                          {euros(l.unitPriceCents * l.quantity)}
                        </span>
                        <button
                          onClick={() => setCart((c) => c.filter((_, i) => i !== idx))}
                          className="min-h-[44px] px-2 text-xs text-rose-600 hover:underline"
                        >
                          Quitar
                        </button>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="border-t border-hair/40 p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]">
            <label htmlFor="comanda-observacion" className="sr-only mb-1 text-sm font-semibold text-ink lg:not-sr-only lg:block">
              Observación <span className="font-normal text-muted">(toda la comanda)</span>
            </label>
            <textarea
              id="comanda-observacion"
              value={observacion}
              onChange={(e) => setObservacion(e.target.value)}
              rows={1}
              maxLength={500}
              placeholder="Alergias, todo para llevar, sin picante…"
              className="mb-2 min-h-[44px] w-full rounded-lg border border-hair/55 bg-paper px-3 py-2 text-sm text-ink placeholder:text-muted focus:border-oro focus:outline-none focus:ring-2 focus:ring-oro/20"
            />
            <div className="flex items-center gap-3">
              <div className="flex shrink-0 flex-col text-sm leading-tight">
                <span className="text-xs text-muted">Total</span>
                <span className="font-display text-xl tabular-nums text-ink">{euros(total)}</span>
              </div>
              <button
                onClick={enviar}
                disabled={sending || cart.length === 0}
                className="min-h-[44px] flex-1 rounded-xl bg-brand-500 px-4 py-2.5 text-base font-extrabold text-white transition hover:bg-brand-600 disabled:opacity-40"
              >
                {sending ? "Enviando…" : `Enviar comanda${cart.length ? ` (${cart.length})` : ""}`}
              </button>
            </div>
          </div>
        </aside>
      </div>

      {/* Configuración de un plato */}
      {configuring && (
        <ItemConfig
          item={configuring}
          onCancel={() => setConfiguring(null)}
          onAdd={addLine}
        />
      )}
    </div>
  );
}

function Chip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      onClick={onClick}
      className={`min-h-[44px] rounded-full px-4 py-1 text-sm font-medium tracking-[0.03em] transition ${
        active ? "bg-brand-500 text-white shadow-warm-sm" : "bg-paper2 text-muted hover:bg-paper"
      }`}
    >
      {children}
    </button>
  );
}

// ─── Configuración de un plato: modificadores + ahora/luego + notas ─────────────
function ItemConfig({
  item,
  onCancel,
  onAdd,
}: {
  item: PublicCatalogItemDto;
  onCancel: () => void;
  onAdd: (line: CartLine) => void;
}) {
  const [qty, setQty] = useState(1);
  const [timing, setTiming] = useState<Timing>("NOW");
  const [notes, setNotes] = useState("");
  // groupId -> set de optionIds elegidos
  const [picks, setPicks] = useState<Record<string, Set<string>>>(() => {
    const init: Record<string, Set<string>> = {};
    for (const g of item.modifierGroups) {
      // Preseleccionar los por defecto de los grupos de una sola opción requerida.
      const defaults = g.options.filter((o) => o.isDefault).map((o) => o.id);
      init[g.id] = new Set(g.type === "SINGLE" && g.required && defaults.length ? [defaults[0]!] : defaults);
    }
    return init;
  });

  function toggle(group: PublicModifierGroupDto, optionId: string) {
    setPicks((prev) => {
      const cur = new Set(prev[group.id] ?? []);
      if (group.type === "SINGLE") {
        cur.clear();
        cur.add(optionId);
      } else {
        if (cur.has(optionId)) cur.delete(optionId);
        else {
          if (group.maxSelect != null && cur.size >= group.maxSelect) return prev;
          cur.add(optionId);
        }
      }
      return { ...prev, [group.id]: cur };
    });
  }

  const selectedOptionIds = useMemo(
    () => Object.values(picks).flatMap((s) => [...s]),
    [picks]
  );

  const missingRequired = item.modifierGroups.filter(
    (g) => g.required && (picks[g.id]?.size ?? 0) < Math.max(1, g.minSelect)
  );

  const unitPriceCents = useMemo(() => {
    let p = item.priceCents;
    for (const g of item.modifierGroups) {
      for (const o of g.options) if (picks[g.id]?.has(o.id)) p += o.priceDeltaCents;
    }
    return p;
  }, [item, picks]);

  const summary = useMemo(() => {
    const parts: string[] = [];
    for (const g of item.modifierGroups) {
      const chosen = g.options.filter((o) => picks[g.id]?.has(o.id));
      for (const o of chosen) parts.push(g.type === "REMOVE" ? `sin ${o.name}` : o.name);
    }
    return parts.join(" · ");
  }, [item, picks]);

  function add() {
    if (missingRequired.length > 0) return;
    onAdd({
      key: `${item.id}-${Date.now()}-${Math.round(unitPriceCents)}`,
      item,
      quantity: qty,
      selectedOptionIds,
      summary,
      timing,
      ...(notes.trim() ? { notes: notes.trim() } : {}),
      unitPriceCents,
    });
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-end justify-center bg-ink/40 sm:items-center">
      <div className="flex max-h-[90dvh] w-full max-w-lg flex-col rounded-t-2xl bg-paper shadow-warm-lg sm:rounded-2xl">
        <div className="flex items-start justify-between gap-3 border-b border-hair/40 p-4">
          <div>
            <h3 className="font-display text-xl text-ink">{item.name}</h3>
            {item.description && <p className="text-sm text-muted">{item.description}</p>}
          </div>
          <button onClick={onCancel} aria-label="Cerrar" className="grid h-11 w-11 shrink-0 place-items-center text-2xl leading-none text-muted hover:text-ink">
            ×
          </button>
        </div>

        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
          {item.modifierGroups.map((g) => (
            <div key={g.id}>
              <div className="mb-1 flex items-baseline justify-between">
                <span className="text-sm font-semibold text-ink">{g.name}</span>
                <span className="text-xs text-muted">
                  {g.required ? "Obligatorio" : "Opcional"}
                  {g.type === "MULTI" && g.maxSelect ? ` · máx ${g.maxSelect}` : ""}
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                {g.options.map((o) => {
                  const on = picks[g.id]?.has(o.id) ?? false;
                  return (
                    <button
                      key={o.id}
                      disabled={!o.available}
                      onClick={() => toggle(g, o.id)}
                      className={`min-h-[44px] rounded-lg border px-3 py-2 text-sm font-medium transition disabled:opacity-40 ${
                        on
                          ? "border-brand-500 bg-oro/[0.10] text-brand-700"
                          : "border-hair/50 bg-paper2 text-ink hover:border-hair/70"
                      }`}
                    >
                      {g.type === "REMOVE" ? `sin ${o.name}` : o.name}
                      {o.priceDeltaCents !== 0 && (
                        <span className="ml-1 text-xs text-muted">
                          {o.priceDeltaCents > 0 ? "+" : ""}
                          {euros(o.priceDeltaCents)}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          ))}

          {item.allowNotes && (
            <div>
              <span className="mb-1 block text-sm font-semibold text-ink">Nota para cocina</span>
              <input
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="p. ej. poco hecho"
                className="w-full rounded-lg border border-hair/55 bg-paper px-3 py-2 text-sm text-ink placeholder:text-muted"
              />
            </div>
          )}

          {/* Ahora / Luego (ADR-026) */}
          <div>
            <span className="mb-1 block text-sm font-semibold text-ink">¿Cuándo sale?</span>
            <div className="grid grid-cols-2 gap-2">
              <button
                onClick={() => setTiming("NOW")}
                className={`min-h-[44px] rounded-lg border px-3 py-2.5 text-sm font-semibold transition ${
                  timing === "NOW"
                    ? "border-emerald-500 bg-emerald-500/10 text-emerald-700"
                    : "border-hair/50 text-muted hover:border-hair/70"
                }`}
              >
                Ahora mismo
              </button>
              <button
                onClick={() => setTiming("LATER")}
                className={`min-h-[44px] rounded-lg border px-3 py-2.5 text-sm font-semibold transition ${
                  timing === "LATER"
                    ? "border-amber-500 bg-amber-500/10 text-amber-700"
                    : "border-hair/50 text-muted hover:border-hair/70"
                }`}
              >
                Luego de mi instrucción
              </button>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-3 border-t border-hair/40 p-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
          <div className="flex items-center overflow-hidden rounded-lg border border-hair/55">
            <button
              onClick={() => setQty(Math.max(1, qty - 1))}
              className="min-h-[44px] min-w-[44px] px-3 py-2 text-lg font-bold text-ink hover:bg-paper2"
            >
              −
            </button>
            <span className="w-9 text-center text-lg font-extrabold tabular-nums text-ink">{qty}</span>
            <button
              onClick={() => setQty(Math.min(99, qty + 1))}
              className="min-h-[44px] min-w-[44px] px-3 py-2 text-lg font-bold text-ink hover:bg-paper2"
            >
              +
            </button>
          </div>
          <button
            onClick={add}
            disabled={missingRequired.length > 0}
            className="flex-1 rounded-xl bg-brand-500 px-4 py-3 text-base font-extrabold text-white transition hover:bg-brand-600 disabled:opacity-40"
            title={missingRequired.length > 0 ? `Falta elegir: ${missingRequired.map((g) => g.name).join(", ")}` : undefined}
          >
            {missingRequired.length > 0
              ? `Elige ${missingRequired[0]!.name}`
              : `Añadir · ${euros(unitPriceCents * qty)}`}
          </button>
        </div>
      </div>
    </div>
  );
}
