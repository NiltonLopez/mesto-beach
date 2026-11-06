import type { Config } from "tailwindcss";

// Sistema de diseño Mesto Beach (docs/marca.md + mock plano-sala).
// Tokens semánticos como variables CSS por canal (definidas en globals.css):
// soportan opacidad (bg-oro/60) y comparten el lenguaje con staff/menu.
const token = (v: string) => `rgb(var(${v}) / <alpha-value>)`;

const config: Config = {
  // Night: el tema vive en <html data-venue="night"> (ADR-039). `dark:` lo activa.
  darkMode: ["selector", '[data-venue="night"]'],
  content: [
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    // También `lib`: si algún día se compone una clase ahí, Tailwind debe verla.
    "./src/lib/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        brand: {
          50: token("--brand-50"),
          100: token("--brand-100"),
          200: token("--brand-200"),
          300: token("--brand-300"),
          400: token("--brand-400"),
          500: token("--brand-500"),
          600: token("--brand-600"),
          700: token("--brand-700"),
          800: token("--brand-800"),
          900: token("--brand-900"),
        },
        canvas: token("--canvas"),
        paper: token("--paper"),
        paper2: token("--paper-2"),
        ink: token("--ink"),
        muted: token("--muted"),
        hair: token("--hair"),
        oro: token("--oro"),
        arena: token("--arena"),
        piedra: token("--piedra"),
      },
      fontFamily: {
        sans: ["var(--font-ui)", "Barlow", "system-ui", "sans-serif"],
        display: ["var(--font-display)", "Gilda Display", "Georgia", "serif"],
        // «Night» en itálica serif real (nunca Gilda inclinada). Solo para BrandMark.
        night: ["var(--font-night)", "Cormorant Garamond", "Georgia", "serif"],
      },
      boxShadow: {
        warm: "0 10px 30px -12px rgb(var(--sombra) / 0.18)",
        "warm-sm": "0 4px 14px -6px rgb(var(--sombra) / 0.16)",
        "warm-lg": "0 22px 60px -22px rgb(var(--sombra) / 0.28)",
      },
    },
  },
  plugins: [],
};

export default config;
