using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MestoBeach.Identity.Infrastructure.Persistence;

/// <summary>
/// Used only by EF Core tooling (dotnet ef migrations add / update-database).
/// Not registered in the DI container.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        // EF tooling uses this to generate migrations without a running host.
        // Override via environment variable or local appsettings.
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev";

        var opts = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly("MestoBeach.Identity.Infrastructure"))
            .Options;

        return new IdentityDbContext(opts);
    }
}
