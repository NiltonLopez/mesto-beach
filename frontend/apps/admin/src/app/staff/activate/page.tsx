"use client";

import { Suspense, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { staffMgmtApi } from "@/lib/api";

function StaffActivateContent() {
  const searchParams = useSearchParams();
  const token = useMemo(() => searchParams.get("token") ?? "", [searchParams]);

  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<null | { tenantId: string; email: string }>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const { data } = await staffMgmtApi.activateStaff({ token, password, confirmPassword });
      const staff = data.staff as { tenantId: string; email: string };
      setSuccess({ tenantId: staff.tenantId, email: staff.email });
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "No se pudo activar la cuenta.";
      setError(detail);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-canvas p-6">
      <div className="w-full max-w-md">
        <div className="card p-8">
          <div className="mb-6 text-center">
            <h1 className="font-display text-2xl font-normal text-ink">Activar cuenta</h1>
            <p className="mt-1 text-sm text-muted">Define tu contraseña inicial para el Admin Panel.</p>
          </div>

          {!token && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-600">
              El enlace de activación no tiene token.
            </div>
          )}

          {error && (
            <div className="mb-4 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-600">
              {error}
            </div>
          )}

          {success ? (
            <div className="space-y-4">
              <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
                Cuenta activada. Ya puedes iniciar sesión.
              </div>
              <div className="rounded-lg border border-hair/40 bg-paper2 px-4 py-3 text-sm text-ink">
                <p><span className="font-medium">Correo:</span> {success.email}</p>
                <p><span className="font-medium">Tenant ID:</span> <span className="font-mono text-xs">{success.tenantId}</span></p>
                <p className="mt-2 text-xs text-muted">Guarda este Tenant ID: el login del Admin Panel lo solicita si no está preconfigurado.</p>
              </div>
              <Link
                href={`/login?tenantId=${encodeURIComponent(success.tenantId)}&email=${encodeURIComponent(success.email)}`}
                className="btn-primary block w-full text-center"
              >
                Ir al login
              </Link>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label className="label">Nueva contraseña</label>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="input"
                  placeholder="••••••••"
                  required
                  minLength={8}
                />
              </div>
              <div>
                <label className="label">Confirmar contraseña</label>
                <input
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="input"
                  placeholder="••••••••"
                  required
                  minLength={8}
                />
              </div>
              <button type="submit" disabled={loading || !token} className="btn-primary w-full">
                {loading ? "Activando…" : "Activar cuenta"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}

export default function StaffActivatePage() {
  return (
    <Suspense fallback={null}>
      <StaffActivateContent />
    </Suspense>
  );
}
