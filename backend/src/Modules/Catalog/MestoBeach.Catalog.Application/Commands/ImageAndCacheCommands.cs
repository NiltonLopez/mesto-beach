using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// ─── Upload Image ─────────────────────────────────────────────────────────────

public sealed record UploadImageCommand(
    Guid TenantId,
    Guid ItemId,
    Stream FileStream,
    string ContentType,
    long FileSizeBytes,
    Guid RequesterId
) : IRequest<Result<UploadImageResponse>>;

public sealed record UploadImageResponse(
    int ImageIndex,
    string Url,
    string ThumbnailUrl,
    int TotalImages
);

public sealed class UploadImageCommandHandler
    : IRequestHandler<UploadImageCommand, Result<UploadImageResponse>>
{
    private const int MaxImages = 3;
    private const long MaxBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly HashSet<string> AllowedTypes =
        new(["image/jpeg", "image/png", "image/webp"], StringComparer.OrdinalIgnoreCase);

    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IBlobStorageService _blob;
    private readonly IImageProcessingService _imaging;
    private readonly ILogger<UploadImageCommandHandler> _log;

    public UploadImageCommandHandler(
        ICatalogRepository repo, ICatalogCacheService cache,
        IBlobStorageService blob, IImageProcessingService imaging,
        ILogger<UploadImageCommandHandler> log)
    { _repo = repo; _cache = cache; _blob = blob; _imaging = imaging; _log = log; }

    public async Task<Result<UploadImageResponse>> Handle(
        UploadImageCommand cmd, CancellationToken ct)
    {
        if (!AllowedTypes.Contains(cmd.ContentType))
            return Result.Failure<UploadImageResponse>(
                Error.Validation("Image.InvalidFormat",
                    "Formato de archivo no soportado. Use JPG, PNG o WebP."));

        if (cmd.FileSizeBytes > MaxBytes)
            return Result.Failure<UploadImageResponse>(
                Error.Validation("Image.TooLarge", "El archivo excede el límite de 2 MB."));

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var item = catalog?.Items.FirstOrDefault(i => i.Id == cmd.ItemId && !i.IsDeleted);

        if (item is null)
            return Result.Failure<UploadImageResponse>(
                Error.NotFound("Item no encontrado o eliminado."));

        if (item.Images.Count >= MaxImages)
            return Result.Failure<UploadImageResponse>(
                Error.Validation("Image.MaxReached",
                    "El item ya tiene 3 imágenes (máximo alcanzado)."));

        var index = item.Images.Count;
        var mainPath = $"catalog-images/{cmd.TenantId}/{cmd.ItemId}/{index}.webp";
        var thumbPath = $"catalog-images/{cmd.TenantId}/{cmd.ItemId}/{index}_thumb.webp";

        // Process both resolutions
        await using var mainStream = await _imaging.ResizeMainAsync(cmd.FileStream, ct);
        // Reset original stream for thumbnail; ImageSharp consumes the stream,
        // so we use the already-resized main as the source for the thumbnail.
        // Actually, we need to re-process from original. Use a MemoryStream buffer.
        cmd.FileStream.Position = 0;
        await using var thumbStream = await _imaging.ResizeThumbnailAsync(cmd.FileStream, ct);

        // Upload to Azure Blob
        string mainUrl, thumbUrl;
        try
        {
            mainUrl = await _blob.UploadAsync(mainPath, mainStream, "image/webp", ct);
            thumbUrl = await _blob.UploadAsync(thumbPath, thumbStream, "image/webp", ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "catalog.admin.item.image_upload_failed tenantId={TenantId} itemId={ItemId} requesterId={RequesterId}",
                cmd.TenantId, cmd.ItemId, cmd.RequesterId);
            return Result.Failure<UploadImageResponse>(
                Error.Failure("Blob.UploadFailed", "Error al subir la imagen al almacenamiento."));
        }

        await _repo.AddImageAsync(cmd.TenantId, cmd.ItemId, mainUrl, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.item.image_uploaded tenantId={TenantId} itemId={ItemId} imageIndex={Index} sizeBytes={Size} requesterId={RequesterId}",
            cmd.TenantId, cmd.ItemId, index, cmd.FileSizeBytes, cmd.RequesterId);

        return Result.Success(new UploadImageResponse(index, mainUrl, thumbUrl, index + 1));
    }
}

// ─── Delete Image ─────────────────────────────────────────────────────────────

public sealed record DeleteImageCommand(
    Guid TenantId, Guid ItemId, int ImageIndex, Guid RequesterId
) : IRequest<Result<DeleteImageResponse>>;

public sealed record DeleteImageResponse(IReadOnlyList<string> RemainingImages, int TotalImages);

public sealed class DeleteImageCommandHandler
    : IRequestHandler<DeleteImageCommand, Result<DeleteImageResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<DeleteImageCommandHandler> _log;

    public DeleteImageCommandHandler(
        ICatalogRepository repo, ICatalogCacheService cache,
        IBlobStorageService blob, ILogger<DeleteImageCommandHandler> log)
    { _repo = repo; _cache = cache; _blob = blob; _log = log; }

    public async Task<Result<DeleteImageResponse>> Handle(
        DeleteImageCommand cmd, CancellationToken ct)
    {
        if (cmd.ImageIndex is < 0 or > 2)
            return Result.Failure<DeleteImageResponse>(
                Error.Validation("Image.InvalidIndex", "imageIndex debe ser 0, 1 o 2."));

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var item = catalog?.Items.FirstOrDefault(i => i.Id == cmd.ItemId && !i.IsDeleted);

        if (item is null)
            return Result.Failure<DeleteImageResponse>(
                Error.NotFound("Item no encontrado o eliminado."));

        if (cmd.ImageIndex >= item.Images.Count)
            return Result.Failure<DeleteImageResponse>(
                Error.Validation("Image.IndexOutOfRange",
                    "imageIndex supera el total de imágenes del item."));

        // Best-effort blob deletion
        var mainPath = $"catalog-images/{cmd.TenantId}/{cmd.ItemId}/{cmd.ImageIndex}.webp";
        var thumbPath = $"catalog-images/{cmd.TenantId}/{cmd.ItemId}/{cmd.ImageIndex}_thumb.webp";

        try
        {
            await _blob.DeleteAsync(mainPath, ct);
            await _blob.DeleteAsync(thumbPath, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "catalog.admin.item.image_blob_delete_failed tenantId={TenantId} itemId={ItemId} imageIndex={Index} blobPath={Path}",
                cmd.TenantId, cmd.ItemId, cmd.ImageIndex, mainPath);
        }

        await _repo.RemoveImageAsync(cmd.TenantId, cmd.ItemId, cmd.ImageIndex, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.item.image_deleted tenantId={TenantId} itemId={ItemId} imageIndex={Index} requesterId={RequesterId}",
            cmd.TenantId, cmd.ItemId, cmd.ImageIndex, cmd.RequesterId);

        // Return updated images list
        var remaining = item.Images.Where((_, i) => i != cmd.ImageIndex).ToList();
        return Result.Success(new DeleteImageResponse(remaining, remaining.Count));
    }
}

// ─── Force Cache Invalidation ─────────────────────────────────────────────────

public sealed record InvalidateCatalogCacheCommand(Guid TenantId, Guid RequesterId)
    : IRequest<Result<InvalidateCacheResponse>>;

public sealed record InvalidateCacheResponse(
    string Message, string Key, DateTime Timestamp
);

public sealed class InvalidateCatalogCacheCommandHandler
    : IRequestHandler<InvalidateCatalogCacheCommand, Result<InvalidateCacheResponse>>
{
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<InvalidateCatalogCacheCommandHandler> _log;

    public InvalidateCatalogCacheCommandHandler(
        ICatalogCacheService cache,
        ILogger<InvalidateCatalogCacheCommandHandler> log)
    { _cache = cache; _log = log; }

    public async Task<Result<InvalidateCacheResponse>> Handle(
        InvalidateCatalogCacheCommand cmd, CancellationToken ct)
    {
        await _cache.InvalidateAsync(cmd.TenantId, ct);
        var key = $"catalog:{cmd.TenantId}";

        _log.LogInformation(
            "catalog.ops.cache_invalidated tenantId={TenantId} requesterId={RequesterId} keyDeleted={Key}",
            cmd.TenantId, cmd.RequesterId, key);

        return Result.Success(new InvalidateCacheResponse(
            "Cache invalidado correctamente.", key, DateTime.UtcNow));
    }
}
