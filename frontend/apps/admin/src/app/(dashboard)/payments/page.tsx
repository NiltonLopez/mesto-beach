"use client";

import { useEffect, useState } from "react";
import type { SessionPaymentStatusDto, RecentPaymentsDto } from "@mestobeach/api-client";
import { paymentsApi, formatEur, parseEurosToCents, todayStr, daysAgoStr } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";

function dateTimeLabel(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
}

export default function PaymentsPage() {
  const { staff } = useAuthStore();
  const isAdmin = staff?.roles?.includes("Admin") ?? false;
  const isCajero = (staff?.roles?.includes("Cajero") ?? false) || isAdmin;

  const [sessionId, setSessionId] = useState("");
  const [status, setStatus] = useState<SessionPaymentStatusDto | null>(null);
  const [loadingStatus, setLoadingStatus] = useState(false);

  // Recent sales list
  const [recent, setRecent] = useState<RecentPaymentsDto | null>(null);
  const [loadingRecent, setLoadingRecent] = useState(true);
  const [recFrom, setRecFrom] = useState(daysAgoStr(29));
  const [recTo, setRecTo] = useState(todayStr());

  async function loadRecent() {
    setLoadingRecent(true);
    try {
      const { data } = await paymentsApi.listRecentPayments({ from: recFrom, to: recTo, limit: 200 });
      setRecent(data);
    } catch {
      setRecent(null);
    } finally {
      setLoadingRecent(false);
    }
  }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { void loadRecent(); }, []);

  // Cash payment form
  const [amount, setAmount] = useState("");
  const [tip, setTip] = useState("");
  const [splitIndex, setSplitIndex] = useState("");
  const [splitTotal, setSplitTotal] = useState("");
  const [paying, setPaying] = useState(false);
  const [paymentResult, setPaymentResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleLoadStatus(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setStatus(null);
    setLoadingStatus(true);
    try {
      const { data } = await paymentsApi.getSessionPaymentStatus(sessionId.trim());
      setStatus(data);
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "No se encontró información de pago.";
      setError(detail);
    } finally {
      setLoadingStatus(false);
    }
  }

  async function handleCashPayment(e: React.FormEvent) {
    e.preventDefault();
    if (!sessionId) return;
    setError(null);
    setPaymentResult(null);
    const amountCents = parseEurosToCents(amount);
    if (amountCents === null || amountCents <= 0) {
      setError("Escribe un importe válido en euros, mayor que 0. Ej: 25,00");
      return;
    }

    setPaying(true);
    try {
      const payload: {
        sessionId: string;
        amountCents: number;
        tipCents?: number;
        splitIndex?: number;
        splitTotal?: number;
      } = {
        sessionId: sessionId.trim(),
        amountCents: amountCents,
      };
      if (tip) {
        const tipCents = parseEurosToCents(tip);
        if (tipCents === null) throw new Error("La propina no es un importe válido. Ej: 2,50");
        payload.tipCents = tipCents;
      }
      if (splitIndex && splitTotal) {
        payload.splitIndex = Number(splitIndex);
        payload.splitTotal = Number(splitTotal);
      }
      const { data } = await paymentsApi.registerCashPayment(payload);
      setPaymentResult(
        `Pago registrado: ${formatEur(data.totalCents)}${data.sessionClosed ? " — Sesión cerrada automáticamente." : ""}`
      );
      // Reload status
      const { data: newStatus } = await paymentsApi.getSessionPaymentStatus(sessionId.trim());
      setStatus(newStatus);
      setAmount("");
      setTip("");
      setSplitIndex("");
      setSplitTotal("");
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "Error al registrar el pago.";
      setError(detail);
    } finally {
      setPaying(false);
    }
  }

  return (
    <div className="p-6">
      <h1 className="mb-6 font-display text-3xl text-ink">Pagos</h1>

      {/* Ventas recientes */}
      <div className="card mb-4 p-5">
        <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="font-display text-lg text-ink">Ventas recientes</h2>
            <p className="text-xs text-muted">Pagos recibidos por tu restaurante.</p>
          </div>
          <div className="flex items-end gap-2">
            <div>
              <label className="block text-[11px] text-muted">Desde</label>
              <input type="date" value={recFrom} max={recTo} onChange={(e) => setRecFrom(e.target.value)} className="input py-1.5 text-sm w-36" />
            </div>
            <div>
              <label className="block text-[11px] text-muted">Hasta</label>
              <input type="date" value={recTo} min={recFrom} onChange={(e) => setRecTo(e.target.value)} className="input py-1.5 text-sm w-36" />
            </div>
            <button onClick={() => void loadRecent()} disabled={loadingRecent} className="btn-secondary py-1.5 text-sm">
              {loadingRecent ? "…" : "Aplicar"}
            </button>
          </div>
        </div>

        {/* Totals */}
        <div className="mb-4 grid grid-cols-3 gap-3 text-center">
          <div className="rounded-xl bg-oro/[0.08] p-3">
            <p className="text-xs text-muted">Total ventas</p>
            <p className="font-semibold text-brand-700">{formatEur(recent?.totalCents ?? 0)}</p>
          </div>
          <div className="rounded-xl bg-paper2 p-3">
            <p className="text-xs text-muted"># Pagos</p>
            <p className="font-semibold text-ink">{recent?.count ?? 0}</p>
          </div>
          <div className="rounded-xl bg-amber-500/10 p-3">
            <p className="text-xs text-muted">Propinas</p>
            <p className="font-semibold text-amber-700">{formatEur(recent?.totalTipsCents ?? 0)}</p>
          </div>
        </div>

        {loadingRecent ? (
          <p className="py-6 text-center text-sm text-muted">Cargando ventas…</p>
        ) : !recent || recent.items.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted">No hay ventas en este rango.</p>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-hair/50">
            <table className="w-full text-sm">
              <thead className="border-b border-hair/50 bg-paper2">
                <tr>
                  <th className="table-th">Fecha</th>
                  <th className="table-th">Pagador</th>
                  <th className="table-th">Método</th>
                  <th className="table-th text-right">Monto</th>
                  <th className="table-th text-right">Propina</th>
                  <th className="table-th text-right">Total</th>
                  <th className="table-th">Estado</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hair/40">
                {recent.items.map((p) => (
                  <tr key={p.id}>
                    <td className="table-td whitespace-nowrap text-muted">{dateTimeLabel(p.createdAt)}</td>
                    <td className="table-td">{p.payerLabel ?? "—"}</td>
                    <td className="table-td font-semibold">{p.method === "Cash" ? "Efectivo" : p.method}</td>
                    <td className="table-td text-right">{formatEur(p.amountCents)}</td>
                    <td className="table-td text-right text-muted">{p.tipCents > 0 ? formatEur(p.tipCents) : "—"}</td>
                    <td className="table-td text-right font-semibold text-brand-700">{formatEur(p.totalCents)}</td>
                    <td className="table-td">
                      <span className={`rounded-full px-2 py-0.5 text-xs font-medium tracking-[0.03em] ${
                        p.status === "Completed" ? "bg-oro/[0.10] text-brand-700" : "bg-paper2 text-muted"
                      }`}>{p.status === "Completed" ? "Completado" : p.status}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Session lookup */}
      <div className="card p-5 mb-4">
        <h2 className="mb-3 font-display text-lg text-ink">Consultar sesión</h2>
        <form onSubmit={handleLoadStatus} className="flex gap-3">
          <input
            type="text"
            value={sessionId}
            onChange={(e) => setSessionId(e.target.value)}
            className="input flex-1 font-mono text-xs"
            placeholder="Session ID (UUID)"
            required
          />
          <button type="submit" disabled={loadingStatus} className="btn-secondary">
            {loadingStatus ? "Cargando…" : "Cargar"}
          </button>
        </form>
        {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      </div>

      {/* Payment status */}
      {status && (
        <div className="card p-5 mb-4 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="font-display text-lg text-ink">Estado de pago</h2>
            <span
              className={`rounded-full px-3 py-0.5 text-xs font-medium tracking-[0.03em] ${
                status.isFullyPaid
                  ? "bg-oro/[0.10] text-brand-700"
                  : "bg-amber-500/10 text-amber-700"
              }`}
            >
              {status.isFullyPaid ? "Pagado" : "Pendiente"}
            </span>
          </div>

          <div className="grid grid-cols-3 gap-3 text-center">
            <div className="rounded-xl bg-paper2 p-3">
              <p className="text-xs text-muted">Total órdenes</p>
              <p className="font-semibold text-ink">{formatEur(status.ordersTotalCents)}</p>
            </div>
            <div className="rounded-xl bg-oro/[0.08] p-3">
              <p className="text-xs text-muted">Pagado</p>
              <p className="font-semibold text-brand-700">{formatEur(status.totalPaidCents)}</p>
            </div>
            <div className="rounded-xl bg-red-500/10 p-3">
              <p className="text-xs text-muted">Pendiente</p>
              <p className="font-semibold text-red-700">{formatEur(status.remainingCents)}</p>
            </div>
          </div>

          {status.payments.length > 0 && (
            <div className="overflow-x-auto rounded-xl border border-hair/50">
              <table className="w-full min-w-[32rem] text-sm">
                <thead className="bg-paper2 border-b border-hair/50">
                  <tr>
                    <th className="table-th">Método</th>
                    <th className="table-th text-right">Monto</th>
                    <th className="table-th text-right">Propina</th>
                    <th className="table-th text-right">Total</th>
                    <th className="table-th">Estado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-hair/40">
                  {status.payments.map((p) => (
                    <tr key={p.id}>
                      <td className="table-td font-semibold">{p.method}</td>
                      <td className="table-td text-right">{formatEur(p.amountCents)}</td>
                      <td className="table-td text-right">{formatEur(p.tipCents)}</td>
                      <td className="table-td text-right font-semibold text-brand-700">{formatEur(p.totalCents)}</td>
                      <td className="table-td">
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium tracking-[0.03em] ${
                          p.status === "COMPLETED" ? "bg-oro/[0.10] text-brand-700" : "bg-paper2 text-muted"
                        }`}>{p.status}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Cash payment form */}
      {isCajero && sessionId && (
        <div className="card p-5">
          <h2 className="mb-4 font-display text-lg text-ink">Registrar pago en efectivo</h2>

          {paymentResult && (
            <div className="mb-4 rounded-xl bg-oro/[0.08] border border-hair/50 px-4 py-3 text-sm text-brand-700">
              {paymentResult}
            </div>
          )}

          <form onSubmit={handleCashPayment} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="label">Monto (€) *</label>
                <input
                  type="number"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  className="input"
                  placeholder="25.00"
                  min={0.01}
                  step={0.01}
                  required
                />
              </div>
              <div>
                <label className="label">Propina (€)</label>
                <input
                  type="number"
                  value={tip}
                  onChange={(e) => setTip(e.target.value)}
                  className="input"
                  placeholder="0.00"
                  min={0}
                  step={0.01}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="label">División # (opcional)</label>
                <input
                  type="number"
                  value={splitIndex}
                  onChange={(e) => setSplitIndex(e.target.value)}
                  className="input"
                  placeholder="1"
                  min={1}
                />
              </div>
              <div>
                <label className="label">Total divisiones</label>
                <input
                  type="number"
                  value={splitTotal}
                  onChange={(e) => setSplitTotal(e.target.value)}
                  className="input"
                  placeholder="2"
                  min={2}
                />
              </div>
            </div>

            <button type="submit" disabled={paying} className="btn-primary w-full">
              {paying ? "Procesando…" : "Registrar pago en efectivo"}
            </button>
          </form>
        </div>
      )}
    </div>
  );
}
