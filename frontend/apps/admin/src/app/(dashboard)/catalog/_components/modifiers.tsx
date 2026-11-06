"use client";

import { useMemo, useState } from "react";
import type {
  ModifierGroupDto,
  ModifierType,
  ModifierOptionInput,
  UpsertModifierGroupRequest,
} from "@mestobeach/api-client";
import { catalogApi, formatEur, centsToEurInput, parseEurosToCents } from "@/lib/api";
import { useDialog } from "@/components/Dialog";

function errMsg(err: unknown, fallback: string): string {
  return (
    (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback
  );
}

// ── Type metadata (drives the UI for each modifier kind) ──

export const TYPE_META: Record<
  ModifierType,
  { label: string; short: string; desc: string; emoji: string; badge: string }
> = {
  SINGLE: {
    label: "Única (radio)",
    short: "Radio",
    desc: "El cliente elige exactamente una opción. Ej: término de la carne 1/2, 3/4.",
    emoji: "◉",
    badge: "bg-violet-500/10 text-violet-700",
  },
  MULTI: {
    label: "Múltiple (checkbox)",
    short: "Checkbox",
    desc: "El cliente puede agregar varias, con costo. Ej: + Michelada, + Queso.",
    emoji: "＋",
    badge: "bg-emerald-500/10 text-emerald-700",
  },
  REMOVE: {
    label: "Quitar (sin X)",
    short: "Sin X",
    desc: "El cliente quita ingredientes, sin costo. Ej: sin cebolla, sin tomate.",
    emoji: "⊘",
    badge: "bg-amber-500/10 text-amber-700",
  },
};

// ── Local lightweight modal (packages/ui is empty) ──

function Modal({
  title,
  onClose,
  children,
  wide,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
  wide?: boolean;
}) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4"
      onClick={onClose}
    >
      <div
        className={`max-h-[90vh] w-full overflow-y-auto rounded-xl bg-paper p-6 shadow-warm-lg ${
          wide ? "max-w-2xl" : "max-w-md"
        }`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h2 className="font-display text-xl font-normal text-ink">{title}</h2>
          <button onClick={onClose} className="text-muted hover:text-ink" aria-label="Cerrar">
            ✕
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

// ── Option form row shape (price held as string for editable input) ──

interface OptionRow {
  id?: string;
  name: string;
  priceDeltaEur: string;
  available: boolean;
  isDefault: boolean;
}

interface GroupForm {
  id: string | null;
  name: string;
  type: ModifierType;
  required: boolean;
  minSelect: string;
  maxSelect: string; // empty = sin límite
  options: OptionRow[];
}

function emptyOption(): OptionRow {
  return { name: "", priceDeltaEur: "0.00", available: true, isDefault: false };
}

function toForm(g: ModifierGroupDto | null): GroupForm {
  if (!g)
    return {
      id: null,
      name: "",
      type: "MULTI",
      required: false,
      minSelect: "0",
      maxSelect: "",
      options: [emptyOption()],
    };
  return {
    id: g.id,
    name: g.name,
    type: g.type,
    required: g.required,
    minSelect: String(g.minSelect),
    maxSelect: g.maxSelect == null ? "" : String(g.maxSelect),
    options: g.options.map((o) => ({
      id: o.id,
      name: o.name,
      priceDeltaEur: centsToEurInput(o.priceDeltaCents),
      available: o.available,
      isDefault: o.isDefault,
    })),
  };
}

// ── Group editor modal ──

export function ModifierGroupEditorModal({
  tenantId,
  initial,
  onClose,
  onSaved,
}: {
  tenantId: string;
  initial: ModifierGroupDto | null;
  onClose: () => void;
  onSaved: (msg: string) => void;
}) {
  const [form, setForm] = useState<GroupForm>(() => toForm(initial));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isRemove = form.type === "REMOVE";
  const isSingle = form.type === "SINGLE";

  function patch(p: Partial<GroupForm>) {
    setForm((f) => ({ ...f, ...p }));
  }

  function setOption(idx: number, p: Partial<OptionRow>) {
    setForm((f) => ({
      ...f,
      options: f.options.map((o, i) => (i === idx ? { ...o, ...p } : o)),
    }));
  }

  function setDefault(idx: number) {
    // Only one default for SINGLE.
    setForm((f) => ({
      ...f,
      options: f.options.map((o, i) => ({ ...o, isDefault: i === idx })),
    }));
  }

  function addOption() {
    setForm((f) => ({ ...f, options: [...f.options, emptyOption()] }));
  }

  function removeOption(idx: number) {
    setForm((f) => ({ ...f, options: f.options.filter((_, i) => i !== idx) }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const name = form.name.trim();
    if (!name) return setError("Ponle un nombre al grupo (ej: Término de la carne).");
    const options = form.options
      .map((o) => ({ ...o, name: o.name.trim() }))
      .filter((o) => o.name.length > 0);
    if (options.length === 0) return setError("Agrega al menos una opción.");

    const minSelect = isRemove ? 0 : Math.max(0, Number(form.minSelect) || 0);
    const maxSelectNum =
      form.maxSelect.trim() === "" ? null : Math.max(1, Number(form.maxSelect) || 1);
    if (maxSelectNum != null && maxSelectNum < minSelect)
      return setError("El máximo no puede ser menor que el mínimo.");

    const payload: UpsertModifierGroupRequest = {
      name,
      type: form.type,
      required: isRemove ? false : form.required,
      minSelect: isSingle && form.required ? 1 : minSelect,
      maxSelect: isSingle ? 1 : maxSelectNum,
      options: options.map<ModifierOptionInput>((o) => ({
        ...(o.id ? { id: o.id } : {}),
        name: o.name,
        priceDeltaCents: isRemove ? 0 : (parseEurosToCents(o.priceDeltaEur) ?? 0),
        available: o.available,
        isDefault: isSingle ? o.isDefault : false,
      })),
    };

    setSubmitting(true);
    try {
      if (form.id) {
        await catalogApi.updateModifierGroup(tenantId, form.id, payload);
        onSaved(`Adición "${name}" actualizada.`);
      } else {
        await catalogApi.createModifierGroup(tenantId, payload);
        onSaved(`Adición "${name}" creada.`);
      }
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo guardar la adición."));
      setSubmitting(false);
    }
  }

  return (
    <Modal
      title={form.id ? "Editar adición" : "Nueva adición"}
      onClose={onClose}
      wide
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div className="rounded-lg border border-rose-600/30 bg-rose-500/10 px-3 py-2 text-sm text-rose-700">
            {error}
          </div>
        )}

        <div>
          <label className="label">Nombre del grupo</label>
          <input
            className="input"
            placeholder="Ej: Término de la carne, Adiciones, Sin ingredientes"
            value={form.name}
            onChange={(e) => patch({ name: e.target.value })}
            maxLength={120}
            autoFocus
            required
          />
        </div>

        {/* Type selector */}
        <div>
          <label className="label">¿Cómo elige el cliente?</label>
          <div className="grid gap-2 sm:grid-cols-3">
            {(Object.keys(TYPE_META) as ModifierType[]).map((t) => {
              const meta = TYPE_META[t];
              const active = form.type === t;
              return (
                <button
                  type="button"
                  key={t}
                  onClick={() => patch({ type: t })}
                  className={`rounded-lg border p-3 text-left transition ${
                    active
                      ? "border-brand-500 bg-oro/[0.08] ring-1 ring-brand-500"
                      : "border-hair/40 hover:border-hair/60"
                  }`}
                >
                  <div className="text-sm font-semibold text-ink">
                    {meta.emoji} {meta.label}
                  </div>
                  <p className="mt-1 text-xs text-muted">{meta.desc}</p>
                </button>
              );
            })}
          </div>
        </div>

        {/* Required + min/max */}
        {!isRemove && (
          <div className="flex flex-wrap items-center gap-4">
            <label className="flex items-center gap-2 text-sm text-ink">
              <input
                type="checkbox"
                checked={form.required}
                onChange={(e) => patch({ required: e.target.checked })}
              />
              {isSingle ? "El cliente debe elegir una opción" : "Obligatorio elegir al menos una"}
            </label>
            {!isSingle && (
              <div className="flex items-center gap-2 text-sm text-muted">
                <span>Mín</span>
                <input
                  type="number"
                  min={0}
                  className="input w-16 py-1"
                  value={form.minSelect}
                  onChange={(e) => patch({ minSelect: e.target.value })}
                />
                <span>Máx</span>
                <input
                  type="number"
                  min={1}
                  placeholder="∞"
                  className="input w-16 py-1"
                  value={form.maxSelect}
                  onChange={(e) => patch({ maxSelect: e.target.value })}
                />
              </div>
            )}
          </div>
        )}
        {isSingle && (
          <p className="-mt-2 text-xs text-muted">
            En “única” el cliente solo puede elegir una opción (radio). Marca una como predeterminada.
          </p>
        )}

        {/* Options */}
        <div>
          <div className="mb-1 flex items-center justify-between">
            <label className="label mb-0">Opciones</label>
            <button
              type="button"
              onClick={addOption}
              className="text-xs font-semibold text-brand-700 hover:underline"
            >
              + Agregar opción
            </button>
          </div>
          <div className="space-y-2">
            {form.options.map((o, idx) => (
              <div
                key={idx}
                className="flex flex-wrap items-center gap-2 rounded-lg border border-hair/40 bg-paper2 p-2"
              >
                {isSingle && (
                  <input
                    type="radio"
                    name="default-option"
                    title="Predeterminada"
                    checked={o.isDefault}
                    onChange={() => setDefault(idx)}
                  />
                )}
                <input
                  className="input flex-1 py-1"
                  placeholder={isRemove ? "Ej: Cebolla" : "Nombre de la opción"}
                  value={o.name}
                  onChange={(e) => setOption(idx, { name: e.target.value })}
                  maxLength={120}
                />
                {!isRemove && (
                  <div className="flex items-center gap-1">
                    <span className="text-xs text-muted">+€</span>
                    <input
                      type="number"
                      min={0}
                      step={0.01}
                      className="input w-24 py-1"
                      value={o.priceDeltaEur}
                      onChange={(e) => setOption(idx, { priceDeltaEur: e.target.value })}
                    />
                  </div>
                )}
                <label
                  className="flex items-center gap-1 text-xs text-muted"
                  title="Disponible para los clientes"
                >
                  <input
                    type="checkbox"
                    checked={o.available}
                    onChange={(e) => setOption(idx, { available: e.target.checked })}
                  />
                  Disp.
                </label>
                <button
                  type="button"
                  onClick={() => removeOption(idx)}
                  className="text-muted hover:text-rose-600"
                  aria-label="Quitar opción"
                  disabled={form.options.length === 1}
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" onClick={onClose} className="btn-secondary">
            Cancelar
          </button>
          <button type="submit" disabled={submitting} className="btn-primary">
            {submitting ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

// ── Add-ons library (searchable list view) ──

export function AddonsLibrary({
  tenantId,
  groups,
  loading,
  onChanged,
  flash,
  setError,
}: {
  tenantId: string;
  groups: ModifierGroupDto[];
  loading: boolean;
  onChanged: () => Promise<void> | void;
  flash: (msg: string) => void;
  setError: (msg: string) => void;
}) {
  const dialog = useDialog();
  const [editing, setEditing] = useState<ModifierGroupDto | null | "new">(null);
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return groups;
    return groups.filter(
      (g) =>
        g.name.toLowerCase().includes(q) ||
        g.options.some((o) => o.name.toLowerCase().includes(q))
    );
  }, [groups, query]);

  async function handleDelete(g: ModifierGroupDto) {
    const warn =
      g.usedByItemCount > 0
        ? `Esta adición está en ${g.usedByItemCount} producto(s). Al eliminarla se quitará de todos. `
        : "";
    if (
      !(await dialog.confirm({
        message: `${warn}¿Eliminar la adición "${g.name}"?`,
        tone: "danger",
        confirmText: "Eliminar",
      }))
    )
      return;
    try {
      await catalogApi.deleteModifierGroup(tenantId, g.id);
      flash(`Adición "${g.name}" eliminada.`);
      await onChanged();
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo eliminar la adición."));
    }
  }

  async function handleDuplicate(g: ModifierGroupDto) {
    try {
      await catalogApi.createModifierGroup(tenantId, {
        name: `${g.name} (copia)`,
        type: g.type,
        required: g.required,
        minSelect: g.minSelect,
        maxSelect: g.maxSelect ?? null,
        options: g.options.map((o) => ({
          name: o.name,
          priceDeltaCents: o.priceDeltaCents,
          available: o.available,
          isDefault: o.isDefault,
        })),
      });
      flash(`Adición "${g.name}" duplicada.`);
      await onChanged();
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo duplicar la adición."));
    }
  }

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <input
          className="input flex-1"
          placeholder="Buscar adición u opción… (ej: michelada, término, cebolla)"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <button onClick={() => setEditing("new")} className="btn-primary whitespace-nowrap text-sm">
          + Nueva adición
        </button>
      </div>

      {loading ? (
        <div className="flex justify-center py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : filtered.length === 0 ? (
        <div className="card p-8 text-center text-sm text-muted">
          {groups.length === 0
            ? "Aún no tienes adiciones. Crea una biblioteca reutilizable: términos de carne, extras con costo, “sin ingrediente”…"
            : "Ninguna adición coincide con tu búsqueda."}
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2">
          {filtered.map((g) => {
            const meta = TYPE_META[g.type];
            return (
              <div key={g.id} className="card p-4">
                <div className="mb-2 flex items-start justify-between gap-2">
                  <div>
                    <p className="font-display text-base font-normal text-ink">{g.name}</p>
                    <span
                      className={`mt-1 inline-block rounded-full px-2 py-0.5 text-[11px] font-medium tracking-[0.03em] ${meta.badge}`}
                    >
                      {meta.emoji} {meta.short}
                      {g.required ? " · obligatorio" : ""}
                    </span>
                  </div>
                  <span className="whitespace-nowrap text-[11px] text-muted">
                    {g.usedByItemCount > 0
                      ? `en ${g.usedByItemCount} producto${g.usedByItemCount > 1 ? "s" : ""}`
                      : "sin usar"}
                  </span>
                </div>
                <div className="mb-3 flex flex-wrap gap-1">
                  {g.options.slice(0, 6).map((o) => (
                    <span
                      key={o.id}
                      className={`rounded-md border px-1.5 py-0.5 text-[11px] ${
                        o.available
                          ? "border-hair/50 text-muted"
                          : "border-hair/30 text-muted/50 line-through"
                      }`}
                    >
                      {g.type === "REMOVE" ? `sin ${o.name}` : o.name}
                      {g.type !== "REMOVE" && o.priceDeltaCents > 0
                        ? ` +${formatEur(o.priceDeltaCents)}`
                        : ""}
                    </span>
                  ))}
                  {g.options.length > 6 && (
                    <span className="text-[11px] text-muted">
                      +{g.options.length - 6} más
                    </span>
                  )}
                </div>
                <div className="flex gap-3 text-xs">
                  <button
                    onClick={() => setEditing(g)}
                    className="font-medium text-brand-700 hover:underline"
                  >
                    Editar
                  </button>
                  <button
                    onClick={() => handleDuplicate(g)}
                    className="font-medium text-muted hover:underline"
                  >
                    Duplicar
                  </button>
                  <button
                    onClick={() => handleDelete(g)}
                    className="font-medium text-rose-600 hover:underline"
                  >
                    Eliminar
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {editing !== null && (
        <ModifierGroupEditorModal
          tenantId={tenantId}
          initial={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async (msg) => {
            setEditing(null);
            flash(msg);
            await onChanged();
          }}
        />
      )}
    </div>
  );
}

// ── Item modifiers field (attach groups + allow notes) — used inside the item modal ──

export function ItemModifiersField({
  groups,
  selectedIds,
  onToggle,
  allowNotes,
  onAllowNotesChange,
}: {
  groups: ModifierGroupDto[];
  selectedIds: string[];
  onToggle: (id: string) => void;
  allowNotes: boolean;
  onAllowNotesChange: (v: boolean) => void;
}) {
  const [query, setQuery] = useState("");
  const selected = groups.filter((g) => selectedIds.includes(g.id));
  const q = query.trim().toLowerCase();
  const matches = q
    ? groups.filter(
        (g) =>
          !selectedIds.includes(g.id) &&
          (g.name.toLowerCase().includes(q) ||
            g.options.some((o) => o.name.toLowerCase().includes(q)))
      )
    : groups.filter((g) => !selectedIds.includes(g.id));

  return (
    <div className="space-y-3 rounded-lg border border-hair/40 bg-paper2/60 p-3">
      <div>
        <label className="label">Adiciones y personalización</label>
        <p className="-mt-1 mb-2 text-xs text-muted">
          Adjunta grupos de tu biblioteca: términos, extras con costo, “sin ingrediente”…
        </p>

        {selected.length > 0 && (
          <div className="mb-2 flex flex-wrap gap-1.5">
            {selected.map((g) => {
              const meta = TYPE_META[g.type];
              return (
                <span
                  key={g.id}
                  className="inline-flex items-center gap-1 rounded-full bg-paper px-2 py-1 text-xs font-medium tracking-[0.03em] text-ink shadow-warm-sm ring-1 ring-hair/50"
                >
                  <span>{meta.emoji}</span>
                  {g.name}
                  <button
                    type="button"
                    onClick={() => onToggle(g.id)}
                    className="text-muted hover:text-rose-600"
                    aria-label={`Quitar ${g.name}`}
                  >
                    ✕
                  </button>
                </span>
              );
            })}
          </div>
        )}

        {groups.length === 0 ? (
          <p className="text-xs text-muted">
            No tienes adiciones todavía. Créalas en la pestaña “Adiciones”.
          </p>
        ) : (
          <>
            <input
              className="input py-1.5 text-sm"
              placeholder="Buscar adición para agregar…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
            {query && (
              <div className="mt-1 max-h-40 overflow-y-auto rounded-lg border border-hair/50 bg-paper">
                {matches.length === 0 ? (
                  <p className="px-3 py-2 text-xs text-muted">Sin coincidencias.</p>
                ) : (
                  matches.slice(0, 8).map((g) => {
                    const meta = TYPE_META[g.type];
                    return (
                      <button
                        type="button"
                        key={g.id}
                        onClick={() => {
                          onToggle(g.id);
                          setQuery("");
                        }}
                        className="flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-paper2"
                      >
                        <span>
                          {meta.emoji} {g.name}
                        </span>
                        <span className="text-[11px] text-muted">{meta.short}</span>
                      </button>
                    );
                  })
                )}
              </div>
            )}
          </>
        )}
      </div>

      <label className="flex items-center gap-2 text-sm text-ink">
        <input
          type="checkbox"
          checked={allowNotes}
          onChange={(e) => onAllowNotesChange(e.target.checked)}
        />
        Permitir comentario libre del cliente (ej: “sin sal, por favor”)
      </label>
    </div>
  );
}
