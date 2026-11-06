"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getStoredStaff, getToken, identityApi, saveAuth } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

export default function ChangePasswordPage() {
  const router = useRouter();
  const { setAuth } = useAuthStore();

  const [ready, setReady] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!getToken() || !getStoredStaff()) {
      router.replace("/login");
      return;
    }
    setReady(true);
  }, [router]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (newPassword !== confirmPassword) {
      setError("Las contraseñas no coinciden.");
      return;
    }

    setLoading(true);
    try {
      await identityApi.changePassword({ currentPassword, newPassword, confirmPassword });

      const token = getToken();
      const stored = getStoredStaff();
      if (token && stored) {
        const updated = { ...stored, mustChangePassword: false };
        saveAuth(token, updated);
        setAuth(updated, token);
        const roles = updated.roles ?? [];
        router.replace(
          roles.includes("Cajero") || roles.includes("Admin")
            ? "/cashdesk"
            : roles.includes("KDS")
              ? "/board"
              : "/salon" // Mesero
        );
      } else {
        router.replace("/login");
      }
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "No se pudo cambiar la contraseña.";
      setError(detail);
    } finally {
      setLoading(false);
    }
  }

  if (!ready) return null;

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <div className="w-full max-w-sm">
        <div className="card p-8">
          <div className="mb-6 text-center">
            <h1 className="font-display text-3xl text-ink">Cambia tu contraseña</h1>
            <p className="mt-1 text-sm text-muted">
              Tu acceso usa una contraseña temporal. Define una nueva para continuar.
            </p>
          </div>

          {error && (
            <div className="mb-4 rounded-lg border border-rose-400/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-600">
              {error}
            </div>
          )}

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="mb-1 block text-xs font-medium text-muted">Contraseña temporal</label>
              <input
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="input"
                placeholder="••••••••"
                required
                autoComplete="current-password"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted">Nueva contraseña</label>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="input"
                placeholder="Mín. 8, con mayúscula, minúscula y número"
                required
                minLength={8}
                autoComplete="new-password"
              />
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium text-muted">Confirmar nueva contraseña</label>
              <input
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="input"
                placeholder="••••••••"
                required
                minLength={8}
                autoComplete="new-password"
              />
            </div>
            <button type="submit" disabled={loading} className="btn-primary w-full mt-2">
              {loading ? "Guardando…" : "Cambiar contraseña"}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
