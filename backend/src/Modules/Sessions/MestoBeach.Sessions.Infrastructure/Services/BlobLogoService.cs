using Azure.Storage.Blobs;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class BlobLogoService : IBlobLogoService
{
    private readonly BlobServiceClient _blobClient;
    private readonly SessionsOptions _opts;
    private readonly ILogger<BlobLogoService> _logger;

    public BlobLogoService(
        BlobServiceClient blobClient,
        IOptions<SessionsOptions> opts,
        ILogger<BlobLogoService> logger)
    {
        _blobClient = blobClient;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<Stream?> GetLogoStreamAsync(Guid tenantId, CancellationToken ct)
    {
        var container = _blobClient.GetBlobContainerClient(_opts.BlobContainerName);
        var blob = container.GetBlobClient($"{tenantId}/logo.png");

        if (!await blob.ExistsAsync(ct))
            return null;

        var ms = new MemoryStream();
        await blob.DownloadToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}
