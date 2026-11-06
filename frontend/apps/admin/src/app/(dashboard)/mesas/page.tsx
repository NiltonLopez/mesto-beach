"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { FloorPlanFloor, FloorPlanTable, KdsStationDto, TableKind, VenueMode } from "@mestobeach/api-client";
import { kdsApi, sessionsApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { useDialog } from "@/components/Dialog";
import { MapTrifold, Info } from "@phosphor-icons/react";
import ScaledFloorMap from "@/components/ScaledFloorMap";

// Editor del plano canónico. Sobre el MISMO mapa a escala que ven Host/PDA/Runner/Caja,
// el admin organiza la sala: arrastra las mesas, añade más de los mismos tipos/dimensiones,
// ajusta forma/plazas y guarda. Las posiciones se guardan en lote; crear/editar/borrar es directo.
type Preset = { label: string; shape: "ROUND" | "SQUARE"; seats: number };
const MESA_PRESETS: Preset[] = [
  { label: "Redonda 2", shape: "ROUND", seats: 2 },
  { label: "Redonda 4", shape: "ROUND", seats: 4 },
  { label: "Redonda 6", shape: "ROUND", seats: 6 },
  { label: "Cuadrada 4", shape: "SQUARE", seats: 4 },
  { label: "Cuadrada 6", shape: "SQUARE", seats: 6 },
  { label: "Cuadrada 8", shape: "SQUARE", seats: 8 },
];
const HAMACA: Preset = { label: "Hamaca", shape: "SQUARE", seats: 2 };

function nextCode(prefix: string, tables: FloorPlanTable[]): string {
  const codes = new Set(tables.map((t) => t.tableCode));
  let n = 1;
  while (codes.has(prefix + String(n).padStart(2, "0"))) n++;
  return prefix + String(n).padStart(2, "0");
}

const MODE_LABEL: Record<VenueMode, string> = { RESTAURANT: "Restaurante", NIGHT: "Night" };
const KINDS: { value: TableKind; label: string }[] = [
  { value: "TABLE", label: "Mesa" },
  { value: "HIGH_TABLE", label: "Mesa alta" },
  { value: "RESERVADO", label: "Reservado" },
];

/** Borrador sin guardar de la distribución de un modo (RF-1938). */
interface Draft {
  tables: FloorPlanTable[];
  dirty: Set<string>;
}

export default function MesasPage() {
  const { staff } = useAuthStore();
  const dialog = useDialog();
  const tenantId = staff?.tenantId ?? "";

  const [floors, setFloors] = useState<FloorPlanFloor[]>([]);
  const [tables, setTables] = useState<FloorPlanTable[]>([]);
  // PRD-019: el editor trabaja sobre la distribución de UN modo, independiente del modo activo del local.
  const [mode, setMode] = useState<VenueMode | null>(null);
  const [activeMode, setActiveMode] = useState<VenueMode | null>(null);
  const [stations, setStations] = useState<KdsStationDto[]>([]);
  // Borradores por modo y aviso propio al cambiar de modo con cambios pendientes.
  const drafts = useRef<Partial<Record<VenueMode, Draft>>>({});
  const [pendingMode, setPendingMode] = useState<VenueMode | null>(null);
  const [activeFloorId, setActiveFloorId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dirty = useRef<Set<string>>(new Set());
  const [dirtyCount, setDirtyCount] = useState(0);
  const addCount = useRef(0);

  useEffect(() => {
    if (!tenantId) return;
    let alive = true;
    (async () => {
      try {
        const { data } = await sessionsApi.getFloorPlan({ includeHidden: true });
        if (!alive) return;
        setFloors(data.floors);
        setTables(data.tables);
        setMode(data.mode);
        setActiveMode(data.activeMode);
        setActiveFloorId((cur) => cur ?? [...data.floors].sort((a, b) => a.sortOrder - b.sortOrder)[0]?.id ?? null);
      } catch {
        if (alive) setError("No se pudo cargar el mapa.");
      } finally {
        if (alive) setLoading(false);
      }
    })();
    kdsApi
      .listStations()
      .then(({ data }) => { if (alive) setStations(data.data); })
      .catch(() => { /* sin estaciones el selector de barra queda vacío */ });
    return () => { alive = false; };
  }, [tenantId]);

  // Barras que se pueden asignar a una mesa de Night: perfil BAR, activas y encendidas en Night.
  const barOptions = useMemo(
    () => stations.filter((st) => st.flowProfile === "BAR" && st.isActive && st.activeIn.includes("NIGHT")),
    [stations],
  );

  /** Cambia la distribución que se edita; el borrador del modo que se deja se conserva. */
  async function goMode(next: VenueMode) {
    if (!mode || next === mode) return;
    setBusy(true);
    setError(null);
    try {
      if (dirty.current.size > 0) drafts.current[mode] = { tables, dirty: new Set(dirty.current) };
      else delete drafts.current[mode];
      const draft = drafts.current[next];
      const { data } = await sessionsApi.getFloorPlan({ mode: next, includeHidden: true });
      setFloors(data.floors);
      setActiveMode(data.activeMode);
      setTables(draft ? draft.tables : data.tables);
      dirty.current = draft ? new Set(draft.dirty) : new Set();
      setDirtyCount(dirty.current.size);
      setMode(next);
      setSelectedId(null);
    } catch {
      setError("No se pudo cargar la distribución de ese modo.");
    } finally {
      setBusy(false);
    }
  }

  function requestMode(next: VenueMode) {
    if (!mode || next === mode || busy) return;
    if (dirty.current.size > 0) setPendingMode(next);
    else void goMode(next);
  }

  const sortedFloors = useMemo(() => [...floors].sort((a, b) => a.sortOrder - b.sortOrder), [floors]);
  const activeFloor = sortedFloors.find((f) => f.id === activeFloorId) ?? sortedFloors[0] ?? null;
  const isBeach = !!activeFloor && /playa/i.test(activeFloor.name);
  const selected = tables.find((t) => t.id === selectedId) ?? null;

  // ── Arrastrar: actualiza la posición local y marca la mesa como sucia ────────
  function handleMove(id: string, posX: number, posY: number) {
    setTables((prev) => prev.map((t) => (t.id === id ? { ...t, posX, posY } : t)));
    dirty.current.add(id);
    setDirtyCount(dirty.current.size);
  }

  // «En servicio en este modo» y barra adscrita: forman parte de la distribución del modo (borrador).
  function patchLayout(id: string, patch: { active?: boolean; stationId?: string | null }) {
    setTables((prev) => prev.map((t) => (t.id === id ? { ...t, ...patch } : t)));
    dirty.current.add(id);
    setDirtyCount(dirty.current.size);
  }

  // Visible de noche (RF-1932): propiedad del piso, se guarda al instante.
  async function toggleFloorNight(f: FloorPlanFloor) {
    if (busy) return;
    const has = f.visibleIn.includes("NIGHT");
    const next: VenueMode[] = has ? f.visibleIn.filter((m) => m !== "NIGHT") : [...f.visibleIn, "NIGHT"];
    if (next.length === 0) {
      setError("Un piso debe existir al menos en un modo.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { data } = await sessionsApi.updateFloor(f.id, { visibleIn: next });
      setFloors((prev) => prev.map((x) => (x.id === f.id ? { ...x, visibleIn: data.visibleIn } : x)));
    } catch {
      setError("No se pudo actualizar la visibilidad del piso.");
    } finally {
      setBusy(false);
    }
  }

  // ── Añadir una mesa del tipo elegido (misma huella que las de su clase) ──────
  async function addTable(p: Preset) {
    if (!activeFloor || busy) return;
    setBusy(true);
    setError(null);
    const prefix = (activeFloor.name.trim()[0] || "M").toUpperCase();
    const code = nextCode(prefix, tables);
    const k = addCount.current++;
    const posX = Math.round(activeFloor.width / 2 + ((k % 4) - 1.5) * 90);
    const posY = Math.round(activeFloor.height / 2 + (Math.floor(k / 4) % 3) * 90);
    try {
      const { data } = await sessionsApi.createTable({
        tenantId,
        tableCode: code,
        floorId: activeFloor.id,
        zone: activeFloor.name,
        posX,
        posY,
        seats: p.seats,
        shape: p.shape,
        qrMode: "FIXED",
        requiresPin: !isBeach,
        ...(mode ? { mode } : {}),
        active: true,
      });
      const nt: FloorPlanTable = {
        id: data.id,
        tableCode: data.tableCode,
        shape: data.shape ?? p.shape,
        active: data.active ?? true,
        hasActiveSession: false,
        floorId: activeFloor.id,
        posX: data.posX ?? posX,
        posY: data.posY ?? posY,
        seats: data.seats ?? p.seats,
      };
      setTables((prev) => [...prev, nt]);
      setSelectedId(nt.id);
      // La mesa nace inactiva en el otro modo: su borrador (si lo hay) la recibe apagada; si no, se recarga.
      for (const m of ["RESTAURANT", "NIGHT"] as const) {
        const d = drafts.current[m];
        if (m === mode || !d) continue;
        if (d.dirty.size === 0) delete drafts.current[m];
        else d.tables = [...d.tables, { ...nt, active: false }];
      }
    } catch {
      setError("No se pudo crear la mesa.");
    } finally {
      setBusy(false);
    }
  }

  async function patchSelected(patch: { shape?: "ROUND" | "SQUARE"; seats?: number; kind?: TableKind }) {
    if (!selected || busy) return;
    setBusy(true);
    try {
      await sessionsApi.updateTable(selected.id, patch);
      setTables((prev) => prev.map((t) => (t.id === selected.id ? { ...t, ...patch } : t)));
    } catch {
      setError("No se pudo actualizar la mesa.");
    } finally {
      setBusy(false);
    }
  }

  async function removeSelected() {
    if (!selected || busy) return;
    if (
      !(await dialog.confirm({
        message: `Eliminar la mesa ${selected.tableCode}?`,
        tone: "danger",
        confirmText: "Eliminar",
      }))
    )
      return;
    setBusy(true);
    try {
      await sessionsApi.deleteTable(selected.id);
      setTables((prev) => prev.filter((t) => t.id !== selected.id));
      dirty.current.delete(selected.id);
      setDirtyCount(dirty.current.size);
      setSelectedId(null);
      for (const d of Object.values(drafts.current)) {
        if (!d) continue;
        d.tables = d.tables.filter((t) => t.id !== selected.id);
        d.dirty.delete(selected.id);
      }
    } catch {
      setError("No se pudo eliminar la mesa.");
    } finally {
      setBusy(false);
    }
  }

  async function save(): Promise<boolean> {
    if (dirty.current.size === 0 || busy) return true;
    setBusy(true);
    setError(null);
    try {
      const placements = tables
        .filter((t) => dirty.current.has(t.id) && t.posX != null && t.posY != null)
        .map((t) => ({
          tableId: t.id,
          floorId: t.floorId ?? null,
          posX: t.posX!,
          posY: t.posY!,
          // Se reenvía el «en servicio» del modo: si faltara, el backend lo pondría a true.
          active: t.active,
          // La barra solo cuenta en Night; null la quita.
          ...(mode === "NIGHT" ? { stationId: t.stationId ?? null } : {}),
        }));
      if (placements.length > 0 && mode) await sessionsApi.updateTablesLayout(mode, placements);
      dirty.current.clear();
      setDirtyCount(0);
      if (mode) delete drafts.current[mode];
      return true;
    } catch {
      setError("No se pudieron guardar las posiciones.");
      return false;
    } finally {
      setBusy(false);
    }
  }

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      {/* Encabezado */}
      <div className="flex items-center justify-between gap-4 border-b border-hair/40 bg-paper px-6 py-4">
        <div>
          <h1 className="font-display text-3xl text-ink">Mesas</h1>
          <p className="text-sm text-muted">Organiza la sala: arrastra las mesas, añade más y guarda.</p>
          {mode && (
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">Distribución</span>
              <div role="tablist" aria-label="Distribución de mesas por modo" className="inline-flex rounded-xl border border-hair/55 bg-paper2 p-1">
                {(["RESTAURANT", "NIGHT"] as const).map((m) => (
                  <button
                    key={m}
                    role="tab"
                    aria-selected={mode === m}
                    onClick={() => requestMode(m)}
                    disabled={busy}
                    className={`inline-flex min-h-[44px] items-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition disabled:opacity-60 ${
                      mode === m ? "bg-brand-500 text-white shadow-warm-sm" : "text-muted hover:text-ink"
                    }`}
                  >
                    {MODE_LABEL[m]}
                    {activeMode === m && (
                      <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${mode === m ? "bg-white/20 text-white" : "bg-oro/[0.14] text-brand-700"}`}>
                        activo ahora
                      </span>
                    )}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
        <button
          onClick={save}
          disabled={dirtyCount === 0 || busy}
          className="rounded-xl bg-brand-500 px-5 py-2.5 text-sm font-bold text-white shadow-warm-sm transition hover:bg-brand-600 disabled:opacity-40"
        >
          {dirtyCount > 0 ? `Guardar posiciones (${dirtyCount})` : "Guardado"}
        </button>
      </div>

      {mode && activeMode && mode !== activeMode && (
        // Franja a todo el ancho, como las barras de encima y de debajo: antes era una caja flotante
        // con margen solo arriba y quedaba pegada a las pestañas de piso, descentrada.
        <div role="status" className="flex items-center gap-2.5 border-b border-hair/40 bg-oro/[0.07] px-6 py-2.5 text-sm leading-snug text-muted">
          <Info size={18} weight="fill" className="flex-shrink-0 text-oro" aria-hidden />
          <p>
            Estás editando la distribución de <strong className="text-ink">{MODE_LABEL[mode]}</strong>; el local está en{" "}
            <strong className="text-ink">{MODE_LABEL[activeMode]}</strong>. Los cambios se verán al cambiar de modo.
          </p>
        </div>
      )}

      {error && <div className="mx-6 mt-3 rounded-lg bg-rose-500/10 px-4 py-2 text-sm text-rose-600">{error}</div>}

      {/* Pestañas de piso */}
      {sortedFloors.length > 0 && (
        <div className="flex items-center gap-2 overflow-x-auto border-b border-hair/40 bg-paper px-6 py-2">
          {sortedFloors.map((f) => (
            <button
              key={f.id}
              onClick={() => { setActiveFloorId(f.id); setSelectedId(null); }}
              className={`min-h-[44px] flex-shrink-0 rounded-full px-4 py-1.5 text-sm font-semibold transition ${
                f.id === activeFloor?.id ? "bg-brand-500 text-white shadow-warm-sm" : "bg-paper2 text-muted hover:text-ink"
              }`}
            >
              {f.name}
              {mode === "NIGHT" && !f.visibleIn.includes("NIGHT") && (
                <span className="ml-1.5 text-xs opacity-70">cerrado de noche</span>
              )}
            </button>
          ))}
          {activeFloor && (
            <button
              role="switch"
              aria-checked={activeFloor.visibleIn.includes("NIGHT")}
              onClick={() => void toggleFloorNight(activeFloor)}
              disabled={busy}
              className={`ml-auto min-h-[44px] flex-shrink-0 rounded-full border px-4 py-1.5 text-sm font-semibold transition disabled:opacity-60 ${
                activeFloor.visibleIn.includes("NIGHT") ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 bg-paper text-muted hover:bg-paper2"
              }`}
            >
              Visible de noche
            </button>
          )}
        </div>
      )}

      {/* Paleta: añadir mesas del mismo tipo/dimensiones */}
      {activeFloor && (
        <div className="flex flex-wrap items-center gap-2 border-b border-hair/40 bg-canvas px-6 py-2.5">
          <span className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">Añadir</span>
          {(isBeach ? [HAMACA] : MESA_PRESETS).map((p) => (
            <button
              key={p.label}
              onClick={() => addTable(p)}
              disabled={busy}
              className="min-h-[44px] rounded-lg border border-hair/55 bg-paper px-4 py-1.5 text-sm font-semibold text-ink transition hover:border-oro hover:bg-paper2 disabled:opacity-50"
            >
              + {p.label}
            </button>
          ))}
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {/* Mapa editable */}
        <div className="flex-1 overflow-auto bg-canvas p-6">
          {activeFloor ? (
            <ScaledFloorMap
              floor={activeFloor}
              tables={tables}
              selectedId={selectedId}
              onPick={(t) => setSelectedId(t.id)}
              onMove={handleMove}
            />
          ) : (
            <div className="flex h-full flex-col items-center justify-center gap-3 text-center text-muted">
              <MapTrifold size={40} weight="light" className="text-brand-500/70" />
              <p className="font-display text-lg text-ink">Aún no hay sectores</p>
              <p className="text-sm">El plano de la sala aparecerá aquí cuando esté configurado.</p>
            </div>
          )}
        </div>

        {/* Panel de edición */}
        <aside
          className={`overflow-y-auto bg-paper p-4 pb-[max(1rem,env(safe-area-inset-bottom))] ${
            selected
              ? "fixed inset-x-0 bottom-0 z-40 max-h-[85dvh] rounded-t-2xl border-t border-hair/40 shadow-warm-lg"
              : "hidden"
          } lg:static lg:z-auto lg:block lg:max-h-none lg:w-72 lg:flex-shrink-0 lg:rounded-none lg:border-l lg:border-t-0 lg:pb-4 lg:shadow-none`}
        >
          {selected ? (
            <TableEditor
              table={selected}
              isBeach={isBeach}
              busy={busy}
              mode={mode}
              bars={barOptions}
              onActive={(active) => patchLayout(selected.id, { active })}
              onStation={(stationId) => patchLayout(selected.id, { stationId })}
              onKind={(kind) => patchSelected({ kind })}
              onShape={(shape) => patchSelected({ shape })}
              onSeats={(seats) => patchSelected({ seats })}
              onDelete={removeSelected}
              onClose={() => setSelectedId(null)}
            />
          ) : (
            <div className="text-sm text-muted">
              <h3 className="font-display text-lg text-ink">Editar mesa</h3>
              <p className="mt-1 text-xs">Toca una mesa del plano para moverla o editarla, o añade una nueva desde la paleta.</p>
            </div>
          )}
        </aside>
      </div>

      {/* Aviso propio al cambiar de distribución con cambios pendientes */}
      {pendingMode && mode && (
        <div
          className="fixed inset-0 z-[60] flex items-center justify-center bg-ink/40 p-4"
          role="presentation"
          onMouseDown={(e) => { if (e.target === e.currentTarget) setPendingMode(null); }}
        >
          <div role="dialog" aria-modal="true" aria-labelledby="mesas-pending-title" className="mestobeach-dialog-anim w-full max-w-sm rounded-2xl border border-hair/40 bg-paper p-5 shadow-warm-lg">
            <h2 id="mesas-pending-title" className="font-display text-lg text-ink">Cambios sin guardar</h2>
            <p className="mt-1.5 text-sm leading-snug text-muted">
              Tienes {dirtyCount} mesa(s) modificada(s) en la distribución de {MODE_LABEL[mode]}. Puedes guardarlas,
              dejarlas como borrador para retomarlas o descartarlas.
            </p>
            <div className="mt-5 flex flex-col gap-2">
              <button
                className="min-h-[44px] rounded-xl bg-brand-500 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-600"
                onClick={async () => {
                  const next = pendingMode;
                  setPendingMode(null);
                  if (await save()) await goMode(next);
                }}
              >
                Guardar y cambiar a {MODE_LABEL[pendingMode]}
              </button>
              <button
                className="min-h-[44px] rounded-xl border border-hair/55 px-4 py-2.5 text-sm font-semibold text-ink transition hover:bg-paper2"
                onClick={() => { const next = pendingMode; setPendingMode(null); void goMode(next); }}
              >
                Conservar borrador y cambiar
              </button>
              <button
                className="min-h-[44px] rounded-xl border border-rose-400/40 bg-rose-500/10 px-4 py-2.5 text-sm font-semibold text-rose-600 transition hover:bg-rose-500/15"
                onClick={() => {
                  const next = pendingMode;
                  setPendingMode(null);
                  dirty.current.clear();
                  setDirtyCount(0);
                  delete drafts.current[mode];
                  void goMode(next);
                }}
              >
                Descartar y cambiar
              </button>
              <button
                className="min-h-[44px] rounded-xl px-4 py-2.5 text-sm font-semibold text-muted transition hover:bg-paper2"
                onClick={() => setPendingMode(null)}
              >
                Seguir editando
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function TableEditor({
  table, isBeach, busy, mode, bars, onActive, onStation, onKind, onShape, onSeats, onDelete, onClose,
}: {
  table: FloorPlanTable;
  isBeach: boolean;
  busy: boolean;
  mode: VenueMode | null;
  bars: KdsStationDto[];
  onActive: (active: boolean) => void;
  onStation: (stationId: string | null) => void;
  onKind: (kind: TableKind) => void;
  onShape: (s: "ROUND" | "SQUARE") => void;
  onSeats: (n: number) => void;
  onDelete: () => void;
  onClose: () => void;
}) {
  const occupied = table.hasActiveSession;
  const seats = table.seats ?? 2;
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="font-display text-lg text-ink">Editar mesa</h3>
        <button onClick={onClose} className="-mr-2 grid h-11 w-11 place-items-center text-muted hover:text-ink" aria-label="Cerrar">✕</button>
      </div>

      <div>
        <p className="font-display text-2xl text-ink">{table.tableCode}</p>
        <span className={`mt-1 inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium tracking-[0.03em] ${occupied ? "bg-oro/[0.14] text-brand-700" : "bg-paper2 text-muted"}`}>
          <span className={`h-1.5 w-1.5 rounded-full ${occupied ? "bg-brand-500" : "bg-hair"}`} />
          {occupied ? "Ocupada" : "Libre"}
        </span>
      </div>

      <div>
        <p className="mb-1 text-xs font-semibold uppercase tracking-[0.06em] text-muted">En servicio en este modo</p>
        <button
          role="switch"
          aria-checked={table.active}
          onClick={() => onActive(!table.active)}
          className={`min-h-[44px] w-full rounded-lg border px-3 py-2 text-sm font-semibold transition ${
            table.active ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 text-muted hover:bg-paper2"
          }`}
        >
          {table.active ? "En servicio" : "Fuera de servicio"}
          {mode ? ` · ${MODE_LABEL[mode]}` : ""}
        </button>
        {table.activeGlobal === false && (
          <p className="mt-1 text-[11px] text-muted">La mesa está apagada en general: no abre sesión en ningún modo.</p>
        )}
      </div>

      {mode === "NIGHT" && (
        <div>
          <label htmlFor="mesa-barra" className="mb-1 block text-xs font-semibold uppercase tracking-[0.06em] text-muted">Barra</label>
          <select
            id="mesa-barra"
            className="input min-h-[44px]"
            value={table.stationId ?? ""}
            onChange={(e) => onStation(e.target.value || null)}
          >
            <option value="">Sin barra adscrita</option>
            {bars.map((b) => (
              <option key={b.id} value={b.id}>{b.name}</option>
            ))}
          </select>
          <p className="mt-1 text-[11px] text-muted">
            {bars.length === 0
              ? "No hay estaciones de barra encendidas en Night. Créalas en Estaciones KDS."
              : "Los platos de barra o sin estación de esta mesa van a esta barra."}
          </p>
        </div>
      )}

      <div>
        <p className="mb-1 text-xs font-semibold uppercase tracking-[0.06em] text-muted">Tipo de mesa</p>
        <div className="grid grid-cols-3 gap-2">
          {KINDS.map((k) => (
            <button
              key={k.value}
              onClick={() => onKind(k.value)}
              disabled={busy}
              aria-pressed={(table.kind ?? "TABLE") === k.value}
              className={`min-h-[44px] rounded-lg border px-2 py-2 text-xs font-semibold transition ${
                (table.kind ?? "TABLE") === k.value ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 text-muted hover:bg-paper2"
              }`}
            >
              {k.label}
            </button>
          ))}
        </div>
      </div>

      {!isBeach && (
        <>
          <div>
            <p className="mb-1 text-xs font-semibold uppercase tracking-[0.06em] text-muted">Forma</p>
            <div className="grid grid-cols-2 gap-2">
              {(["ROUND", "SQUARE"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => onShape(s)}
                  disabled={busy}
                  className={`min-h-[44px] rounded-lg border px-3 py-2 text-sm font-semibold transition ${
                    table.shape === s ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/55 text-muted hover:bg-paper2"
                  }`}
                >
                  {s === "ROUND" ? "Redonda" : "Cuadrada"}
                </button>
              ))}
            </div>
          </div>

          <div>
            <p className="mb-1 text-xs font-semibold uppercase tracking-[0.06em] text-muted">Plazas</p>
            <div className="flex items-center overflow-hidden rounded-lg border border-hair/55">
              <button onClick={() => onSeats(Math.max(1, seats - 1))} disabled={busy} className="min-h-[44px] min-w-[44px] px-3 py-2 text-lg font-bold text-ink hover:bg-paper2">−</button>
              <span className="flex-1 text-center text-lg font-extrabold tabular-nums text-ink">{seats}</span>
              <button onClick={() => onSeats(Math.min(12, seats + 1))} disabled={busy} className="min-h-[44px] min-w-[44px] px-3 py-2 text-lg font-bold text-ink hover:bg-paper2">+</button>
            </div>
            <p className="mt-1 text-[11px] text-muted">El tamaño en el plano se ajusta solo a las plazas.</p>
          </div>
        </>
      )}

      {isBeach && (
        <p className="rounded-lg bg-paper2 px-3 py-2 text-xs text-muted">Las hamacas tienen una huella fija; arrástrala para colocarla.</p>
      )}

      <button
        onClick={onDelete}
        disabled={busy}
        className="min-h-[44px] w-full rounded-lg border border-rose-400/40 bg-rose-500/10 px-4 py-2.5 text-sm font-bold text-rose-600 transition hover:bg-rose-500/15 disabled:opacity-50"
      >
        Eliminar mesa
      </button>
    </div>
  );
}
