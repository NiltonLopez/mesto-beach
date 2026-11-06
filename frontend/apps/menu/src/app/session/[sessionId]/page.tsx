"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { PublicCatalogItemDto, PublicCatalogResponse, OrderItemDto } from "@mestobeach/api-client";
import {
  catalogApi, ordersApi, sessionsApi, getStoredSession,
  isIdentified, getDisplayName, getStoredCustomer,
} from "@/lib/api";
import { parseApiError, TABLE_CLOSED_MESSAGE, VENUE_UNAVAILABLE_MESSAGE } from "@/lib/errors";
import { useVenue, type VenueMode } from "@/lib/venue";
import { useCartStore, hydrateCart } from "@/lib/stores";
import { applyMenuBranding, resetMenuBranding } from "@/lib/menuBranding";
import BottomNav, { VISTAS, type Vista } from "@/components/BottomNav";
import CartaPane, { BANDA_SUPERIOR } from "@/components/CartaPane";
import BotonCamarero from "@/components/BotonCamarero";
import CartPane from "@/components/CartPane";
import CuentaPane from "@/components/CuentaPane";
import ProductDetailModal from "@/components/ProductDetailModal";
import PinModal from "@/components/PinModal";
import IdentifyScreen from "@/components/IdentifyScreen";
import EmptyState from "@/components/EmptyState";
import OrderSentAnimation from "@/components/OrderSentAnimation";
import VueloAlCarrito, { type Vuelo } from "@/components/VueloAlCarrito";
import { useDialog } from "@/components/Dialog";

/** Un producto está pedible si la carta del modo vigente lo trae (visible) y no está agotado. */
function orderable(catalog: PublicCatalogResponse): Map<string, PublicCatalogItemDto> {
  return new Map(catalog.items.filter((i) => i.available).map((i) => [i.id, i]));
}

interface Props {
  params: { sessionId: string };
}

/** Recorrido mínimo del dedo (px) para que el gesto cambie de ventana. */
const UMBRAL_DESLIZAR = 56;
/** A partir de aquí el gesto se declara horizontal o vertical, y ya no cambia. */
const UMBRAL_EJE = 10;

function esVista(v: string | null): v is Vista {
  return VISTAS.some((x) => x.id === v);
}

export default function SessionPage({ params }: Props) {
  const { sessionId } = params;
  const router = useRouter();
  const dialog = useDialog();
  const { mode, setMode, setReloader } = useVenue();

  const [catalog, setCatalog] = useState<PublicCatalogResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [vista, setVista] = useState<Vista>("carta");
  const [ordering, setOrdering] = useState(false);
  // Confirmación del pedido: mientras `confirmando` está activo se ve la
  // pantalla con la ventana para deshacer, y el pedido aún no ha salido.
  const [confirmando, setConfirmando] = useState(false);
  const [sendStatus, setSendStatus] = useState<"idle" | "pending" | "success">("idle");
  const [orderedItems, setOrderedItems] = useState<{ name: string; quantity: number }[]>([]);
  const [pinRetry, setPinRetry] = useState(false);
  const [calling, setCalling] = useState(false);
  const [callSent, setCallSent] = useState(false);
  const [pinModalOpen, setPinModalOpen] = useState(false);
  const [identified, setIdentified] = useState(true); // se asume true hasta comprobar (evita el parpadeo)
  const [detailItem, setDetailItem] = useState<PublicCatalogItemDto | null>(null);
  // La barra de categorías pegada arriba: con ella pegada, el botón de camarero
  // se encoge y se posa sobre ella en vez de flotar sobre la portada.
  const [barraPegada, setBarraPegada] = useState(false);
  const [lastOrderItems, setLastOrderItems] = useState<OrderItemDto[]>([]);
  const [vuelo, setVuelo] = useState<Vuelo | null>(null);
  const vueloId = useRef(0);

  const { items, addLine, updateQuantity, totalItems, clear } = useCartStore();

  const session = getStoredSession();
  const indice = VISTAS.findIndex((v) => v.id === vista);

  // ── Gesto lateral entre ventanas ──────────────────────────────────────────
  const pistaRef = useRef<HTMLDivElement>(null);
  const gesto = useRef<{ x: number; y: number; eje: "x" | "y" | null } | null>(null);
  // Desplazamiento en píxeles mientras el dedo está abajo: la pista sigue la mano
  // y solo al soltar decide si salta de ventana. El valor bueno vive en el ref y
  // el estado es solo para pintar: al soltar hay que leer el de ESTE gesto, y en
  // un golpe rápido el último `touchmove` aún no ha re-renderizado nada.
  const arrastreRef = useRef(0);
  const [arrastre, setArrastre] = useState(0);

  function moverPista(px: number) {
    arrastreRef.current = px;
    setArrastre(px);
  }

  function alTocar(e: React.TouchEvent) {
    // Modales y pantallas superpuestas mandan sobre el carrusel.
    if (confirmando || detailItem || pinModalOpen || !identified) return;
    // Hay tiras que ya se deslizan solas (las categorías): ahí el gesto es suyo.
    if ((e.target as HTMLElement).closest?.("[data-sin-deslizar]")) return;
    const t = e.touches[0];
    if (!t) return;
    gesto.current = { x: t.clientX, y: t.clientY, eje: null };
  }

  function alMover(e: React.TouchEvent) {
    const g = gesto.current;
    const t = e.touches[0];
    if (!g || !t) return;
    const dx = t.clientX - g.x;
    const dy = t.clientY - g.y;
    if (g.eje === null) {
      if (Math.abs(dx) < UMBRAL_EJE && Math.abs(dy) < UMBRAL_EJE) return;
      g.eje = Math.abs(dx) > Math.abs(dy) ? "x" : "y";
    }
    if (g.eje !== "x") return;
    // Resistencia en los extremos: se nota que no hay nada más allá.
    const tope = (indice === 0 && dx > 0) || (indice === VISTAS.length - 1 && dx < 0);
    moverPista(tope ? dx * 0.28 : dx);
  }

  function alSoltar() {
    const g = gesto.current;
    gesto.current = null;
    const dx = arrastreRef.current;
    moverPista(0);
    if (!g || g.eje !== "x" || Math.abs(dx) < UMBRAL_DESLIZAR) return;
    const destino = VISTAS[dx < 0 ? indice + 1 : indice - 1];
    if (destino) irA(destino.id);
  }

  /** Cambia de ventana y deja la elección en la URL, para que "atrás" funcione. */
  function irA(v: Vista) {
    setVista(v);
    if (typeof window === "undefined") return;
    const url = new URL(window.location.href);
    if (v === "carta") url.searchParams.delete("vista");
    else url.searchParams.set("vista", v);
    window.history.replaceState(null, "", url.toString());
  }

  // La ventana inicial puede venir en la URL (`?vista=cuenta`): es como aterrizan
  // el redirect de /orders y los enlaces que se compartieron antes.
  useEffect(() => {
    if (typeof window === "undefined") return;
    const v = new URLSearchParams(window.location.search).get("vista");
    if (esVista(v)) setVista(v);
  }, []);

  // Comprueba si este dispositivo ya se ha identificado en la sesión.
  useEffect(() => {
    setIdentified(isIdentified(sessionId));
  }, [sessionId]);

  // Recupera el carrito guardado al recargar la página. Va en un efecto —y no al
  // crear el store— para que el primer render del cliente coincida con el HTML
  // que pintó el servidor, donde no existe sessionStorage.
  useEffect(() => {
    void hydrateCart(sessionId);
  }, [sessionId]);

  useEffect(() => {
    if (!session) {
      router.replace("/join");
      return;
    }
    catalogApi
      .getPublicCatalog(session.tenantId)
      .then(({ data }) => {
        setCatalog(data);
        // La carta del servidor manda: el modo local se alinea sin animación en el arranque.
        if (data.venueMode && data.venueMode !== modeRef.current) setMode(data.venueMode);
        pruneCart(data, false);
      })
      .catch(() => setError("No se pudo cargar el menú."))
      .finally(() => setLoading(false));
  }, [session?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  /** Quita del carrito lo que la carta ya no ofrece; avisa con un toast de lo que se fue. */
  function pruneCart(c: PublicCatalogResponse, announce: boolean) {
    const ok = orderable(c);
    const gone = useCartStore.getState().items.filter((i) => !ok.has(i.catalogItemId));
    if (gone.length === 0) return;
    const ids = new Set(gone.map((g) => g.lineId));
    useCartStore.setState((st) => ({ items: st.items.filter((i) => !ids.has(i.lineId)) }));
    const names = [...new Set(gone.map((g) => g.name))].join(", ");
    dialog.toast(
      announce
        ? `Cambió la carta y quitamos de tu carrito: ${names}.`
        : `Quitamos de tu carrito lo que ya no está disponible: ${names}.`,
      { tone: "info", duration: 6000 },
    );
  }

  // Cambio de modo del local (RF-191x): lo dispara VenueProvider dentro de la transición, bajo el
  // velo. Recarga la carta, aplica el branding que ya resolvió el servidor y limpia el carrito.
  const modeRef = useRef<VenueMode>(mode);
  modeRef.current = mode;
  const reloadForMode = useRef<(next: VenueMode) => Promise<void>>(async () => {});
  reloadForMode.current = async () => {
    const tenantId = getStoredSession()?.tenantId;
    if (!tenantId) return;
    const { data } = await catalogApi.getPublicCatalog(tenantId);
    setCatalog(data);
    applyMenuBranding(data.branding);
    pruneCart(data, true);
  };
  useEffect(() => {
    setReloader((next) => reloadForMode.current(next));
    return () => setReloader(null);
  }, [setReloader]);

  // Último pedido de este participante, para «Repetir lo último».
  async function refreshLastOrder() {
    const me = getStoredSession();
    if (!me) return;
    try {
      const { data } = await ordersApi.listOrders(sessionId);
      const mine = data.data
        .filter((o) => o.participantId === me.participantId && o.status !== "CANCELLED")
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
      setLastOrderItems(mine?.items ?? []);
    } catch {
      /* sin historial: el botón simplemente no aparece */
    }
  }
  useEffect(() => {
    void refreshLastOrder();
  }, [sessionId]); // eslint-disable-line react-hooks/exhaustive-deps

  /** Re-añade el último pedido: solo lo visible y disponible; el resto se cuenta en el toast. */
  function repeatLast() {
    if (!catalog || lastOrderItems.length === 0) return;
    const ok = orderable(catalog);
    const added: string[] = [];
    const skipped: string[] = [];
    for (const it of lastOrderItems) {
      const item = ok.get(it.catalogItemId);
      // Los productos con opciones elegibles no se reconstruyen: el pedido solo guarda el
      // resumen en texto, no los ids de las opciones. Hay que elegirlas de nuevo.
      if (!item || item.modifierGroups.length > 0) {
        skipped.push(it.itemNameSnapshot);
        continue;
      }
      addLine(
        {
          catalogItemId: item.id,
          name: item.name,
          priceCents: item.priceCents,
          selectedOptionIds: [],
          ...(it.notes && item.allowNotes ? { notes: it.notes } : {}),
        },
        it.quantity,
      );
      added.push(item.name);
    }
    if (added.length === 0) {
      dialog.toast("Nada de tu último pedido se puede repetir ahora mismo.", { tone: "error" });
    } else if (skipped.length > 0) {
      dialog.toast(`Añadido a tu carrito. No se pudo repetir: ${[...new Set(skipped)].join(", ")}.`, {
        tone: "info",
        duration: 6000,
      });
    } else {
      dialog.toast("Tu último pedido está en el carrito.", { tone: "success" });
    }
  }

  // Aplica la marca del local (colores, tipografía, fondo, logo/portada).
  useEffect(() => {
    if (catalog?.branding) applyMenuBranding(catalog.branding);
  }, [catalog?.branding]);
  useEffect(() => () => resetMenuBranding(), []);

  /**
   * Abre la pantalla de confirmación. Aquí NO se envía nada: el comensal tiene
   * una ventana para cancelar o lanzarlo antes de tiempo, y quien manda la
   * comanda es `commitOrder`.
   */
  function beginOrder() {
    if (!session || items.length === 0 || confirmando) return;
    // De noche el nombre es obligatorio antes del primer pedido (el servidor responde NAME_REQUIRED).
    if (mode === "NIGHT" && !getDisplayName()) {
      setIdentified(false);
      return;
    }
    // Los nombres se capturan ahora porque el carrito se vacía al enviar y la
    // pantalla los sigue mostrando mientras dura la animación.
    setOrderedItems(items.map((i) => ({ name: i.name, quantity: i.quantity })));
    setSendStatus("idle");
    setConfirmando(true);
  }

  async function commitOrder() {
    if (!session || sendStatus !== "idle") return;
    setSendStatus("pending");
    setOrdering(true);
    // Se lee del store (no del render) para que sobreviva al reintento por PIN.
    const observacion = useCartStore.getState().observacion.trim();
    try {
      await ordersApi.createOrder({
        sessionId,
        participantId: session.participantId,
        ...(observacion ? { observacion } : {}),
        items: items.map((i) => ({
          catalogItemId: i.catalogItemId,
          quantity: i.quantity,
          ...(i.notes !== undefined ? { notes: i.notes } : {}),
          ...(i.selectedOptionIds.length > 0 ? { selectedOptionIds: i.selectedOptionIds } : {}),
        })),
      });
      clear();
      setPinModalOpen(false);
      setSendStatus("success");
      void refreshLastOrder();
    } catch (err: unknown) {
      const problem = parseApiError(err, "Error al enviar el pedido.");
      const detail = problem.detail;
      // La mesa exige el PIN compartido (QR fijo). Se cierra la confirmación y se
      // pide el PIN; al validarlo se reintenta el envío, ya sin ventana para
      // deshacer: el comensal ya esperó la suya antes de que saltara el PIN.
      setConfirmando(false);
      setSendStatus("idle");
      if (problem.code === "PIN_REQUIRED" || /PIN_REQUIRED/i.test(detail)) {
        setPinModalOpen(true);
      } else if (problem.code === "NAME_REQUIRED") {
        setIdentified(false);
      } else if (problem.code === "ITEM_NOT_IN_MODE") {
        const gone = new Set(problem.itemIds);
        useCartStore.setState((st) => ({ items: st.items.filter((i) => !gone.has(i.catalogItemId)) }));
        irA("carrito");
        dialog.toast(detail, { tone: "error", duration: 6000 });
      } else if (problem.code === "TABLE_CLOSED_IN_MODE") {
        irA("carrito");
        dialog.toast(TABLE_CLOSED_MESSAGE, { tone: "error" });
      } else if (problem.code === "VENUE_MODE_UNAVAILABLE") {
        irA("carrito");
        dialog.toast(VENUE_UNAVAILABLE_MESSAGE, { tone: "error" });
      } else {
        irA("carrito");
        dialog.toast(detail, { tone: "error" });
      }
    } finally {
      setOrdering(false);
    }
  }

  /** El comensal se echó atrás dentro de la ventana: nada llegó a cocina. */
  function cancelOrder() {
    setConfirmando(false);
    setSendStatus("idle");
    irA("carrito");
    dialog.toast("Pedido cancelado. No se ha enviado nada.", { tone: "info" });
  }

  /** Reintento tras validar el PIN: sin ventana, directo al envío. */
  function retryAfterPin() {
    setSendStatus("idle");
    setPinRetry(true);
    setConfirmando(true);
  }

  async function handleCallWaiter() {
    if (!session || calling || callSent) return;
    setCalling(true);
    try {
      await sessionsApi.callWaiter(sessionId);
      setCallSent(true);
      dialog.toast("¡Camarero avisado! Estará contigo en un momento.", { tone: "success" });
      // Se puede volver a llamar a los 30 s (el backend es idempotente).
      setTimeout(() => setCallSent(false), 30_000);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      dialog.toast(
        status === 404
          ? "Esta mesa ya no tiene una sesión activa. Vuelve a escanear el QR."
          : "No se pudo enviar el llamado. Intenta de nuevo.",
        { tone: "error" },
      );
    } finally {
      setCalling(false);
    }
  }

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-10 w-10 animate-spin rounded-full border-4 border-hair/50 border-t-brand-500" />
      </div>
    );
  }

  if (error || !catalog) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center p-6">
        <EmptyState title="Menú no disponible" {...(error ? { hint: error } : {})} />
      </div>
    );
  }

  // Cantidad total del producto sumando todas sus líneas configuradas.
  const cartQty = (catalogItemId: string) =>
    items.filter((i) => i.catalogItemId === catalogItemId).reduce((s, i) => s + i.quantity, 0);

  const needsCustomize = (item: PublicCatalogItemDto) =>
    (item.modifierGroups?.length ?? 0) > 0 || item.allowNotes;

  // El "+" redondo de una tarjeta: añade directo los productos simples y abre el
  // detalle de los configurables (para que se elijan las opciones obligatorias).
  function handleQuickAdd(item: PublicCatalogItemDto, origen: DOMRect) {
    if (needsCustomize(item)) {
      setDetailItem(item);
    } else {
      addLine({
        catalogItemId: item.id,
        name: item.name,
        priceCents: item.priceCents,
        selectedOptionIds: [],
      });
      lanzarVuelo(origen, item);
    }
  }

  /**
   * Lanza el plato desde el botón que lo añadió hasta la pestaña del carrito.
   * Se salta cuando el sistema pide menos movimiento: la animación es adorno, y
   * el plato entra en el carrito igual.
   */
  function lanzarVuelo(origen: DOMRect, item: PublicCatalogItemDto) {
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;
    vueloId.current += 1;
    setVuelo({
      id: vueloId.current,
      origen: { x: origen.left, y: origen.top, ancho: origen.width, alto: origen.height },
      imagen: item.imageUrl ?? item.images?.[0],
    });
  }

  function handleRemoveProduct(item: PublicCatalogItemDto) {
    // Descuenta de la última línea configurada de ese producto.
    const line = [...items].reverse().find((i) => i.catalogItemId === item.id);
    if (line) updateQuantity(line.lineId, line.quantity - 1);
  }

  const puntos = getStoredCustomer()?.loyaltyPoints;

  return (
    <div
      className="flex h-[100dvh] flex-col overflow-hidden"
      style={{ background: "var(--m-bg)", color: "var(--m-text)" }}
    >
      {/* Carrusel de ventanas. `touch-action: pan-y` deja el scroll vertical al
          navegador y nos reserva el horizontal, que es el que movemos a mano. */}
      <div
        ref={pistaRef}
        className="flex min-h-0 flex-1"
        style={{
          width: `${VISTAS.length * 100}%`,
          touchAction: "pan-y",
          transform: `translateX(calc(${(-indice * 100) / VISTAS.length}% + ${arrastre}px))`,
          transition: arrastre !== 0 ? "none" : "transform 320ms cubic-bezier(.22,1,.36,1)",
        }}
        onTouchStart={alTocar}
        onTouchMove={alMover}
        onTouchEnd={alSoltar}
        onTouchCancel={alSoltar}
      >
        <div className="h-full min-w-0" style={{ width: `${100 / VISTAS.length}%` }}>
          <CartaPane
            catalog={catalog}
            tableCode={session?.tableCode}
            displayName={getDisplayName() || undefined}
            loyaltyPoints={puntos}
            cartQty={cartQty}
            needsCustomize={needsCustomize}
            onOpenDetail={setDetailItem}
            onAdd={handleQuickAdd}
            onRemove={handleRemoveProduct}
            onBarraPegada={setBarraPegada}
            canRepeatLast={lastOrderItems.length > 0}
            onRepeatLast={repeatLast}
          />
        </div>

        <div className="h-full min-w-0" style={{ width: `${100 / VISTAS.length}%` }}>
          <CartPane
            onOrder={beginOrder}
            loading={ordering}
            onIrALaCarta={() => irA("carta")}
            onCallWaiter={handleCallWaiter}
            calling={calling}
            callSent={callSent}
          />
        </div>

        <div className="h-full min-w-0" style={{ width: `${100 / VISTAS.length}%` }}>
          <CuentaPane sessionId={sessionId} activo={vista === "cuenta"} onIrALaCarta={() => irA("carta")} />
        </div>
      </div>

      {/* Llamar al camarero: arriba a la derecha mientras se ve la portada y, en
          cuanto la barra de categorías se pega arriba, encogido sobre ella para
          no comerse la rejilla de platos.
          Va FUERA de la pista a propósito: un ancestro con `transform` hace que
          `position: fixed` se ancle a él, y dentro del carrusel el botón acabaría
          al final de las tres ventanas en vez de en la pantalla. */}
      {/* Fondo de la banda: sin él se vería la rejilla de platos desfilando por
          detrás del botón, porque la barra de categorías se pega por debajo. */}
      {vista === "carta" && (
        <div
          aria-hidden
          className="pointer-events-none fixed inset-x-0 top-0 z-30 transition-opacity duration-200"
          style={{
            height: BANDA_SUPERIOR,
            background: "var(--m-bg)",
            opacity: barraPegada ? 1 : 0,
          }}
        />
      )}

      {vista === "carta" && (
        <BotonCamarero
          compacto={barraPegada}
          variante={barraPegada ? "solido" : "sobre-foto"}
          llamando={calling}
          avisado={callSent}
          onClick={handleCallWaiter}
          className="fixed right-3 z-40"
          style={{
            top: barraPegada
              ? "calc(env(safe-area-inset-top) + 0.35rem)"
              : "calc(env(safe-area-inset-top) + 0.75rem)",
          }}
        />
      )}

      {vuelo && <VueloAlCarrito vuelo={vuelo} onFin={() => setVuelo(null)} />}

      <BottomNav vista={vista} onChange={irA} badge={totalItems()} />

      {detailItem && (
        <ProductDetailModal
          item={detailItem}
          onClose={() => setDetailItem(null)}
          onAdd={(line, quantity, origen) => {
            addLine(line, quantity);
            lanzarVuelo(origen, detailItem);
            setDetailItem(null);
          }}
        />
      )}

      {confirmando && (
        <OrderSentAnimation
          status={sendStatus}
          items={orderedItems}
          immediate={pinRetry}
          onCommit={commitOrder}
          onCancel={cancelOrder}
          onFinished={() => {
            setConfirmando(false);
            setPinRetry(false);
            irA("cuenta");
          }}
        />
      )}

      {session && (
        <PinModal
          open={pinModalOpen}
          sessionId={sessionId}
          tenantId={session.tenantId}
          participantId={session.participantId}
          {...(session.tableCode ? { tableCode: session.tableCode } : {})}
          onValidated={retryAfterPin}
          onClose={() => setPinModalOpen(false)}
        />
      )}

      {session && !identified && (
        <IdentifyScreen
          sessionId={sessionId}
          participantId={session.participantId}
          onDone={() => setIdentified(true)}
        />
      )}
    </div>
  );
}
