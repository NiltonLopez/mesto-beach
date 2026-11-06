using Microsoft.EntityFrameworkCore;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Infrastructure.Persistence;

namespace MestoBeach.Identity.Infrastructure.Services;

/// <summary>
/// Lectura de sectores. Solo responde "existe / no existe" para el tenant: Identity no necesita
/// —ni debe— saber nada más del plano, que es cosa de Sessions.
/// </summary>
public sealed class FloorReadService : IFloorReadService
{
    private readonly IdentityDbContext _db;

    public FloorReadService(IdentityDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid tenantId, Guid floorId, CancellationToken ct = default) =>
        _db.Floors.AsNoTracking().AnyAsync(f => f.Id == floorId && f.TenantId == tenantId, ct);
}

/// <summary>Lectura de estaciones de preparación (módulo Orders).</summary>
public sealed class StationReadService : IStationReadService
{
    private readonly IdentityDbContext _db;

    public StationReadService(IdentityDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid tenantId, Guid stationId, CancellationToken ct = default) =>
        _db.KdsStations.AsNoTracking().AnyAsync(s => s.Id == stationId && s.TenantId == tenantId, ct);
}

/// <summary>
/// Lectura de viajes de entrega (módulo Orders). El literal "OPEN" es el mismo que persiste
/// Orders (<c>DeliveryTripStatus.Open.ToString().ToUpperInvariant()</c>) y el mismo que usa su
/// índice único parcial <c>ux_delivery_trips_runner_open</c>.
/// </summary>
public sealed class DeliveryTripReadService : IDeliveryTripReadService
{
    private const string EstadoAbierto = "OPEN";

    private readonly IdentityDbContext _db;

    public DeliveryTripReadService(IdentityDbContext db) => _db = db;

    public Task<bool> HasOpenTripAsync(Guid tenantId, Guid runnerId, CancellationToken ct = default) =>
        _db.DeliveryTrips.AsNoTracking()
            .AnyAsync(t => t.TenantId == tenantId
                        && t.RunnerId == runnerId
                        && t.Status == EstadoAbierto, ct);
}
