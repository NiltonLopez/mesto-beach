"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter, usePathname } from "next/navigation";
import Link from "next/link";
import { clearAuth, getToken, getStoredStaff, saveAuth, identityApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { DialogProvider } from "@/components/Dialog";
import { FullscreenButton } from "@/components/FullscreenButton";
import { useKioskMode } from "@/lib/useKioskMode";
import { VenueProvider } from "@/lib/venue";
import { VenueSwitch } from "@/components/VenueSwitch";
import { BrandMark } from "@/components/BrandMark";
import {
  ChartLineUp, CreditCard, ForkKnife, IdentificationBadge, List, MapTrifold, Monitor,
  Money, Palette, SignOut, SquaresFour, Users, Warning, X,
} from "@phosphor-icons/react";

const ACTIVE_STATES = ["Active", "Trial"];

// Iconografía Mesto Beach: Phosphor en peso "light" — trazo fino, monocromo,
// hereda el color del texto (dorado solo en el ítem activo). Ver docs/marca.md.
const NAV = [
  { href: "/dashboard", label: "Dashboard", Icon: SquaresFour },
  { href: "/mesas", label: "Mesas", Icon: MapTrifold },
  { href: "/sessions", label: "Sesiones", Icon: Users },
  { href: "/payments", label: "Pagos", Icon: Money },
  { href: "/analytics", label: "Analítica", Icon: ChartLineUp },
  { href: "/staff", label: "Personal", Icon: IdentificationBadge },
  { href: "/catalog", label: "Catálogo", Icon: ForkKnife },
  { href: "/menu-appearance", label: "Apariencia", Icon: Palette },
  { href: "/kds-stations", label: "Estaciones KDS", Icon: Monitor },
] as const;

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { staff, setAuth, clearAuth: clearStore } = useAuthStore();
  // Pantalla completa del panel: aparta la barra lateral para que el contenido (sobre todo el
  // editor de mesas) gane todo el ancho, y pide el fullscreen del navegador si lo concede.
  const kiosk = useKioskMode();
  const [activating, setActivating] = useState(false);
  const [activateErr, setActivateErr] = useState<string | null>(null);
  // Cajón de navegación en móvil (< md): la barra lateral fija solo existe desde md.
  const [drawerOpen, setDrawerOpen] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);

  // Cambiar de página cierra el cajón.
  useEffect(() => {
    setDrawerOpen(false);
  }, [pathname]);

  // Esc cierra el cajón y el foco inicial va al botón de cierre.
  useEffect(() => {
    if (!drawerOpen) return;
    closeRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setDrawerOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  async function handleActivate() {
    if (!staff) return;
    setActivating(true);
    setActivateErr(null);
    try {
      const { data } = await identityApi.activateTenant();
      const token = getToken();
      const updated = { ...staff, tenantStatus: data.status };
      if (token) {
        saveAuth(token, updated); // persist so staff/cajeros can log in immediately
        setAuth(updated, token);
      }
    } catch (e) {
      setActivateErr((e as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? "No se pudo activar. Intenta de nuevo.");
    } finally {
      setActivating(false);
    }
  }

  useEffect(() => {
    const token = getToken();
    const stored = getStoredStaff();
    // Reject stale sessions saved before the unified-identity change (no `roles[]`).
    if (!token || !stored || !Array.isArray(stored.roles)) {
      clearAuth();
      router.replace("/login");
      return;
    }
    if (stored.mustChangePassword) {
      router.replace("/change-password");
      return;
    }
    setAuth(stored, token);
  }, [setAuth, router]);

  function handleLogout() {
    clearAuth();
    clearStore();
    router.replace("/login");
  }

  if (!staff) return null;

  const sidebarContent = (
    <>
      <div className="border-b border-hair/40 px-4 py-4">
        <BrandMark className="mb-3 gap-1.5 px-1 text-[11px] font-semibold uppercase tracking-[0.2em] text-brand-700" />

        {/* Restaurante activo */}
        <div className="flex items-center gap-2.5 rounded-xl bg-oro/[0.08] px-3 py-2.5 ring-1 ring-hair/40">
          <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-brand-500 text-white shadow-warm-sm">
            <span className="font-display text-lg leading-none">P</span>
          </div>
          <div className="min-w-0">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-brand-700/70">Restaurante</p>
            <p className="truncate font-display text-[15px] font-normal leading-tight text-ink" title={staff.tenantName ?? undefined}>
              {staff.tenantName ?? "—"}
            </p>
          </div>
        </div>

        {/* Usuario */}
        <div className="mt-3 flex items-center gap-2.5 px-1">
          <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-oro/[0.10] text-sm font-semibold uppercase text-brand-700">
            {(staff.name?.trim()?.[0] ?? "?")}
          </div>
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold leading-tight text-ink">{staff.name}</p>
            <p className="truncate text-xs leading-tight text-muted">{(staff.roles ?? []).join(" · ")}</p>
          </div>
        </div>
      </div>

      <nav className="flex-1 overflow-y-auto px-3 py-3 space-y-0.5">
        {NAV.map(({ href, label, Icon }) => {
          const active = pathname === href;
          return (
            <Link
              key={href}
              href={href}
              className={`flex min-h-[44px] items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition ${
                active
                  ? "bg-oro/[0.10] text-brand-700"
                  : "text-muted hover:bg-paper2 hover:text-ink"
              }`}
            >
              <Icon size={20} weight="light" aria-hidden />
              {label}
            </Link>
          );
        })}
      </nav>

      {/* Pie de la barra: acciones de pantalla, separadas de la navegación. Las páginas de
          admin no tienen cabecera propia, así que este es el sitio natural del botón. */}
      <div className="space-y-0.5 border-t border-hair/40 p-3">
        <FullscreenButton />
        <VenueSwitch />
        <button
          onClick={handleLogout}
          className="flex min-h-[44px] w-full items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-muted hover:bg-paper2 hover:text-ink transition"
        >
          <SignOut size={20} weight="light" aria-hidden />
          Cerrar sesión
        </button>
      </div>
    </>
  );

  return (
    <VenueProvider>
    <DialogProvider>
    <div className="flex h-[100dvh] bg-canvas">
      {/* Sidebar — en pantalla completa se aparta entera y el contenido gana todo el ancho.
          Sin ella no queda ninguna forma de volver: por eso la pastilla flotante de abajo. */}
      {!kiosk && (
        <aside className="hidden w-56 flex-shrink-0 flex-col border-r border-hair/50 bg-paper md:flex">
          {sidebarContent}
        </aside>
      )}

      {/* Main content */}
      <div className="flex min-w-0 flex-1 flex-col">
      {/* Barra superior móvil con hamburguesa */}
      {!kiosk && (
        <div className="flex items-center gap-3 border-b border-hair/40 bg-paper px-4 py-2.5 pt-[max(0.625rem,env(safe-area-inset-top))] md:hidden">
          <button
            onClick={() => setDrawerOpen(true)}
            aria-label="Abrir menú"
            className="grid h-11 w-11 place-items-center rounded-lg border border-hair/50 bg-paper2 text-ink"
          >
            <List size={22} aria-hidden />
          </button>
          <BrandMark className="gap-1.5 text-[11px] font-semibold uppercase tracking-[0.2em] text-brand-700" />
          <span className="ml-auto truncate text-sm font-semibold text-muted">{staff.tenantName ?? ""}</span>
        </div>
      )}
      <main className="min-h-0 flex-1 overflow-y-auto">
        {/* Restaurant not live yet → alert the owner; their staff can't sign in until it's activated. */}
        {staff.tenantStatus && !ACTIVE_STATES.includes(staff.tenantStatus) && (
          <div className="border-b border-amber-200 bg-amber-50 px-6 py-3.5">
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
              <Warning size={22} weight="light" className="text-amber-700" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-amber-900">Tu restaurante aún no está activo</p>
                <p className="text-sm text-amber-800">
                  Mientras esté en proceso de activación, <strong>tu personal (cajeros y meseros) no podrá iniciar sesión</strong>.
                  Termina de configurar tu menú y mesas, y cuando estés listo, actívalo.
                </p>
                {activateErr && <p className="mt-1 text-sm font-medium text-rose-600">{activateErr}</p>}
              </div>
              <button
                onClick={() => void handleActivate()}
                disabled={activating}
                className="btn-primary flex-shrink-0 text-sm"
              >
                {activating ? "Activando…" : "Activar restaurante"}
              </button>
            </div>
          </div>
        )}
        {children}
      </main>
      </div>

      {/* Cajón móvil (< md) */}
      {/* Siempre montado para poder animar la entrada y la salida; cerrado queda inerte. */}
      {!kiosk && (
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
            className={`absolute inset-y-0 left-0 flex w-64 max-w-[85vw] flex-col border-r border-hair/50 bg-paper pb-[env(safe-area-inset-bottom)] pl-[env(safe-area-inset-left)] shadow-warm-lg transition-transform duration-300 ease-[cubic-bezier(0.32,0.72,0,1)] motion-reduce:transition-none ${drawerOpen ? "translate-x-0" : "-translate-x-full"}`}
          >
            <button
              ref={closeRef}
              onClick={() => setDrawerOpen(false)}
              aria-label="Cerrar menú"
              className="absolute right-2 top-2 z-10 grid h-11 w-11 place-items-center rounded-lg text-muted hover:text-ink"
            >
              <X size={20} aria-hidden />
            </button>
            {sidebarContent}
          </div>
        </div>
      )}

      {/* Salida de emergencia: con la barra oculta esta pastilla es la única forma segura de
          volver. Esc solo sirve si el navegador concedió el fullscreen nativo; si lo negó, sin
          esto el usuario quedaría encerrado. */}
      {kiosk && <FullscreenButton variant="flotante" />}
    </div>
    </DialogProvider>
    </VenueProvider>
  );
}
