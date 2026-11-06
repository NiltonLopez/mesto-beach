"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { OrderSummaryDto, SessionPaymentStatusDto } from "@mestobeach/api-client";
import {
  ordersApi, sessionsApi, paymentsApi,
  getStoredSession, getStoredCustomer, getDisplayName, formatEur,
} from "@/lib/api";
import { fraseDeEspera, pedidoEnCurso } from "@/lib/progresoPedido";
import StatusBadge from "@/components/StatusBadge";
import EmptyState from "@/components/EmptyState";
import { useDialog } from "@/components/Dialog";

const POLL_MS = 8000;
const ACTIVE = new Set(["PENDING", "CONFIRMED", "IN_PREPARATION"]);
/** El "desde hace…" late solo, aunque el sondeo falle. */
const TICK_MS = 20_000;
/** Tras preguntar, el botón descansa un minuto: nadie debe poder repicar a cocina. */
const ETA_COOLDOWN_MS = 60_000;
/** Lo mismo para la cuenta: pedirla dos veces seguidas confunde a caja. */
const BILL_COOLDOWN_MS = 30_000;

type Person = {
  key: string;
  name: string;
  orders: OrderSummaryDto[];
  subtotal: number;
  isCurrent: boolean;
  isPaid: boolean;
  ready: number;
  prep: number;
  pending: number;
  delivered: number;
};

interface Props {
  sessionId: string;
  /** Solo sondea mientras la ventana se está viendo: en la carta no pinta nada. */
  activo: boolean;
  onIrALaCarta: () => void;
}

export default function CuentaPane({ sessionId, activo, onIrALaCarta }: Props) {
  const dialog = useDialog();

  const [orders, setOrders] = useState<OrderSummaryDto[]>([]);
  const [pay, setPay] = useState<SessionPaymentStatusDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [sharing, setSharing] = useState(false);
  const [shareMsg, setShareMsg] = useState<string | null>(null);
  // "¿Cuándo sale?" — el aviso es de la mesa entera, así que el estado es uno solo
  // para todos los pedidos: si ya se preguntó, todos los botones descansan.
  const [askingEta, setAskingEta] = useState(false);
  const [etaSent, setEtaSent] = useState(false);
  const etaTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [billing, setBilling] = useState(false);
  const [billSent, setBillSent] = useState(false);
  const billTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [ahora, setAhora] = useState(() => Date.now());

  const session = getStoredSession();
  const customer = getStoredCustomer();
  const myName = getDisplayName();

  const fetchAll = useCallback(async () => {
    if (!session) return;
    try {
      const [ordRes, payRes] = await Promise.allSettled([
        ordersApi.listOrders(sessionId),
        paymentsApi.getSessionPaymentStatus(sessionId),
      ]);
      if (ordRes.status === "fulfilled") {
        setOrders(ordRes.value.data.data.filter((o) => o.status !== "CANCELLED"));
      }
      setPay(payRes.status === "fulfilled" ? payRes.value.data : null);
      if (ordRes.status === "rejected") setError("No se pudieron cargar los pedidos.");
      else setError(null);
    } catch {
      setError("No se pudieron cargar los pedidos.");
    } finally {
      setLoading(false);
    }
  }, [sessionId, session?.participantId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!activo) return;
    void fetchAll();
    const t = setInterval(fetchAll, POLL_MS);
    return () => clearInterval(t);
  }, [activo, fetchAll]);

  // Reloj propio para los "desde hace…" y limpieza de los descansos.
  useEffect(() => {
    const t = setInterval(() => setAhora(Date.now()), TICK_MS);
    return () => {
      clearInterval(t);
      if (etaTimer.current) clearTimeout(etaTimer.current);
      if (billTimer.current) clearTimeout(billTimer.current);
    };
  }, []);

  const tableTotal = useMemo(() => orders.reduce((s, o) => s + o.totalCents, 0), [orders]);
  const paidTotal = pay?.totalPaidCents ?? 0;
  const remaining = pay?.remainingCents ?? Math.max(0, tableTotal - paidTotal);
  const fullyPaid = !!pay?.isFullyPaid;
  const paidPct = tableTotal > 0 ? Math.min(100, Math.round((paidTotal / tableTotal) * 100)) : 0;

  // Nombres con un pago por persona completado.
  const paidNames = useMemo(() => {
    const s = new Set<string>();
    (pay?.payments ?? [])
      .filter((p) => p.status === "COMPLETED" && p.payerLabel)
      .forEach((p) => s.add(p.payerLabel!.trim().toLowerCase()));
    return s;
  }, [pay]);

  // Agrupa por el nombre con el que se pidió; la persona actual va primero.
  const people: Person[] = useMemo(() => {
    const myId = session?.participantId;
    const groups = new Map<string, Person>();
    orders.forEach((o) => {
      const name = o.customerName?.trim() || "Comensal";
      const key = o.customerName?.trim() ? `name:${name.toLowerCase()}` : `part:${o.participantId}`;
      const g = groups.get(key) ?? {
        key, name, orders: [], subtotal: 0, isCurrent: false, isPaid: false,
        ready: 0, prep: 0, pending: 0, delivered: 0,
      };
      g.orders.push(o);
      g.subtotal += o.totalCents;
      if (o.participantId === myId || (myName && name.toLowerCase() === myName.toLowerCase())) g.isCurrent = true;
      if (o.status === "READY") g.ready++;
      else if (o.status === "IN_PREPARATION") g.prep++;
      else if (o.status === "PENDING" || o.status === "CONFIRMED") g.pending++;
      else if (o.status === "DELIVERED") g.delivered++;
      groups.set(key, g);
    });
    const list = Array.from(groups.values()).map((g) => ({
      ...g,
      isPaid: fullyPaid || paidNames.has(g.name.toLowerCase()),
    }));
    list.sort((a, b) => (a.isCurrent === b.isCurrent ? a.name.localeCompare(b.name) : a.isCurrent ? -1 : 1));
    return list;
  }, [orders, session?.participantId, myName, fullyPaid, paidNames]);

  // La tarjeta de quien mira se abre sola.
  useEffect(() => {
    const cur = people.find((p) => p.isCurrent);
    if (cur) setExpanded((prev) => (prev.size === 0 ? new Set([cur.key]) : prev));
  }, [people]);

  function toggle(key: string) {
    setExpanded((prev) => {
      const n = new Set(prev);
      if (n.has(key)) n.delete(key); else n.add(key);
      return n;
    });
  }

  async function handleCancel(orderId: string) {
    const reason = await dialog.prompt({
      title: "Cancelar pedido",
      message: "¿Por qué deseas cancelar este pedido?",
      placeholder: "Motivo (opcional)",
      confirmText: "Cancelar pedido",
    });
    if (!reason || reason.trim().length < 5) return;
    try {
      await ordersApi.cancelOrder(orderId, reason.trim());
      await fetchAll();
    } catch (err: unknown) {
      dialog.toast(
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? "No se pudo cancelar.",
        { tone: "error" },
      );
    }
  }

  /**
   * "¿Cuándo sale?" — deja un aviso de mesa (motivo ETA) en Salón/PDA. No llega a
   * cocina y no interrumpe nada: alguien de sala se acerca a contarlo en persona.
   */
  async function handleAskEta() {
    if (!session || askingEta || etaSent) return;
    setAskingEta(true);
    try {
      await sessionsApi.askEta(sessionId);
      setEtaSent(true);
      dialog.toast("Ya preguntamos por tu mesa. Alguien de sala pasará a decirte cómo va.", {
        tone: "success",
      });
      if (etaTimer.current) clearTimeout(etaTimer.current);
      etaTimer.current = setTimeout(() => setEtaSent(false), ETA_COOLDOWN_MS);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      dialog.toast(
        status === 404
          ? "Esta mesa ya no tiene una sesión activa. Vuelve a escanear el QR."
          : "No se pudo preguntar. Inténtalo de nuevo.",
        { tone: "error" },
      );
    } finally {
      setAskingEta(false);
    }
  }

  /** Pedir la cuenta: avisa a caja. Vive aquí, con el total delante. */
  async function handleRequestBill() {
    if (!session || billing || billSent) return;
    setBilling(true);
    try {
      await sessionsApi.requestBill(sessionId);
      setBillSent(true);
      dialog.toast("¡Cuenta pedida! El cajero la está preparando.", { tone: "success" });
      if (billTimer.current) clearTimeout(billTimer.current);
      billTimer.current = setTimeout(() => setBillSent(false), BILL_COOLDOWN_MS);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      dialog.toast(
        status === 404
          ? "Esta mesa ya no tiene una sesión activa. Vuelve a escanear el QR."
          : "No se pudo pedir la cuenta. Intenta de nuevo.",
        { tone: "error" },
      );
    } finally {
      setBilling(false);
    }
  }

  async function handleShare() {
    if (!session || sharing) return;
    setSharing(true);
    setShareMsg(null);
    try {
      const { data } = await sessionsApi.createInviteLink(sessionId, session.tenantId);
      const origin = typeof window !== "undefined" ? window.location.origin : "";
      const joinUrl = `${origin}/join?tenantId=${encodeURIComponent(session.tenantId)}&token=${encodeURIComponent(data.token)}&type=INVITE`;
      const msg = `¡Únete a nuestra mesa${session.tableCode ? ` (${session.tableCode})` : ""} en Mesto Beach! Pide desde tu móvil sin ingresar PIN:`;
      const wa = `https://wa.me/?text=${encodeURIComponent(`${msg}\n${joinUrl}`)}`;
      if (navigator.share) {
        await navigator.share({ title: "Únete a la mesa", text: msg, url: joinUrl }).catch(() => window.open(wa, "_blank"));
      } else {
        window.open(wa, "_blank");
      }
      setShareMsg("Link de invitación listo — sin PIN para quien lo abra.");
    } catch {
      setShareMsg("No se pudo crear el link. Intenta de nuevo.");
    } finally {
      setSharing(false);
    }
  }

  return (
    <div className="h-full overflow-y-auto overscroll-contain">
      <div className="px-5 pb-3 pt-6">
        <h1 className="m-head text-3xl" style={{ color: "var(--m-text)" }}>Mi cuenta</h1>
        <p className="mt-1 text-sm" style={{ color: "var(--m-muted)" }}>
          {session?.tableCode ? `Mesa ${session.tableCode}` : "Mesa"}
          {customer && <span className="m-accent-text ml-2 font-semibold">✦ {customer.loyaltyPoints} pts</span>}
        </p>
      </div>

      <div
        className="space-y-4 px-4"
        style={{ paddingBottom: "calc(env(safe-area-inset-bottom) + 6rem)" }}
      >
        {loading && (
          <div className="flex justify-center py-10">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-t-transparent" style={{ borderColor: "var(--m-border)", borderTopColor: "var(--m-accent)" }} />
          </div>
        )}

        {!loading && error && (
          <div className="rounded-xl bg-red-500/10 p-4 text-center text-sm text-red-600">{error}</div>
        )}

        {!loading && !error && (
          <>
            {/* Total de la mesa */}
            <div
              className={`rounded-3xl p-5 shadow-warm-lg ${fullyPaid ? "border border-emerald-500/40 bg-emerald-500/[0.08]" : "m-card"}`}
            >
              <div className="flex items-start justify-between">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em]" style={{ color: "var(--m-muted)" }}>Cuenta de la mesa</p>
                  <p className="m-head mt-1 text-4xl" style={{ color: "var(--m-text)" }}>{formatEur(tableTotal)}</p>
                </div>
                {fullyPaid && (
                  <span className="rounded-full bg-emerald-500/10 px-3 py-1 text-xs font-medium tracking-[0.03em] text-emerald-700">Pagada</span>
                )}
              </div>

              <div className="mt-4">
                <div className="h-2 w-full overflow-hidden rounded-full" style={{ background: "color-mix(in srgb, var(--m-accent) 12%, transparent)" }}>
                  <div
                    className="h-full rounded-full transition-all"
                    style={{ width: `${paidPct}%`, background: fullyPaid ? "#10b981" : "var(--m-accent)" }}
                  />
                </div>
                <div className="mt-2 flex justify-between text-sm" style={{ color: "var(--m-muted)" }}>
                  <span>Pagado <strong style={{ color: "var(--m-text)" }}>{formatEur(paidTotal)}</strong></span>
                  <span>Pendiente <strong style={{ color: "var(--m-text)" }}>{formatEur(remaining)}</strong></span>
                </div>
              </div>
            </div>

            {/* Pedir la cuenta: el gesto de esta ventana, en grande y con su total */}
            {!fullyPaid && orders.length > 0 && (
              <div className="space-y-1.5">
                <button
                  onClick={handleRequestBill}
                  disabled={billing || billSent}
                  className="btn-primary w-full text-base"
                  style={billSent
                    ? { background: "var(--m-surface)", color: "var(--m-accent)", border: "1px solid var(--m-accent)", opacity: 1 }
                    : undefined}
                >
                  {billSent
                    ? "Cuenta pedida — el cajero va para allá"
                    : billing
                      ? "Pidiendo la cuenta…"
                      : `Pedir la cuenta · ${formatEur(remaining)}`}
                </button>
                <p className="text-center text-xs" style={{ color: "var(--m-muted)" }}>
                  Avisa a caja de que queréis pagar. No cierra la mesa: podéis seguir pidiendo.
                </p>
              </div>
            )}

            {/* Invitar */}
            <button
              onClick={handleShare}
              disabled={sharing}
              className="m-card flex w-full items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-medium tracking-[0.02em] shadow-warm-sm transition active:scale-[.99] disabled:opacity-60"
              style={{ color: "var(--m-text)" }}
            >
              {sharing ? "Generando link…" : "Invitar a la mesa por WhatsApp"}
            </button>
            {shareMsg && <p className="-mt-2 text-center text-xs" style={{ color: "var(--m-muted)" }}>{shareMsg}</p>}

            {/* Por persona */}
            <section className="space-y-3">
              <h2 className="m-head text-lg" style={{ color: "var(--m-text)" }}>Por persona</h2>
              {people.length === 0 && (
                <div className="m-card flex flex-col items-center rounded-2xl py-4 text-center shadow-warm-sm">
                  <EmptyState title="Aún no hay pedidos en la mesa" />
                  <button onClick={onIrALaCarta} className="btn-primary -mt-4 mb-2">Ver la carta</button>
                </div>
              )}
              {people.map((p) => {
                const open = expanded.has(p.key);
                return (
                  <div
                    key={p.key}
                    className="m-card overflow-hidden rounded-2xl shadow-warm-sm"
                    style={p.isCurrent ? { borderColor: "var(--m-accent)" } : undefined}
                  >
                    <button onClick={() => toggle(p.key)} className="flex w-full items-center gap-3 px-4 py-3 text-left">
                      <span
                        className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full text-sm font-medium text-white"
                        style={{ background: p.isPaid ? "#10b981" : "var(--m-accent)", color: p.isPaid ? "#fff" : "var(--m-accent-ink)" }}
                      >
                        {p.name.charAt(0).toUpperCase()}
                      </span>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="m-head truncate text-base" style={{ color: "var(--m-text)" }}>{p.name}</span>
                          {p.isCurrent && (
                            <span
                              className="m-accent-text rounded-full px-2 py-0.5 text-[10px] font-medium tracking-[0.03em]"
                              style={{ background: "color-mix(in srgb, var(--m-accent) 10%, transparent)" }}
                            >
                              Tú
                            </span>
                          )}
                          {p.isPaid && <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium tracking-[0.03em] text-emerald-700">✓ Pagado</span>}
                        </div>
                        <p className="text-xs" style={{ color: "var(--m-muted)" }}>
                          {[p.ready && `${p.ready} listo`, p.prep && `${p.prep} en prep.`, p.pending && `${p.pending} en cola`, p.delivered && `${p.delivered} entregado`]
                            .filter(Boolean).join(" · ") || `${p.orders.length} pedido(s)`}
                        </p>
                      </div>
                      <div className="text-right">
                        <p className="font-semibold" style={{ color: "var(--m-text)" }}>{formatEur(p.subtotal)}</p>
                        <span className="text-xs" style={{ color: "var(--m-muted)" }}>{open ? "▲" : "▼"}</span>
                      </div>
                    </button>

                    {open && (
                      <div className="space-y-3 border-t px-4 py-3" style={{ borderColor: "var(--m-border)" }}>
                        {p.orders.map((o) => {
                          const enCurso = pedidoEnCurso(o.status);
                          // Responder antes de que pregunten: desde cuándo lleva en este paso.
                          const espera = fraseDeEspera(o, ahora);
                          return (
                            <div key={o.id}>
                              <div className="mb-1 flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
                                <span className="text-[11px]" style={{ color: "var(--m-muted)" }}>Orden #{o.id.slice(-6).toUpperCase()}</span>
                                <span className="flex items-center gap-1.5">
                                  <StatusBadge status={o.status} />
                                  {espera && (
                                    <span className="text-[11px]" style={{ color: "var(--m-muted)" }}>· {espera}</span>
                                  )}
                                </span>
                              </div>
                              {o.items.map((it) => (
                                <div key={it.id} className="flex justify-between text-sm" style={{ color: "var(--m-text)" }}>
                                  <span>
                                    {it.quantity}× {it.itemNameSnapshot}
                                    {it.selectedModifiers ? <em className="m-accent-text not-italic"> · {it.selectedModifiers}</em> : null}
                                    {it.notes ? <em style={{ color: "var(--m-muted)" }}> — {it.notes}</em> : null}
                                  </span>
                                  <span>{formatEur(it.itemPriceSnapshotCents * it.quantity)}</span>
                                </div>
                              ))}
                              {enCurso && (
                                <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-2">
                                  <button
                                    type="button"
                                    onClick={handleAskEta}
                                    disabled={askingEta || etaSent}
                                    className="rounded-full px-3.5 py-1.5 text-xs font-medium tracking-[0.03em] transition active:scale-95"
                                    style={{
                                      border: "1px solid var(--m-border)",
                                      background: etaSent
                                        ? "color-mix(in srgb, var(--m-accent) 12%, transparent)"
                                        : "transparent",
                                      color: "var(--m-accent)",
                                      opacity: askingEta ? 0.7 : 1,
                                    }}
                                  >
                                    {etaSent ? "Ya avisamos" : askingEta ? "Avisando…" : "¿Cuándo sale?"}
                                  </button>
                                  {p.isCurrent && ACTIVE.has(o.status) && (
                                    <button onClick={() => handleCancel(o.id)} className="text-xs font-medium text-rose-600 underline">Cancelar pedido</button>
                                  )}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>
                );
              })}
            </section>

            {/* Pagos hechos */}
            {(pay?.payments?.length ?? 0) > 0 && (
              <section>
                <h2 className="m-head mb-2 text-lg" style={{ color: "var(--m-text)" }}>Pagos realizados</h2>
                <div className="m-card space-y-2 rounded-2xl p-3 shadow-warm-sm">
                  {pay!.payments.filter((p) => p.status === "COMPLETED").map((p) => (
                    <div key={p.id} className="flex items-center justify-between text-sm">
                      <span style={{ color: "var(--m-muted)" }}>
                        {p.payerLabel ? p.payerLabel : "Pago"}{p.tipCents ? ` (incl. ${formatEur(p.tipCents)} propina)` : ""}
                      </span>
                      <span className="font-semibold" style={{ color: "var(--m-text)" }}>{formatEur(p.totalCents)}</span>
                    </div>
                  ))}
                </div>
              </section>
            )}

            {orders.length > 0 && (
              <p className="text-center text-xs" style={{ color: "var(--m-muted)" }}>
                Se actualiza cada {POLL_MS / 1000}s
              </p>
            )}
          </>
        )}
      </div>
    </div>
  );
}
