using MestoBeach.Sessions.Application.Common;
using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>Reads the cashier shift state (shifts table owned by Payments) via a read-only projection.</summary>
public sealed class ShiftReaderService : IShiftReader
{
    private readonly SessionsDbContext _db;

    public ShiftReaderService(SessionsDbContext db) => _db = db;

    public Task<bool> HasOpenShiftAsync(Guid tenantId, CancellationToken ct) =>
        _db.Shifts.AnyAsync(s => s.TenantId == tenantId && s.Status == "Open", ct);
}
