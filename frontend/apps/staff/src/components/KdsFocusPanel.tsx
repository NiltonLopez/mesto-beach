"use client";

import { useEffect } from "react";
import { X } from "@phosphor-icons/react";
import type { KdsFlowProfile, KdsStationDto, OrderItemDto } from "@mestobeach/api-client";
import { ReservadoPill } from "@/components/ReservadoPill";
import type { KdsOrder } from "@/lib/stores";
import KdsRecorrido from "@/components/KdsRecorrido";
import {
  buildRecorrido,
  clockLabel,
  columnDef,
  columnOf,
  stateLabel,
  stationColor,
  stationName,
  tint,
  urgencyClass,
  KDS_ACTION_CLASS,
  KDS_ACTION_LABEL,
  type KdsActionKind,
} from "@/lib/kds";

const ORO = "#AA8453";

interface Props {
  order: KdsOrder;
  lensStationId: string | null;
  stations: KdsStationDto[];
  sector: string | null;
  now: number;
  /** Perfil de flujo de la estación elegida (ADR-037). */
  profile?: KdsFlowProfile;
  /** Perfil de cualquier estación, para dibujar el recorrido de los platos ajenos. */
  profileOf?: (stationId: string | null | undefined) => KdsFlowProfile;
  busyItemId: string | null;
  onAction: (kind: KdsActionKind, itemId: string) => void;
  onClose: () => void;
}

/**
 * Modo foco: una comanda en primer plano con el tablero difuminado detrás. Sirve para
 * la pregunta del pase — "¿esta mesa sale junta?" —: muestra tus platos, los de las otras
 * estaciones y, en ambos, cuánto lleva cada plato en cada paso de su viaje.
 */
export default function KdsFocusPanel({
  order,
  lensStationId,
  stations,
  sector,
  now,
  profile = "FULL_PASS",
  profileOf,
  busyItemId,
  onAction,
  onClose,
}: Props) {
  // Esc cierra, igual que la × y el clic en el fondo difuminado.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const vivos = order.items.filter((i) => i.status !== "CANCELLED");
  const propios = vivos.filter(
    (i) => lensStationId === null || (i.stationId ?? null) === lensStationId
  );
  const ajenos = vivos.filter((i) => !propios.includes(i));

  const lensLabel = lensStationId ? stationName(lensStationId, stations) : "Todas";
  const subtitulo = [sector, order.customerName, `#${order.id.slice(0, 8).toUpperCase()}`]
    .filter(Boolean)
    .join(" · ");

  return (
    <div
      className="mestobeach-backdrop-in fixed inset-0 z-[55] flex items-center justify-center bg-ink/40 p-4 sm:p-8"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={`Comanda de ${order.tableCode ?? "la mesa"}`}
        className="mestobeach-card-in flex max-h-full w-full max-w-4xl flex-col overflow-hidden rounded-2xl border border-hair/40 bg-paper shadow-warm-lg"
      >
        {/* Cabecera */}
        <header className="flex items-start justify-between gap-4 border-b border-hair/40 px-6 py-5">
          <div className="flex min-w-0 flex-col gap-1">
            <span className="flex flex-wrap items-center gap-3 font-display text-4xl font-normal leading-none text-ink">
              {order.tableCode ?? "Sin mesa"}
              {(order.priority ?? 0) > 0 && <ReservadoPill />}
            </span>
            {subtitulo && <span className="truncate text-sm text-muted">{subtitulo}</span>}
          </div>
          <div className="flex shrink-0 items-center gap-4">
            <span
              className={`text-3xl font-bold tabular-nums ${urgencyClass(order.createdAt, now)}`}
            >
              {clockLabel(order.createdAt, now)}
            </span>
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar"
              className="grid h-11 w-11 place-items-center rounded-xl border border-hair/55 text-muted transition hover:bg-paper2 hover:text-ink"
            >
              <X size={20} weight="bold" aria-hidden />
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-y-auto px-6 py-5">
          {/* Observación de la comanda */}
          {order.observacion && (
            <div className="mb-5 rounded-xl bg-oro/[0.12] px-4 py-3 text-brand-700">
              <span className="block text-[10px] font-bold uppercase tracking-wide text-brand-700/70">
                Observación
              </span>
              <span className="text-base font-semibold">{order.observacion}</span>
            </div>
          )}

          {/* Tus platos */}
          <h3 className="font-display text-lg font-normal text-ink">
            Tu estación · {lensLabel}
          </h3>
          {propios.length === 0 ? (
            <p className="mt-2 text-sm text-muted">Esta comanda no tiene platos tuyos.</p>
          ) : (
            <ul className="mt-3 flex flex-col gap-3">
              {propios.map((item) => (
                <PlatoPropio
                  key={item.id}
                  item={item}
                  order={order}
                  now={now}
                  busy={busyItemId === item.id}
                  station={stationName(item.stationId, stations)}
                  profile={profile}
                  onAction={onAction}
                />
              ))}
            </ul>
          )}

          {/* Lo de las otras estaciones (ADR-029): informativo, nunca accionable */}
          {ajenos.length > 0 && (
            <>
              <h3 className="mt-7 font-display text-lg font-normal text-ink">
                También en esta comanda
              </h3>
              <ul className="mt-3 flex flex-col gap-3">
                {ajenos.map((item) => {
                  const color = stationColor(item.stationId, stations);
                  return (
                    <li
                      key={item.id}
                      className="rounded-xl border-l-4 px-4 py-3"
                      style={{ borderLeftColor: color, backgroundColor: tint(color) }}
                    >
                      <span
                        className="block text-[10px] font-bold uppercase tracking-wide"
                        style={{ color }}
                      >
                        También · {stationName(item.stationId, stations)}
                      </span>
                      <span className="block text-base font-bold text-ink">
                        {item.quantity}× {item.itemNameSnapshot}
                        <span className="ml-2 text-sm font-normal italic text-muted">
                          {stateLabel(item.status)}
                        </span>
                      </span>
                      <div className="mt-2.5">
                        <KdsRecorrido
                          steps={buildRecorrido(
                            item,
                            order.createdAt,
                            now,
                            profileOf ? profileOf(item.stationId) : "FULL_PASS"
                          )}
                          currentColor={color}
                        />
                      </div>
                    </li>
                  );
                })}
              </ul>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

// ─── Un plato tuyo, en grande y con su recorrido ──────────────────────────────

function PlatoPropio({
  item,
  order,
  now,
  busy,
  station,
  profile,
  onAction,
}: {
  item: OrderItemDto;
  order: KdsOrder;
  now: number;
  busy: boolean;
  station: string;
  profile: KdsFlowProfile;
  onAction: (kind: KdsActionKind, itemId: string) => void;
}) {
  const key = columnOf(item.status, profile);
  const action = key ? columnDef(key, profile).action : null;

  return (
    <li className="rounded-xl bg-paper2 px-4 py-3">
      <div className="flex flex-col gap-0.5">
        <span className="text-xl font-bold leading-tight text-ink">
          {item.quantity}× {item.itemNameSnapshot}
        </span>
        {item.selectedModifiers && (
          <span className="text-base font-semibold leading-snug text-cyan-700">
            {item.selectedModifiers}
          </span>
        )}
        {item.notes && (
          <span className="text-base italic leading-snug text-amber-700">{item.notes}</span>
        )}
        {item.status === "RETURNED" && (
          <span className="mt-1 inline-flex w-fit items-center rounded-full bg-rose-500/10 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-rose-600">
            Devuelto — rehacer
          </span>
        )}
      </div>

      <div className="mt-3">
        <KdsRecorrido steps={buildRecorrido(item, order.createdAt, now, profile)} currentColor={ORO} />
      </div>

      {action ? (
        <button
          type="button"
          onClick={() => onAction(action, item.id)}
          disabled={busy}
          className={`mt-3 min-h-[56px] w-full rounded-xl px-4 text-lg font-extrabold transition active:scale-[0.99] disabled:opacity-50 disabled:active:scale-100 motion-reduce:transition-none motion-reduce:active:scale-100 ${KDS_ACTION_CLASS[action]}`}
        >
          {busy ? "…" : KDS_ACTION_LABEL[action]}
        </button>
      ) : (
        <p className="mt-3 text-sm italic text-muted">Esperando runner de {station}</p>
      )}
    </li>
  );
}
