"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import * as signalR from "@microsoft/signalr";
import { ordersApi, sessionsApi, getStoredStaff, getToken, SESSIONS_HUB_URL } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { pitido } from "@/lib/avisos";
import type {
  FloorPlanFloor,
  FloorPlanTable,
  OrderSummaryDto,
  ActiveKdsOrderDto,
  WaiterCallDto,
} from "@mestobeach/api-client";
import ScaledFloorMap from "@/components/ScaledFloorMap";
import AvisosPanel from "@/components/AvisosPanel";
import ComandaCard from "./ComandaCard";
import ComandaDrawer from "./ComandaDrawer";
import { useDialog } from "@/components/Dialog";
import { FullscreenButton } from "@/components/FullscreenButton";

const REFRESH_MS = 6_000;

export default function PdaPage() {
  const router = useRouter();
  const dialog = useDialog();
  const { staff, setAuth } = useAuthStore();
  const [ready, setReady] = useState(false);

  const [floors, setFloors] = useState<FloorPlanFloor[]>([]);
  const [tables, setTables] = useState<FloorPlanTable[]>([]);
  const [activeOrders, setActiveOrders] = useState<ActiveKdsOrderDto[]>([]);
  const [activeFloorId, setActiveFloorId] = useState<string | null>(null);
  const [selected, setSelected] = useState<FloorPlanTable | null>(null);
  const [tableOrders, setTableOrders] = useState<OrderSummaryDto[]>([]);
  const [loadingTable, setLoadingTable] = useState(false);
  const [busyItem, setBusyItem] = useState<string | null>(null);
  // Contexto de la toma de comandas: sesión + participante ya resueltos para la mesa.
  const [drawer, setDrawer] = useState<{ sessionId: string; participantId: string; tableCode: string } | null>(null);
  const [openingComanda, setOpeningComanda] = useState(false);
  // Avisos de mesa (llamar al camarero, PIN, cuenta, "¿cuándo sale?").
  const [avisos, setAvisos] = useState<WaiterCallDto[]>([]);
  const [atendiendo, setAtendiendo] = useState<string | null>(null);

  const assignedFloorId = staff?.assignedFloorId ?? null;

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
    if (!roles.includes("PDA") && !roles.includes("Mesero") && !roles.includes("Admin")) {
      router.replace("/board");
      return;
    }
    setAuth(stored, token);
    setReady(true);
  }, [router, setAuth]);

  // ── Plano + tablero activo (para los puntos de "en espera"), sondeados ──────
  const load = useCallback(async () => {
    try {
      const staffAhora = getStoredStaff();
      const [plan, active, calls] = await Promise.all([
        sessionsApi.getFloorPlan(),
        ordersApi.listActiveOrders(),
        staffAhora ? sessionsApi.listWaiterCalls(staffAhora.tenantId) : Promise.resolve(null),
      ]);
      setFloors(plan.data.floors);
      setTables(plan.data.tables);
      setActiveOrders(active.data.data);
      if (calls) setAvisos(calls.data);
      setActiveFloorId((cur) => {
        if (cur) return cur;
        // El PDA arranca en SU sector; Administración, en el primero.
        if (assignedFloorId && plan.data.floors.some((f) => f.id === assignedFloorId))
          return assignedFloorId;
        return [...plan.data.floors].sort((a, b) => a.sortOrder - b.sortOrder)[0]?.id ?? null;
      });
      setSelected((cur) => (cur ? plan.data.tables.find((t) => t.id === cur.id) ?? null : null));
    } catch {
      /* ignore */
    }
  }, [assignedFloorId]);

  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!staff) return;
    void loadRef.current();
    const t = setInterval(() => void loadRef.current(), REFRESH_MS);
    return () => clearInterval(t);
  }, [staff]);

  // Mesas con platos EN ESPERA (para el punto del mapa). Cruza el tablero activo (por tableCode)
  // con el plano. Un pedido todo-retenido no sale del tablero, pero eso es un caso de borde: en
  // cuanto tiene un plato "ahora" el pedido está activo y sus retenidos viajan en él.
  const tableIdsWithHeld = useMemo(() => {
    const codesWithHeld = new Set(
      activeOrders
        .filter((o) => o.items.some((i) => i.status === "HELD"))
        .map((o) => o.tableCode)
        .filter(Boolean) as string[]
    );
    return new Set(tables.filter((t) => codesWithHeld.has(t.tableCode)).map((t) => t.id));
  }, [activeOrders, tables]);

  const sortedFloors = useMemo(
    () => [...floors].sort((a, b) => a.sortOrder - b.sortOrder),
    [floors]
  );
  // El PDA solo ve su sector; Administración ve todos.
  const shownFloors = assignedFloorId
    ? sortedFloors.filter((f) => f.id === assignedFloorId)
    : sortedFloors;
  const activeFloor = shownFloors.find((f) => f.id === activeFloorId) ?? shownFloors[0] ?? null;

  // Los avisos que le tocan a ESTE PDA. Sin filtro, un llamado de Playa sonaría en
  // el PDA de Salón y el camarero saldría a buscar una mesa que no es suya.
  //
  // La regla es descartar SOLO lo que se sabe de otro sector. Una mesa sin sector
  // asignado —o dada de alta al vuelo desde caja, que no está en el plano— se
  // muestra en todos: quedarse sin dueño no puede significar quedarse sin nadie
  // que la atienda, que es justo lo que pasaba filtrando por pertenencia.
  const avisosDelSector = useMemo(() => {
    const deOtroSector = new Set(
      tables
        .filter((t) => t.floorId && activeFloor && t.floorId !== activeFloor.id)
        .map((t) => t.tableCode)
    );
    return avisos.filter((a) => !deOtroSector.has(a.tableCode));
  }, [avisos, tables, activeFloor]);

  // ── Cargar las comandas de la mesa tocada ───────────────────────────────────
  const loadTableOrders = useCallback(async (table: FloorPlanTable) => {
    if (!table.activeSessionId) {
      setTableOrders([]);
      return;
    }
    setLoadingTable(true);
    try {
      const { data } = await ordersApi.listOrders(table.activeSessionId);
      setTableOrders(data.data);
    } catch {
      setTableOrders([]);
    } finally {
      setLoadingTable(false);
    }
  }, []);

  async function pick(table: FloorPlanTable) {
    setSelected(table);
    await loadTableOrders(table);
  }

  // Abrir la toma de comandas: el PDA se une a la mesa para tener un participante y, si la mesa
  // pide PIN, lo valida (puede leerlo por ser staff). Con eso ya puede enviar la comanda.
  async function abrirComanda(table: FloorPlanTable) {
    if (!staff) return;
    setOpeningComanda(true);
    const deviceId = `pda-${staff.id}`;
    try {
      const { data: join } = await sessionsApi.joinByTable({
        tenantId: staff.tenantId,
        tableCode: table.tableCode,
        deviceId,
      });
      if (join.pinRequired && !join.pinValidated) {
        try {
          const { data: pinDto } = await sessionsApi.getSessionPin(join.sessionId);
          await sessionsApi.validatePin(join.sessionId, {
            tenantId: staff.tenantId,
            participantId: join.participantId,
            deviceId,
            pin: pinDto.pin,
          });
        } catch {
          /* si no se puede validar, el envío avisará con PIN_REQUIRED */
        }
      }
      setDrawer({ sessionId: join.sessionId, participantId: join.participantId, tableCode: table.tableCode });
    } catch (err: unknown) {
      dialog.toast(
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
          "No se pudo abrir la comanda de la mesa.",
        { tone: "error" }
      );
    } finally {
      setOpeningComanda(false);
    }
  }

  // ── Avisos en vivo ────────────────────────────────────────────────────────
  // El sondeo de 6 s ya los traería, pero un llamado tiene que sonar al instante:
  // el comensal está con la mano levantada mientras tanto.
  useEffect(() => {
    if (!staff) return;
    const token = getToken();
    if (!token) return;

    const hub = new signalR.HubConnectionBuilder()
      .withUrl(`${SESSIONS_HUB_URL}?access_token=${token}`, {
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    hub.on("SessionEvent", (payload: unknown) => {
      const e = payload as { type?: string; sessionId?: string };
      if (!e.type) return;
      if (
        e.type === "WAITER_CALLED" || e.type === "PIN_REQUESTED" ||
        e.type === "BILL_REQUESTED" || e.type === "ETA_REQUESTED"
      ) {
        sessionsApi
          .listWaiterCalls(staff.tenantId)
          .then(({ data }) => setAvisos(data))
          .catch(() => {});
        // El ETA es una pregunta, no una urgencia: suena más grave y más bajo.
        pitido(e.type === "ETA_REQUESTED" ? "suave" : "aviso");
      }
      if (e.type === "WAITER_CALL_ACKNOWLEDGED" && e.sessionId) {
        // Otra pantalla lo atendió: desaparece de la mía sin esperar al sondeo.
        setAvisos((cur) => cur.filter((a) => a.sessionId !== e.sessionId));
      }
    });

    // Se guarda la promesa del start para que la limpieza haga stop() SOLO cuando
    // la negociación ya terminó: si no, el doble montaje de StrictMode la aborta.
    // JoinWaiterGroup mete la conexión en el grupo waiter-{tenant}: sin él no llega ningún aviso en vivo.
    const arranque = hub
      .start()
      .then(() => hub.invoke("JoinWaiterGroup", staff.tenantId))
      .catch(() => {});
    return () => {
      void arranque.then(() => hub.stop()).catch(() => {});
    };
  }, [staff]);

  async function atenderAviso(aviso: WaiterCallDto) {
    setAtendiendo(aviso.sessionId);
    // Optimista: el camarero ya va hacia la mesa, la tarjeta no debe seguir latiendo.
    setAvisos((cur) => cur.filter((a) => a.sessionId !== aviso.sessionId));
    try {
      await sessionsApi.acknowledgeWaiterCall(aviso.sessionId);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      // 404 = ya lo atendió otra pantalla: no es un fallo, la tarjeta sobra igual.
      if (status !== 404) {
        setAvisos((cur) => (cur.some((a) => a.sessionId === aviso.sessionId) ? cur : [...cur, aviso]));
        dialog.toast("No se pudo dar el aviso por atendido.", { tone: "error" });
      }
    } finally {
      setAtendiendo(null);
    }
  }

  async function pinDeLaMesa(sessionId: string): Promise<string | null> {
    try {
      const { data } = await sessionsApi.getSessionPin(sessionId);
      return data.pin;
    } catch (err: unknown) {
      // El caso normal de fallo no es una avería: la mesa dejó de pedir PIN entre
      // que el comensal lo pidió y el camarero llegó. Decir "no se pudo leer"
      // mandaría a buscar un problema que no existe.
      const detalle = (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail;
      dialog.toast(detalle ?? "No se pudo leer el PIN de la mesa.", { tone: "error" });
      return null;
    }
  }

  function irAMesa(tableCode: string) {
    const mesa = tables.find((t) => t.tableCode === tableCode);
    if (mesa) void pick(mesa);
  }

  async function marchar(itemId: string) {
    setBusyItem(itemId);
    try {
      await ordersApi.marchItem(itemId);
      if (selected) await loadTableOrders(selected);
      void loadRef.current();
    } catch (err: unknown) {
      dialog.toast(
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
          "No se pudo marchar el plato.",
        { tone: "error" }
      );
    } finally {
      setBusyItem(null);
    }
  }

  if (!ready || !staff) return null;

  return (
    <div className="flex h-full flex-col bg-canvas text-ink">
      <header className="flex items-center justify-between gap-3 border-b border-hair/40 bg-paper px-4 py-3">
        <div>
          <h1 className="font-display text-lg font-normal text-ink">
            PDA{activeFloor ? ` — ${activeFloor.name}` : ""}
          </h1>
          <p className="text-xs text-muted">Toca una mesa para ver y marchar su comanda</p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {/* Pestañas solo para Administración (el PDA está fijo a su sector) */}
          {!assignedFloorId && shownFloors.length > 1 && (
            <div className="flex flex-wrap gap-2">
              {shownFloors.map((f) => (
                <button
                  key={f.id}
                  onClick={() => setActiveFloorId(f.id)}
                  className={`min-h-[44px] rounded-full px-4 py-1.5 text-sm font-medium tracking-[0.03em] transition ${
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
          {/* La PDA va en tablet: sin barra lateral el plano de sala respira mucho más. */}
          <FullscreenButton />
        </div>
      </header>

      <AvisosPanel
        avisos={avisosDelSector}
        onAtender={(a) => void atenderAviso(a)}
        atendiendo={atendiendo}
        onIrAMesa={irAMesa}
        onVerPin={pinDeLaMesa}
        // Al escalar a urgente vuelve a sonar, una sola vez: si nadie ha ido en
        // tres minutos, la pantalla ya no basta.
        onEscalada={() => pitido("aviso")}
      />

      <div className="flex flex-1 flex-col gap-4 overflow-auto p-4 lg:flex-row">
        {/* Mapa del sector */}
        <div className="lg:flex-1">
          {activeFloor ? (
            <ScaledFloorMap
              floor={activeFloor}
              tables={tables}
              selectedId={selected?.id ?? null}
              onPick={pick}
              beach={/playa/i.test(activeFloor.name)}
              highlightIds={tableIdsWithHeld}
            />
          ) : (
            <p className="pt-12 text-center text-sm text-muted">
              Aún no hay plano de tu sector. Pídele al administrador que lo diseñe.
            </p>
          )}
        </div>

        {/* Panel de la mesa */}
        <aside className="lg:w-96 lg:shrink-0">
          {!selected ? (
            <div className="rounded-2xl border border-dashed border-hair/50 px-6 py-16 text-center text-sm text-muted">
              Toca una mesa del mapa para ver su comanda.
            </div>
          ) : (
            <div className="fixed inset-x-0 bottom-0 z-40 max-h-[85dvh] overflow-y-auto rounded-t-2xl border border-hair/50 bg-paper pb-[env(safe-area-inset-bottom)] shadow-warm-lg lg:static lg:z-auto lg:max-h-none lg:overflow-visible lg:rounded-2xl lg:pb-0 lg:shadow-warm">
              <div className="sticky top-0 z-10 flex items-center justify-between border-b border-hair/40 bg-paper px-4 py-3 lg:static">
                <div>
                  <p className="font-display text-xl text-ink">{selected.tableCode}</p>
                  <p className="text-xs text-muted">
                    {selected.hasActiveSession ? "Ocupada" : "Libre"}
                    {selected.guestCount != null ? ` · ${selected.guestCount} comensales` : ""}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  {selected.hasActiveSession && (
                    <button
                      onClick={() => abrirComanda(selected)}
                      disabled={openingComanda}
                      className="min-h-[44px] rounded-lg bg-brand-500 px-4 py-1.5 text-sm font-extrabold text-white hover:bg-brand-600 disabled:opacity-50"
                    >
                      {openingComanda ? "…" : "Nueva comanda"}
                    </button>
                  )}
                  <button
                    onClick={() => setSelected(null)}
                    className="min-h-[44px] rounded-lg border border-hair/55 px-4 py-1.5 text-sm font-bold text-muted hover:bg-paper2"
                  >
                    Cerrar
                  </button>
                </div>
              </div>

              <div className="space-y-3 p-4 lg:max-h-[60vh] lg:overflow-y-auto">
                {!selected.hasActiveSession ? (
                  <p className="py-8 text-center text-sm text-muted">
                    La mesa está libre. La Host la sienta antes de comandar.
                  </p>
                ) : loadingTable ? (
                  <p className="py-8 text-center text-sm text-muted">Cargando comanda…</p>
                ) : tableOrders.length === 0 ? (
                  <p className="py-8 text-center text-sm text-muted">
                    Sin comandas todavía en esta mesa.
                  </p>
                ) : (
                  tableOrders.map((order) => (
                    <ComandaCard
                      key={order.id}
                      order={order}
                      busyItem={busyItem}
                      onMarchar={marchar}
                    />
                  ))
                )}
              </div>
            </div>
          )}
        </aside>
      </div>

      {/* Toma de comandas */}
      {drawer && (
        <ComandaDrawer
          tenantId={staff.tenantId}
          sessionId={drawer.sessionId}
          participantId={drawer.participantId}
          tableCode={drawer.tableCode}
          onClose={() => setDrawer(null)}
          onSubmitted={() => {
            setDrawer(null);
            if (selected) void loadTableOrders(selected);
            void loadRef.current();
          }}
        />
      )}
    </div>
  );
}
