/**
 * Marca «◆ Mesto Beach» + «Night» (PRD-019 RT-1914, ADR-039).
 *
 * CONTRATO (idéntico en las tres apps; packages/ui sigue vacío, deuda registrada en ADR-039):
 *  - props: `night?`, `size?`, `className?`.
 *  - `className` lleva la tipografía y el layout del sitio donde se pone (tamaño, tracking,
 *    color, `gap-*`, `justify-*`); BrandMark solo aporta el rombo y las palabras.
 *  - `night` sin definir: «Night» sale solo cuando `<html data-venue="night">` (variante `dark:`);
 *    `true` / `false` lo fuerzan (vista previa, impresos).
 *  - «Night» es Cormorant Garamond 400 itálica REAL (`font-night`, variable --font-night que
 *    carga app/layout.tsx), dorada. Gilda Display nunca se inclina (PRD-019 D-12, RF-1972).
 *  - `size` es solo el tamaño del rombo: sm 9px · md 13px · lg 16px.
 */
type BrandMarkSize = "sm" | "md" | "lg";

interface BrandMarkProps {
  night?: boolean;
  size?: BrandMarkSize;
  className?: string;
}

const DIAMOND: Record<BrandMarkSize, string> = {
  sm: "text-[9px]",
  md: "text-[13px]",
  lg: "text-[16px]",
};

export function BrandMark({ night, size = "md", className }: BrandMarkProps) {
  const nightVisibility = night === undefined ? "hidden dark:inline" : "inline";
  return (
    <div className={`flex items-center ${className ?? ""}`}>
      <span aria-hidden className={`${DIAMOND[size]} leading-none text-oro`}>◆</span>
      <span>
        Mesto Beach
        {night !== false && (
          <>
            {" "}
            <em
              className={`${nightVisibility} font-night text-[1.3em] font-normal normal-case italic leading-none tracking-normal text-oro`}
            >
              Night
            </em>
          </>
        )}
      </span>
    </div>
  );
}
