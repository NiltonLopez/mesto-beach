import type { Config } from "tailwindcss";

// Sistema de diseño Mesto Beach (docs/marca.md + mock plano-sala).
// Los tokens semánticos son variables CSS por canal (definidas en globals.css),
// así soportan opacidad (bg-oro/60) y cambian con el tema claro/oscuro-cálido.
const token = (v: string) => `rgb(var(${v}) / <alpha-value>)`;

const config: Config = {
  // Night: el tema vive en <html data-venue="night"> (ADR-039). `dark:` lo activa.
  darkMode: ["selector", '[data-venue="night"]'],
  content: [
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    // También `lib`: ahí viven los mapas de clases del KDS (colores de botón, urgencia del
    // reloj…). Sin esta línea Tailwind no las ve, no genera esas reglas y los elementos salen
    // sin color — que es como el botón "Listo" acabó transparente sobre el crema.
    "./src/lib/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        // Escala de dorado arena (se mantiene: mucho código usa brand-500…900).
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
        // Tokens semánticos del sistema (claros por defecto, cálidos en oscuro).
        canvas: token("--canvas"), // fondo de la app (crema mate)
        paper: token("--paper"), // tarjetas / paneles
        paper2: token("--paper-2"), // superficie secundaria
        ink: token("--ink"), // texto principal / titulares
        muted: token("--muted"), // texto secundario
        hair: token("--hair"), // hairlines (dorado tenue)
        oro: token("--oro"), // acento / joyería
        arena: token("--arena"), // arena de la playa
        piedra: token("--piedra"), // muros del salón
        // Terracota del fuego: la acción de "sacar el plato" en el KDS. Vive aquí —y no como
        // hex suelto dentro de una clase— porque Tailwind solo genera las clases que encuentra
        // escritas literalmente en el código; compuesta en un objeto, `bg-[#D85A30]` nunca
        // llegaba al CSS y el botón salía transparente: blanco sobre crema, ilegible.
        fuego: {
          500: token("--fuego-500"),
          600: token("--fuego-600"),
          700: token("--fuego-700"),
        },
      },
      fontFamily: {
        sans: ["var(--font-ui)", "Barlow", "system-ui", "sans-serif"],
        display: ["var(--font-display)", "Gilda Display", "Georgia", "serif"],
        // «Night» en itálica serif real (nunca Gilda inclinada). Solo para BrandMark.
        night: ["var(--font-night)", "Cormorant Garamond", "Georgia", "serif"],
      },
      boxShadow: {
        // Sombras cálidas y difusas (marrón cálido, nunca negro duro).
        warm: "0 10px 30px -12px rgb(var(--sombra) / 0.18)",
        "warm-sm": "0 4px 14px -6px rgb(var(--sombra) / 0.16)",
        "warm-lg": "0 22px 60px -22px rgb(var(--sombra) / 0.28)",
      },
    },
  },
  plugins: [],
};

export default config;
