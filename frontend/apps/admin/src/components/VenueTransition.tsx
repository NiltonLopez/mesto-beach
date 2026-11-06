"use client";

import { createPortal } from "react-dom";
import { useEffect, useState } from "react";
import { useVenue } from "@/lib/venue";

/**
 * Transición «agujero negro» entre modos (PRD-019 RT-1915, ADR-039).
 *
 * CONTRATO (idéntico en las tres apps; packages/ui sigue vacío, deuda registrada en ADR-039):
 *  - Sin props. Lo monta <VenueProvider> y lee `phase` y `target` de useVenue().
 *  - Se pinta por portal en document.body para no heredar el transform de #venue-root.
 *  - La caída en espiral y la emersión del propio #venue-root las aplica el proveedor con las
 *    clases `venue-collapse` / `venue-hold` / `venue-emerge` (keyframes en globals.css). Aquí
 *    viven el horizonte de sucesos (el velo con anillo), el disco de acreción, el punto de la
 *    singularidad, la marca del modo de destino y la ráfaga de partículas.
 *  - `data-to` tiñe el anillo: oro hacia Night, luz de arena hacia Restaurante.
 *  - prefers-reduced-motion: solo fundido del contenido, sin velo (200 ms en total).
 *  - Un aviso `role="status"` fuera del velo anuncia el modo nuevo a lectores de pantalla.
 */

// Ráfaga determinista (sin Math.random: mismo render en cada montaje).
const PARTICLES = Array.from({ length: 28 }, (_, i) => ({
  angle: i * (360 / 28) + ((i * 37) % 11),
  dist: 26 + ((i * 53) % 24), // vmax
  size: 3 + ((i * 7) % 4), // px
  dur: 650 + ((i * 89) % 350), // ms
}));

export function VenueTransition() {
  const { phase, target, mode } = useVenue();
  const [host, setHost] = useState<HTMLElement | null>(null);
  const [announce, setAnnounce] = useState("");
  useEffect(() => setHost(document.body), []);
  useEffect(() => {
    if (phase === "emerge") setAnnounce(mode === "NIGHT" ? "Modo Night activado" : "Modo Restaurante activado");
  }, [phase, mode]);

  if (!host) return null;
  const toNight = (target ?? mode) === "NIGHT";

  return createPortal(
    <>
      <span role="status" className="sr-only">
        {announce}
      </span>
      {phase !== "idle" && (
        <div className="venue-overlay" data-phase={phase} data-to={toNight ? "night" : "restaurant"} aria-hidden="true">
          {phase !== "collapse" && (
            <>
              <span className="venue-disk" />
              <span className="venue-dot" />
              <p className="venue-word">
                {toNight ? (
                  <>
                    Mesto Beach{" "}
                    <em className="font-night text-[1.9em] font-normal normal-case italic tracking-normal">Night</em>
                  </>
                ) : (
                  <>Mesto Beach · Restaurante</>
                )}
              </p>
            </>
          )}
          {phase === "emerge" &&
            PARTICLES.map((p, i) => (
              <span
                key={i}
                className="venue-particle"
                style={{
                  ["--venue-angle" as string]: `${p.angle}deg`,
                  ["--venue-dist" as string]: `${p.dist}vmax`,
                  ["--venue-size" as string]: `${p.size}px`,
                  ["--venue-dur" as string]: `${p.dur}ms`,
                }}
              />
            ))}
        </div>
      )}
    </>,
    host,
  );
}
