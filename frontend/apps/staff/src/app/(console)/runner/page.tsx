"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import {
  ordersApi,
  kdsApi,
  sessionsApi,
  identityApi,
  getStoredStaff,
  getToken,
  saveAuth,
} from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import type {
  DeliveryQueueItemDto,
  DeliveryTripDto,
  FloorPlanFloor,
  FloorPlanTable,
  KdsStationDto,
  StaffDto,
} from "@mestobeach/api-client";
import ScaledFloorMap from "@/components/ScaledFloorMap";
import AvisosPanel from "@/components/AvisosPanel";
import { ReservadoPill } from "@/components/ReservadoPill";
import { useVenue } from "@/lib/venue";
import { pitido } from "@/lib/avisos";
import { useAvisos } from "@/lib/useAvisos";
import { useDialog } from "@/components/Dialog";
import { FullscreenButton } from "@/components/FullscreenButton";

const REFRESH_MS = 6_000;

// El runner reclama de UNA estación (su adscripción). Admin no tiene estación: elige cuál mirar.
export default function RunnerPage() {
  const router = useRouter();
  const dialog = useDialog();
  const { staff, setAuth } = useAuthStore();
  const [ready, setReady] = useState(false);

  const [queue, setQueue] = useState<DeliveryQueueItemDto[]>([]);
  const [trip, setTrip] = useState<DeliveryTripDto | null>(null);
  const [floors, setFloors] = useState<FloorPlanFloor[]>([]);
  const [tables, setTables] = useState<FloorPlanTable[]>([]);
  const [stations, setStations] = useState<KdsStationDto[]>([]);

  // Estación desde la que se lee el pase: la del runner, o la elegida por Admin.
  const [pickedStation, setPickedStation] = useState<string | null>(null);
  // Selección en la cola (para armar bandeja) y marcados en el viaje (para cerrar).
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  const stationId = staff?.assignedStationId ?? pickedStation ?? null;
  // De noche el runner también atiende los avisos de mesa (PRD-019, US-1907).
  const { mode } = useVenue();
  const nocturno = mode === "NIGHT";
  const avisosHub = useAvisos(staff?.tenantId, nocturno && ready);
  const isAdmin = (staff?.roles ?? []).includes("Admin");

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
    if (!roles.includes("Runner") && !roles.includes("Admin")) {
      router.replace("/board");
      return;
    }
    setAuth(stored, token);
    setReady(true);
    // SDD-001 v2.1 §7: la estación se lee de la base; la copia del login puede estar vieja.
    void refreshProfileRef.current();
  }, [router, setAuth]);

  // Perfil fresco (GET /identity/auth/me lee user_memberships.assigned_station_id). Se llama al
  // cargar, ante un 403 WRONG_STATION y cuando se rechaza un POST /delivery-trips.
  const refreshProfile = useCallback(async () => {
    const token = getToken();
    if (!token) return;
    try {
      const { data } = await identityApi.getStaffProfile();
      saveAuth(token, data);
      setAuth(data, token);
    } catch {
      /* red caída: se reintenta en el siguiente rechazo */
    }
  }, [setAuth]);
  const refreshProfileRef = useRef(refreshProfile);
  refreshProfileRef.current = refreshProfile;

  // ── Cargas de apoyo (una vez) ──────────────────────────────────────────────
  useEffect(() => {
    if (!staff) return;
    kdsApi.listStations().then(({ data }) => setStations(data.data)).catch(() => {});
    sessionsApi
      .getFloorPlan()
      .then(({ data }) => {
        setFloors(data.floors);
        setTables(data.tables);
      })
      .catch(() => {});
  }, [staff]);

  // ── Fuente de verdad: mi viaje + la cola de mi estación ─────────────────────
  const load = useCallback(async () => {
    if (!staff) return;
    // Mi viaje manda: si llevo bandeja, no hay cola que armar.
    try {
      const { data } = await ordersApi.getMyDeliveryTrip();
      const t = data && typeof data === "object" && "tripId" in data ? (data as DeliveryTripDto) : null;
      setTrip(t);
      if (t) {
        setQueue([]);
        return;
      }
    } catch {
      /* red caída: se reintenta al siguiente ciclo */
    }
    // Sin viaje: leo la cola del pase. SDD-001 v2.1 §7: quien no es Admin no envía stationId
    // (la estación la pone el servidor desde la base); solo Admin elige cuál mirar.
    if (isAdmin && !stationId) {
      setQueue([]);
      return;
    }
    try {
      const { data } = await ordersApi.getDeliveryQueue(isAdmin ? (stationId ?? undefined) : undefined);
      setQueue(data.data);
    } catch (err: unknown) {
      // 403 WRONG_STATION: la estación cambió. Se limpia la cola vieja y se recarga el perfil.
      if (statusOf(err) === 403) {
        setQueue([]);
        setSelected(new Set());
        void refreshProfileRef.current();
      }
    }
  }, [staff, stationId, isAdmin]);

  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!staff) return;
    void loadRef.current();
    const t = setInterval(() => void loadRef.current(), REFRESH_MS);
    return () => clearInterval(t);
  }, [staff, stationId]);

  // Al cambiar de viaje, todos sus platos empiezan marcados (se entregan por defecto).
  useEffect(() => {
    if (!trip) {
      setChecked(new Set());
      return;
    }
    setChecked(new Set(trip.stops.flatMap((s) => s.items.map((i) => i.itemId))));
  }, [trip?.tripId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Acciones ────────────────────────────────────────────────────────────────
  async function armarBandeja() {
    if (selected.size === 0) return;
    setBusy(true);
    try {
      const { data } = await ordersApi.createDeliveryTrip([...selected]);
      setTrip(data);
      setSelected(new Set());
    } catch (err: unknown) {
      dialog.toast(detailOf(err, "No se pudo armar la bandeja. Puede que un plato ya no esté listo."), {
        tone: "error",
      });
      // La estación pudo cambiar en la base: refrescar la etiqueta antes de recargar la cola.
      void refreshProfileRef.current();
      void loadRef.current();
    } finally {
      setBusy(false);
    }
  }

  async function entregaFinalizada() {
    if (!trip) return;
    const all = trip.stops.flatMap((s) => s.items.map((i) => i.itemId));
    const returned = all.filter((id) => !checked.has(id));
    const msg =
      returned.length > 0
        ? `Cerrar la ruta: ${all.length - returned.length} entregados y ${returned.length} devueltos a su estación. ¿Confirmas?`
        : "Cerrar la ruta y dar todo por entregado. ¿Confirmas?";
    if (!(await dialog.confirm({ message: msg, tone: "danger" }))) return;
    setBusy(true);
    try {
      await ordersApi.completeDeliveryTrip(trip.tripId, returned);
      setTrip(null);
      void loadRef.current();
    } catch (err: unknown) {
      dialog.toast(detailOf(err, "No se pudo cerrar la ruta."), { tone: "error" });
    } finally {
      setBusy(false);
    }
  }

  async function liberarRuta() {
    if (!trip) return;
    if (
      !(await dialog.confirm({
        message: "Liberar la ruta devuelve todos sus platos al pase para otro runner. ¿Seguro?",
        tone: "danger",
      }))
    )
      return;
    setBusy(true);
    try {
      await ordersApi.releaseDeliveryTrip(trip.tripId);
      setTrip(null);
      void loadRef.current();
    } catch (err: unknown) {
      dialog.toast(detailOf(err, "No se pudo liberar la ruta."), { tone: "error" });
    } finally {
      setBusy(false);
    }
  }

  const stationName = useMemo(
    () => stations.find((s) => s.id === stationId)?.name ?? null,
    [stations, stationId]
  );

  // Piso de las paradas: se asume que todas comparten sector; usamos el de la primera.
  const stopFloor = useMemo(() => {
    const fid = trip?.stops[0]?.floorId ?? null;
    return fid ? floors.find((f) => f.id === fid) ?? null : null;
  }, [trip, floors]);

  // Pines numerados del mapa: id de mesa -> nº de parada (1-based), casando por tableCode.
  const stopLabels = useMemo(() => {
    const m = new Map<string, number>();
    if (!trip || !stopFloor) return m;
    const floorTables = tables.filter((t) => t.floorId === stopFloor.id);
    trip.stops.forEach((s, i) => {
      if (!s.tableCode) return;
      const t = floorTables.find((ft) => ft.tableCode === s.tableCode);
      if (t) m.set(t.id, i + 1);
    });
    return m;
  }, [trip, stopFloor, tables]);

  if (!ready || !staff) return null;

  return (
    <div className="flex min-h-full flex-col bg-canvas text-ink">
      <Header staff={staff} stationName={stationName} trip={trip} />

      {nocturno && (
        <AvisosPanel
          avisos={avisosHub.avisos}
          onAtender={(a) => void avisosHub.atender(a)}
          atendiendo={avisosHub.atendiendo}
          onVerPin={avisosHub.verPin}
          onEscalada={() => pitido("aviso")}
        />
      )}

      <div className="mx-auto w-full max-w-2xl flex-1 px-4 pb-4 pt-4">
        {/* Admin sin estación adscrita: elige de qué pase lee */}
        {isAdmin && !staff.assignedStationId && !trip && (
          <div className="mb-4">
            <label className="mb-1 block text-xs font-semibold uppercase tracking-[0.03em] text-muted">
              Pase de estación
            </label>
            <select
              value={pickedStation ?? ""}
              onChange={(e) => setPickedStation(e.target.value || null)}
              className="min-h-[44px] w-full rounded-lg border border-hair/55 bg-paper px-3 py-2 text-base text-ink"
            >
              <option value="">Elige una estación…</option>
              {stations.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </div>
        )}

        {trip ? (
          <TripView
            trip={trip}
            stopFloor={stopFloor}
            tables={tables}
            stopLabels={stopLabels}
            checked={checked}
            onToggle={(id) =>
              setChecked((prev) => {
                const next = new Set(prev);
                next.has(id) ? next.delete(id) : next.add(id);
                return next;
              })
            }
          />
        ) : (
          <QueueView
            queue={queue}
            selected={selected}
            needsStation={!stationId}
            onToggle={(id) =>
              setSelected((prev) => {
                const next = new Set(prev);
                next.has(id) ? next.delete(id) : next.add(id);
                return next;
              })
            }
          />
        )}
      </div>

      {/* Barra de acción pegada abajo de la columna de contenido (mano dominante, mobile-first) */}
      <ActionBar
        trip={trip}
        busy={busy}
        selectedCount={selected.size}
        returnedCount={
          trip ? trip.stops.flatMap((s) => s.items).filter((i) => !checked.has(i.itemId)).length : 0
        }
        onArmar={armarBandeja}
        onEntregar={entregaFinalizada}
        onLiberar={liberarRuta}
      />
    </div>
  );
}

// ─── Cabecera ────────────────────────────────────────────────────────────────
function Header({
  staff,
  stationName,
  trip,
}: {
  staff: StaffDto;
  stationName: string | null;
  trip: DeliveryTripDto | null;
}) {
  return (
    <header className="sticky top-0 z-10 border-b border-hair/50 bg-canvas/95 px-4 py-3 backdrop-blur">
      <div className="mx-auto flex max-w-2xl items-center justify-between gap-3">
        <div className="min-w-0">
          <h1 className="font-display text-xl text-ink">
            Runner{stationName ? ` — ${stationName}` : ""}
          </h1>
          <p className="truncate text-xs text-muted">{staff.name}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <span
            className={`shrink-0 rounded-full px-3 py-1 text-xs font-medium tracking-[0.03em] ${
              trip
                ? "bg-oro/[0.10] text-brand-700"
                : "border border-hair/50 bg-paper2 text-muted"
            }`}
          >
            {trip ? "Con bandeja" : "Libre"}
          </span>
          {/* El runner va con el móvil en la mano: sin barra lateral cabe una fila más de pase. */}
          <FullscreenButton />
        </div>
      </div>
    </header>
  );
}

// ─── Vista de cola (armar bandeja) ───────────────────────────────────────────
function QueueView({
  queue,
  selected,
  needsStation,
  onToggle,
}: {
  queue: DeliveryQueueItemDto[];
  selected: Set<string>;
  needsStation: boolean;
  onToggle: (id: string) => void;
}) {
  if (needsStation) {
    return <Empty>Elige una estación para ver su pase.</Empty>;
  }
  if (queue.length === 0) {
    return <Empty>No hay platos listos en el pase. Cuando la cocina dé por listo, aparecen aquí.</Empty>;
  }
  return (
    <div>
      <p className="mb-2 text-xs uppercase tracking-[0.03em] text-muted">
        En el pase · {queue.length} plato{queue.length === 1 ? "" : "s"}
      </p>
      <ul className="space-y-2">
        {queue.map((it) => {
          const sel = selected.has(it.itemId);
          return (
            <li key={it.itemId}>
              <button
                onClick={() => onToggle(it.itemId)}
                className={`flex w-full items-start gap-3 rounded-2xl border p-4 text-left transition ${
                  sel
                    ? "border-brand-500 bg-oro/[0.10]"
                    : "border-hair/50 bg-paper hover:border-hair"
                }`}
              >
                <span
                  className={`mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-md border-2 text-sm font-bold ${
                    sel ? "border-brand-500 bg-brand-500 text-white" : "border-hair text-transparent"
                  }`}
                  aria-hidden
                >
                  ✓
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <span className="font-display text-lg text-ink">
                      {it.quantity}× {it.name}
                    </span>
                    {(it.priority ?? 0) > 0 && <ReservadoPill />}
                    <span className="shrink-0 rounded-full bg-paper2 px-2 py-0.5 text-sm font-medium text-ink">
                      {it.tableCode ?? "—"}
                    </span>
                  </div>
                  {it.selectedModifiers && (
                    <p className="text-sm font-medium text-sky-700">{it.selectedModifiers}</p>
                  )}
                  {it.notes && <p className="text-sm italic text-amber-700">{it.notes}</p>}
                  {it.observacion && <p className="text-sm font-semibold text-brand-700">Obs: {it.observacion}</p>}
                  {it.customerName && <p className="text-xs text-muted">{it.customerName}</p>}
                </div>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// ─── Vista del viaje (manifiesto + mapa) ─────────────────────────────────────
function TripView({
  trip,
  stopFloor,
  tables,
  stopLabels,
  checked,
  onToggle,
}: {
  trip: DeliveryTripDto;
  stopFloor: FloorPlanFloor | null;
  tables: FloorPlanTable[];
  stopLabels: Map<string, number>;
  checked: Set<string>;
  onToggle: (id: string) => void;
}) {
  return (
    <div className="space-y-5">
      {/* Mapa-manifiesto */}
      <section>
        <p className="mb-2 text-xs uppercase tracking-[0.03em] text-muted">Ruta · {trip.stops.length} parada{trip.stops.length === 1 ? "" : "s"}</p>
        {stopFloor ? (
          <ScaledFloorMap floor={stopFloor} tables={tables} stopLabels={stopLabels} />
        ) : (
          <p className="text-sm text-muted">
            Aún no hay plano del sector. Pídele al administrador que diseñe el mapa.
          </p>
        )}
      </section>

      {/* Paradas */}
      <section className="space-y-3">
        {trip.stops.map((stop, i) => (
          <div key={`${stop.tableCode}-${i}`} className="rounded-2xl border border-hair/50 bg-paper p-4">
            <div className="mb-2 flex items-center gap-2">
              <span className="grid h-7 w-7 place-items-center rounded-full bg-brand-500 text-sm font-bold text-white">
                {i + 1}
              </span>
              <span className="font-display text-xl text-ink">{stop.tableCode ?? "Sin puesto"}</span>
              {stop.items.some((it) => (it.priority ?? 0) > 0) && <ReservadoPill />}
            </div>
            {groupByGuest(stop.items).map((group) => (
            <div key={group.name ?? "__sala"} className="mt-3 first:mt-0">
              {/* PRD-019: a quién se entrega; sin nombre = comanda del personal (SDD-004). */}
              <p className="mb-1.5 text-sm font-semibold text-ink">
                {group.name
                  ? `Pregunta por: ${group.name}`
                  : `Comanda de sala · ${stop.tableCode ?? "Sin puesto"}`}
              </p>
            <ul className="space-y-2">
              {group.items.map((item) => {
                const on = checked.has(item.itemId);
                return (
                  <li key={item.itemId}>
                    <button
                      onClick={() => onToggle(item.itemId)}
                      className={`flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left transition ${
                        on ? "bg-paper2" : "bg-rose-500/10 ring-1 ring-rose-500/40"
                      }`}
                    >
                      <span
                        className={`mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-md border-2 text-sm font-bold ${
                          on ? "border-brand-500 bg-brand-500 text-white" : "border-rose-400 text-transparent"
                        }`}
                        aria-hidden
                      >
                        ✓
                      </span>
                      <div className="min-w-0 flex-1">
                        <span className="font-display text-lg text-ink">
                          {item.quantity}× {item.name}
                        </span>
                        {item.selectedModifiers && (
                          <p className="text-sm font-medium text-sky-700">{item.selectedModifiers}</p>
                        )}
                        {item.notes && <p className="text-sm italic text-amber-700">{item.notes}</p>}
                        {item.observacion && <p className="text-sm font-semibold text-brand-700">Obs: {item.observacion}</p>}
                        {!on && <p className="text-xs font-medium text-rose-600">Se devuelve a su estación</p>}
                      </div>
                    </button>
                  </li>
                );
              })}
            </ul>
            </div>
            ))}
          </div>
        ))}
      </section>
    </div>
  );
}

/** Agrupa los platos de una parada por comensal (null = comanda de sala), en orden de aparición. */
function groupByGuest<T extends { customerName?: string | null }>(
  items: T[],
): { name: string | null; items: T[] }[] {
  const groups = new Map<string, { name: string | null; items: T[] }>();
  for (const it of items) {
    const name = it.customerName?.trim() || null;
    const key = name ?? "\u0000sala";
    const g = groups.get(key);
    if (g) g.items.push(it);
    else groups.set(key, { name, items: [it] });
  }
  return [...groups.values()];
}

// ─── Barra de acción fija ────────────────────────────────────────────────────
function ActionBar({
  trip,
  busy,
  selectedCount,
  returnedCount,
  onArmar,
  onEntregar,
  onLiberar,
}: {
  trip: DeliveryTripDto | null;
  busy: boolean;
  selectedCount: number;
  returnedCount: number;
  onArmar: () => void;
  onEntregar: () => void;
  onLiberar: () => void;
}) {
  return (
    <div className="sticky bottom-0 z-20 border-t border-hair/50 bg-canvas/95 p-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur">
      <div className="mx-auto flex max-w-2xl items-center gap-3">
        {trip ? (
          <>
            <button
              onClick={onLiberar}
              disabled={busy}
              className="min-h-[44px] shrink-0 rounded-xl border border-hair/55 px-4 py-3 text-sm font-medium text-ink transition hover:bg-paper2 disabled:opacity-50"
            >
              Liberar
            </button>
            <button
              onClick={onEntregar}
              disabled={busy}
              className="flex-1 rounded-xl bg-brand-500 px-4 py-3 text-base font-semibold text-white transition hover:bg-brand-600 disabled:opacity-50"
            >
              {busy
                ? "…"
                : returnedCount > 0
                ? `Entrega finalizada (${returnedCount} devuelto${returnedCount === 1 ? "" : "s"})`
                : "Entrega finalizada"}
            </button>
          </>
        ) : (
          <button
            onClick={onArmar}
            disabled={busy || selectedCount === 0}
            className="flex-1 rounded-xl bg-brand-500 px-4 py-3 text-base font-semibold text-white transition hover:bg-brand-600 disabled:opacity-40"
          >
            {selectedCount === 0 ? "Selecciona platos" : `Armar bandeja (${selectedCount})`}
          </button>
        )}
      </div>
    </div>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed border-hair/50 px-6 py-16 text-center text-sm text-muted">
      {children}
    </div>
  );
}

function statusOf(err: unknown): number | undefined {
  return (err as { response?: { status?: number } })?.response?.status;
}

function detailOf(err: unknown, fallback: string): string {
  return (
    (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback
  );
}
