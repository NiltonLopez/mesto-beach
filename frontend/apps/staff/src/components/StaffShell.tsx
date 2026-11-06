"use client";

import { BrandMark } from "@/components/BrandMark";
import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { StaffDto } from "@mestobeach/api-client";
import { StaffNav, navItemsFor } from "@/components/StaffNav";
import { StaffFooter } from "@/components/StaffFooter";
import { useKioskToggle } from "@/lib/useKioskMode";
import { CornersIn, List, X } from "@phosphor-icons/react";

/**
 * Shell responsive compartido por todas las vistas del staff.
 * - Escritorio/tablet (md+): sidebar fijo a la izquierda (`StaffNav`).
 * - Celular: barra superior con botón de menú que abre un drawer con la misma navegación.
 * En ambos casos, el footer compartido (restaurante + roles + logout) queda abajo.
 *
 * En **modo pantalla completa** el shell se aparta entero: la pantalla de cocina cuelga de la
 * pared y no se navega desde ella, así que la barra y el pie serían ancho robado a las
 * comandas. Lo mismo vale para la caja, el salón o la PDA en una tablet fija.
 */
export function StaffShell({ staff, children }: { staff: StaffDto; children: React.ReactNode }) {
  const pathname = usePathname();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const items = navItemsFor(staff.roles ?? []);
  const [kiosk, toggleKiosk] = useKioskToggle();
  const closeRef = useRef<HTMLButtonElement>(null);

  // Cajón móvil: Esc lo cierra y el foco inicial va al botón de cierre.
  useEffect(() => {
    if (!drawerOpen) return;
    closeRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setDrawerOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  if (kiosk) {
    return (
      <div className="flex h-[100dvh] overflow-hidden bg-canvas text-ink">
        <main className="min-h-0 flex-1 overflow-y-auto">{children}</main>

        {/* Salida de emergencia. Sin barra lateral, la única puerta es el botón de la cabecera
            del módulo… que puede haberse ido con el scroll, y si el navegador negó el
            fullscreen nativo la tecla Esc tampoco dispara nada. Esta pastilla siempre está.
            Va centrada arriba —el hueco muerto de todas las cabeceras, entre el título a la
            izquierda y los controles a la derecha— y translúcida hasta que la buscas. */}
        <button
          type="button"
          onClick={toggleKiosk}
          title="Salir de pantalla completa"
          style={{ top: "calc(env(safe-area-inset-top, 0px) + 0.5rem)" }}
          className="fixed left-1/2 z-[60] flex -translate-x-1/2 items-center gap-1.5 rounded-full border border-hair/50 bg-paper/85 px-3 py-1 text-xs font-semibold text-muted opacity-50 shadow-warm-sm backdrop-blur transition hover:bg-paper hover:text-ink hover:opacity-100 focus-visible:opacity-100"
        >
          <CornersIn size={14} weight="bold" aria-hidden />
          Salir
        </button>
      </div>
    );
  }

  return (
    <div className="flex h-[100dvh] overflow-hidden bg-canvas text-ink">
      {/* Sidebar (md+) */}
      <StaffNav staff={staff} />

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Barra superior móvil */}
        <div className="flex items-center gap-3 border-b border-hair/40 bg-paper px-4 py-2.5 pt-[max(0.625rem,env(safe-area-inset-top))] md:hidden">
          <button
            onClick={() => setDrawerOpen(true)}
            aria-label="Abrir menú"
            className="grid h-11 w-11 place-items-center rounded-lg border border-hair/50 bg-paper2 text-lg text-ink"
          >
            <List size={20} />
          </button>
          <BrandMark className="gap-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-oro" />
          <span className="ml-auto truncate text-sm font-semibold text-muted">{staff.tenantName ?? ""}</span>
        </div>

        <main className="min-h-0 flex-1 overflow-y-auto">{children}</main>
        <StaffFooter staff={staff} />
      </div>

      {/* Drawer móvil */}
      {/* Siempre montado para poder animar la entrada y la salida; cerrado queda inerte. */}
      {(
        <div
          className={`fixed inset-0 z-50 md:hidden ${drawerOpen ? "" : "pointer-events-none"}`}
          {...({ inert: drawerOpen ? undefined : "" } as object)}
          aria-hidden={!drawerOpen}
          role="dialog" aria-modal="true" aria-label="Menú de navegación">
          <div
            className={`absolute inset-0 bg-ink/40 backdrop-blur-sm transition-opacity duration-300 ease-out motion-reduce:transition-none ${drawerOpen ? "opacity-100" : "opacity-0"}`}
            onClick={() => setDrawerOpen(false)}
          />
          <div
            className={`absolute inset-y-0 left-0 flex w-64 max-w-[85vw] flex-col border-r pb-[env(safe-area-inset-bottom)] pl-[env(safe-area-inset-left)] border-hair/40 bg-paper shadow-warm-lg transition-transform duration-300 ease-[cubic-bezier(0.32,0.72,0,1)] motion-reduce:transition-none ${drawerOpen ? "translate-x-0" : "-translate-x-full"}`}
          >
            <div className="flex items-center justify-between border-b border-hair/40 px-4 py-4">
              <BrandMark className="gap-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-oro" />
              <button
                ref={closeRef}
                onClick={() => setDrawerOpen(false)}
                aria-label="Cerrar menú"
                className="-mr-2 grid h-11 w-11 place-items-center rounded-lg text-muted hover:text-ink"
              >
                <X size={20} />
              </button>
            </div>
            <div className="mx-4 mt-4 flex items-center gap-2.5 rounded-xl bg-oro/[0.08] px-3 py-2.5 ring-1 ring-hair/25">
              <div className="grid h-9 w-9 flex-shrink-0 place-items-center rounded-lg bg-brand-500 text-white"><span className="font-display text-lg leading-none">P</span></div>
              <div className="min-w-0">
                <p className="text-[10px] font-semibold uppercase tracking-wide text-oro/80">Restaurante</p>
                <p className="truncate text-sm font-bold leading-tight text-ink">{staff.tenantName ?? "—"}</p>
              </div>
            </div>
            <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 py-3">
              {items.map(({ href, label, Icon }) => {
                const active = pathname === href || pathname.startsWith(href + "/");
                return (
                  <Link
                    key={href}
                    href={href as never}
                    onClick={() => setDrawerOpen(false)}
                    className={`flex items-center gap-3 rounded-lg px-3 py-3 text-base font-semibold transition ${
                      active ? "bg-oro/[0.12] text-brand-700" : "text-muted hover:bg-paper2 hover:text-ink"
                    }`}
                  >
                    <Icon size={22} weight={active ? "fill" : "regular"} aria-hidden />
                    {label}
                  </Link>
                );
              })}
            </nav>
          </div>
        </div>
      )}
    </div>
  );
}
