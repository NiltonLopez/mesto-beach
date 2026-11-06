"use client";

import { useCallback, useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import type { WaiterCallDto } from "@mestobeach/api-client";
import { getToken, sessionsApi, SESSIONS_HUB_URL } from "@/lib/api";
import { pitido } from "@/lib/avisos";

/**
 * Avisos de mesa en vivo (llamar al camarero, PIN, cuenta, "¿cuándo sale?") para pantallas que
 * no son la PDA: conecta al SessionsHub con ?access_token e invoca JoinWaiterGroup. El sondeo
 * de 6 s garantiza la verdad aunque el socket falle. `enabled` apaga todo (p. ej. fuera de NIGHT).
 */
export function useAvisos(tenantId: string | null | undefined, enabled: boolean) {
  const [avisos, setAvisos] = useState<WaiterCallDto[]>([]);
  const [atendiendo, setAtendiendo] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled || !tenantId) {
      setAvisos([]);
      return;
    }
    const cargar = () =>
      sessionsApi
        .listWaiterCalls(tenantId)
        .then(({ data }) => setAvisos(data))
        .catch(() => {});
    void cargar();
    const timer = setInterval(() => void cargar(), 6_000);

    const token = getToken();
    let hub: signalR.HubConnection | null = null;
    let arranque: Promise<unknown> = Promise.resolve();
    if (token) {
      hub = new signalR.HubConnectionBuilder()
        .withUrl(`${SESSIONS_HUB_URL}?access_token=${token}`, {
          transport: signalR.HttpTransportType.WebSockets,
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();
      const conn = hub;
      conn.on("SessionEvent", (payload: unknown) => {
        const e = payload as { type?: string; sessionId?: string };
        if (!e.type) return;
        if (
          e.type === "WAITER_CALLED" || e.type === "PIN_REQUESTED" ||
          e.type === "BILL_REQUESTED" || e.type === "ETA_REQUESTED"
        ) {
          void cargar();
          pitido(e.type === "ETA_REQUESTED" ? "suave" : "aviso");
        }
        if (e.type === "WAITER_CALL_ACKNOWLEDGED" && e.sessionId) {
          setAvisos((cur) => cur.filter((a) => a.sessionId !== e.sessionId));
        }
      });
      conn.onreconnected(() => {
        void conn.invoke("JoinWaiterGroup", tenantId).catch(() => {});
        void cargar();
      });
      // stop() solo tras resolver start(): evita abortar la negociación con el doble montaje de StrictMode.
      arranque = conn
        .start()
        .then(() => conn.invoke("JoinWaiterGroup", tenantId))
        .catch(() => {});
    }
    return () => {
      clearInterval(timer);
      const c = hub;
      if (c) void arranque.then(() => c.stop()).catch(() => {});
    };
  }, [tenantId, enabled]);

  const atender = useCallback(async (aviso: WaiterCallDto) => {
    setAtendiendo(aviso.sessionId);
    setAvisos((cur) => cur.filter((a) => a.sessionId !== aviso.sessionId));
    try {
      await sessionsApi.acknowledgeWaiterCall(aviso.sessionId);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      // 404 = ya lo atendió otra pantalla: no es un fallo.
      if (status !== 404) {
        setAvisos((cur) => (cur.some((a) => a.sessionId === aviso.sessionId) ? cur : [...cur, aviso]));
      }
    } finally {
      setAtendiendo(null);
    }
  }, []);

  const verPin = useCallback(async (sessionId: string): Promise<string | null> => {
    try {
      const { data } = await sessionsApi.getSessionPin(sessionId);
      return data.pin ?? null;
    } catch {
      return null;
    }
  }, []);

  return { avisos, atendiendo, atender, verPin };
}
