"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { QRCodeSVG } from "qrcode.react";
import type {
  SessionByTableDto,
  SessionDetailDto,
  SessionJoinTokenDto,
  SessionPinDto,
  TableDto,
  WaiterCallDto,
} from "@mestobeach/api-client";
import { useRouter } from "next/navigation";
import {
  getStoredStaff,
  getToken,
  ordersApi,
  ORDERS_HUB_URL,
  sessionsApi,
  SESSIONS_HUB_URL,
} from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
// Mismos umbrales que el PDA: un aviso en rojo allí tiene que estar en rojo aquí.
import { desdeHace, nivelDe, segundosDesde } from "@/lib/avisos";
import { FullscreenButton } from "@/components/FullscreenButton";
import { Bell, Clock, ForkKnife, Key, Receipt } from "@phosphor-icons/react";

type ReadyAlert = {
  orderId: string;
  sessionId: string;
  tableCode: string;
  itemNames: string;
  observacion?: string | null;
  createdAt: string;
};

type TableCard = {
  table: TableDto;
  session: SessionByTableDto | null;
  detail: SessionDetailDto | null;
};

type SessionAccessState = {
  pin?: SessionPinDto | null;
  invite?: SessionJoinTokenDto | null;
  dynamicQr?: SessionJoinTokenDto | null;
};

type Filter = "all" | "busy" | "free";
type AccessTab = "PIN" | "INVITE" | "DYNAMIC_QR";

/** Motivo del aviso tal y como lo persiste Sesiones: "WAITER" | "PIN" | "BILL" | "ETA". */
type CallReason = NonNullable<WaiterCallDto["reason"]>;

/**
 * Cómo se pinta cada aviso en el salón.
 * "eta" = el comensal pregunta cuándo sale su plato: es una pregunta, no una urgencia.
 */
type CallKind = "pin" | "bill" | "eta" | "call";

/** Lo que reclama atención en una mesa del plano (badge de la tarjeta). */
type AttnKind = "ready" | "pin" | "bill" | "call" | "eta";

function callKind(reason: CallReason | undefined): CallKind {
  if (reason === "PIN") return "pin";
  // La cuenta tiene nombre propio desde que el aviso se persiste: antes nunca
  // llegaba a esta lista y caía en el saco de "te llaman", que manda al camarero
  // a la mesa sin saber que lo que quieren es pagar.
  if (reason === "BILL") return "bill";
  if (reason === "ETA") return "eta";
  return "call"; // WAITER y cualquier motivo futuro → llamada genérica
}

/** Iconos y textos de cada tipo de aviso; el tono del ETA es tranquilo a propósito. */
const CALL_ICON = { pin: Key, bill: Receipt, eta: Clock, call: Bell } as const;

const CALL_COPY: Record<CallKind, { titulo: string; detalle: string; accion: string }> = {
  pin: { titulo: "Pide el PIN", detalle: "Toca para dictar el PIN de la mesa", accion: "Ver PIN" },
  bill: { titulo: "Pide la cuenta", detalle: "Quiere pagar y marcharse", accion: "Voy" },
  eta: {
    titulo: "Pregunta por su plato",
    detalle: "¿Cuándo sale? — quiere saber cómo va su pedido",
    accion: "Le informo",
  },
  call: { titulo: "Te llaman", detalle: "Solicita atención del mesero", accion: "Voy" },
};

const ATTN_LABEL: Record<AttnKind, string> = {
  ready: "Listo",
  pin: "PIN",
  bill: "Cuenta",
  call: "Llaman",
  eta: "Pregunta",
};

const ATTN_COLOR: Record<AttnKind, string> = {
  ready: "var(--ready)",
  pin: "var(--pin)",
  bill: "var(--call)",
  call: "var(--call)",
  eta: "var(--eta)",
};

const EUR = new Intl.NumberFormat("es-ES", {
  style: "currency",
  currency: "EUR",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** `cents` llega en céntimos de euro: 1450 -> "14,50 €". */
function formatEur(cents: number): string {
  return EUR.format(cents / 100);
}

function bigLabel(code: string): string {
  return code.replace(/^mesa\s*/i, "").trim() || code;
}

function elapsed(iso?: string): string {
  if (!iso) return "—";
  const diff = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (diff < 1) return "recién";
  if (diff < 60) return `${diff}m`;
  const h = Math.floor(diff / 60);
  const m = diff % 60;
  return m ? `${h}h ${m}m` : `${h}h`;
}

export default function SalonPage() {
  const router = useRouter();
  const { staff, setAuth } = useAuthStore();

  const [tables, setTables] = useState<TableCard[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [readyAlerts, setReadyAlerts] = useState<ReadyAlert[]>([]);
  const [waiterCalls, setWaiterCalls] = useState<WaiterCallDto[]>([]);
  const [accessBySession, setAccessBySession] = useState<Record<string, SessionAccessState>>({});
  const [selectedTable, setSelectedTable] = useState<TableCard | null>(null);
  const [openingTableId, setOpeningTableId] = useState<string | null>(null);
  const [loadingCallId, setLoadingCallId] = useState<string | null>(null);
  const [loadingDeliverId, setLoadingDeliverId] = useState<string | null>(null);
  const [accessTab, setAccessTab] = useState<AccessTab>("PIN");
  const [filter, setFilter] = useState<Filter>("all");
  const [live, setLive] = useState(false);
  const [now, setNow] = useState(() => Date.now());

  // Cada aviso vuelve a sonar UNA vez al cruzar a urgente. Sin este registro, el
  // reloj de un segundo lo dispararía en cada tic mientras siga sin atenderse.
  const yaEscalados = useRef(new Set<string>());

  const sessionsHubRef = useRef<signalR.HubConnection | null>(null);
  const ordersHubRef = useRef<signalR.HubConnection | null>(null);

  // El reloj de las etiquetas "hace un rato". Con un llamado pendiente late cada
  // segundo: el primer minuto se cuenta en segundos y el aviso tiene que apretar
  // solo aunque no llegue nada nuevo. Sin llamados basta con medio minuto.
  useEffect(() => {
    const cadencia = waiterCalls.length > 0 ? 1000 : 30000;
    const t = setInterval(() => setNow(Date.now()), cadencia);
    return () => clearInterval(t);
  }, [waiterCalls.length]);

  // Si a los tres minutos nadie ha ido, la pantalla ya no basta: vuelve a sonar.
  useEffect(() => {
    for (const c of waiterCalls) {
      if (nivelDe(segundosDesde(c.createdAt, now)) !== "urgente") continue;
      if (yaEscalados.current.has(c.sessionId)) continue;
      yaEscalados.current.add(c.sessionId);
      beep("aviso");
    }
    // Atendido y olvidado: el próximo llamado de esa mesa vuelve a poder sonar.
    const vivos = new Set(waiterCalls.map((c) => c.sessionId));
    for (const id of yaEscalados.current) if (!vivos.has(id)) yaEscalados.current.delete(id);
  }, [waiterCalls, now]);

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
    // Role gate: el salón es la vista del Mesero (y Admin para supervisión).
    // Un Cajero/KDS sin rol Mesero va a su propia home.
    const roles = stored.roles ?? [];
    if (!roles.includes("Mesero") && !roles.includes("Admin")) {
      router.replace(roles.includes("Cajero") ? "/cashdesk" : "/board");
      return;
    }
    setAuth(stored, token);
  }, [router, setAuth]);

  useEffect(() => {
    if (!staff) return;
    void hydrateDashboard(staff.tenantId);
    // Keyed por tenantId (primitivo estable) — el shell y la página pueden
    // setear `staff` con distintas referencias; evita re-disparos espurios.
  }, [staff?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!staff) return;

    const token = getToken();
    if (!token) return;

    const sessionsHub = new signalR.HubConnectionBuilder()
      .withUrl(`${SESSIONS_HUB_URL}?access_token=${token}`, {
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    const ordersHub = new signalR.HubConnectionBuilder()
      .withUrl(`${ORDERS_HUB_URL}?access_token=${token}`, {
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    sessionsHub.on("SessionEvent", (payload: unknown) => {
      const event = payload as { type?: string; sessionId?: string; tableCode?: string };
      if (!event.type) return;

      // Avisos del comensal: llamada, petición de PIN y "¿cuándo sale?" (ETA).
      if (event.type === "WAITER_CALLED" || event.type === "PIN_REQUESTED" || event.type === "ETA_REQUESTED") {
        void hydrateCalls(staff.tenantId);
        // El ETA es una pregunta, no una urgencia: suena más grave y más bajo.
        beep(event.type === "ETA_REQUESTED" ? "suave" : "aviso");
      }
      if (event.type === "WAITER_CALL_ACKNOWLEDGED") {
        setWaiterCalls((current) => current.filter((call) => call.sessionId !== event.sessionId));
      }
      if (event.type === "SESSION_OPENED" || event.type === "PIN_REGENERATED" || event.type === "SESSION_CLOSED") {
        void hydrateTables(staff.tenantId);
        if (event.sessionId) void hydrateSessionAccess(event.sessionId);
      }
    });

    ordersHub.on("OrderEvent", (payload: unknown) => {
      const event = payload as { type?: string; orderId?: string; sessionId?: string; status?: string; tableCode?: string };
      if (!event.type) return;
      if (event.type === "order.status_changed" && event.status === "READY" && event.orderId && event.sessionId) {
        void addReadyAlert(event.orderId, event.sessionId, event.tableCode ?? "Mesa");
      }
      if (event.sessionId) void refreshSingleSession(event.sessionId);
    });

    // Guardamos las promesas de start para que el cleanup haga stop() SOLO cuando
    // la negociación ya terminó — así el doble-invoke de StrictMode (dev) no aborta
    // la negociación en curso ("connection was stopped during negotiation").
    const sStart = sessionsHub.start().then(() => { setLive(true); return sessionsHub.invoke("JoinWaiterGroup", staff.tenantId); }).catch(() => {});
    const oStart = ordersHub.start().then(() => ordersHub.invoke("JoinTenantGroup", staff.tenantId)).catch(() => {});

    sessionsHub.onreconnected(() => setLive(true));
    sessionsHub.onreconnecting(() => setLive(false));
    sessionsHub.onclose(() => setLive(false));

    sessionsHubRef.current = sessionsHub;
    ordersHubRef.current = ordersHub;

    return () => {
      setLive(false);
      void sStart.finally(() => sessionsHub.stop().catch(() => {}));
      void oStart.finally(() => ordersHub.stop().catch(() => {}));
    };
    // Keyed por tenantId para que el hub se monte UNA vez (no en cada cambio de
    // referencia de `staff`), evitando abortar la negociación de SignalR.
  }, [staff?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function hydrateDashboard(tenantId: string) {
    setLoading(true);
    setError(null);
    try {
      await Promise.all([hydrateTables(tenantId), hydrateCalls(tenantId)]);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo cargar el salón."));
    } finally {
      setLoading(false);
    }
  }

  async function hydrateTables(tenantId: string) {
    const { data } = await sessionsApi.listTables(tenantId, { active: true });
    const cards = await Promise.all(
      data.tables.map(async (table) => {
        if (!table.hasActiveSession || !table.activeSessionId) {
          return { table, session: null, detail: null } satisfies TableCard;
        }
        try {
          const sessionRes = await sessionsApi.getSessionByTable(tenantId, table.tableCode);
          const detailRes = await sessionsApi.getSession(sessionRes.data.sessionId);
          return { table, session: sessionRes.data, detail: detailRes.data } satisfies TableCard;
        } catch {
          return { table, session: null, detail: null } satisfies TableCard;
        }
      })
    );
    setTables(cards);
    setSelectedTable((current) => (current ? cards.find((c) => c.table.id === current.table.id) ?? null : null));
  }

  async function hydrateCalls(tenantId: string) {
    const { data } = await sessionsApi.listWaiterCalls(tenantId);
    setWaiterCalls(data);
  }

  async function refreshSingleSession(sessionId: string) {
    if (!staff) return;
    try {
      const sessionRes = await sessionsApi.getSession(sessionId);
      setTables((current) =>
        current.map((card) =>
          card.table.activeSessionId === sessionId
            ? {
                ...card,
                session: {
                  sessionId: sessionRes.data.sessionId,
                  tenantId: sessionRes.data.tenantId,
                  tableCode: sessionRes.data.tableCode,
                  status: sessionRes.data.status,
                  serviceMode: sessionRes.data.serviceMode,
                  pinRequired: sessionRes.data.pinRequired,
                  participantCount: sessionRes.data.participants.length,
                  createdAt: sessionRes.data.createdAt,
                },
                detail: sessionRes.data,
              }
            : card
        )
      );
    } catch {
      await hydrateTables(staff.tenantId);
    }
  }

  async function hydrateSessionAccess(sessionId: string) {
    const [pin, invite] = await Promise.allSettled([
      sessionsApi.getSessionPin(sessionId),
      sessionsApi.getJoinLink(sessionId, "INVITE"),
    ]);
    const dynamic = await sessionsApi.getJoinLink(sessionId, "DYNAMIC_QR").catch(() => null);
    setAccessBySession((current) => ({
      ...current,
      [sessionId]: {
        pin: pin.status === "fulfilled" ? pin.value.data : null,
        invite: invite.status === "fulfilled" ? invite.value.data : null,
        dynamicQr: dynamic?.data ?? null,
      },
    }));
  }

  async function addReadyAlert(orderId: string, sessionId: string, tableCode: string) {
    try {
      const { data } = await ordersApi.getOrder(orderId);
      const itemNames = data.items.map((item) => `${item.quantity}× ${item.itemNameSnapshot}`).join(", ");
      setReadyAlerts((current) => [
        { orderId, sessionId, tableCode, itemNames, observacion: data.observacion ?? null, createdAt: new Date().toISOString() },
        ...current.filter((a) => a.orderId !== orderId),
      ]);
    } catch {
      setReadyAlerts((current) => [
        { orderId, sessionId, tableCode, itemNames: "Pedido listo", createdAt: new Date().toISOString() },
        ...current.filter((a) => a.orderId !== orderId),
      ]);
    }
    beep();
  }

  function openDrawer(card: TableCard, tab: AccessTab = "PIN") {
    setSelectedTable(card);
    setAccessTab(tab);
    if (card.session?.sessionId) void hydrateSessionAccess(card.session.sessionId);
  }

  function openBySession(sessionId: string, tab: AccessTab = "PIN") {
    const card = tables.find((c) => c.session?.sessionId === sessionId || c.table.activeSessionId === sessionId);
    if (card) openDrawer(card, tab);
  }

  async function handleOpenTable() {
    if (!staff || !selectedTable) return;
    const table = selectedTable.table;
    setOpeningTableId(table.id);
    try {
      const { data } = await sessionsApi.openSession({
        tenantId: staff.tenantId,
        tableCode: table.tableCode,
        serviceMode: "STAFFED",
        // QR mode comes from the table (admin-set); no prompt here.
        ...(table.qrMode ? { qrMode: table.qrMode } : {}),
        ...(getToken() ? { waiterJwt: getToken()! } : {}),
      });
      await hydrateTables(staff.tenantId);
      await hydrateSessionAccess(data.sessionId);
      // re-point the drawer at the now-occupied table
      setSelectedTable({
        table: { ...table, hasActiveSession: true, activeSessionId: data.sessionId },
        session: {
          sessionId: data.sessionId,
          tenantId: data.tenantId,
          tableCode: data.tableCode,
          status: data.status,
          serviceMode: data.serviceMode,
          pinRequired: data.pinRequired,
          participantCount: 1,
          createdAt: data.createdAt,
        },
        detail: null,
      });
      // Dynamic-QR tables → jump to the live-QR tab so the waiter shows/prints it. Otherwise the PIN.
      setAccessTab(data.qrMode === "DYNAMIC" ? "DYNAMIC_QR" : "PIN");
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo abrir la mesa."));
    } finally {
      setOpeningTableId(null);
    }
  }

  async function handleDeliver(alert: ReadyAlert) {
    setLoadingDeliverId(alert.orderId);
    try {
      await ordersApi.updateOrderStatus(alert.orderId, "DELIVERED");
      setReadyAlerts((current) => current.filter((item) => item.orderId !== alert.orderId));
      if (staff) await hydrateTables(staff.tenantId);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo marcar como entregado."));
    } finally {
      setLoadingDeliverId(null);
    }
  }

  async function handleAcknowledgeCall(call: WaiterCallDto) {
    setLoadingCallId(call.sessionId);
    try {
      await sessionsApi.acknowledgeWaiterCall(call.sessionId);
      setWaiterCalls((current) => current.filter((item) => item.sessionId !== call.sessionId));
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo atender el llamado."));
    } finally {
      setLoadingCallId(null);
    }
  }


  // ── derived ────────────────────────────────────────────────
  const orderedTables = useMemo(
    () => [...tables].sort((a, b) => a.table.tableCode.localeCompare(b.table.tableCode, "es", { numeric: true })),
    [tables]
  );

  const visibleTables = useMemo(
    () =>
      orderedTables.filter((c) =>
        filter === "busy" ? c.table.hasActiveSession : filter === "free" ? !c.table.hasActiveSession : true
      ),
    [orderedTables, filter]
  );

  const busyCount = orderedTables.filter((c) => c.table.hasActiveSession).length;

  // sessionId → kinds of attention (for the floor badges)
  const attentionBySession = useMemo(() => {
    const m: Record<string, AttnKind> = {};
    for (const a of readyAlerts) m[a.sessionId] = "ready";
    for (const c of waiterCalls) {
      const kind = callKind(c.reason);
      // El ETA es el aviso de menor prioridad (igual que en el backend): no pisa
      // un "listo", un PIN ni un llamado que ya estén marcados en la mesa.
      if (kind === "eta" && m[c.sessionId]) continue;
      m[c.sessionId] = kind;
    }
    return m;
  }, [readyAlerts, waiterCalls]);

  // sessionId → platos aún en cocina, para responder al "¿cuándo sale?" sin abrir la mesa.
  const pendingBySession = useMemo(() => {
    const m: Record<string, number> = {};
    for (const card of tables) {
      const sid = card.session?.sessionId ?? card.table.activeSessionId;
      if (sid) m[sid] = card.detail?.ordersSummary.pendingItems ?? 0;
    }
    return m;
  }, [tables]);

  const attentionCount = readyAlerts.length + waiterCalls.length;

  void now; // tie elapsed labels to the 30s tick

  if (loading) {
    return (
      <div className="loadwrap">
        <div className="pulse">Preparando el salón…</div>
      </div>
    );
  }

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="mark">Mesto Beach · Salón</span>
          <span className="who serif">
            {staff?.name?.split(" ")[0] ?? "Mesero"} <small>· {busyCount}/{orderedTables.length} mesas activas</small>
          </span>
        </div>
        <span className={`live ${live ? "" : "off"}`}>
          <span className="dot" /> {live ? "En vivo" : "Reconectando"}
        </span>
        {/* Sin Tailwind: la pastilla la viste `salon.css` (`.fsbtn`), a juego con `.live`. */}
        <FullscreenButton unstyled className="fsbtn" />
      </header>

      {error && (
        <div className="banner" onClick={() => setError(null)}>
          {error}
        </div>
      )}

      {/* ── Necesitan atención ahora ── */}
      <div className="sec-head">
        <h2>Necesitan atención</h2>
        {attentionCount > 0 ? <span className="pip">{attentionCount}</span> : <span className="count">todo en calma</span>}
      </div>

      {attentionCount === 0 ? (
        <div className="calm"><span>✦</span> Sin pendientes. Buen servicio.</div>
      ) : (
        <div className="now-grid">
          {readyAlerts.map((a, i) => (
            <div className="now ready glow" key={`r-${a.orderId}`} style={{ animationDelay: `${i * 50}ms` }}>
              <div className="ring"><ForkKnife size={20} /></div>
              <div className="body">
                <div className="t">
                  <span className="table serif">{bigLabel(a.tableCode)}</span>
                  <span className="kind">Listo para llevar</span>
                </div>
                <p className="desc">{a.itemNames}</p>
                {a.observacion && <p className="desc" style={{ fontStyle: "italic" }}>Obs: {a.observacion}</p>}
              </div>
              <button className="act" disabled={loadingDeliverId === a.orderId} onClick={() => void handleDeliver(a)}>
                {loadingDeliverId === a.orderId ? "…" : "Entregar"}
              </button>
            </div>
          ))}

          {waiterCalls.map((c, i) => {
            const kind = callKind(c.reason);
            const copy = CALL_COPY[kind];
            const Icono = CALL_ICON[kind];
            const espera = segundosDesde(c.createdAt, now);
            const nivel = nivelDe(espera);
            const pendientes = pendingBySession[c.sessionId] ?? 0;
            // `pendingItems` cuenta los platos de comandas aún en PENDING (sin confirmar) — es el
            // mismo número que el "N pend." de la tarjeta de mesa, NO lo que hay en fuegos. Decir
            // "en cocina" sería falso justo al revés: si están pendientes, aún no han bajado.
            const detalle =
              kind === "eta" && pendientes > 0
                ? `¿Cuándo sale? — ${pendientes} ${pendientes === 1 ? "plato pendiente" : "platos pendientes"}`
                : copy.detalle;
            return (
              <div
                className={`now ${kind} glow`}
                data-nivel={nivel}
                key={`c-${c.sessionId}`}
                style={{ animationDelay: `${(readyAlerts.length + i) * 50}ms` }}
              >
                <div className="ring"><Icono size={20} /></div>
                <div className="body">
                  <div className="t">
                    <span className="table serif">{bigLabel(c.tableCode)}</span>
                    <span className="kind">{copy.titulo}</span>
                    {/* Cuánto lleva esperando: es lo que convierte el aviso en prisa. */}
                    <span className="espera"><i className="punto" aria-hidden />{desdeHace(espera)}</span>
                  </div>
                  <p className="desc">{detalle}</p>
                </div>
                {kind === "pin" ? (
                  <button className="act" onClick={() => openBySession(c.sessionId, "PIN")}>{copy.accion}</button>
                ) : (
                  <button className="act" disabled={loadingCallId === c.sessionId} onClick={() => void handleAcknowledgeCall(c)}>
                    {loadingCallId === c.sessionId ? "…" : copy.accion}
                  </button>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* ── Salón ── */}
      <div className="sec-head">
        <h2>Salón</h2>
        <span className="count">{visibleTables.length} mesas</span>
      </div>

      <div className="chips">
        {([["all", "Todas"], ["busy", "Ocupadas"], ["free", "Libres"]] as [Filter, string][]).map(([f, label]) => (
          <button key={f} className="chip" data-on={filter === f} onClick={() => setFilter(f)}>{label}</button>
        ))}
      </div>

      {visibleTables.length === 0 ? (
        <div className="empty"><div className="big">◆</div>No hay mesas en esta vista.</div>
      ) : (
        <div className="floor">
          {visibleTables.map((card, i) => {
            const busy = !!card.table.hasActiveSession;
            const sid = card.session?.sessionId ?? card.table.activeSessionId ?? "";
            const attn = attentionBySession[sid];
            const pending = card.detail?.ordersSummary.pendingItems ?? 0;
            const total = card.detail?.ordersSummary.totalCents ?? 0;
            return (
              <button
                key={card.table.id}
                onClick={() => openDrawer(card)}
                className={`tcard ${busy ? "occupied" : ""} ${attn ? "attn" : ""}`}
                style={{
                  animationDelay: `${Math.min(i * 35, 300)}ms`,
                  ...(attn ? ({ "--accent2": ATTN_COLOR[attn] } as React.CSSProperties) : {}),
                }}
              >
                <div className="top">
                  <div className="id">
                    <div className="no serif">{bigLabel(card.table.tableCode)}</div>
                    <div className="zone">{card.table.zone ?? "Sin zona"}</div>
                  </div>
                  {attn ? (
                    <span className={`state ${attn}`}>
                      <span className="d" /> {ATTN_LABEL[attn]}
                    </span>
                  ) : (
                    <span className={`state ${busy ? "busy" : "free"}`}>
                      <span className="d" /> {busy ? "Ocupada" : "Libre"}
                    </span>
                  )}
                </div>

                {busy ? (
                  <div className="stats">
                    <span className="meta num">
                      {elapsed(card.session?.createdAt)}
                      {pending > 0 && <> · <b>{pending} pend.</b></>}
                    </span>
                    <span className="bill num">{formatEur(total)}</span>
                  </div>
                ) : (
                  <div className="openhint">＋ Abrir mesa</div>
                )}
              </button>
            );
          })}
        </div>
      )}

      {/* ── Drawer ── */}
      {selectedTable && (
        <TableDrawer
          card={selectedTable}
          access={selectedTable.session ? accessBySession[selectedTable.session.sessionId] : undefined}
          accessTab={accessTab}
          setAccessTab={setAccessTab}
          opening={openingTableId === selectedTable.table.id}
          onOpenTable={handleOpenTable}
          onRefresh={() => selectedTable.session && hydrateSessionAccess(selectedTable.session.sessionId)}
          onClose={() => setSelectedTable(null)}
        />
      )}
    </div>
  );
}

/* ============================================================ */

function TableDrawer({
  card,
  access,
  accessTab,
  setAccessTab,
  opening,
  onOpenTable,
  onRefresh,
  onClose,
}: {
  card: TableCard;
  access: SessionAccessState | undefined;
  accessTab: AccessTab;
  setAccessTab: (t: AccessTab) => void;
  opening: boolean;
  onOpenTable: () => void;
  onRefresh: () => void;
  onClose: () => void;
}) {
  const busy = !!card.session;
  const s = card.detail?.ordersSummary;
  const hasDynamic = !!access?.dynamicQr;

  return (
    <>
      <div className="scrim" onClick={onClose} />
      <div className="drawer" role="dialog" aria-modal="true">
        <div className="grab" />
        <div className="dhead">
          <div>
            <div className="dno serif">{bigLabel(card.table.tableCode)}</div>
            <div className="dsub">
              {card.table.zone ?? "Sin zona"}
              {busy && <> · abierta hace {elapsed(card.session?.createdAt)}</>}
            </div>
          </div>
          <button className="iconbtn" style={{ marginLeft: "auto" }} onClick={onClose} aria-label="Cerrar">✕</button>
        </div>

        {!busy ? (
          <>
            <p className="dsub" style={{ marginTop: -8, marginBottom: 16 }}>
              Mesa libre. Ábrela para empezar a atenderla — el tipo de QR (fijo, dinámico o sin seguridad) ya lo configuró el administrador en cada mesa.
            </p>
            <button className="btn" disabled={opening} onClick={onOpenTable}>
              {opening ? "Abriendo…" : "Abrir mesa"}
            </button>
          </>
        ) : (
          <>
            <div className="metrics">
              <div className="metric">
                <div className="k">Comensales</div>
                <div className="v num">{card.session?.participantCount ?? card.detail?.participants.length ?? 0}</div>
              </div>
              <div className="metric">
                <div className="k">Pendientes</div>
                <div className="v pend num">{s?.pendingItems ?? 0}</div>
              </div>
              <div className="metric">
                <div className="k">Cuenta</div>
                <div className="v money num">{formatEur(s?.totalCents ?? 0)}</div>
              </div>
            </div>

            <div className="access">
              <div className="tabs">
                <button className="tab" data-on={accessTab === "PIN"} onClick={() => setAccessTab("PIN")}>PIN</button>
                <button className="tab" data-on={accessTab === "INVITE"} onClick={() => setAccessTab("INVITE")}>Invitar</button>
                {hasDynamic && (
                  <button className="tab" data-on={accessTab === "DYNAMIC_QR"} onClick={() => setAccessTab("DYNAMIC_QR")}>QR vivo</button>
                )}
              </div>
              <AccessBody access={access} tab={accessTab} />
            </div>

            <button className="btn ghost row" onClick={onRefresh}>↻ Refrescar acceso</button>
          </>
        )}
      </div>
    </>
  );
}

function AccessBody({ access, tab }: { access: SessionAccessState | undefined; tab: AccessTab }) {
  if (!access) return <p className="dsub" style={{ textAlign: "center", padding: "20px 0" }}>Cargando acceso…</p>;

  if (tab === "PIN") {
    return access.pin ? (
      <div className="pin-show">
        <span className="lbl">PIN de la mesa</span>
        <div className="digits num">{access.pin.pin}</div>
      </div>
    ) : (
      <p className="dsub" style={{ textAlign: "center", padding: "20px 0" }}>Esta mesa no requiere PIN.</p>
    );
  }

  const token = tab === "INVITE" ? access.invite : access.dynamicQr;
  if (!token) return <p className="dsub" style={{ textAlign: "center", padding: "20px 0" }}>No hay enlace disponible.</p>;

  return (
    <div className="qr-show">
      <div className="qr-frame">
        <QRCodeSVG value={token.joinUrl} size={190} bgColor="#ffffff" fgColor="#2a2118" />
      </div>
      <p className="dsub" style={{ textAlign: "center", margin: 0 }}>
        {tab === "INVITE" ? "El cliente escanea para unirse a la cuenta" : "QR temporal — válido para esta sesión"}
      </p>
      <code>{token.joinUrl}</code>
    </div>
  );
}

function detailOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback;
}

/** `tono` "suave" para lo que no corre prisa (la pregunta "¿cuándo sale?"). */
function beep(tono: "aviso" | "suave" = "aviso") {
  if (typeof window === "undefined") return;
  const suave = tono === "suave";
  const dur = suave ? 0.35 : 0.2;
  try {
    const ctx = new window.AudioContext();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.frequency.value = suave ? 587 : 880;
    osc.start();
    gain.gain.setValueAtTime(suave ? 0.08 : 0.15, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + dur);
    osc.stop(ctx.currentTime + dur);
  } catch {
    /* audio not available */
  }
}
