"use client";

/**
 * Modo del local (PRD-019 RT-1913, ADR-036/039).
 *
 * El modo viene del backend: se sondea GET /api/v1/venue/{tenantId} cada 30 s y al volver a la
 * pestaña (visibilitychange). El último modo conocido se guarda en `localStorage`
 * `mestobeach_venue_mode:{tenantId}` (y en la clave sin tenant, que lee el script anti-destello).
 * Si el modo cambia, `runTransition` envuelve: recarga del catálogo (la registra la página de la
 * sesión con `useVenueReload`) -> branding ya resuelto por el servidor -> limpieza del carrito.
 * Contrato: `{ mode, setMode, transitioning, phase, target, runTransition, setReloader }`.
 *
 * El tema NO depende de React: el script inline de app/layout.tsx ya fijó
 * `<html data-venue>` antes del primer pintado. Aquí solo se mantiene sincronizado.
 */
import { usePathname } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { venueApi, getStoredSession } from "@/lib/api";
import { VenueTransition } from "@/components/VenueTransition";

export type VenueMode = "RESTAURANT" | "NIGHT";

export const VENUE_STORAGE_KEY = "mestobeach_venue_mode";
export const venueStorageKey = (tenantId: string) => `${VENUE_STORAGE_KEY}:${tenantId}`;

const POLL_MS = 30_000;

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
  /** La página de la sesión registra aquí cómo recargar la carta al cambiar de modo (null para quitarla). */
  setReloader: (fn: ((next: VenueMode) => Promise<void>) | null) => void;
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
  setReloader: () => {},
});

function readStoredMode(tenantId?: string | null): VenueMode {
  try {
    const raw = (tenantId ? localStorage.getItem(venueStorageKey(tenantId)) : null) ?? localStorage.getItem(VENUE_STORAGE_KEY);
    return raw?.toUpperCase() === "NIGHT" ? "NIGHT" : "RESTAURANT";
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
  const modeRef = useRef<VenueMode>("RESTAURANT");
  const reloader = useRef<((next: VenueMode) => Promise<void>) | null>(null);
  const pathname = usePathname();

  const setReloader = useCallback((fn: ((next: VenueMode) => Promise<void>) | null) => {
    reloader.current = fn;
  }, []);

  const applyMode = useCallback((next: VenueMode) => {
    modeRef.current = next;
    setModeState(next);
    applyToDocument(next);
  }, []);

  // Modo guardado para el tenant de la sesión actual (cambia al navegar: /mesa -> /session).
  useEffect(() => {
    applyMode(readStoredMode(getStoredSession()?.tenantId));
  }, [pathname, applyMode]);

  const setMode = useCallback(
    (next: VenueMode) => {
      try {
        const tenantId = getStoredSession()?.tenantId;
        if (tenantId) localStorage.setItem(venueStorageKey(tenantId), next);
        localStorage.setItem(VENUE_STORAGE_KEY, next); // último conocido, para el script anti-destello
      } catch {
        /* modo privado o almacenamiento bloqueado: el modo vive solo en memoria */
      }
      applyMode(next);
    },
    [applyMode],
  );

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

  // Sondeo del modo real (RF-191x): cada 30 s y al volver a la pestaña. Un fallo de red no cambia nada.
  useEffect(() => {
    let alive = true;
    const tick = async () => {
      const tenantId = getStoredSession()?.tenantId;
      if (!tenantId || busy.current) return;
      try {
        const { data } = await venueApi.getPublic(tenantId);
        if (!alive || busy.current) return;
        const next: VenueMode = data.mode === "NIGHT" ? "NIGHT" : "RESTAURANT";
        if (next === modeRef.current) return;
        const reload = reloader.current;
        await runTransition(next, reload ? () => reload(next) : undefined);
      } catch {
        /* sin red o 5xx: se conserva el último modo conocido hasta el siguiente sondeo */
      }
    };
    void tick();
    const id = setInterval(() => void tick(), POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") void tick();
    };
    document.addEventListener("visibilitychange", onVisible);
    // Otra pestaña del mismo dispositivo ya cambió el modo: se sigue sin repetir la animación.
    const onStorage = (e: StorageEvent) => {
      if (!e.key || !e.key.startsWith(VENUE_STORAGE_KEY)) return;
      const tenantId = getStoredSession()?.tenantId;
      if (e.key !== VENUE_STORAGE_KEY && e.key !== (tenantId ? venueStorageKey(tenantId) : "")) return;
      applyMode(readStoredMode(tenantId));
    };
    window.addEventListener("storage", onStorage);
    return () => {
      alive = false;
      clearInterval(id);
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("storage", onStorage);
    };
  }, [pathname, runTransition, applyMode]);

  const transitioning = phase !== "idle";
  const value = useMemo(
    () => ({ mode, setMode, transitioning, phase, target, runTransition, setReloader }),
    [mode, setMode, transitioning, phase, target, runTransition, setReloader],
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
