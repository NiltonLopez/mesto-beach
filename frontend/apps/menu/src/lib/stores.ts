import { create } from "zustand";
import { createJSONStorage, persist } from "zustand/middleware";

export interface CartItem {
  /** Unique per distinct configuration (item + selected options + notes). */
  lineId: string;
  catalogItemId: string;
  name: string;
  /** Unit price including modifier deltas. */
  priceCents: number;
  quantity: number;
  notes?: string;
  selectedOptionIds: string[];
  /** Human-readable modifier summary, e.g. "3/4 · sin Cebolla · + Michelada". */
  modifiersSummary?: string;
}

export type CartLineInput = Omit<CartItem, "quantity" | "lineId">;

function makeLineId(catalogItemId: string, selectedOptionIds: string[], notes?: string): string {
  const opts = [...selectedOptionIds].sort().join(",");
  return `${catalogItemId}::${opts}::${notes ?? ""}`;
}

interface CartState {
  items: CartItem[];
  sessionId: string | null;
  /** Observación libre para toda la comanda (alergias, "todo para llevar"…). */
  observacion: string;
  /** Add a configured line; identical configurations merge by incrementing quantity. */
  addLine: (item: CartLineInput, quantity?: number) => void;
  removeLine: (lineId: string) => void;
  updateQuantity: (lineId: string, quantity: number) => void;
  setNotes: (lineId: string, notes: string) => void;
  setObservacion: (observacion: string) => void;
  clear: () => void;
  setSessionId: (id: string) => void;
  totalCents: () => number;
  totalItems: () => number;
}

/**
 * Carrito del comensal, persistido en `sessionStorage`.
 *
 * Sin esto el carrito vivía solo en memoria y **se perdía al recargar la página**:
 * en el móvil basta con que el navegador descarte la pestaña en segundo plano para
 * que alguien pierda lo que llevaba elegido.
 *
 * Se guarda en `sessionStorage`, no en `localStorage`, por coherencia con el resto
 * del menú —la sesión y el JWT del comensal viven ahí— y porque el carrito no debe
 * sobrevivir al cierre de la pestaña: la siguiente persona que escanee ese QR en el
 * mismo dispositivo empieza de cero.
 */
export const useCartStore = create<CartState>()(
  persist(
    (set, get) => ({
      items: [],
      sessionId: null,
      observacion: "",

      addLine: (item, quantity = 1) =>
        set((state) => {
          const lineId = makeLineId(item.catalogItemId, item.selectedOptionIds, item.notes);
          const existing = state.items.find((i) => i.lineId === lineId);
          if (existing) {
            return {
              items: state.items.map((i) =>
                i.lineId === lineId ? { ...i, quantity: i.quantity + quantity } : i
              ),
            };
          }
          return { items: [...state.items, { ...item, lineId, quantity }] };
        }),

      removeLine: (lineId) =>
        set((state) => ({ items: state.items.filter((i) => i.lineId !== lineId) })),

      updateQuantity: (lineId, quantity) =>
        set((state) => ({
          items:
            quantity <= 0
              ? state.items.filter((i) => i.lineId !== lineId)
              : state.items.map((i) => (i.lineId === lineId ? { ...i, quantity } : i)),
        })),

      setNotes: (lineId, notes) =>
        set((state) => ({
          items: state.items.map((i) => (i.lineId === lineId ? { ...i, notes } : i)),
        })),

      setObservacion: (observacion) => set({ observacion }),

      clear: () => set({ items: [], observacion: "" }),
      setSessionId: (id) => set({ sessionId: id }),

      totalCents: () => get().items.reduce((sum, i) => sum + i.priceCents * i.quantity, 0),
      totalItems: () => get().items.reduce((sum, i) => sum + i.quantity, 0),
    }),
    {
      name: "mestobeach_cart",
      storage: createJSONStorage(() => sessionStorage),
      // Solo los datos: las funciones se recrean al cargar el módulo.
      partialize: (state) => ({
        items: state.items,
        observacion: state.observacion,
        sessionId: state.sessionId,
      }),
      // Next renderiza este componente también en el servidor, donde no hay
      // sessionStorage. Si zustand rehidratara al crear el store, el primer
      // render del cliente ya traería el carrito y no coincidiría con el HTML
      // del servidor (error de hidratación). Por eso se rehidrata a mano desde
      // un efecto: ver `useHydrateCart`.
      skipHydration: true,
    }
  )
);

/**
 * Carga el carrito guardado y lo ata a la sesión actual.
 *
 * Devuelve `true` cuando ya ha terminado, para que la interfaz pueda esperar y no
 * enseñe "carrito vacío" durante el parpadeo de la carga.
 *
 * Si lo guardado pertenece a **otra** sesión —la misma pestaña reutilizada tras
 * escanear el QR de otra mesa— se descarta: heredar el carrito de la mesa anterior
 * es peor que empezar de cero.
 */
export function hydrateCart(sessionId: string): Promise<void> {
  return Promise.resolve(useCartStore.persist.rehydrate()).then(() => {
    const state = useCartStore.getState();
    if (state.sessionId !== sessionId) {
      state.clear();
      state.setSessionId(sessionId);
    }
  });
}
