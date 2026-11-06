namespace MestoBeach.Catalog.Infrastructure.Configuration;

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";
    public string MongoConnectionString { get; set; } = string.Empty;
    public string MongoDatabaseName { get; set; } = "mestobeach_catalog";
    public string MongoCollectionName { get; set; } = "catalogs";
}

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "catalog-images";
    /// <summary>Base URL for public blob access, e.g. https://account.blob.core.windows.net/catalog-images</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
