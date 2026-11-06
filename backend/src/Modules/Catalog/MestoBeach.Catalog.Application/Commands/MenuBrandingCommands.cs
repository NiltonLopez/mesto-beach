using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// ─── DTO ──────────────────────────────────────────────────────────────────────

public sealed record MenuBrandingDto(
    string DisplayName,
    string Slogan,
    string? LogoUrl,
    string? HeroUrl,
    string PrimaryColor,
    string Theme,
    string? BackgroundColor,
    string FontKey);

internal static class BrandingMapping
{
    public static MenuBrandingDto ToDto(MenuBranding b) =>
        new(b.DisplayName, b.Slogan, b.LogoUrl, b.HeroUrl, b.PrimaryColor, b.Theme, b.BackgroundColor, b.FontKey);
}

// ─── Get ──────────────────────────────────────────────────────────────────────

public sealed record GetMenuBrandingQuery(Guid TenantId, Guid RequesterId)
    : IRequest<Result<MenuBrandingDto>>;

public sealed class GetMenuBrandingQueryHandler
    : IRequestHandler<GetMenuBrandingQuery, Result<MenuBrandingDto>>
{
    private readonly ICatalogRepository _repo;
    public GetMenuBrandingQueryHandler(ICatalogRepository repo) => _repo = repo;

    public async Task<Result<MenuBrandingDto>> Handle(GetMenuBrandingQuery q, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(q.TenantId, ct);
        var branding = catalog?.Branding ?? new MenuBranding();
        return Result.Success(BrandingMapping.ToDto(branding));
    }
}

// ─── Update (text / colors / theme / font) ──────────────────────────────────────

public sealed record UpdateMenuBrandingCommand(
    Guid TenantId,
    string DisplayName,
    string Slogan,
    string PrimaryColor,
    string Theme,
    string? BackgroundColor,
    string FontKey,
    Guid RequesterId) : IRequest<Result<MenuBrandingDto>>;

public sealed class UpdateMenuBrandingValidator : AbstractValidator<UpdateMenuBrandingCommand>
{
    // "night" (ADR-039): preset oscuro del modo NIGHT.
    internal static readonly string[] Themes = ["light", "cream", "dark", "night"];

    public UpdateMenuBrandingValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(80);
        RuleFor(x => x.Slogan).MaximumLength(140);
        RuleFor(x => x.PrimaryColor)
            .Matches("^#[0-9a-fA-F]{6}$")
            .WithMessage("primaryColor debe ser un hex #RRGGBB.");
        RuleFor(x => x.Theme)
            .Must(t => Themes.Contains(t))
            .WithMessage("theme debe ser light, cream, dark o night.");
        // Optional custom background: empty/null clears it, otherwise must be a hex.
        RuleFor(x => x.BackgroundColor)
            .Matches("^#[0-9a-fA-F]{6}$")
            .When(x => !string.IsNullOrWhiteSpace(x.BackgroundColor))
            .WithMessage("backgroundColor debe ser un hex #RRGGBB.");
        RuleFor(x => x.FontKey).NotEmpty().MaximumLength(40);
    }
}

public sealed class UpdateMenuBrandingHandler
    : IRequestHandler<UpdateMenuBrandingCommand, Result<MenuBrandingDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<UpdateMenuBrandingHandler> _log;

    public UpdateMenuBrandingHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<UpdateMenuBrandingHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<MenuBrandingDto>> Handle(UpdateMenuBrandingCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var current = catalog?.Branding ?? new MenuBranding();

        // Update editable fields; keep existing images (managed via upload endpoints).
        var updated = new MenuBranding
        {
            DisplayName = cmd.DisplayName.Trim(),
            Slogan = cmd.Slogan.Trim(),
            LogoUrl = current.LogoUrl,
            HeroUrl = current.HeroUrl,
            PrimaryColor = cmd.PrimaryColor.ToLowerInvariant(),
            Theme = cmd.Theme,
            BackgroundColor = string.IsNullOrWhiteSpace(cmd.BackgroundColor)
                ? null
                : cmd.BackgroundColor.ToLowerInvariant(),
            FontKey = cmd.FontKey,
        };

        await _repo.UpdateBrandingAsync(cmd.TenantId, updated, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.branding.updated tenantId={TenantId} theme={Theme} font={Font} requesterId={RequesterId}",
            cmd.TenantId, updated.Theme, updated.FontKey, cmd.RequesterId);

        return Result.Success(BrandingMapping.ToDto(updated));
    }
}

// ─── Upload image (logo | hero) → resized webp data URL ─────────────────────────

public sealed record UploadBrandingImageCommand(
    Guid TenantId,
    string Kind,            // "logo" | "hero"
    Stream FileStream,
    string ContentType,
    long FileSizeBytes,
    Guid RequesterId) : IRequest<Result<MenuBrandingDto>>;

public sealed class UploadBrandingImageHandler
    : IRequestHandler<UploadBrandingImageCommand, Result<MenuBrandingDto>>
{
    private const long MaxBytes = 4 * 1024 * 1024; // 4 MB input
    private static readonly HashSet<string> AllowedTypes =
        new(["image/jpeg", "image/png", "image/webp"], StringComparer.OrdinalIgnoreCase);

    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IImageProcessingService _imaging;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<UploadBrandingImageHandler> _log;

    public UploadBrandingImageHandler(ICatalogRepository repo, ICatalogCacheService cache,
        IImageProcessingService imaging, IBlobStorageService blob, ILogger<UploadBrandingImageHandler> log)
    { _repo = repo; _cache = cache; _imaging = imaging; _blob = blob; _log = log; }

    public async Task<Result<MenuBrandingDto>> Handle(UploadBrandingImageCommand cmd, CancellationToken ct)
    {
        var kind = cmd.Kind?.ToLowerInvariant();
        if (kind is not ("logo" or "hero"))
            return Result.Failure<MenuBrandingDto>(Error.Validation("Branding.InvalidKind", "kind debe ser logo o hero."));

        if (!AllowedTypes.Contains(cmd.ContentType))
            return Result.Failure<MenuBrandingDto>(
                Error.Validation("Image.InvalidFormat", "Formato no soportado. Use JPG, PNG o WebP."));
        if (cmd.FileSizeBytes > MaxBytes)
            return Result.Failure<MenuBrandingDto>(
                Error.Validation("Image.TooLarge", "El archivo excede el límite de 4 MB."));

        // Resize (logos use the thumbnail size, heroes the main size) and upload to blob storage.
        // Unique path per upload → no CDN/browser cache staleness; the old blob is removed below.
        string url;
        try
        {
            await using var resized = kind == "logo"
                ? await _imaging.ResizeLogoAsync(cmd.FileStream, ct)
                : await _imaging.ResizeHeroAsync(cmd.FileStream, ct);
            var path = $"branding/{cmd.TenantId}/{kind}-{Guid.NewGuid():N}.webp";
            url = await _blob.UploadAsync(path, resized, "image/webp", ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "catalog.admin.branding.image_upload_failed tenantId={TenantId} kind={Kind}", cmd.TenantId, kind);
            return Result.Failure<MenuBrandingDto>(Error.Failure("Image.UploadFailed", "No se pudo subir la imagen."));
        }

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var current = catalog?.Branding ?? new MenuBranding();
        var oldUrl = kind == "logo" ? current.LogoUrl : current.HeroUrl;
        var updated = new MenuBranding
        {
            DisplayName = current.DisplayName,
            Slogan = current.Slogan,
            LogoUrl = kind == "logo" ? url : current.LogoUrl,
            HeroUrl = kind == "hero" ? url : current.HeroUrl,
            PrimaryColor = current.PrimaryColor,
            Theme = current.Theme,
            BackgroundColor = current.BackgroundColor,
            FontKey = current.FontKey,
        };

        await _repo.UpdateBrandingAsync(cmd.TenantId, updated, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        if (!string.IsNullOrEmpty(oldUrl)) await _blob.DeleteByUrlAsync(oldUrl, ct);

        _log.LogInformation(
            "catalog.admin.branding.image_uploaded tenantId={TenantId} kind={Kind} bytes={Bytes} requesterId={RequesterId}",
            cmd.TenantId, kind, cmd.FileSizeBytes, cmd.RequesterId);

        return Result.Success(BrandingMapping.ToDto(updated));
    }
}

// ─── Delete image (logo | hero) ─────────────────────────────────────────────────

public sealed record DeleteBrandingImageCommand(Guid TenantId, string Kind, Guid RequesterId)
    : IRequest<Result<MenuBrandingDto>>;

public sealed class DeleteBrandingImageHandler
    : IRequestHandler<DeleteBrandingImageCommand, Result<MenuBrandingDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IBlobStorageService _blob;
    public DeleteBrandingImageHandler(ICatalogRepository repo, ICatalogCacheService cache, IBlobStorageService blob)
    { _repo = repo; _cache = cache; _blob = blob; }

    public async Task<Result<MenuBrandingDto>> Handle(DeleteBrandingImageCommand cmd, CancellationToken ct)
    {
        var kind = cmd.Kind?.ToLowerInvariant();
        if (kind is not ("logo" or "hero"))
            return Result.Failure<MenuBrandingDto>(Error.Validation("Branding.InvalidKind", "kind debe ser logo o hero."));

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var current = catalog?.Branding ?? new MenuBranding();
        var oldUrl = kind == "logo" ? current.LogoUrl : current.HeroUrl;
        var updated = new MenuBranding
        {
            DisplayName = current.DisplayName,
            Slogan = current.Slogan,
            LogoUrl = kind == "logo" ? null : current.LogoUrl,
            HeroUrl = kind == "hero" ? null : current.HeroUrl,
            PrimaryColor = current.PrimaryColor,
            Theme = current.Theme,
            BackgroundColor = current.BackgroundColor,
            FontKey = current.FontKey,
        };
        await _repo.UpdateBrandingAsync(cmd.TenantId, updated, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);
        if (!string.IsNullOrEmpty(oldUrl)) await _blob.DeleteByUrlAsync(oldUrl, ct);
        return Result.Success(BrandingMapping.ToDto(updated));
    }
}
