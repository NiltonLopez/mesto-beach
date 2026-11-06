"use client";

/**
 * Modo del local (PRD-019 RT-1913, ADR-036/039).
 *
 * La fuente de verdad es el backend: al montar se pide GET /api/v1/venue (modo + estaciones con
 * flowProfile/isOn) y el OrdersHub avisa con `OrderEvent` de tipo VENUE_MODE_CHANGED, que dispara
 * `runTransition` y una relectura. Ante un error de red se conserva el último modo conocido.
 * `localStorage mestobeach_venue_mode` es solo caché anti-destello (lo lee el script de layout.tsx).
 * Contrato: `{ mode, stations, setMode, transitioning, phase, target, runTransition, refetch }`. Un cambio
 * visible debe pasar por `runTransition`; `setMode` aplica el modo sin animación (arranque, otra pestaña).
 *
 * El tema NO depende de React: el script inline de app/layout.tsx ya fijó
 * `<html data-venue>` antes del primer pintado. Aquí solo se mantiene sincronizado.
 */
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import * as signalR from "@microsoft/signalr";
import type { VenueStationDto } from "@mestobeach/api-client";
import { VenueTransition } from "@/components/VenueTransition";
import { getToken, ORDERS_HUB_URL, venueApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

export type VenueMode = "RESTAURANT" | "NIGHT";

export const VENUE_STORAGE_KEY = "mestobeach_venue_mode";

const THEME_COLOR: Record<VenueMode, string> = { RESTAURANT: "#F4EFE7", NIGHT: "#161616" };

export type VenuePhase = "idle" | "collapse" | "singularity" | "emerge";

interface VenueContextValue {
  mode: VenueMode;
  /** Estaciones del local con su perfil de flujo y si están encendidas en el modo vigente. */
  stations: VenueStationDto[];
  /** Relee GET /venue; ante un error conserva lo último que se sabía. */
  refetch: () => Promise<void>;
  setMode: (mode: VenueMode) => void;
  transitioning: boolean;
  phase: VenuePhase;
  /** Modo de destino mientras dura la animación (la transición pinta su marca). */
  target: VenueMode | null;
  /** Cambia de modo con la animación; `work` (recarga de datos) corre durante la singularidad (tope 4 s). */
  runTransition: (next: VenueMode, work?: () => Promise<void>) => Promise<void>;
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
  stations: [],
  refetch: async () => {},
  setMode: () => {},
  transitioning: false,
  phase: "idle",
  target: null,
  runTransition: async () => {},
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
  const [stations, setStations] = useState<VenueStationDto[]>([]);
  const busy = useRef(false);
  const tenantId = useAuthStore((st) => st.staff?.tenantId ?? null);

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

  // ── Fuente de verdad: GET /venue ───────────────────────────────────────────
  const refetch = useCallback(async () => {
    if (!getToken()) return;
    try {
      const { data } = await venueApi.get();
      setStations(data.stations ?? []);
      setMode(data.mode === "NIGHT" ? "NIGHT" : "RESTAURANT");
    } catch {
      /* se conserva el último modo conocido (RF-1913) */
    }
  }, [setMode]);

  // Valores vigentes para los manejadores del socket, sin reconstruir la conexión en cada cambio.
  const modeRef = useRef<VenueMode>(mode);
  modeRef.current = mode;
  const runRef = useRef(runTransition);
  runRef.current = runTransition;
  const refetchRef = useRef(refetch);
  refetchRef.current = refetch;

  useEffect(() => {
    if (!tenantId) return;
    void refetchRef.current();

    const token = getToken();
    if (!token) return;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${ORDERS_HUB_URL}?access_token=${token}`, {
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on("OrderEvent", (payload: unknown) => {
      const e = payload as { type?: string; mode?: string };
      if (e?.type !== "VENUE_MODE_CHANGED") return;
      const next: VenueMode = e.mode?.toUpperCase() === "NIGHT" ? "NIGHT" : "RESTAURANT";
      if (next === modeRef.current) {
        void refetchRef.current();
        return;
      }
      // La relectura corre bajo el velo (singularidad); setMode del proveedor aplica el modo.
      void runRef.current(next, () => refetchRef.current());
    });
    connection.onreconnected(() => {
      void connection.invoke("JoinTenantGroup", tenantId).catch(() => {});
      void refetchRef.current(); // lo que pasara mientras estaba caído
    });

    // stop() solo tras resolver start(): evita abortar la negociación con el doble montaje de StrictMode.
    const started = connection
      .start()
      .then(() => connection.invoke("JoinTenantGroup", tenantId))
      .catch(() => {});
    return () => {
      void started.finally(() => connection.stop().catch(() => {}));
    };
  }, [tenantId]);

  const transitioning = phase !== "idle";
  const value = useMemo(
    () => ({ mode, stations, refetch, setMode, transitioning, phase, target, runTransition }),
    [mode, stations, refetch, setMode, transitioning, phase, target, runTransition],
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
