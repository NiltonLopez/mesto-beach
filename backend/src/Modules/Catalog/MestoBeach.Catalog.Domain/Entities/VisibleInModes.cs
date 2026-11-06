namespace MestoBeach.Catalog.Domain.Entities;

/// <summary>
/// Modos del local en los que se ve una categoría o un ítem (ADR-041, SDD-002 §Visibilidad por modo).
/// Strings y no el enum de Shared porque es lo que se persiste en Mongo y viaja en la caché; los valores
/// coinciden con <c>VenueMode.ToString()</c>.
/// </summary>
public static class VisibleInModes
{
    public const string Restaurant = "RESTAURANT";
    public const string Night = "NIGHT";

    public const string EmptyErrorCode = "VISIBLE_IN_EMPTY";
    public const string InvalidModeErrorCode = "INVALID_MODE";

    /// <summary>Default de todo registro sin el campo: visible en ambos modos, en orden canónico.</summary>
    public static List<string> Both() => [Restaurant, Night];

    /// <summary>null/ausente = ambos modos (datos anteriores a la función, entradas de caché viejas).</summary>
    public static bool Includes(IReadOnlyCollection<string>? visibleIn, string mode) =>
        visibleIn is null || visibleIn.Contains(mode);

    public static bool IsBoth(IReadOnlyCollection<string> visibleIn) =>
        visibleIn.Contains(Restaurant) && visibleIn.Contains(Night);

    /// <summary>
    /// Normaliza lo que llega del admin: recorta, mayúsculas, sin duplicados y en orden canónico.
    /// Devuelve el código de error (<see cref="InvalidModeErrorCode"/> o <see cref="EmptyErrorCode"/>) si no vale.
    /// </summary>
    public static (List<string>? Value, string? ErrorCode) Normalize(IEnumerable<string?> raw)
    {
        var set = new HashSet<string>();
        foreach (var r in raw)
        {
            var v = (r ?? string.Empty).Trim().ToUpperInvariant();
            if (v.Length == 0) continue;
            if (v != Restaurant && v != Night) return (null, InvalidModeErrorCode);
            set.Add(v);
        }
        if (set.Count == 0) return (null, EmptyErrorCode);
        var result = new List<string>(2);
        if (set.Contains(Restaurant)) result.Add(Restaurant);
        if (set.Contains(Night)) result.Add(Night);
        return (result, null);
    }
}
