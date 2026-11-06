using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MestoBeach.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var conn = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev";

        var opts = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(conn, b => b.MigrationsAssembly("MestoBeach.Orders.Infrastructure"))
            .Options;

        return new OrdersDbContext(opts);
    }
}
