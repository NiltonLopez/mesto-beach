"use client";

import { useEffect, useRef, useState } from "react";
import { sessionsApi, getOrCreateDeviceId, markPinValidated } from "@/lib/api";

interface Props {
  open: boolean;
  sessionId: string;
  tenantId: string;
  participantId: string;
  tableCode?: string;
  /** Called once the PIN is validated — the caller retries the pending order. */
  onValidated: () => void;
  onClose: () => void;
}

const PIN_LENGTH = 4;

export default function PinModal({
  open,
  sessionId,
  tenantId,
  participantId,
  tableCode,
  onValidated,
  onClose,
}: Props) {
  const [digits, setDigits] = useState<string[]>(Array(PIN_LENGTH).fill(""));
  const [validating, setValidating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requesting, setRequesting] = useState(false);
  const [requested, setRequested] = useState(false);
  const inputs = useRef<Array<HTMLInputElement | null>>([]);

  // Reset + focus first box whenever the modal opens.
  useEffect(() => {
    if (open) {
      setDigits(Array(PIN_LENGTH).fill(""));
      setError(null);
      setRequested(false);
      // Defer focus until the element is mounted/visible.
      setTimeout(() => inputs.current[0]?.focus(), 80);
    }
  }, [open]);

  if (!open) return null;

  const pin = digits.join("");

  function setDigit(index: number, value: string) {
    const clean = value.replace(/\D/g, "");
    setError(null);
    if (clean.length > 1) {
      // Paste / autofill of the whole code into one box.
      const next = clean.slice(0, PIN_LENGTH).split("");
      const filled = Array(PIN_LENGTH)
        .fill("")
        .map((_, i) => next[i] ?? "");
      setDigits(filled);
      const last = Math.min(next.length, PIN_LENGTH) - 1;
      inputs.current[last >= 0 ? last : 0]?.focus();
      return;
    }
    setDigits((prev) => {
      const copy = [...prev];
      copy[index] = clean;
      return copy;
    });
    if (clean && index < PIN_LENGTH - 1) inputs.current[index + 1]?.focus();
  }

  function onKeyDown(index: number, e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Backspace" && !digits[index] && index > 0) {
      inputs.current[index - 1]?.focus();
    }
    if (e.key === "Enter" && pin.length === PIN_LENGTH) void handleValidate();
  }

  async function handleValidate() {
    if (pin.length !== PIN_LENGTH || validating) return;
    setValidating(true);
    setError(null);
    try {
      const { data } = await sessionsApi.validatePin(sessionId, {
        tenantId,
        participantId,
        deviceId: getOrCreateDeviceId(),
        pin,
      });
      if (data.validated) {
        markPinValidated(sessionId);
        onValidated();
      } else {
        setError("PIN incorrecto. Verifícalo con el mesero.");
        setDigits(Array(PIN_LENGTH).fill(""));
        inputs.current[0]?.focus();
      }
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? "";
      setError(
        /lock|bloque|intent/i.test(detail)
          ? "Demasiados intentos. Pídele al mesero que regenere el PIN."
          : "PIN incorrecto. Verifícalo con el mesero."
      );
      setDigits(Array(PIN_LENGTH).fill(""));
      inputs.current[0]?.focus();
    } finally {
      setValidating(false);
    }
  }

  async function handleRequestPin() {
    if (requesting || requested) return;
    setRequesting(true);
    try {
      await sessionsApi.requestPin(sessionId);
      setRequested(true);
      setTimeout(() => setRequested(false), 30_000);
    } catch {
      setError("No se pudo avisar al mesero. Intenta de nuevo.");
    } finally {
      setRequesting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-ink/40 backdrop-blur-sm sm:items-center">
      <div
        className="w-full max-w-sm rounded-t-3xl p-6 shadow-warm-lg sm:rounded-3xl m-slide-up"
        style={{ background: "var(--m-surface)" }}
      >
        {/* Close */}
        <div className="flex justify-end">
          <button
            onClick={onClose}
            aria-label="Cerrar"
            className="flex h-8 w-8 items-center justify-center rounded-full transition active:scale-95"
            style={{ background: "color-mix(in srgb, var(--m-accent) 8%, transparent)", color: "var(--m-muted)" }}
          >
            ✕
          </button>
        </div>

        {/* Icon + title */}
        <div className="-mt-2 flex flex-col items-center text-center">
          <div
            className="flex h-16 w-16 items-center justify-center rounded-full text-2xl"
            style={{ background: "color-mix(in srgb, var(--m-accent) 10%, transparent)", color: "var(--m-accent)" }}
          >
            ◆
          </div>
          <h2 className="m-head mt-3 text-2xl" style={{ color: "var(--m-text)" }}>PIN de la mesa</h2>
          <p className="mt-1 text-sm" style={{ color: "var(--m-muted)" }}>
            Para hacer pedidos, <span className="font-semibold" style={{ color: "var(--m-text)" }}>pídele el PIN al camarero</span>.
            Es el mismo para toda la mesa{tableCode ? ` ${tableCode}` : ""}.
          </p>
        </div>

        {/* OTP inputs */}
        <div className="mt-5 flex justify-center gap-3">
          {digits.map((d, i) => (
            <input
              key={i}
              ref={(el) => {
                inputs.current[i] = el;
              }}
              value={d}
              onChange={(e) => setDigit(i, e.target.value)}
              onKeyDown={(e) => onKeyDown(i, e)}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={PIN_LENGTH}
              className="h-14 w-12 rounded-2xl border-2 text-center text-2xl font-semibold outline-none transition"
              style={{
                color: "var(--m-text)",
                borderColor: error
                  ? "rgb(244 63 94 / 0.55)"
                  : d
                    ? "var(--m-accent)"
                    : "var(--m-border)",
                background: error
                  ? "rgb(244 63 94 / 0.08)"
                  : d
                    ? "color-mix(in srgb, var(--m-accent) 10%, transparent)"
                    : "color-mix(in srgb, var(--m-accent) 4%, transparent)",
              }}
            />
          ))}
        </div>

        {/* Error */}
        {error && <p className="mt-3 text-center text-sm font-medium text-rose-600">{error}</p>}

        {/* Confirm */}
        <button
          onClick={handleValidate}
          disabled={pin.length !== PIN_LENGTH || validating}
          className="btn-primary mt-5 w-full justify-center text-base disabled:opacity-50"
        >
          {validating ? "Verificando…" : "Confirmar PIN"}
        </button>

        {/* Ask the waiter */}
        <button
          onClick={handleRequestPin}
          disabled={requesting || requested}
          className="mt-3 w-full rounded-xl border px-4 py-3 text-sm font-semibold transition active:scale-[.99] disabled:opacity-70"
          style={{
            borderColor: "var(--m-border)",
            color: "var(--m-accent)",
            background: requested
              ? "color-mix(in srgb, var(--m-accent) 16%, transparent)"
              : "color-mix(in srgb, var(--m-accent) 6%, transparent)",
          }}
        >
          {requested ? "Le avisamos al camarero" : requesting ? "Avisando…" : "Pedir el PIN al camarero"}
        </button>
      </div>
    </div>
  );
}
