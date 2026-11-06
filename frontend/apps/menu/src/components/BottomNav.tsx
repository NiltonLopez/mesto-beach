"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";

export type Vista = "carta" | "carrito" | "cuenta";

/** El orden manda: es el que recorre el dedo al deslizar. */
export const VISTAS: { id: Vista; label: string }[] = [
  { id: "carta", label: "Carta" },
  { id: "carrito", label: "Carrito" },
  { id: "cuenta", label: "Cuenta" },
];

interface Props {
  vista: Vista;
  onChange: (v: Vista) => void;
  /** Nº de productos en el carrito; 0 esconde el globo. */
  badge: number;
}

/**
 * Barra flotante de navegación: una píldora centrada sobre el contenido con una
 * pastilla que se desliza hasta la pestaña activa.
 *
 * La pastilla se **mide** del botón activo en lugar de calcularse por índice: las
 * etiquetas tienen anchos distintos y la tipografía la elige el local, así que
 * cualquier reparto fijo se desalinearía en cuanto cambie una palabra o la fuente.
 */
export default function BottomNav({ vista, onChange, badge }: Props) {
  const dockRef = useRef<HTMLDivElement>(null);
  const botones = useRef<(HTMLButtonElement | null)[]>([]);
  const [pastilla, setPastilla] = useState<{ x: number; w: number; h: number } | null>(null);

  const activo = VISTAS.findIndex((v) => v.id === vista);

  // Late al recibir un plato: es el final del vuelo, y confirma que llegó.
  const [latido, setLatido] = useState(false);
  const anterior = useRef(badge);
  useEffect(() => {
    if (badge > anterior.current) {
      setLatido(true);
      const t = setTimeout(() => setLatido(false), 420);
      anterior.current = badge;
      return () => clearTimeout(t);
    }
    anterior.current = badge;
    return;
  }, [badge]);

  useLayoutEffect(() => {
    function medir() {
      const dock = dockRef.current;
      const btn = botones.current[activo];
      if (!dock || !btn) return setPastilla(null);
      const d = dock.getBoundingClientRect();
      const b = btn.getBoundingClientRect();
      setPastilla({ x: b.left - d.left - dock.clientLeft, w: b.width, h: b.height });
    }
    medir();
    window.addEventListener("resize", medir);
    // La fuente del local llega por la red: cuando entra, las etiquetas cambian
    // de ancho y la pastilla quedaría corrida si no se vuelve a medir.
    void document.fonts?.ready.then(medir).catch(() => {});
    return () => window.removeEventListener("resize", medir);
  }, [activo]);

  return (
    <nav
      className="pointer-events-none fixed inset-x-0 bottom-0 z-40 flex justify-center px-4"
      style={{ paddingBottom: "calc(env(safe-area-inset-bottom) + 0.625rem)" }}
    >
      <div
        ref={dockRef}
        className="pointer-events-auto relative inline-flex max-w-full items-center gap-1 rounded-full p-1.5 shadow-warm-lg backdrop-blur-md"
        style={{
          background: "color-mix(in srgb, var(--m-surface) 88%, transparent)",
          border: "1px solid var(--m-border)",
        }}
      >
        {pastilla && (
          <span
            aria-hidden
            className="absolute left-0 top-1.5 rounded-full"
            style={{
              transform: `translateX(${pastilla.x}px)`,
              width: pastilla.w,
              height: pastilla.h,
              background: "var(--m-accent)",
              transition: "transform 280ms cubic-bezier(.22,1,.36,1), width 280ms cubic-bezier(.22,1,.36,1)",
            }}
          />
        )}
        {VISTAS.map((v, i) => {
          const on = v.id === vista;
          return (
            <button
              key={v.id}
              ref={(el) => { botones.current[i] = el; }}
              // El vuelo del plato busca este punto para saber dónde aterrizar.
              {...(v.id === "carrito" ? { "data-destino-carrito": "" } : {})}
              onClick={() => onChange(v.id)}
              aria-current={on ? "page" : undefined}
              className="relative z-10 whitespace-nowrap rounded-full px-5 py-2 text-sm transition-colors"
              style={{
                color: on ? "var(--m-accent-ink)" : "var(--m-muted)",
                fontWeight: on ? 600 : 500,
              }}
            >
              {v.label}
              {v.id === "carrito" && badge > 0 && (
                <span
                  className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-bold"
                  style={{
                    transform: latido ? "scale(1.45)" : "scale(1)",
                    transition: "transform 200ms cubic-bezier(.34,1.56,.64,1)",
                    // Sobre la pastilla el acento no contrastaría: el globo se
                    // invierte cuando la pestaña está activa.
                    background: on ? "var(--m-accent-ink)" : "var(--m-accent)",
                    color: on ? "var(--m-accent)" : "var(--m-accent-ink)",
                  }}
                >
                  {badge}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </nav>
  );
}
