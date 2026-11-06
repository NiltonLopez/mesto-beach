"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { PublicCatalogItemDto, PublicCatalogResponse } from "@mestobeach/api-client";
import { fontHead } from "@/lib/menuBranding";
import ProductCard from "@/components/ProductCard";

interface Props {
  catalog: PublicCatalogResponse;
  tableCode?: string | undefined;
  displayName?: string | undefined;
  loyaltyPoints?: number | undefined;
  cartQty: (catalogItemId: string) => number;
  needsCustomize: (item: PublicCatalogItemDto) => boolean;
  onOpenDetail: (item: PublicCatalogItemDto) => void;
  onAdd: (item: PublicCatalogItemDto, origen: DOMRect) => void;
  onRemove: (item: PublicCatalogItemDto) => void;
  /**
   * Avisa de cuándo la barra de categorías se ha pegado arriba. El botón de
   * camarero lo pinta el armazón (fuera del carrusel) y necesita saberlo para
   * encogerse y colocarse sobre ella.
   */
  onBarraPegada: (pegada: boolean) => void;
  /** Hay un pedido anterior de este participante que se puede volver a pedir. */
  canRepeatLast?: boolean;
  onRepeatLast?: () => void;
}

interface Seccion {
  id: string;
  nombre: string;
  items: PublicCatalogItemDto[];
}

/** Holgura bajo la cabecera pegada a partir de la cual una sección "manda". */
const HOLGURA_ACTIVA = 12;

/**
 * Banda que la barra de categorías deja libre sobre ella al pegarse arriba: es el
 * sitio del botón de camarero. Se reserva subiendo el `top` de la barra —no con
 * relleno— porque cambiar su alto al pegarse reflotaría toda la carta y el
 * contenido daría un salto justo en el momento de la transición.
 */
export const BANDA_SUPERIOR = "calc(env(safe-area-inset-top) + 2.5rem)";

export default function CartaPane({
  catalog, tableCode, displayName, loyaltyPoints,
  cartQty, needsCustomize, onOpenDetail, onAdd, onRemove, onBarraPegada,
  canRepeatLast = false, onRepeatLast,
}: Props) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const stripRef = useRef<HTMLDivElement>(null);
  /** La barra pegada de categorías: su alto es lo que hay que descontar al saltar. */
  const barraRef = useRef<HTMLDivElement>(null);
  const seccionRefs = useRef(new Map<string, HTMLElement>());
  const chipRefs = useRef(new Map<string, HTMLButtonElement>());
  const activaRef = useRef<string | null>(null);
  // Mientras un salto se anima, el espía barrería todas las secciones intermedias
  // y la etiqueta acabaría en la equivocada: se calla hasta que el scroll se posa.
  const espiaEnPausa = useRef(false);

  const [activa, setActiva] = useState<string | null>(null);
  // La barra de categorías tocando su tope: con ella pegada, el botón de camarero
  // se encoge y se posa en la banda de encima. Lo pinta el armazón, así que aquí
  // solo hace falta el ref para no re-renderizar la carta en cada scroll.
  const pegadaRef = useRef(false);
  const avisar = useRef(onBarraPegada);
  avisar.current = onBarraPegada;
  // Si la portada no carga (URL de blob inalcanzable, red…) se cae al degradado
  // en vez de dejar el icono de imagen rota.
  const [heroOk, setHeroOk] = useState(true);

  const branding = catalog.branding;
  const nombreLocal = branding?.displayName?.trim() || "Menú";

  // Toda la carta en un solo hilo, partida por categorías y en el orden del local.
  const secciones = useMemo<Seccion[]>(() => {
    const items = [...catalog.items].sort((a, b) => a.displayOrder - b.displayOrder);
    return [...catalog.categories]
      .sort((a, b) => a.displayOrder - b.displayOrder)
      .map((c) => ({
        id: c.id,
        nombre: c.name,
        items: items.filter((i) => i.categoryId === c.id),
      }))
      .filter((s) => s.items.length > 0);
  }, [catalog]);

  // ── Espía de scroll ───────────────────────────────────────────────────────
  // Mantiene el chip encendido apuntando a la sección que hay bajo el borde de
  // arriba. Lee solo los rects de las (pocas) secciones dentro de un rAF y toca
  // el estado únicamente cuando la sección cambia: recorrer la carta entera no
  // cuesta ni un re-render de más.
  useEffect(() => {
    const c = scrollRef.current;
    if (!c || secciones.length === 0) return;

    const calcular = () => {
      const cTop = c.getBoundingClientRect().top;
      const barra = barraRef.current;
      const tope = barra ? parseFloat(getComputedStyle(barra).top) || 0 : 0;
      // La sección que manda es la última que ya ha pasado por debajo de la
      // cabecera pegada. El límite se saca de la propia cabecera (banda + barra)
      // en vez de fijar un número: con una constante, cambiar el alto de la barra
      // deja el chip apuntando a la categoría anterior sin que nada falle a la vista.
      const limite = tope + (barra?.offsetHeight ?? 0) + HOLGURA_ACTIVA;

      let actual = secciones[0]?.id ?? null;
      for (const s of secciones) {
        const el = seccionRefs.current.get(s.id);
        if (!el) continue;
        if (el.getBoundingClientRect().top - cTop <= limite) actual = s.id;
        else break;
      }
      // Al llegar al fondo manda la última sección aunque su encabezado no haya
      // cruzado el umbral: si es corta nunca lo cruzaría, y su chip no se
      // encendería jamás por mucho que se siga arrastrando.
      if (c.scrollTop + c.clientHeight >= c.scrollHeight - 4) {
        actual = secciones[secciones.length - 1]?.id ?? actual;
      }
      if (actual && actual !== activaRef.current) {
        activaRef.current = actual;
        setActiva(actual);
      }

      // "Pegada" es que ya ha llegado a su tope. El tope se lee del estilo
      // calculado en vez de repetir aquí la cuenta de la banda: así solo hay un
      // sitio donde cambiarla, y el `env()` del móvil ya viene resuelto en px.
      if (barra) {
        const tocando = barra.getBoundingClientRect().top - cTop <= tope + 0.5;
        if (tocando !== pegadaRef.current) {
          pegadaRef.current = tocando;
          avisar.current(tocando);
        }
      }
    };

    let raf = 0;
    let reposo: ReturnType<typeof setTimeout> | undefined;
    const alHacerScroll = () => {
      if (reposo) clearTimeout(reposo);
      reposo = setTimeout(() => { espiaEnPausa.current = false; calcular(); }, 160);
      if (espiaEnPausa.current || raf) return;
      raf = requestAnimationFrame(() => { raf = 0; calcular(); });
    };

    c.addEventListener("scroll", alHacerScroll, { passive: true });
    calcular();
    return () => {
      c.removeEventListener("scroll", alHacerScroll);
      if (raf) cancelAnimationFrame(raf);
      if (reposo) clearTimeout(reposo);
    };
  }, [secciones]);

  // El chip encendido se centra solo en su tira. Se mueve `scrollLeft` a mano en
  // vez de usar scrollIntoView: ese arrastraría también el scroll vertical de la
  // carta y daría un salto en medio de la lectura.
  useEffect(() => {
    const tira = stripRef.current;
    const chip = activa ? chipRefs.current.get(activa) : null;
    if (!tira || !chip) return;
    const destino = chip.offsetLeft - tira.clientWidth / 2 + chip.clientWidth / 2;
    tira.scrollTo({ left: Math.max(0, destino), behavior: "smooth" });
  }, [activa]);

  /** Pulsar una categoría lleva la carta hasta ella; nunca esconde el resto. */
  function saltarA(id: string) {
    activaRef.current = id;
    setActiva(id);
    const c = scrollRef.current;
    const el = seccionRefs.current.get(id);
    if (!c || !el) return;
    espiaEnPausa.current = true;
    const top = el.getBoundingClientRect().top - c.getBoundingClientRect().top + c.scrollTop;
    // Sin descontar la barra pegada —y la banda que deja libre encima— el
    // encabezado aterriza detrás de ella y no se ve: el comensal salta a una
    // categoría y le recibe media rejilla.
    const barra = barraRef.current;
    const alto = (barra?.offsetHeight ?? 0) + (barra ? parseFloat(getComputedStyle(barra).top) || 0 : 0);
    c.scrollTo({ top: Math.max(0, top - alto - 8), behavior: "smooth" });
  }

  const totalProductos = secciones.reduce((s, x) => s + x.items.length, 0);

  return (
    <div ref={scrollRef} className="h-full overflow-y-auto overscroll-contain">
      {/* Portada de marca */}
      <div className="relative h-60 overflow-hidden sm:h-72">
        {branding?.heroUrl && heroOk ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={branding.heroUrl}
            alt=""
            onError={() => setHeroOk(false)}
            className="absolute inset-0 h-full w-full object-cover"
            // El recorte se ancla en la parte baja de la foto: es donde está el
            // local (hamacas, sombrillas, pasarela), no el cielo vacío.
            style={{ objectPosition: "center 72%" }}
          />
        ) : (
          <div
            className="absolute inset-0"
            style={{ background: "linear-gradient(135deg, var(--m-accent), color-mix(in srgb, var(--m-accent) 55%, #000))" }}
          />
        )}
        {/* Manto de arenilla: velo del acento de marca sobre la foto, para que el hero
            hable el mismo idioma cromático que el resto de la carta. */}
        <div className="absolute inset-0" style={{ background: "color-mix(in srgb, var(--m-accent) 30%, transparent)" }} />
        {/* Scrims cálidos (marrón de marca, no negro): dan cuerpo al centro para que
            la firma se lea. Sin enfriar la imagen. */}
        <div className="absolute inset-0" style={{ background: "linear-gradient(to bottom, rgba(38,27,17,.42), rgba(38,27,17,.14) 34%, rgba(38,27,17,.24))" }} />

        {/* Marca centrada sobre la foto: la firma manda, el eslogan la acompaña. */}
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center px-6 text-center">
          {branding?.logoUrl ? (
            <>
              {/* La firma viene en tinta oscura (versión "dark", pensada para fondos
                  claros). La pasamos a blanco por filtro para que respire sobre la
                  foto, sin depender de que nos den una segunda versión del archivo. */}
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={branding.logoUrl}
                alt={nombreLocal}
                className="h-16 w-auto max-w-[74%] object-contain sm:h-20"
                style={{ filter: "brightness(0) invert(1) drop-shadow(0 2px 12px rgba(38,27,17,.55))" }}
              />
              <h1 className="sr-only">{nombreLocal}</h1>
            </>
          ) : (
            <h1
              className="m-head line-clamp-3 text-4xl font-normal leading-[1.08] text-white drop-shadow-md [overflow-wrap:anywhere]"
              style={{ fontFamily: fontHead(branding) }}
            >
              {nombreLocal}
            </h1>
          )}
          {branding?.slogan && (
            <p className="mt-3.5 max-w-[85%] text-[11px] font-medium uppercase leading-relaxed tracking-[0.24em] text-white/85 drop-shadow sm:text-xs">
              {branding.slogan}
            </p>
          )}
        </div>
      </div>

      {/* Línea de contexto */}
      <div className="flex items-center justify-between px-4 py-2.5 text-xs" style={{ color: "var(--m-muted)" }}>
        <span className="truncate">
          {tableCode ? `Mesa ${tableCode}` : "Menú"}
          {displayName ? ` · Hola, ${displayName}` : ""}
        </span>
        {loyaltyPoints !== undefined && (
          <span className="m-accent-text flex-shrink-0 font-semibold">✦ {loyaltyPoints} pts</span>
        )}
      </div>

      {canRepeatLast && onRepeatLast && (
        <div className="px-4 pb-2">
          <button
            type="button"
            onClick={onRepeatLast}
            className="m-chip w-full rounded-full px-4 py-2 text-[13px] font-medium tracking-[0.03em] transition active:scale-[.99]"
          >
            Repetir lo último
          </button>
        </div>
      )}

      {/* Tira de categorías: se queda pegada arriba y sigue a la lectura */}
      <div
        ref={barraRef}
        className="sticky z-30 shadow-warm-sm"
        style={{ top: BANDA_SUPERIOR, background: "var(--m-bg)" }}
      >
        <div className="relative">
          <div
            ref={stripRef}
            // La tira se desliza en horizontal por su cuenta: si el gesto llegara
            // al carrusel de vistas, arrastrar categorías cambiaría de pestaña.
            data-sin-deslizar
            className="scrollbar-none flex gap-2 overflow-x-auto px-4 pb-3 pr-12"
          >
            {secciones.map((s) => (
              <button
                key={s.id}
                ref={(el) => { if (el) chipRefs.current.set(s.id, el); else chipRefs.current.delete(s.id); }}
                onClick={() => saltarA(s.id)}
                className={`flex-shrink-0 rounded-full px-4 py-1.5 text-[13px] font-medium tracking-[0.03em] transition ${
                  activa === s.id ? "m-chip-on shadow-warm-sm" : "m-chip"
                }`}
              >
                {s.nombre}
              </button>
            ))}
          </div>
          {/* Difuminado: avisa de que hay más categorías a la derecha. */}
          <div
            className="pointer-events-none absolute bottom-3 right-0 top-0 w-12"
            style={{ background: "linear-gradient(to left, var(--m-bg), transparent)" }}
          />
        </div>
      </div>

      {/* La carta entera, sección a sección */}
      {secciones.map((s) => (
        <section
          key={s.id}
          ref={(el) => { if (el) seccionRefs.current.set(s.id, el); else seccionRefs.current.delete(s.id); }}
        >
          <div className="flex items-baseline justify-between px-4 pt-5">
            <h2 className="m-head text-2xl" style={{ color: "var(--m-text)", fontFamily: fontHead(branding) }}>
              {s.nombre}
            </h2>
            <span className="text-xs" style={{ color: "var(--m-muted)" }}>{s.items.length} productos</span>
          </div>
          <div className="grid grid-cols-2 gap-3 p-4 sm:grid-cols-3 lg:grid-cols-4">
            {s.items.map((item) => (
              <ProductCard
                key={item.id}
                item={item}
                cartQty={cartQty(item.id)}
                customizable={needsCustomize(item)}
                onOpen={() => onOpenDetail(item)}
                onAdd={(origen) => onAdd(item, origen)}
                onRemove={() => onRemove(item)}
              />
            ))}
          </div>
        </section>
      ))}

      {totalProductos === 0 && (
        <p className="px-4 pt-10 text-center text-sm" style={{ color: "var(--m-muted)" }}>
          Todavía no hay productos en la carta.
        </p>
      )}

      {/* Hueco para que el último producto no quede bajo la barra ni bajo el botón
          de camarero, que van flotando por encima (los pinta el armazón). */}
      <div style={{ height: "calc(env(safe-area-inset-bottom) + 8.5rem)" }} />
    </div>
  );
}
