"use client";

import { useLayoutEffect, useRef } from "react";

export interface Vuelo {
  /** Cambia en cada añadido: reinicia el vuelo aunque salga del mismo botón. */
  id: number;
  origen: { x: number; y: number; ancho: number; alto: number };
  imagen?: string | undefined;
}

interface Props {
  vuelo: Vuelo;
  onFin: () => void;
}

const DURACION_MS = 620;

/**
 * El plato "vuela" del botón que lo añadió hasta la pestaña del carrito.
 *
 * Se pinta FUERA del carrusel de ventanas: un ancestro con `transform` haría que
 * este `fixed` se anclara a él y el vuelo saldría de la pantalla.
 */
export default function VueloAlCarrito({ vuelo, onFin }: Props) {
  const ref = useRef<HTMLDivElement>(null);
  const finRef = useRef(onFin);
  finRef.current = onFin;

  useLayoutEffect(() => {
    const el = ref.current;
    const destino = document.querySelector("[data-destino-carrito]");
    if (!el || !destino) return finRef.current();

    const d = destino.getBoundingClientRect();
    const dx = d.left + d.width / 2 - (vuelo.origen.x + vuelo.origen.ancho / 2);
    const dy = d.top + d.height / 2 - (vuelo.origen.y + vuelo.origen.alto / 2);

    const anim = el.animate(
      [
        { transform: "translate(0px, 0px) scale(1)", opacity: 1 },
        // A media curva sube: describe un arco en vez de cortar en línea recta,
        // que es lo que hace que se lea como "se va al carrito" y no como un
        // elemento desplazándose.
        {
          transform: `translate(${dx * 0.5}px, ${dy * 0.5 - 90}px) scale(0.75)`,
          opacity: 0.95,
          offset: 0.5,
        },
        { transform: `translate(${dx}px, ${dy}px) scale(0.18)`, opacity: 0.35 },
      ],
      { duration: DURACION_MS, easing: "cubic-bezier(.4,.05,.35,1)", fill: "forwards" },
    );

    anim.onfinish = () => finRef.current();
    // Si la pestaña se va a segundo plano la animación se congela y `onfinish`
    // no llegaría: el reloj la retira igual y nada se queda pegado en pantalla.
    const red = setTimeout(() => finRef.current(), DURACION_MS + 400);
    return () => {
      clearTimeout(red);
      anim.cancel();
    };
  }, [vuelo.id]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div
      ref={ref}
      aria-hidden
      className="pointer-events-none fixed z-[60] overflow-hidden rounded-2xl shadow-warm-lg"
      style={{
        left: vuelo.origen.x,
        top: vuelo.origen.y,
        width: vuelo.origen.ancho,
        height: vuelo.origen.alto,
        background: "var(--m-accent)",
      }}
    >
      {vuelo.imagen && (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={vuelo.imagen} alt="" className="h-full w-full object-cover" />
      )}
    </div>
  );
}
