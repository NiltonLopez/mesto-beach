import type { MenuTheme } from "@mestobeach/api-client";

/** Background presets the admin can choose for the customer menu. */
export const THEMES: Record<
  MenuTheme,
  { label: string; bg: string; surface: string; text: string; muted: string; border: string }
> = {
  light: { label: "Claro", bg: "#f8fafc", surface: "#ffffff", text: "#0f172a", muted: "#64748b", border: "#e7ebf0" },
  cream: { label: "Crema", bg: "#faf4e9", surface: "#fffdf8", text: "#3a3026", muted: "#8a7a66", border: "#ece0cd" },
  dark: { label: "Oscuro", bg: "#17120d", surface: "#241b14", text: "#f4ece2", muted: "#b8a795", border: "#37291f" },
  // ADR-039: tema del modo NIGHT.
  night: { label: "Night", bg: "#161616", surface: "#1E1C1A", text: "#F4ECE2", muted: "#a89c8e", border: "#2e2a26" },
};

/** Curated font pairings (heading + body) loaded from Google Fonts. */
export const FONTS: Record<
  string,
  { label: string; heading: string; body: string; gf: string }
> = {
  manrope: { label: "Moderna", heading: "Manrope", body: "Manrope", gf: "Manrope:wght@400;600;700;800" },
  poppins: { label: "Redondeada", heading: "Poppins", body: "Poppins", gf: "Poppins:wght@400;500;600;700" },
  fraunces: {
    label: "Serif cálida",
    heading: "Fraunces",
    body: "Hanken Grotesk",
    gf: "Fraunces:opsz,wght@9..144,500;9..144,600&family=Hanken+Grotesk:wght@400;500;600;700",
  },
  playfair: {
    label: "Elegante",
    heading: "Playfair Display",
    body: "Mulish",
    gf: "Playfair+Display:wght@500;600;700&family=Mulish:wght@400;500;600;700",
  },
  bricolage: {
    label: "Con carácter",
    heading: "Bricolage Grotesque",
    body: "DM Sans",
    gf: "Bricolage+Grotesque:opsz,wght@12..96,500;12..96,700&family=DM+Sans:wght@400;500;600;700",
  },
  // PRD-019: tipografía de Mesto Beach (mismo par que el menú).
  mestobeach: {
    label: "Mesto Beach",
    heading: "Gilda Display",
    body: "Barlow",
    gf: "Gilda+Display&family=Barlow:wght@400;500;600;700",
  },
  pacifico: {
    label: "Casual",
    heading: "Pacifico",
    body: "Nunito",
    gf: "Pacifico&family=Nunito:wght@400;500;600;700",
  },
};

export const ACCENT_SWATCHES = [
  "#f97316", "#e11d48", "#db2777", "#7c3aed", "#2563eb",
  "#0891b2", "#059669", "#65a30d", "#ca8a04", "#92400e",
];

/** Black or white text that reads on top of a given accent color. */
export function accentInk(hex: string): string {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return "#ffffff";
  const n = parseInt(m[1]!, 16);
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  // perceived luminance
  const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return lum > 0.62 ? "#1a1207" : "#ffffff";
}

export function googleFontsHref(fontKey: string): string {
  const f = FONTS[fontKey] ?? FONTS["manrope"]!;
  return `https://fonts.googleapis.com/css2?family=${f.gf}&display=swap`;
}

// ── Custom background → derived, readable palette (mirrors the customer menu) ──
function parseHex(hex: string): [number, number, number] | null {
  const m = /^#?([0-9a-f]{6})$/i.exec((hex ?? "").trim());
  if (!m) return null;
  const n = parseInt(m[1]!, 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
function toHex([r, g, b]: [number, number, number]): string {
  const h = (v: number) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, "0");
  return `#${h(r)}${h(g)}${h(b)}`;
}
function mix(a: [number, number, number], b: [number, number, number], t: number): [number, number, number] {
  return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
}
/** Derive surface/text/muted/border from any background color, matching the customer menu. */
export function paletteFromBg(hex: string): { label: string; bg: string; surface: string; text: string; muted: string; border: string } {
  const rgb = parseHex(hex) ?? [248, 250, 252];
  const dark = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255 < 0.5;
  const white: [number, number, number] = [255, 255, 255];
  const black: [number, number, number] = [0, 0, 0];
  const text: [number, number, number] = dark ? [246, 243, 238] : [22, 24, 29];
  return {
    label: "Personalizado",
    bg: toHex(rgb),
    surface: toHex(mix(rgb, white, dark ? 0.08 : 0.6)),
    text: toHex(text),
    muted: toHex(mix(text, rgb, 0.42)),
    border: toHex(dark ? mix(rgb, white, 0.16) : mix(rgb, black, 0.09)),
  };
}
