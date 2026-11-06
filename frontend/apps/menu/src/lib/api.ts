import {
  createApiClient,
  createCatalogApi,
  createSessionsApi,
  createOrdersApi,
  createIdentityApi,
  createPaymentsApi,
  createVenueApi,
  type CustomerDto,
} from "@mestobeach/api-client";

import { readVisit, patchVisit, upsertVisit, type Visit } from "./visit";

const CUSTOMER_JWT_KEY = "mestobeach_customer_jwt";
const CUSTOMER_KEY = "mestobeach_customer";

function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(CUSTOMER_JWT_KEY);
}

export const apiClient = createApiClient(getToken);
export const catalogApi = createCatalogApi(apiClient);
export const sessionsApi = createSessionsApi(apiClient);
export const ordersApi = createOrdersApi(apiClient);
export const identityApi = createIdentityApi(apiClient);
export const paymentsApi = createPaymentsApi(apiClient);
export const venueApi = createVenueApi(apiClient);

// ── Cuenta del comensal (puntos de fidelización) — persiste entre visitas en localStorage ──
export function saveCustomerAuth(token: string, customer: CustomerDto) {
  localStorage.setItem(CUSTOMER_JWT_KEY, token);
  localStorage.setItem(CUSTOMER_KEY, JSON.stringify(customer));
}

export function getStoredCustomer(): CustomerDto | null {
  if (typeof window === "undefined") return null;
  const raw = localStorage.getItem(CUSTOMER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as CustomerDto;
  } catch {
    return null;
  }
}

export function clearCustomerAuth() {
  localStorage.removeItem(CUSTOMER_JWT_KEY);
  localStorage.removeItem(CUSTOMER_KEY);
}

// ── Sesión actual (puntero por pestaña) + visita por dispositivo (lib/visit.ts) ──
const SESSION_ID_KEY = "mestobeach_session_id";
const PARTICIPANT_ID_KEY = "mestobeach_participant_id";
const TENANT_ID_KEY = "mestobeach_tenant_id";
const TABLE_CODE_KEY = "mestobeach_table_code";
const DEVICE_ID_KEY = "mestobeach_device_id";

export function getStoredSession() {
  if (typeof window === "undefined") return null;
  const sessionId = sessionStorage.getItem(SESSION_ID_KEY);
  const participantId = sessionStorage.getItem(PARTICIPANT_ID_KEY);
  const tenantId = sessionStorage.getItem(TENANT_ID_KEY);
  const tableCode = sessionStorage.getItem(TABLE_CODE_KEY);
  if (!sessionId || !participantId || !tenantId) return null;
  return { sessionId, participantId, tenantId, tableCode: tableCode ?? "" };
}

/** Visita de este dispositivo para la sesión indicada, o null si no es la vigente. */
function currentVisit(sessionId: string): { visit: Visit; tenantId: string; tableCode: string } | null {
  const s = getStoredSession();
  if (!s || s.sessionId !== sessionId) return null;
  const visit = readVisit(s.tenantId, s.tableCode);
  if (!visit || visit.sessionId !== sessionId || visit.participantId !== s.participantId) return null;
  return { visit, tenantId: s.tenantId, tableCode: s.tableCode };
}

export function storeSession(data: {
  sessionId: string;
  participantId: string;
  tenantId: string;
  tableCode: string;
  /** Lo que devolvió el join: si el PIN de la mesa ya está validado para este participante. */
  pinValidated?: boolean;
}): { resumed: boolean; visit: Visit } {
  sessionStorage.setItem(SESSION_ID_KEY, data.sessionId);
  sessionStorage.setItem(PARTICIPANT_ID_KEY, data.participantId);
  sessionStorage.setItem(TENANT_ID_KEY, data.tenantId);
  sessionStorage.setItem(TABLE_CODE_KEY, data.tableCode);
  return upsertVisit(data.tenantId, data.tableCode, data);
}

export function clearSession() {
  sessionStorage.removeItem(SESSION_ID_KEY);
  sessionStorage.removeItem(PARTICIPANT_ID_KEY);
  sessionStorage.removeItem(TENANT_ID_KEY);
  sessionStorage.removeItem(TABLE_CODE_KEY);
}

export function isIdentified(sessionId: string): boolean {
  return currentVisit(sessionId)?.visit.identified === true;
}

export function markIdentified(sessionId: string, displayName: string) {
  const c = currentVisit(sessionId);
  if (c) patchVisit(c.tenantId, c.tableCode, { identified: true, displayName });
}

export function getDisplayName(): string | null {
  const s = getStoredSession();
  if (!s) return null;
  return currentVisit(s.sessionId)?.visit.displayName || null;
}

export function isPinValidated(sessionId: string): boolean {
  return currentVisit(sessionId)?.visit.pinValidated === true;
}

export function markPinValidated(sessionId: string) {
  const c = currentVisit(sessionId);
  if (c) patchVisit(c.tenantId, c.tableCode, { pinValidated: true });
}

/**
 * UUID v4 que también funciona fuera de contexto seguro. `crypto.randomUUID`
 * solo existe en HTTPS o localhost; al abrir el menú por IP LAN en HTTP (probar
 * desde el celular) no está, así que caemos a `getRandomValues` —disponible en
 * cualquier contexto— y, como último recurso, a Math.random.
 */
function safeUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  const b = new Uint8Array(16);
  if (typeof crypto !== "undefined" && typeof crypto.getRandomValues === "function") {
    crypto.getRandomValues(b);
  } else {
    for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256);
  }
  b[6] = ((b[6] ?? 0) & 0x0f) | 0x40; // versión 4
  b[8] = ((b[8] ?? 0) & 0x3f) | 0x80; // variante RFC 4122
  const h = Array.from(b, (x) => x.toString(16).padStart(2, "0"));
  return `${h[0]}${h[1]}${h[2]}${h[3]}-${h[4]}${h[5]}-${h[6]}${h[7]}-${h[8]}${h[9]}-${h[10]}${h[11]}${h[12]}${h[13]}${h[14]}${h[15]}`;
}

export function getOrCreateDeviceId(): string {
  let deviceId = localStorage.getItem(DEVICE_ID_KEY);
  if (!deviceId) {
    deviceId = safeUuid();
    localStorage.setItem(DEVICE_ID_KEY, deviceId);
  }
  return deviceId;
}

/** Formatea un importe entero en céntimos de euro: 1450 -> "14,50 €". */
export function formatEur(cents: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(cents / 100);
}
