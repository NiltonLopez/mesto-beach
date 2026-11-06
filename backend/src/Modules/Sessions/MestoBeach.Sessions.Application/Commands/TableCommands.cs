using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Enums;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Plans;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Sessions.Application.Commands;

internal static class TableQrParsing
{
    public static QrMode Parse(string? value) =>
        string.Equals(value, "DYNAMIC", StringComparison.OrdinalIgnoreCase) ? QrMode.Dynamic : QrMode.Fixed;
}

// ══════════════════════════════════════════════════════════════════════════════
// CREATE TABLE
// ══════════════════════════════════════════════════════════════════════════════

public record CreateTableDto(
    Guid Id,
    Guid TenantId,
    string TableCode,
    string? Zone,
    bool Active,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    int? Seats,
    string Shape,
    string QrMode,
    bool RequiresPin,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Kind = DiningTable.KindTable,
    string? Mode = null);

public record CreateTableCommand(
    Guid TenantId,
    string TableCode,
    string? Zone,
    bool Active,
    Guid RequesterId,
    Guid? FloorId = null,
    double? PosX = null,
    double? PosY = null,
    int? Seats = null,
    string? Shape = null,
    string? QrMode = null,         // "FIXED" | "DYNAMIC" (default FIXED)
    bool RequiresPin = true,
    string? Mode = null,           // PRD-019: "RESTAURANT" | "NIGHT"; null = modo activo
    string? Kind = null) : IRequest<Result<CreateTableDto>>;

public sealed class CreateTableValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.TableCode)
            .NotEmpty().WithMessage("tableCode es requerido.")
            .MaximumLength(50).WithMessage("tableCode no puede exceder 50 caracteres.")
            .Matches(@"^[a-zA-Z0-9\-]+$").WithMessage("tableCode solo permite alfanuméricos y guiones.");
        RuleFor(c => c.Zone)
            .MaximumLength(100).When(c => c.Zone is not null);
        RuleFor(c => c.Shape)
            .Must(s => s!.ToUpperInvariant() is "SQUARE" or "ROUND").When(c => c.Shape is not null)
            .WithMessage("shape debe ser SQUARE o ROUND.");
        RuleFor(c => c.Seats).GreaterThanOrEqualTo(0).When(c => c.Seats.HasValue);
        RuleFor(c => c.QrMode)
            .Must(q => q!.ToUpperInvariant() is "FIXED" or "DYNAMIC").When(c => c.QrMode is not null)
            .WithMessage("qrMode debe ser FIXED o DYNAMIC.");
    }
}

public sealed class CreateTableHandler : IRequestHandler<CreateTableCommand, Result<CreateTableDto>>
{
    private readonly ITableRepository _tables;
    private readonly IPlanEntitlementsReader _entitlements;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<CreateTableHandler> _logger;

    public CreateTableHandler(
        ITableRepository tables, IPlanEntitlementsReader entitlements, IVenueModeProvider venueMode,
        ILogger<CreateTableHandler> logger)
    {
        _tables = tables;
        _entitlements = entitlements;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<CreateTableDto>> Handle(CreateTableCommand cmd, CancellationToken ct)
    {
        // PRD-019: modo de la fila activa. Por defecto, el modo vigente del local.
        string mode;
        if (cmd.Mode is not null)
        {
            var parsed = TableModeRules.ParseMode(cmd.Mode);
            if (parsed is null)
                return Result.Failure<CreateTableDto>(
                    Error.Validation(TableModeRules.InvalidModeCode, "mode debe ser RESTAURANT o NIGHT."));
            mode = parsed;
        }
        else
        {
            var current = await _venueMode.GetModeAsync(cmd.TenantId, ct);
            if (current.IsFailure) return Result.Failure<CreateTableDto>(current.Error);
            mode = current.Value;
        }

        if (cmd.Kind is not null && !DiningTable.Kinds.Contains(cmd.Kind.Trim().ToUpperInvariant()))
            return Result.Failure<CreateTableDto>(Error.Validation(TableModeRules.InvalidTableKindCode,
                "kind debe ser TABLE, HIGH_TABLE o RESERVADO."));

        var existing = await _tables.GetByCodeAsync(cmd.TenantId, cmd.TableCode, ct);
        if (existing is not null)
            return Result.Failure<CreateTableDto>(
                Error.Conflict($"Ya existe una mesa con código '{cmd.TableCode}' para este tenant."));

        var ent = await _entitlements.GetAsync(cmd.TenantId, ct);
        if (ent is not null && ent.MaxTables is int maxTables)
        {
            var count = (await _tables.ListAsync(cmd.TenantId, null, null, ct)).Count;
            if (ent.ExceedsTables(count))
                return Result.Failure<CreateTableDto>(
                    PlanLimits.Exceeded("mesas", maxTables, count, ent.PlanCode));
        }

        var table = new DiningTable(Guid.NewGuid(), cmd.TenantId, cmd.TableCode, cmd.Zone, cmd.Active);
        table.SetDetails(cmd.Seats, cmd.Shape);
        table.SetQrConfig(TableQrParsing.Parse(cmd.QrMode), cmd.RequiresPin);
        if (cmd.Kind is not null) table.SetKind(cmd.Kind);
        await _tables.AddAsync(table, ct);

        // Las dos filas de table_layouts en la misma transacción (un solo SaveChanges): activa en
        // `mode` con la posición recibida, inactiva y sin barra en el otro modo con la misma posición.
        // tables.floor_id/pos_x/pos_y ya no se escriben (deprecadas).
        var (requested, other) = TableModeRules.NewTableRows(table, mode, cmd.FloorId, cmd.PosX, cmd.PosY);
        await _tables.AddLayoutAsync(requested, ct);
        await _tables.AddLayoutAsync(other, ct);
        await _tables.SaveChangesAsync(ct);

        _logger.LogInformation(
            "sessions.admin.table.created {TenantId} {TableId} {TableCode} {Zone} {QrMode} {RequiresPin} {Mode} {Kind} {RequesterId}",
            cmd.TenantId, table.Id, table.TableCode, table.Zone, table.QrMode, table.RequiresPin, mode, table.Kind, cmd.RequesterId);

        return Result.Success(new CreateTableDto(
            table.Id, table.TenantId, table.TableCode, table.Zone, table.Active,
            cmd.FloorId, cmd.PosX, cmd.PosY, table.Seats, table.Shape,
            table.QrMode.ToString().ToUpperInvariant(), table.RequiresPin,
            table.CreatedAt, table.UpdatedAt, table.Kind, mode));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE TABLE
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>PRD-019: la posición ya no viaja aquí (se edita por modo con PATCH /tables/layout).</summary>
public record UpdateTableDto(
    Guid Id,
    Guid TenantId,
    string TableCode,
    string? Zone,
    bool Active,
    int? Seats,
    string Shape,
    string QrMode,
    bool RequiresPin,
    string Kind,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <param name="NewTableCode">null = no change.</param>
/// <param name="NewZone">null = no change unless ClearZone = true.</param>
/// <param name="ClearZone">When true, sets zone to null.</param>
/// <param name="Active">null = no change (interruptor global, tables.active).</param>
/// <param name="Kind">PRD-019: TABLE | HIGH_TABLE | RESERVADO; null = no change.</param>
public record UpdateTableCommand(
    Guid TenantId,
    Guid TableId,
    string? NewTableCode,
    string? NewZone,
    bool ClearZone,
    bool? Active,
    Guid RequesterId,
    int? Seats = null,
    string? Shape = null,
    string? QrMode = null,         // "FIXED" | "DYNAMIC"; null = no change
    bool? RequiresPin = null,
    string? Kind = null) : IRequest<Result<UpdateTableDto>>;

public sealed class UpdateTableValidator : AbstractValidator<UpdateTableCommand>
{
    public UpdateTableValidator()
    {
        RuleFor(c => c.TableId).NotEmpty();
        RuleFor(c => c.NewTableCode)
            .MaximumLength(50).When(c => c.NewTableCode is not null)
            .Matches(@"^[a-zA-Z0-9\-]+$").When(c => c.NewTableCode is not null);
        RuleFor(c => c.NewZone)
            .MaximumLength(100).When(c => c.NewZone is not null);
        RuleFor(c => c.Shape)
            .Must(s => s!.ToUpperInvariant() is "SQUARE" or "ROUND").When(c => c.Shape is not null)
            .WithMessage("shape debe ser SQUARE o ROUND.");
        RuleFor(c => c.Seats).GreaterThanOrEqualTo(0).When(c => c.Seats.HasValue);
        RuleFor(c => c.QrMode)
            .Must(q => q!.ToUpperInvariant() is "FIXED" or "DYNAMIC").When(c => c.QrMode is not null)
            .WithMessage("qrMode debe ser FIXED o DYNAMIC.");
        RuleFor(c => c)
            .Must(c => c.NewTableCode is not null || c.NewZone is not null || c.ClearZone || c.Active.HasValue
                || c.Seats.HasValue || c.Shape is not null || c.QrMode is not null || c.RequiresPin.HasValue
                || c.Kind is not null)
            .WithMessage("Body vacío: debe proveer al menos un campo.");
    }
}

public sealed class UpdateTableHandler : IRequestHandler<UpdateTableCommand, Result<UpdateTableDto>>
{
    private readonly ITableRepository _tables;
    private readonly ISessionRepository _sessions;
    private readonly ILogger<UpdateTableHandler> _logger;

    public UpdateTableHandler(
        ITableRepository tables,
        ISessionRepository sessions,
        ILogger<UpdateTableHandler> logger)
    {
        _tables = tables;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<Result<UpdateTableDto>> Handle(UpdateTableCommand cmd, CancellationToken ct)
    {
        string? kind = cmd.Kind?.Trim().ToUpperInvariant();
        if (kind is not null && !DiningTable.Kinds.Contains(kind))
            return Result.Failure<UpdateTableDto>(Error.Validation(TableModeRules.InvalidTableKindCode,
                "kind debe ser TABLE, HIGH_TABLE o RESERVADO."));

        var table = await _tables.GetByIdAsync(cmd.TableId, ct);
        if (table is null || table.TenantId != cmd.TenantId)
            return Result.Failure<UpdateTableDto>(
                Error.NotFound($"Mesa no encontrada."));

        if (cmd.NewTableCode is not null)
        {
            var conflict = await _tables.GetByCodeAsync(cmd.TenantId, cmd.NewTableCode, ct);
            if (conflict is not null && conflict.Id != cmd.TableId)
                return Result.Failure<UpdateTableDto>(
                    Error.Conflict($"Ya existe una mesa con código '{cmd.NewTableCode}'."));
            table.UpdateTableCode(cmd.NewTableCode);
        }

        if (cmd.ClearZone)
            table.UpdateZone(null);
        else if (cmd.NewZone is not null)
            table.UpdateZone(cmd.NewZone);

        if (cmd.Active.HasValue)
        {
            if (!cmd.Active.Value)
            {
                // Block deactivation if table has open session
                var hasOpen = await _sessions.HasOpenByTableIdAsync(table.Id, ct);
                if (hasOpen)
                    return Result.Failure<UpdateTableDto>(
                        Error.Conflict("No se puede desactivar una mesa con una sesión abierta."));
            }
            table.SetActive(cmd.Active.Value);
        }

        if (cmd.Seats.HasValue || cmd.Shape is not null)
            table.SetDetails(cmd.Seats, cmd.Shape);

        if (cmd.QrMode is not null || cmd.RequiresPin.HasValue)
            table.SetQrConfig(
                cmd.QrMode is not null ? TableQrParsing.Parse(cmd.QrMode) : table.QrMode,
                cmd.RequiresPin ?? table.RequiresPin);

        // Se permite con sesión abierta: no reescribe la prioridad de comandas ya creadas.
        if (kind is not null) table.SetKind(kind);

        await _tables.SaveChangesAsync(ct);

        _logger.LogInformation(
            "sessions.admin.table.updated {TenantId} {TableId} {QrMode} {RequiresPin} {Kind} {RequesterId}",
            cmd.TenantId, cmd.TableId, table.QrMode, table.RequiresPin, table.Kind, cmd.RequesterId);

        return Result.Success(new UpdateTableDto(
            table.Id, table.TenantId, table.TableCode, table.Zone, table.Active,
            table.Seats, table.Shape,
            table.QrMode.ToString().ToUpperInvariant(), table.RequiresPin, table.Kind,
            table.CreatedAt, table.UpdatedAt));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GENERATE TABLE QR
// ══════════════════════════════════════════════════════════════════════════════

public record GenerateTableQrCommand(
    Guid TenantId,
    Guid TableId,
    Guid RequesterId) : IRequest<Result<GenerateTableQrDto>>;

public record GenerateTableQrDto(byte[] PngBytes, string TableCode);

public sealed class GenerateTableQrHandler
    : IRequestHandler<GenerateTableQrCommand, Result<GenerateTableQrDto>>
{
    private readonly ITableRepository _tables;
    private readonly IMenuUrlProvider _menuUrls;
    private readonly IQrCodeService _qrCode;
    private readonly IBlobLogoService _blobLogo;
    private readonly ILogger<GenerateTableQrHandler> _logger;

    public GenerateTableQrHandler(
        ITableRepository tables,
        IMenuUrlProvider menuUrls,
        IQrCodeService qrCode,
        IBlobLogoService blobLogo,
        ILogger<GenerateTableQrHandler> logger)
    {
        _tables = tables;
        _menuUrls = menuUrls;
        _qrCode = qrCode;
        _blobLogo = blobLogo;
        _logger = logger;
    }

    public async Task<Result<GenerateTableQrDto>> Handle(
        GenerateTableQrCommand cmd, CancellationToken ct)
    {
        var table = await _tables.GetByIdAsync(cmd.TableId, ct);
        if (table is null || table.TenantId != cmd.TenantId)
            return Result.Failure<GenerateTableQrDto>(Error.NotFound("Mesa no encontrada."));

        if (!table.Active)
            return Result.Failure<GenerateTableQrDto>(
                Error.Validation("TABLE_INACTIVE", "La mesa está inactiva."));

        // PRD-019: la ruta real del menú, por tenantId (el slug desaparece). Base = MENU_PUBLIC_URL
        // (o Frontend:BaseUrl). Tras desplegar hay que regenerar e imprimir los QR.
        string url = $"{_menuUrls.MenuBaseUrl}/mesa?tenant={cmd.TenantId}&table={Uri.EscapeDataString(table.TableCode)}";

        Stream? logoStream = null;
        bool hasLogo = false;
        try
        {
            logoStream = await _blobLogo.GetLogoStreamAsync(cmd.TenantId, ct);
            hasLogo = logoStream is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "sessions.admin.table.qr_logo_fetch_failed {TenantId} {TableId}",
                cmd.TenantId, cmd.TableId);
        }

        byte[] png;
        try
        {
            png = await _qrCode.GenerateAsync(url, logoStream, ct);
        }
        finally
        {
            if (logoStream is not null) await logoStream.DisposeAsync();
        }

        _logger.LogInformation(
            "sessions.admin.table.qr_generated {TenantId} {TableId} {TableCode} {HasLogo} {RequesterId}",
            cmd.TenantId, cmd.TableId, table.TableCode, hasLogo, cmd.RequesterId);

        return Result.Success(new GenerateTableQrDto(png, table.TableCode));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE TABLES LAYOUT — distribución de UN modo (PRD-019, ADR-038)
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Una mesa tocada en el editor. Los campos <c>Has*</c> distinguen «no enviado» (conserva el valor
/// de la fila) de «enviado a null» (<c>floorId</c>/<c>stationId</c> = null quita el piso/la barra).
/// </summary>
public record TableLayoutInput(
    Guid TableId,
    bool HasFloorId, Guid? FloorId,
    double? PosX, double? PosY,
    bool? Active,
    bool HasStationId, Guid? StationId);

public record UpdateTablesLayoutDto(string Mode, int Applied, int Skipped);

public record UpdateTablesLayoutCommand(
    Guid TenantId, string? Mode, List<TableLayoutInput> Tables, Guid RequesterId)
    : IRequest<Result<UpdateTablesLayoutDto>>;

public sealed class UpdateTablesLayoutValidator : AbstractValidator<UpdateTablesLayoutCommand>
{
    public UpdateTablesLayoutValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Tables).NotNull();
        RuleForEach(c => c.Tables).ChildRules(p =>
        {
            p.RuleFor(x => x.TableId).NotEmpty();
            p.RuleFor(x => x.PosX).GreaterThanOrEqualTo(0).When(x => x.PosX.HasValue);
            p.RuleFor(x => x.PosY).GreaterThanOrEqualTo(0).When(x => x.PosY.HasValue);
        });
    }
}

public sealed class UpdateTablesLayoutHandler
    : IRequestHandler<UpdateTablesLayoutCommand, Result<UpdateTablesLayoutDto>>
{
    private readonly ITableRepository _tables;
    private readonly IFloorRepository _floors;
    private readonly ISessionRepository _sessions;
    private readonly IStationReadService _stations;
    private readonly IVenueModeProvider _venueMode;
    private readonly ISignalRNotifier _signalR;
    private readonly ILogger<UpdateTablesLayoutHandler> _logger;

    public UpdateTablesLayoutHandler(
        ITableRepository tables, IFloorRepository floors, ISessionRepository sessions,
        IStationReadService stations, IVenueModeProvider venueMode, ISignalRNotifier signalR,
        ILogger<UpdateTablesLayoutHandler> logger)
    {
        _tables = tables;
        _floors = floors;
        _sessions = sessions;
        _stations = stations;
        _venueMode = venueMode;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<UpdateTablesLayoutDto>> Handle(UpdateTablesLayoutCommand cmd, CancellationToken ct)
    {
        var mode = TableModeRules.ParseMode(cmd.Mode);
        if (mode is null)
            return Result.Failure<UpdateTablesLayoutDto>(
                Error.Validation(TableModeRules.InvalidModeCode, "mode es obligatorio: RESTAURANT o NIGHT."));

        if (cmd.Tables.Count == 0) return Result.Success(new UpdateTablesLayoutDto(mode, 0, 0));

        var activeMode = await _venueMode.GetModeAsync(cmd.TenantId, ct);
        if (activeMode.IsFailure) return Result.Failure<UpdateTablesLayoutDto>(activeMode.Error);
        bool isActiveMode = activeMode.Value == mode;

        // ── Validación completa antes de escribir nada ───────────────────────────
        var floorIds = (await _floors.ListAsync(cmd.TenantId, ct)).Select(f => f.Id).ToHashSet();
        foreach (var fid in cmd.Tables.Where(t => t.HasFloorId && t.FloorId.HasValue).Select(t => t.FloorId!.Value).Distinct())
            if (!floorIds.Contains(fid))
                return Result.Failure<UpdateTablesLayoutDto>(
                    new Error("FLOOR_NOT_FOUND", "Piso no encontrado.", ErrorType.NotFound));

        foreach (var sid in cmd.Tables.Where(t => t.HasStationId && t.StationId.HasValue).Select(t => t.StationId!.Value).Distinct())
        {
            var st = await _stations.GetAsync(sid, ct);
            if (st is null || st.TenantId != cmd.TenantId)
                return Result.Failure<UpdateTablesLayoutDto>(
                    new Error("STATION_NOT_FOUND", "Estación no encontrada.", ErrorType.NotFound));
            if (!st.IsBar || !st.IsOnIn(mode))
                return Result.Failure<UpdateTablesLayoutDto>(Error.Validation("STATION_NOT_BAR",
                    $"La estación «{st.Name}» no es una barra encendida en este modo."));
        }

        var ids = cmd.Tables.Select(p => p.TableId).Distinct().ToList();
        var tables = (await _tables.ListByIdsAsync(cmd.TenantId, ids, ct)).ToDictionary(t => t.Id);
        var rows = (await _tables.ListLayoutsAsync(cmd.TenantId, mode, ct))
            .Where(l => tables.ContainsKey(l.TableId))
            .ToDictionary(l => l.TableId);

        // Apagar en el modo activo una mesa ocupada → 409 (en otro modo surte efecto al cambiar).
        if (isActiveMode && cmd.Tables.Any(t => t.Active == false))
        {
            var occupied = (await _sessions.GetOpenByTenantAsync(cmd.TenantId, ct)).Select(s => s.TableId).ToHashSet();
            var blocked = cmd.Tables
                .Where(t => t.Active == false && tables.ContainsKey(t.TableId) && occupied.Contains(t.TableId))
                .Select(t => tables[t.TableId].TableCode).Distinct().ToList();
            if (blocked.Count > 0)
                return Result.Failure<UpdateTablesLayoutDto>(new Error("TABLE_HAS_ACTIVE_SESSION",
                    "No se puede sacar de servicio una mesa con una sesión abierta.", ErrorType.Conflict,
                    new Dictionary<string, object?> { ["tableCodes"] = blocked }));
        }

        // ── UPSERT por (table_id, mode). tables.floor_id/pos_* no se tocan (deprecadas). ──
        int applied = 0, skipped = 0, deactivated = 0;
        var touched = new List<Guid>();
        foreach (var p in cmd.Tables)
        {
            if (!tables.TryGetValue(p.TableId, out var table)) { skipped++; continue; }

            if (!rows.TryGetValue(p.TableId, out var row))
            {
                row = new TableLayout(cmd.TenantId, table.Id, mode, null, null, null, active: true);
                await _tables.AddLayoutAsync(row, ct);
                rows[p.TableId] = row;
            }

            var floorId = p.HasFloorId ? p.FloorId : row.FloorId;
            var posX = p.PosX ?? row.PosX;
            var posY = p.PosY ?? row.PosY;
            if (floorId != row.FloorId || posX != row.PosX || posY != row.PosY)
                row.SetPlacement(floorId, posX, posY);

            var active = p.Active ?? true;
            if (active != row.Active)
            {
                row.SetActive(active);
                if (!active) deactivated++;
            }

            if (p.HasStationId && p.StationId != row.StationId)
                row.SetStation(p.StationId);

            applied++;
            touched.Add(table.Id);
        }

        if (applied > 0) await _tables.SaveChangesAsync(ct);

        _logger.LogInformation(
            "sessions.admin.tables.layout_saved {TenantId} {Mode} {Applied} {Skipped} {Deactivated} {RequesterId}",
            cmd.TenantId, mode, applied, skipped, deactivated, cmd.RequesterId);
        SessionsMetrics.TablesLayoutSaved.Add(1, new KeyValuePair<string, object?>("mode", mode));

        if (isActiveMode && applied > 0)
        {
            try
            {
                await _signalR.NotifyWaitersAsync(cmd.TenantId,
                    new { type = "MAP_UPDATED", mode, tableIds = touched }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sessions.admin.tables.map_updated_notify_failed {TenantId}", cmd.TenantId);
            }
        }

        return Result.Success(new UpdateTablesLayoutDto(mode, applied, skipped));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DELETE TABLE
// ══════════════════════════════════════════════════════════════════════════════

public record DeleteTableCommand(Guid TenantId, Guid TableId, Guid RequesterId)
    : IRequest<Result<bool>>;

public sealed class DeleteTableHandler : IRequestHandler<DeleteTableCommand, Result<bool>>
{
    private readonly ITableRepository _tables;
    private readonly ISessionRepository _sessions;
    private readonly ILogger<DeleteTableHandler> _logger;

    public DeleteTableHandler(ITableRepository tables, ISessionRepository sessions, ILogger<DeleteTableHandler> logger)
    {
        _tables = tables;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteTableCommand cmd, CancellationToken ct)
    {
        var table = await _tables.GetByIdAsync(cmd.TableId, ct);
        if (table is null || table.TenantId != cmd.TenantId)
            return Result.Failure<bool>(Error.NotFound("Mesa no encontrada."));

        if (await _sessions.HasOpenByTableIdAsync(table.Id, ct))
            return Result.Failure<bool>(
                Error.Conflict("No se puede eliminar una mesa con una sesión abierta."));

        // No FK from sessions.table_id → tables (table_code is denormalized on sessions),
        // so closed-session history survives the table deletion.
        await _tables.RemoveAsync(table, ct);
        await _tables.SaveChangesAsync(ct);

        _logger.LogInformation("sessions.admin.table.deleted {TenantId} {TableId} {RequesterId}",
            cmd.TenantId, cmd.TableId, cmd.RequesterId);

        return Result.Success(true);
    }
}
