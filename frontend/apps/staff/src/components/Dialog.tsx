"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";

// ─── Diálogos propios (estética Mesto Beach, sin alert/confirm nativos) ─────────────
// Un solo proveedor da: toasts apilables (aviso no bloqueante) y un modal centrado
// que sirve para confirmaciones sí/no y para pedir texto. La API es idéntica en
// todas las apps del monorepo — respétala si la copias.

type ToastTone = "success" | "error" | "info";

interface ToastItem {
  id: number;
  message: string;
  tone: ToastTone;
  duration: number;
}

interface ConfirmRequest {
  kind: "confirm";
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  tone?: "default" | "danger";
  resolve: (value: boolean) => void;
}

interface PromptRequest {
  kind: "prompt";
  title?: string;
  message?: string;
  placeholder?: string;
  confirmText?: string;
  defaultValue?: string;
  resolve: (value: string | null) => void;
}

type PendingRequest = ConfirmRequest | PromptRequest;

export interface DialogApi {
  toast: (message: string, opts?: { tone?: ToastTone; duration?: number }) => void;
  confirm: (o: {
    title?: string;
    message: string;
    confirmText?: string;
    cancelText?: string;
    tone?: "default" | "danger";
  }) => Promise<boolean>;
  prompt: (o: {
    title?: string;
    message?: string;
    placeholder?: string;
    confirmText?: string;
    defaultValue?: string;
  }) => Promise<string | null>;
}

const DialogContext = createContext<DialogApi | null>(null);

export function useDialog(): DialogApi {
  const ctx = useContext(DialogContext);
  if (!ctx) {
    throw new Error("useDialog debe usarse dentro de <DialogProvider>.");
  }
  return ctx;
}

export function DialogProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [pending, setPending] = useState<PendingRequest | null>(null);
  const nextId = useRef(1);

  const dismissToast = useCallback((id: number) => {
    setToasts((list) => list.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback<DialogApi["toast"]>((message, opts) => {
    const id = nextId.current++;
    const item: ToastItem = {
      id,
      message,
      tone: opts?.tone ?? "info",
      duration: opts?.duration ?? 3500,
    };
    setToasts((list) => [...list, item]);
  }, []);

  const confirm = useCallback<DialogApi["confirm"]>((o) => {
    return new Promise<boolean>((resolve) => {
      setPending({ kind: "confirm", ...o, resolve });
    });
  }, []);

  const prompt = useCallback<DialogApi["prompt"]>((o) => {
    return new Promise<string | null>((resolve) => {
      setPending({ kind: "prompt", ...o, resolve });
    });
  }, []);

  const api = useMemo<DialogApi>(() => ({ toast, confirm, prompt }), [toast, confirm, prompt]);

  // Cierra la petición pendiente resolviendo la promesa y limpiando el estado.
  const settle = useCallback((value: boolean | string | null) => {
    setPending((req) => {
      if (req) {
        if (req.kind === "confirm") req.resolve(value === true);
        else req.resolve(typeof value === "string" ? value : null);
      }
      return null;
    });
  }, []);

  return (
    <DialogContext.Provider value={api}>
      {children}
      <ToastStack toasts={toasts} onClose={dismissToast} />
      {pending && <PendingModal request={pending} onSettle={settle} />}
      <DialogAnimations />
    </DialogContext.Provider>
  );
}

// ─── Toasts ──────────────────────────────────────────────────────────────────
function ToastStack({
  toasts,
  onClose,
}: {
  toasts: ToastItem[];
  onClose: (id: number) => void;
}) {
  if (toasts.length === 0) return null;
  return (
    <div className="pointer-events-none fixed right-3 top-3 z-[70] flex w-[min(22rem,calc(100vw-1.5rem))] flex-col gap-2">
      {toasts.map((t) => (
        <Toast key={t.id} item={t} onClose={onClose} />
      ))}
    </div>
  );
}

const TOAST_ACCENT: Record<ToastTone, string> = {
  success: "bg-emerald-500",
  error: "bg-rose-500",
  info: "bg-oro",
};

function Toast({ item, onClose }: { item: ToastItem; onClose: (id: number) => void }) {
  useEffect(() => {
    if (item.duration <= 0) return;
    const timer = setTimeout(() => onClose(item.id), item.duration);
    return () => clearTimeout(timer);
  }, [item.id, item.duration, onClose]);

  return (
    <div
      role="status"
      aria-live="polite"
      className="mestobeach-toast-in pointer-events-auto flex items-stretch gap-0 overflow-hidden rounded-xl bg-paper shadow-warm-lg ring-1 ring-hair/40"
    >
      <span className={`w-1 shrink-0 ${TOAST_ACCENT[item.tone]}`} aria-hidden="true" />
      <div className="flex min-w-0 flex-1 items-start gap-3 px-3 py-2.5">
        <p className="min-w-0 flex-1 text-sm text-ink">{item.message}</p>
        <button
          type="button"
          onClick={() => onClose(item.id)}
          aria-label="Cerrar aviso"
          className="-mr-1 -mt-0.5 shrink-0 rounded-md px-1 text-lg leading-none text-muted transition hover:bg-paper2 hover:text-ink"
        >
          ×
        </button>
      </div>
    </div>
  );
}

// ─── Modal (confirm / prompt) ────────────────────────────────────────────────
function PendingModal({
  request,
  onSettle,
}: {
  request: PendingRequest;
  onSettle: (value: boolean | string | null) => void;
}) {
  const confirmBtnRef = useRef<HTMLButtonElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState(request.kind === "prompt" ? request.defaultValue ?? "" : "");

  const cancelValue = request.kind === "confirm" ? false : null;

  // Foco inicial: el input (prompt) o el botón de confirmar.
  useEffect(() => {
    if (request.kind === "prompt") inputRef.current?.focus();
    else confirmBtnRef.current?.focus();
  }, [request.kind]);

  // Esc cierra devolviendo la respuesta de cancelación.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        onSettle(cancelValue);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onSettle, cancelValue]);

  const isDanger = request.kind === "confirm" && request.tone === "danger";
  const confirmText = request.confirmText ?? "Confirmar";
  const cancelText = request.kind === "confirm" ? request.cancelText ?? "Cancelar" : "Cancelar";

  function accept() {
    onSettle(request.kind === "prompt" ? value : true);
  }

  const titleId = "mestobeach-dialog-title";
  const bodyId = "mestobeach-dialog-body";

  return (
    <div
      className="mestobeach-backdrop-in fixed inset-0 z-[60] flex items-center justify-center bg-ink/40 p-4"
      onMouseDown={(e) => {
        // Sólo el click directo sobre el backdrop cancela (no el que empieza dentro de la card).
        if (e.target === e.currentTarget) onSettle(cancelValue);
      }}
    >
      <div
        role={request.kind === "prompt" ? "dialog" : "alertdialog"}
        aria-modal="true"
        aria-labelledby={request.title ? titleId : undefined}
        aria-describedby={bodyId}
        className="mestobeach-card-in w-full max-w-sm rounded-2xl border border-hair/40 bg-paper p-5 shadow-warm-lg"
      >
        {request.title && (
          <h2 id={titleId} className="font-display text-lg text-ink">
            {request.title}
          </h2>
        )}

        {request.kind === "confirm" ? (
          <p id={bodyId} className={`text-sm text-muted ${request.title ? "mt-1.5" : ""}`}>
            {request.message}
          </p>
        ) : (
          <div id={bodyId} className={request.title ? "mt-1.5" : ""}>
            {request.message && <p className="text-sm text-muted">{request.message}</p>}
            <input
              ref={inputRef}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  accept();
                }
              }}
              placeholder={request.placeholder ?? ""}
              className="mt-3 w-full rounded-xl border border-hair/55 bg-paper px-4 py-3 text-sm text-ink outline-none placeholder:text-muted/60 focus:border-oro focus:ring-2 focus:ring-oro/20"
            />
          </div>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={() => onSettle(cancelValue)}
            className="inline-flex min-h-[44px] items-center justify-center rounded-xl border border-hair/55 px-4 py-2.5 text-sm font-semibold text-muted transition hover:bg-paper2"
          >
            {cancelText}
          </button>
          <button
            ref={confirmBtnRef}
            type="button"
            onClick={accept}
            className={
              isDanger
                ? "inline-flex min-h-[44px] items-center justify-center rounded-xl bg-rose-600 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-rose-700"
                : "inline-flex min-h-[44px] items-center justify-center rounded-xl bg-brand-500 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-600"
            }
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
}

// Animaciones de entrada, anuladas si el usuario pide menos movimiento.
function DialogAnimations() {
  return (
    <style>{`
      @keyframes mestobeachToastIn {
        from { opacity: 0; transform: translateY(-8px); }
        to { opacity: 1; transform: translateY(0); }
      }
      @keyframes mestobeachBackdropIn { from { opacity: 0; } to { opacity: 1; } }
      @keyframes mestobeachCardIn {
        from { opacity: 0; transform: translateY(8px) scale(0.98); }
        to { opacity: 1; transform: translateY(0) scale(1); }
      }
      .mestobeach-toast-in { animation: mestobeachToastIn 160ms ease-out; }
      .mestobeach-backdrop-in { animation: mestobeachBackdropIn 140ms ease-out; }
      .mestobeach-card-in { animation: mestobeachCardIn 160ms ease-out; }
      @media (prefers-reduced-motion: reduce) {
        .mestobeach-toast-in, .mestobeach-backdrop-in, .mestobeach-card-in { animation: none; }
      }
    `}</style>
  );
}
