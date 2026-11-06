"use client";

import { Fragment } from "react";
import type { RecorridoStep } from "@/lib/kds";

interface Props {
  steps: RecorridoStep[];
  /** Color sólido del paso actual: dorado si el plato es tuyo, el de la estación si es ajeno. */
  currentColor: string;
  /** Compacto para las tarjetas del tablero; suelto para el modo foco. */
  dense?: boolean;
}

/**
 * El viaje del plato en cuatro pastillas: esperando orden → en cola → en preparación → en el
 * pase. Los pasos cumplidos llevan su duración en verde tenue, el actual va sólido con el
 * tiempo en vivo y los que faltan quedan en gris. Es lo que responde "¿por qué tarda?".
 */
export default function KdsRecorrido({ steps, currentColor, dense = false }: Props) {
  return (
    <ol className="flex flex-wrap items-stretch gap-1" aria-label="Recorrido del plato">
      {steps.map((step, i) => (
        <Fragment key={step.key}>
          {i > 0 && (
            <li aria-hidden className="flex items-center">
              <span className="block h-px w-3 bg-hair/55" />
            </li>
          )}
          <li
            className={`min-w-[82px] rounded-xl px-2 ${dense ? "py-1" : "py-1.5"} text-center text-[11px] leading-tight ${
              step.state === "done"
                ? "bg-emerald-500/10 text-emerald-700"
                : step.state === "current"
                ? "text-white"
                : "bg-paper2 text-muted"
            }`}
            {...(step.state === "current"
              ? { style: { backgroundColor: currentColor } }
              : {})}
          >
            <span className="block truncate opacity-90">{step.label}</span>
            <span className="block font-bold tabular-nums">{step.time}</span>
          </li>
        </Fragment>
      ))}
    </ol>
  );
}
