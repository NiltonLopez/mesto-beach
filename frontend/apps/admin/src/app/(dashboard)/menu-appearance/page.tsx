"use client";

import { useEffect, useRef, useState } from "react";
import type { MenuBrandingDto, MenuTheme, NightBrandingDto } from "@mestobeach/api-client";
import { catalogApi } from "@/lib/api";
import { useAuthStore } from "@/lib/stores";
import { THEMES, FONTS, ACCENT_SWATCHES, accentInk, googleFontsHref, paletteFromBg } from "@/lib/menuBranding";

function errMsg(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail ?? fallback;
}

const DEFAULT: MenuBrandingDto = {
  displayName: "",
  slogan: "",
  logoUrl: null,
  heroUrl: null,
  primaryColor: "#f97316",
  theme: "light",
  backgroundColor: null,
  fontKey: "manrope",
};

const NIGHT_EMPTY: NightBrandingDto = {
  enabled: false,
  displayName: null,
  slogan: null,
  logoUrl: null,
  heroUrl: null,
  primaryColor: null,
  theme: null,
  backgroundColor: null,
  fontKey: null,
};

type Tab = "day" | "night";
type InheritField = "displayName" | "slogan" | "primaryColor" | "theme" | "fontKey";

export default function MenuAppearancePage() {
  const { staff } = useAuthStore();
  const tenantId = staff?.tenantId ?? process.env["NEXT_PUBLIC_TENANT_ID"] ?? "";
  const isAdmin = staff?.roles?.includes("Admin") ?? false;

  const [b, setB] = useState<MenuBrandingDto>(DEFAULT);
  // PRD-019 RF-1917: pestaña «Día | Night». Night es una capa de overrides (null = hereda del día).
  const [tab, setTab] = useState<Tab>("day");
  const [n, setN] = useState<NightBrandingDto>(NIGHT_EMPTY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState<"logo" | "hero" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const logoInput = useRef<HTMLInputElement>(null);
  const heroInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!tenantId) return;
    catalogApi
      .getMenuBranding(tenantId)
      .then(({ data }) => setB({ ...DEFAULT, ...data }))
      .catch(() => setError("No se pudo cargar la apariencia."))
      .finally(() => setLoading(false));
    catalogApi
      .getNightBranding(tenantId)
      .then(({ data }) => setN(data.night))
      .catch(() => setError("No se pudo cargar la apariencia de Night."));
  }, [tenantId]);

  // Vista resuelta de la pestaña activa: en Night, cada campo sin override muestra el del día.
  const cur: MenuBrandingDto =
    tab === "day"
      ? b
      : {
          displayName: n.displayName ?? b.displayName,
          slogan: n.slogan ?? b.slogan,
          logoUrl: n.logoUrl ?? b.logoUrl ?? null,
          heroUrl: n.heroUrl ?? b.heroUrl ?? null,
          primaryColor: n.primaryColor ?? b.primaryColor,
          theme: n.theme ?? b.theme,
          backgroundColor: n.backgroundColor,
          fontKey: n.fontKey ?? b.fontKey,
        };

  /** Escribe en el día o como override de Night según la pestaña. */
  function setField(patch: Partial<Pick<MenuBrandingDto, "displayName" | "slogan" | "primaryColor" | "theme" | "backgroundColor" | "fontKey">>) {
    if (tab === "day") setB((prev) => ({ ...prev, ...patch }));
    else setN((prev) => ({ ...prev, ...patch }));
  }
  function inherit(field: InheritField) {
    setN((prev) => ({ ...prev, [field]: null }));
  }

  // Load the selected font so the preview shows it live.
  useEffect(() => {
    const id = "menu-preview-font";
    let link = document.getElementById(id) as HTMLLinkElement | null;
    if (!link) {
      link = document.createElement("link");
      link.id = id;
      link.rel = "stylesheet";
      document.head.appendChild(link);
    }
    link.href = googleFontsHref(cur.fontKey);
  }, [cur.fontKey]);

  function flash(msg: string) {
    setSuccess(msg);
    setTimeout(() => setSuccess(null), 3500);
  }

  async function handleSave() {
    setError(null);
    setSaving(true);
    try {
      if (tab === "night") {
        const { data } = await catalogApi.updateNightBranding(tenantId, {
          enabled: n.enabled,
          displayName: n.displayName,
          slogan: n.slogan,
          primaryColor: n.primaryColor,
          theme: n.theme,
          backgroundColor: n.backgroundColor,
          fontKey: n.fontKey,
        });
        setN(data.night);
        flash("Apariencia de Night guardada.");
        return;
      }
      const { data } = await catalogApi.updateMenuBranding(tenantId, {
        displayName: b.displayName,
        slogan: b.slogan,
        primaryColor: b.primaryColor,
        theme: b.theme,
        backgroundColor: b.backgroundColor ?? null,
        fontKey: b.fontKey,
      });
      setB((prev) => ({ ...prev, ...data }));
      flash("Apariencia guardada. Tus clientes ya la verán en el menú.");
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo guardar."));
    } finally {
      setSaving(false);
    }
  }

  async function handleUpload(kind: "logo" | "hero", file: File) {
    setError(null);
    setUploading(kind);
    try {
      if (tab === "night") {
        const { data } = await catalogApi.uploadNightBrandingImage(tenantId, kind, file);
        setN(data.night);
      } else {
        const { data } = await catalogApi.uploadBrandingImage(tenantId, kind, file);
        setB((prev) => ({ ...prev, ...data }));
      }
      flash(kind === "logo" ? "Logo actualizado." : "Portada actualizada.");
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo subir la imagen (máx 4 MB, JPG/PNG/WebP)."));
    } finally {
      setUploading(null);
    }
  }

  async function handleRemoveImage(kind: "logo" | "hero") {
    try {
      if (tab === "night") {
        const { data } = await catalogApi.deleteNightBrandingImage(tenantId, kind);
        setN(data.night);
      } else {
        const { data } = await catalogApi.deleteBrandingImage(tenantId, kind);
        setB((prev) => ({ ...prev, ...data }));
      }
    } catch (err: unknown) {
      setError(errMsg(err, "No se pudo quitar la imagen."));
    }
  }

  if (!isAdmin) {
    return (
      <div className="p-6">
        <h1 className="mb-2 font-display text-2xl font-normal text-ink">Apariencia del menú</h1>
        <div className="card p-6 text-sm text-muted">
          Solo un usuario <strong>Admin</strong> puede personalizar la apariencia del menú.
        </div>
      </div>
    );
  }

  // Custom background color (if set) overrides the light/cream/dark preset.
  const theme = cur.backgroundColor ? paletteFromBg(cur.backgroundColor) : THEMES[cur.theme];
  const font = FONTS[cur.fontKey] ?? FONTS["manrope"]!;
  const ink = accentInk(cur.primaryColor);
  const shownName = cur.displayName.trim() || staff?.tenantName || "Mi Restaurante";

  return (
    <div className="p-6">
      <div className="mb-1 flex items-center justify-between">
        <h1 className="font-display text-2xl font-normal text-ink">Apariencia del menú</h1>
        <button onClick={handleSave} disabled={saving || loading} className="btn-primary text-sm">
          {saving ? "Guardando…" : "Guardar cambios"}
        </button>
      </div>
      <p className="mb-5 max-w-2xl text-sm text-muted">
        Personaliza cómo ven tus clientes el menú al escanear el QR. El <strong>login</strong> del menú conserva la
        marca de la plataforma; esto aplica al menú una vez dentro.
      </p>

      <div role="tablist" aria-label="Modo de la apariencia" className="mb-5 inline-flex rounded-xl border border-hair/55 bg-paper p-1">
        {(["day", "night"] as const).map((t) => (
          <button
            key={t}
            role="tab"
            aria-selected={tab === t}
            onClick={() => setTab(t)}
            className={`min-h-[44px] rounded-lg px-5 py-2 text-sm font-semibold transition ${
              tab === t ? "bg-brand-500 text-white shadow-warm-sm" : "text-muted hover:text-ink"
            }`}
          >
            {t === "day" ? "Día" : "Night"}
          </button>
        ))}
      </div>

      {tab === "night" && (
        <div className="card mb-5 flex flex-wrap items-center justify-between gap-3 p-4">
          <p className="max-w-xl text-sm text-muted">
            Night se aplica sobre la apariencia del día: lo que no cambies aquí se <strong>hereda</strong>. El fondo
            libre no se hereda: sin color propio manda el tema.
          </p>
          <button
            type="button"
            role="switch"
            aria-checked={n.enabled}
            onClick={() => setN((prev) => ({ ...prev, enabled: !prev.enabled }))}
            className={`min-h-[44px] rounded-full border px-4 py-1.5 text-sm font-semibold transition ${
              n.enabled ? "border-brand-500 bg-brand-500 text-white" : "border-hair/55 bg-paper text-muted"
            }`}
          >
            {n.enabled ? "Apariencia Night activa" : "Apariencia Night apagada"}
          </button>
        </div>
      )}

      {error && <div className="mb-4 rounded-lg border border-rose-600/30 bg-rose-500/10 px-4 py-2.5 text-sm text-rose-700">{error}</div>}
      {success && <div className="mb-4 rounded-lg border border-emerald-600/30 bg-emerald-500/10 px-4 py-2.5 text-sm text-emerald-700">{success}</div>}

      {loading ? (
        <div className="flex justify-center py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-hair/40 border-t-brand-500" />
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[1fr_minmax(320px,380px)]">
          {/* ── Controls ── */}
          <div className="space-y-5">
            {/* Identity */}
            <section className="card p-5">
              <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-muted">Identidad</h2>
              <div className="space-y-4">
                <div>
                  <div className="flex items-center justify-between"><label className="label">Nombre a mostrar</label><InheritBtn tab={tab} overridden={n.displayName !== null} onInherit={() => inherit("displayName")} /></div>
                  <input className="input" value={cur.displayName} maxLength={80}
                    onChange={(e) => setField({ displayName: e.target.value })}
                    placeholder={staff?.tenantName ?? "Mi Restaurante"} />
                </div>
                <div>
                  <div className="flex items-center justify-between"><label className="label">Slogan</label><InheritBtn tab={tab} overridden={n.slogan !== null} onInherit={() => inherit("slogan")} /></div>
                  <input className="input" value={cur.slogan} maxLength={140}
                    onChange={(e) => setField({ slogan: e.target.value })}
                    placeholder="Ej: Comida casera, hecha con cariño" />
                </div>
              </div>
            </section>

            {/* Images */}
            <section className="card p-5">
              <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-muted">Logo y portada</h2>
              <div className="grid gap-4 sm:grid-cols-2">
                <ImageField
                  label="Logo" hint="Cuadrado, fondo transparente ideal"
                  url={cur.logoUrl} busy={uploading === "logo"} round
                  onPick={() => logoInput.current?.click()} onRemove={() => handleRemoveImage("logo")} />
                <ImageField
                  label="Portada (banner)" hint="Foto del local o de tus platos"
                  url={cur.heroUrl} busy={uploading === "hero"}
                  onPick={() => heroInput.current?.click()} onRemove={() => handleRemoveImage("hero")} />
              </div>
              <input ref={logoInput} type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
                onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ""; if (f) void handleUpload("logo", f); }} />
              <input ref={heroInput} type="file" accept="image/jpeg,image/png,image/webp" className="hidden"
                onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ""; if (f) void handleUpload("hero", f); }} />
            </section>

            {/* Color */}
            <section className="card p-5">
              <div className="mb-4 flex items-center justify-between"><h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Color de acento</h2><InheritBtn tab={tab} overridden={n.primaryColor !== null} onInherit={() => inherit("primaryColor")} /></div>
              <div className="flex flex-wrap items-center gap-2.5">
                {ACCENT_SWATCHES.map((c) => (
                  <button key={c} onClick={() => setField({ primaryColor: c })}
                    className={`h-9 w-9 rounded-full transition ${cur.primaryColor.toLowerCase() === c ? "ring-2 ring-ink ring-offset-2" : "hover:scale-110"}`}
                    style={{ background: c }} aria-label={c} />
                ))}
                <label className="ml-2 flex items-center gap-2 text-sm text-muted">
                  <input type="color" value={cur.primaryColor}
                    onChange={(e) => setField({ primaryColor: e.target.value })}
                    className="h-9 w-9 cursor-pointer rounded-lg border border-hair/50 bg-paper p-0.5" />
                  Personalizado
                </label>
              </div>
            </section>

            {/* Theme */}
            <section className="card p-5">
              <div className="mb-1 flex items-center justify-between"><h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Fondo del menú</h2><InheritBtn tab={tab} overridden={n.theme !== null} onInherit={() => inherit("theme")} /></div>
              <p className="mb-4 text-xs text-muted">Elige un fondo predefinido o tu propio color. Los textos y tarjetas se ajustan solos para que siempre se lean bien.</p>
              <div className="grid grid-cols-3 gap-3">
                {(Object.keys(THEMES) as MenuTheme[]).map((t) => {
                  const th = THEMES[t];
                  const on = !cur.backgroundColor && cur.theme === t;
                  return (
                    <button key={t} onClick={() => setField({ theme: t, backgroundColor: null })}
                      className={`rounded-xl border p-3 text-left transition ${on ? "border-brand-500 ring-1 ring-brand-500" : "border-hair/40 hover:border-hair/60"}`}>
                      <div className="mb-2 h-12 w-full rounded-lg border" style={{ background: th.bg, borderColor: th.border }}>
                        <div className="m-2 h-4 w-2/3 rounded" style={{ background: th.surface, border: `1px solid ${th.border}` }} />
                      </div>
                      <span className="text-sm font-semibold text-ink">{th.label}</span>
                    </button>
                  );
                })}
              </div>

              {/* Custom background color */}
              <label
                className={`mt-3 flex cursor-pointer items-center gap-3 rounded-xl border p-3 transition ${
                  cur.backgroundColor ? "border-brand-500 ring-1 ring-brand-500" : "border-hair/40 hover:border-hair/60"
                }`}
              >
                <span className="relative h-12 w-12 flex-shrink-0">
                  <span className="block h-full w-full rounded-lg border border-hair/50" style={{ background: cur.backgroundColor ?? "#ffffff" }} />
                  <input
                    type="color"
                    value={cur.backgroundColor ?? "#ffffff"}
                    onChange={(e) => setField({ backgroundColor: e.target.value })}
                    className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
                    aria-label="Color de fondo personalizado"
                  />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-semibold text-ink">Color personalizado</div>
                  <div className="text-xs text-muted">
                    {cur.backgroundColor ? <span className="font-mono uppercase">{cur.backgroundColor}</span> : "Toca para elegir cualquier color"}
                  </div>
                </div>
                {cur.backgroundColor && (
                  <button
                    type="button"
                    onClick={(e) => { e.preventDefault(); setField({ backgroundColor: null }); }}
                    className="flex-shrink-0 rounded-lg border border-hair/50 px-2.5 py-1.5 text-xs font-semibold text-muted hover:bg-paper2"
                  >
                    Quitar
                  </button>
                )}
              </label>
            </section>

            {/* Font */}
            <section className="card p-5">
              <div className="mb-4 flex items-center justify-between"><h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Tipografía</h2><InheritBtn tab={tab} overridden={n.fontKey !== null} onInherit={() => inherit("fontKey")} /></div>
              <div className="grid gap-2.5 sm:grid-cols-2">
                {Object.entries(FONTS).map(([key, f]) => {
                  const on = cur.fontKey === key;
                  return (
                    <button key={key} onClick={() => setField({ fontKey: key })}
                      className={`flex items-center justify-between rounded-xl border px-4 py-3 text-left transition ${on ? "border-brand-500 ring-1 ring-brand-500" : "border-hair/40 hover:border-hair/60"}`}>
                      <div>
                        <div className="text-lg leading-tight text-ink" style={{ fontFamily: `"${f.heading}", serif` }}>Aa Menú</div>
                        <div className="text-xs text-muted">{f.label}</div>
                      </div>
                      {on && <span className="text-brand-700">✓</span>}
                    </button>
                  );
                })}
              </div>
            </section>
          </div>

          {/* ── Live preview ── */}
          <div className="lg:sticky lg:top-6 lg:self-start">
            <p className="mb-2 text-center text-xs font-semibold uppercase tracking-wide text-muted">Vista previa</p>
            <Preview
              theme={theme} font={font} accent={cur.primaryColor} ink={ink}
              name={shownName} slogan={cur.slogan} logoUrl={cur.logoUrl} heroUrl={cur.heroUrl} />
          </div>
        </div>
      )}
    </div>
  );
}

/** «Heredar del día» (solo en Night): vuelve el campo a seguir al día. */
function InheritBtn({ tab, overridden, onInherit }: { tab: Tab; overridden: boolean; onInherit: () => void }) {
  if (tab !== "night") return null;
  if (!overridden) return <span className="text-xs text-muted">Heredado del día</span>;
  return (
    <button
      type="button"
      onClick={onInherit}
      className="inline-flex min-h-[44px] items-center px-2 text-xs font-semibold text-brand-700 hover:underline"
    >
      Heredar del día
    </button>
  );
}

function ImageField({
  label, hint, url, busy, round, onPick, onRemove,
}: {
  label: string; hint: string; url: string | null | undefined; busy: boolean; round?: boolean;
  onPick: () => void; onRemove: () => void;
}) {
  return (
    <div>
      <label className="label">{label}</label>
      {url ? (
        <div className="relative inline-block">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={url} alt={label}
            className={`border border-hair/50 object-cover ${round ? "h-20 w-20 rounded-full" : "h-20 w-32 rounded-lg"}`} />
          <button onClick={onRemove}
            className="absolute -right-2 -top-2 flex h-6 w-6 items-center justify-center rounded-full bg-rose-500 text-xs text-white shadow-warm-sm"
            aria-label="Quitar">×</button>
        </div>
      ) : (
        <button onClick={onPick} disabled={busy}
          className={`flex flex-col items-center justify-center gap-1 border-2 border-dashed border-hair/50 text-muted transition hover:border-brand-500 hover:text-brand-700 ${round ? "h-20 w-20 rounded-full" : "h-20 w-32 rounded-lg"}`}>
          <span className="text-lg">{busy ? "…" : "＋"}</span>
          {!busy && <span className="text-[10px]">Subir</span>}
        </button>
      )}
      <p className="mt-1 text-xs text-muted">{hint}</p>
    </div>
  );
}

function Preview({
  theme, font, accent, ink, name, slogan, logoUrl, heroUrl,
}: {
  theme: (typeof THEMES)[MenuTheme]; font: { heading: string; body: string };
  accent: string; ink: string; name: string; slogan: string;
  logoUrl: string | null | undefined; heroUrl: string | null | undefined;
}) {
  const head = `"${font.heading}", serif`;
  const body = `"${font.body}", system-ui, sans-serif`;
  return (
    <div className="mx-auto w-full max-w-[340px] overflow-hidden rounded-[28px] border-[6px] border-ink shadow-warm-lg"
      style={{ background: theme.bg, fontFamily: body, color: theme.text }}>
      {/* Header / hero */}
      <div className="relative" style={{ height: heroUrl ? 130 : 96 }}>
        {heroUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={heroUrl} alt="" className="absolute inset-0 h-full w-full object-cover" />
        ) : (
          <div className="absolute inset-0" style={{ background: `linear-gradient(135deg, ${accent}, ${accent}cc)` }} />
        )}
        <div className="absolute inset-0" style={{ background: "linear-gradient(to top, rgba(0,0,0,.55), transparent 70%)" }} />
        <div className="absolute inset-x-0 bottom-0 flex items-end gap-2.5 p-3">
          {logoUrl && (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={logoUrl} alt="" className="h-11 w-11 flex-shrink-0 rounded-xl border-2 border-white/80 bg-white object-cover" />
          )}
          <div className="min-w-0">
            <div className="truncate text-[17px] font-bold leading-tight text-white" style={{ fontFamily: head }}>{name}</div>
            {slogan && <div className="truncate text-[11px] text-white/85">{slogan}</div>}
          </div>
        </div>
      </div>

      {/* Chips */}
      <div className="flex gap-2 px-3 pt-3">
        <span className="rounded-full px-3 py-1 text-[11px] font-semibold" style={{ background: accent, color: ink }}>Todos</span>
        <span className="rounded-full px-3 py-1 text-[11px] font-semibold" style={{ background: theme.surface, color: theme.muted, border: `1px solid ${theme.border}` }}>Bebidas</span>
        <span className="rounded-full px-3 py-1 text-[11px] font-semibold" style={{ background: theme.surface, color: theme.muted, border: `1px solid ${theme.border}` }}>Platos</span>
      </div>

      {/* Product cards */}
      <div className="grid grid-cols-2 gap-2.5 p-3">
        {[["Limonada de la casa", "8,00 €"], ["Café especial", "5,00 €"]].map(([n, p]) => (
          <div key={n} className="overflow-hidden rounded-2xl" style={{ background: theme.surface, border: `1px solid ${theme.border}` }}>
            <div className="relative aspect-square" style={{ background: `linear-gradient(135deg, ${accent}22, ${accent}08)` }}>
              <div className="absolute inset-0 grid place-items-center text-xl opacity-40" style={{ color: accent }}>◆</div>
              <span className="absolute right-2 top-2 grid h-7 w-7 place-items-center rounded-full text-sm font-bold shadow" style={{ background: accent, color: ink }}>＋</span>
            </div>
            <div className="p-2">
              <div className="truncate text-[12px] font-semibold" style={{ color: theme.text }}>{n}</div>
              <div className="text-[13px] font-bold" style={{ color: accent }}>{p}</div>
            </div>
          </div>
        ))}
      </div>

      {/* Cart button */}
      <div className="p-3 pt-0">
        <div className="flex items-center justify-between rounded-xl px-4 py-3 text-sm font-bold shadow" style={{ background: accent, color: ink }}>
          <span className="grid h-5 w-5 place-items-center rounded-full text-xs" style={{ background: "rgba(255,255,255,.25)" }}>2</span>
          <span>Ver carrito</span>
          <span style={{ fontFamily: head }}>13,00 €</span>
        </div>
      </div>
    </div>
  );
}
