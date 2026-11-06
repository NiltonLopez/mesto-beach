"use client";

import type { KdsOrder } from "@/lib/stores";
import type { OrderItemDto, KdsStationDto, KdsFlowProfile } from "@mestobeach/api-client";
import { ReservadoPill } from "@/components/ReservadoPill";
import {
  columnDef,
  columnOf,
  clockLabel,
  stateLabel,
  stationColor,
  stationName,
  tint,
  urgencyClass,
  KDS_ACTION_CLASS,
  KDS_ACTION_LABEL,
  type KdsActionKind,
  type KdsColumnKey,
  type KdsDensity,
} from "@/lib/kds";

interface Props {
  order: KdsOrder;
  /** Estación desde la que se mira; null = "todas". Fija qué es tuyo y qué es contexto. */
  lensStationId: string | null;
  /** Columna en la que vive esta tarjeta: los platos propios en ese paso son el foco. */
  column: KdsColumnKey;
  stations: KdsStationDto[];
  /** Sector (piso) de la mesa, resuelto por el tablero. */
  sector: string | null;
  /** Reloj de la página (ticker de 1s) para que los cronómetros latan. */
  now: number;
  /** Cuánto aprieta la tarjeta, según cuántas comandas haya en su columna. */
  density: KdsDensity;
  /** Perfil de flujo de la estación elegida: define columnas y acciones (ADR-037). */
  profile?: KdsFlowProfile;
  /** Pedido recién llegado: borde dorado durante unos segundos (sin parpadeo). */
  fresh?: boolean;
  /** Plato con una acción en vuelo: su botón se bloquea. */
  busyItemId: string | null;
  onAction: (kind: KdsActionKind, itemId: string) => void;
  /** Abrir la comanda en modo foco. */
  onOpen: (orderId: string) => void;
}

/** ¿Es un plato de la estación desde la que miramos? En modo "todas", todo es propio. */
function isOwn(item: OrderItemDto, lens: string | null): boolean {
  if (lens === null) return true;
  return (item.stationId ?? null) === lens;
}

export default function KdsComandaCard({
  order,
  lensStationId,
  column,
  stations,
  sector,
  now,
  density,
  profile = "FULL_PASS",
  fresh = false,
  busyItemId,
  onAction,
  onOpen,
}: Props) {
  const col = columnDef(column, profile);
  const reservado = (order.priority ?? 0) > 0;
  // Un pedido nuevo se anuncia con un anillo dorado fijo que se desvanece; nada parpadea.
  const aura = fresh ? "ring-2 ring-oro" : "ring-1 ring-hair/40";
  // Con la columna llena la tarjeta se aprieta: menos aire, tipografía más contenida y, en el
  // nivel más denso, sin los adornos (modificadores, notas, contexto de otras estaciones) que
  // siguen estando a un toque en el modo foco.
  const compact = density !== "comfy";
  const row = density === "row";
  const dense = density === "dense" || row;
  // Anulado no existe para la cocina; lo que ya salió (en reparto/entregado) sí se ve,
  // en gris, porque le dice al cocinero que la mesa ya está comiendo.
  const vivos = order.items.filter((i) => i.status !== "CANCELLED");

  // Foco: los platos PROPIOS que están en el paso de esta columna.
  const foco = vivos.filter((i) => isOwn(i, lensStationId) && columnOf(i.status, profile) === column);
  if (foco.length === 0) return null; // esta comanda no aporta nada aquí

  // Contexto (ADR-029): el resto de la comanda —sobre todo lo de OTRAS estaciones— para
  // que el plato salga acompasado con la mesa. Informativo, nunca accionable.
  const contexto = vivos.filter((i) => !foco.includes(i));

  const subtitulo = [sector, order.customerName].filter(Boolean).join(" · ");

  // ── Servicio desbordado: una fila por comanda ──────────────────────────────
  // Todo en una línea —mesa, reloj, platos resumidos y la acción— para que la cola entera
  // quepa sin scroll. Lo que se recorta (modificadores, notas, otras estaciones) vive a un
  // toque en el modo foco; la observación se marca con un punto dorado para no perderla.
  if (row) {
    const total = foco.reduce((n, i) => n + i.quantity, 0);
    const resumen =
      foco.length === 1 && foco[0]
        ? `${foco[0].quantity}× ${foco[0].itemNameSnapshot}`
        : `${total} platos · ${foco.length} líneas`;
    const busy = foco.some((i) => busyItemId === i.id);

    return (
      <article
        onClick={() => onOpen(order.id)}
        className={`flex cursor-pointer items-center gap-2.5 rounded-xl border-l-4 bg-paper px-2.5 py-2 text-left shadow-warm ${aura} transition hover:shadow-warm-lg motion-reduce:transition-none ${col.edge}`}
      >
        <span className="shrink-0 font-display text-lg font-normal leading-none text-ink">
          {order.tableCode ?? "—"}
        </span>
        <span
          className={`shrink-0 text-sm font-bold tabular-nums ${urgencyClass(order.createdAt, now)}`}
        >
          {clockLabel(order.createdAt, now)}
        </span>
        {reservado && <ReservadoPill />}
        {order.observacion && (
          <span
            className="shrink-0 rounded bg-oro/[0.16] px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide text-brand-700"
            title={order.observacion}
          >
            Obs
          </span>
        )}
        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink" title={resumen}>
          {resumen}
        </span>
        {contexto.length > 0 && (
          <span
            className="shrink-0 rounded-full"
            style={{
              width: 8,
              height: 8,
              backgroundColor: stationColor(contexto[0]?.stationId ?? null, stations),
            }}
            title={`También: ${[...new Set(contexto.map((i) => stationName(i.stationId, stations)))].join(", ")}`}
          />
        )}
        {col.action && (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              // En fila la acción vale por toda la comanda: es lo que hace el encargado
              // cuando lanza una mesa entera de golpe.
              if (col.action) foco.forEach((i) => onAction(col.action as KdsActionKind, i.id));
            }}
            disabled={busy}
            className={`shrink-0 rounded-lg px-3 py-1.5 text-xs font-extrabold transition disabled:opacity-50 motion-reduce:transition-none ${KDS_ACTION_CLASS[col.action]}`}
          >
            {busy ? "…" : KDS_ACTION_LABEL[col.action]}
          </button>
        )}
      </article>
    );
  }

  return (
    <article
      onClick={() => onOpen(order.id)}
      className={`flex cursor-pointer flex-col rounded-2xl border-l-4 bg-paper text-left shadow-warm ${aura} transition hover:shadow-warm-lg motion-reduce:transition-none ${col.edge} ${
        dense ? "gap-1.5 p-2.5" : compact ? "gap-2 p-3" : "gap-3 p-4"
      }`}
    >
      {/* Cabecera: mesa + sector/comensal + cronómetro. La mesa es el asa accesible del
          modo foco (con el ratón vale cualquier punto de la tarjeta). */}
      <header className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpen(order.id);
            }}
            aria-label={`Abrir la comanda de ${order.tableCode ?? "la mesa"}`}
            className={`rounded text-left font-display font-normal leading-none text-ink focus:outline-none focus-visible:ring-2 focus-visible:ring-oro ${
              dense ? "text-xl" : compact ? "text-2xl" : "text-3xl"
            }`}
          >
            {order.tableCode ?? "Sin mesa"}
          </button>
          {/* En denso el sector sobra: la mesa ya lo dice y cada línea cuenta. */}
          {subtitulo && !dense && <span className="truncate text-xs text-muted">{subtitulo}</span>}
          {reservado && <ReservadoPill className="mt-1 w-fit" />}
        </div>
        <span
          className={`shrink-0 font-bold tabular-nums ${
            dense ? "text-lg" : "text-2xl"
          } ${urgencyClass(order.createdAt, now)}`}
        >
          {clockLabel(order.createdAt, now)}
        </span>
      </header>

      {/* Observación de la comanda entera */}
      {/* La observación NUNCA se oculta —puede ser una alergia—; en denso se recorta a una
          línea y el texto completo sigue en el modo foco (y en el title). */}
      {order.observacion && (
        <div
          className={`rounded-lg bg-oro/[0.12] text-brand-700 ${
            dense ? "px-2.5 py-1 text-xs" : "px-3 py-2 text-sm"
          }`}
          title={order.observacion}
        >
          <span
            className={`font-bold uppercase tracking-wide text-brand-700/70 ${
              dense ? "mr-1 text-[9px]" : "mr-1.5 text-[10px]"
            }`}
          >
            Obs
          </span>
          <span className={`font-semibold ${dense ? "line-clamp-1" : ""}`}>
            {order.observacion}
          </span>
        </div>
      )}

      {/* Los platos de esta estación en el paso de esta columna */}
      <ul className="flex flex-col gap-2">
        {foco.map((item) => {
          const busy = busyItemId === item.id;
          return (
            <li
              key={item.id}
              className={`rounded-xl bg-paper2 ${dense ? "px-2.5 py-1.5" : "px-3 py-2.5"}`}
            >
              <div className="flex min-w-0 flex-col gap-0.5">
                <span
                  className={`font-bold leading-tight text-ink ${dense ? "text-base" : "text-lg"}`}
                >
                  {item.quantity}× {item.itemNameSnapshot}
                </span>
                {/* Modificadores y notas se recogen en denso: el detalle vive en el modo foco.
                    Se deja una señal para que el cocinero sepa que ese plato lleva letra pequeña. */}
                {item.selectedModifiers &&
                  (dense ? (
                    <span className="text-xs font-semibold text-cyan-700">· con ajustes</span>
                  ) : (
                    <span className="text-sm font-semibold leading-snug text-cyan-700">
                      {item.selectedModifiers}
                    </span>
                  ))}
                {item.notes && !dense && (
                  <span className="text-sm italic leading-snug text-amber-700">{item.notes}</span>
                )}
                {item.notes && dense && (
                  <span className="text-xs italic text-amber-700">· con nota</span>
                )}
                {item.status === "RETURNED" && (
                  <span className="mt-0.5 inline-flex w-fit items-center rounded-full bg-rose-500/10 px-2 py-0.5 text-xs font-medium tracking-[0.03em] text-rose-600">
                    Devuelto — rehacer
                  </span>
                )}
              </div>

              {col.action ? (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (col.action) onAction(col.action, item.id);
                  }}
                  disabled={busy}
                  className={`w-full rounded-xl px-4 font-extrabold transition active:scale-[0.98] disabled:opacity-50 disabled:active:scale-100 motion-reduce:transition-none motion-reduce:active:scale-100 ${
                    dense
                      ? "mt-1.5 min-h-[40px] text-sm"
                      : compact
                        ? "mt-2 min-h-[46px] text-base"
                        : "mt-2.5 min-h-[52px] text-base"
                  } ${KDS_ACTION_CLASS[col.action]}`}
                >
                  {busy ? "…" : KDS_ACTION_LABEL[col.action]}
                </button>
              ) : (
                // En el pase no hay nada que hacer: el plato es del runner.
                <p className="mt-2 text-xs italic text-muted">
                  Esperando runner de {stationName(item.stationId, stations)}
                </p>
              )}
            </li>
          );
        })}
      </ul>

      {/* Lo demás de la misma comanda, con el color de su estación (ADR-029). En denso se
          resume a una línea: "También · Parrilla, Cócteles" — el detalle, en el modo foco. */}
      {contexto.length > 0 && dense && (
        <div className="flex flex-wrap items-center gap-1.5 border-t border-hair/40 pt-1.5">
          <span className="text-[9px] font-bold uppercase tracking-wide text-muted">También</span>
          {[...new Set(contexto.map((i) => i.stationId ?? "sin"))].map((sid) => {
            const color = stationColor(sid === "sin" ? null : sid, stations);
            return (
              <span
                key={sid}
                className="rounded px-1.5 py-0.5 text-[10px] font-bold"
                style={{ color, backgroundColor: tint(color) }}
              >
                {stationName(sid === "sin" ? null : sid, stations)}
              </span>
            );
          })}
        </div>
      )}
      {contexto.length > 0 && !dense && (
        <ul className="flex flex-col gap-1.5 border-t border-hair/40 pt-2.5">
          {contexto.map((item) => {
            const color = stationColor(item.stationId, stations);
            return (
              <li
                key={item.id}
                className="rounded-lg border-l-[3px] py-1.5 pl-2.5 pr-2"
                style={{ borderLeftColor: color, backgroundColor: tint(color) }}
              >
                <span
                  className="block text-[10px] font-bold uppercase tracking-wide"
                  style={{ color }}
                >
                  También · {stationName(item.stationId, stations)}
                </span>
                <span className="block truncate text-sm text-ink">
                  {item.quantity}× {item.itemNameSnapshot}
                  <span className="ml-1.5 italic text-muted">{stateLabel(item.status)}</span>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </article>
  );
}
