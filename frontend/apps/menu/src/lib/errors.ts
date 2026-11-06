/**
 * Errores del backend como ProblemDetails (RFC 7807): el código vive en `title`, el texto
 * para la persona en `detail` y las extensiones (p. ej. `itemIds`) en la raíz del cuerpo.
 */
export interface ApiProblem {
  code: string;
  detail: string;
  status: number | undefined;
  itemIds: string[];
}

export function parseApiError(err: unknown, fallback = "Algo ha fallado. Inténtalo de nuevo."): ApiProblem {
  const res = (err as { response?: { status?: number; data?: Record<string, unknown> } } | null)?.response;
  const data = res?.data ?? {};
  const title = typeof data.title === "string" ? data.title : "";
  const detail = typeof data.detail === "string" ? data.detail : "";
  const ids = data.itemIds;
  // Compatibilidad: algunos flujos antiguos llevaban el código dentro del detalle.
  const code = /^[A-Z][A-Z0-9_]+$/.test(title) ? title : (/PIN_REQUIRED/.test(detail) ? "PIN_REQUIRED" : "");
  return {
    code,
    detail: detail || fallback,
    status: res?.status,
    itemIds: Array.isArray(ids) ? ids.filter((x): x is string => typeof x === "string") : [],
  };
}

export const TABLE_CLOSED_MESSAGE = "Esta zona está cerrada esta noche; acércate a Recepción.";
export const VENUE_UNAVAILABLE_MESSAGE =
  "No podemos comprobar el estado del local ahora mismo. Inténtalo de nuevo en unos segundos.";
