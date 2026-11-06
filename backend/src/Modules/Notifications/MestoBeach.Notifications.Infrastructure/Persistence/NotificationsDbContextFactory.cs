using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MestoBeach.Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev";

        var opts = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly("MestoBeach.Notifications.Infrastructure"))
            .Options;

        return new NotificationsDbContext(opts);
    }
}
