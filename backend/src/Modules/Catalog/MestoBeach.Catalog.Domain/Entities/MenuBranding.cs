namespace MestoBeach.Catalog.Domain.Entities;

/// <summary>
/// Tenant-controlled look &amp; feel for the customer menu (the QR-ordering PWA).
/// Lives in the catalog document so the public menu gets it in the same fetch.
/// Images are stored as data URLs (resized webp) to stay self-contained.
/// </summary>
public sealed class MenuBranding
{
    /// <summary>Name shown at the top of the menu. Empty → the frontend falls back to the tenant name.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string Slogan { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? HeroUrl { get; set; }
    /// <summary>Accent color (buttons, prices, active chips) as a #RRGGBB hex.</summary>
    public string PrimaryColor { get; set; } = "#f97316";
    /// <summary>Background preset: "light" | "cream" | "dark" | "night". Ignored when <see cref="BackgroundColor"/> is set.</summary>
    public string Theme { get; set; } = "light";
    /// <summary>Optional custom menu background as a #RRGGBB hex. When set it overrides the preset and the
    /// frontend derives a readable surface/text/border palette from it.</summary>
    public string? BackgroundColor { get; set; }
    /// <summary>Curated font pairing key (resolved to Google Fonts on the frontend).</summary>
    public string FontKey { get; set; } = "manrope";
    /// <summary>
    /// Overrides del modo NIGHT (ADR-039/041). null = el tenant nunca configuró Night, que equivale a
    /// <c>{ enabled: true }</c> con todo heredado. Setter público: viaja en la caché catalog:{t}.
    /// </summary>
    public NightBranding? Night { get; set; }
}

/// <summary>
/// Subdocumento <c>branding.night</c>: overrides, no copia. Cada campo null hereda el de día, salvo
/// <see cref="BackgroundColor"/>, cuyo null significa «sin fondo propio, manda el tema».
/// </summary>
public sealed class NightBranding
{
    /// <summary>Ausente = true. Solo false sirve de noche el branding de día tal cual.</summary>
    public bool Enabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public string? Slogan { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeroUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? Theme { get; set; }
    public string? BackgroundColor { get; set; }
    public string? FontKey { get; set; }
}

/// <summary>Resolución del branding por modo (SDD-002 §branding.night). Pura, sin I/O.</summary>
public static class BrandingResolver
{
    public const string NightDefaultTheme = "night";
    public const string NightDefaultPrimaryColor = "#AA8453";

    /// <summary>Branding que se sirve en <paramref name="mode"/> ("RESTAURANT" | "NIGHT").</summary>
    public static MenuBranding Resolve(MenuBranding day, string mode)
    {
        if (mode != VisibleInModes.Night) return day;
        if (day.Night is { Enabled: false }) return day;
        return ResolveNight(day);
    }

    /// <summary>
    /// Merge día + defaults de Night + overrides, sin mirar <c>enabled</c> (vista previa del admin).
    /// <c>backgroundColor</c> nunca hereda del día.
    /// </summary>
    public static MenuBranding ResolveNight(MenuBranding day)
    {
        var n = day.Night ?? new NightBranding();
        return new MenuBranding
        {
            DisplayName = n.DisplayName ?? day.DisplayName,
            Slogan = n.Slogan ?? day.Slogan,
            LogoUrl = n.LogoUrl ?? day.LogoUrl,
            HeroUrl = n.HeroUrl ?? day.HeroUrl,
            PrimaryColor = n.PrimaryColor ?? NightDefaultPrimaryColor,
            Theme = n.Theme ?? NightDefaultTheme,
            BackgroundColor = n.BackgroundColor,
            FontKey = n.FontKey ?? day.FontKey,
            Night = null,
        };
    }
}
