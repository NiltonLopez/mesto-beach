using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MestoBeach.Sessions.Infrastructure.Persistence;

/// <summary>Design-time factory for EF Core migrations.</summary>
public sealed class SessionsDbContextFactory : IDesignTimeDbContextFactory<SessionsDbContext>
{
    public SessionsDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var conn = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev";

        var opts = new DbContextOptionsBuilder<SessionsDbContext>()
            .UseNpgsql(conn, b => b.MigrationsAssembly("MestoBeach.Sessions.Infrastructure"))
            .Options;

        return new SessionsDbContext(opts);
    }
}
