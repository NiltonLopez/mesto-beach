"use client";

import { useState } from "react";
import type {
  SalesSummaryDto,
  TopProductsDto,
  SessionAveragesDto,
  HourlyHeatmapDto,
  PrepTimeDto,
} from "@mestobeach/api-client";
import { analyticsApi, formatEur, todayStr, daysAgoStr } from "@/lib/api";

type Tab = "sales" | "products" | "sessions" | "hourly" | "prep";

const TABS: { key: Tab; label: string }[] = [
  { key: "sales", label: "Ventas" },
  { key: "products", label: "Productos" },
  { key: "sessions", label: "Sesiones" },
  { key: "hourly", label: "Horario" },
  { key: "prep", label: "Tiempos" },
];

export default function AnalyticsPage() {
  const [tab, setTab] = useState<Tab>("sales");
  const [from, setFrom] = useState(daysAgoStr(29));
  const [to, setTo] = useState(todayStr());
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [sales, setSales] = useState<SalesSummaryDto | null>(null);
  const [products, setProducts] = useState<TopProductsDto | null>(null);
  const [sessionAvgs, setSessionAvgs] = useState<SessionAveragesDto | null>(null);
  const [hourly, setHourly] = useState<HourlyHeatmapDto | null>(null);
  const [prep, setPrep] = useState<PrepTimeDto | null>(null);

  async function handleFetch() {
    setError(null);
    setLoading(true);
    try {
      switch (tab) {
        case "sales": {
          const { data } = await analyticsApi.getSalesSummary(from, to, "day");
          setSales(data);
          break;
        }
        case "products": {
          const { data } = await analyticsApi.getTopProducts(from, to, 20, "revenue");
          setProducts(data);
          break;
        }
        case "sessions": {
          const { data } = await analyticsApi.getSessionAverages(from, to);
          setSessionAvgs(data);
          break;
        }
        case "hourly": {
          const { data } = await analyticsApi.getHourlyHeatmap(from, to);
          setHourly(data);
          break;
        }
        case "prep": {
          const { data } = await analyticsApi.getPrepTime(from, to);
          setPrep(data);
          break;
        }
      }
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ??
        "Error al cargar los datos.";
      setError(detail);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="p-6">
      <h1 className="mb-6 font-display text-3xl text-ink">Analítica</h1>

      {/* Controls */}
      <div className="card p-4 mb-4 flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <label className="text-xs font-medium text-muted">Desde</label>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="input py-1.5 text-sm w-36" max={to} />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-xs font-medium text-muted">Hasta</label>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="input py-1.5 text-sm w-36" min={from} />
        </div>
        <button onClick={handleFetch} disabled={loading} className="btn-primary py-1.5 text-sm">
          {loading ? "Cargando…" : "Consultar"}
        </button>
      </div>

      {/* Tabs */}
      <div className="mb-4 flex gap-1 border-b border-hair/50">
        {TABS.map(({ key, label }) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={`px-4 py-2 text-sm font-medium border-b-2 transition ${
              tab === key
                ? "border-brand-500 text-brand-700"
                : "border-transparent text-muted hover:text-ink"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {error && <div className="rounded-xl bg-red-500/10 px-4 py-3 text-sm text-red-700 mb-4">{error}</div>}

      {/* Sales */}
      {tab === "sales" && sales && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 text-center">
            {[
              { label: "Ingresos", v: formatEur(sales.totalRevenueCents) },
              { label: "Órdenes", v: sales.totalOrderCount.toLocaleString("es-CO") },
              { label: "Sesiones", v: sales.totalSessionCount.toLocaleString("es-CO") },
              { label: "Ticket prom.", v: formatEur(sales.averageTicketCents) },
            ].map(({ label, v }) => (
              <div key={label} className="card p-4">
                <p className="text-xs text-muted">{label}</p>
                <p className="text-lg font-semibold text-brand-700">{v}</p>
              </div>
            ))}
          </div>
          <div className="card overflow-x-auto">
            <table className="w-full min-w-[32rem]">
              <thead className="bg-paper2 border-b border-hair/50">
                <tr>
                  <th className="table-th">Período</th>
                  <th className="table-th text-right">Ingresos</th>
                  <th className="table-th text-right">Sesiones</th>
                  <th className="table-th text-right">Órdenes</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hair/40">
                {sales.series.map((p) => (
                  <tr key={p.period} className="hover:bg-paper2">
                    <td className="table-td font-mono text-xs">{p.period}</td>
                    <td className="table-td text-right font-semibold text-brand-700">{formatEur(p.revenueCents)}</td>
                    <td className="table-td text-right">{p.sessionCount}</td>
                    <td className="table-td text-right">{p.orderCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Top Products */}
      {tab === "products" && products && (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[32rem]">
            <thead className="bg-paper2 border-b border-hair/50">
              <tr>
                <th className="table-th">#</th>
                <th className="table-th">Producto</th>
                <th className="table-th text-right">Ingresos</th>
                <th className="table-th text-right">Cantidad</th>
                <th className="table-th text-right">Órdenes</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-hair/40">
              {products.products.map((p, i) => (
                <tr key={p.catalogItemId} className="hover:bg-paper2">
                  <td className="table-td text-muted font-semibold">{i + 1}</td>
                  <td className="table-td font-display text-ink">{p.itemNameSnapshot}</td>
                  <td className="table-td text-right font-semibold text-brand-700">{formatEur(p.totalRevenueCents)}</td>
                  <td className="table-td text-right">{p.totalQuantity}</td>
                  <td className="table-td text-right">{p.orderCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Session averages */}
      {tab === "sessions" && sessionAvgs && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {[
            { label: "Total sesiones", v: sessionAvgs.totalSessions },
            { label: "Sesiones pagadas", v: sessionAvgs.totalPaidSessions },
            { label: "Total órdenes", v: sessionAvgs.totalOrders },
            { label: "Ticket prom.", v: formatEur(sessionAvgs.averageTicketCents) },
            { label: "Duración media (min)", v: sessionAvgs.averageSessionDurationMin },
            { label: "Órd./sesión", v: sessionAvgs.averageOrdersPerSession.toFixed(2) },
            { label: "Ítems/orden", v: sessionAvgs.averageItemsPerOrder.toFixed(2) },
          ].map(({ label, v }) => (
            <div key={label} className="card p-4">
              <p className="text-xs text-muted">{label}</p>
              <p className="text-xl font-semibold text-brand-700">{v}</p>
            </div>
          ))}
        </div>
      )}

      {/* Hourly heatmap */}
      {tab === "hourly" && hourly && (
        <div className="card p-5">
          <h2 className="mb-4 font-display text-lg text-ink">Órdenes por hora — {hourly.timezone}</h2>
          <div className="space-y-2">
            {hourly.hours.map((h) => {
              const maxOrders = Math.max(...hourly.hours.map((x) => x.orderCount), 1);
              const pct = Math.max(2, Math.round((h.orderCount / maxOrders) * 100));
              return (
                <div key={h.hour} className="flex items-center gap-3">
                  <span className="w-12 flex-shrink-0 text-xs font-mono text-muted">
                    {String(h.hour).padStart(2, "0")}:00
                  </span>
                  <div className="flex-1 h-5 rounded-full bg-oro/[0.10] overflow-hidden">
                    <div
                      className="h-full rounded-full bg-brand-500 transition-all"
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                  <span className="w-20 text-right text-xs text-muted">
                    {h.orderCount} órd. · {formatEur(h.revenueCents)}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Prep time */}
      {tab === "prep" && prep && (
        <div className="card overflow-x-auto">
          <table className="w-full min-w-[32rem]">
            <thead className="bg-paper2 border-b border-hair/50">
              <tr>
                <th className="table-th">Estación</th>
                <th className="table-th text-right">Prom. (min)</th>
                <th className="table-th text-right">P90 (min)</th>
                <th className="table-th text-right">Muestras</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-hair/40">
              {prep.stations.map((s, i) => (
                <tr key={s.stationId ?? i} className="hover:bg-paper2">
                  <td className="table-td font-display text-ink">{s.stationName}</td>
                  <td className="table-td text-right">{s.avgPrepTimeMin}</td>
                  <td className="table-td text-right">{s.p90PrepTimeMin}</td>
                  <td className="table-td text-right text-muted">{s.sampleCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {!loading && tab === "sales" && !sales && (
        <p className="text-center text-sm text-muted mt-8">Selecciona un rango y presiona Consultar.</p>
      )}
    </div>
  );
}
