"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { QRCodeSVG } from "qrcode.react";
import { useRouter } from "next/navigation";
import * as signalR from "@microsoft/signalr";
import type {
  FloorPlanDto,
  ListOrdersResponse,
  OrderSummaryDto,
  PaymentConfigDto,
  PublicCatalogItemDto,
  PublicCatalogResponse,
  SessionDetailDto,
  SessionJoinTokenDto,
  SessionPaymentStatusDto,
  SessionPinDto,
  ShiftDto,
  ShiftReportDto,
  TableDto,
} from "@mestobeach/api-client";
import { parseEurosToCents, centsToEurosInput } from "@mestobeach/api-client";
import {
  getStoredStaff,
  getToken,
  catalogApi,
  ordersApi,
  paymentsApi,
  sessionsApi,
  ORDERS_HUB_URL,
  SESSIONS_HUB_URL,
} from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { isWebUsbSupported, printReceipt } from "@/lib/escpos";
import { ArrowClockwise, CaretDown, Check, Play, Plus } from "@phosphor-icons/react";
import ScaledFloorMap from "@/components/ScaledFloorMap";
import { useDialog } from "@/components/Dialog";
import { FullscreenButton } from "@/components/FullscreenButton";


type CashdeskTable = {
  table: TableDto;
  session: SessionDetailDto | null;
  orders: ListOrdersResponse["data"];
  payment: SessionPaymentStatusDto | null;
};

type SessionAccess = {
  pin?: SessionPinDto | null;
  dynamicQr?: SessionJoinTokenDto | null;
};

type ChargeMode = "full" | "perPerson" | "byItem" | "equal";

/** Atajos de billete, en céntimos de euro: 10 €, 20 €, 50 €. */
const BILL_SHORTCUTS = [1000, 2000, 5000];

// Base URL of the customer menu app for the fixed table QR.
const MENU_BASE = process.env["NEXT_PUBLIC_MENU_URL"] ?? "http://localhost:3000";

/** Fixed (permanent) QR URL for a table — resolves the table's active session in the menu. */
function fixedTableUrl(tenantId: string, tableCode: string): string {
  return `${MENU_BASE}/mesa?tenant=${tenantId}&table=${encodeURIComponent(tableCode)}`;
}

function printQrSvg(svgId: string, title: string) {
  const svg = document.getElementById(svgId);
  if (!svg) return;
  const w = window.open("", "_blank", "width=420,height=520");
  if (!w) return;
  w.document.write(
    `<html><head><title>${title}</title></head><body style="text-align:center;font-family:sans-serif;padding:24px">` +
      `<h2>${title}</h2>${svg.outerHTML}<p>Escanea para ver el menú de tu mesa</p>` +
      `<script>window.onload=function(){window.print();}</script></body></html>`
  );
  w.document.close();
}

/**
 * El cajero teclea euros ("14,50"); dentro se trabaja en céntimos.
 * `parseEurosToCents` devuelve null si lo tecleado no es un importe válido —
 * antes esta copia devolvía 0 en silencio, que en una caja significa cobrar de menos.
 */
function eurosToCents(value: string): number {
  return parseEurosToCents(value) ?? 0;
}

/** True si el texto es un importe válido: sirve para bloquear el cobro. */
function esImporteValido(value: string): boolean {
  return parseEurosToCents(value) !== null;
}

/** `amount` llega en céntimos de euro: 1450 -> "14,50 €". */
function formatEur(amount: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount / 100);
}

export default function CashdeskPage() {
  const router = useRouter();
  const dialog = useDialog();
  const { staff, setAuth } = useAuthStore();

  const [authReady, setAuthReady] = useState(false);
  const [tables, setTables] = useState<CashdeskTable[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [access, setAccess] = useState<Record<string, SessionAccess>>({});
  const [openingTableId, setOpeningTableId] = useState<string | null>(null);
  const [showNewTable, setShowNewTable] = useState(false);
  const [newTableCode, setNewTableCode] = useState("");
  // After opening a Dynamic-QR table, offer to print its freshly created QR.
  const [printQr, setPrintQr] = useState<{ sessionId: string; tableCode: string } | null>(null);
  const [viewMode, setViewMode] = useState<"map" | "list">("map");
  const [floorPlan, setFloorPlan] = useState<FloorPlanDto | null>(null);
  // Pestañas de piso (patrón del Host): ScaledFloorMap dibuja UN piso, así que la
  // página elige el activo. Si es null, cae al primero por sortOrder.
  const [activeFloorId, setActiveFloorId] = useState<string | null>(null);
  const [creatingTable, setCreatingTable] = useState(false);
  const [showPin, setShowPin] = useState(false);
  const [tipConfig, setTipConfig] = useState<PaymentConfigDto | null>(null);

  // Cobro state
  const [chargeMode, setChargeMode] = useState<ChargeMode>("full");
  const [selectedItemIds, setSelectedItemIds] = useState<Set<string>>(new Set());
  const [splitN, setSplitN] = useState(2);
  const [tipPct, setTipPct] = useState<number | null>(null);
  const [customTip, setCustomTip] = useState("");
  const [received, setReceived] = useState("");
  const [paying, setPaying] = useState(false);
  const [regeneratingPin, setRegeneratingPin] = useState(false);
  const [closing, setClosing] = useState(false);
  const [realtime, setRealtime] = useState(false);
  const tenantRef = useRef<string | null>(null);
  const [shift, setShift] = useState<ShiftDto | null>(null);
  const [shiftReport, setShiftReport] = useState<ShiftReportDto | null>(null);
  const [shiftBusy, setShiftBusy] = useState(false);
  const [closeNote, setCloseNote] = useState("");
  const [showCloseShift, setShowCloseShift] = useState(false);

  // Tomar pedido (sin celular)
  const [catalog, setCatalog] = useState<PublicCatalogResponse | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [showTakeOrder, setShowTakeOrder] = useState(false);
  const [cart, setCart] = useState<Record<string, number>>({});
  const [takingOrder, setTakingOrder] = useState(false);
  const [catalogSearch, setCatalogSearch] = useState("");

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
    // Role gate: the cashdesk is for Cajero/Admin. A KDS-only user belongs on the board.
    const roles = stored.roles ?? [];
    if (!roles.includes("Cajero") && !roles.includes("Admin")) {
      router.replace("/board");
      return;
    }
    setAuth(stored, token);
    setAuthReady(true);
  }, [router, setAuth]);

  useEffect(() => {
    if (!staff) return;
    void hydrateTables(staff.tenantId);
    void paymentsApi
      .getPaymentConfig()
      .then((r) => setTipConfig(r.data))
      .catch(() => setTipConfig(null));
    void paymentsApi
      .getOpenShift()
      .then((r) => setShift(r.data))
      .catch(() => setShift(null));
    setCatalogLoading(true);
    void catalogApi
      .getPublicCatalog(staff.tenantId)
      .then((r) => setCatalog(r.data))
      .catch(() => setCatalog(null))
      .finally(() => setCatalogLoading(false));
  }, [staff]);

  async function handleOpenShift() {
    setShiftBusy(true);
    try {
      const { data } = await paymentsApi.openShift(0);
      setShift(data);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo abrir el turno."));
    } finally {
      setShiftBusy(false);
    }
  }

  async function handleCloseShift() {
    if (!shift) return;
    setShiftBusy(true);
    try {
      const { data } = await paymentsApi.closeShift(shift.id, closeNote.trim() || undefined);
      setShiftReport(data);
      setShift(null);
      setShowCloseShift(false);
      setCloseNote("");
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo cerrar el turno."));
    } finally {
      setShiftBusy(false);
    }
  }

  const selected = useMemo(
    () => tables.find((entry) => entry.table.id === selectedId) ?? null,
    [selectedId, tables]
  );

  // Pisos ordenados + piso activo para el mapa (ScaledFloorMap renderiza uno solo).
  const sortedFloors = useMemo(
    () => [...(floorPlan?.floors ?? [])].sort((a, b) => a.sortOrder - b.sortOrder),
    [floorPlan]
  );
  const activeFloor = sortedFloors.find((f) => f.id === activeFloorId) ?? sortedFloors[0] ?? null;

  // Real-time: bill requests / waiter calls (SessionsHub) + order changes (OrdersHub)
  useEffect(() => {
    if (!staff) return;
    const token = getToken();
    if (!token) return;
    const tenantId = staff.tenantId;
    tenantRef.current = tenantId;
    let active = true;

    const build = (url: string) =>
      new signalR.HubConnectionBuilder()
        .withUrl(`${url}?access_token=${token}`, { transport: signalR.HttpTransportType.WebSockets })
        .withAutomaticReconnect()
        .build();

    const sessions = build(SESSIONS_HUB_URL);
    const orders = build(ORDERS_HUB_URL);

    sessions.on("SessionEvent", (e: { type?: string; sessionId?: string; tableCode?: string; createdAt?: string }) => {
      if (!active) return;
      // Los avisos del comensal —llamar al camarero, pedir la cuenta— NO se
      // atienden desde caja: viven en el PDA, que es quien está en sala, y en
      // Salón. Aquí solo estorbaban, porque el cajero no puede ir a la mesa.
      // Mantener el mapa/lista en sync cuando otro staff (p.ej. un mesero desde
      // /salon) abre/cierra una mesa o regenera su PIN. El backend emite estos
      // eventos a waiter-{tenantId}, al que ya estamos unidos.
      if (e?.type === "SESSION_OPENED" || e?.type === "SESSION_CLOSED" || e?.type === "PIN_REGENERATED") {
        void hydrateTables(tenantId, { silent: true });
      }
    });
    orders.on("OrderEvent", () => {
      if (active) void hydrateTables(tenantId, { silent: true });
    });

    // stop() solo tras resolver start() (en el cleanup) — evita abortar la
    // negociación con el doble-invoke de StrictMode (dev).
    const started = (async () => {
      try {
        await sessions.start();
        await sessions.invoke("JoinWaiterGroup", tenantId);
        await orders.start();
        await orders.invoke("JoinTenantGroup", tenantId);
        if (active) setRealtime(true);
      } catch {
        if (active) setRealtime(false);
      }
    })();

    return () => {
      active = false;
      setRealtime(false);
      void started.finally(() => { void sessions.stop().catch(() => {}); void orders.stop().catch(() => {}); });
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [staff?.tenantId]);

  // Reset cobro inputs when changing table
  useEffect(() => {
    setChargeMode("full");
    setSplitN(2);
    setTipPct(null);
    setCustomTip("");
    setReceived("");
      setCustomTip("");   // si no, la propina absoluta anterior se arrastra al siguiente cobro
    setSelectedItemIds(new Set());
  }, [selectedId]);

  // ── Bill computation ────────────────────────────────────────────────────────
  const activeOrders = useMemo(
    () => (selected?.orders ?? []).filter((o) => o.status !== "CANCELLED"),
    [selected]
  );

  const subtotal = selected?.payment?.ordersTotalCents ?? activeOrders.reduce((s, o) => s + o.totalCents, 0);
  const paid = selected?.payment?.totalPaidCents ?? 0;
  const remaining = selected?.payment?.remainingCents ?? subtotal;
  const hasUndeliveredOrders = activeOrders.some((o) => o.status !== "DELIVERED");

  // participantId -> label: prefer the customer's real name (from the identify screen),
  // fall back to "Anfitrión" / "Invitado N" by join order.
  const participantLabels = useMemo(() => {
    const map = new Map<string, string>();
    const ps = [...(selected?.session?.participants ?? [])].sort(
      (a, b) => new Date(a.joinedAt).getTime() - new Date(b.joinedAt).getTime()
    );
    let guest = 0;
    ps.forEach((p) => {
      guest += p.isHost ? 0 : 1;
      const fallback = p.isHost ? "Anfitrión" : `Invitado ${guest}`;
      map.set(p.participantId, p.displayName?.trim() || fallback);
    });
    return map;
  }, [selected]);

  // Name an order was placed under: the order's own customerName (denormalized at creation),
  // falling back to the participant label. NOTE: we group by this — NOT by participantId —
  // because the same device/participant can place orders under different names (the customer
  // re-identifies on the menu), and the cashier must see each order under the name it was made.
  const orderWho = useCallback(
    (o: OrderSummaryDto) => o.customerName?.trim() || participantLabels.get(o.participantId) || "Otro",
    [participantLabels]
  );
  const orderKey = useCallback(
    (o: OrderSummaryDto) =>
      o.customerName?.trim() ? `name:${o.customerName.trim().toLowerCase()}` : `part:${o.participantId}`,
    []
  );

  // grouped bill by person (the name each order was placed under)
  const billByParticipant = useMemo(() => {
    const groups = new Map<string, { key: string; label: string; subtotal: number; count: number; orderIds: string[] }>();
    activeOrders.forEach((o) => {
      const key = orderKey(o);
      const g = groups.get(key) ?? { key, label: orderWho(o), subtotal: 0, count: 0, orderIds: [] };
      g.subtotal += o.totalCents;
      g.count += o.itemCount;
      g.orderIds.push(o.id);
      groups.set(key, g);
    });
    return Array.from(groups.values());
  }, [activeOrders, orderKey, orderWho]);

  // Flat list of bill items (across orders) for the "pay by product" mode.
  const billItems = useMemo(() => {
    return activeOrders.flatMap((o) =>
      o.items.map((it) => ({
        id: it.id,
        name: it.itemNameSnapshot,
        modifiers: it.selectedModifiers ?? null,
        qty: it.quantity,
        amount: it.itemPriceSnapshotCents * it.quantity,
        who: orderWho(o),
      }))
    );
  }, [activeOrders, orderWho]);

  const selectedItemsAmount = useMemo(
    () => billItems.filter((i) => selectedItemIds.has(i.id)).reduce((s, i) => s + i.amount, 0),
    [billItems, selectedItemIds]
  );

  const paidSplits = useMemo(
    () => (selected?.payment?.payments ?? []).filter((p) => p.splitTotal != null && p.status === "COMPLETED").length,
    [selected]
  );

  function tipFor(base: number): number {
    if (customTip.trim() !== "") return eurosToCents(customTip);
    if (tipPct) return Math.floor((base * tipPct) / 100);
    return 0;
  }

  async function hydrateTables(tenantId: string, opts?: { silent?: boolean }) {
    if (!opts?.silent) setLoading(true);
    setError(null);
    try {
      const { data } = await sessionsApi.listTables(tenantId);
      const next = await Promise.all(
        data.tables.map(async (table) => {
          if (!table.hasActiveSession || !table.activeSessionId) {
            return { table, session: null, orders: [], payment: null } satisfies CashdeskTable;
          }
          try {
            const [sessionRes, ordersRes, paymentRes] = await Promise.all([
              sessionsApi.getSession(table.activeSessionId),
              ordersApi.listOrders(table.activeSessionId),
              paymentsApi.getSessionPaymentStatus(table.activeSessionId),
            ]);
            return {
              table,
              session: sessionRes.data,
              orders: ordersRes.data.data,
              payment: paymentRes.data,
            } satisfies CashdeskTable;
          } catch {
            return { table, session: null, orders: [], payment: null } satisfies CashdeskTable;
          }
        })
      );
      setTables(next.sort((a, b) => a.table.tableCode.localeCompare(b.table.tableCode, "es")));
      setSelectedId((current) => current ?? next[0]?.table.id ?? null);
      // Refresh the floor map (positions + live status) alongside the table list.
      sessionsApi.getFloorPlan().then(({ data }) => setFloorPlan(data)).catch(() => {});
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo cargar la vista de cajero."));
    } finally {
      setLoading(false);
    }
  }

  // Pulsar una mesa del mapa solo la selecciona. Abrirla es un acto deliberado y se
  // hace desde el botón «Abrir mesa», que además pide confirmación: antes, un roce
  // sobre el plano abría la sesión sin preguntar, y deshacerlo obliga a cerrarla.
  function handleMapPick(tableId: string) {
    const entry = tables.find((e) => e.table.id === tableId);
    if (!entry) return;
    setSelectedId(tableId);
    if (entry.session) {
      void hydrateSessionAccess(entry.session.sessionId);
    }
  }

  async function hydrateSessionAccess(sessionId: string) {
    const [pinRes, dynamicQrRes] = await Promise.allSettled([
      sessionsApi.getSessionPin(sessionId),
      sessionsApi.getJoinLink(sessionId, "DYNAMIC_QR"),
    ]);
    setAccess((current) => ({
      ...current,
      [sessionId]: {
        pin: pinRes.status === "fulfilled" ? pinRes.value.data : null,
        dynamicQr: dynamicQrRes.status === "fulfilled" ? dynamicQrRes.value.data : null,
      },
    }));
  }

  /**
   * Abre la mesa tras confirmarlo. El modo de QR sale de lo que el admin configuró
   * en la mesa; si es dinámico, después se ofrece imprimir el recién creado.
   *
   * Se pregunta porque abrir una sesión no es inocuo: crea la cuenta de esa mesa y
   * cerrarla luego es un paso aparte.
   */
  async function openTable(table: TableDto) {
    if (!staff) return;

    const seguir = await dialog.confirm({
      title: `Abrir la mesa ${table.tableCode}`,
      message: "Se creará una nueva sesión para esta mesa. ¿Quieres abrirla?",
      confirmText: "Abrir mesa",
      cancelText: "Cancelar",
    });
    if (!seguir) return;

    setOpeningTableId(table.id);
    setError(null);
    try {
      const { data } = await sessionsApi.openSession({
        tenantId: staff.tenantId,
        tableCode: table.tableCode,
        serviceMode: "STAFFED",
        ...(table.qrMode ? { qrMode: table.qrMode } : {}),
        ...(getToken() ? { waiterJwt: getToken()! } : {}),
      });
      await hydrateTables(staff.tenantId);
      await hydrateSessionAccess(data.sessionId);
      setSelectedId(table.id);
      if ((data.qrMode ?? table.qrMode) === "DYNAMIC") {
        setPrintQr({ sessionId: data.sessionId, tableCode: table.tableCode });
      }
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo abrir la mesa."));
    } finally {
      setOpeningTableId(null);
    }
  }

  async function handleRefreshSelected() {
    if (!staff || !selected?.session) return;
    await hydrateTables(staff.tenantId);
    await hydrateSessionAccess(selected.session.sessionId);
  }

  /** Open a session on a typed table code; the backend creates the table if it doesn't exist (walk-in). */
  async function handleOpenNewTable() {
    if (!staff || !newTableCode.trim()) return;
    setCreatingTable(true);
    setError(null);
    try {
      const { data } = await sessionsApi.openSession({
        tenantId: staff.tenantId,
        tableCode: newTableCode.trim(),
        serviceMode: "STAFFED",
        ...(getToken() ? { waiterJwt: getToken()! } : {}),
      });
      await hydrateTables(staff.tenantId);
      await hydrateSessionAccess(data.sessionId);
      const t = tables.find((x) => x.table.tableCode === newTableCode.trim());
      if (t) setSelectedId(t.table.id);
      setShowNewTable(false);
      setNewTableCode("");
      if (data.qrMode === "DYNAMIC") setPrintQr({ sessionId: data.sessionId, tableCode: newTableCode.trim() });
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo abrir la mesa."));
    } finally {
      setCreatingTable(false);
    }
  }

  /** Registers a cash payment for the given bill amount (tip computed on that base). */
  async function charge(
    amountCents: number,
    split?: { index: number; total: number },
    payerLabel?: string
  ) {
    if (!staff || !selected?.session || amountCents <= 0) return;
    setPaying(true);
    try {
      await paymentsApi.registerCashPayment({
        sessionId: selected.session.sessionId,
        amountCents,
        tipCents: tipFor(amountCents),
        ...(split ? { splitIndex: split.index, splitTotal: split.total } : {}),
        ...(payerLabel ? { payerLabel } : {}),
      });
      await hydrateTables(staff.tenantId);
      setReceived("");
      setCustomTip("");   // si no, la propina absoluta anterior se arrastra al siguiente cobro
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo registrar el pago."));
    } finally {
      setPaying(false);
    }
  }

  function setCartQty(itemId: string, qty: number) {
    setCart((prev) => {
      const next = { ...prev };
      if (qty <= 0) delete next[itemId];
      else next[itemId] = qty;
      return next;
    });
  }

  function openTakeOrder() {
    setCart({});
    setCatalogSearch("");
    setShowTakeOrder(true);
  }

  /** Staff takes an order for a customer without a phone: create a staff participant, then the order. */
  async function handleTakeOrder() {
    if (!staff || !selected?.session) return;
    const items = Object.entries(cart)
      .filter(([, qty]) => qty > 0)
      .map(([catalogItemId, quantity]) => ({ catalogItemId, quantity }));
    if (items.length === 0) return;
    setTakingOrder(true);
    setError(null);
    try {
      const { data: participant } = await sessionsApi.addParticipant(selected.session.sessionId, {
        deviceId: `staff-${Date.now()}`,
        isHost: false,
      });
      await ordersApi.createOrder({
        sessionId: selected.session.sessionId,
        participantId: participant.participantId,
        items,
      });
      await hydrateTables(staff.tenantId);
      setShowTakeOrder(false);
      setCart({});
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo tomar el pedido."));
    } finally {
      setTakingOrder(false);
    }
  }

  async function handleCancelOrder(orderId: string) {
    if (!staff) return;
    try {
      await ordersApi.cancelOrder(orderId, "Cancelado por cajero");
      await hydrateTables(staff.tenantId);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo cancelar el pedido."));
    }
  }

  async function handleRegeneratePin() {
    if (!staff || !selected?.session) return;
    setRegeneratingPin(true);
    try {
      await sessionsApi.regeneratePin(selected.session.sessionId);
      await hydrateSessionAccess(selected.session.sessionId);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo regenerar el PIN."));
    } finally {
      setRegeneratingPin(false);
    }
  }

  async function handleCloseSession() {
    if (!staff || !selected?.session) return;
    if (
      hasUndeliveredOrders &&
      !(await dialog.confirm({
        message: "Hay pedidos sin entregar. ¿Cerrar la sesión de todos modos?",
        tone: "danger",
        confirmText: "Cerrar sesión",
      }))
    )
      return;
    setClosing(true);
    try {
      await sessionsApi.closeSession(selected.session.sessionId, "MANUAL_CLOSE");
      await hydrateTables(staff.tenantId);
    } catch (err: unknown) {
      setError(detailOf(err, "No se pudo cerrar la sesión."));
    } finally {
      setClosing(false);
    }
  }

  async function handlePrintReceipt() {
    if (!selected?.session) return;
    const tip = tipFor(remaining);
    try {
      await printReceipt({
        restaurant: "Mesto Beach",
        tableCode: selected.table.tableCode,
        cashier: staff?.name ?? "Cajero",
        lines: activeOrders.map((o) => ({
          name: `Pedido #${o.id.slice(-6).toUpperCase()}`,
          qty: o.itemCount,
          totalCents: o.totalCents,
        })),
        subtotalCents: subtotal,
        tipCents: tip,
        totalCents: remaining + tip,
        method: "Efectivo",
        at: new Date(),
      });
    } catch (err: unknown) {
      setError((err as Error)?.message ?? "No se pudo imprimir el recibo.");
    }
  }

  if (!authReady) return null;
  if (loading) {
    return <main className="grid min-h-screen place-items-center bg-canvas text-muted">Cargando caja…</main>;
  }

  const tipOptions = tipConfig?.tipEnabled ? tipConfig.tipOptions : [];

  return (
    <div className="min-h-full bg-canvas">
      {/* Sticky app bar */}
      <header className="sticky top-0 z-30 border-b border-hair/40 bg-paper/80 backdrop-blur">
        <div className="mx-auto flex max-w-[1320px] flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
          <div className="mr-auto flex items-center gap-3">
            <div>
              <h1 className="font-display text-2xl font-normal leading-none text-ink">Caja</h1>
              <p className="mt-1 text-xs text-muted">{staff?.name}</p>
            </div>
            <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold tracking-[0.03em] ${realtime ? "bg-emerald-500/10 text-emerald-700" : "bg-paper2 text-muted"}`}>
              <span className={`h-1.5 w-1.5 rounded-full ${realtime ? "bg-emerald-500" : "bg-muted/50"}`} />
              {realtime ? "En vivo" : "Sin conexión"}
            </span>
          </div>

          {/* Shift status + primary action */}
          {shift ? (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-oro/[0.10] px-3 py-1.5 text-xs font-semibold tracking-[0.03em] text-brand-700">
              <span className="h-1.5 w-1.5 rounded-full bg-oro" /> Turno abierto
            </span>
          ) : (
            <button onClick={() => void handleOpenShift()} disabled={shiftBusy} className="cd-chip">
              {shiftBusy ? "…" : (<><Play size={16} weight="fill" /> Abrir turno</>)}
            </button>
          )}
          <button
            onClick={() => setShowNewTable(true)}
            disabled={!shift}
            title={shift ? "Abrir/crear una mesa" : "Abre el turno primero"}
            className="cd-btn cd-primary"
          >
            <Plus size={16} weight="bold" /> Abrir mesa
          </button>

          {/* Acción propia de caja (la navegación entre vistas vive en el sidebar) */}
          {shift && <button onClick={() => setShowCloseShift(true)} className="cd-chip">Cerrar turno</button>}

          {/* La caja también vive en una pantalla fija: se le puede dar todo el ancho.
              `min-h-[44px]` para que case con la altura táctil de los chips de al lado. */}
          <FullscreenButton className="min-h-[44px]" />
        </div>
      </header>

      <div className="mx-auto grid max-w-[1320px] gap-4 px-4 pb-8 pt-5">

        {error && <div className="rounded-xl border border-rose-400/40 bg-rose-500/10 px-4 py-3 text-sm font-medium text-rose-600">{error}</div>}

        <section className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(420px,500px)]">
          {/* Left: floor — toggle between interactive map and table list */}
          <aside className="cd-panel flex flex-col p-3 xl:max-h-[calc(100vh-150px)]">
            <div className="mb-3 flex flex-wrap items-center gap-2 px-1">
              {/* View toggle */}
              <div className="flex rounded-xl bg-paper2 p-1">
                <button
                  onClick={() => setViewMode("map")}
                  className={`min-h-[40px] rounded-lg px-4 text-sm font-bold transition active:scale-95 ${viewMode === "map" ? "cd-seg-on" : "cd-seg-off"}`}
                >
                  Mapa
                </button>
                <button
                  onClick={() => setViewMode("list")}
                  className={`min-h-[40px] rounded-lg px-4 text-sm font-bold transition active:scale-95 ${viewMode === "list" ? "cd-seg-on" : "cd-seg-off"}`}
                >
                  Lista
                </button>
              </div>
              <span className="ml-auto rounded-full border border-hair/40 bg-paper2 px-2.5 py-1 text-xs font-semibold text-muted">{tables.length} mesas</span>
            </div>

            <div className="overflow-y-auto">
              {viewMode === "map" && floorPlan && floorPlan.floors.length > 0 ? (
                <div>
                  {/* Pestañas de piso (patrón del Host) */}
                  {sortedFloors.length > 0 && (
                    <div className="mb-3 flex flex-wrap gap-2">
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
                  {activeFloor && (
                    <ScaledFloorMap
                      floor={activeFloor}
                      tables={floorPlan.tables}
                      selectedId={selectedId}
                      onPick={(t) => handleMapPick(t.id)}
                    />
                  )}
                </div>
              ) : tables.length === 0 ? (
                <p className="py-6 text-center text-sm text-muted">No hay mesas.</p>
              ) : (
                <div className="grid gap-4">
                  {(() => {
                    // Group the table list by floor, mirroring the map's floor separation.
                    // The floor placement lives in the floor plan (planTables), so resolve each
                    // table's floor from there (falling back to the table's own floorId).
                    const floorsSorted = [...(floorPlan?.floors ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
                    const tableFloor = new Map((floorPlan?.tables ?? []).map((pt) => [pt.id, pt.floorId ?? null]));
                    const floorOf = (t: (typeof tables)[number]) => t.table.floorId ?? tableFloor.get(t.table.id) ?? null;
                    const known = new Set(floorsSorted.map((f) => f.id));
                    const groups = floorsSorted
                      .map((f) => ({ id: f.id, name: f.name, items: tables.filter((t) => floorOf(t) === f.id) }))
                      .filter((g) => g.items.length > 0);
                    const ungrouped = tables.filter((t) => { const fid = floorOf(t); return !fid || !known.has(fid); });
                    if (ungrouped.length) groups.push({ id: "__none__", name: groups.length ? "Sin piso" : "", items: ungrouped });

                    return groups.map((g) => (
                      <div key={g.id}>
                        {g.name && (
                          <div className="mb-2 flex items-center gap-2 px-0.5">
                            <h3 className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">{g.name}</h3>
                            <span className="rounded-full border border-hair/40 bg-paper2 px-1.5 text-[11px] font-semibold text-muted">{g.items.length}</span>
                          </div>
                        )}
                        <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 xl:grid-cols-2 2xl:grid-cols-3">
                          {g.items.map((entry) => {
                            const busy = entry.table.hasActiveSession;
                            const on = selectedId === entry.table.id;
                            return (
                              <button
                                key={entry.table.id}
                                onClick={() => {
                                  setSelectedId(entry.table.id);
                                  if (entry.session) void hydrateSessionAccess(entry.session.sessionId);
                                }}
                                className={`rounded-2xl border-2 p-3.5 text-left transition active:scale-[.98] ${
                                  on ? "border-brand-500" : "border-hair/40"
                                } ${busy ? "bg-oro/[0.10]" : "bg-paper"}`}
                              >
                                <div className="flex items-center justify-between gap-2">
                                  <strong className="truncate font-display text-lg font-normal text-ink">{entry.table.tableCode}</strong>
                                  <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.03em] ${busy ? "bg-oro/[0.12] text-brand-700" : "bg-paper2 text-muted"}`}>
                                    <span className={`h-1.5 w-1.5 rounded-full ${busy ? "bg-oro" : "bg-muted/40"}`} />
                                    {busy ? "Ocupada" : "Libre"}
                                  </span>
                                </div>
                                <div className="mt-1.5 truncate text-xs text-muted">{entry.table.zone ?? "Sin zona"}</div>
                              </button>
                            );
                          })}
                        </div>
                      </div>
                    ));
                  })()}
                </div>
              )}
            </div>
          </aside>

          {/* Right: selected table workspace (bill + cobro) */}
          <div className="min-w-0 xl:max-h-[calc(100vh-150px)] xl:overflow-y-auto xl:pr-1">
            {!selected ? (
              <div className="cd-panel grid place-items-center p-10 text-center text-sm text-muted">
                <div>
                  <div className="mb-2 text-2xl text-oro/70">◆</div>
                  Selecciona una mesa para cobrar o atender.
                </div>
              </div>
            ) : (
              <div className="grid gap-4">
                <div className="cd-panel flex flex-wrap items-center justify-between gap-3 p-4">
                  <div className="flex items-center gap-3">
                    <div className={`grid h-14 w-14 place-items-center rounded-2xl font-display text-xl font-normal ${selected.table.hasActiveSession ? "bg-oro/[0.12] text-brand-700" : "bg-paper2 text-muted"}`}>
                      {selected.table.tableCode}
                    </div>
                    <div>
                      <h2 className="font-display text-xl font-normal leading-tight text-ink">Mesa {selected.table.tableCode}</h2>
                      <p className="text-xs text-muted">{selected.table.zone ?? "Sin zona"} · {selected.table.hasActiveSession ? "Ocupada" : "Libre"}</p>
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {!selected.table.hasActiveSession && (
                      <button onClick={() => void openTable(selected.table)} disabled={!shift || openingTableId === selected.table.id} title={shift ? "" : "Abre el turno primero"} className="cd-btn cd-primary">{openingTableId === selected.table.id ? "Abriendo…" : "Abrir mesa"}</button>
                    )}
                    {selected.session && (
                      <>
                        <button onClick={openTakeOrder} className="cd-btn cd-primary"><Plus size={16} weight="bold" /> Tomar pedido</button>
                        <button onClick={() => void handleRefreshSelected()} className="cd-btn cd-ghost"><ArrowClockwise size={16} weight="bold" /> Refrescar</button>
                      </>
                    )}
                  </div>
                </div>

                {selected.session ? (
                  <>
                    {/* Acceso de la mesa (PIN + QR) — secundario para el cajero, plegado por defecto */}
                    <details className="cd-panel group overflow-hidden">
                      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 px-4 py-3.5 text-sm font-bold">
                        <span>Compartir menú / acceso</span>
                        <CaretDown size={16} className="text-muted transition group-open:rotate-180" />
                      </summary>
                      <div className="grid gap-4 border-t border-hair/40 p-4">
                        {/* PIN */}
                        <div className="flex flex-wrap items-center gap-2">
                          <button onClick={() => setShowPin((c) => !c)} className="cd-chip">
                            {showPin ? "Ocultar PIN" : "Mostrar PIN"}
                          </button>
                          <button onClick={() => void handleRegeneratePin()} className="cd-chip">
                            {regeneratingPin ? "Regenerando…" : "Regenerar PIN"}
                          </button>
                          {showPin && access[selected.session.sessionId]?.pin && (
                            <span className="cd-money rounded-xl bg-oro/[0.08] px-4 py-2 text-2xl tracking-[0.3em] text-brand-700">
                              {access[selected.session.sessionId]?.pin?.pin}
                            </span>
                          )}
                        </div>

                        {access[selected.session.sessionId]?.dynamicQr && (
                          <div className="grid w-fit justify-items-center gap-2.5 rounded-2xl border border-hair/40 bg-paper2 p-4">
                            <div className="rounded-lg bg-paper p-2.5">
                              <QRCodeSVG value={access[selected.session.sessionId]!.dynamicQr!.joinUrl} size={140} />
                            </div>
                            <button onClick={() => window.print()} className="cd-chip">Imprimir QR dinámico</button>
                          </div>
                        )}

                        {/* QR FIJO de la mesa */}
                        {staff && (() => {
                          const url = fixedTableUrl(staff.tenantId, selected.table.tableCode);
                          const waText = `Aquí está el menú de tu mesa (${selected.table.tableCode}): ${url}`;
                          return (
                            <div className="grid w-fit max-w-[300px] justify-items-center gap-2.5 rounded-2xl border border-hair/40 bg-paper2 p-4">
                              <strong className="self-start text-sm text-ink">QR de la mesa (fijo)</strong>
                              <div className="rounded-lg bg-paper p-2.5">
                                <QRCodeSVG id="fixed-table-qr" value={url} size={160} />
                              </div>
                              <span className="break-all text-center text-[11px] text-muted">{url}</span>
                              <div className="flex flex-wrap justify-center gap-2">
                                <a
                                  href={`https://wa.me/?text=${encodeURIComponent(waText)}`}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="cd-btn"
                                  style={{ background: "#16a34a", color: "white" }}
                                >
                                  WhatsApp
                                </a>
                                <button onClick={() => printQrSvg("fixed-table-qr", `Mesa ${selected.table.tableCode}`)} className="cd-chip">Imprimir</button>
                                <button onClick={() => { void navigator.clipboard?.writeText(url); }} className="cd-chip">Copiar enlace</button>
                              </div>
                            </div>
                          );
                        })()}
                      </div>
                    </details>

                    {/* Cuenta por participante */}
                    <div className="cd-panel p-4">
                      <h3 className="mb-3 font-display text-lg font-normal text-ink">Cuenta {billByParticipant.length > 1 ? "· por persona" : ""}</h3>
                      {activeOrders.length === 0 && <p className="text-sm text-muted">Sin pedidos en la sesión.</p>}
                      <div className="grid gap-2.5">
                        {billByParticipant.map((g) => {
                          const personOrders = activeOrders.filter((o) => g.orderIds.includes(o.id));
                          return (
                            <div key={g.key} className="rounded-xl border border-hair/40 bg-paper2 p-3">
                              <div className="mb-1.5 flex items-center justify-between gap-3">
                                <strong className="font-display text-base font-normal text-ink">{g.label}</strong>
                                <strong className="cd-money text-sm text-ink">{formatEur(g.subtotal)}</strong>
                              </div>
                              {personOrders.map((order) => (
                                <div key={order.id} className="border-t border-hair/40 py-1">
                                  <div className="flex items-center justify-between gap-3 text-[11px] text-muted">
                                    <span>#{order.id.slice(-6).toUpperCase()} · {order.status}</span>
                                    {order.status === "PENDING" && (
                                      <button onClick={() => void handleCancelOrder(order.id)} className="rounded-md px-2 py-0.5 text-[11px] font-semibold text-rose-600 hover:bg-rose-500/10">Cancelar</button>
                                    )}
                                  </div>
                                  {order.observacion && (
                                    <div className="mt-1 rounded-md bg-oro/[0.10] px-2 py-1 text-[12px] text-brand-700">
                                      <span className="mr-1 text-[9px] font-bold uppercase tracking-wide text-brand-700/70">Obs</span>
                                      <span className="font-medium">{order.observacion}</span>
                                    </div>
                                  )}
                                  {order.items.map((it) => (
                                    <div key={it.id} className="flex justify-between gap-3 py-0.5 text-[13px] text-ink">
                                      <span>
                                        {it.quantity}× {it.itemNameSnapshot}
                                        {it.selectedModifiers ? <em className="text-brand-700"> · {it.selectedModifiers}</em> : null}
                                        {it.notes ? <em className="text-muted"> — {it.notes}</em> : null}
                                      </span>
                                      <span className="cd-money whitespace-nowrap text-[13px]">{formatEur(it.itemPriceSnapshotCents * it.quantity)}</span>
                                    </div>
                                  ))}
                                </div>
                              ))}
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    {/* Resumen — money panel */}
                    <div className="cd-panel grid grid-cols-3 gap-3 p-4">
                      <div>
                        <div className="text-[11px] uppercase tracking-wide text-muted">Subtotal</div>
                        <div className="cd-money mt-0.5 text-lg text-ink">{formatEur(subtotal)}</div>
                      </div>
                      <div>
                        <div className="text-[11px] uppercase tracking-wide text-muted">Pagado</div>
                        <div className="cd-money mt-0.5 text-lg text-emerald-700">{formatEur(paid)}</div>
                      </div>
                      <div>
                        <div className="text-[11px] uppercase tracking-wide text-muted">Pendiente</div>
                        <div className="cd-money mt-0.5 text-lg text-brand-700">{formatEur(remaining)}</div>
                      </div>
                      {hasUndeliveredOrders && (
                        <p className="col-span-3 mt-1 rounded-lg bg-amber-500/10 px-3 py-2 text-[13px] text-amber-700">Hay pedidos sin entregar.</p>
                      )}
                    </div>

                    {/* Cobro — el panel protagonista */}
                    {remaining > 0 ? (
                      <div className="cd-panel grid gap-4 border-2 border-brand-500/40 p-4">
                        <div className="flex items-center justify-between">
                          <h3 className="font-display text-lg font-normal text-ink">Cobro en caja</h3>
                          <span className="cd-money text-lg text-brand-700">{formatEur(remaining)}</span>
                        </div>

                        {/* Modo — segmented, touch grid */}
                        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                          {(["full", "perPerson", "byItem", "equal"] as ChargeMode[]).map((m) => (
                            <button
                              key={m}
                              onClick={() => setChargeMode(m)}
                              className={`cd-seg ${chargeMode === m ? "cd-seg-on" : "cd-seg-off"}`}
                            >
                              {m === "full" ? "Completa"
                                : m === "perPerson" ? "Por persona"
                                : m === "byItem" ? "Por productos"
                                : "Dividir"}
                            </button>
                          ))}
                        </div>

                        {/* Propina */}
                        {tipConfig?.tipEnabled && (
                          <div className="grid gap-2">
                            <span className="text-xs font-semibold uppercase tracking-wide text-muted">Propina (servicio)</span>
                            <div className="flex flex-wrap items-center gap-2">
                              <button onClick={() => { setTipPct(null); setCustomTip(""); }} className={`cd-chip ${tipPct === null && customTip === "" ? "!border-brand-500 !bg-brand-500 !text-white" : ""}`}>Sin propina</button>
                              {tipOptions.map((pct) => (
                                <button key={pct} onClick={() => { setTipPct(pct); setCustomTip(""); }} className={`cd-chip min-w-[64px] ${tipPct === pct && customTip === "" ? "!border-brand-500 !bg-brand-500 !text-white" : ""}`}>
                                  {pct}%
                                </button>
                              ))}
                              <input
                                value={customTip}
                                onChange={(e) => { setCustomTip(e.target.value); setTipPct(null); }}
                                placeholder="Otro (€)"
                                inputMode="decimal"
                                className="cd-field w-28"
                              />
                            </div>
                          </div>
                        )}

                        {/* Modo COMPLETO */}
                        {chargeMode === "full" && (
                          <CashCharge
                            base={remaining}
                            tip={tipFor(remaining)}
                            received={received}
                            setReceived={setReceived}
                            paying={paying}
                            onCharge={() => void charge(remaining)}
                            label="Cobrar cuenta completa"
                          />
                        )}

                        {/* Modo IGUALITARIO */}
                        {chargeMode === "equal" && (
                          <div className="grid gap-2.5">
                            <label className="flex items-center gap-3">
                              <span className="text-sm text-muted">Número de partes</span>
                              <input type="number" min={2} max={20} value={splitN} onChange={(e) => setSplitN(Math.max(2, Number(e.target.value) || 2))} className="cd-field w-24" />
                            </label>
                            <p className="rounded-lg bg-paper2 px-3 py-2 text-sm text-muted">
                              Parte {Math.min(paidSplits + 1, splitN)} de {splitN} · <strong className="cd-money">{formatEur(equalPart(subtotal, splitN, paidSplits, remaining))}</strong>
                            </p>
                            <CashCharge
                              base={equalPart(subtotal, splitN, paidSplits, remaining)}
                              tip={tipFor(equalPart(subtotal, splitN, paidSplits, remaining))}
                              received={received}
                              setReceived={setReceived}
                              paying={paying}
                              onCharge={() => void charge(equalPart(subtotal, splitN, paidSplits, remaining), { index: paidSplits + 1, total: splitN })}
                              label={`Cobrar parte ${Math.min(paidSplits + 1, splitN)}/${splitN}`}
                            />
                          </div>
                        )}

                        {/* Modo POR PERSONA */}
                        {chargeMode === "perPerson" && (
                          <div className="grid gap-2.5">
                            {billByParticipant.map((g, idx) => (
                              <div key={g.key} className="flex items-center justify-between gap-3 rounded-xl border border-hair/40 bg-paper2 p-3">
                                <span className="text-sm text-ink">{g.label} · <strong className="cd-money">{formatEur(g.subtotal)}</strong> {tipFor(g.subtotal) > 0 ? <em className="text-muted">(+{formatEur(tipFor(g.subtotal))} prop.)</em> : ""}</span>
                                <button
                                  className="cd-btn cd-primary"
                                  disabled={paying || g.subtotal <= 0}
                                  onClick={() => void charge(g.subtotal, { index: idx + 1, total: billByParticipant.length }, g.label)}
                                >
                                  Cobrar
                                </button>
                              </div>
                            ))}
                            <p className="text-xs text-muted">La sesión se cierra al cobrar a todos.</p>
                          </div>
                        )}

                        {/* Modo POR PRODUCTOS */}
                        {chargeMode === "byItem" && (
                          <div className="grid gap-2.5">
                            <div className="flex items-center justify-between">
                              <span className="text-sm text-muted">Selecciona los productos a cobrar</span>
                              <button
                                className="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-brand-700 hover:bg-oro/[0.10]"
                                onClick={() => setSelectedItemIds(
                                  selectedItemIds.size === billItems.length ? new Set() : new Set(billItems.map((i) => i.id))
                                )}
                              >
                                {selectedItemIds.size === billItems.length ? "Quitar todo" : "Seleccionar todo"}
                              </button>
                            </div>
                            <div className="grid max-h-60 gap-1 overflow-y-auto">
                              {billItems.map((it) => {
                                const checked = selectedItemIds.has(it.id);
                                return (
                                  <label key={it.id} className={`flex cursor-pointer items-center justify-between gap-3 rounded-xl px-3 py-2.5 ${checked ? "bg-oro/[0.10] ring-1 ring-oro/40" : "bg-paper2"}`}>
                                    <span className="flex items-center gap-3">
                                      <input
                                        type="checkbox"
                                        className="h-5 w-5 accent-brand-500"
                                        checked={checked}
                                        onChange={() => setSelectedItemIds((prev) => {
                                          const next = new Set(prev);
                                          if (next.has(it.id)) next.delete(it.id); else next.add(it.id);
                                          return next;
                                        })}
                                      />
                                      <span className="text-[13px] text-ink">{it.qty}× {it.name}{it.modifiers ? <span className="text-brand-700"> · {it.modifiers}</span> : null} <span className="text-muted">· {it.who}</span></span>
                                    </span>
                                    <span className="cd-money text-[13px]">{formatEur(it.amount)}</span>
                                  </label>
                                );
                              })}
                            </div>
                            <p className="text-sm text-muted">
                              Seleccionado: <strong className="cd-money text-ink">{formatEur(selectedItemsAmount)}</strong>
                              {selectedItemsAmount > remaining ? <span className="text-amber-700"> · supera lo pendiente</span> : null}
                            </p>
                            <CashCharge
                              base={Math.min(selectedItemsAmount, remaining)}
                              tip={tipFor(Math.min(selectedItemsAmount, remaining))}
                              received={received}
                              setReceived={setReceived}
                              paying={paying || selectedItemsAmount <= 0}
                              onCharge={() => void charge(Math.min(selectedItemsAmount, remaining)).then(() => setSelectedItemIds(new Set()))}
                              label="Cobrar productos seleccionados"
                            />
                            <p className="text-xs text-muted">El abono parcial reduce lo pendiente; la sesión se cierra al saldar el total.</p>
                          </div>
                        )}
                      </div>
                    ) : (
                      <div className="cd-panel flex items-center gap-3 border border-emerald-500/40 bg-emerald-500/10 p-5 text-emerald-700">
                        <Check size={22} weight="bold" />
                        <span className="text-base font-semibold">Cuenta saldada · {formatEur(paid)}</span>
                      </div>
                    )}

                    {/* Cierre */}
                    <div className="flex flex-wrap justify-end gap-2.5">
                      {activeOrders.length > 0 && (
                        <button
                          onClick={() => void handlePrintReceipt()}
                          disabled={!isWebUsbSupported()}
                          title={isWebUsbSupported() ? "Imprimir en impresora térmica" : "WebUSB no disponible (usa Chrome con impresora)"}
                          className="cd-btn cd-ghost"
                        >
                          Imprimir recibo
                        </button>
                      )}
                      <button onClick={() => void handleCloseSession()} disabled={closing} className="cd-btn cd-danger">
                        {closing ? "Cerrando…" : "Cerrar sesión"}
                      </button>
                    </div>
                  </>
                ) : (
                  <div className="cd-panel grid place-items-center p-10 text-center text-sm text-muted">
                    <div>
                      <div className="mx-auto mb-3 h-2.5 w-2.5 rounded-full border border-hair/60 bg-paper" />
                      Mesa libre. Ábrela para crear la sesión.
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </section>

        {/* Open an ad-hoc table by code (walk-in). QR type comes from the table; ad-hoc defaults to Fijo. */}
        {showNewTable && (
          <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4 backdrop-blur-sm">
            <div className="cd-panel w-full max-w-[420px] p-6">
              <h3 className="mb-1 font-display text-lg font-normal text-ink">Abrir mesa</h3>
              <p className="mb-3 text-sm text-muted">
                Escribe el número/nombre de la mesa y se abre la sesión. El tipo de QR ya lo definió el administrador en cada mesa; una mesa nueva improvisada queda como QR fijo.
              </p>
              <div className="grid gap-3">
                <label className="grid gap-1.5">
                  <span className="text-sm text-muted">Mesa</span>
                  <input value={newTableCode} onChange={(e) => setNewTableCode(e.target.value)} placeholder="Ej. 5 o Barra-1" className="cd-field" />
                </label>
                <div className="flex justify-end gap-2">
                  <button onClick={() => { setShowNewTable(false); setNewTableCode(""); }} className="cd-btn cd-ghost">Cancelar</button>
                  <button onClick={() => void handleOpenNewTable()} disabled={creatingTable || !newTableCode.trim()} className="cd-btn cd-primary">
                    {creatingTable ? "Abriendo…" : "Abrir mesa"}
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Dynamic QR just created → offer to print it for the table. */}
        {printQr && (
          <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4 backdrop-blur-sm">
            <div className="cd-panel grid w-full max-w-[380px] justify-items-center gap-3 p-6 text-center">
              <h3 className="font-display text-lg font-normal text-ink">Mesa {printQr.tableCode} abierta</h3>
              <p className="text-sm text-muted">Es una mesa con <strong>QR dinámico</strong>. Imprime el QR y déjalo en la mesa para que el cliente lo escanee.</p>
              {access[printQr.sessionId]?.dynamicQr ? (
                <div id="dynamic-qr-print" className="rounded-xl bg-paper p-3">
                  <QRCodeSVG value={access[printQr.sessionId]!.dynamicQr!.joinUrl} size={180} />
                </div>
              ) : (
                <p className="text-sm text-muted">Generando QR…</p>
              )}
              <div className="mt-1 flex w-full gap-2">
                <button onClick={() => setPrintQr(null)} className="cd-btn cd-ghost flex-1">Ahora no</button>
                <button onClick={() => window.print()} disabled={!access[printQr.sessionId]?.dynamicQr} className="cd-btn cd-primary flex-1">Imprimir</button>
              </div>
            </div>
          </div>
        )}

        {showTakeOrder && selected?.session && (
          <TakeOrderModal
            catalog={catalog}
            loading={catalogLoading}
            cart={cart}
            setCartQty={setCartQty}
            search={catalogSearch}
            setSearch={setCatalogSearch}
            busy={takingOrder}
            tableCode={selected.table.tableCode}
            onClose={() => setShowTakeOrder(false)}
            onSubmit={() => void handleTakeOrder()}
          />
        )}

        {showCloseShift && (
          <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4">
            <div className="card p-6" style={{ width: "100%", maxWidth: 460 }}>
              <h3 className="mt-0 font-display text-lg font-normal text-ink">Cerrar turno</h3>
              {tables.some((t) => t.table.hasActiveSession) && (
                <p className="text-[13px] text-amber-700">Hay mesas activas. El turno se cerrará igual.</p>
              )}
              <label style={{ display: "grid", gap: 6 }}>
                <span className="text-[13px] text-muted">Nota (opcional)</span>
                <textarea value={closeNote} onChange={(e) => setCloseNote(e.target.value)} maxLength={500} className="input" rows={3} />
              </label>
              <div style={{ display: "flex", justifyContent: "end", gap: 8, marginTop: 12 }}>
                <button onClick={() => setShowCloseShift(false)} className="btn-secondary">Cancelar</button>
                <button onClick={() => void handleCloseShift()} disabled={shiftBusy} className="btn-primary">
                  {shiftBusy ? "Cerrando..." : "Cerrar turno"}
                </button>
              </div>
            </div>
          </div>
        )}

        {shiftReport && (
          <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4">
            <div className="card p-6" style={{ width: "100%", maxWidth: 460 }}>
              <h3 className="mt-0 font-display text-lg font-normal text-ink">Reporte de caja</h3>
              <div style={{ display: "grid", gap: 6 }}>
                <Row label="Base inicial" value={formatEur(shiftReport.openingFloatCents)} />
                <Row label="Ventas" value={formatEur(shiftReport.totalSalesCents)} />
                <Row label="Propinas" value={formatEur(shiftReport.totalTipsCents)} />
                <Row label="Comisión plataforma" value={formatEur(shiftReport.totalCommissionCents)} />
                <Row label="Efectivo esperado en caja" value={formatEur(shiftReport.expectedCashCents)} strong />
                <Row label="# Pagos" value={String(shiftReport.paymentCount)} />
                <Row label="# Sesiones" value={String(shiftReport.sessionCount)} />
              </div>
              {shiftReport.byMethod.length > 0 && (
                <div style={{ marginTop: 10 }}>
                  <p className="text-[13px] text-muted" style={{ margin: "0 0 4px" }}>Por método</p>
                  {shiftReport.byMethod.map((m) => (
                    <Row key={m.method} label={m.method} value={`${formatEur(m.amountCents)} (+${formatEur(m.tipCents)} prop.) · ${m.count}`} />
                  ))}
                </div>
              )}
              <div style={{ display: "flex", justifyContent: "end", gap: 8, marginTop: 12 }}>
                <button onClick={() => window.print()} className="btn-secondary">Imprimir</button>
                <button onClick={() => setShiftReport(null)} className="btn-primary">Listo</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

/** Equal-split part amount: floor(subtotal/N), last pending part takes the remainder. */
function equalPart(subtotal: number, n: number, paidSplits: number, remaining: number): number {
  if (paidSplits + 1 >= n) return remaining; // last part settles the rest
  return Math.floor(subtotal / n);
}

function CashCharge({
  base,
  tip,
  received,
  setReceived,
  paying,
  onCharge,
  label,
}: {
  base: number;
  tip: number;
  received: string;
  setReceived: (v: string) => void;
  paying: boolean;
  onCharge: () => void;
  label: string;
}) {
  const total = base + tip;
  const receivedNum = eurosToCents(received);
  const change = receivedNum - total;
  return (
    <div className="grid gap-3 rounded-2xl border border-hair/40 bg-paper2 p-4">
      <div className="flex items-baseline justify-between">
        <span className="text-sm text-muted">A cobrar{tip > 0 ? " (con propina)" : ""}</span>
        <span className="cd-money text-2xl text-ink">{formatEur(total)}</span>
      </div>

      <input
        value={received}
        onChange={(e) => setReceived(e.target.value)}
        placeholder="Efectivo recibido (€)"
        inputMode="decimal"
        className="cd-field text-lg"
      />
      <div className="flex flex-wrap gap-2">
        {BILL_SHORTCUTS.map((b) => (
          <button key={b} onClick={() => setReceived(centsToEurosInput(b))} className="cd-chip flex-1">{formatEur(b)}</button>
        ))}
        <button onClick={() => setReceived(centsToEurosInput(total))} className="cd-chip flex-1">Exacto</button>
      </div>

      {received !== "" && (
        <div className={`flex items-center justify-between rounded-xl px-3 py-2.5 ${change >= 0 ? "bg-emerald-500/10 text-emerald-700" : "bg-amber-500/10 text-amber-700"}`}>
          <span className="text-sm font-semibold">{change >= 0 ? "Vuelto" : "Falta"}</span>
          <span className="cd-money text-lg">{change >= 0 ? formatEur(change) : formatEur(-change)}</span>
        </div>
      )}

      <button onClick={onCharge} disabled={paying || base <= 0} className="cd-btn cd-primary min-h-[56px] w-full text-base">
        {paying ? "Registrando…" : (<><Check size={18} weight="bold" /> {label}</>)}
      </button>
    </div>
  );
}

function TakeOrderModal({
  catalog,
  loading,
  cart,
  setCartQty,
  search,
  setSearch,
  busy,
  tableCode,
  onClose,
  onSubmit,
}: {
  catalog: PublicCatalogResponse | null;
  loading: boolean;
  cart: Record<string, number>;
  setCartQty: (itemId: string, qty: number) => void;
  search: string;
  setSearch: (v: string) => void;
  busy: boolean;
  tableCode: string;
  onClose: () => void;
  onSubmit: () => void;
}) {
  const itemsById = useMemo(() => {
    const map = new Map<string, PublicCatalogItemDto>();
    (catalog?.items ?? []).forEach((it) => map.set(it.id, it));
    return map;
  }, [catalog]);

  const term = search.trim().toLowerCase();
  const visibleItems = useMemo(
    () =>
      (catalog?.items ?? [])
        .filter((it) => it.available)
        .filter((it) => (term ? it.name.toLowerCase().includes(term) : true)),
    [catalog, term]
  );

  const categories = useMemo(
    () => [...(catalog?.categories ?? [])].sort((a, b) => a.displayOrder - b.displayOrder),
    [catalog]
  );

  const cartLines = Object.entries(cart).filter(([, qty]) => qty > 0);
  const total = cartLines.reduce((sum, [id, qty]) => sum + (itemsById.get(id)?.priceCents ?? 0) * qty, 0);
  const totalUnits = cartLines.reduce((sum, [, qty]) => sum + qty, 0);

  return (
    <div className="fixed inset-0 z-50 grid place-items-end bg-ink/40 p-0 backdrop-blur-sm sm:place-items-center sm:p-4">
      <div className="cd-panel grid w-full max-h-[92vh] grid-rows-[auto_auto_1fr_auto] gap-3 rounded-b-none p-4 sm:max-w-[720px] sm:rounded-2xl sm:p-6">
        <div className="flex items-center justify-between gap-3">
          <h3 className="font-display text-lg font-normal text-ink">Tomar pedido · Mesa {tableCode}</h3>
          <button onClick={onClose} className="cd-chip">Cerrar</button>
        </div>

        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar ítem…"
          className="cd-field"
        />

        <div className="grid gap-4 overflow-y-auto pr-1">
          {loading && <p className="text-sm text-muted">Cargando catálogo…</p>}
          {!loading && visibleItems.length === 0 && (
            <p className="text-sm text-muted">No hay ítems disponibles{term ? " para esa búsqueda" : ""}.</p>
          )}
          {!loading &&
            categories.map((cat) => {
              const catItems = visibleItems
                .filter((it) => it.categoryId === cat.id)
                .sort((a, b) => a.displayOrder - b.displayOrder);
              if (catItems.length === 0) return null;
              return (
                <div key={cat.id} className="grid gap-2">
                  <strong className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">{cat.name}</strong>
                  {catItems.map((it) => {
                    const qty = cart[it.id] ?? 0;
                    return (
                      <div key={it.id} className={`flex items-center justify-between gap-3 rounded-xl border p-3 ${qty > 0 ? "border-oro/50 bg-oro/[0.10]" : "border-hair/40 bg-paper2"}`}>
                        <div className="min-w-0">
                          <div className="truncate font-display text-base font-normal text-ink">{it.name}</div>
                          <div className="cd-money text-xs text-muted">{formatEur(it.priceCents)}</div>
                        </div>
                        <div className="flex items-center gap-2">
                          <button className="cd-btn cd-ghost h-11 w-11 !px-0 text-xl" onClick={() => setCartQty(it.id, qty - 1)} disabled={qty <= 0}>−</button>
                          <span className="w-7 text-center text-lg font-bold">{qty}</span>
                          <button className="cd-btn cd-primary h-11 w-11 !px-0 text-xl" onClick={() => setCartQty(it.id, qty + 1)}>+</button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              );
            })}
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-hair/40 pt-3">
          <div>
            <div className="text-xs text-muted">{totalUnits} ítem(s)</div>
            <strong className="cd-money text-xl text-ink">{formatEur(total)}</strong>
          </div>
          <button onClick={onSubmit} disabled={busy || cartLines.length === 0} className="cd-btn cd-primary min-h-[52px] px-6 text-base">
            {busy ? "Creando pedido…" : "Crear pedido"}
          </button>
        </div>
      </div>
    </div>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
      <span className="text-muted">{label}</span>
      <span className="tabular-nums text-ink" style={{ fontWeight: strong ? 700 : 400 }}>{value}</span>
    </div>
  );
}

function detailOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback;
}
