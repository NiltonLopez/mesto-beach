"use client";

import { useState } from "react";
import { sessionsApi, identityApi, saveCustomerAuth, markIdentified } from "@/lib/api";

interface Props {
  sessionId: string;
  participantId: string;
  /** Called once the participant is identified (guest name or logged-in account). */
  onDone: (displayName: string) => void;
}

type Tab = "guest" | "login" | "register";

export default function IdentifyScreen({ sessionId, participantId, onDone }: Props) {
  const [tab, setTab] = useState<Tab>("guest");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // shared
  const [name, setName] = useState("");
  // login/register
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [phone, setPhone] = useState("");

  async function identify(displayName: string) {
    await sessionsApi.identifyParticipant(sessionId, participantId, { displayName });
    markIdentified(sessionId, displayName);
    onDone(displayName);
  }

  async function handleGuest() {
    if (!name.trim() || busy) return;
    setBusy(true);
    setError(null);
    try {
      await identify(name.trim());
    } catch {
      setError("No se pudo continuar. Intenta de nuevo.");
      setBusy(false);
    }
  }

  async function handleLogin() {
    if (!email.trim() || !password || busy) return;
    setBusy(true);
    setError(null);
    try {
      const { data } = await identityApi.loginCustomer(email.trim(), password);
      saveCustomerAuth(data.token, data.customer);
      await identify(data.customer.name);
    } catch (err: unknown) {
      setError(detail(err) ?? "Correo o contraseña incorrectos.");
      setBusy(false);
    }
  }

  async function handleRegister() {
    if (!name.trim() || !email.trim() || password.length < 8 || busy) return;
    setBusy(true);
    setError(null);
    try {
      const { data } = await identityApi.registerCustomer({
        name: name.trim(),
        email: email.trim(),
        password,
        ...(phone.trim() ? { phone: phone.trim() } : {}),
      });
      saveCustomerAuth(data.token, data.customer);
      await identify(data.customer.name);
    } catch (err: unknown) {
      setError(detail(err) ?? "No se pudo crear la cuenta.");
      setBusy(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex flex-col"
      style={{ background: "var(--m-bg)", color: "var(--m-text)" }}
    >
      <div className="flex flex-1 flex-col justify-center px-6 py-8">
        <div className="mx-auto w-full max-w-sm">
          {/* Brand */}
          <div className="mb-6 text-center">
            <div className="mx-auto mb-4 flex w-40 items-center gap-2.5" style={{ color: "var(--m-accent)" }}>
              <span className="h-px flex-1" style={{ background: "currentColor", opacity: 0.45 }} />
              <span className="text-sm leading-none">✦</span>
              <span className="h-px flex-1" style={{ background: "currentColor", opacity: 0.45 }} />
            </div>
            <h1 className="m-head text-4xl" style={{ color: "var(--m-text)" }}>Bienvenido</h1>
            <p className="mt-2 text-sm" style={{ color: "var(--m-muted)" }}>¿Cómo te llamamos para tu pedido?</p>
          </div>

          <div
            className="rounded-3xl border p-5 shadow-warm-lg"
            style={{ background: "var(--m-surface)", borderColor: "var(--m-border)" }}
          >
            {/* Tabs */}
            <div
              className="mb-4 flex rounded-xl p-1 text-sm font-medium tracking-[0.03em]"
              style={{ background: "color-mix(in srgb, var(--m-accent) 8%, transparent)" }}
            >
              {([["guest", "Invitado"], ["login", "Entrar"], ["register", "Crear cuenta"]] as [Tab, string][]).map(
                ([k, label]) => (
                  <button
                    key={k}
                    onClick={() => { setTab(k); setError(null); }}
                    className="flex-1 rounded-lg px-2 py-2 transition"
                    style={
                      tab === k
                        ? { background: "var(--m-surface)", color: "var(--m-accent)", boxShadow: "0 4px 14px -6px rgb(78 58 38 / 0.16)" }
                        : { color: "var(--m-muted)" }
                    }
                  >
                    {label}
                  </button>
                )
              )}
            </div>

            {tab === "guest" && (
              <div className="space-y-3">
                <Field label="Tu nombre">
                  <input
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && void handleGuest()}
                    placeholder="Ej: Ana"
                    autoFocus
                    maxLength={120}
                    className={inputCls}
                  />
                </Field>
                <button onClick={handleGuest} disabled={!name.trim() || busy} className={primaryCls}>
                  {busy ? "Un momento…" : "Continuar como invitado"}
                </button>
                <p className="text-center text-xs" style={{ color: "var(--m-muted)" }}>
                  ¿Quieres acumular puntos Mesto Beach? Crea una cuenta.
                </p>
              </div>
            )}

            {tab === "login" && (
              <div className="space-y-3">
                <Field label="Correo">
                  <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" inputMode="email" autoComplete="email" placeholder="tucorreo@email.com" className={inputCls} />
                </Field>
                <Field label="Contraseña">
                  <input value={password} onChange={(e) => setPassword(e.target.value)} type="password" autoComplete="current-password" placeholder="••••••••" className={inputCls} />
                </Field>
                <button onClick={handleLogin} disabled={!email.trim() || !password || busy} className={primaryCls}>
                  {busy ? "Entrando…" : "Iniciar sesión"}
                </button>
                <p className="text-center text-xs" style={{ color: "var(--m-muted)" }}>Recuperar contraseña y Google — próximamente.</p>
              </div>
            )}

            {tab === "register" && (
              <div className="space-y-3">
                <Field label="Tu nombre">
                  <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ej: Ana Pérez" maxLength={120} className={inputCls} />
                </Field>
                <Field label="Correo">
                  <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" inputMode="email" autoComplete="email" placeholder="tucorreo@email.com" className={inputCls} />
                </Field>
                <Field label="Contraseña (mín. 8)">
                  <input value={password} onChange={(e) => setPassword(e.target.value)} type="password" autoComplete="new-password" placeholder="••••••••" className={inputCls} />
                </Field>
                <Field label="Teléfono (opcional)">
                  <input value={phone} onChange={(e) => setPhone(e.target.value)} type="tel" inputMode="tel" placeholder="+57 300 000 0000" className={inputCls} />
                </Field>
                <button onClick={handleRegister} disabled={!name.trim() || !email.trim() || password.length < 8 || busy} className={primaryCls}>
                  {busy ? "Creando…" : "Crear cuenta Mesto Beach"}
                </button>
              </div>
            )}

            {error && <p className="mt-3 text-center text-sm font-medium text-rose-600">{error}</p>}
          </div>
        </div>
      </div>
    </div>
  );
}

const inputCls = "input text-base";
const primaryCls = "btn-primary mt-1 w-full justify-center text-base disabled:opacity-50";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold" style={{ color: "var(--m-muted)" }}>{label}</span>
      {children}
    </label>
  );
}

function detail(err: unknown): string | null {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? null;
}
