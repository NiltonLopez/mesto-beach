import { create } from "zustand";
import type {
  StaffDto,
  OrderDetailDto,
  KdsStationDto,
  OrderItemStatus,
} from "@mestobeach/api-client";

// KdsOrder enriches OrderDetailDto with the extras the active-board query resolves:
// the table code, the readyAt stamp and the guest's name (KdsOrderDto, backend).
export type KdsOrder = OrderDetailDto & {
  tableCode?: string | null;
  readyAt?: string | null;
  customerName?: string | null;
  /** PRD-019 / ADR-038: > 0 = «Reservado» (nació en NIGHT desde una mesa RESERVADO). */
  priority?: number;
};

// ─── Auth Store ───────────────────────────────────────────────────────────────

interface AuthState {
  staff: StaffDto | null;
  token: string | null;
  setAuth: (staff: StaffDto, token: string) => void;
  clearAuth: () => void;
}

export const useAuthStore = create<AuthState>()((set) => ({
  staff: null,
  token: null,
  setAuth: (staff, token) => set({ staff, token }),
  clearAuth: () => set({ staff: null, token: null }),
}));

// ─── KDS Orders Store ─────────────────────────────────────────────────────────

interface KdsState {
  orders: KdsOrder[];
  stations: KdsStationDto[];
  trackedSessions: Set<string>;
  sessionTableMap: Map<string, string>; // sessionId → tableCode
  selectedStationId: string | null;
  soundEnabled: boolean;

  upsertOrder: (order: KdsOrder) => void;
  setOrders: (sessionId: string, orders: KdsOrder[]) => void;
  /** Authoritative full replace of the board (used by the KDS poll). */
  setAllOrders: (orders: KdsOrder[]) => void;
  /** Delta sync (P2): upsert `changed` and prune anything whose id is not in `activeIds`. */
  reconcileOrders: (changed: KdsOrder[], activeIds: string[]) => void;
  /** Feedback optimista del ciclo por ítem: mueve un plato de estado hasta que el sondeo confirma. */
  patchItemStatus: (orderId: string, itemId: string, status: OrderItemStatus) => void;
  removeOrder: (orderId: string) => void;
  setStations: (stations: KdsStationDto[]) => void;
  addTrackedSession: (sessionId: string) => void;
  setSessionTableMap: (map: Map<string, string>) => void;
  setSelectedStation: (stationId: string | null) => void;
  setSoundEnabled: (enabled: boolean) => void;
}

export const useKdsStore = create<KdsState>()((set) => ({
  orders: [],
  stations: [],
  trackedSessions: new Set(),
  sessionTableMap: new Map(),
  selectedStationId: null,
  soundEnabled: true,

  upsertOrder: (order) =>
    set((state) => {
      const idx = state.orders.findIndex((o) => o.id === order.id);
      if (idx >= 0) {
        const updated = [...state.orders];
        updated[idx] = order;
        return { orders: updated };
      }
      return { orders: [order, ...state.orders] };
    }),

  setOrders: (sessionId, orders) =>
    set((state) => {
      const others = state.orders.filter((o) => o.sessionId !== sessionId);
      return {
        orders: [...orders, ...others],
        trackedSessions: new Set([...state.trackedSessions, sessionId]),
      };
    }),

  setAllOrders: (orders) => set({ orders }),

  reconcileOrders: (changed, activeIds) =>
    set((state) => {
      const active = new Set(activeIds);
      const byId = new Map(changed.map((o) => [o.id, o]));
      // Keep existing active orders (apply incoming changes), drop those no longer active.
      const merged = state.orders
        .filter((o) => active.has(o.id))
        .map((o) => byId.get(o.id) ?? o);
      // Add brand-new active orders that weren't on the board yet.
      const existingIds = new Set(merged.map((o) => o.id));
      for (const o of changed) {
        if (active.has(o.id) && !existingIds.has(o.id)) merged.unshift(o);
      }
      return { orders: merged };
    }),

  patchItemStatus: (orderId, itemId, status) =>
    set((state) => ({
      orders: state.orders.map((o) =>
        o.id !== orderId
          ? o
          : { ...o, items: o.items.map((i) => (i.id === itemId ? { ...i, status } : i)) }
      ),
    })),

  removeOrder: (orderId) =>
    set((state) => ({
      orders: state.orders.filter((o) => o.id !== orderId),
    })),

  setStations: (stations) => set({ stations }),

  addTrackedSession: (sessionId) =>
    set((state) => ({
      trackedSessions: new Set([...state.trackedSessions, sessionId]),
    })),

  setSessionTableMap: (map) => set({ sessionTableMap: map }),

  setSelectedStation: (stationId) => set({ selectedStationId: stationId }),

  setSoundEnabled: (enabled) => set({ soundEnabled: enabled }),
}));
