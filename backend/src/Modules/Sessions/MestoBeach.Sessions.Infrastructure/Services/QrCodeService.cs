using MestoBeach.Sessions.Application.Common;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class QrCodeService : IQrCodeService
{
    private const int QrSize = 1000;
    private const int LogoMaxSize = 200; // max logo dimension in pixels

    public async Task<byte[]> GenerateAsync(string url, Stream? logoStream, CancellationToken ct)
    {
        // Generate QR bitmap using QRCoder (ECCLevel H for logo overlay tolerance)
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        using var qrCode = new PngByteQRCode(qrData);
        byte[] qrPng = qrCode.GetGraphic(10); // 10 px per module

        // Load QR as ImageSharp image and resize to target
        using var qrImage = Image.Load(qrPng);
        qrImage.Mutate(x => x.Resize(QrSize, QrSize));

        // Overlay logo if provided
        if (logoStream is not null)
        {
            using var logo = await Image.LoadAsync(logoStream, ct);

            // Scale logo to fit within LogoMaxSize × LogoMaxSize
            int logoW = logo.Width;
            int logoH = logo.Height;
            if (logoW > LogoMaxSize || logoH > LogoMaxSize)
            {
                double scale = Math.Min((double)LogoMaxSize / logoW, (double)LogoMaxSize / logoH);
                logoW = (int)(logoW * scale);
                logoH = (int)(logoH * scale);
                logo.Mutate(x => x.Resize(logoW, logoH));
            }

            // Center the logo on the QR code
            int x = (QrSize - logoW) / 2;
            int y = (QrSize - logoH) / 2;

            qrImage.Mutate(ctx => ctx.DrawImage(logo, new Point(x, y), opacity: 1f));
        }

        // Export to PNG bytes
        using var ms = new MemoryStream();
        await qrImage.SaveAsync(ms, new PngEncoder(), ct);
        return ms.ToArray();
    }
}
