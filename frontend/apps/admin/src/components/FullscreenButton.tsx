"use client";

import { CornersIn, CornersOut } from "@phosphor-icons/react";
import { useKioskToggle } from "@/lib/useKioskMode";

/**
 * Botón de pantalla completa del panel. Oculta la barra lateral de navegación y pide, en el mismo
 * gesto, el fullscreen del navegador (si lo niega, la barra se oculta igual). Se recuerda entre
 * recargas — ver `@/lib/useKioskMode`.
 *
 * Dos presentaciones del mismo botón:
 * - `nav` — un ítem más de la barra lateral, con el mismo lenguaje visual que los enlaces y
 *   "Cerrar sesión". Es el sitio natural: las páginas de admin no tienen cabecera propia.
 * - `flotante` — pastilla discreta arriba al centro. Es la SALIDA DE EMERGENCIA: con el modo
 *   activo la barra desaparece, y si el navegador negó el fullscreen nativo la tecla Esc no
 *   dispara nada, así que sin esta pastilla el usuario quedaría encerrado.
 *   Va CENTRADA, no en la esquina derecha: todas las cabeceras del panel son título a la
 *   izquierda y acción a la derecha (el "Guardar posiciones" de Mesas, sin ir más lejos), y ahí
 *   arriba también aterrizan los avisos del `Dialog` (`right-3 top-3`, z-70), que la taparían.
 *   El centro superior es hueco muerto en todas. Mismo criterio que el shell de staff.
 */
export function FullscreenButton({ variant = "nav" }: { variant?: "nav" | "flotante" }) {
  const [kiosk, toggle] = useKioskToggle();

  const titulo = kiosk
    ? "Salir de pantalla completa (Esc)"
    : "Pantalla completa — el panel ocupa toda la pantalla";

  if (variant === "flotante") {
    return (
      <button
        type="button"
        onClick={toggle}
        title={titulo}
        className="fixed left-1/2 top-4 z-50 flex -translate-x-1/2 items-center gap-1.5 rounded-full border border-hair/50 bg-paper/95 px-3 py-1.5 text-xs font-semibold text-muted shadow-warm backdrop-blur transition hover:bg-paper2 hover:text-ink"
      >
        <CornersIn size={15} weight="light" aria-hidden />
        Salir
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={toggle}
      title={titulo}
      aria-pressed={kiosk}
      className={`flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition ${
        kiosk
          ? "bg-oro/[0.10] text-brand-700"
          : "text-muted hover:bg-paper2 hover:text-ink"
      }`}
    >
      {kiosk ? (
        <CornersIn size={20} weight="light" aria-hidden />
      ) : (
        <CornersOut size={20} weight="light" aria-hidden />
      )}
      {kiosk ? "Salir" : "Pantalla completa"}
    </button>
  );
}
