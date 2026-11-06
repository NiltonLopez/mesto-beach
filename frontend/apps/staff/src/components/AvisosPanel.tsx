"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { WaiterCallDto } from "@mestobeach/api-client";
import { Bell, Clock, Key, Receipt } from "@phosphor-icons/react";
// Umbrales compartidos con el Salón: las dos pantallas tienen que pintar del
// mismo color el mismo aviso.
import { desdeHace, nivelDe, segundosDesde } from "@/lib/avisos";
import { useVenue } from "@/lib/venue";
import "./avisos.css";

type Motivo = "pin" | "bill" | "eta" | "call";

function motivoDe(reason: WaiterCallDto["reason"]): Motivo {
  if (reason === "PIN") return "pin";
  if (reason === "BILL") return "bill";
  if (reason === "ETA") return "eta";
  return "call"; // WAITER y cualquier motivo futuro
}

const COPY: Record<Motivo, { titulo: string; detalle: string }> = {
  pin: { titulo: "Pide el PIN", detalle: "No puede pedir hasta que se lo dictes" },
  bill: { titulo: "Pide la cuenta", detalle: "Quiere pagar y marcharse" },
  eta: { titulo: "Pregunta por su plato", detalle: "¿Cuándo sale? — quiere saber cómo va" },
  call: { titulo: "Te llaman", detalle: "Solicita atención en la mesa" },
};

const ICONO = { pin: Key, bill: Receipt, eta: Clock, call: Bell } as const;

interface Props {
  avisos: WaiterCallDto[];
  /** Da el aviso por atendido (lo borra para todas las pantallas). */
  onAtender: (aviso: WaiterCallDto) => void;
  /** sessionId en curso, para bloquear su botón. */
  atendiendo: string | null;
  /** Selecciona la mesa en el mapa, para saber de cuál se trata. */
  onIrAMesa?: (tableCode: string) => void;
  /** Descubre el PIN de la mesa cuando el comensal lo ha pedido. */
  onVerPin: (sessionId: string) => Promise<string | null>;
  /** Un aviso acaba de cruzar a "urgente": la página decide si suena. */
  onEscalada: (sessionId: string) => void;
}

export default function AvisosPanel({
  avisos, onAtender, atendiendo, onIrAMesa, onVerPin, onEscalada,
}: Props) {
  // PRD-019 (SDD-003): de noche, los avisos de un reservado van primero.
  const { mode } = useVenue();
  const nocturno = mode === "NIGHT";

  // Reloj propio: los avisos envejecen aunque no llegue nada nuevo, y por debajo
  // del minuto se cuenta en segundos, así que el latido tiene que ser de 1 s.
  const [ahora, setAhora] = useState(() => Date.now());
  useEffect(() => {
    if (avisos.length === 0) return;
    const t = setInterval(() => setAhora(Date.now()), 1000);
    return () => clearInterval(t);
  }, [avisos.length]);

  const [pines, setPines] = useState<Record<string, string>>({});
  const [pidiendoPin, setPidiendoPin] = useState<string | null>(null);

  // Cada aviso avisa UNA vez al escalar. Sin este registro, el reloj de 1 s
  // dispararía el aviso sonoro en cada tic mientras siga sin atenderse.
  const yaEscalados = useRef(new Set<string>());
  const escalar = useRef(onEscalada);
  escalar.current = onEscalada;

  const conEdad = useMemo(
    () =>
      avisos
        .map((a) => {
          const segundos = segundosDesde(a.createdAt, ahora);
          return { aviso: a, segundos, nivel: nivelDe(segundos) };
        })
        // En NIGHT, reservados primero (SDD-003, ADR-038); después, el más viejo primero:
        // es el que lleva más tiempo esperando. De día, solo por antigüedad.
        .sort((x, y) => {
          if (nocturno) {
            const rx = x.aviso.tableKind === "RESERVADO" ? 1 : 0;
            const ry = y.aviso.tableKind === "RESERVADO" ? 1 : 0;
            if (rx !== ry) return ry - rx;
          }
          return y.segundos - x.segundos;
        }),
    [avisos, ahora, nocturno],
  );

  useEffect(() => {
    for (const { aviso, nivel } of conEdad) {
      if (nivel === "urgente" && !yaEscalados.current.has(aviso.sessionId)) {
        yaEscalados.current.add(aviso.sessionId);
        escalar.current(aviso.sessionId);
      }
    }
    // Un aviso atendido se olvida, para que el siguiente de esa mesa vuelva a sonar.
    const vivos = new Set(avisos.map((a) => a.sessionId));
    for (const id of yaEscalados.current) if (!vivos.has(id)) yaEscalados.current.delete(id);
  }, [conEdad, avisos]);

  async function verPin(sessionId: string) {
    setPidiendoPin(sessionId);
    try {
      const pin = await onVerPin(sessionId);
      if (pin) setPines((p) => ({ ...p, [sessionId]: pin }));
    } finally {
      setPidiendoPin(null);
    }
  }

  const urgentes = conEdad.filter((a) => a.nivel === "urgente").length;

  return (
    <section className="avisos-root border-b border-hair/40 bg-paper2 px-4 py-3">
      <div className="mb-2 flex items-baseline gap-2">
        <h2 className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">
          Avisos de mesa
        </h2>
        {avisos.length > 0 && (
          <span className="text-xs text-muted">
            {avisos.length}
            {urgentes > 0 && <strong className="ml-1 text-rose-600">· {urgentes} sin atender</strong>}
          </span>
        )}
      </div>

      {avisos.length === 0 ? (
        <p className="py-3 text-center text-sm text-muted">
          Sin avisos en tu sector. Los llamados de los comensales aparecen aquí.
        </p>
      ) : (
        <div className="flex gap-3 overflow-x-auto pb-1">
          {conEdad.map(({ aviso, segundos, nivel }) => {
            const motivo = motivoDe(aviso.reason);
            const copy = COPY[motivo];
            const Icono = ICONO[motivo];
            const pin = pines[aviso.sessionId];
            return (
              <div key={aviso.sessionId} className="aviso-entra w-64 shrink-0">
              <article
                data-nivel={nivel}
                className="aviso flex h-full flex-col gap-2 rounded-2xl bg-paper p-3"
                style={{ border: "1.5px solid var(--aviso)" }}
              >
                <div className="flex items-start gap-2.5">
                  <span
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
                    style={{ background: "var(--aviso-tenue)", color: "var(--aviso)" }}
                  >
                    <Icono size={20} aria-hidden />
                  </span>
                  <div className="min-w-0 flex-1">
                    {onIrAMesa ? (
                      <button
                        onClick={() => onIrAMesa(aviso.tableCode)}
                        className="font-display text-xl leading-none text-ink underline-offset-4 hover:underline"
                        title="Ver esta mesa en el mapa"
                      >
                        {aviso.tableCode}
                      </button>
                    ) : (
                      <span className="font-display text-xl leading-none text-ink">{aviso.tableCode}</span>
                    )}
                    <p className="mt-0.5 truncate text-sm font-semibold text-ink">{copy.titulo}</p>
                  </div>
                  <span className="flex shrink-0 items-center gap-1.5">
                    <span
                      className="punto inline-block h-2 w-2 rounded-full"
                      style={{ background: "var(--aviso)" }}
                      aria-hidden
                    />
                    <span
                      className="text-xs font-semibold tabular-nums"
                      style={{ color: "var(--aviso)" }}
                    >
                      {desdeHace(segundos)}
                    </span>
                  </span>
                </div>

                <p className="text-xs leading-snug text-muted">
                  {pin ? <>PIN de la mesa: <strong className="text-ink tracking-[0.3em]">{pin}</strong></> : copy.detalle}
                </p>

                <div className="flex gap-2">
                  {motivo === "pin" && !pin && (
                    <button
                      onClick={() => void verPin(aviso.sessionId)}
                      disabled={pidiendoPin === aviso.sessionId}
                      className="flex-1 rounded-lg border border-hair/55 px-3 py-1.5 text-xs font-bold text-ink hover:bg-paper2 disabled:opacity-50"
                    >
                      {pidiendoPin === aviso.sessionId ? "…" : "Ver PIN"}
                    </button>
                  )}
                  <button
                    onClick={() => onAtender(aviso)}
                    disabled={atendiendo === aviso.sessionId}
                    className="flex-1 rounded-lg px-3 py-1.5 text-xs font-extrabold text-white disabled:opacity-50"
                    style={{ background: "var(--aviso)" }}
                  >
                    {atendiendo === aviso.sessionId ? "…" : "Atendido"}
                  </button>
                </div>
              </article>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
