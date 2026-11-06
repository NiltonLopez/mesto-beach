"use client";

import { useState, useEffect } from "react";
import { BrandMark } from "@/components/BrandMark";
import { useRouter } from "next/navigation";
import type { StaffTenantOption } from "@mestobeach/api-client";
import { describeApiError } from "@mestobeach/api-client";
import { identityApi, saveAuth, getToken, getStoredStaff } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

const ALLOWED_ROLES = ["Admin", "Cajero", "KDS", "Mesero", "Runner", "PDA"];

// Home por rol, en orden de precedencia. Admin/Cajero a Caja; luego los roles operativos de
// Mesto Beach cada uno a su vista. El PDA tiene su vista de mapa+comandas; el Mesero (nomenclatura
// anterior) sigue en el Salón.
function homeFor(roles: string[]): "/cashdesk" | "/board" | "/runner" | "/host" | "/pda" | "/salon" {
  if (roles.includes("Cajero") || roles.includes("Admin")) return "/cashdesk";
  if (roles.includes("KDS")) return "/board";
  if (roles.includes("Runner")) return "/runner";
  if (roles.includes("Host")) return "/host";
  if (roles.includes("PDA")) return "/pda";
  return "/salon"; // Mesero
}

export default function LoginPage() {
  const router = useRouter();
  const { setAuth } = useAuthStore();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tenants, setTenants] = useState<StaffTenantOption[] | null>(null);

  const envTenantId = process.env["NEXT_PUBLIC_TENANT_ID"];

  useEffect(() => {
    // getStoredStaff() returns null for stale/old-schema sessions (and clears them),
    // so a leftover session pointing to a deleted tenant won't trap the user — they stay on login.
    const stored = getStoredStaff();
    if (!getToken() || !stored) return;
    router.replace(homeFor(stored.roles));
  }, [router]);

  async function loginWithTenant(tenantId: string) {
    setError(null);
    setLoading(true);
    try {
      const { data } = await identityApi.loginStaff(email, password, tenantId);
      if (!data.staff.roles.some((r) => ALLOWED_ROLES.includes(r))) {
        setError("Este acceso es solo para Meseros, Cajeros, Cocineros (KDS) o Administradores.");
        setTenants(null);
        return;
      }
      saveAuth(data.token, data.staff);
      setAuth(data.staff, data.token);
      router.replace(
        data.staff.mustChangePassword ? "/change-password" : homeFor(data.staff.roles)
      );
    } catch (err: unknown) {
      const detail = describeApiError(err, "No se pudo iniciar sesión.");
      setError(detail);
    } finally {
      setLoading(false);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (envTenantId) {
      await loginWithTenant(envTenantId);
      return;
    }
    setLoading(true);
    try {
      const { data } = await identityApi.resolveStaffTenants(email, password);
      const list = data.tenants ?? [];
      if (list.length === 1) {
        await loginWithTenant(list[0]!.tenantId);
      } else {
        setTenants(list);
        setLoading(false);
      }
    } catch (err: unknown) {
      const detail = describeApiError(err, "Credenciales incorrectas.");
      setError(detail);
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <div className="w-full max-w-sm">
        <div className="card p-8">
          <div className="mb-6 text-center">
            <BrandMark size="sm" className="justify-center gap-2 text-[11px] font-medium uppercase tracking-[0.22em] text-oro" />
            <h1 className="mt-3 font-display text-3xl font-normal tracking-tight text-ink">Bienvenido</h1>
            <p className="mt-1 text-sm text-muted">Caja · Cocina · Salón — acceso de staff</p>
            <div className="mx-auto mt-4 h-px w-12 bg-oro/55" />
          </div>

          {error && (
            <div className="mb-4 rounded-xl border border-rose-400/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-600">
              {error}
            </div>
          )}

          {tenants ? (
            <div className="space-y-3">
              <p className="text-sm text-muted">Tienes acceso a varios restaurantes. Elige uno:</p>
              <div className="space-y-2">
                {tenants.map((t) => (
                  <button
                    key={t.tenantId}
                    onClick={() => loginWithTenant(t.tenantId)}
                    disabled={loading}
                    className="flex w-full items-center justify-between rounded-xl border border-hair/55 px-4 py-3 text-left transition hover:border-oro hover:bg-paper2 disabled:opacity-60"
                  >
                    <span className="font-semibold text-ink">{t.tenantName}</span>
                    <span className="rounded-full bg-paper2 px-2 py-0.5 text-xs font-medium text-muted">
                      {t.roles.join(", ")}
                    </span>
                  </button>
                ))}
              </div>
              <button onClick={() => { setTenants(null); setError(null); }} className="text-sm font-medium text-muted hover:underline">
                ← Volver
              </button>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="mb-1 block text-xs font-medium uppercase tracking-[0.08em] text-muted">Correo</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                  className="input" placeholder="usuario@restaurante.co" required autoComplete="email" />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium uppercase tracking-[0.08em] text-muted">Contraseña</label>
                <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
                  className="input" placeholder="••••••••" required autoComplete="current-password" />
              </div>
              <button type="submit" disabled={loading} className="btn-primary w-full mt-2">
                {loading ? "Entrando…" : "Entrar"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
