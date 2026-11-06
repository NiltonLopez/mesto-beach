namespace MestoBeach.Orders.Infrastructure.Configuration;

public sealed class OrdersCatalogOptions
{
    public const string SectionName = "Catalog";   // same section as Catalog module

    public string MongoConnectionString { get; set; } = string.Empty;
    public string MongoDatabaseName { get; set; } = "mestobeach_catalog";
    public string MongoCollectionName { get; set; } = "catalogs";
}
