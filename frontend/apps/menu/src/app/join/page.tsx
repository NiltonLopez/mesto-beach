"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { sessionsApi, storeSession, getOrCreateDeviceId } from "@/lib/api";
import { parseApiError, TABLE_CLOSED_MESSAGE, VENUE_UNAVAILABLE_MESSAGE } from "@/lib/errors";
import EmptyState from "@/components/EmptyState";

function JoinContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [joining, setJoining] = useState(false);

  useEffect(() => {
    const tenantId = params.get("tenantId");
    const token = params.get("token");
    // Normalize the token type robustly: case/space-insensitive, default to DYNAMIC_QR.
    // (A shared link may arrive as "invite", "INVITE ", etc.; the backend only accepts the
    // exact strings DYNAMIC_QR | INVITE.)
    const rawType = (params.get("type") ?? "").trim().toUpperCase();
    const tokenType: "DYNAMIC_QR" | "INVITE" = rawType === "INVITE" ? "INVITE" : "DYNAMIC_QR";

    if (!tenantId || !token) {
      setError("Código QR inválido. Solicita un nuevo código al mesero.");
      return;
    }

    const deviceId = getOrCreateDeviceId();

    setJoining(true);
    sessionsApi
      .joinSession({
        tenantId,
        deviceId,
        joinToken: token,
        tokenType,
      })
      .then(({ data }) => {
        storeSession({
          sessionId: data.sessionId,
          participantId: data.participantId,
          tenantId: data.tenantId,
          tableCode: data.tableCode,
          pinValidated: data.pinValidated,
        });
        router.replace(`/session/${data.sessionId}`);
      })
      .catch((err) => {
        const p = parseApiError(err, "No fue posible unirse a la sesión.");
        const detail = p.code === "TABLE_CLOSED_IN_MODE" ? TABLE_CLOSED_MESSAGE : p.code === "VENUE_MODE_UNAVAILABLE" ? VENUE_UNAVAILABLE_MESSAGE : p.detail;
        setError(detail);
        setJoining(false);
      });
  }, [params, router]);

  if (error) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center p-6 text-center">
        <EmptyState title="No se pudo unir a la mesa" hint={error} />
        <p className="mt-1 text-xs text-muted">Escanea el código QR nuevamente o pide ayuda al camarero.</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-6">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-hair/50 border-t-brand-500" />
      <p className="text-sm text-muted">
        {joining ? "Uniéndose a tu mesa…" : "Verificando código QR…"}
      </p>
    </div>
  );
}

export default function JoinPage() {
  return (
    <Suspense
      fallback={
        <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-6">
          <div className="h-10 w-10 animate-spin rounded-full border-4 border-hair/50 border-t-brand-500" />
          <p className="text-sm text-muted">Verificando código QR…</p>
        </div>
      }
    >
      <JoinContent />
    </Suspense>
  );
}
