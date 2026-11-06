"use client";

import Link from "next/link";
import { BrandMark } from "@/components/BrandMark";
import { usePathname } from "next/navigation";
import type { StaffDto } from "@mestobeach/api-client";
import type { Icon } from "@phosphor-icons/react";
import { Armchair, ClockCounterClockwise, CookingPot, DeviceMobile, MapTrifold, Money, Path } from "@phosphor-icons/react";

// Navegación del staff gateada por rol. La misma persona puede tener uno o
// varios roles; el sidebar solo muestra las vistas a las que tiene permiso.
// Admin ve todas. Mapea cada vista al rol que la habilita.
// Iconos Phosphor en peso "regular": el staff trabaja a contraluz/al sol y
// necesita más cuerpo que el "light" elegante del admin (docs/marca.md).
type NavItem = { href: string; label: string; Icon: Icon; roles: string[] };

const NAV: NavItem[] = [
  { href: "/board", label: "Cocina", Icon: CookingPot, roles: ["KDS"] },
  { href: "/cashdesk", label: "Caja", Icon: Money, roles: ["Cajero"] },
  { href: "/host", label: "Recepción", Icon: Armchair, roles: ["Host"] },
  { href: "/pda", label: "PDA", Icon: DeviceMobile, roles: ["PDA"] },
  { href: "/salon", label: "Salón", Icon: MapTrifold, roles: ["Mesero"] },
  { href: "/runner", label: "Runner", Icon: Path, roles: ["Runner"] },
  { href: "/history", label: "Historial", Icon: ClockCounterClockwise, roles: ["Cajero", "Admin"] },
];

export function navItemsFor(roles: string[]): NavItem[] {
  const isAdmin = roles.includes("Admin");
  return NAV.filter((it) => isAdmin || it.roles.some((r) => roles.includes(r)));
}

export function StaffNav({ staff }: { staff: StaffDto }) {
  const pathname = usePathname();
  const roles = staff.roles ?? [];
  const items = navItemsFor(roles);

  return (
    <aside className="hidden w-52 flex-shrink-0 flex-col border-r border-hair/40 bg-paper md:flex">
      {/* Marca */}
      <div className="border-b border-hair/40 px-4 py-4">
        <BrandMark className="gap-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-oro" />
        <div className="mt-3 flex items-center gap-2.5 rounded-xl bg-oro/[0.08] px-3 py-2.5 ring-1 ring-hair/25">
          <div className="grid h-9 w-9 flex-shrink-0 place-items-center rounded-lg bg-brand-500 text-white shadow-warm-sm">
            <span className="font-display text-lg leading-none">P</span>
          </div>
          <div className="min-w-0">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-oro/80">Restaurante</p>
            <p className="truncate text-sm font-bold leading-tight text-ink" title={staff.tenantName ?? undefined}>
              {staff.tenantName ?? "—"}
            </p>
          </div>
        </div>
      </div>

      {/* Navegación por rol */}
      <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 py-3">
        {items.map(({ href, label, Icon }) => {
          const active = pathname === href || pathname.startsWith(href + "/");
          return (
            <Link
              key={href}
              href={href as never}
              className={`flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-semibold transition ${
                active
                  ? "bg-oro/[0.12] text-brand-700"
                  : "text-muted hover:bg-paper2 hover:text-ink"
              }`}
            >
              <Icon size={20} weight={active ? "fill" : "regular"} aria-hidden />
              {label}
            </Link>
          );
        })}
      </nav>

      <p className="px-4 pb-3 text-[10px] leading-tight text-muted/70">
        Tu acceso depende de tus roles asignados en este restaurante.
      </p>
    </aside>
  );
}
