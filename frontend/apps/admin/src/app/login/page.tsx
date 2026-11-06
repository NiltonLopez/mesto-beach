"use client";

import { Suspense, useState, useEffect } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import type { StaffTenantOption } from "@mestobeach/api-client";
import { describeApiError } from "@mestobeach/api-client";
import { identityApi, saveAuth, getToken } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

const ALLOWED_ROLES = ["Admin", "Cajero"];

function LoginContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { setAuth } = useAuthStore();

  const [email, setEmail] = useState(searchParams.get("email") ?? "");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // When the admin has access to more than one restaurant, we show a picker instead of logging in.
  const [tenants, setTenants] = useState<StaffTenantOption[] | null>(null);

  const envTenantId = process.env["NEXT_PUBLIC_TENANT_ID"];

  useEffect(() => {
    if (getToken()) router.replace("/dashboard");
  }, [router]);

  // Completes the login once a tenant is known (single tenant, picked tenant, or env override).
  async function loginWithTenant(tenantId: string) {
    setError(null);
    setLoading(true);
    try {
      const { data } = await identityApi.loginStaff(email, password, tenantId);
      if (!data.staff.roles.some((r) => ALLOWED_ROLES.includes(r))) {
        setError("Solo Administradores y Cajeros pueden acceder a este panel.");
        setTenants(null);
        return;
      }
      saveAuth(data.token, data.staff);
      setAuth(data.staff, data.token);
      router.replace(data.staff.mustChangePassword ? "/change-password" : "/dashboard");
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

    // Dev override: a fixed tenant skips discovery entirely.
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
        // More than one restaurant → let the user choose.
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
    <div className="flex min-h-screen items-center justify-center bg-canvas p-6">
      <div className="w-full max-w-sm">
        <div className="card p-8">
          <div className="mb-6 text-center">
            <h1 className="font-display text-3xl font-normal tracking-tight text-ink">Mesto Beach</h1>
            <p className="mt-1 text-sm text-muted">Panel de administración</p>
            <div className="mx-auto mt-4 h-px w-12 bg-oro/60" />
          </div>

          {error && (
            <div className="mb-4 rounded-lg bg-rose-50 border border-rose-200 px-4 py-3 text-sm text-rose-600">
              {error}
            </div>
          )}

          {tenants ? (
            /* Tenant picker — shown only when the admin can access more than one restaurant. */
            <div className="space-y-3">
              <p className="text-sm text-muted">
                Tienes acceso a varios restaurantes. Elige con cuál entrar:
              </p>
              <div className="space-y-2">
                {tenants.map((t) => (
                  <button
                    key={t.tenantId}
                    onClick={() => loginWithTenant(t.tenantId)}
                    disabled={loading}
                    className="flex w-full items-center justify-between rounded-lg border border-hair/50 px-4 py-3 text-left transition hover:border-oro hover:bg-oro/[0.08] disabled:opacity-60"
                  >
                    <span className="font-display text-[15px] font-normal text-ink">{t.tenantName}</span>
                    <span className="rounded-full border border-hair/40 bg-paper2 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-muted">
                      {t.roles.join(", ")}
                    </span>
                  </button>
                ))}
              </div>
              <button
                onClick={() => {
                  setTenants(null);
                  setError(null);
                }}
                className="text-sm font-medium text-muted hover:underline"
              >
                ← Volver
              </button>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="label">Correo electrónico</label>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input"
                  placeholder="admin@restaurante.co"
                  required
                  autoComplete="email"
                />
              </div>
              <div>
                <label className="label">Contraseña</label>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="input"
                  placeholder="••••••••"
                  required
                  autoComplete="current-password"
                />
              </div>
              <button type="submit" disabled={loading} className="btn-primary w-full mt-2">
                {loading ? "Iniciando sesión…" : "Entrar"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <LoginContent />
    </Suspense>
  );
}
