"use client";

// Proveedor de diálogos con la estética Mesto Beach (crema/dorado). Reemplaza a
// alert()/confirm() nativos: toasts no bloqueantes para avisos y modales sí/no
// para confirmaciones. API idéntica en las 3 apps del repo (staff/menu/admin).

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

// ── Tipos públicos ──────────────────────────────────────────────────────────

type ToastTone = "success" | "error" | "info";

interface ToastOptions {
  tone?: ToastTone;
  duration?: number;
}

interface ConfirmOptions {
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  tone?: "default" | "danger";
}

interface PromptOptions {
  title?: string;
  message?: string;
  placeholder?: string;
  confirmText?: string;
  defaultValue?: string;
}

interface DialogApi {
  toast: (message: string, opts?: ToastOptions) => void;
  confirm: (o: ConfirmOptions) => Promise<boolean>;
  prompt: (o: PromptOptions) => Promise<string | null>;
}

// ── Estado interno ──────────────────────────────────────────────────────────

interface ToastItem {
  id: number;
  message: string;
  tone: ToastTone;
}

type Pending =
  | { kind: "confirm"; opts: ConfirmOptions; resolve: (value: boolean) => void }
  | { kind: "prompt"; opts: PromptOptions; resolve: (value: string | null) => void };

const DialogContext = createContext<DialogApi | null>(null);

export function useDialog(): DialogApi {
  const ctx = useContext(DialogContext);
  if (!ctx) throw new Error("useDialog debe usarse dentro de <DialogProvider>");
  return ctx;
}

// ── Proveedor ───────────────────────────────────────────────────────────────

export function DialogProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [pending, setPending] = useState<Pending | null>(null);
  const idRef = useRef(0);

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback<DialogApi["toast"]>(
    (message, opts) => {
      const id = ++idRef.current;
      const tone: ToastTone = opts?.tone ?? "info";
      const duration = opts?.duration ?? 3500;
      setToasts((prev) => [...prev, { id, message, tone }]);
      if (duration > 0) window.setTimeout(() => dismissToast(id), duration);
    },
    [dismissToast],
  );

  const confirm = useCallback<DialogApi["confirm"]>(
    (o) => new Promise<boolean>((resolve) => setPending({ kind: "confirm", opts: o, resolve })),
    [],
  );

  const prompt = useCallback<DialogApi["prompt"]>(
    (o) => new Promise<string | null>((resolve) => setPending({ kind: "prompt", opts: o, resolve })),
    [],
  );

  const resolvePending = useCallback((value: boolean | string | null) => {
    setPending((cur) => {
      if (cur) {
        if (cur.kind === "confirm") cur.resolve(value === true);
        else cur.resolve(typeof value === "string" ? value : null);
      }
      return null;
    });
  }, []);

  const api = useMemo<DialogApi>(() => ({ toast, confirm, prompt }), [toast, confirm, prompt]);

  return (
    <DialogContext.Provider value={api}>
      {children}

      {/* Keyframes propios (respetan prefers-reduced-motion) */}
      <style>{DIALOG_KEYFRAMES}</style>

      {/* Toasts: apilados, no bloqueantes */}
      {toasts.length > 0 && (
        <div
          className="fixed top-3 right-3 z-[70] flex w-[min(92vw,22rem)] flex-col gap-2"
          role="region"
          aria-live="polite"
          aria-label="Notificaciones"
        >
          {toasts.map((t) => (
            <ToastCard key={t.id} toast={t} onClose={() => dismissToast(t.id)} />
          ))}
        </div>
      )}

      {/* Modal de confirmación / prompt */}
      {pending && <DialogModal pending={pending} onResolve={resolvePending} />}
    </DialogContext.Provider>
  );
}

// ── Toast ───────────────────────────────────────────────────────────────────

function ToastCard({ toast, onClose }: { toast: ToastItem; onClose: () => void }) {
  const dot =
    toast.tone === "success"
      ? "bg-emerald-500"
      : toast.tone === "error"
        ? "bg-rose-500"
        : "bg-oro";

  return (
    <div
      className="mestobeach-toast-anim relative flex items-start gap-3 rounded-xl bg-paper px-4 py-3 shadow-warm-lg ring-1 ring-hair/40"
      role="status"
    >
      <span className={`mt-1.5 h-2 w-2 flex-shrink-0 rounded-full ${dot}`} aria-hidden />
      <p className="flex-1 text-sm leading-snug text-ink">{toast.message}</p>
      <button
        type="button"
        onClick={onClose}
        aria-label="Cerrar"
        className="-mr-1 -mt-0.5 flex-shrink-0 rounded-md p-1 text-muted transition hover:bg-paper2 hover:text-ink"
      >
        <XIcon />
      </button>
    </div>
  );
}

// ── Modal ───────────────────────────────────────────────────────────────────

function DialogModal({
  pending,
  onResolve,
}: {
  pending: Pending;
  onResolve: (value: boolean | string | null) => void;
}) {
  const isPrompt = pending.kind === "prompt";
  const danger = pending.kind === "confirm" && pending.opts.tone === "danger";
  const message: string | undefined = pending.opts.message;

  const [value, setValue] = useState(
    pending.kind === "prompt" ? (pending.opts.defaultValue ?? "") : "",
  );
  const confirmRef = useRef<HTMLButtonElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const cancel = useCallback(() => onResolve(isPrompt ? null : false), [isPrompt, onResolve]);
  const accept = useCallback(
    () => onResolve(pending.kind === "prompt" ? value : true),
    [onResolve, pending.kind, value],
  );

  useEffect(() => {
    if (isPrompt) inputRef.current?.focus();
    else confirmRef.current?.focus();
  }, [isPrompt]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.preventDefault();
        cancel();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [cancel]);

  const confirmText = pending.opts.confirmText ?? (isPrompt ? "Aceptar" : "Confirmar");
  const cancelText = pending.kind === "confirm" ? (pending.opts.cancelText ?? "Cancelar") : "Cancelar";

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-ink/40 p-4"
      role="presentation"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) cancel();
      }}
    >
      <form
        role="dialog"
        aria-modal="true"
        onSubmit={(e) => {
          e.preventDefault();
          accept();
        }}
        className="mestobeach-dialog-anim w-full max-w-sm rounded-2xl border border-hair/40 bg-paper p-5 shadow-warm-lg"
      >
        {pending.opts.title && (
          <h2 className="font-display text-lg text-ink">{pending.opts.title}</h2>
        )}
        {message && (
          <p className={`text-sm leading-snug text-muted ${pending.opts.title ? "mt-1.5" : ""}`}>
            {message}
          </p>
        )}

        {pending.kind === "prompt" && (
          <input
            ref={inputRef}
            className="input mt-3"
            value={value}
            placeholder={pending.opts.placeholder ?? ""}
            onChange={(e) => setValue(e.target.value)}
          />
        )}

        <div className="mt-5 flex justify-end gap-2.5">
          <button
            type="button"
            onClick={cancel}
            className="rounded-xl border border-hair/55 px-4 py-2.5 text-sm font-semibold text-muted transition hover:bg-paper2"
          >
            {cancelText}
          </button>
          <button
            ref={confirmRef}
            type="submit"
            className={
              danger
                ? "rounded-xl bg-rose-600 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-rose-700 active:bg-rose-800"
                : "rounded-xl bg-brand-500 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-600 active:bg-brand-700"
            }
          >
            {confirmText}
          </button>
        </div>
      </form>
    </div>
  );
}

// ── Auxiliares ──────────────────────────────────────────────────────────────

function XIcon() {
  return (
    <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none" aria-hidden>
      <path
        d="M6 6l12 12M18 6L6 18"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

const DIALOG_KEYFRAMES = `
@keyframes mestobeach-dialog-in { from { opacity: 0; transform: translateY(8px) scale(.98); } to { opacity: 1; transform: none; } }
@keyframes mestobeach-toast-in { from { opacity: 0; transform: translateX(14px); } to { opacity: 1; transform: none; } }
.mestobeach-dialog-anim { animation: mestobeach-dialog-in .16s ease-out; }
.mestobeach-toast-anim { animation: mestobeach-toast-in .18s ease-out; }
@media (prefers-reduced-motion: reduce) {
  .mestobeach-dialog-anim, .mestobeach-toast-anim { animation: none; }
}
`;
