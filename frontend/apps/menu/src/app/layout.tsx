import type { Metadata, Viewport } from "next";
import { Barlow, Cormorant_Garamond, Gilda_Display } from "next/font/google";
import "./globals.css";
import { DialogProvider } from "@/components/Dialog";
import ConnectionBanner from "@/components/ConnectionBanner";
import { VenueProvider } from "@/lib/venue";

const barlow = Barlow({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-ui",
});
const gilda = Gilda_Display({
  subsets: ["latin"],
  weight: "400",
  variable: "--font-display",
});

// «Night» es serif itálica REAL (Cormorant Garamond 400 italic): Gilda no trae cursiva y una
// sintética la deformaría (docs/marca.md, PRD-019 D-12).
const cormorant = Cormorant_Garamond({
  subsets: ["latin"],
  weight: "400",
  style: "italic",
  variable: "--font-night",
});

// Anti-destello (RF-1974): fija data-venue ANTES del primer pintado leyendo el último modo
// conocido. Mismo criterio que VenueProvider (src/lib/venue.tsx): clave y valores.
const VENUE_BOOT = `(function(){try{var t=sessionStorage.getItem("mestobeach_tenant_id");var m=(t&&localStorage.getItem("mestobeach_venue_mode:"+t))||localStorage.getItem("mestobeach_venue_mode");document.documentElement.dataset.venue=m&&m.toUpperCase()==="NIGHT"?"night":"restaurant"}catch(e){}})();`;

export const metadata: Metadata = {
  title: "Mesto Beach — Menú",
  description: "Ordena desde tu mesa",
};

// Sin maximum-scale (RT-1912): el zoom del usuario no se bloquea. themeColor del modo día;
// VenueProvider lo cambia a #161616 en Night.
export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#F4EFE7",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="es"
      className={`${barlow.variable} ${gilda.variable} ${cormorant.variable}`}
      suppressHydrationWarning
    >
      <head>
        <script dangerouslySetInnerHTML={{ __html: VENUE_BOOT }} />
      </head>
      <body>
        <VenueProvider>
          <ConnectionBanner />
          <DialogProvider>{children}</DialogProvider>
        </VenueProvider>
      </body>
    </html>
  );
}
