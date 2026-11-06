import {
  createApiClient,
  createIdentityApi,
  createOrdersApi,
  createKdsApi,
  createPaymentsApi,
  createSessionsApi,
  createCatalogApi,
  createVenueApi,
  type StaffDto,
} from "@mestobeach/api-client";

const TOKEN_KEY = "mestobeach_staff_jwt";
const STAFF_KEY = "mestobeach_staff_info";

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(TOKEN_KEY);
}

export function saveAuth(token: string, staff: StaffDto) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(STAFF_KEY, JSON.stringify(staff));
}

export function getStoredStaff(): StaffDto | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(STAFF_KEY);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as StaffDto;
    // Reject sessions saved before the unified-identity change (had `role`, no `roles[]`),
    // or anything malformed — forces a clean re-login instead of crashing/looping downstream.
    if (!Array.isArray(parsed.roles) || !parsed.tenantId) {
      clearAuth();
      return null;
    }
    return parsed;
  } catch {
    clearAuth();
    return null;
  }
}

export function clearAuth() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(STAFF_KEY);
}

export const apiClient = createApiClient(getToken);

// Auto-logout on an expired/invalidated token: clear the stale session and bounce to login
// (skip when already on /login so credential errors there render inline instead of redirecting).
apiClient.interceptors.response.use(
  (res) => res,
  (err) => {
    if (
      typeof window !== "undefined" &&
      err?.response?.status === 401 &&
      !window.location.pathname.startsWith("/login")
    ) {
      clearAuth();
      window.location.href = "/login";
    }
    return Promise.reject(err);
  }
);
export const identityApi = createIdentityApi(apiClient);
export const ordersApi = createOrdersApi(apiClient);
export const kdsApi = createKdsApi(apiClient);
export const paymentsApi = createPaymentsApi(apiClient);
export const sessionsApi = createSessionsApi(apiClient);
export const catalogApi = createCatalogApi(apiClient);
export const venueApi = createVenueApi(apiClient);

const API_URL = process.env["NEXT_PUBLIC_API_URL"] ?? "http://localhost:5011";
export const ORDERS_HUB_URL = `${API_URL}/hubs/orders`;
export const SESSIONS_HUB_URL = `${API_URL}/hubs/sessions`;

/** `amount` llega en céntimos de euro: 1450 -> "14,50 €". */
export function formatEur(amount: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount / 100);
}

export function elapsedLabel(createdAt: string): string {
  const diff = Math.floor((Date.now() - new Date(createdAt).getTime()) / 1000);
  if (diff < 60) return `${diff}s`;
  if (diff < 3600) return `${Math.floor(diff / 60)}m`;
  return `${Math.floor(diff / 3600)}h`;
}
