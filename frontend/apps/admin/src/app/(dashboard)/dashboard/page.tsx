"use client";

import { useEffect, useState } from "react";
import type { SalesSummaryDto, SessionAveragesDto } from "@mestobeach/api-client";
import { analyticsApi, formatEur, todayStr, daysAgoStr } from "@/lib/api";
import StatCard from "@/components/StatCard";
import {
  ChartLineUp, CheckCircle, ForkKnife, Money, Receipt, Stack, Timer, Users,
} from "@phosphor-icons/react";

export default function DashboardPage() {
  const [sales, setSales] = useState<SalesSummaryDto | null>(null);
  const [avgs, setAvgs] = useState<SessionAveragesDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const from = daysAgoStr(29);
    const to = todayStr();
    Promise.all([
      analyticsApi.getSalesSummary(from, to, "day"),
      analyticsApi.getSessionAverages(from, to),
    ])
      .then(([s, a]) => {
        setSales(s.data);
        setAvgs(a.data);
      })
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="font-display text-3xl text-ink">Dashboard</h1>
        <p className="text-sm text-muted">Últimos 30 días</p>
      </div>

      {loading && (
        <div className="flex justify-center py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      )}

      {!loading && (
        <>
          <div className="grid grid-cols-2 gap-4 xl:grid-cols-4">
            <StatCard
              label="Ingresos totales"
              value={formatEur(sales?.totalRevenueCents ?? 0)}
              icon={<Money size={22} weight="light" />}
              accent="green"
            />
            <StatCard
              label="Órdenes"
              value={(sales?.totalOrderCount ?? 0).toLocaleString("es-CO")}
              icon={<Receipt size={22} weight="light" />}
              accent="blue"
            />
            <StatCard
              label="Sesiones"
              value={(sales?.totalSessionCount ?? 0).toLocaleString("es-CO")}
              icon={<Users size={22} weight="light" />}
              accent="orange"
            />
            <StatCard
              label="Ticket promedio"
              value={formatEur(sales?.averageTicketCents ?? 0)}
              icon={<ChartLineUp size={22} weight="light" />}
            />
          </div>

          {avgs && (
            <div className="mt-4 grid grid-cols-2 gap-4 xl:grid-cols-4">
              <StatCard
                label="Sesiones pagadas"
                value={avgs.totalPaidSessions.toLocaleString("es-CO")}
                icon={<CheckCircle size={22} weight="light" />}
                accent="green"
              />
              <StatCard
                label="Duración media"
                value={`${avgs.averageSessionDurationMin} min`}
                icon={<Timer size={22} weight="light" />}
              />
              <StatCard
                label="Órd. por sesión"
                value={avgs.averageOrdersPerSession.toFixed(1)}
                icon={<Stack size={22} weight="light" />}
              />
              <StatCard
                label="Ítems por orden"
                value={avgs.averageItemsPerOrder.toFixed(1)}
                icon={<ForkKnife size={22} weight="light" />}
              />
            </div>
          )}

          {/* Revenue series */}
          {sales && sales.series.length > 0 && (
            <div className="card mt-6 p-5">
              <h2 className="mb-4 font-display text-lg text-ink">Ingresos diarios</h2>
              <div className="space-y-2">
                {sales.series.slice(-14).map((point) => {
                  const pct =
                    sales.totalRevenueCents > 0
                      ? Math.max(2, Math.round((point.revenueCents / Math.max(...sales.series.map((s) => s.revenueCents))) * 100))
                      : 2;
                  return (
                    <div key={point.period} className="flex items-center gap-3">
                      <span className="w-20 flex-shrink-0 text-xs text-muted">{point.period.slice(5)}</span>
                      <div className="flex-1 rounded-full bg-oro/[0.10] h-5 overflow-hidden">
                        <div
                          className="h-full rounded-full bg-brand-500 transition-all"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                      <span className="w-28 text-right text-xs font-semibold text-brand-700">
                        {formatEur(point.revenueCents)}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
