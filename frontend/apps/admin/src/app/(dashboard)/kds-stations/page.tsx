"use client";

import { useEffect, useMemo, useState } from "react";
import type { AdminCategoryDto, KdsFlowProfile, KdsStationDto, VenueMode } from "@mestobeach/api-client";
import { kdsApi, catalogApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { useDialog } from "@/components/Dialog";
import { Monitor } from "@phosphor-icons/react";

function errMsg(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback;
}

type Editing = KdsStationDto | "new" | null;

const PROFILES: { value: KdsFlowProfile; label: string; hint: string }[] = [
  { value: "FULL_PASS", label: "Pase completo", hint: "Preparación, pase y entrega: cocina." },
  { value: "BAR", label: "Barra", hint: "Preparar y entregar: bebidas y servicio rápido." },
];
const MODES: { value: VenueMode; label: string }[] = [
  { value: "RESTAURANT", label: "Restaurante" },
  { value: "NIGHT", label: "Night" },
];
const modeLabel = (m: VenueMode) => (m === "NIGHT" ? "Night" : "Restaurante");

export default function KdsStationsPage() {
  const { staff } = useAuthStore();
  const dialog = useDialog();
  const tenantId = staff?.tenantId ?? process.env["NEXT_PUBLIC_TENANT_ID"] ?? "";
  const isAdmin = staff?.roles?.includes("Admin") ?? false;

  const [stations, setStations] = useState<KdsStationDto[]>([]);
  const [categories, setCategories] = useState<AdminCategoryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // editor
  const [editing, setEditing] = useState<Editing>(null);
  const [name, setName] = useState("");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [flowProfile, setFlowProfile] = useState<KdsFlowProfile>("FULL_PASS");
  const [isActive, setIsActive] = useState(true);
  const [activeIn, setActiveIn] = useState<VenueMode[]>(["RESTAURANT", "NIGHT"]);
  const [submitting, setSubmitting] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    setLoading(true);
    Promise.all([kdsApi.listStations(), catalogApi.getAdminCategories(tenantId)])
      .then(([st, cat]) => {
        setStations(st.data.data);
        setCategories(cat.data);
      })
      .catch(() => setError("No se pudieron cargar las estaciones o categorías."))
      .finally(() => setLoading(false));
  }, [tenantId]);

  const catName = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of categories) m.set(c.id, c.name);
    return m;
  }, [categories]);

  // Which station currently owns each category (first match wins on the backend) — used to warn about double routing.
  const ownerByCategory = useMemo(() => {
    const m = new Map<string, string>();
    for (const s of stations) for (const cid of s.categoryIds) if (!m.has(cid)) m.set(cid, s.id);
    return m;
  }, [stations]);

  function openNew() {
    setEditing("new");
    setName("");
    setPicked(new Set());
    setFlowProfile("FULL_PASS");
    setIsActive(true);
    setActiveIn(["RESTAURANT", "NIGHT"]);
    setEditError(null);
  }

  function openEdit(s: KdsStationDto) {
    setEditing(s);
    setName(s.name);
    setPicked(new Set(s.categoryIds));
    setFlowProfile(s.flowProfile);
    setIsActive(s.isActive);
    setActiveIn(s.activeIn);
    setEditError(null);
  }

  function toggle(id: string) {
    setPicked((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  function toggleMode(m: VenueMode) {
    setActiveIn((prev) => (prev.includes(m) ? prev.filter((x) => x !== m) : [...prev, m]));
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setEditError(null);
    const trimmed = name.trim();
    if (!trimmed) return setEditError("Ponle un nombre a la estación.");
    if (activeIn.length === 0) return setEditError("Elige al menos un modo en el que se encienda.");
    setSubmitting(true);
    const categoryIds = [...picked];
    try {
      if (editing === "new") {
        const { data } = await kdsApi.createStation({ name: trimmed, categoryIds, isActive, flowProfile, activeIn });
        setStations((prev) => [...prev, data]);
      } else if (editing) {
        const { data } = await kdsApi.updateStation(editing.id, { name: trimmed, categoryIds, isActive, flowProfile, activeIn });
        setStations((prev) =>
          prev.map((s) =>
            s.id === editing.id
              ? {
                  ...s,
                  name: data.name,
                  categoryIds: data.categoryIds,
                  isActive: data.isActive,
                  flowProfile: data.flowProfile,
                  activeIn: data.activeIn,
                  isOn: data.isOn,
                }
              : s,
          )
        );
      }
      setEditing(null);
    } catch (err: unknown) {
      setEditError(errMsg(err, "No se pudo guardar la estación."));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(s: KdsStationDto) {
    if (
      !(await dialog.confirm({
        message: `¿Eliminar la estación "${s.name}"? Los pedidos de sus categorías volverán al tablero general.`,
        tone: "danger",
        confirmText: "Eliminar",
      }))
    )
      return;
    try {
      await kdsApi.deleteStation(s.id);
      setStations((prev) => prev.filter((x) => x.id !== s.id));
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo eliminar la estación."));
    }
  }

  if (!isAdmin) {
    return (
      <div className="p-6">
        <h1 className="mb-2 font-display text-2xl font-normal text-ink">Estaciones KDS</h1>
        <div className="card p-6 text-sm text-muted">
          Solo un usuario con rol <strong>Admin</strong> puede configurar las estaciones KDS.
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="mb-2 flex items-center justify-between">
        <h1 className="font-display text-2xl font-normal text-ink">Estaciones KDS</h1>
        {!editing && (
          <button onClick={openNew} className="btn-primary text-sm">+ Nueva estación</button>
        )}
      </div>
      <p className="mb-5 max-w-2xl text-sm text-muted">
        Asigna <strong>categorías</strong> a cada estación: cuando un cliente pida un producto de esa categoría,
        el pedido llegará a este KDS (cocina, barra, parrilla…). Una categoría sin estación va al tablero general.
      </p>

      {error && (
        <div className="mb-4 rounded-lg border border-rose-600/30 bg-rose-500/10 px-4 py-2.5 text-sm text-rose-700">{error}</div>
      )}

      {/* Editor */}
      {editing && (
        <div className="card mb-5 p-5">
          <h2 className="mb-4 font-display text-lg font-normal text-ink">
            {editing === "new" ? "Nueva estación" : `Editar “${editing.name}”`}
          </h2>
          {editError && <p className="mb-3 text-sm text-rose-600">{editError}</p>}
          <form onSubmit={handleSave} className="space-y-4">
            <div>
              <label className="label">Nombre de la estación</label>
              <input
                className="input"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Ej: Cocina, Barra, Parrilla, Fríos"
                maxLength={100}
                autoFocus
                required
              />
            </div>

            <div>
              <label className="label">Perfil de flujo</label>
              <div className="grid gap-2 sm:grid-cols-2">
                {PROFILES.map((p) => (
                  <button
                    type="button"
                    key={p.value}
                    onClick={() => setFlowProfile(p.value)}
                    aria-pressed={flowProfile === p.value}
                    className={`min-h-[44px] rounded-lg border px-3 py-2 text-left transition ${
                      flowProfile === p.value ? "border-oro bg-oro/[0.10]" : "border-hair/55 hover:bg-paper2"
                    }`}
                  >
                    <span className="block text-sm font-semibold text-ink">{p.label}</span>
                    <span className="block text-xs text-muted">{p.hint}</span>
                  </button>
                ))}
              </div>
            </div>

            <div>
              <label className="label">Encendida y modos</label>
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  role="switch"
                  aria-checked={isActive}
                  onClick={() => setIsActive((v) => !v)}
                  className={`min-h-[44px] rounded-full border px-4 py-1.5 text-sm font-semibold transition ${
                    isActive ? "border-brand-500 bg-brand-500 text-white" : "border-hair/55 bg-paper text-muted"
                  }`}
                >
                  {isActive ? "Activa" : "Apagada"}
                </button>
                <span className="text-xs text-muted">Se enciende en:</span>
                {MODES.map((m) => {
                  const on = activeIn.includes(m.value);
                  return (
                    <button
                      type="button"
                      key={m.value}
                      onClick={() => toggleMode(m.value)}
                      aria-pressed={on}
                      className={`min-h-[44px] rounded-full border px-4 py-1.5 text-sm font-medium transition ${
                        on ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 bg-paper text-muted hover:bg-paper2"
                      }`}
                    >
                      {m.label}
                    </button>
                  );
                })}
              </div>
              <p className="mt-2 text-xs text-muted">
                Una estación apagada, o fuera de su modo, rechaza pedidos con aviso de estación cerrada.
              </p>
            </div>

            <div>
              <label className="label">Categorías que atiende</label>
              {categories.length === 0 ? (
                <p className="text-sm text-muted">
                  No tienes categorías en el catálogo todavía. Créalas en <strong>Catálogo</strong> y vuelve aquí.
                </p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {categories.map((c) => {
                    const on = picked.has(c.id);
                    const owner = ownerByCategory.get(c.id);
                    const takenByOther = !on && owner && (editing === "new" || owner !== editing.id);
                    return (
                      <button
                        type="button"
                        key={c.id}
                        onClick={() => toggle(c.id)}
                        title={takenByOther ? "Ya está asignada a otra estación; aquí no tendría efecto." : undefined}
                        className={`rounded-full border px-3 py-1.5 text-sm font-medium tracking-[0.03em] transition ${
                          on
                            ? "border-brand-500 bg-brand-500 text-white shadow-warm-sm"
                            : "border-hair/40 bg-paper text-ink hover:border-brand-500"
                        }`}
                      >
                        {on && <span className="mr-1">✓</span>}
                        {c.name}
                        <span className={`ml-1.5 text-xs ${on ? "text-white/70" : "text-muted"}`}>
                          {c.itemCount}
                        </span>
                        {takenByOther && <span className="ml-1.5 text-xs text-amber-600">●</span>}
                      </button>
                    );
                  })}
                </div>
              )}
              <p className="mt-2 text-xs text-muted">
                El número es la cantidad de productos en la categoría. El punto ámbar indica que ya está asignada a
                otra estación (el pedido va a la primera que la tenga).
              </p>
            </div>

            <div className="flex justify-end gap-2 pt-1">
              <button type="button" onClick={() => setEditing(null)} className="btn-secondary">Cancelar</button>
              <button type="submit" disabled={submitting} className="btn-primary">
                {submitting ? "Guardando…" : "Guardar estación"}
              </button>
            </div>
          </form>
        </div>
      )}

      {loading ? (
        <div className="flex justify-center py-10">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : stations.length === 0 ? (
        <div className="card flex flex-col items-center gap-3 py-14 text-center">
          <Monitor size={36} weight="light" className="text-oro/70" />
          <p className="text-sm text-muted">No hay estaciones KDS. Crea una y asígnale categorías.</p>
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2">
          {stations.map((s) => (
            <div key={s.id} className="card p-4">
              <div className="mb-3 flex items-start justify-between gap-2">
                <div>
                  <p className="flex items-center gap-2 font-display text-lg font-normal text-ink"><Monitor size={18} weight="light" className="text-oro" />{s.name}</p>
                  <p className="mt-0.5 text-xs text-muted">
                    {s.categoryIds.length === 0
                      ? "Sin categorías — no recibe pedidos"
                      : `${s.categoryIds.length} categoría(s)`}
                  </p>
                  <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs">
                    <span className="rounded-md border border-hair/50 bg-paper2 px-2 py-0.5 text-muted">
                      {s.flowProfile === "BAR" ? "Barra" : "Pase completo"}
                    </span>
                    <span
                      className={`rounded-md px-2 py-0.5 font-medium ${
                        s.isOn ? "bg-oro/[0.14] text-brand-700" : "bg-paper2 text-muted"
                      }`}
                    >
                      {s.isOn ? "Encendida ahora" : s.isActive ? "Apagada en este modo" : "Apagada"}
                    </span>
                    <span className="text-muted">{s.activeIn.map(modeLabel).join(" · ")}</span>
                  </p>
                </div>
                <div className="flex gap-3 text-xs">
                  <button onClick={() => openEdit(s)} className="font-medium text-brand-700 hover:underline">
                    Editar
                  </button>
                  <button onClick={() => handleDelete(s)} className="font-medium text-rose-600 hover:underline">
                    Eliminar
                  </button>
                </div>
              </div>
              {s.categoryIds.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {s.categoryIds.map((cid) => (
                    <span
                      key={cid}
                      className="rounded-md border border-hair/50 bg-paper2 px-2 py-0.5 text-xs text-muted"
                    >
                      {catName.get(cid) ?? "Categoría eliminada"}
                    </span>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
