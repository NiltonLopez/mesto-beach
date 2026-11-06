import {
  createApiClient,
  createIdentityApi,
  createCatalogApi,
  createSessionsApi,
  createOrdersApi,
  createPaymentsApi,
  createAnalyticsApi,
  createStaffManagementApi,
  createKdsApi,
  createVenueApi,
  type StaffDto,
} from "@mestobeach/api-client";

const TOKEN_KEY = "mestobeach_admin_jwt";
const STAFF_KEY = "mestobeach_admin_staff";

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
    return JSON.parse(raw) as StaffDto;
  } catch {
    return null;
  }
}

export function clearAuth() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(STAFF_KEY);
}

export const apiClient = createApiClient(getToken);
export const identityApi = createIdentityApi(apiClient);
export const catalogApi = createCatalogApi(apiClient);
export const sessionsApi = createSessionsApi(apiClient);
export const ordersApi = createOrdersApi(apiClient);
export const paymentsApi = createPaymentsApi(apiClient);
export const analyticsApi = createAnalyticsApi(apiClient);
export const staffMgmtApi = createStaffManagementApi(apiClient);
export const kdsApi = createKdsApi(apiClient);
export const venueApi = createVenueApi(apiClient);

/** Formatea un importe en céntimos de euro. 1450 -> "14,50 €". */
export function formatEur(amountCents: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amountCents / 100);
}

/**
 * Conversión euros <-> céntimos: vive en @mestobeach/api-client para que admin y
 * staff compartan exactamente el mismo comportamiento (antes eran dos copias con
 * semánticas de fallo opuestas). `parseEurosToCents` devuelve **null** ante un
 * valor inválido: quien llama debe decidir, no puede tratarlo como número.
 */
export { parseEurosToCents, centsToEurosInput } from "@mestobeach/api-client";

/** @deprecated Usa centsToEurosInput. Se conserva para no romper llamadas existentes. */
export function centsToEurInput(amountCents: number): string {
  return (amountCents / 100).toFixed(2);
}

export function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

export function daysAgoStr(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}
