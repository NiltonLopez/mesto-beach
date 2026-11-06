/**
 * Visita por dispositivo (PRD-019 US-1912, RF-194x).
 *
 * Quién es este dispositivo en esta mesa vive en `localStorage`, bajo
 * `mestobeach_visit:{tenantId}:{tableCode}`. Así, escanear de nuevo el QR (o reabrir la
 * pestaña) no vuelve a pedir el nombre ni el PIN mientras la sesión de la mesa siga siendo
 * la misma. Si el servidor devuelve otra sesión u otro participante, la visita se descarta.
 */
export interface Visit {
  sessionId: string;
  participantId: string;
  displayName: string;
  identified: boolean;
  pinValidated: boolean;
  savedAt: number;
}

const PREFIX = "mestobeach_visit:";
/** Tope de vida de una visita: pasado esto se ignora aunque nadie la haya cerrado. */
const MAX_AGE_MS = 24 * 60 * 60 * 1000;

export const visitKey = (tenantId: string, tableCode: string) => `${PREFIX}${tenantId}:${tableCode}`;

export function readVisit(tenantId: string, tableCode: string): Visit | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = localStorage.getItem(visitKey(tenantId, tableCode));
    if (!raw) return null;
    const v = JSON.parse(raw) as Partial<Visit>;
    if (!v.sessionId || !v.participantId) return null;
    if (typeof v.savedAt === "number" && Date.now() - v.savedAt > MAX_AGE_MS) return null;
    return {
      sessionId: v.sessionId,
      participantId: v.participantId,
      displayName: v.displayName ?? "",
      identified: v.identified === true,
      pinValidated: v.pinValidated === true,
      savedAt: v.savedAt ?? 0,
    };
  } catch {
    return null;
  }
}

export function writeVisit(tenantId: string, tableCode: string, visit: Omit<Visit, "savedAt">): void {
  try {
    localStorage.setItem(visitKey(tenantId, tableCode), JSON.stringify({ ...visit, savedAt: Date.now() }));
  } catch {
    /* almacenamiento bloqueado: la visita no persiste, el flujo sigue funcionando */
  }
}

export function clearVisit(tenantId: string, tableCode: string): void {
  try {
    localStorage.removeItem(visitKey(tenantId, tableCode));
  } catch {
    /* nada que limpiar */
  }
}

/**
 * Guarda la visita que acaba de devolver el servidor. Si coincide sesión y participante con la
 * que ya había, conserva lo local (nombre, identificado); si no, empieza de cero. El PIN manda
 * lo que diga el servidor, o lo que ya estuviera validado en la misma visita.
 */
export function upsertVisit(
  tenantId: string,
  tableCode: string,
  server: { sessionId: string; participantId: string; pinValidated?: boolean },
): { visit: Visit; resumed: boolean } {
  const prev = readVisit(tenantId, tableCode);
  const same = !!prev && prev.sessionId === server.sessionId && prev.participantId === server.participantId;
  const next: Omit<Visit, "savedAt"> = {
    sessionId: server.sessionId,
    participantId: server.participantId,
    displayName: same ? prev.displayName : "",
    identified: same ? prev.identified : false,
    pinValidated: server.pinValidated === true || (same && prev.pinValidated),
  };
  writeVisit(tenantId, tableCode, next);
  return { visit: { ...next, savedAt: Date.now() }, resumed: same && prev.identified };
}

export function patchVisit(tenantId: string, tableCode: string, patch: Partial<Omit<Visit, "savedAt">>): void {
  const v = readVisit(tenantId, tableCode);
  if (!v) return;
  const { savedAt: _omit, ...rest } = v;
  void _omit;
  writeVisit(tenantId, tableCode, { ...rest, ...patch });
}
