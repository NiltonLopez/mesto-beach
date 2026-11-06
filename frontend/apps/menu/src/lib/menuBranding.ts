import type { MenuBrandingDto, MenuTheme } from "@mestobeach/api-client";

const THEMES: Record<
  MenuTheme,
  { bg: string; surface: string; text: string; muted: string; border: string }
> = {
  light: { bg: "#f8fafc", surface: "#ffffff", text: "#0f172a", muted: "#64748b", border: "#e7ebf0" },
  cream: { bg: "#faf4e9", surface: "#fffdf8", text: "#3a3026", muted: "#8a7a66", border: "#ece0cd" },
  dark: { bg: "#17120d", surface: "#241b14", text: "#f4ece2", muted: "#b8a795", border: "#37291f" },
  // ADR-039: tema del modo NIGHT (lo sirve el servidor en el branding resuelto).
  night: { bg: "#161616", surface: "#1E1C1A", text: "#F4ECE2", muted: "#a89c8e", border: "#2e2a26" },
};

const FONTS: Record<string, { heading: string; body: string; gf: string }> = {
  mestobeach: { heading: "Gilda Display", body: "Barlow", gf: "Gilda+Display&family=Barlow:wght@400;500;600;700" },
  manrope: { heading: "Manrope", body: "Manrope", gf: "Manrope:wght@400;600;700;800" },
  poppins: { heading: "Poppins", body: "Poppins", gf: "Poppins:wght@400;500;600;700" },
  fraunces: { heading: "Fraunces", body: "Hanken Grotesk", gf: "Fraunces:opsz,wght@9..144,500;9..144,600&family=Hanken+Grotesk:wght@400;500;600;700" },
  playfair: { heading: "Playfair Display", body: "Mulish", gf: "Playfair+Display:wght@500;600;700&family=Mulish:wght@400;500;600;700" },
  bricolage: { heading: "Bricolage Grotesque", body: "DM Sans", gf: "Bricolage+Grotesque:opsz,wght@12..96,500;12..96,700&family=DM+Sans:wght@400;500;600;700" },
  pacifico: { heading: "Pacifico", body: "Nunito", gf: "Pacifico&family=Nunito:wght@400;500;600;700" },
};

function accentInk(hex: string): string {
  const m = /^#?([0-9a-f]{6})$/i.exec((hex ?? "").trim());
  if (!m) return "#ffffff";
  const n = parseInt(m[1]!, 16);
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return lum > 0.62 ? "#1a1207" : "#ffffff";
}

// ── Custom background → derived, readable palette ─────────────────────────────
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
function luminance(rgb: [number, number, number]): number {
  return (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
}
/** Derive a full, legible menu palette (surface/text/muted/border) from any background color. */
export function paletteFromBg(hex: string): { bg: string; surface: string; text: string; muted: string; border: string } {
  const rgb = parseHex(hex) ?? [248, 250, 252];
  const dark = luminance(rgb) < 0.5;
  const white: [number, number, number] = [255, 255, 255];
  const black: [number, number, number] = [0, 0, 0];
  const text: [number, number, number] = dark ? [246, 243, 238] : [22, 24, 29];
  return {
    bg: toHex(rgb),
    surface: toHex(mix(rgb, white, dark ? 0.08 : 0.6)),
    text: toHex(text),
    muted: toHex(mix(text, rgb, 0.42)),
    border: toHex(dark ? mix(rgb, white, 0.16) : mix(rgb, black, 0.09)),
  };
}

/** True if the menu's effective background is dark (callers may want dark-aware overlays). */
export function isDarkTheme(branding?: MenuBrandingDto | null): boolean {
  if (branding?.backgroundColor) {
    const rgb = parseHex(branding.backgroundColor);
    if (rgb) return luminance(rgb) < 0.5;
  }
  return branding?.theme === "dark";
}

export function fontHead(branding?: MenuBrandingDto | null): string {
  const f = FONTS[branding?.fontKey ?? "mestobeach"] ?? FONTS["mestobeach"]!;
  return `"${f.heading}", system-ui, sans-serif`;
}

const FONT_LINK_ID = "menu-tenant-font";
const THEMED_ATTR = "data-menu-themed";

/** Applies the tenant branding to the whole menu surface (CSS vars, font, background). */
export function applyMenuBranding(branding: MenuBrandingDto | null | undefined): void {
  if (typeof document === "undefined") return;
  const root = document.documentElement;

  // A custom background color (if set) wins over the light/cream/dark preset.
  const theme = branding?.backgroundColor
    ? paletteFromBg(branding.backgroundColor)
    : (THEMES[branding?.theme ?? "light"] ?? THEMES.light);
  const font = FONTS[branding?.fontKey ?? "mestobeach"] ?? FONTS["mestobeach"]!;
  const accent = branding?.primaryColor || "#aa8453";

  root.style.setProperty("--m-bg", theme.bg);
  root.style.setProperty("--m-surface", theme.surface);
  root.style.setProperty("--m-text", theme.text);
  root.style.setProperty("--m-muted", theme.muted);
  root.style.setProperty("--m-border", theme.border);
  root.style.setProperty("--m-accent", accent);
  root.style.setProperty("--m-accent-ink", accentInk(accent));
  root.style.setProperty("--m-font-body", `"${font.body}", system-ui, sans-serif`);
  root.style.setProperty("--m-font-head", `"${font.heading}", system-ui, sans-serif`);

  let link = document.getElementById(FONT_LINK_ID) as HTMLLinkElement | null;
  if (!link) {
    link = document.createElement("link");
    link.id = FONT_LINK_ID;
    link.rel = "stylesheet";
    document.head.appendChild(link);
  }
  link.href = `https://fonts.googleapis.com/css2?family=${font.gf}&display=swap`;

  document.body.setAttribute(THEMED_ATTR, "true");
}

/** Reverts to the default Mesto Beach look (used when leaving the menu / on the login). */
export function resetMenuBranding(): void {
  if (typeof document === "undefined") return;
  const root = document.documentElement;
  for (const v of ["--m-bg", "--m-surface", "--m-text", "--m-muted", "--m-border", "--m-accent", "--m-accent-ink", "--m-font-body", "--m-font-head"]) {
    root.style.removeProperty(v);
  }
  document.body.removeAttribute(THEMED_ATTR);
}
