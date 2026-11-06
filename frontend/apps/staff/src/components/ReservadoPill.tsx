/**
 * Pastilla «Reservado» (PRD-019, ADR-038): marca los pedidos con priority > 0, es decir, los que
 * nacieron en NIGHT desde una mesa RESERVADO. Hairline dorado y versalitas; no depende solo del color.
 */
export function ReservadoPill({ className = "" }: { className?: string }) {
  return (
    <span
      className={`inline-flex shrink-0 items-center rounded-full border border-oro/55 bg-oro/[0.10] px-2 py-0.5 text-xs font-semibold tracking-[0.12em] text-brand-700 [font-variant-caps:all-small-caps] ${className}`}
    >
      Reservado
    </span>
  );
}
