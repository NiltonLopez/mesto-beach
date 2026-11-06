"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { sessionsApi, storeSession, markIdentified, getOrCreateDeviceId } from "@/lib/api";
import { parseApiError, TABLE_CLOSED_MESSAGE, VENUE_UNAVAILABLE_MESSAGE } from "@/lib/errors";
import EmptyState from "@/components/EmptyState";

function LoadingFallback() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-6">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-hair/50 border-t-brand-500" />
      <p className="text-sm text-muted">Conectando con tu mesa…</p>
    </div>
  );
}

interface Failure {
  message: string;
  /** Con la mesa cerrada en este modo no tiene sentido sugerir que abran la mesa. */
  closedInMode: boolean;
}

// Fixed table QR landing: /mesa?tenant={tenantId}&table={tableCode}
// Resolves the active session of the table and joins it (no token).
function MesaContent() {
  const router = useRouter();
  const params = useSearchParams();
  const [error, setError] = useState<Failure | null>(null);

  useEffect(() => {
    const tenantId = params.get("tenant");
    const tableCode = params.get("table");

    if (!tenantId || !tableCode) {
      setError({ message: "Código de mesa inválido.", closedInMode: false });
      return;
    }

    let cancelled = false;
    const deviceId = getOrCreateDeviceId();
    (async () => {
      try {
        const { data } = await sessionsApi.joinByTable({ tenantId, tableCode, deviceId });
        // `storeSession` compara con la visita guardada en este dispositivo: si el servidor
        // devuelve la misma sesión y el mismo participante, conserva nombre y PIN.
        const { resumed } = storeSession({
          sessionId: data.sessionId,
          participantId: data.participantId,
          tenantId: data.tenantId,
          tableCode: data.tableCode,
          pinValidated: data.pinValidated,
        });

        if (!resumed) {
          // Falta lo local (otro navegador, almacenamiento limpiado) pero el servidor sí
          // conoce a este dispositivo: se rehidrata el nombre en vez de volver a pedirlo.
          try {
            const { data: detail } = await sessionsApi.getSession(data.sessionId, deviceId);
            const me = detail.participants.find((p) => p.participantId === data.participantId);
            const name = me?.displayName?.trim();
            if (name) markIdentified(data.sessionId, name);
          } catch {
            /* sin rehidratación: se pedirá la identificación como siempre */
          }
        }
        if (!cancelled) router.replace(`/session/${data.sessionId}`);
      } catch (err) {
        if (cancelled) return;
        const p = parseApiError(err, "No hay una mesa abierta con este código.");
        if (p.code === "TABLE_CLOSED_IN_MODE") setError({ message: TABLE_CLOSED_MESSAGE, closedInMode: true });
        else if (p.code === "VENUE_MODE_UNAVAILABLE") setError({ message: VENUE_UNAVAILABLE_MESSAGE, closedInMode: false });
        else setError({ message: p.detail, closedInMode: false });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [params, router]);

  if (error) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center p-6 text-center">
        <EmptyState title={error.closedInMode ? "Zona cerrada" : "Mesa no disponible"} hint={error.message} />
        {!error.closedInMode && (
          <p className="mt-1 text-xs text-muted">Pide al camarero que abra tu mesa y vuelve a escanear.</p>
        )}
      </div>
    );
  }

  return <LoadingFallback />;
}

export default function MesaPage() {
  return (
    <Suspense fallback={<LoadingFallback />}>
      <MesaContent />
    </Suspense>
  );
}
