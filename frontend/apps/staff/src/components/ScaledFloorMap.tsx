"use client";

import { useMemo, useRef, useState } from "react";
import type { FloorPlanFloor, FloorPlanTable } from "@mestobeach/api-client";

// Plano a escala canónico (el de plano-sala.html llevado a datos reales). Es EL mapa del sistema:
// el mismo render en admin/mesas, Host, PDA, Runner y Caja, para que todos coincidan. Cada mesa se
// dibuja a su HUELLA REAL, derivada de (shape, seats); no hay campo de tamaño.
const K = 0.6; // px por cm (1 unidad del modelo = 1 cm)
const M = 60; // px por metro
// Color por token (globals.css): en claro resuelven a los mismos valores de siempre y en Night
// (html[data-venue="night"]) se redefinen. Los atributos SVG aceptan var() en los navegadores objetivo.
const tok = (v: string, a?: number) => (a === undefined ? `rgb(var(--${v}))` : `rgb(var(--${v}) / ${a})`);
const oro = (a: number) => tok("oro", a);
const WARM = (a: number) => tok("sombra", a);

const CREMA = tok("paper-2"); // #F8F5F0
const CREMA_ALT = tok("paper-3"); // #F1EEEB
const ARENA = tok("arena");
const PIEDRA = tok("piedra");
const TINTA = tok("map-ink"); // #222222
const SEC = tok("map-sec"); // #666666
const PAPER = tok("paper"); // mesas libres, trazos de contraste
const OCC = tok("occ-ink"); // código de mesa ocupada

const CHAIR_L = 25;
const CHAIR_D = 10;
const RESERVE = 27; // holgura de 0,45 m dibujada como anillo de reserva

type Zone = "beach" | "terraza" | "salon";

interface Props {
  floor: FloorPlanFloor;
  tables: FloorPlanTable[];
  selectedId?: string | null;
  onPick?: (t: FloorPlanTable) => void;
  /** Mesas a resaltar con un punto (p. ej. platos en espera del PDA). */
  highlightIds?: Set<string>;
  /** Paradas numeradas del runner: id de mesa -> nº de parada. */
  stopLabels?: Map<string, number>;
  /** Fuerza el tratamiento de zona; si no, se infiere del nombre del piso. */
  zone?: Zone;
  /** Atajo/override: fuerza el tratamiento de playa (hamacas). Compat con Host/PDA. */
  beach?: boolean;
  /** Muestra la barra de controles (Sillas / Anillo de reserva / Leyenda). */
  controls?: boolean;
  /** Modo edición: si se pasa, las mesas se pueden ARRASTRAR; devuelve la nueva posición en cm. */
  onMove?: (tableId: string, posX: number, posY: number) => void;
}

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

interface ChairSpec { x: number; y: number; w: number; h: number; rot: number }

function roundDiamCm(seats: number) {
  return Math.round(Math.min(1.6, Math.max(0.7, 0.4 + 0.15 * seats)) * 100);
}
function squareCm(seats: number) {
  if (seats <= 4) {
    const s = Math.round((0.5 + 0.1 * seats) * 100);
    return { w: s, h: s };
  }
  return { w: Math.round(0.3 * seats * 100), h: 90 };
}
const fmtM = (cm: number) => (cm / 100).toFixed(2).replace(".", ",");

function roundChairs(diamPx: number, seats: number): ChairSpec[] {
  const R = diamPx / 2 + 6 + 5;
  const out: ChairSpec[] = [];
  for (let i = 0; i < seats; i++) {
    const ang = -Math.PI / 2 + (i * 2 * Math.PI) / seats;
    out.push({ x: Math.cos(ang) * R, y: Math.sin(ang) * R, w: CHAIR_L, h: CHAIR_D, rot: (ang * 180) / Math.PI + 90 });
  }
  return out;
}
function rectChairs(wPx: number, hPx: number, seats: number): ChairSpec[] {
  const out: ChairSpec[] = [];
  const offV = hPx / 2 + 6 + 5;
  const offH = wPx / 2 + 6 + 5;
  let top = 0, bottom = 0, left = 0, right = 0;
  if (wPx >= hPx * 1.4) {
    const per = Math.floor(seats / 2);
    top = per; bottom = per;
    const extra = seats - per * 2;
    if (extra >= 1) right = 1;
    if (extra >= 2) left = 1;
  } else {
    const order = ["top", "bottom", "left", "right"] as const;
    for (let i = 0; i < seats; i++) {
      const s = order[i % 4];
      if (s === "top") top++; else if (s === "bottom") bottom++;
      else if (s === "left") left++; else right++;
    }
  }
  const along = (n: number, len: number) => {
    const pts: number[] = [];
    for (let i = 0; i < n; i++) pts.push(-len / 2 + (len * (i + 1)) / (n + 1));
    return pts;
  };
  along(top, wPx).forEach((x) => out.push({ x, y: -offV, w: CHAIR_L, h: CHAIR_D, rot: 0 }));
  along(bottom, wPx).forEach((x) => out.push({ x, y: offV, w: CHAIR_L, h: CHAIR_D, rot: 0 }));
  along(left, hPx).forEach((y) => out.push({ x: -offH, y, w: CHAIR_D, h: CHAIR_L, rot: 0 }));
  along(right, hPx).forEach((y) => out.push({ x: offH, y, w: CHAIR_D, h: CHAIR_L, rot: 0 }));
  return out;
}

function Chairs({ specs }: { specs: ChairSpec[] }) {
  return (
    <>
      {specs.map((c, i) => (
        <rect key={i} x={c.x - c.w / 2} y={c.y - c.h / 2} width={c.w} height={c.h} rx={2.5}
          fill="none" stroke={oro(0.32)} strokeWidth={1}
          transform={c.rot ? `rotate(${c.rot} ${c.x} ${c.y})` : undefined} />
      ))}
    </>
  );
}

// ─── Barra de controles (estética plano-sala) ───────────────────────────────
function Toggle({ on, onClick, children }: { on: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={on}
      className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-medium tracking-[0.03em] transition ${
        on ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/50 bg-paper text-muted hover:border-hair"
      }`}
    >
      <span className={`h-2.5 w-2.5 rounded-full border-2 border-oro transition ${on ? "bg-oro" : "bg-transparent"}`} />
      {children}
    </button>
  );
}

// ─── Tarjeta de leyenda (glifos dibujados igual que el mapa) ─────────────────
function LegendGlyph({ kind }: { kind: string }) {
  return (
    <svg viewBox="0 0 22 22" className="h-[22px] w-[22px] flex-none">
      {kind === "round" && <circle cx={11} cy={11} r={8} fill={PAPER} stroke={oro(0.7)} strokeWidth={1.5} />}
      {kind === "square" && <rect x={3} y={3} width={16} height={16} rx={2.5} fill={PAPER} stroke={oro(0.7)} strokeWidth={1.5} />}
      {kind === "hammock" && <rect x={7} y={2} width={8} height={18} rx={4} fill={PAPER} stroke={oro(0.5)} strokeWidth={1} />}
      {kind === "free" && <circle cx={11} cy={11} r={8} fill={PAPER} stroke={oro(0.7)} strokeWidth={1.5} />}
      {kind === "occ" && <circle cx={11} cy={11} r={8} fill={oro(0.14)} stroke={oro(0.88)} strokeWidth={1.5} />}
      {kind === "chair" && <rect x={4} y={8} width={14} height={6} rx={2} fill="none" stroke={oro(0.4)} strokeWidth={1} />}
    </svg>
  );
}
function LegendCard() {
  const items: [string, string][] = [
    ["round", "Mesa redonda"], ["square", "Mesa cuadrada"], ["hammock", "Hamaca"],
    ["sep", ""], ["free", "Libre"], ["occ", "Ocupada"], ["sep", ""], ["chair", "Silla insinuada"],
  ];
  return (
    <aside className="absolute right-3 top-3 w-48 rounded-lg border border-hair/50 bg-paper p-3.5 shadow-warm">
      <h2 className="mb-2.5 font-display text-[15px] text-ink">Leyenda</h2>
      <ul className="flex flex-col gap-2">
        {items.map(([k, label], i) =>
          k === "sep" ? (
            <li key={i} className="h-px bg-oro/20" />
          ) : (
            <li key={i} className="flex items-center gap-2.5 text-xs text-ink/80">
              <LegendGlyph kind={k} />
              {label}
            </li>
          )
        )}
      </ul>
    </aside>
  );
}

interface Tip { x: number; y: number; code: string; dim: string; sub: string }

export default function ScaledFloorMap({
  floor, tables, selectedId, onPick, highlightIds, stopLabels, zone, beach: beachProp, controls = true, onMove,
}: Props) {
  const [chairsOn, setChairsOn] = useState(true);
  const [reserveOn, setReserveOn] = useState(false);
  const [legendOn, setLegendOn] = useState(false);
  const [tip, setTip] = useState<Tip | null>(null);
  const [dragId, setDragId] = useState<string | null>(null);
  const svgRef = useRef<SVGSVGElement>(null);

  const z: Zone = zone ?? (beachProp || /playa/i.test(floor.name) ? "beach" : /terraza/i.test(floor.name) ? "terraza" : "salon");
  const beach = z === "beach";

  // Puntero en pantalla -> coordenadas del piso (cm), respetando el escalado real del SVG.
  const toFloor = (e: React.PointerEvent) => {
    const svg = svgRef.current;
    const m = svg?.getScreenCTM();
    if (!svg || !m) return null;
    const pt = svg.createSVGPoint();
    pt.x = e.clientX; pt.y = e.clientY;
    const p = pt.matrixTransform(m.inverse());
    return { x: clamp(Math.round(p.x / K), 0, floor.width), y: clamp(Math.round(p.y / K), 0, floor.height) };
  };

  const Wp = floor.width * K;
  const Hp = floor.height * K;
  const placed = useMemo(
    () => tables.filter((t) => t.floorId === floor.id && t.posX != null && t.posY != null),
    [tables, floor.id]
  );

  const padL = 52, padT = 46, padR = 20, padB = 60;

  return (
    <div>
      {controls && (
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <Toggle on={chairsOn} onClick={() => setChairsOn((v) => !v)}>Sillas</Toggle>
          <Toggle on={reserveOn} onClick={() => setReserveOn((v) => !v)}>Anillo de reserva</Toggle>
          <button
            onClick={() => setLegendOn((v) => !v)}
            aria-pressed={legendOn}
            className={`ml-auto inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-medium tracking-[0.03em] transition ${
              legendOn ? "border-oro bg-oro/[0.10] text-ink" : "border-hair/50 bg-paper text-muted hover:border-hair"
            }`}
          >
            Leyenda <span className="text-[10px] leading-none">◆</span>
          </button>
        </div>
      )}

      <div className="relative overflow-auto rounded-xl border border-hair/30" style={{ background: CREMA }}>
        <svg
          ref={svgRef}
          viewBox={`${-padL} ${-padT} ${Wp + padL + padR} ${Hp + padT + padB}`}
          style={{ display: "block", width: "100%", height: "auto", touchAction: onMove ? "none" : undefined }}
          role="img"
          aria-label={`Plano a escala del sector ${floor.name}`}
          onPointerLeave={() => setTip(null)}
        >
          <defs>
            <filter id="sfm-soft" x="-40%" y="-40%" width="180%" height="180%">
              <feGaussianBlur in="SourceGraphic" stdDeviation="3.2" />
            </filter>
            <linearGradient id="sfm-mar" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor={oro(0.16)} />
              <stop offset="1" stopColor={tok("paper", 0)} />
            </linearGradient>
          </defs>

          {/* Cabecera del sector + cota total */}
          <text x={0} y={-padT + 16} fontFamily="var(--font-display), serif" fontSize={15} fill={TINTA}>{floor.name}</text>
          <text x={0} y={-padT + 32} fontFamily="var(--font-ui), sans-serif" fontSize={11} fill={SEC}>
            {fmtM(floor.width)} × {fmtM(floor.height)} m
          </text>

          {/* Suelo por zona */}
          <rect x={0} y={0} width={Wp} height={Hp} rx={3} fill={beach ? ARENA : z === "salon" ? CREMA_ALT : CREMA} />

          {/* Textura por zona */}
          {beach &&
            Array.from({ length: 260 }).map((_, i) => (
              <circle key={i} cx={(i * 97) % Wp} cy={(i * 53 + 13) % Hp} r={1} fill={oro(0.07)} />
            ))}
          {z === "terraza" &&
            Array.from({ length: Math.floor(Hp / M) }).map((_, i) => (
              <line key={i} x1={0} y1={(i + 1) * M} x2={Wp} y2={(i + 1) * M} stroke={oro(0.07)} strokeWidth={1} />
            ))}
          {z === "salon" && (
            <>
              {Array.from({ length: Math.floor(Wp / M) }).map((_, i) => (
                <line key={"v" + i} x1={(i + 1) * M} y1={0} x2={(i + 1) * M} y2={Hp} stroke={oro(0.06)} strokeWidth={1} />
              ))}
              {Array.from({ length: Math.floor(Hp / M) }).map((_, i) => (
                <line key={"h" + i} x1={0} y1={(i + 1) * M} x2={Wp} y2={(i + 1) * M} stroke={oro(0.06)} strokeWidth={1} />
              ))}
            </>
          )}

          {/* Orilla "Mar" (playa) */}
          {beach && (
            <>
              <rect x={0} y={0} width={Wp} height={46} fill="url(#sfm-mar)" />
              <text x={Wp - 14} y={22} textAnchor="end" fontFamily="var(--font-display), serif" fontSize={15} fill={oro(0.7)}>Mar</text>
            </>
          )}

          {/* Muros: salón sólido (piedra), terraza barandilla (dorado punteado) */}
          {!beach &&
            floor.walls.map((w, i) =>
              z === "salon" ? (
                <g key={i}>
                  <line x1={w.x1 * K} y1={w.y1 * K} x2={w.x2 * K} y2={w.y2 * K} stroke={PIEDRA} strokeWidth={9} strokeLinecap="round" />
                  <line x1={w.x1 * K} y1={w.y1 * K} x2={w.x2 * K} y2={w.y2 * K} stroke={oro(0.5)} strokeWidth={1} />
                </g>
              ) : (
                <line key={i} x1={w.x1 * K} y1={w.y1 * K} x2={w.x2 * K} y2={w.y2 * K}
                  stroke={oro(0.55)} strokeWidth={2} strokeDasharray="6 4" strokeLinecap="round" />
              )
            )}

          {/* Mesas / hamacas */}
          {placed.map((t) => {
            const px = t.posX! * K, py = t.posY! * K;
            const occ = t.hasActiveSession;
            const sel = t.id === selectedId;
            const hl = highlightIds?.has(t.id);
            const stopN = stopLabels?.get(t.id);
            const seats = t.seats ?? 2;
            const fill = occ ? oro(0.14) : PAPER;
            const ring = sel ? oro(1) : occ ? oro(0.88) : oro(0.7);
            const ringW = sel ? 2.5 : 1.5;
            const codeColor = occ ? OCC : TINTA;

            let body: JSX.Element;
            let reserve: JSX.Element | null = null;
            let chairs: ChairSpec[] = [];
            let dim: string;

            if (beach) {
              const HL = 190 * K, HW = 60 * K;
              dim = "1,90 × 0,60 m";
              if (reserveOn)
                reserve = <rect x={-HW / 2 - RESERVE} y={-HL / 2 - RESERVE} width={HW + 2 * RESERVE} height={HL + 2 * RESERVE} rx={8} fill="none" stroke={oro(0.28)} strokeWidth={1} strokeDasharray="3 4" />;
              body = (
                <>
                  <rect x={-HW / 2 + 1.5} y={-HL / 2 + 2} width={HW} height={HL} rx={6} fill={WARM(0.1)} filter="url(#sfm-soft)" />
                  <rect x={-HW / 2} y={-HL / 2} width={HW} height={HL} rx={6} fill={occ ? oro(0.14) : PAPER} stroke={sel ? oro(1) : occ ? oro(0.85) : oro(0.5)} strokeWidth={sel ? 2 : 1} />
                  <line x1={-HW / 2 + 4} y1={-HL / 2 + HL * 0.2} x2={HW / 2 - 4} y2={-HL / 2 + HL * 0.2} stroke={oro(0.3)} strokeWidth={1} />
                  <text y={HL / 2 - 6} textAnchor="middle" fontFamily="var(--font-ui), sans-serif" fontSize={8} fontWeight={500} fill={SEC}>{t.tableCode}</text>
                </>
              );
            } else if (t.shape === "ROUND") {
              const dCm = roundDiamCm(seats);
              const r = (dCm * K) / 2;
              dim = `ø ${fmtM(dCm)} m`;
              chairs = chairsOn ? roundChairs(dCm * K, seats) : [];
              if (reserveOn) reserve = <circle r={r + RESERVE} fill="none" stroke={oro(0.28)} strokeWidth={1} strokeDasharray="3 4" />;
              body = (
                <>
                  <circle r={r} cx={2} cy={3} fill={WARM(0.12)} filter="url(#sfm-soft)" />
                  <circle r={r} fill={fill} stroke={ring} strokeWidth={ringW} />
                </>
              );
            } else {
              const { w, h } = squareCm(seats);
              const wp = w * K, hp = h * K;
              dim = `${fmtM(w)} × ${fmtM(h)} m`;
              chairs = chairsOn ? rectChairs(wp, hp, seats) : [];
              if (reserveOn) reserve = <rect x={-wp / 2 - RESERVE} y={-hp / 2 - RESERVE} width={wp + 2 * RESERVE} height={hp + 2 * RESERVE} rx={6} fill="none" stroke={oro(0.28)} strokeWidth={1} strokeDasharray="3 4" />;
              body = (
                <>
                  <rect x={-wp / 2 + 2} y={-hp / 2 + 3} width={wp} height={hp} rx={2.5} fill={WARM(0.12)} filter="url(#sfm-soft)" />
                  <rect x={-wp / 2} y={-hp / 2} width={wp} height={hp} rx={2.5} fill={fill} stroke={ring} strokeWidth={ringW} />
                </>
              );
            }

            const moveTip = (e: React.PointerEvent) =>
              setTip({ x: e.clientX, y: e.clientY, code: t.tableCode, dim, sub: beach ? "hamaca" : `${seats} plazas · ${t.shape === "ROUND" ? "redonda" : "cuadrada"}` });

            return (
              <g
                key={t.id}
                transform={`translate(${px} ${py})`}
                onClick={onPick ? () => onPick(t) : undefined}
                onPointerDown={
                  onMove
                    ? (e) => {
                        e.stopPropagation();
                        (e.currentTarget as Element).setPointerCapture(e.pointerId);
                        setDragId(t.id);
                        setTip(null);
                      }
                    : undefined
                }
                onPointerEnter={dragId ? undefined : moveTip}
                onPointerMove={(e) => {
                  if (onMove && dragId === t.id) {
                    const p = toFloor(e);
                    if (p) onMove(t.id, p.x, p.y);
                  } else if (!dragId) {
                    moveTip(e);
                  }
                }}
                onPointerUp={
                  onMove
                    ? (e) => {
                        (e.currentTarget as Element).releasePointerCapture(e.pointerId);
                        setDragId(null);
                      }
                    : undefined
                }
                style={{ cursor: onMove ? "move" : onPick ? "pointer" : "default", opacity: t.active ? 1 : 0.4 }}
              >
                {reserve}
                <Chairs specs={chairs} />
                {body}
                {!beach && (
                  <>
                    <text y={1} textAnchor="middle" fontFamily="var(--font-display), serif" fontSize={13} fill={codeColor}>{t.tableCode}</text>
                    <text y={14} textAnchor="middle" fontFamily="var(--font-ui), sans-serif" fontSize={9} fill={SEC} letterSpacing="0.04em">· {seats} pax ·</text>
                  </>
                )}
                {stopN != null ? (
                  <g transform={`translate(${beach ? 60 * K * 0.5 + 4 : 0} ${beach ? -190 * K * 0.5 : -2})`}>
                    <circle r={11} fill={oro(1)} stroke={PAPER} strokeWidth={1.5} />
                    <text textAnchor="middle" y={4} fontFamily="var(--font-ui), sans-serif" fontSize={12} fontWeight={700} fill={PAPER}>{stopN}</text>
                  </g>
                ) : (
                  hl && <circle cx={beach ? 60 * K * 0.5 : 0} cy={beach ? -190 * K * 0.5 : 0} r={4} fill={PAPER} stroke={oro(1)} strokeWidth={2} />
                )}
              </g>
            );
          })}

          {/* Regla gráfica de 5 m */}
          <g transform={`translate(0 ${Hp + 20})`}>
            {Array.from({ length: 5 }).map((_, i) => (
              <rect key={i} x={i * M} y={0} width={M} height={8} fill={i % 2 ? PAPER : oro(0.85)} stroke={oro(0.7)} strokeWidth={1} />
            ))}
            {Array.from({ length: 6 }).map((_, i) => (
              <text key={i} x={i * M} y={22} textAnchor="middle" fontFamily="var(--font-ui), sans-serif" fontSize={10} fill={SEC}>
                {i === 5 ? "5 m" : String(i)}
              </text>
            ))}
          </g>
        </svg>

        {legendOn && <LegendCard />}
      </div>

      {/* Cota flotante al pasar el cursor */}
      {tip && (
        <div
          className="pointer-events-none fixed z-50 rounded-lg border border-hair/60 bg-paper px-2.5 py-1.5 text-xs text-ink shadow-warm"
          style={{ left: tip.x + 14, top: tip.y + 14 }}
        >
          <span className="font-display text-sm">{tip.code}</span> &nbsp;
          <span className="font-semibold tabular-nums text-oro">{tip.dim}</span>
          <br />
          <span className="text-muted">{tip.sub}</span>
        </div>
      )}
    </div>
  );
}
