"use client";

import { useEffect, useState, useCallback, useRef } from "react";
import { useRouter } from "next/navigation";
import * as signalR from "@microsoft/signalr";
import {
  ordersApi,
  kdsApi,
  sessionsApi,
  getToken,
  ORDERS_HUB_URL,
  getStoredStaff,
} from "@/lib/api";
import { useAuthStore, useKdsStore, type KdsOrder } from "@/lib/stores";
import { Warning } from "@phosphor-icons/react";
import KdsComandaCard from "@/components/KdsComandaCard";
import KdsFocusPanel from "@/components/KdsFocusPanel";
import { useDialog } from "@/components/Dialog";
import { useVenue } from "@/lib/venue";
import type { KdsFlowProfile } from "@mestobeach/api-client";
import { FullscreenButton } from "@/components/FullscreenButton";
import {
  columnOf,
  columnsFor,
  byPriorityThenAge,
  stationName as stationNameOf,
  KDS_ACTION_ERROR,
  KDS_ACTION_NEXT,
  densityFor,
  clockLabel,
  type KdsActionKind,
  type KdsColumnKey,
} from "@/lib/kds";

// ─── El viaje del plato, de izquierda a derecha (ADR-025 + QUEUED) ──────────────
// Cuatro columnas que se leen como se cocina: lo que espera orden, lo que ya está en
// cola, lo que está en fuegos y lo que descansa en el pase. El plato es la unidad de
// estado, no la comanda; la tarjeta agrupa por mesa para que salga acompasada.

// Authoritative poll cadence. The socket makes updates feel instant in between;
// this guarantees the board reflects the DB truth within one cycle even if every
// SignalR message is lost.
const REFRESH_INTERVAL = 10_000;

// If no authoritative load has succeeded in this long, the board may be stale and
// we warn the cook explicitly (P1): the poll is failing, not just the socket.
const STALE_THRESHOLD = 25_000;

// Cuánto dura el borde dorado de un pedido recién llegado (sin parpadeo, RF-195x).
const FRESH_MS = 3_000;

const stationKey = (tenantId: string) => `kds_station:${tenantId}`;

// ─── Helpers (module-level — stable, no captured component state) ─────────────

function playBeep(audioCtxRef: React.MutableRefObject<AudioContext | null>) {
  try {
    if (!audioCtxRef.current) {
      audioCtxRef.current = new AudioContext();
    }
    const ctx = audioCtxRef.current;
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.type = "sine";
    osc.frequency.value = 880;
    gain.gain.setValueAtTime(0.4, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.5);
    osc.start(ctx.currentTime);
    osc.stop(ctx.currentTime + 0.5);
  } catch {
    // AudioContext blocked or unavailable
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BoardPage() {
  const router = useRouter();
  const dialog = useDialog();
  const { staff, setAuth } = useAuthStore();
  const [authReady, setAuthReady] = useState(false);
  const {
    orders,
    stations,
    selectedStationId,
    soundEnabled,
    setAllOrders,
    reconcileOrders,
    patchItemStatus,
    setStations,
    setSelectedStation,
    setSoundEnabled,
  } = useKdsStore();

  const [connectionState, setConnectionState] = useState<string>("Desconectado");
  // Plato con una acción en vuelo, para bloquear su botón mientras responde.
  const [busyItemId, setBusyItemId] = useState<string | null>(null);
  // Comanda abierta en modo foco (el tablero de atrás se difumina).
  const [focusOrderId, setFocusOrderId] = useState<string | null>(null);
  // Sector (piso) por código de mesa: contexto de sala en la cabecera de cada tarjeta.
  const [sectorByTable, setSectorByTable] = useState<Map<string, string>>(new Map());
  // Authoritative-sync health (P1): timestamp of the last successful board load,
  // and a 1s ticker so the staleness banner + ticket timers update live.
  // Comandas recién llegadas (borde dorado 3 s). Solo tras la primera carga: el arranque no es "nuevo".
  const [freshIds, setFreshIds] = useState<Set<string>>(new Set());
  const freshTimersRef = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());
  const firstLoadRef = useRef(true);
  const stationRestoredRef = useRef(false);
  const { stations: venueStations } = useVenue();
  const [lastSyncAt, setLastSyncAt] = useState(0);
  const [nowTick, setNowTick] = useState(0);
  const isStale = lastSyncAt > 0 && nowTick - lastSyncAt > STALE_THRESHOLD;

  // Refs for stable closures inside the SignalR effect (avoid rebuilding on state changes)
  const soundEnabledRef = useRef(soundEnabled);
  soundEnabledRef.current = soundEnabled;

  const audioCtxRef = useRef<AudioContext | null>(null);
  const knownOrderIdsRef = useRef<Set<string>>(new Set());
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Delta-sync cursor (P2): serverTime of the last successful load. null → full snapshot.
  const cursorRef = useRef<string | null>(null);

  // ── Authoritative board load (the source of truth) ──────────────────────────
  // Delta sync: send the cursor and receive only changed orders + the authoritative
  // active-id set (snapshot backstop) to prune. `full` forces a clean snapshot — used
  // on mount and on every heal (reconnect/online/visible) so nothing can drift.

  const loadBoard = useCallback(
    async (full = false) => {
      if (!staff) return;
      const since = full ? undefined : cursorRef.current ?? undefined;
      const { data } = await ordersApi.listActiveOrders(since ? { since } : {});
      const changed = data.data as KdsOrder[];

      // Alerta de pedido nuevo: ids activos que no conocíamos (salvo en la primera carga).
      const arrived = data.activeIds.filter((id) => !knownOrderIdsRef.current.has(id));
      if (arrived.length > 0 && !firstLoadRef.current) {
        setFreshIds((cur) => new Set([...cur, ...arrived]));
        const timer = setTimeout(() => {
          freshTimersRef.current.delete(timer);
          setFreshIds((cur) => {
            const next = new Set(cur);
            arrived.forEach((id) => next.delete(id));
            return next;
          });
        }, FRESH_MS);
        freshTimersRef.current.add(timer);
      }
      firstLoadRef.current = false;

      // Beep when a CONFIRMED order we hadn't seen shows up (a new ticket).
      if (soundEnabledRef.current) {
        const hasNew = changed.some(
          (o) => o.status === "CONFIRMED" && !knownOrderIdsRef.current.has(o.id)
        );
        if (hasNew) playBeep(audioCtxRef);
      }

      if (since) {
        reconcileOrders(changed, data.activeIds); // upsert changes + prune to active set
      } else {
        setAllOrders(changed); // full snapshot
      }
      cursorRef.current = data.serverTime;
      data.activeIds.forEach((id) => knownOrderIdsRef.current.add(id));
      // Forget ids that left the board so a returning order beeps again.
      knownOrderIdsRef.current = new Set(
        [...knownOrderIdsRef.current].filter((id) => data.activeIds.includes(id))
      );
      setLastSyncAt(Date.now()); // mark a successful authoritative sync
    },
    [staff, setAllOrders, reconcileOrders]
  );

  const loadBoardRef = useRef(loadBoard);
  loadBoardRef.current = loadBoard;

  // ── Hydrate auth from localStorage ─────────────────────────────────────────

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
    // Role gate: the board (KDS) is for cocina/barra (KDS) or Admin. A Cajero-only user goes to the cashdesk.
    const roles = stored.roles ?? [];
    if (!roles.includes("KDS") && !roles.includes("Admin")) {
      router.replace("/cashdesk");
      return;
    }
    setAuth(stored, token);
    setAuthReady(true);
    // Restore sound preference
    try {
      const saved = localStorage.getItem("kds_sound");
      if (saved !== null) setSoundEnabled(saved !== "0");
    } catch {
      // ignore
    }
  }, [setAuth, setSoundEnabled, router]);

  // ── Load KDS stations ───────────────────────────────────────────────────────

  useEffect(() => {
    if (!staff) return;
    kdsApi.listStations().then(({ data }) => setStations(data.data)).catch(() => {});
  }, [staff, setStations]);

  // ── Estación elegida: la última del dispositivo, o la asignada al usuario ──────────

  useEffect(() => {
    if (!staff || stations.length === 0 || stationRestoredRef.current) return;
    stationRestoredRef.current = true;
    let saved: string | null = null;
    try {
      saved = localStorage.getItem(stationKey(staff.tenantId));
    } catch {
      /* almacenamiento bloqueado: se usa la asignada */
    }
    // "" guardado = «Todas las estaciones» elegido a propósito.
    const pick = saved !== null ? saved || null : staff.assignedStationId ?? null;
    setSelectedStation(pick && stations.some((s) => s.id === pick) ? pick : null);
  }, [staff, stations, setSelectedStation]);

  useEffect(
    () => () => {
      freshTimersRef.current.forEach(clearTimeout);
      freshTimersRef.current.clear();
    },
    []
  );

  function handleStationChange(value: string) {
    setSelectedStation(value || null);
    if (!staff) return;
    try {
      localStorage.setItem(stationKey(staff.tenantId), value);
    } catch {
      /* ignore */
    }
  }

  // ── Plano de sala: sector de cada mesa (carga única, contexto de la tarjeta) ──

  useEffect(() => {
    if (!staff) return;
    sessionsApi
      .getFloorPlan()
      .then(({ data }) => {
        const floorNames = new Map(data.floors.map((f) => [f.id, f.name]));
        const map = new Map<string, string>();
        for (const t of data.tables) {
          const name = t.floorId ? floorNames.get(t.floorId) : undefined;
          if (name) map.set(t.tableCode, name);
        }
        setSectorByTable(map);
      })
      .catch(() => {});
  }, [staff]);

  // ── Load the board on mount ─────────────────────────────────────────────────

  useEffect(() => {
    if (!staff) return;
    loadBoard().catch(() => {});
  }, [loadBoard, staff]);

  // ── SignalR: a low-latency HINT to refresh, never the source of truth ───────

  useEffect(() => {
    const token = getToken();
    if (!token || !staff) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${ORDERS_HUB_URL}?access_token=${token}`, {
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // Debounce: a burst of events collapses into one authoritative refetch.
    const scheduleRefresh = () => {
      if (refreshTimerRef.current) return;
      refreshTimerRef.current = setTimeout(() => {
        refreshTimerRef.current = null;
        void loadBoardRef.current().catch(() => {});
      }, 250);
    };

    connection.onreconnecting(() => setConnectionState("Reconectando…"));
    connection.onreconnected(() => {
      setConnectionState("Conectado");
      void loadBoardRef.current(true).catch(() => {}); // full heal of anything missed while down
    });
    connection.onclose(() => setConnectionState("Desconectado"));

    // Any order event → refetch the authoritative board (hint only, no payload trust).
    connection.on("OrderEvent", () => scheduleRefresh());

    // stop() solo tras resolver start() — evita abortar la negociación con el
    // doble-invoke de StrictMode (dev) ("stopped during negotiation").
    const started = connection
      .start()
      .then(async () => {
        await connection.invoke("JoinTenantGroup", staff.tenantId);
        setConnectionState("Conectado");
      })
      .catch(() => setConnectionState("Error al conectar"));

    connectionRef.current = connection;
    return () => {
      if (refreshTimerRef.current) { clearTimeout(refreshTimerRef.current); refreshTimerRef.current = null; }
      void started.finally(() => connection.stop().catch(() => {}));
    };
    // Keyed por tenantId: el hub se monta una vez (no en cada cambio de referencia de `staff`).
  }, [staff?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Authoritative safety net: poll + refetch on wake/online ─────────────────
  // Independiente de la salud del socket — garantiza consistencia en ≤1 ciclo,
  // y descubre mesas/pedidos nuevos aunque se haya perdido todo evento.

  useEffect(() => {
    if (!staff) return;
    const timer = setInterval(() => void loadBoardRef.current().catch(() => {}), REFRESH_INTERVAL);
    const onVisible = () => {
      if (document.visibilityState === "visible") void loadBoardRef.current(true).catch(() => {});
    };
    const onOnline = () => void loadBoardRef.current(true).catch(() => {});
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("online", onOnline);
    return () => {
      clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("online", onOnline);
    };
  }, [staff?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── 1s ticker: live ticket timers + staleness recompute ─────────────────────
  useEffect(() => {
    setNowTick(Date.now());
    const t = setInterval(() => setNowTick(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  // ── Actions ─────────────────────────────────────────────────────────────────

  // Las tres acciones del plato comparten forma: llamada, salto de estado optimista y
  // recarga autoritativa (dar por listo el último plato puede mover la comanda entera).
  async function handleAction(kind: KdsActionKind, itemId: string) {
    const parent = orders.find((o) => o.items.some((i) => i.id === itemId));
    setBusyItemId(itemId);
    try {
      if (kind === "MARCH") await ordersApi.marchItem(itemId);
      else if (kind === "START") await ordersApi.startItemPreparation(itemId);
      else await ordersApi.markItemReady(itemId);

      if (parent) patchItemStatus(parent.id, itemId, KDS_ACTION_NEXT[kind]);
      void loadBoardRef.current(true).catch(() => {});
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        KDS_ACTION_ERROR[kind];
      dialog.toast(detail, { tone: "error" });
    } finally {
      setBusyItemId(null);
    }
  }

  function handleSoundToggle() {
    const next = !soundEnabled;
    setSoundEnabled(next);
    try {
      localStorage.setItem("kds_sound", next ? "1" : "0");
    } catch {
      // ignore
    }
    // Initialize AudioContext on first user interaction (browser requirement)
    if (next) {
      try {
        if (!audioCtxRef.current) {
          audioCtxRef.current = new AudioContext();
        }
        playBeep(audioCtxRef);
      } catch {
        // ignore
      }
    }
  }

  // ── Display ─────────────────────────────────────────────────────────────────

  const lens = selectedStationId; // null = todas las estaciones (vista de conjunto)
  const now = nowTick || Date.now(); // el ticker aún no ha latido en el primer render

  // ¿El plato es de la estación desde la que miramos? En "todas", todo cuenta.
  const isOwnItem = (stationId: string | null | undefined) =>
    lens === null || (stationId ?? null) === lens;

  // Perfil de flujo y estado de cada estación: el modo vigente (venue) manda sobre la lista de cocina.
  const profileOf = (stationId: string | null | undefined): KdsFlowProfile =>
    (stationId &&
      (venueStations.find((s) => s.id === stationId)?.flowProfile ??
        stations.find((s) => s.id === stationId)?.flowProfile)) ||
    "FULL_PASS";
  const profile = profileOf(lens); // "Todas" = FULL_PASS
  const columns = columnsFor(profile);
  const lensOn = lens
    ? venueStations.find((s) => s.id === lens)?.isOn ?? stations.find((s) => s.id === lens)?.isOn ?? true
    : true;

  // Comandas que aportan al menos un plato propio a una columna: reservados primero, luego FIFO.
  const comandasFor = (key: KdsColumnKey) =>
    orders
      .filter((o) =>
        o.items.some((i) => isOwnItem(i.stationId) && columnOf(i.status, profile) === key)
      )
      .sort(byPriorityThenAge);

  // El contador de la columna cuenta PLATOS: es la unidad de trabajo de la cocina.
  const platosFor = (key: KdsColumnKey) =>
    orders.reduce(
      (n, o) =>
        n +
        o.items.filter((i) => isOwnItem(i.stationId) && columnOf(i.status, profile) === key).length,
      0
    );

  const platosTotales = columns.reduce((n, c) => n + platosFor(c.key), 0);

  // «Sin atender»: comandas con algún plato propio que aún no se ha tocado (QUEUED).
  const sinAtender = orders.filter((o) =>
    o.items.some((i) => isOwnItem(i.stationId) && i.status === "QUEUED")
  ).length;

  const sectorOf = (tableCode: string | null | undefined) =>
    (tableCode && sectorByTable.get(tableCode)) || null;

  const stationLabel = lens ? stationNameOf(lens, stations) : "Todas";

  const focusOrder = focusOrderId ? orders.find((o) => o.id === focusOrderId) ?? null : null;

  // ── Render ──────────────────────────────────────────────────────────────────

  if (!authReady) return null;

  return (
    <div className="relative h-full">
      <div
        className={`flex h-full flex-col bg-canvas text-ink ${
          focusOrder ? "pointer-events-none select-none blur-[4px]" : ""
        }`}
        aria-hidden={focusOrder ? true : undefined}
      >
        {/* Top bar */}
        <header className="flex items-center justify-between gap-4 border-b border-hair/40 bg-paper px-4 py-3">
          <div className="flex min-w-0 items-center gap-3">
            <h1 className="truncate font-display text-lg font-normal tracking-wide text-ink">
              Cocina — {stationLabel}
            </h1>
            <span
              className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium tracking-[0.03em] ${
                connectionState === "Conectado"
                  ? "bg-emerald-500/10 text-emerald-700"
                  : connectionState === "Reconectando…"
                  ? "bg-amber-500/10 text-amber-700"
                  : "bg-rose-500/10 text-rose-600"
              }`}
            >
              {connectionState}
            </span>
            {/* Contador de pedidos sin atender: número fijo, sin parpadeo. */}
            <span
              className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-semibold tabular-nums tracking-[0.03em] ${
                sinAtender > 0 ? "bg-oro/[0.16] text-brand-700" : "bg-paper2 text-muted"
              }`}
              aria-live="polite"
            >
              {sinAtender} sin atender
            </span>
          </div>

          <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">
            {/* Station selector (RF-302) */}
            {stations.length > 0 && (
              <select
                value={selectedStationId ?? ""}
                onChange={(e) => handleStationChange(e.target.value)}
                aria-label="Estación"
                className="min-h-[44px] rounded-lg border border-hair/55 bg-paper px-3 py-1.5 text-sm text-ink focus:border-oro focus:outline-none focus:ring-2 focus:ring-oro/20"
              >
                <option value="">Todas las estaciones</option>
                {stations.map((s) => {
                  const on = venueStations.find((v) => v.id === s.id)?.isOn ?? s.isOn;
                  return (
                    <option key={s.id} value={s.id}>
                      {s.name}
                      {on ? "" : " · cerrada"}
                    </option>
                  );
                })}
              </select>
            )}

            {/* Sound toggle (RF-305) */}
            <button
              onClick={handleSoundToggle}
              className={`min-h-[44px] rounded-lg border px-3 py-1.5 text-sm font-semibold transition ${
                soundEnabled
                  ? "border-hair/50 bg-oro/[0.12] text-brand-700 hover:bg-oro/[0.18]"
                  : "border-hair/40 bg-paper2 text-muted hover:bg-paper"
              }`}
              title={
                soundEnabled
                  ? "Sonido activado — clic para desactivar"
                  : "Sonido desactivado — clic para activar"
              }
            >
              {soundEnabled ? "Sonido ON" : "Sonido OFF"}
            </button>

            {/* Pantalla completa: la de cocina es una pantalla de pared, así que el tablero se
                queda con todo — se oculta la barra de la app y se pide el fullscreen del
                navegador. Se recuerda entre recargas. */}
            <FullscreenButton />
          </div>
        </header>

        {/* Sin-sincronizar banner (P1): the authoritative poll is failing → the board
            may be stale, so warn the cook explicitly to avoid missing tickets. */}
        {isStale && (
          <div className="flex items-center justify-center gap-2 border-y border-rose-400/50 bg-rose-500/10 px-4 py-2 text-sm font-bold text-rose-700">
            <Warning size={16} weight="fill" className="animate-pulse" aria-hidden />
            Sin sincronizar — el tablero puede estar desactualizado. Reintentando…
            <span className="tabular-nums opacity-80">
              (hace {Math.round((nowTick - lastSyncAt) / 1000)}s)
            </span>
          </div>
        )}

        {/* Estación apagada en este modo (ADR-037): no acepta pedidos nuevos, pero se ve lo que le quede */}
        {!lensOn && (
          <div
            role="status"
            className="flex items-center justify-center gap-2 border-b border-hair/40 bg-paper2 px-4 py-3 text-sm font-semibold text-muted"
          >
            Cerrada en este modo — no acepta pedidos nuevos
          </div>
        )}

        {/* Las columnas del viaje del plato: cuatro en FULL_PASS, dos en BAR */}
        {!lensOn && platosTotales === 0 ? (
          <div className="flex flex-1 items-center justify-center p-8 text-center">
            <p className="font-display text-xl text-muted">
              {stationLabel}: sin pedidos pendientes
            </p>
          </div>
        ) : (
        <div className="flex flex-1 overflow-hidden">
          {columns.map((col) => {
            const comandas = comandasFor(col.key);
            const platos = platosFor(col.key);
            // La tarjeta se aprieta según la carga de ESTA columna: con la cola llena, el
            // cocinero necesita verlo todo de un vistazo más que verlo cómodo.
            const density = densityFor(comandas.length);

            return (
              <section
                key={col.key}
                className="flex min-w-0 flex-1 flex-col border-r border-hair/40 last:border-r-0"
              >
                {/* Cabecera de columna */}
                <div className="flex items-center justify-between gap-3 border-b border-hair/40 bg-paper2/80 px-4 py-3">
                  <div className="flex min-w-0 flex-col">
                    <span className="font-display text-lg font-normal text-ink">{col.label}</span>
                    <span className="truncate text-xs text-muted">{col.hint}</span>
                  </div>
                  <span
                    className={`shrink-0 rounded-full px-2.5 py-0.5 text-sm font-bold ${col.pill}`}
                    title={`${platos} plato(s) en ${col.label.toLowerCase()}`}
                  >
                    {platos}
                  </span>
                </div>

                {/* Comandas de la columna */}
                <div className="flex-1 space-y-3 overflow-y-auto p-3">
                  {comandas.length === 0 ? (
                    <p className="pt-12 text-center text-sm text-muted">Sin platos</p>
                  ) : (
                    comandas.map((order) => (
                      <KdsComandaCard
                        key={order.id}
                        order={order}
                        lensStationId={lens}
                        column={col.key}
                        stations={stations}
                        sector={sectorOf(order.tableCode)}
                        now={now}
                        density={density}
                        profile={profile}
                        fresh={freshIds.has(order.id)}
                        busyItemId={busyItemId}
                        onAction={handleAction}
                        onOpen={setFocusOrderId}
                      />
                    ))
                  )}
                </div>

                {/* Aviso de cola: con la columna muy cargada puede que aún quede algo bajo el
                    pliegue. El cocinero no va a hacer scroll, así que al menos sabe QUE hay cola
                    y cuánto lleva esperando lo más antiguo — que es lo que decide su siguiente
                    movimiento. */}
                {comandas.length > 8 && (
                  <div className="flex items-center justify-between gap-2 border-t border-hair/40 bg-paper2 px-3 py-2 text-xs">
                    <span className="font-semibold text-muted">
                      {comandas.length} comandas en cola
                    </span>
                    {comandas[0] && (
                      <span className="font-bold tabular-nums text-brand-700">
                        la más antigua: {clockLabel(comandas[0].createdAt, now)}
                      </span>
                    )}
                  </div>
                )}
              </section>
            );
          })}
        </div>
        )}
      </div>

      {/* Modo foco: la comanda en primer plano, el tablero difuminado detrás */}
      {focusOrder && (
        <KdsFocusPanel
          order={focusOrder}
          lensStationId={lens}
          stations={stations}
          sector={sectorOf(focusOrder.tableCode)}
          now={now}
          profile={profile}
          profileOf={profileOf}
          busyItemId={busyItemId}
          onAction={handleAction}
          onClose={() => setFocusOrderId(null)}
        />
      )}
    </div>
  );
}
