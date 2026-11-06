using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MestoBeach.Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev";

        var opts = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly("MestoBeach.Payments.Infrastructure"))
            .Options;

        return new PaymentsDbContext(opts);
    }
}
