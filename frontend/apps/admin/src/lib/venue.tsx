"use client";

/**
 * Modo del local (PRD-019 RT-1913, ADR-036/039).
 *
 * La fuente de verdad es el backend (GET /venue, PR7): al montar y al volver a primer plano se
 * relee; ante un error de red o un 503 se conserva el último modo conocido (RF-1908).
 * `localStorage mestobeach_venue_mode` es solo la caché del último modo para el anti-destello.
 * Contrato: `{ mode, setMode, transitioning, phase, target, runTransition, refresh }`. `transitioning` lo gobierna `runTransition` (PR10, VenueTransition);
 * `phase` expone la fase de la animación. Un cambio visible debe pasar por `runTransition`;
 * `setMode` aplica el modo sin animación (arranque, otra pestaña).
 *
 * El tema NO depende de React: el script inline de app/layout.tsx ya fijó
 * `<html data-venue>` antes del primer pintado. Aquí solo se mantiene sincronizado.
 */
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { VenueTransition } from "@/components/VenueTransition";
import { venueApi } from "@/lib/api";

export type VenueMode = "RESTAURANT" | "NIGHT";

export const VENUE_STORAGE_KEY = "mestobeach_venue_mode";

const THEME_COLOR: Record<VenueMode, string> = { RESTAURANT: "#F4EFE7", NIGHT: "#161616" };

export type VenuePhase = "idle" | "collapse" | "singularity" | "emerge";

interface VenueContextValue {
  mode: VenueMode;
  setMode: (mode: VenueMode) => void;
  transitioning: boolean;
  phase: VenuePhase;
  /** Modo de destino mientras dura la animación (la transición pinta su marca). */
  target: VenueMode | null;
  /** Cambia de modo con la animación; `work` (recarga de datos) corre durante la singularidad (tope 4 s). */
  runTransition: (next: VenueMode, work?: () => Promise<void>) => Promise<void>;
  /** Relee GET /venue. Si el modo del servidor difiere, lo aplica con la animación. */
  refresh: () => Promise<void>;
}

const COLLAPSE_MS = 900;
const SINGULARITY_MIN_MS = 1000;
const WORK_TIMEOUT_MS = 4000;
const EMERGE_MS = 900;
const REDUCED_MS = 100; // 100 + 100 = fundido de 200 ms

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));
const prefersReducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const VenueContext = createContext<VenueContextValue>({
  mode: "RESTAURANT",
  setMode: () => {},
  transitioning: false,
  phase: "idle",
  target: null,
  runTransition: async () => {},
  refresh: async () => {},
});

function readStoredMode(): VenueMode {
  try {
    return localStorage.getItem(VENUE_STORAGE_KEY)?.toUpperCase() === "NIGHT" ? "NIGHT" : "RESTAURANT";
  } catch {
    return "RESTAURANT";
  }
}

/** Refleja el modo en el DOM: `data-venue` y el color de la barra del navegador (RF-1974). */
function applyToDocument(mode: VenueMode): void {
  document.documentElement.dataset.venue = mode === "NIGHT" ? "night" : "restaurant";
  let meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
  if (!meta) {
    meta = document.createElement("meta");
    meta.name = "theme-color";
    document.head.appendChild(meta);
  }
  meta.content = THEME_COLOR[mode];
}

export function VenueProvider({ children }: { children: ReactNode }) {
  // Se arranca en RESTAURANT para que servidor y primer render coincidan; el efecto lee el modo
  // real (el tema visual ya es el correcto gracias al script anti-destello).
  const [mode, setModeState] = useState<VenueMode>("RESTAURANT");
  const [phase, setPhase] = useState<VenuePhase>("idle");
  const [target, setTarget] = useState<VenueMode | null>(null);
  const wanted = useRef<VenueMode | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const busy = useRef(false);

  useEffect(() => {
    const stored = readStoredMode();
    setModeState(stored);
    applyToDocument(stored);

    // Otra pestaña del mismo dispositivo cambió el modo.
    const onStorage = (e: StorageEvent) => {
      if (e.key !== VENUE_STORAGE_KEY) return;
      const next = readStoredMode();
      setModeState(next);
      applyToDocument(next);
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, []);

  const setMode = useCallback((next: VenueMode) => {
    try {
      localStorage.setItem(VENUE_STORAGE_KEY, next);
    } catch {
      /* modo privado o almacenamiento bloqueado: el modo vive solo en memoria */
    }
    setModeState(next);
    applyToDocument(next);
  }, []);

  const runTransition = useCallback(
    async (next: VenueMode, work?: () => Promise<void>) => {
      // Siempre gana el último destino pedido: si llega otro cambio a mitad de la animación, la
      // singularidad aplica ese y no el que la arrancó (antes podía quedar el modo equivocado).
      wanted.current = next;
      const root = rootRef.current;
      if (!root) {
        setMode(next);
        return;
      }
      if (busy.current) return;
      busy.current = true;
      setTarget(next);
      const reduced = prefersReducedMotion();
      const setRootClass = (cls: string | null) => {
        root.classList.remove("venue-collapse", "venue-hold", "venue-emerge");
        if (cls) root.classList.add(cls);
      };
      // El agujero se abre en el centro de lo que se VE, aunque la página tenga scroll.
      root.style.transformOrigin = `50% ${window.scrollY + window.innerHeight / 2}px`;
      // Capa propia en la GPU ANTES del primer fotograma: si se promueve con la animación ya
      // corriendo, el arranque de la caída da un tirón.
      root.style.willChange = "transform, opacity";
      await new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r())));
      let applied: VenueMode = next;
      try {
        // 1) Colapso
        setPhase("collapse");
        setRootClass("venue-collapse");
        await sleep(reduced ? REDUCED_MS : COLLAPSE_MS);

        // 2) Singularidad: el modo cambia aquí, bajo el velo.
        setPhase("singularity");
        setRootClass("venue-hold");
        const minimum = sleep(reduced ? 0 : SINGULARITY_MIN_MS);
        const job = work ? work().catch(() => {}) : Promise.resolve();
        await Promise.all([minimum, Promise.race([job, sleep(WORK_TIMEOUT_MS)])]);
        applied = wanted.current ?? next;
        setTarget(applied);
        setMode(applied);

        // 3) Emersión
        setPhase("emerge");
        setRootClass("venue-emerge");
        await sleep(reduced ? REDUCED_MS : EMERGE_MS);
      } finally {
        setRootClass(null);
        root.style.removeProperty("transform-origin");
        root.style.removeProperty("will-change");
        root.style.removeProperty("filter");
        root.style.removeProperty("transform");
        setPhase("idle");
        busy.current = false;
      }
      // Llegó otro cambio durante la emersión: se encadena una segunda transición.
      if (wanted.current && wanted.current !== applied) await runTransitionRef.current(wanted.current);
    },
    [setMode],
  );
  const runTransitionRef = useRef(runTransition);
  runTransitionRef.current = runTransition;

  const transitioning = phase !== "idle";
  // Modo real del backend. Solo un 200 con un modo distinto cambia el tema (RF-1908).
  const modeRef = useRef<VenueMode>(mode);
  modeRef.current = mode;
  const refresh = useCallback(async () => {
    try {
      const { data } = await venueApi.get();
      const server: VenueMode = data.mode === "NIGHT" ? "NIGHT" : "RESTAURANT";
      if (server === modeRef.current) return;
      if (busy.current) return;
      await runTransition(server);
    } catch {
      /* sin red o 503: se conserva el último modo conocido */
    }
  }, [runTransition]);

  useEffect(() => {
    void refresh();
    const onVisible = () => {
      if (document.visibilityState === "visible") void refresh();
    };
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("focus", onVisible);
    return () => {
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("focus", onVisible);
    };
  }, [refresh]);

  const value = useMemo(
    () => ({ mode, setMode, transitioning, phase, target, runTransition, refresh }),
    [mode, setMode, transitioning, phase, target, runTransition, refresh],
  );
  return (
    <VenueContext.Provider value={value}>
      <div id="venue-root" ref={rootRef}>
        {children}
      </div>
      <VenueTransition />
    </VenueContext.Provider>
  );
}

export function useVenue(): VenueContextValue {
  return useContext(VenueContext);
}
