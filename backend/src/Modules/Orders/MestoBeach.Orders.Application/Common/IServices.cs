namespace MestoBeach.Orders.Application.Common;

// ─── Catalog validation ───────────────────────────────────────────────────────

public record ModifierOptionSnapshot(
    string Id, string Name, int PriceDeltaCents, bool Available);

public record ModifierGroupSnapshot(
    string Id,
    string Name,
    string Type,           // SINGLE | MULTI | REMOVE
    bool Required,
    int MinSelect,
    int? MaxSelect,
    IReadOnlyList<ModifierOptionSnapshot> Options);

public record CatalogItemSnapshot(
    string Id,         // Catalog item GUID (string)
    string Name,
    int PriceCents,
    string CategoryId,
    bool Available,
    IReadOnlyList<ModifierGroupSnapshot> ModifierGroups,
    bool AllowNotes,
    /// <summary>
    /// Estación fijada en el propio plato. Si es null, manda la de su categoría.
    /// Va al final y con valor por defecto para no romper a quien construya el snapshot
    /// por posición.
    /// </summary>
    string? StationId = null);

// ─── Modifier resolution & pricing (server-authoritative) ─────────────────────

/// <summary>
/// Validates a customer's modifier selection against an item's groups and computes the
/// authoritative unit price (base + Σ deltas) plus a human-readable summary
/// (e.g. "3/4 · sin Cebolla · + Michelada"). REMOVE options never add cost.
/// </summary>
public static class ModifierResolution
{
    public sealed record LineResult(int UnitPriceCents, string? Summary);

    public static (LineResult? Result, string? Error) Resolve(
        CatalogItemSnapshot item, IReadOnlyList<Guid>? selectedOptionIds)
    {
        var selected = (selectedOptionIds ?? [])
            .Select(g => g.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allOptions = item.ModifierGroups
            .SelectMany(g => g.Options)
            .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Reject options that don't belong to this item's groups (anti-tampering).
        foreach (var s in selected)
        {
            if (!allOptions.TryGetValue(s, out var opt))
                return (null, $"Selección no válida para '{item.Name}'.");
            if (!opt.Available)
                return (null, $"La opción '{opt.Name}' no está disponible.");
        }

        int delta = 0;
        var parts = new List<string>();

        foreach (var g in item.ModifierGroups)
        {
            var chosen = g.Options.Where(o => selected.Contains(o.Id)).ToList();
            var type = (g.Type ?? "MULTI").ToUpperInvariant();

            if (type == "SINGLE")
            {
                if (chosen.Count > 1)
                    return (null, $"Solo puede elegir una opción en '{g.Name}'.");
                if (g.Required && chosen.Count < 1)
                    return (null, $"Debe elegir una opción en '{g.Name}'.");
                foreach (var o in chosen) { delta += o.PriceDeltaCents; parts.Add(o.Name); }
            }
            else if (type == "REMOVE")
            {
                // No cost, optional by nature; summary reads "sin X".
                foreach (var o in chosen) parts.Add($"sin {o.Name}");
            }
            else // MULTI
            {
                int min = g.Required ? Math.Max(1, g.MinSelect) : g.MinSelect;
                if (chosen.Count < min)
                    return (null, $"Debe elegir al menos {min} en '{g.Name}'.");
                if (g.MaxSelect.HasValue && chosen.Count > g.MaxSelect.Value)
                    return (null, $"Máximo {g.MaxSelect.Value} en '{g.Name}'.");
                foreach (var o in chosen) { delta += o.PriceDeltaCents; parts.Add($"+ {o.Name}"); }
            }
        }

        var summary = parts.Count > 0 ? string.Join(" · ", parts) : null;
        return (new LineResult(item.PriceCents + delta, summary), null);
    }
}

public record CatalogValidationResult(
    List<CatalogItemSnapshot> ValidItems,
    List<string> InvalidItemIds,
    /// <summary>
    /// Existen pero no son pedibles en el modo (ADR-041): item.visibleIn ∋ M ∧ category.visibleIn ∋ M,
    /// con null/ausente = ambos. Aunque estén agotados cuentan aquí → 409 ITEM_NOT_IN_MODE.
    /// </summary>
    List<NotInModeItem> NotInModeItems);

public record NotInModeItem(string Id, string Name);

public interface ICatalogValidationService
{
    /// <summary>
    /// Validates a list of catalogItemIds against the tenant catalog.
    /// Uses Redis (L1) then MongoDB (L2) with a 2s timeout on MongoDB.
    /// Throws <see cref="CatalogUnavailableException"/> if Redis miss AND MongoDB timeout.
    /// </summary>
    Task<CatalogValidationResult> ValidateAsync(
        Guid tenantId, IEnumerable<string> catalogItemIds, MestoBeach.Shared.Venue.VenueMode mode,
        CancellationToken ct = default);
}

public sealed class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException() : base("Catálogo no disponible.") { }
}

// ─── PIN validation ───────────────────────────────────────────────────────────

public interface IPinValidationService
{
    /// <summary>
    /// True si el participante validó el PIN. Redis (<c>pin_validated:*</c>) es caché; si falta o
    /// falla, manda <paramref name="dbPinValidated"/> (session_participants.pin_validated) y, si es
    /// true, se re-siembra la clave con 12 h (best-effort, log order.pin_reseeded). ADR-040.
    /// </summary>
    Task<bool> IsPinValidatedAsync(
        Guid tenantId, Guid sessionId, Guid participantId, bool dbPinValidated, CancellationToken ct = default);
}

// ─── KDS station routing ──────────────────────────────────────────────────────

public interface IKdsRoutingService
{
    /// <summary>
    /// Returns the stationId for the first item whose categoryId matches a KDS station,
    /// or null if no match found.
    /// </summary>
    Task<Guid?> ResolveStationAsync(
        Guid tenantId, IEnumerable<string> categoryIds, CancellationToken ct = default);
}

// ─── SignalR notifications ────────────────────────────────────────────────────

public interface IOrderSignalRNotifier
{
    Task NotifySessionAsync(Guid tenantId, Guid sessionId, object payload, CancellationToken ct = default);
    Task NotifyTenantAsync(Guid tenantId, object payload, CancellationToken ct = default);
    Task NotifyStationAsync(Guid tenantId, Guid stationId, object payload, CancellationToken ct = default);
}

// ─── Session read (cross-module) ──────────────────────────────────────────────

public record SessionInfo(
    Guid Id,
    Guid TenantId,
    string TableCode,
    bool IsOpen,
    bool PinRequired,
    // PRD-019: mesa de la sesión (sessions.table_id) para leer su fila de table_layouts.
    Guid TableId = default);

public record ParticipantInfo(Guid Id, Guid SessionId, Guid? CustomerAccountId, string? DisplayName, bool PinValidated = false);

public interface ISessionReadService
{
    Task<SessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<ParticipantInfo?> GetParticipantAsync(Guid participantId, Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// PRD-019 / ADR-038: barra de la mesa en el modo (<c>table_layouts(table_id, mode).station_id</c>).
    /// Null si la mesa no tiene fila en ese modo o no tiene barra.
    /// </summary>
    Task<Guid?> GetTableStationAsync(Guid tableId, string mode, CancellationToken ct = default);

    /// <summary>
    /// PRD-019 / ADR-038: clase de la mesa (<c>tables.kind</c>: TABLE | HIGH_TABLE | RESERVADO).
    /// Null si la mesa no existe. Fija <c>orders.priority</c> al crear la comanda en NIGHT.
    /// </summary>
    Task<string?> GetTableKindAsync(Guid tableId, CancellationToken ct = default);
}
