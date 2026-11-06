/**
 * Dinero: una sola forma de convertir entre lo que teclea una persona y lo que
 * viaja por la API.
 *
 * En el sistema, **todo importe entero son céntimos de euro** (1450 = 14,50 €).
 * Las personas, en cambio, teclean euros con coma. Estas funciones son la única
 * frontera entre las dos representaciones.
 *
 * Existen aquí, en el paquete compartido, porque antes estaban duplicadas en
 * `admin` y en `staff` con nombres distintos y **semánticas de fallo opuestas**:
 * una devolvía `NaN` ante basura y la otra `0` en silencio. Un cobro que se
 * convierte en cero sin avisar es exactamente la clase de error que no se ve
 * hasta que descuadra la caja.
 */

/**
 * Euros tecleados → céntimos. Acepta coma o punto decimal ("14,50" y "14.50").
 *
 * Devuelve `null` —no `NaN`, no `0`— cuando el texto no es un importe válido.
 * `null` obliga a decidir: quien llama tiene que mostrar un error o bloquear la
 * acción, no puede tratarlo como un número por accidente.
 */
export function parseEurosToCents(value: string): number | null {
  const limpio = value.trim().replace(",", ".");
  if (limpio === "") return null;

  const n = Number(limpio);
  if (!Number.isFinite(n) || n < 0) return null;

  return Math.round(n * 100);
}

/**
 * Céntimos → texto editable dentro de un input.
 *
 * `separador` decide la forma: un `<input type="number">` exige el punto
 * canónico del DOM; un input de texto con teclado español espera la coma, que
 * es lo que la persona va a teclear encima.
 */
export function centsToEurosInput(cents: number, separador: "." | "," = "."): string {
  const texto = (cents / 100).toFixed(2);
  return separador === "," ? texto.replace(".", ",") : texto;
}

/** Céntimos → texto para mostrar: 1450 → "14,50 €". */
export function formatEur(cents: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(cents / 100);
}
