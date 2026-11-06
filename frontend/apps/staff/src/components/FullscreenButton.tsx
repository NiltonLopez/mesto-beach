"use client";

import { CornersIn, CornersOut } from "@phosphor-icons/react";
import { useKioskToggle } from "@/lib/useKioskMode";

/**
 * Botón de pantalla completa, uno por cabecera de módulo.
 *
 * Nació en el tablero de cocina y ahora vive en todos los módulos del staff: la caja, la
 * recepción, la PDA, el runner, el salón y el historial también se usan en pantallas fijas o
 * tablets donde cada píxel cuenta. La lógica está en `useKioskToggle()` — aquí solo hay el
 * botón: icono, texto y los dos estados.
 *
 * Dos maneras de vestirlo:
 * - Por defecto se pinta con los tokens del sistema (paper / hair / oro / brand).
 * - `unstyled` apaga ese vestido para las cabeceras que traen su propia hoja de estilos
 *   (el salón con `salon.css`, el historial con `.btn-secondary`); ahí la clase la pone quien
 *   lo monta y el estado se lee del atributo `data-on`.
 */

// Las clases viven aquí, en `src/components`, que el `content` del tailwind.config sí rastrea.
const BASE =
  "inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm font-semibold transition";
const ENCENDIDO = "border-hair/50 bg-oro/[0.12] text-brand-700 hover:bg-oro/[0.18]";
const APAGADO = "border-hair/40 bg-paper2 text-muted hover:bg-paper";

export type FullscreenButtonProps = {
  /** Clases extra para encajar en la cabecera que lo hospeda. */
  className?: string;
  /** No pintes con Tailwind: la cabecera tiene su propia hoja (salón, historial). */
  unstyled?: boolean;
};

export function FullscreenButton({ className = "", unstyled = false }: FullscreenButtonProps) {
  const [kiosk, toggleKiosk] = useKioskToggle();

  const vestido = unstyled ? "" : `${BASE} ${kiosk ? ENCENDIDO : APAGADO}`;

  return (
    <button
      type="button"
      onClick={toggleKiosk}
      aria-pressed={kiosk}
      // Gancho para las hojas propias (`.salon-root .fsbtn[data-on="true"]`).
      data-on={kiosk ? "true" : "false"}
      title={
        kiosk
          ? "Salir de pantalla completa (Esc)"
          : "Pantalla completa — la vista ocupa toda la pantalla, sin barra lateral"
      }
      className={`${vestido} ${className}`.trim()}
    >
      {kiosk ? (
        <CornersIn size={16} weight="bold" aria-hidden />
      ) : (
        <CornersOut size={16} weight="bold" aria-hidden />
      )}
      {kiosk ? "Salir" : "Pantalla completa"}
    </button>
  );
}

export default FullscreenButton;
