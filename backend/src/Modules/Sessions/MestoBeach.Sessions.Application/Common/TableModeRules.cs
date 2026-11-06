using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Sessions.Application.Common;

/// <summary>Estación KDS leída en solo lectura (la tabla es de Orders): lo justo para validar la barra de una mesa.</summary>
public record StationInfo(Guid Id, Guid TenantId, string Name, string FlowProfile, bool IsActive, IReadOnlyList<string> ActiveIn)
{
    public bool IsBar => FlowProfile == "BAR";
    /// <summary>ADR-037: <c>is_active && active_in ∋ mode</c>.</summary>
    public bool IsOnIn(string mode) => IsActive && ActiveIn.Contains(mode);
}

public interface IStationReadService
{
    Task<StationInfo?> GetAsync(Guid stationId, CancellationToken ct = default);
}

/// <summary>
/// Reglas de la distribución por modo (PRD-019, ADR-038, SDD-003 §table_layouts). Una mesa está en
/// servicio en un modo si <c>tables.active</c>, su fila de ese modo existe con <c>active = true</c> y,
/// cuando la fila tiene piso, el piso incluye el modo en <c>visible_in</c>.
/// </summary>
public static class TableModeRules
{
    public const string TableClosedInModeCode = "TABLE_CLOSED_IN_MODE";
    public const string InvalidModeCode = "INVALID_MODE";
    public const string InvalidTableKindCode = "INVALID_TABLE_KIND";

    public static readonly IReadOnlyList<string> Modes = [nameof(VenueMode.RESTAURANT), nameof(VenueMode.NIGHT)];

    /// <summary>Normaliza un modo del wire (case-insensitive). Null si no es RESTAURANT ni NIGHT.</summary>
    public static string? ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = raw.Trim().ToUpperInvariant();
        return Modes.Contains(m) ? m : null;
    }

    public static string Other(string mode) =>
        mode == nameof(VenueMode.NIGHT) ? nameof(VenueMode.RESTAURANT) : nameof(VenueMode.NIGHT);

    /// <summary>Piso visible para la fila: sin piso (sin ubicar / ad-hoc) no se le aplica la condición.</summary>
    public static bool FloorVisible(TableLayout? row, IReadOnlyDictionary<Guid, Floor> floors, string mode) =>
        row?.FloorId is not Guid fid
        || !floors.TryGetValue(fid, out var floor)
        || floor.IsVisibleIn(mode);

    public static bool InService(DiningTable table, TableLayout? row, IReadOnlyDictionary<Guid, Floor> floors, string mode) =>
        table.Active && row is not null && row.Active && FloorVisible(row, floors, mode);

    public static Error TableClosedInMode(string tableCode, string mode) => new(
        TableClosedInModeCode,
        "Esta zona está cerrada en el modo actual.",
        ErrorType.Conflict,
        new Dictionary<string, object?> { ["tableCode"] = tableCode, ["mode"] = mode });

    public static Error VenueModeUnavailable() =>
        new(VenueModeUnavailableException.Code, "No se pudo leer el modo del local. Inténtalo de nuevo.",
            ErrorType.ServiceUnavailable);

    /// <summary>
    /// Las dos filas de una mesa nueva (SDD-003 §POST /tables): la del modo pedido con la posición
    /// recibida y <paramref name="activeInMode"/>, y la del otro modo con la misma posición, inactiva y sin barra.
    /// </summary>
    public static (TableLayout Requested, TableLayout Other) NewTableRows(
        DiningTable table, string mode, Guid? floorId, double? posX, double? posY, bool activeInOther = false) =>
        (new TableLayout(table.TenantId, table.Id, mode, floorId, posX, posY, active: true),
         new TableLayout(table.TenantId, table.Id, Other(mode), floorId, posX, posY, active: activeInOther));
}

public static class VenueModeProviderExtensions
{
    /// <summary>Modo vigente como string del wire; <c>VENUE_MODE_UNAVAILABLE</c> (503) si Redis y Postgres fallan.</summary>
    public static async Task<Result<string>> GetModeAsync(this IVenueModeProvider provider, Guid tenantId, CancellationToken ct)
    {
        try
        {
            return Result.Success((await provider.GetAsync(tenantId, ct)).ToString());
        }
        catch (VenueModeUnavailableException)
        {
            return Result.Failure<string>(TableModeRules.VenueModeUnavailable());
        }
    }
}

public static class SessionsMetrics
{
    public static readonly System.Diagnostics.Metrics.Meter Meter = new("MestoBeach.Sessions");
    /// <summary>label <c>mode</c>.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> TablesLayoutSaved =
        Meter.CreateCounter<long>("sessions.tables_layout_saved");
    /// <summary>label <c>mode</c>.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> TableClosedInMode =
        Meter.CreateCounter<long>("sessions.table_closed_in_mode");
    /// <summary>labels <c>mode</c>, <c>includeHidden</c>.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> FloorplanGet =
        Meter.CreateCounter<long>("sessions.floorplan_get");
    public static readonly System.Diagnostics.Metrics.Counter<long> FloorVisibilityUpdated =
        Meter.CreateCounter<long>("sessions.floor_visibility_updated");
}

/// <summary>Comprobación de «mesa en servicio en el modo activo» para abrir o unirse (TABLE_CLOSED_IN_MODE).</summary>
public static class TableServiceGate
{
    /// <summary>
    /// Null si la mesa acepta sesiones en el modo activo; si no, el error (409 TABLE_CLOSED_IN_MODE o
    /// 503 VENUE_MODE_UNAVAILABLE). Solo comprueba piso visible y fila del modo: el interruptor global
    /// <c>tables.active</c> lo valida cada handler como hasta ahora.
    /// </summary>
    public static async Task<(Error? Error, string? Mode)> CheckAsync(
        MestoBeach.Sessions.Domain.Repositories.ITableRepository tables,
        MestoBeach.Sessions.Domain.Repositories.IFloorRepository floors,
        IVenueModeProvider venueMode,
        DiningTable table, CancellationToken ct)
    {
        var modeResult = await venueMode.GetModeAsync(table.TenantId, ct);
        if (modeResult.IsFailure) return (modeResult.Error, null);
        var mode = modeResult.Value;

        var row = await tables.GetLayoutAsync(table.Id, mode, ct);
        bool open = row is not null && row.Active;
        if (open && row!.FloorId is Guid fid)
        {
            var floor = await floors.GetByIdAsync(fid, ct);
            open = floor is null || floor.IsVisibleIn(mode);
        }

        if (open) return (null, mode);
        SessionsMetrics.TableClosedInMode.Add(1, new KeyValuePair<string, object?>("mode", mode));
        return (TableModeRules.TableClosedInMode(table.TableCode, mode), mode);
    }
}
