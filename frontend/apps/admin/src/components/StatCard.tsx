interface Props {
  label: string;
  value: string;
  sub?: string;
  /** Icono Phosphor (peso "light"), no emoji — ver docs/marca.md. */
  icon?: React.ReactNode;
  accent?: "default" | "green" | "blue" | "orange";
}

const ACCENT_MAP = {
  default: "bg-paper2 text-muted",
  green: "bg-emerald-50 text-emerald-600",
  blue: "bg-sky-50 text-sky-600",
  orange: "bg-oro/[0.10] text-brand-700",
};

export default function StatCard({ label, value, sub, icon, accent = "default" }: Props) {
  return (
    <div className="card p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="flex-1 min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">{label}</p>
          <p className="mt-1 font-display text-3xl font-normal text-ink truncate">{value}</p>
          {sub && <p className="mt-0.5 text-xs text-muted">{sub}</p>}
        </div>
        {icon && (
          <div className={`flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl ${ACCENT_MAP[accent]}`}>
            {icon}
          </div>
        )}
      </div>
    </div>
  );
}
