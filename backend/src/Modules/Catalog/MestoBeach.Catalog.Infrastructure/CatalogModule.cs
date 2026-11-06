using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Catalog.Infrastructure.Configuration;
using MestoBeach.Catalog.Infrastructure.Persistence;
using MestoBeach.Catalog.Infrastructure.Services;
using MestoBeach.Shared.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MestoBeach.Catalog.Infrastructure;

public static class CatalogModule
{
    private const string LocalMongoFallback =
        "mongodb://mestobeach:mestobeach_dev@localhost:27017/mestobeach_catalog?authSource=admin";

    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Options
        services.Configure<CatalogOptions>(config.GetSection(CatalogOptions.SectionName));
        services.Configure<BlobStorageOptions>(config.GetSection(BlobStorageOptions.SectionName));

        // MongoDB client (singleton — thread-safe per MongoDB docs)
        services.AddSingleton<IMongoClient>(_ =>
        {
            var connStr = config.GetValue<string>("Catalog:MongoConnectionString")
                ?? config.GetConnectionString("MongoDB")
                ?? LocalMongoFallback;
            return new MongoClient(connStr);
        });

        // Repositories
        services.AddScoped<ICatalogRepository, CatalogRepository>();

        // Services
        services.AddScoped<ICatalogCacheService, CatalogCacheService>();
        services.AddScoped<IBlobStorageService, BlobStorageService>();
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();

        // MediatR handlers from Application layer
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(Application.Commands.CreateCategoryCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(Application.Commands.CreateCategoryCommand).Assembly);

        return services;
    }
}
