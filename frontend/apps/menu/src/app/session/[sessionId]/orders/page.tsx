"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

interface Props {
  params: { sessionId: string };
}

/**
 * «Mis pedidos» dejó de ser una página aparte: ahora es la ventana «Cuenta» del
 * propio menú. Esta ruta se queda como puerta para los enlaces ya repartidos
 * —los que se compartieron por WhatsApp, los que alguien guardó— y los deja en
 * la ventana correcta.
 */
export default function OrdersRedirect({ params }: Props) {
  const router = useRouter();

  useEffect(() => {
    router.replace(`/session/${params.sessionId}?vista=cuenta`);
  }, [router, params.sessionId]);

  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-hair/50 border-t-brand-500" />
    </div>
  );
}
