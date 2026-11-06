"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { sessionsApi, getStoredStaff, getToken } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import type { FloorPlanFloor, FloorPlanTable } from "@mestobeach/api-client";
import ScaledFloorMap from "@/components/ScaledFloorMap";
import { useDialog } from "@/components/Dialog";
import { FullscreenButton } from "@/components/FullscreenButton";

const REFRESH_MS = 5_000;

export default function HostPage() {
  const router = useRouter();
  const dialog = useDialog();
  const { staff, setAuth } = useAuthStore();
  const [ready, setReady] = useState(false);

  const [floors, setFloors] = useState<FloorPlanFloor[]>([]);
  const [tables, setTables] = useState<FloorPlanTable[]>([]);
  const [activeFloorId, setActiveFloorId] = useState<string | null>(null);
  const [selected, setSelected] = useState<FloorPlanTable | null>(null);
  const [pax, setPax] = useState(2);
  const [pin, setPin] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // ── Auth + rol ───────────────────────────────────────────────────────────
  useEffect(() => {
    const stored = getStoredStaff();
    const token = getToken();
    if (!stored || !token) {
      router.replace("/login");
      return;
    }
    if (stored.mustChangePassword) {
      router.replace("/change-password");
      return;
    }
    const roles = stored.roles ?? [];
    if (!roles.includes("Host") && !roles.includes("Admin")) {
      router.replace("/board");
      return;
    }
    setAuth(stored, token);
    setReady(true);
  }, [router, setAuth]);

  // ── Fuente de verdad: el plano con ocupación, sondeado (RT-1803) ────────────
  const load = useCallback(async () => {
    try {
      const { data } = await sessionsApi.getFloorPlan();
      setFloors(data.floors);
      setTables(data.tables);
      setActiveFloorId((cur) => cur ?? [...data.floors].sort((a, b) => a.sortOrder - b.sortOrder)[0]?.id ?? null);
      // Mantener la tarjeta seleccionada al día con el sondeo.
      setSelected((cur) => (cur ? data.tables.find((t) => t.id === cur.id) ?? null : null));
    } catch {
      /* ignore */
    }
  }, []);

  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!staff) return;
    void loadRef.current();
    const t = setInterval(() => void loadRef.current(), REFRESH_MS);
    return () => clearInterval(t);
  }, [staff]);

  const sortedFloors = useMemo(
    () => [...floors].sort((a, b) => a.sortOrder - b.sortOrder),
    [floors]
  );
  const activeFloor = sortedFloors.find((f) => f.id === activeFloorId) ?? sortedFloors[0] ?? null;

  // ── Ocupación total y por sector (RF-1841) ──────────────────────────────────
  const occ = useMemo(() => {
    const perFloor = new Map<string, { occupied: number; total: number; pax: number }>();
    let totalOccupied = 0;
    let totalPax = 0;
    for (const t of tables) {
      if (!t.active || !t.floorId) continue;
      const f = perFloor.get(t.floorId) ?? { occupied: 0, total: 0, pax: 0 };
      f.total += 1;
      if (t.hasActiveSession) {
        f.occupied += 1;
        f.pax += t.guestCount ?? 0;
        totalOccupied += 1;
        totalPax += t.guestCount ?? 0;
      }
      perFloor.set(t.floorId, f);
    }
    return { perFloor, totalOccupied, totalPax };
  }, [tables]);

  // ── Acciones ────────────────────────────────────────────────────────────────
  async function sentar() {
    if (!staff || !selected) return;
    setBusy(true);
    try {
      const { data } = await sessionsApi.openSession({
        tenantId: staff.tenantId,
        tableCode: selected.tableCode,
        serviceMode: "STAFFED",
        guestCount: pax,
      });
      setPin(data.pin ?? null);
      await loadRef.current();
    } catch (err: unknown) {
      dialog.toast(detailOf(err, "No se pudo sentar la mesa."), { tone: "error" });
    } finally {
      setBusy(false);
    }
  }

  async function liberar() {
    if (!selected?.activeSessionId) return;
    if (
      !(await dialog.confirm({
        message: `Liberar ${selected.tableCode}: se cierra su sesión. ¿Confirmas?`,
        tone: "danger",
        confirmText: "Liberar",
      }))
    )
      return;
    setBusy(true);
    try {
      await sessionsApi.closeSession(selected.activeSessionId, "MANUAL_CLOSE");
      setSelected(null);
      setPin(null);
      await loadRef.current();
    } catch (err: unknown) {
      dialog.toast(detailOf(err, "No se pudo liberar la mesa."), { tone: "error" });
    } finally {
      setBusy(false);
    }
  }

  function pick(t: FloorPlanTable) {
    setSelected(t);
    setPin(null);
    setPax(t.seats && t.seats > 0 ? Math.min(t.seats, 2) : 2);
  }

  if (!ready || !staff) return null;

  return (
    <div className="flex h-full flex-col bg-canvas text-ink">
      {/* Ocupación total + por sector */}
      <header className="border-b border-hair/40 bg-paper px-4 py-3">
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <div>
            <h1 className="font-display text-lg font-normal text-ink">Recepción</h1>
            <p className="text-xs text-muted">Ocupación en vivo</p>
          </div>
          <Stat label="Comensales" value={occ.totalPax} accent="text-brand-700" />
          <Stat label="Mesas ocupadas" value={occ.totalOccupied} accent="text-oro" />
          <div className="ml-auto flex flex-wrap items-center gap-2">
            {sortedFloors.map((f) => {
              const s = occ.perFloor.get(f.id);
              return (
                <span
                  key={f.id}
                  className="rounded-lg border border-hair/30 bg-paper2 px-3 py-1.5 text-xs font-semibold text-muted"
                >
                  {f.name}:{" "}
                  <span className="text-brand-700">{s?.pax ?? 0}</span>
                  <span className="text-muted"> · {s?.occupied ?? 0}/{s?.total ?? 0}</span>
                </span>
              );
            })}
            {/* Recepción también es una pantalla de atril: el mapa de sala gana todo el ancho. */}
            <FullscreenButton />
          </div>
        </div>
      </header>

      {/* Pestañas de sector */}
      {sortedFloors.length > 0 && (
        <div className="flex flex-wrap gap-2 border-b border-hair/40 px-4 py-2">
          {sortedFloors.map((f) => (
            <button
              key={f.id}
              onClick={() => setActiveFloorId(f.id)}
              className={`rounded-full px-4 py-1.5 text-sm font-bold transition ${
                f.id === activeFloor?.id
                  ? "bg-brand-500 text-white shadow-warm-sm"
                  : "bg-paper2 text-muted hover:bg-paper"
              }`}
            >
              {f.name}
            </button>
          ))}
        </div>
      )}

      {/* Mapa */}
      <div className="flex-1 overflow-auto p-4">
        {activeFloor ? (
          <ScaledFloorMap
            floor={activeFloor}
            tables={tables}
            selectedId={selected?.id ?? null}
            onPick={pick}
            beach={/playa/i.test(activeFloor.name)}
          />
        ) : (
          <p className="pt-12 text-center text-sm text-muted">
            Aún no hay sectores. Pídele al administrador que diseñe el mapa.
          </p>
        )}
        <Legend />
      </div>

      {/* Panel de la mesa seleccionada */}
      {selected && (
        <TablePanel
          table={selected}
          pax={pax}
          setPax={setPax}
          pin={pin}
          busy={busy}
          onClose={() => {
            setSelected(null);
            setPin(null);
          }}
          onSeat={sentar}
          onFree={liberar}
        />
      )}
    </div>
  );
}

function Stat({ label, value, accent }: { label: string; value: number; accent: string }) {
  return (
    <div className="leading-none">
      <p className={`font-display text-2xl tabular-nums ${accent}`}>{value}</p>
      <p className="text-[11px] uppercase tracking-wide text-muted">{label}</p>
    </div>
  );
}

function Legend() {
  return (
    <div className="mt-3 flex gap-5 text-xs text-muted">
      <span className="flex items-center gap-1.5">
        <span className="inline-block h-3 w-3 rounded-full border" style={{ background: "#FFFFFF", borderColor: "rgba(170,132,83,0.7)" }} /> Libre
      </span>
      <span className="flex items-center gap-1.5">
        <span className="inline-block h-3 w-3 rounded-full border" style={{ background: "rgba(170,132,83,0.18)", borderColor: "rgba(170,132,83,0.88)" }} /> Ocupada
      </span>
    </div>
  );
}

function TablePanel({
  table,
  pax,
  setPax,
  pin,
  busy,
  onClose,
  onSeat,
  onFree,
}: {
  table: FloorPlanTable;
  pax: number;
  setPax: (n: number) => void;
  pin: string | null;
  busy: boolean;
  onClose: () => void;
  onSeat: () => void;
  onFree: () => void;
}) {
  const occupied = table.hasActiveSession;
  return (
    <div className="border-t border-hair/40 bg-paper px-4 py-4 shadow-warm">
      <div className="mx-auto flex max-w-3xl flex-wrap items-center gap-4">
        <div className="min-w-0">
          <p className="font-display text-xl text-ink">{table.tableCode}</p>
          <p className="text-xs text-muted">
            {occupied
              ? `Ocupada${table.guestCount != null ? ` · ${table.guestCount} comensales` : ""}`
              : "Libre"}
          </p>
        </div>

        {pin && (
          <div className="rounded-lg bg-oro/10 px-3 py-2 ring-1 ring-hair/30">
            <p className="text-[10px] uppercase tracking-wide text-oro">PIN de mesa</p>
            <p className="font-display text-xl tracking-[0.3em] text-brand-700">{pin}</p>
          </div>
        )}

        <div className="ml-auto flex items-center gap-3">
          {!occupied && (
            <div className="flex items-center gap-2">
              <span className="text-sm text-muted">Comensales</span>
              <div className="flex items-center overflow-hidden rounded-lg border border-hair/55">
                <button
                  onClick={() => setPax(Math.max(1, pax - 1))}
                  className="px-3 py-2 text-lg font-bold text-ink hover:bg-paper2"
                  aria-label="Menos"
                >
                  −
                </button>
                <span className="w-10 text-center text-lg font-extrabold tabular-nums text-ink">{pax}</span>
                <button
                  onClick={() => setPax(Math.min(999, pax + 1))}
                  className="px-3 py-2 text-lg font-bold text-ink hover:bg-paper2"
                  aria-label="Más"
                >
                  +
                </button>
              </div>
            </div>
          )}

          <button
            onClick={onClose}
            className="rounded-lg border border-hair/55 px-4 py-2.5 text-sm font-bold text-muted hover:bg-paper2"
          >
            Cerrar
          </button>

          {occupied ? (
            <button
              onClick={onFree}
              disabled={busy}
              className="rounded-lg bg-brand-600 px-5 py-2.5 text-sm font-extrabold text-white hover:bg-brand-500 disabled:opacity-50"
            >
              {busy ? "…" : "Liberar mesa"}
            </button>
          ) : (
            <button
              onClick={onSeat}
              disabled={busy}
              className="rounded-lg bg-brand-500 px-5 py-2.5 text-sm font-extrabold text-white hover:bg-brand-600 disabled:opacity-50"
            >
              {busy ? "…" : `Sentar ${pax}`}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

function detailOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback;
}
