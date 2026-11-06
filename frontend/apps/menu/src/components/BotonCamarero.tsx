"use client";

import type { CSSProperties } from "react";

interface Props {
  /** Encogido: se queda la campana y una palabra, para ocupar lo mínimo. */
  compacto: boolean;
  /**
   * "sobre-foto" es la píldora de cristal oscuro para la portada —cualquier color
   * plano se pelea con una fotografía—; "solido" es el acento de la marca.
   */
  variante: "sobre-foto" | "solido";
  llamando: boolean;
  avisado: boolean;
  onClick: () => void;
  /** La posición la pone quien lo usa: aquí flota fijo, en el carrito va en línea. */
  className?: string;
  style?: CSSProperties;
}

/**
 * Campana dibujada a mano en vez de traer la librería de iconos: el menú del
 * comensal no usa iconos (`CLAUDE.md`), y para una sola figura no compensa
 * meterle a la carta un paquete entero.
 */
function Campana({ lado }: { lado: number }) {
  return (
    <svg
      width={lado}
      height={lado}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      className="shrink-0"
    >
      <path d="M12 2.6v1.2" />
      <path d="M6.2 9.4a5.8 5.8 0 0 1 11.6 0c0 4.1 1 5.5 1.7 6.3a.6.6 0 0 1-.45 1H4.95a.6.6 0 0 1-.45-1c.7-.8 1.7-2.2 1.7-6.3Z" />
      <path d="M10.1 19.4a2 2 0 0 0 3.8 0" />
    </svg>
  );
}

export default function BotonCamarero({
  compacto, variante, llamando, avisado, onClick, className, style,
}: Props) {
  const sobreFoto = variante === "sobre-foto" && !avisado;

  return (
    <button
      onClick={onClick}
      disabled={llamando || avisado}
      className={`inline-flex items-center rounded-full font-semibold shadow-warm-lg active:scale-95 disabled:opacity-100 ${className ?? ""}`}
      style={{
        gap: compacto ? "0.3rem" : "0.4rem",
        padding: compacto ? "0.3rem 0.7rem" : "0.6rem 1rem",
        fontSize: compacto ? "0.72rem" : "0.875rem",
        background: sobreFoto ? "rgba(38,27,17,.62)" : "var(--m-accent)",
        color: sobreFoto ? "#fff" : "var(--m-accent-ink)",
        border: sobreFoto ? "1px solid rgba(255,255,255,.25)" : "1px solid transparent",
        backdropFilter: sobreFoto ? "blur(8px)" : "none",
        // `top` va aquí, y no en el estilo de quien lo usa, porque ese `style` se
        // aplica después: si trajera su propia `transition` borraría esta entera
        // y el botón cambiaría de tamaño y de color a saltos.
        transition:
          "top 240ms cubic-bezier(.22,1,.36,1), gap 240ms cubic-bezier(.22,1,.36,1), padding 240ms cubic-bezier(.22,1,.36,1), font-size 240ms cubic-bezier(.22,1,.36,1), background 240ms ease, color 240ms ease",
        ...style,
      }}
    >
      {/* La campana solo aparece encogido, que es cuando el texto ya no lo explica.
          Se queda montada y se estrecha a cero para que entre y salga con el
          mismo gesto que el botón, en vez de aparecer de golpe a mitad. */}
      <span
        className="inline-flex items-center overflow-hidden"
        style={{
          width: compacto ? 14 : 0,
          opacity: compacto ? 1 : 0,
          transition: "width 240ms cubic-bezier(.22,1,.36,1), opacity 200ms ease",
        }}
      >
        <Campana lado={14} />
      </span>
      <span>
        {avisado ? "Ya viene" : llamando ? "Llamando…" : compacto ? "Camarero" : "Llamar al camarero"}
      </span>
    </button>
  );
}
