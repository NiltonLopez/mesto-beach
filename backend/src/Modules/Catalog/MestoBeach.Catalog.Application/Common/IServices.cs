namespace MestoBeach.Catalog.Application.Common;

/// <summary>Redis cache for the full tenant catalog document.</summary>
public interface ICatalogCacheService
{
    Task<string?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task SetAsync(Guid tenantId, string json, CancellationToken ct = default);
    Task InvalidateAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Azure Blob Storage operations for catalog images.</summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads a processed image stream.
    /// Returns the public URL of the uploaded blob.
    /// </summary>
    Task<string> UploadAsync(
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Deletes a blob by path. Best-effort — does not throw on 404.</summary>
    Task DeleteAsync(string blobPath, CancellationToken ct = default);

    /// <summary>Deletes a blob given its public URL (ignores URLs we don't own, e.g. legacy data URLs).</summary>
    Task DeleteByUrlAsync(string url, CancellationToken ct = default);

    /// <summary>Builds the public URL for a blob path without uploading.</summary>
    string GetUrl(string blobPath);
}

/// <summary>Converts and resizes images using SixLabors.ImageSharp.</summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Converts the input stream to WebP at 800×600.
    /// Returns a new MemoryStream ready for upload.
    /// </summary>
    Task<Stream> ResizeMainAsync(Stream input, CancellationToken ct = default);

    /// <summary>
    /// Converts to WebP at up to 2048×1152 for full-bleed hero images
    /// (a menu cover is displayed edge to edge, so 800 px pixelates on modern phones).
    /// </summary>
    Task<Stream> ResizeHeroAsync(Stream input, CancellationToken ct = default);

    /// <summary>Converts to WebP at 200×150 (thumbnail).</summary>
    Task<Stream> ResizeThumbnailAsync(Stream input, CancellationToken ct = default);

    /// <summary>
    /// Converts to lossless WebP at up to 1024×1024, preserving transparency —
    /// logos are line art and a 200 px thumbnail renders them jagged.
    /// </summary>
    Task<Stream> ResizeLogoAsync(Stream input, CancellationToken ct = default);
}
