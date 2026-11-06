"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import type { StaffDto } from "@mestobeach/api-client";
import { clearAuth } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

// Etiquetas amigables para los roles del staff (la clave técnica → español).
const ROLE_LABEL: Record<string, string> = {
  KDS: "Cocinero",
  Cajero: "Cajero",
  Mesero: "Mesero",
  Admin: "Admin",
  Host: "Host",
  PDA: "PDA",
  Runner: "Runner",
};

function tenantStatusInfo(status?: string | null): { label: string; cls: string } {
  switch (status) {
    case "Active":
      return { label: "Activo", cls: "bg-emerald-500/10 text-emerald-700" };
    case "Trial":
      return { label: "Trial", cls: "bg-sky-500/10 text-sky-700" };
    case "Suspended":
      return { label: "Suspendido", cls: "bg-rose-500/10 text-rose-600" };
    default:
      return { label: status ?? "—", cls: "bg-paper2 text-muted" };
  }
}

/**
 * Footer compartido por todas las vistas del staff (Cocina / Caja / Salón / Historial).
 * Muestra el restaurante, el usuario y sus roles — funcione el usuario con un solo
 * rol o con varios — y centraliza el cierre de sesión.
 */
export function StaffFooter({ staff }: { staff: StaffDto }) {
  const router = useRouter();
  const { clearAuth: clearStore } = useAuthStore();
  const [showAccount, setShowAccount] = useState(false);

  const roles = staff.roles ?? [];
  const status = tenantStatusInfo(staff.tenantStatus);

  function handleLogout() {
    clearAuth();
    clearStore();
    router.replace("/login");
  }

  return (
    <>
      <footer className="flex flex-shrink-0 items-center gap-3 border-t border-hair/40 bg-paper/95 px-4 py-2.5 backdrop-blur">
        <div className="grid h-10 w-10 flex-shrink-0 place-items-center rounded-xl bg-oro/[0.12]"><span className="font-display text-lg leading-none text-brand-700">P</span></div>
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="truncate text-sm font-bold text-ink">{staff.tenantName ?? "Restaurante"}</span>
            <span className={`flex-shrink-0 rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.04em] ${status.cls}`}>{status.label}</span>
          </div>
          <p className="truncate text-xs text-muted">{staff.name}</p>
        </div>

        {/* Chips de rol — el mismo usuario puede tener uno o varios */}
        <div className="ml-auto hidden flex-wrap items-center gap-1.5 sm:flex">
          {roles.length === 0 ? (
            <span className="text-xs text-muted">Sin rol</span>
          ) : (
            roles.map((r) => (
              <span key={r} className="rounded-full bg-paper2 px-2.5 py-1 text-[11px] font-semibold text-ink ring-1 ring-hair/30">
                {ROLE_LABEL[r] ?? r}
              </span>
            ))
          )}
        </div>

        <button onClick={() => setShowAccount(true)} className="cd-chip ml-auto flex-shrink-0 sm:ml-2" title="Información de la cuenta activa">
          Cuenta
        </button>
      </footer>

      {showAccount && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-ink/40 p-4 backdrop-blur-sm" onClick={() => setShowAccount(false)}>
          <div className="cd-panel w-full max-w-[400px] p-6" onClick={(e) => e.stopPropagation()}>
            <div className="mb-4 flex items-center gap-3">
              <div className="grid h-14 w-14 flex-shrink-0 place-items-center rounded-2xl bg-oro/[0.12]"><span className="font-display text-2xl leading-none text-brand-700">P</span></div>
              <div className="min-w-0">
                <h3 className="truncate text-lg font-extrabold text-ink">{staff.tenantName ?? "Restaurante"}</h3>
                <span className={`inline-block rounded-full px-2 py-0.5 text-[11px] font-bold uppercase tracking-[0.04em] ${status.cls}`}>{status.label}</span>
              </div>
            </div>

            <div className="grid gap-2 rounded-2xl bg-paper2 p-4 text-sm">
              <div className="flex justify-between gap-3"><span className="text-muted">Usuario</span><span className="font-semibold text-ink">{staff.name}</span></div>
              <div className="flex justify-between gap-3"><span className="text-muted">Correo</span><span className="truncate font-medium text-ink">{staff.email}</span></div>
              <div className="flex items-start justify-between gap-3">
                <span className="text-muted">Roles</span>
                <span className="flex flex-wrap justify-end gap-1.5">
                  {roles.map((r) => (
                    <span key={r} className="rounded-full bg-paper px-2 py-0.5 text-[11px] font-semibold text-ink ring-1 ring-hair/25">{ROLE_LABEL[r] ?? r}</span>
                  ))}
                </span>
              </div>
            </div>

            <div className="mt-4 flex gap-2">
              <button onClick={() => setShowAccount(false)} className="cd-btn cd-ghost flex-1">Cerrar</button>
              <button onClick={handleLogout} className="cd-btn cd-danger flex-1">Cerrar sesión</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
