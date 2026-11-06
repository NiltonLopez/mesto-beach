using MestoBeach.Catalog.Application.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MestoBeach.Catalog.Infrastructure.Services;

public sealed class ImageProcessingService : IImageProcessingService
{
    private static readonly WebpEncoder Encoder = new() { Quality = 82 };
    /// Calidad algo mayor para la portada: ocupa la pantalla completa y los
    /// artefactos de compresión se notan mucho más que en una miniatura.
    private static readonly WebpEncoder HeroEncoder = new() { Quality = 86 };

    /// Sin pérdida y con alfa: un logo suele ser trazo o texto sobre fondo
    /// transparente, donde la compresión con pérdida ensucia los bordes.
    private static readonly WebpEncoder LogoEncoder = new()
    {
        // Sin pérdida: preserva la transparencia y los bordes del trazo.
        FileFormat = WebpFileFormatType.Lossless
    };

    public async Task<Stream> ResizeLogoAsync(Stream input, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(input, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(1024, 1024),
            Mode = ResizeMode.Max
        }));

        var output = new MemoryStream();
        await image.SaveAsync(output, LogoEncoder, ct);
        output.Position = 0;
        return output;
    }

    public async Task<Stream> ResizeHeroAsync(Stream input, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(input, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            // A pantalla completa en un móvil moderno (3x DPR) hacen falta ~1200 px
            // reales; 2048 cubre además tablets y pantallas de escritorio.
            Size = new Size(2048, 1152),
            Mode = ResizeMode.Max
        }));

        var output = new MemoryStream();
        await image.SaveAsync(output, HeroEncoder, ct);
        output.Position = 0;
        return output;
    }

    public async Task<Stream> ResizeMainAsync(Stream input, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(input, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(800, 600),
            Mode = ResizeMode.Max
        }));

        var output = new MemoryStream();
        await image.SaveAsync(output, Encoder, ct);
        output.Position = 0;
        return output;
    }

    public async Task<Stream> ResizeThumbnailAsync(Stream input, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(input, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(200, 150),
            Mode = ResizeMode.Max
        }));

        var output = new MemoryStream();
        await image.SaveAsync(output, Encoder, ct);
        output.Position = 0;
        return output;
    }
}
