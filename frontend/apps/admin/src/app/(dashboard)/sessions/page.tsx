"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { ParticipantDto, ClosedSessionDto, WaiterCallDto } from "@mestobeach/api-client";
import { sessionsApi, formatEur, todayStr } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { useDialog } from "@/components/Dialog";
import { Bell, Clock, IdentificationBadge, Key, Users } from "@phosphor-icons/react";

interface LiveSession {
  sessionId: string;
  tableCode: string;
  zone: string | null;
  createdAt: string;
  participants: ParticipantDto[];
  waiterName?: string;
  totalCents: number;
  totalItems: number;
  pendingItems: number;
  pin?: string | null;
  calls: NonNullable<WaiterCallDto["reason"]>[];
}

function durationLabel(iso: string): string {
  const mins = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60000));
  if (mins < 60) return `${mins} min`;
  const h = Math.floor(mins / 60);
  return `${h}h ${mins % 60}m`;
}

function timeLabel(iso: string): string {
  return new Date(iso).toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });
}

function participantNames(ps: ParticipantDto[]): string {
  const named = ps.map((p) => p.displayName?.trim()).filter(Boolean) as string[];
  if (named.length === 0) return `${ps.length} ${ps.length === 1 ? "persona" : "personas"}`;
  return named.join(", ");
}

export default function SessionsPage() {
  const { staff } = useAuthStore();
  const dialog = useDialog();
  const tenantId = staff?.tenantId ?? process.env["NEXT_PUBLIC_TENANT_ID"] ?? "";

  const [live, setLive] = useState<LiveSession[]>([]);
  const [closed, setClosed] = useState<ClosedSessionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const firstLoad = useRef(true);

  const load = useCallback(async () => {
    if (!tenantId) return;
    if (firstLoad.current) setLoading(true); else setRefreshing(true);
    try {
      const [{ data: tablesRes }, callsRes] = await Promise.all([
        sessionsApi.listTables(tenantId),
        sessionsApi.listWaiterCalls(tenantId).catch(() => ({ data: [] as WaiterCallDto[] })),
      ]);
      // Los motivos salen del DTO (WAITER / PIN / ETA); así no hay que tocar este tipo al añadir uno nuevo.
      const callsByTable = new Map<string, NonNullable<WaiterCallDto["reason"]>[]>();
      for (const c of (callsRes.data ?? [])) {
        const arr = callsByTable.get(c.tableCode) ?? [];
        arr.push(c.reason ?? "WAITER");
        callsByTable.set(c.tableCode, arr);
      }

      const active = tablesRes.tables.filter((t) => t.hasActiveSession && t.activeSessionId);
      const sessions = await Promise.all(
        active.map(async (t): Promise<LiveSession | null> => {
          try {
            const { data: detail } = await sessionsApi.getSession(t.activeSessionId!);
            let pin: string | null = null;
            try { pin = (await sessionsApi.getSessionPin(t.activeSessionId!)).data.pin; } catch { /* no PIN */ }
            return {
              sessionId: detail.sessionId,
              tableCode: detail.tableCode,
              zone: t.zone ?? null,
              createdAt: detail.createdAt,
              participants: detail.participants,
              ...(detail.waiterName ? { waiterName: detail.waiterName } : {}),
              totalCents: detail.ordersSummary.totalCents,
              totalItems: detail.ordersSummary.totalItems,
              pendingItems: detail.ordersSummary.pendingItems,
              pin,
              calls: callsByTable.get(detail.tableCode) ?? [],
            };
          } catch {
            return null;
          }
        })
      );
      setLive(sessions.filter((s): s is LiveSession => s !== null)
        .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()));
      setError(null);

      const today = todayStr();
      try {
        const { data: cl } = await sessionsApi.listClosedSessions({ from: today, to: today, pageSize: 50 });
        setClosed(cl.items);
      } catch { /* ignore */ }
    } catch {
      setError("No se pudieron cargar las sesiones.");
    } finally {
      firstLoad.current = false;
      setLoading(false);
      setRefreshing(false);
    }
  }, [tenantId]);

  useEffect(() => {
    void load();
    const id = setInterval(() => void load(), 15000); // live refresh
    return () => clearInterval(id);
  }, [load]);

  async function handleRegenPin(s: LiveSession) {
    setBusyId(s.sessionId);
    try {
      const { data } = await sessionsApi.regeneratePin(s.sessionId);
      setLive((prev) => prev.map((x) => x.sessionId === s.sessionId ? { ...x, pin: data.newPin } : x));
    } catch {
      dialog.toast("No se pudo regenerar el PIN.", { tone: "error" });
    } finally {
      setBusyId(null);
    }
  }

  async function handleClose(s: LiveSession) {
    if (
      !(await dialog.confirm({
        message: `¿Cerrar la sesión de la mesa ${s.tableCode}?`,
        confirmText: "Cerrar",
      }))
    )
      return;
    setBusyId(s.sessionId);
    try {
      await sessionsApi.closeSession(s.sessionId, "MANUAL_CLOSE");
      await load();
    } catch (err: unknown) {
      dialog.toast(
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? "No se pudo cerrar la sesión.",
        { tone: "error" }
      );
    } finally {
      setBusyId(null);
    }
  }

  const totalPeople = live.reduce((s, x) => s + x.participants.length, 0);
  const totalInPlay = live.reduce((s, x) => s + x.totalCents, 0);
  const attentionCount = live.filter((x) => x.calls.length > 0).length;

  return (
    <div className="p-6">
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl text-ink">Sala en vivo</h1>
          <p className="text-sm text-muted">Sesiones activas por mesa, con su PIN y consumo en tiempo real.</p>
        </div>
        <div className="flex items-center gap-2">
          <span className="inline-flex items-center gap-1.5 rounded-full bg-oro/[0.10] px-2.5 py-1 text-xs font-medium tracking-[0.03em] text-brand-700">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-brand-500" /> En vivo
          </span>
          <button onClick={() => void load()} disabled={refreshing} className="btn-secondary py-1.5 text-sm">
            {refreshing ? "Actualizando…" : "↻ Actualizar"}
          </button>
        </div>
      </div>

      {/* Summary */}
      <div className="mb-5 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <div className="card p-4">
          <p className="text-xs text-muted">Mesas activas</p>
          <p className="text-2xl font-semibold text-ink">{live.length}</p>
        </div>
        <div className="card p-4">
          <p className="text-xs text-muted">Personas</p>
          <p className="text-2xl font-semibold text-ink">{totalPeople}</p>
        </div>
        <div className="card p-4">
          <p className="text-xs text-muted">En curso</p>
          <p className="text-2xl font-semibold text-brand-700">{formatEur(totalInPlay)}</p>
        </div>
        <div className="card p-4">
          <p className="text-xs text-muted">Requieren atención</p>
          <p className={`text-2xl font-semibold ${attentionCount > 0 ? "text-amber-600" : "text-ink"}`}>{attentionCount}</p>
        </div>
      </div>

      {error && <div className="mb-4 rounded-lg border border-red-200 bg-red-500/10 px-4 py-2.5 text-sm text-red-700">{error}</div>}

      {/* Active sessions */}
      {loading ? (
        <div className="flex justify-center py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : live.length === 0 ? (
        <div className="card p-10 text-center">
          <Users size={36} weight="light" className="mx-auto mb-2 text-brand-500/70" />
          <p className="text-sm text-muted">No hay mesas con sesión activa en este momento.</p>
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {live.map((s) => {
            const busy = busyId === s.sessionId;
            return (
              <div key={s.sessionId} className={`card overflow-hidden ${s.calls.length > 0 ? "ring-2 ring-amber-300" : ""}`}>
                {/* Header */}
                <div className="flex items-start justify-between gap-2 border-b border-hair/50 p-4">
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="font-display text-xl text-ink">Mesa {s.tableCode}</span>
                      <span className="rounded-full bg-oro/[0.10] px-2 py-0.5 text-[10px] font-medium uppercase tracking-[0.03em] text-brand-700">Ocupada</span>
                    </div>
                    <p className="text-xs text-muted">{s.zone ?? "Sin zona"} · abierta hace {durationLabel(s.createdAt)} ({timeLabel(s.createdAt)})</p>
                  </div>
                  {s.calls.length > 0 && (
                    <div className="flex flex-col items-end gap-1">
                      {s.calls.includes("WAITER") && <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-[11px] font-medium tracking-[0.03em] text-amber-700"><Bell size={12} weight="light" />Llamó</span>}
                      {s.calls.includes("PIN") && <span className="inline-flex items-center gap-1 rounded-full bg-yellow-500/10 px-2 py-0.5 text-[11px] font-medium tracking-[0.03em] text-yellow-700"><Key size={12} weight="light" />Pide PIN</span>}
                      {/* "¿cuándo sale?": informativo, en verde apagado para no leerse como incidencia. */}
                      {s.calls.includes("ETA") && <span className="inline-flex items-center gap-1 rounded-full bg-emerald-600/10 px-2 py-0.5 text-[11px] font-medium tracking-[0.03em] text-emerald-800"><Clock size={12} weight="light" />Pregunta por su plato</span>}
                    </div>
                  )}
                </div>

                {/* PIN */}
                <div className="flex items-center justify-between gap-3 bg-paper2 px-4 py-3">
                  <div>
                    <p className="text-[10px] font-semibold uppercase tracking-wide text-muted">PIN de la mesa</p>
                    {s.pin ? (
                      <p className="font-mono text-2xl font-semibold tracking-[0.25em] text-ink">{s.pin}</p>
                    ) : (
                      <p className="text-sm text-muted">Sin PIN</p>
                    )}
                  </div>
                  <button onClick={() => void handleRegenPin(s)} disabled={busy} className="btn-secondary py-1.5 text-xs">
                    {busy ? "…" : "Regenerar"}
                  </button>
                </div>

                {/* Body */}
                <div className="space-y-2 p-4">
                  <div className="flex items-start gap-2 text-sm">
                    <Users size={16} weight="light" className="mt-0.5 flex-shrink-0 text-muted" />
                    <span className="text-ink">{participantNames(s.participants)}</span>
                  </div>
                  {s.waiterName && (
                    <div className="flex items-center gap-2 text-sm">
                      <IdentificationBadge size={16} weight="light" className="flex-shrink-0 text-muted" />
                      <span className="text-ink">Camarero: <strong>{s.waiterName}</strong></span>
                    </div>
                  )}
                  <div className="flex items-center justify-between rounded-xl bg-paper2 px-3 py-2">
                    <span className="text-xs text-muted">Consumo en curso</span>
                    <span className="font-semibold text-brand-700">{formatEur(s.totalCents)}</span>
                  </div>
                  <div className="flex gap-3 text-xs text-muted">
                    <span>{s.totalItems} ítem(s)</span>
                    {s.pendingItems > 0 && <span className="text-amber-600">· {s.pendingItems} sin entregar</span>}
                  </div>
                </div>

                {/* Actions */}
                <div className="flex gap-2 border-t border-hair/50 p-3">
                  <button onClick={() => void handleClose(s)} disabled={busy} className="btn-secondary flex-1 text-sm">
                    Cerrar sesión
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Closed today */}
      {closed.length > 0 && (
        <div className="mt-8">
          <h2 className="mb-3 font-display text-lg text-ink">Cerradas hoy <span className="text-muted">· {closed.length}</span></h2>
          <div className="card overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-hair/50 bg-paper2">
                <tr>
                  <th className="table-th">Mesa</th>
                  <th className="table-th">Personas</th>
                  <th className="table-th">Abierta</th>
                  <th className="table-th">Cerrada</th>
                  <th className="table-th">Duración</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hair/40">
                {closed.map((c) => (
                  <tr key={c.sessionId}>
                    <td className="table-td font-display text-ink">{c.tableCode}</td>
                    <td className="table-td">{c.participantCount}</td>
                    <td className="table-td text-muted">{timeLabel(c.createdAt)}</td>
                    <td className="table-td text-muted">{c.closedAt ? timeLabel(c.closedAt) : "—"}</td>
                    <td className="table-td text-muted">
                      {c.closedAt ? `${Math.max(0, Math.floor((new Date(c.closedAt).getTime() - new Date(c.createdAt).getTime()) / 60000))} min` : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
