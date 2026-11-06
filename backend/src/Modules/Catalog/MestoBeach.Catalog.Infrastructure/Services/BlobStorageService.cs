using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MestoBeach.Catalog.Infrastructure.Services;

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly BlobStorageOptions _opts;
    private readonly ILogger<BlobStorageService> _log;

    public BlobStorageService(IOptions<BlobStorageOptions> opts, ILogger<BlobStorageService> log)
    {
        _opts = opts.Value;
        _log = log;
        _client = new BlobServiceClient(_opts.ConnectionString);
    }

    public async Task<string> UploadAsync(
        string blobPath, Stream content, string contentType, CancellationToken ct)
    {
        var container = _client.GetBlobContainerClient(_opts.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        var blobClient = container.GetBlobClient(blobPath);
        // Overwrite if it already exists (fixed-path uploads like branding replace in place).
        await blobClient.UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken: ct);

        return GetUrl(blobPath);
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_opts.ContainerName);
            await container.DeleteBlobIfExistsAsync(blobPath, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "blob.delete_failed path={Path}", blobPath);
        }
    }

    public async Task DeleteByUrlAsync(string url, CancellationToken ct)
    {
        var prefix = _opts.BaseUrl.TrimEnd('/') + "/";
        if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return; // not a blob we own (e.g. a legacy data URL) — nothing to delete
        var path = url[prefix.Length..].Split('?')[0];
        await DeleteAsync(path, ct);
    }

    public string GetUrl(string blobPath) =>
        $"{_opts.BaseUrl.TrimEnd('/')}/{blobPath}";
}
