/**
 * Estado vacío de la carta: sin iconos, a puro filete dorado y serif.
 * El menú del comensal no lleva iconografía — la tipografía carga el peso
 * (decisión de marca, docs/marca.md).
 */
export default function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="px-6 py-12 text-center">
      <div className="mx-auto mb-4 flex w-32 items-center gap-2.5" style={{ color: "var(--m-accent)" }}>
        <span className="h-px flex-1" style={{ background: "currentColor", opacity: 0.45 }} />
        <span className="text-[11px] leading-none">✦</span>
        <span className="h-px flex-1" style={{ background: "currentColor", opacity: 0.45 }} />
      </div>
      <p className="m-head text-lg" style={{ color: "var(--m-text)" }}>{title}</p>
      {hint && <p className="mt-1.5 text-sm" style={{ color: "var(--m-muted)" }}>{hint}</p>}
    </div>
  );
}
