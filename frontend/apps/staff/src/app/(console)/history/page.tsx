"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import type { ClosedSessionDto, ListOrdersResponse, SessionPaymentStatusDto } from "@mestobeach/api-client";
import { getStoredStaff, getToken, ordersApi, paymentsApi, sessionsApi } from "@/lib/api";
import { FullscreenButton } from "@/components/FullscreenButton";

/** `amount` llega en céntimos de euro: 1450 -> "14,50 €". */
function formatEur(amount: number): string {
  return new Intl.NumberFormat("es-ES", {
    style: "currency",
    currency: "EUR",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount / 100);
}

type Detail = { orders: ListOrdersResponse["data"]; payment: SessionPaymentStatusDto } | null;

export default function HistoryPage() {
  const router = useRouter();
  const [ready, setReady] = useState(false);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [tableCode, setTableCode] = useState("");
  const [items, setItems] = useState<ClosedSessionDto[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [detail, setDetail] = useState<Detail>(null);

  useEffect(() => {
    if (!getToken() || !getStoredStaff()) {
      router.replace("/login");
      return;
    }
    setReady(true);
  }, [router]);

  useEffect(() => {
    if (ready) void search();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready]);

  async function search() {
    setLoading(true);
    setError(null);
    try {
      const { data } = await sessionsApi.listClosedSessions({
        ...(from ? { from: new Date(from).toISOString() } : {}),
        ...(to ? { to: new Date(to + "T23:59:59").toISOString() } : {}),
        ...(tableCode.trim() ? { tableCode: tableCode.trim() } : {}),
        page: 1,
        pageSize: 50,
      });
      setItems(data.items);
      setTotal(data.total);
    } catch (err: unknown) {
      setError((err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? "No se pudo cargar el historial.");
    } finally {
      setLoading(false);
    }
  }

  async function toggleDetail(sessionId: string) {
    if (openId === sessionId) {
      setOpenId(null);
      setDetail(null);
      return;
    }
    setOpenId(sessionId);
    setDetail(null);
    try {
      const [ordersRes, payRes] = await Promise.all([
        ordersApi.listOrders(sessionId),
        paymentsApi.getSessionPaymentStatus(sessionId),
      ]);
      setDetail({ orders: ordersRes.data.data, payment: payRes.data });
    } catch {
      setDetail(null);
    }
  }

  if (!ready) return null;

  return (
    <main style={{ minHeight: "100vh", padding: 20 }}>
      <div style={{ maxWidth: 900, margin: "0 auto", display: "grid", gap: 16 }}>
        <header style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
          <h1 className="font-display" style={{ margin: 0, fontSize: 28 }}>Historial de sesiones</h1>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            {/* Sin Tailwind: aquí manda `.btn-secondary`, la misma piel que el botón de al lado. */}
            <FullscreenButton unstyled className="btn-secondary gap-2" />
            <button onClick={() => router.push("/cashdesk")} className="btn-secondary">← Cashdesk</button>
          </div>
        </header>

        <div className="card p-4" style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "end" }}>
          <label style={{ display: "grid", gap: 4 }}><span style={{ fontSize: 12, color: "rgb(var(--muted))" }}>Desde</span>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="input" /></label>
          <label style={{ display: "grid", gap: 4 }}><span style={{ fontSize: 12, color: "rgb(var(--muted))" }}>Hasta</span>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="input" /></label>
          <label style={{ display: "grid", gap: 4 }}><span style={{ fontSize: 12, color: "rgb(var(--muted))" }}>Mesa</span>
            <input value={tableCode} onChange={(e) => setTableCode(e.target.value)} placeholder="Ej. A1" className="input" style={{ width: 100 }} /></label>
          <button onClick={() => void search()} className="btn-primary">Filtrar</button>
        </div>

        {error && <div className="rounded-xl border border-rose-400/40 bg-rose-500/10 px-3 py-3 text-sm text-rose-600">{error}</div>}

        <div className="card p-4">
          <p style={{ marginTop: 0, color: "rgb(var(--muted))", fontSize: 13 }}>{loading ? "Cargando…" : `${total} sesiones`}</p>
          <div style={{ display: "grid", gap: 8 }}>
            {items.map((s) => (
              <div key={s.sessionId} style={{ border: "1px solid rgb(var(--hair) / 0.5)", borderRadius: 10, padding: 10 }}>
                <div style={{ display: "flex", justifyContent: "space-between", gap: 10, alignItems: "center" }}>
                  <span>
                    <span className="font-display">Mesa {s.tableCode}</span> · {s.participantCount} pers. · cerrada {s.closedAt ? new Date(s.closedAt).toLocaleString("es-ES") : "—"}
                  </span>
                  <button onClick={() => void toggleDetail(s.sessionId)} className="btn-secondary">
                    {openId === s.sessionId ? "Ocultar" : "Ver detalle"}
                  </button>
                </div>
                {openId === s.sessionId && (
                  <div style={{ marginTop: 8, borderTop: "1px solid rgb(var(--hair) / 0.5)", paddingTop: 8 }}>
                    {!detail ? (
                      <span style={{ color: "rgb(var(--muted))", fontSize: 13 }}>Cargando…</span>
                    ) : (
                      <div style={{ display: "grid", gap: 4, fontSize: 13 }}>
                        {detail.orders.filter((o) => o.status !== "CANCELLED").map((o) => (
                          <div key={o.id} style={{ display: "flex", justifyContent: "space-between", color: "rgb(var(--muted))" }}>
                            <span>#{o.id.slice(-6).toUpperCase()} · {o.itemCount} ítems</span>
                            <span>{formatEur(o.totalCents)}</span>
                          </div>
                        ))}
                        <div style={{ display: "flex", justifyContent: "space-between", fontWeight: 700, marginTop: 4 }}>
                          <span>Total pagado</span><span>{formatEur(detail.payment.totalPaidCents)}</span>
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </div>
            ))}
            {!loading && items.length === 0 && <p style={{ color: "rgb(var(--muted))" }}>Sin sesiones cerradas para el filtro.</p>}
          </div>
        </div>
      </div>
    </main>
  );
}
