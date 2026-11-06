"use client";

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

/* ──────────────────────────────────────────────────────────────────────────
   Diálogos propios del menú (toast / confirm / prompt) con la estética del
   tenant (--m-*). Reemplazan a alert()/prompt() nativos. La API es idéntica en
   las tres apps (menu/staff/admin) para poder calcar los call sites.
   ────────────────────────────────────────────────────────────────────────── */

type ToastTone = "success" | "error" | "info";

interface ToastItem {
  id: number;
  message: string;
  tone: ToastTone;
  duration: number;
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

type ModalState =
  | { kind: "confirm"; opts: ConfirmOptions; resolve: (v: boolean) => void }
  | { kind: "prompt"; opts: PromptOptions; resolve: (v: string | null) => void };

interface DialogApi {
  toast: (message: string, opts?: { tone?: ToastTone; duration?: number }) => void;
  confirm: (o: ConfirmOptions) => Promise<boolean>;
  prompt: (o: PromptOptions) => Promise<string | null>;
}

const DialogContext = createContext<DialogApi | null>(null);

export function useDialog(): DialogApi {
  const ctx = useContext(DialogContext);
  if (!ctx) throw new Error("useDialog debe usarse dentro de <DialogProvider>.");
  return ctx;
}

/* Estilos + keyframes locales. Respetan prefers-reduced-motion. */
const DIALOG_CSS = `
@keyframes m-dlg-fade { from { opacity: 0; } to { opacity: 1; } }
@keyframes m-dlg-pop {
  from { opacity: 0; transform: translateY(8px) scale(0.98); }
  to { opacity: 1; transform: translateY(0) scale(1); }
}
@keyframes m-dlg-toast {
  from { opacity: 0; transform: translateY(12px); }
  to { opacity: 1; transform: translateY(0); }
}
.m-dlg-anim-fade { animation: m-dlg-fade 160ms ease-out; }
.m-dlg-anim-pop { animation: m-dlg-pop 200ms cubic-bezier(0.16, 1, 0.3, 1); }
.m-dlg-anim-toast { animation: m-dlg-toast 220ms cubic-bezier(0.16, 1, 0.3, 1); }
@media (prefers-reduced-motion: reduce) {
  .m-dlg-anim-fade, .m-dlg-anim-pop, .m-dlg-anim-toast { animation: none; }
}
`;

export function DialogProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [modal, setModal] = useState<ModalState | null>(null);
  const idRef = useRef(0);

  const removeToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback<DialogApi["toast"]>((message, opts) => {
    idRef.current += 1;
    const item: ToastItem = {
      id: idRef.current,
      message,
      tone: opts?.tone ?? "info",
      duration: opts?.duration ?? 3500,
    };
    setToasts((prev) => [...prev, item]);
  }, []);

  const confirm = useCallback<DialogApi["confirm"]>(
    (opts) => new Promise<boolean>((resolve) => setModal({ kind: "confirm", opts, resolve })),
    [],
  );

  const prompt = useCallback<DialogApi["prompt"]>(
    (opts) => new Promise<string | null>((resolve) => setModal({ kind: "prompt", opts, resolve })),
    [],
  );

  const api = useMemo<DialogApi>(() => ({ toast, confirm, prompt }), [toast, confirm, prompt]);

  return (
    <DialogContext.Provider value={api}>
      {children}

      <style>{DIALOG_CSS}</style>

      {/* Toasts: pila inferior centrada, mobile-first, respeta safe-area */}
      {toasts.length > 0 && (
        <div
          className="pointer-events-none fixed inset-x-0 bottom-0 z-[60] flex flex-col items-center gap-2 px-4"
          style={{ paddingBottom: "calc(1rem + env(safe-area-inset-bottom))" }}
          aria-live="polite"
          role="status"
        >
          {toasts.map((t) => (
            <ToastView key={t.id} item={t} onDismiss={removeToast} />
          ))}
        </div>
      )}

      {/* Modales confirm/prompt */}
      {modal && modal.kind === "confirm" && (
        <ConfirmModal
          opts={modal.opts}
          onResolve={(v) => {
            modal.resolve(v);
            setModal(null);
          }}
        />
      )}
      {modal && modal.kind === "prompt" && (
        <PromptModal
          opts={modal.opts}
          onResolve={(v) => {
            modal.resolve(v);
            setModal(null);
          }}
        />
      )}
    </DialogContext.Provider>
  );
}

/* ── Toast ────────────────────────────────────────────────────────────────── */

function toneAccent(tone: ToastTone): string {
  if (tone === "success") return "#16a34a";
  if (tone === "error") return "#dc2626";
  return "var(--m-accent)";
}

function ToastView({ item, onDismiss }: { item: ToastItem; onDismiss: (id: number) => void }) {
  useEffect(() => {
    const t = setTimeout(() => onDismiss(item.id), item.duration);
    return () => clearTimeout(t);
  }, [item.id, item.duration, onDismiss]);

  const accent = toneAccent(item.tone);

  return (
    <div
      className="m-dlg-anim-toast pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-2xl px-4 py-3 shadow-warm-lg"
      style={{
        background: "var(--m-surface)",
        color: "var(--m-text)",
        border: "1px solid var(--m-border)",
        borderLeft: `3px solid ${accent}`,
      }}
    >
      <span className="mt-1 h-2 w-2 flex-shrink-0 rounded-full" style={{ background: accent }} />
      <p className="flex-1 text-sm leading-snug">{item.message}</p>
      <button
        type="button"
        onClick={() => onDismiss(item.id)}
        aria-label="Cerrar aviso"
        className="-mr-1 flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-sm transition active:scale-90"
        style={{ color: "var(--m-muted)" }}
      >
        ✕
      </button>
    </div>
  );
}

/* ── Confirm ──────────────────────────────────────────────────────────────── */

function ConfirmModal({
  opts,
  onResolve,
}: {
  opts: ConfirmOptions;
  onResolve: (v: boolean) => void;
}) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  const danger = opts.tone === "danger";

  useEffect(() => {
    confirmRef.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onResolve(false);
      else if (e.key === "Enter") onResolve(true);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onResolve]);

  return (
    <div
      className="m-dlg-anim-fade fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4"
      onClick={() => onResolve(false)}
      role="dialog"
      aria-modal="true"
    >
      <div
        className="m-dlg-anim-pop relative w-full max-w-sm rounded-3xl p-6 shadow-warm-lg"
        style={{ background: "var(--m-surface)", color: "var(--m-text)" }}
        onClick={(e) => e.stopPropagation()}
      >
        {opts.title && (
          <h2 className="m-head mb-2 text-xl" style={{ color: "var(--m-text)" }}>
            {opts.title}
          </h2>
        )}
        <p className="text-sm leading-relaxed" style={{ color: "var(--m-muted)" }}>
          {opts.message}
        </p>

        <div className="mt-6 flex gap-3">
          <button
            type="button"
            onClick={() => onResolve(false)}
            className="inline-flex flex-1 items-center justify-center rounded-xl px-5 py-3 text-base font-semibold transition active:scale-[.98]"
            style={{ background: "var(--m-surface)", color: "var(--m-text)", border: "1px solid var(--m-border)" }}
          >
            {opts.cancelText ?? "Cancelar"}
          </button>
          <button
            ref={confirmRef}
            type="button"
            onClick={() => onResolve(true)}
            className="inline-flex flex-1 items-center justify-center rounded-xl px-5 py-3 text-base font-semibold transition active:scale-[.98]"
            style={
              danger
                ? { background: "#dc2626", color: "#ffffff" }
                : { background: "var(--m-accent)", color: "var(--m-accent-ink)" }
            }
          >
            {opts.confirmText ?? "Confirmar"}
          </button>
        </div>
      </div>
    </div>
  );
}

/* ── Prompt ───────────────────────────────────────────────────────────────── */

function PromptModal({
  opts,
  onResolve,
}: {
  opts: PromptOptions;
  onResolve: (v: string | null) => void;
}) {
  const [value, setValue] = useState(opts.defaultValue ?? "");
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    textareaRef.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onResolve(null);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onResolve]);

  return (
    <div
      className="m-dlg-anim-fade fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4"
      onClick={() => onResolve(null)}
      role="dialog"
      aria-modal="true"
    >
      <div
        className="m-dlg-anim-pop relative w-full max-w-sm rounded-3xl p-6 shadow-warm-lg"
        style={{ background: "var(--m-surface)", color: "var(--m-text)" }}
        onClick={(e) => e.stopPropagation()}
      >
        {opts.title && (
          <h2 className="m-head mb-2 text-xl" style={{ color: "var(--m-text)" }}>
            {opts.title}
          </h2>
        )}
        {opts.message && (
          <p className="mb-3 text-sm leading-relaxed" style={{ color: "var(--m-muted)" }}>
            {opts.message}
          </p>
        )}

        <textarea
          ref={textareaRef}
          className="input"
          rows={3}
          maxLength={500}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          {...(opts.placeholder ? { placeholder: opts.placeholder } : {})}
        />

        <div className="mt-6 flex gap-3">
          <button
            type="button"
            onClick={() => onResolve(null)}
            className="inline-flex flex-1 items-center justify-center rounded-xl px-5 py-3 text-base font-semibold transition active:scale-[.98]"
            style={{ background: "var(--m-surface)", color: "var(--m-text)", border: "1px solid var(--m-border)" }}
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => onResolve(value)}
            className="btn-primary flex-1 text-base"
          >
            {opts.confirmText ?? "Aceptar"}
          </button>
        </div>
      </div>
    </div>
  );
}
