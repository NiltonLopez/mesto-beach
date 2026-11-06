"use client";

import { useEffect, useRef, useState } from "react";

/**
 * Pantalla de confirmación de la comanda, con ventana para deshacer.
 *
 * El pedido NO se ha enviado cuando esto aparece: durante VENTANA_DESHACER_MS el
 * comensal puede cancelarlo o lanzarlo antes de tiempo tocando la pantalla. Solo
 * al agotarse la ventana —o al tocar— se llama a `onCommit`, que es quien manda
 * la comanda a cocina. En una hamaca a treinta metros de la barra, rectificar de
 * otro modo significa levantarse.
 *
 * Los colores salen de las variables --m-* del menú, así que respeta la
 * apariencia que el local tenga configurada en Admin → Apariencia.
 */

const VENTANA_DESHACER_MS = 6000;
const ESPERA_TRAS_ENVIO_MS = 1500;
const SALIDA_MS = 300;

type Props = {
  status: "idle" | "pending" | "success";
  items?: { name: string; quantity: number }[];
  onCommit: () => void;
  onCancel: () => void;
  onFinished: () => void;
  /**
   * Envía sin ventana para deshacer. Se usa al reintentar tras el PIN: el
   * comensal ya esperó su ventana antes de que el backend pidiera el PIN, y
   * hacerle esperar otra vez sería castigarle por una condición de la mesa.
   */
  immediate?: boolean;
};

export default function OrderSentAnimation({
  status,
  items = [],
  onCommit,
  onCancel,
  onFinished,
  immediate = false,
}: Props) {
  const total = items.reduce((suma, i) => suma + i.quantity, 0);

  // Los platos van rotando bajo el emblema mientras dura la espera.
  const [indice, setIndice] = useState(0);
  useEffect(() => {
    if (items.length <= 1) return;
    const id = setInterval(() => setIndice((i) => (i + 1) % items.length), 1400);
    return () => clearInterval(id);
  }, [items.length]);
  const plato = items[indice] ?? items[0] ?? null;

  const [lanzado, setLanzado] = useState(false);
  const lanzadoRef = useRef(false);
  const cancelandoRef = useRef(false);
  const [saliendo, setSaliendo] = useState(false);
  const [relleno, setRelleno] = useState<"0%" | "100%">("0%");

  function lanzar() {
    if (lanzadoRef.current || cancelandoRef.current) return;
    lanzadoRef.current = true;
    setLanzado(true);
    onCommit();
  }

  // Arranca la barra y programa el envío automático al agotarse la ventana.
  // En modo inmediato no hay ventana: se lanza en cuanto se monta.
  useEffect(() => {
    if (immediate) {
      lanzar();
      return undefined;
    }
    const raf = requestAnimationFrame(() => setRelleno("100%"));
    const temporizador = setTimeout(lanzar, VENTANA_DESHACER_MS);
    return () => {
      cancelAnimationFrame(raf);
      clearTimeout(temporizador);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Enviado: se deja leer un momento y se empieza a desvanecer.
  useEffect(() => {
    if (lanzado && status === "success" && !saliendo) {
      const id = setTimeout(() => setSaliendo(true), ESPERA_TRAS_ENVIO_MS);
      return () => clearTimeout(id);
    }
    return undefined;
  }, [lanzado, status, saliendo]);

  useEffect(() => {
    if (!saliendo) return undefined;
    const id = setTimeout(onFinished, SALIDA_MS);
    return () => clearTimeout(id);
  }, [saliendo, onFinished]);

  function cancelar() {
    if (lanzadoRef.current || cancelandoRef.current) return;
    cancelandoRef.current = true;
    onCancel();
  }

  // Una ola: onda de longitud 20 repetida a lo ancho, para que al desplazarla
  // 20 px el dibujo encaje consigo mismo y el movimiento no dé un salto.
  const ola = (y: number, amplitud: number) =>
    `M0 ${y} q 5 ${-amplitud} 10 0 ` + "t 10 0 ".repeat(22);

  // Los rayos del sol: a ángulo, l largo, d duración y r retardo. Cada uno late a
  // su ritmo para que el conjunto ondule desordenado en vez de pulsar a la vez.
  // Los de abajo son cortos: ahí ya empieza el mar y estorbarían.
  const rayos = [
    { a: -90, l: 14, d: 2.6, r: 0 },
    { a: -62, l: 15, d: 3.2, r: 0.5 },
    { a: -34, l: 16, d: 2.4, r: 0.9 },
    { a: -6, l: 16, d: 3.5, r: 0.2 },
    { a: 22, l: 15, d: 2.8, r: 1.1 },
    { a: 50, l: 11, d: 3.1, r: 0.7 },
    { a: 78, l: 8, d: 2.5, r: 1.4 },
    { a: 102, l: 8, d: 3.3, r: 0.35 },
    { a: 130, l: 11, d: 2.7, r: 1.0 },
    { a: 158, l: 15, d: 3.6, r: 0.6 },
    { a: 186, l: 16, d: 2.9, r: 0.15 },
    { a: 214, l: 15, d: 3.4, r: 1.25 },
    { a: 242, l: 14, d: 2.6, r: 0.8 },
    { a: 270, l: 14, d: 3.0, r: 0.45 },
  ];

  // Destellos, el brillo del sol en el agua: ángulo, distancia, retardo y tamaño.
  const destellos = [
    { a: -90, d: 150, r: 0.35, s: 8 },
    { a: -40, d: 168, r: 0.45, s: 6 },
    { a: 20, d: 158, r: 0.4, s: 9 },
    { a: 70, d: 150, r: 0.5, s: 5 },
    { a: 130, d: 162, r: 0.42, s: 7 },
    { a: 175, d: 152, r: 0.48, s: 6 },
    { a: -140, d: 158, r: 0.38, s: 8 },
    { a: 110, d: 128, r: 0.55, s: 5 },
  ];

  return (
    <div
      role="status"
      aria-live="polite"
      onClick={lanzar}
      className="fixed inset-0 z-[1000] cursor-pointer overflow-y-auto"
      style={{
        background:
          "radial-gradient(circle at 50% 38%, var(--m-surface) 0%, var(--m-bg) 70%)",
        animation: saliendo
          ? `m-os-salida ${SALIDA_MS}ms ease-in forwards`
          : "m-os-entrada 280ms ease-out both",
      }}
    >
      <style>{KEYFRAMES}</style>

      {/* El contenido se centra cuando cabe y se puede desplazar cuando no. Centrar
          directamente en el contenedor que desborda recortaría por arriba sin poder
          subir; por eso el centrado vive aquí, con `min-h-full`. */}
      <div className="flex min-h-full flex-col items-center justify-center gap-5 px-6 py-6 text-center">

      {/* El emblema cede tamaño en pantallas cortas, para que nunca empuje a los
          botones fuera de la vista. */}
      <div className="relative h-[min(250px,32vh)] w-[min(250px,32vh)] shrink-0">
        {/* Halo cálido */}
        <div
          className="absolute inset-0 rounded-full"
          style={{
            background:
              "radial-gradient(circle, color-mix(in srgb, var(--m-accent) 22%, transparent) 0%, transparent 65%)",
            animation: "m-os-halo 2.2s ease-in-out infinite",
          }}
        />

        {destellos.map((c, i) => {
          const rad = (c.a * Math.PI) / 180;
          return (
            <span
              key={i}
              className="absolute left-1/2 top-1/2 rounded-full"
              style={{
                width: c.s,
                height: c.s,
                marginLeft: -c.s / 2,
                marginTop: -c.s / 2,
                background: "var(--m-accent)",
                ["--tx" as string]: `${Math.cos(rad) * c.d}px`,
                ["--ty" as string]: `${Math.sin(rad) * c.d}px`,
                opacity: 0,
                animation: `m-os-destello 1.1s ease-out ${c.r}s forwards`,
              }}
            />
          );
        })}

        {/* El plato enmarca el horizonte: sol, mar en movimiento y, en la
            orilla, la palmera quieta y la comanda escribiéndose sola. */}
        <svg viewBox="0 0 200 200" className="relative h-full w-full">
          <defs>
            <clipPath id="m-os-clip">
              <circle cx="100" cy="100" r="72" />
            </clipPath>
          </defs>

          {/* Borde del plato */}
          <circle
            cx="100" cy="100" r="90"
            fill="none" stroke="var(--m-accent)" strokeWidth="2.6" strokeOpacity="0.5"
            style={{ transformOrigin: "100px 100px", animation: "m-os-aro 700ms cubic-bezier(.2,.8,.2,1) both" }}
          />
          <circle
            cx="100" cy="100" r="74"
            fill="none" stroke="var(--m-accent)" strokeWidth="1.2" strokeOpacity="0.25"
            style={{ transformOrigin: "100px 100px", animation: "m-os-aro 700ms cubic-bezier(.2,.8,.2,1) 90ms both" }}
          />

          <g clipPath="url(#m-os-clip)">
            {/* El sol, posándose sobre el horizonte */}
            <g style={{ transformOrigin: "100px 64px", animation: "m-os-sol 900ms cubic-bezier(.2,.8,.2,1) 200ms both" }}>
              <circle cx="100" cy="64" r="16" fill="var(--m-accent)" fillOpacity="0.3" />
              <circle cx="100" cy="64" r="16" fill="none" stroke="var(--m-accent)" strokeWidth="1.7" strokeOpacity="0.6" />
              {rayos.map((y) => (
                <g key={y.a} transform={`rotate(${y.a} 100 64)`}>
                  <path
                    d={`M121 64 q ${y.l / 4} -2.2 ${y.l / 2} 0 t ${y.l / 2} 0`}
                    fill="none" stroke="var(--m-accent)" strokeWidth="2.6" strokeLinecap="round"
                    style={{
                      transformOrigin: "121px 64px",
                      animation: `m-os-rayo ${y.d}s ease-in-out ${y.r}s infinite`,
                    }}
                  />
                </g>
              ))}
            </g>

            {/* El mar: tres líneas que van y vienen a distinto ritmo */}
            <path d={ola(96, 5)} fill="none" stroke="var(--m-accent)" strokeWidth="2.8" strokeOpacity="0.55" strokeLinecap="round"
                  style={{ animation: "m-os-marea 3.2s linear infinite" }} />
            <path d={ola(107, 4)} fill="none" stroke="var(--m-accent)" strokeWidth="2.3" strokeOpacity="0.4" strokeLinecap="round"
                  style={{ animation: "m-os-marea-inv 4.6s linear infinite" }} />
            <path d={ola(117, 3.5)} fill="none" stroke="var(--m-accent)" strokeWidth="1.9" strokeOpacity="0.28" strokeLinecap="round"
                  style={{ animation: "m-os-marea 5.8s linear infinite" }} />

            {/* La orilla y la arena */}
            <path d="M18 132 q 26 -8 54 -3 q 30 5 54 -1 q 28 -6 56 3 L190 195 L18 195 Z"
                  fill="var(--m-accent)" fillOpacity="0.1" />
            <path d="M18 132 q 26 -8 54 -3 q 30 5 54 -1 q 28 -6 56 3"
                  fill="none" stroke="var(--m-accent)" strokeWidth="1.9" strokeOpacity="0.45" strokeLinecap="round" />

            {/* La palmera, quieta. Se escala desde su base para que crezca como un
                árbol —no flotando— y se corre a la derecha lo justo para que la
                fronda de la izquierda no la recorte el borde del plato. */}
            <g transform="translate(11 0) translate(73 166) scale(1.22) translate(-73 -166)">
              <path d="M70 166 C 67 152, 62 138, 55 128 L60 125 C 68 136, 74 151, 77 166 Z"
                    fill="var(--m-accent)" fillOpacity="0.8" />
              <g fill="var(--m-accent)" fillOpacity="0.72">
                <path d="M58 126 C 47 122, 38 125, 33 131 C 41 128, 50 128, 58 130 Z" />
                <path d="M58 126 C 49 116, 43 108, 41 100 C 46 109, 53 118, 60 124 Z" />
                <path d="M58 126 C 57 113, 59 104, 64 96 C 64 107, 62 117, 60 125 Z" />
                <path d="M58 126 C 67 114, 77 107, 86 105 C 78 112, 68 119, 60 126 Z" />
                <path d="M58 126 C 69 122, 79 124, 86 130 C 78 127, 68 127, 58 130 Z" />
              </g>
              <circle cx="60" cy="129" r="2" fill="var(--m-accent)" fillOpacity="0.85" />
              <circle cx="55" cy="130" r="1.6" fill="var(--m-accent)" fillOpacity="0.7" />
            </g>

            {/* La comanda, con el lápiz recorriendo los renglones */}
            <g transform="rotate(6 112 146)">
              <rect x="98" y="130" width="28" height="32" rx="3"
                    fill="var(--m-surface)" stroke="var(--m-accent)" strokeWidth="1.9" strokeOpacity="0.8" />
              {[
                { d: "M103 139 h18", delay: 900 },
                { d: "M103 147 h18", delay: 1500 },
                { d: "M103 155 h11", delay: 2100 },
              ].map((linea) => (
                <path
                  key={linea.d}
                  d={linea.d}
                  stroke="var(--m-accent)" strokeWidth="1.6" strokeOpacity="0.6" strokeLinecap="round"
                  pathLength={1}
                  style={{
                    strokeDasharray: 1,
                    strokeDashoffset: 1,
                    animation: `m-os-escribe 480ms ease-out ${linea.delay}ms forwards`,
                  }}
                />
              ))}

              {/* El lápiz: baja al papel, recorre cada renglón, salta al siguiente
                  y se retira. Va al mismo compás con el que se dibujan las líneas. */}
              <g style={{ animation: "m-os-lapiz 6s ease-in-out infinite" }}>
                <g transform="rotate(36 103 139)">
                  <path d="M100.6 124 h5 v13 h-5 Z" fill="var(--m-accent)" fillOpacity="0.9" />
                  <path d="M100.6 121 h5 v3 h-5 Z" fill="var(--m-accent)" fillOpacity="0.45" />
                  <path d="M100.6 137 h5 l-2.5 4.5 Z" fill="var(--m-accent)" />
                </g>
              </g>
            </g>
          </g>

          {/* Sello de visto: solo cuando el pedido ya ha salido de verdad.
              Durante la ventana no hay nada confirmado que sellar. */}
          {lanzado && (
            <g style={{ transformOrigin: "158px 158px", animation: "m-os-surge 520ms cubic-bezier(.2,1.6,.4,1) both" }}>
              <circle cx="158" cy="158" r="21" fill="#2f7d51" />
              <path
                d="M148.5 158 l6.5 6.5 l12.5 -13.5"
                fill="none" stroke="#ffffff" strokeWidth="3.6" strokeLinecap="round" strokeLinejoin="round"
                pathLength={1}
                style={{ strokeDasharray: 1, strokeDashoffset: 1, animation: "m-os-visto 360ms ease-out 180ms forwards" }}
              />
            </g>
          )}
        </svg>
      </div>

      <div className="flex max-w-[340px] flex-col gap-2">
        <h2
          className="m-head text-2xl"
          style={{ color: "var(--m-text)", animation: "m-os-sube 500ms ease-out 600ms both" }}
        >
          {lanzado ? "Tu pedido viaja hacia ti" : "Tu pedido está a punto de salir"}
        </h2>
        <p
          className="text-sm"
          style={{ color: "var(--m-muted)", animation: "m-os-sube 500ms ease-out 760ms both" }}
        >
          {lanzado
            ? "Ya está en cocina. Puedes seguir su marcha en Mis pedidos."
            : "Tómate un momento. Si algo no encaja, aún estás a tiempo."}
        </p>
      </div>

      {total > 0 && (
        <div
          className="flex min-h-[58px] flex-col items-center gap-2"
          style={{ animation: "m-os-sube 500ms ease-out 900ms both" }}
        >
          <div className="text-base font-semibold" style={{ color: "var(--m-text)" }}>
            {total === 1 ? "1 plato" : `${total} platos`}
          </div>
          {plato && (
            <div
              key={indice}
              className="inline-flex max-w-[300px] items-center gap-1.5 rounded-full px-3.5 py-1 text-sm font-semibold"
              style={{
                background: "var(--m-surface)",
                border: "1px solid var(--m-border)",
                color: "var(--m-accent)",
                animation: "m-os-sube 350ms ease-out both",
              }}
            >
              <span className="tabular-nums">{plato.quantity}×</span>
              <span className="truncate">{plato.name}</span>
            </div>
          )}
        </div>
      )}

      {/* Los dos botones van en el flujo normal, uno debajo del otro. El de cancelar
          estaba antes anclado al fondo de la pantalla (`absolute bottom-8`), y en
          cuanto el contenido crecía o el móvil era corto se montaba encima del de
          enviar. Apilados no pueden solaparse en ninguna pantalla. */}
      {!lanzado && (
        <div className="flex w-full max-w-[260px] flex-col items-stretch gap-3">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              lanzar();
            }}
            className="relative w-full overflow-hidden rounded-full px-5 py-3 font-semibold"
            style={{ background: "var(--m-accent)", color: "var(--m-accent-ink)" }}
          >
            <span
              aria-hidden
              className="absolute inset-y-0 left-0"
              style={{
                width: relleno,
                background: "rgba(0,0,0,0.2)",
                transition: `width ${VENTANA_DESHACER_MS}ms linear`,
              }}
            />
            <span className="relative z-[1]">Enviar ya</span>
          </button>

          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              cancelar();
            }}
            className="w-full rounded-full px-5 py-3 font-semibold"
            style={{
              background: "transparent",
              color: "#b3261e",
              border: "1px solid #b3261e",
            }}
          >
            Cancelar pedido
          </button>
        </div>
      )}
      </div>
    </div>
  );
}

const KEYFRAMES = `
@keyframes m-os-entrada { from { opacity: 0; } to { opacity: 1; } }
@keyframes m-os-salida { from { opacity: 1; } to { opacity: 0; } }
@keyframes m-os-halo { 0%,100% { transform: scale(0.9); opacity: 0.5; } 50% { transform: scale(1.12); opacity: 1; } }
@keyframes m-os-aro { from { transform: scale(0.4); opacity: 0; } to { transform: scale(1); opacity: 1; } }
@keyframes m-os-surge { from { transform: scale(0); } to { transform: scale(1); } }
@keyframes m-os-visto { to { stroke-dashoffset: 0; } }
@keyframes m-os-escribe { to { stroke-dashoffset: 0; } }
@keyframes m-os-sube { from { transform: translateY(12px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
@keyframes m-os-sol { from { transform: translateY(10px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
/* El desplazamiento es de una longitud de onda exacta (20 px), así que al
   reiniciarse el ciclo el dibujo coincide consigo mismo y el mar fluye sin corte. */
@keyframes m-os-marea { to { transform: translateX(-20px); } }
@keyframes m-os-marea-inv { to { transform: translateX(20px); } }
/* Cada rayo se alarga y encoge hacia fuera mientras ondula: eso es lo que le da
   el meneo de gusano en vez de parecer una raya rigida. */
@keyframes m-os-rayo {
  0%,100% { transform: translateX(-1.6px) scaleX(0.88); opacity: 0.45; }
  50% { transform: translateX(1.8px) scaleX(1.14); opacity: 0.95; }
}
/* El lápiz baja al papel, recorre un renglón, salta al siguiente y se retira. */
@keyframes m-os-lapiz {
  0%,12%   { transform: translate(0,0);       opacity: 0; }
  15%      { transform: translate(0,0);       opacity: 1; }
  23%      { transform: translate(18px,0);    opacity: 1; }
  25%      { transform: translate(0,8px);     opacity: 1; }
  33%      { transform: translate(18px,8px);  opacity: 1; }
  35%      { transform: translate(0,16px);    opacity: 1; }
  43%      { transform: translate(11px,16px); opacity: 1; }
  55%      { transform: translate(11px,16px); opacity: 1; }
  62%,100% { transform: translate(11px,16px); opacity: 0; }
}
@keyframes m-os-destello {
  0% { transform: translate(0,0) scale(0); opacity: 0; }
  35% { opacity: 1; }
  100% { transform: translate(var(--tx), var(--ty)) scale(1); opacity: 0; }
}
/* Con movimiento reducido: nada de mar ni de destellos, solo lo imprescindible. */
@media (prefers-reduced-motion: reduce) {
  [role="status"] * { animation-duration: 1ms !important; animation-delay: 0ms !important; }
  [role="status"] [style*="m-os-marea"] { animation: none !important; }
}
`;
