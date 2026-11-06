"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { clearAuth, getStoredStaff, getToken } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { StaffShell } from "@/components/StaffShell";
import { VenueProvider } from "@/lib/venue";

// Shell común de las vistas de consola del staff (Cocina / Caja / Historial):
// sidebar izquierdo gateado por rol (estilo admin) + footer compartido.
// El gate de rol específico de cada vista vive en su propia página.
export default function ConsoleLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const { staff, setAuth } = useAuthStore();

  useEffect(() => {
    const token = getToken();
    const stored = getStoredStaff();
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
  }, [router, setAuth]);

  if (!staff) return null;

  return (
    <VenueProvider>
      <StaffShell staff={staff}>{children}</StaffShell>
    </VenueProvider>
  );
}
