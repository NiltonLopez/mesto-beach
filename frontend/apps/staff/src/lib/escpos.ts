// Minimal ESC/POS receipt printing over WebUSB (Chrome only).
// No external deps: builds the byte buffer by hand. Requires a real thermal
// printer to actually print; gracefully reports when WebUSB is unavailable.

export interface ReceiptLine {
  name: string;
  qty: number;
  totalCents: number;
}

export interface ReceiptData {
  restaurant: string;
  tableCode: string;
  cashier: string;
  lines: ReceiptLine[];
  subtotalCents: number;
  tipCents: number;
  totalCents: number;
  method: string;
  at: Date;
}

// Minimal shape of the WebUSB API we use (avoids depending on DOM USB lib types).
interface UsbInterfaceLike { interfaceNumber: number; alternates: { endpoints: { direction: string; endpointNumber: number }[] }[] }
interface UsbDeviceLike {
  open(): Promise<void>;
  selectConfiguration(n: number): Promise<void>;
  claimInterface(n: number): Promise<void>;
  transferOut(endpoint: number, data: Uint8Array): Promise<unknown>;
  close(): Promise<void>;
  configuration?: { interfaces: UsbInterfaceLike[] };
}
interface UsbLike {
  requestDevice(opts: { filters: { classCode?: number }[] }): Promise<UsbDeviceLike>;
}

function usb(): UsbLike | null {
  if (typeof navigator === "undefined") return null;
  return (navigator as unknown as { usb?: UsbLike }).usb ?? null;
}

export function isWebUsbSupported(): boolean {
  return usb() !== null;
}

/**
 * `n` llega en céntimos de euro: 1450 -> "14,50 EUR".
 *
 * Deliberadamente NO usa el símbolo €: esto no va a una pantalla, va por WebUSB
 * a una impresora térmica que interpreta una página de códigos de un solo byte
 * (CP437/CP858). El símbolo € es U+20AC y sale como tres bytes basura; el
 * espacio duro que Intl inserta antes, otros dos. Se formatea el número con
 * las convenciones españolas (coma decimal, punto de millar) y se etiqueta la
 * moneda en ASCII puro.
 */
const EUR = (n: number) =>
  new Intl.NumberFormat("es-ES", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n / 100) + " EUR";

/**
 * Deja el texto en ASCII imprimible. Quita diacríticos (á -> a) y además
 * normaliza los caracteres que Intl introduce y que la impresora no entiende:
 * el espacio duro (U+00A0) y el símbolo de euro (U+20AC), por si alguna cadena
 * lo trae desde el catálogo o desde un nombre escrito por el usuario.
 */
const ascii = (s: string) =>
  s
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/ /g, " ")
    .replace(/€/g, "EUR");

function buildBuffer(r: ReceiptData): Uint8Array {
  const ESC = 0x1b, GS = 0x1d, LF = 0x0a;
  const bytes: number[] = [];
  const enc = new TextEncoder();
  const push = (s: string) => bytes.push(...Array.from(enc.encode(ascii(s))));
  const line = (s = "") => { push(s); bytes.push(LF); };

  bytes.push(ESC, 0x40); // init
  bytes.push(ESC, 0x61, 0x01); // center
  bytes.push(ESC, 0x21, 0x10); // double height
  line(r.restaurant);
  bytes.push(ESC, 0x21, 0x00); // normal
  line(`Mesa ${r.tableCode}`);
  line(r.at.toLocaleString("es-ES"));
  line(`Cajero: ${r.cashier}`);
  bytes.push(ESC, 0x61, 0x00); // left
  line("--------------------------------");
  for (const it of r.lines) line(`${it.qty}x ${it.name}  ${EUR(it.totalCents)}`);
  line("--------------------------------");
  line(`Subtotal: ${EUR(r.subtotalCents)}`);
  if (r.tipCents > 0) line(`Propina:  ${EUR(r.tipCents)}`);
  bytes.push(ESC, 0x21, 0x08); // emphasized
  line(`TOTAL:    ${EUR(r.totalCents)}`);
  bytes.push(ESC, 0x21, 0x00);
  line(`Pago: ${r.method}`);
  line();
  bytes.push(ESC, 0x61, 0x01);
  line("Gracias por su visita");
  bytes.push(LF, LF, LF);
  bytes.push(GS, 0x56, 0x00); // full cut
  return new Uint8Array(bytes);
}

/** Prompts for a USB printer (once) and prints the receipt. Throws on failure. */
export async function printReceipt(r: ReceiptData): Promise<void> {
  const u = usb();
  if (!u) throw new Error("WebUSB no está disponible en este navegador (usa Chrome).");

  const device = await u.requestDevice({ filters: [{ classCode: 7 }] }); // class 7 = printer
  await device.open();
  if (!device.configuration) await device.selectConfiguration(1);
  const iface = device.configuration?.interfaces[0];
  if (!iface) throw new Error("No se encontró una interfaz de impresora.");
  await device.claimInterface(iface.interfaceNumber);
  const outEndpoint = iface.alternates[0]?.endpoints.find((e) => e.direction === "out");
  if (!outEndpoint) throw new Error("La impresora no tiene endpoint de salida.");
  await device.transferOut(outEndpoint.endpointNumber, buildBuffer(r));
  await device.close();
}
