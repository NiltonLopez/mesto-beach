"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { VenueMode, VenuePreflightDto } from "@mestobeach/api-client";
import { ArrowsClockwise, MoonStars, Storefront } from "@phosphor-icons/react";
import { venueApi } from "@/lib/api";
import { useDialog } from "@/components/Dialog";
import { useVenue } from "@/lib/venue";
import { useAuthStore } from "@/lib/stores";

/**
 * Interruptor de modo del local (PRD-019 RF-1902..1907, ADR-036).
 *
 * Diálogo propio de la app (ADR-033, sin confirm nativo): pre-flight en vivo, cuenta atrás de 5 s
 * antes de habilitar «Cambiar». El historial de cambios sigue auditado en el backend (GET /venue/history)
 * pero no se muestra aquí: en este momento solo estorba. No existe `force`: si el local no está vacío, solo se
 * muestra qué lo bloquea.
 */
const COUNTDOWN_S = 5;

const modeName = (m: VenueMode) => (m === "NIGHT" ? "Night" : "Restaurante");

interface NotIdleBody {
  title?: string;
  openSessions?: number;
  activeItems?: number;
  openTrips?: number;
  orphanItems?: number;
  tables?: string[];
}

export function VenueSwitch() {
  const { mode, runTransition, transitioning } = useVenue();
  const dialog = useDialog();
  const isAdmin = useAuthStore((st) => st.staff?.roles?.includes("Admin") ?? false);
  const [open, setOpen] = useState(false);
  const target: VenueMode = mode === "NIGHT" ? "RESTAURANT" : "NIGHT";

  const [pre, setPre] = useState<VenuePreflightDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [left, setLeft] = useState(COUNTDOWN_S);
  const [busy, setBusy] = useState(false);
  const cancelRef = useRef<HTMLButtonElement>(null);

  const loadPreflight = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const { data } = await venueApi.preflight();
      setPre(data);
      setLeft(COUNTDOWN_S);
    } catch {
      setPre(null);
      setLoadError("No se pudo comprobar el estado del local. Inténtalo de nuevo.");
    } finally {
      setLoading(false);
    }
  }, []);

  // Al abrir: pre-flight.
  useEffect(() => {
    if (!open) return;
    void loadPreflight();
    cancelRef.current?.focus();
  }, [open, loadPreflight]);

  // Cuenta atrás: solo corre con el local listo.
  useEffect(() => {
    if (!open || !pre?.ready || left <= 0) return;
    const id = window.setTimeout(() => setLeft((v) => v - 1), 1000);
    return () => window.clearTimeout(id);
  }, [open, pre?.ready, left]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) setOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, busy]);

  async function confirmChange() {
    if (!pre?.ready || left > 0 || busy) return;
    setBusy(true);
    try {
      await venueApi.changeMode(target);
      setOpen(false);
      await runTransition(target);
      dialog.toast(`Local en modo ${modeName(target)}.`, { tone: "success" });
    } catch (err: unknown) {
      const res = (err as { response?: { status?: number; data?: NotIdleBody & { detail?: string } } })?.response;
      if (res?.status === 409 && res.data?.title === "VENUE_NOT_IDLE") {
        const d = res.data;
        setPre({
          ready: false,
          openSessions: d.openSessions ?? 0,
          activeItems: d.activeItems ?? 0,
          openTrips: d.openTrips ?? 0,
          orphanItems: d.orphanItems ?? 0,
          tables: d.tables ?? [],
        });
      } else {
        dialog.toast(res?.data?.detail ?? "No se pudo cambiar el modo.", { tone: "error" });
      }
    } finally {
      setBusy(false);
    }
  }

  if (!isAdmin) return null;

  const Icon = mode === "NIGHT" ? MoonStars : Storefront;

  return (
    <>
      {/* Dos líneas para que nada se recorte en la barra de 14rem: el modo actual arriba (Night con
          su itálica dorada, como la marca) y la acción debajo. */}
      <button
        onClick={() => setOpen(true)}
        disabled={transitioning}
        aria-label={`Modo del local: ${modeName(mode)}. Cambiar a ${modeName(target)}`}
        className="group flex min-h-[52px] w-full items-center gap-3 rounded-lg px-3 py-2 text-left transition hover:bg-paper2 disabled:opacity-50"
      >
        <span className="grid h-8 w-8 flex-shrink-0 place-items-center rounded-full bg-oro/[0.12] text-oro ring-1 ring-oro/25">
          <Icon size={17} weight={mode === "NIGHT" ? "fill" : "light"} aria-hidden />
        </span>
        <span className="min-w-0 flex-1 leading-tight">
          <span className="block text-[10px] font-semibold uppercase tracking-[0.12em] text-muted">Modo del local</span>
          {mode === "NIGHT" ? (
            <span className="block font-night text-xl italic leading-none text-oro">Night</span>
          ) : (
            <span className="block text-sm font-semibold text-ink">Restaurante</span>
          )}
        </span>
        <ArrowsClockwise
          size={16}
          aria-hidden
          className="flex-shrink-0 text-muted transition group-hover:rotate-180 group-hover:text-ink motion-reduce:transition-none"
        />
      </button>

      {open && (
        <div
          className="fixed inset-0 z-[60] flex items-center justify-center bg-ink/40 p-4"
          role="presentation"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget && !busy) setOpen(false);
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="venue-switch-title"
            className="mestobeach-dialog-anim max-h-[90dvh] w-full max-w-md overflow-y-auto rounded-2xl border border-hair/40 bg-paper p-5 shadow-warm-lg"
          >
            <h2 id="venue-switch-title" className="font-display text-lg text-ink">
              Cambiar a {modeName(target)}
            </h2>
            <p className="mt-1 text-sm leading-snug text-muted">
              Ahora el local está en modo <strong>{modeName(mode)}</strong>. El cambio afecta a todo el local y solo es
              posible con el salón vacío.
            </p>

            <div className="mt-4" aria-live="polite">
              {loading && <p className="text-sm text-muted">Comprobando el estado del local…</p>}
              {loadError && <p className="text-sm text-rose-600">{loadError}</p>}

              {pre && !loading && !pre.ready && (
                <div className="rounded-xl border border-rose-400/40 bg-rose-500/10 p-3 text-sm">
                  <p className="font-semibold text-rose-700">El local aún no está vacío</p>
                  <ul className="mt-1 list-disc space-y-0.5 pl-5 text-ink">
                    {pre.openSessions > 0 && <li>{pre.openSessions} sesión(es) abierta(s)</li>}
                    {pre.activeItems > 0 && <li>{pre.activeItems} plato(s) sin finalizar</li>}
                    {pre.openTrips > 0 && <li>{pre.openTrips} viaje(s) del runner abierto(s)</li>}
                  </ul>
                  {pre.tables.length > 0 && (
                    <p className="mt-2 text-xs text-muted">
                      Mesas abiertas: <span className="font-semibold text-ink">{pre.tables.join(", ")}</span>
                    </p>
                  )}
                </div>
              )}

              {pre?.ready && !loading && (
                <p className="rounded-xl border border-hair/40 bg-paper2 p-3 text-sm text-ink">
                  El salón está vacío: puedes cambiar de modo.
                </p>
              )}

              {pre && pre.orphanItems > 0 && !loading && (
                <p className="mt-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900">
                  Hay {pre.orphanItems} plato(s) huérfano(s) de mesas ya cerradas. No bloquean el cambio, pero no
                  podrán avanzar en su tablero.
                </p>
              )}
            </div>

            <div className="mt-5 flex flex-wrap justify-end gap-2.5">
              <button
                ref={cancelRef}
                type="button"
                onClick={() => setOpen(false)}
                disabled={busy}
                className="min-h-[44px] rounded-xl border border-hair/55 px-4 py-2.5 text-sm font-semibold text-muted transition hover:bg-paper2"
              >
                Cancelar
              </button>
              {pre && !pre.ready ? (
                <button
                  type="button"
                  onClick={() => void loadPreflight()}
                  disabled={loading}
                  className="min-h-[44px] rounded-xl bg-brand-500 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-600 disabled:opacity-50"
                >
                  Volver a comprobar
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => void confirmChange()}
                  disabled={!pre?.ready || left > 0 || busy}
                  className="min-h-[44px] rounded-xl bg-brand-500 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-600 disabled:opacity-40"
                >
                  {busy
                    ? "Cambiando…"
                    : pre?.ready && left > 0
                      ? `Cambiar a ${modeName(target)} (${left})`
                      : `Cambiar a ${modeName(target)}`}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </>
  );
}
