using FluentValidation;
using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// Branding Night (ADR-039 / ADR-041, SDD-002 §Branding Night). Las escrituras de Night solo tocan
// branding.night (UpdateNightBrandingAsync); las de día nunca lo tocan.

/// <summary>Subdocumento <c>night</c> crudo: null = hereda del día (salvo backgroundColor: usar el tema).</summary>
public sealed record NightBrandingDto(
    bool Enabled,
    string? DisplayName,
    string? Slogan,
    string? LogoUrl,
    string? HeroUrl,
    string? PrimaryColor,
    string? Theme,
    string? BackgroundColor,
    string? FontKey);

/// <summary><c>night</c> guardado + <c>effective</c> (vista previa de noche, independiente de enabled).</summary>
public sealed record NightBrandingResponse(NightBrandingDto Night, MenuBrandingDto Effective);

internal static class NightBrandingMapping
{
    public static NightBrandingResponse ToResponse(MenuBranding day)
    {
        var n = day.Night ?? new NightBranding();
        return new NightBrandingResponse(
            new NightBrandingDto(n.Enabled, n.DisplayName, n.Slogan, n.LogoUrl, n.HeroUrl,
                n.PrimaryColor, n.Theme, n.BackgroundColor, n.FontKey),
            BrandingMapping.ToDto(BrandingResolver.ResolveNight(day)));
    }

    public static NightBranding Copy(NightBranding? n) => n is null ? new NightBranding() : new NightBranding
    {
        Enabled = n.Enabled,
        DisplayName = n.DisplayName,
        Slogan = n.Slogan,
        LogoUrl = n.LogoUrl,
        HeroUrl = n.HeroUrl,
        PrimaryColor = n.PrimaryColor,
        Theme = n.Theme,
        BackgroundColor = n.BackgroundColor,
        FontKey = n.FontKey,
    };
}

// ─── Get ────────────────────────────────────────────────────────────────────────

public sealed record GetNightBrandingQuery(Guid TenantId, Guid RequesterId)
    : IRequest<Result<NightBrandingResponse>>;

public sealed class GetNightBrandingQueryHandler
    : IRequestHandler<GetNightBrandingQuery, Result<NightBrandingResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ILogger<GetNightBrandingQueryHandler> _log;

    public GetNightBrandingQueryHandler(ICatalogRepository repo, ILogger<GetNightBrandingQueryHandler> log)
    { _repo = repo; _log = log; }

    public async Task<Result<NightBrandingResponse>> Handle(GetNightBrandingQuery q, CancellationToken ct)
    {
        // Mongo directo, sin caché, como el resto de endpoints de administración.
        var catalog = await _repo.GetByTenantIdAsync(q.TenantId, ct);
        var response = NightBrandingMapping.ToResponse(catalog?.Branding ?? new MenuBranding());

        _log.LogInformation(
            "catalog.admin.branding_night.get tenantId={TenantId} enabled={Enabled} requesterId={RequesterId}",
            q.TenantId, response.Night.Enabled, q.RequesterId);

        return Result.Success(response);
    }
}

// ─── Update (text / colors / theme / font / enabled) ────────────────────────────

public sealed record UpdateNightBrandingCommand(
    Guid TenantId,
    bool? Enabled,
    string? DisplayName,
    string? Slogan,
    string? PrimaryColor,
    string? Theme,
    string? BackgroundColor,
    string? FontKey,
    Guid RequesterId) : IRequest<Result<NightBrandingResponse>>;

public sealed class UpdateNightBrandingValidator : AbstractValidator<UpdateNightBrandingCommand>
{
    public UpdateNightBrandingValidator()
    {
        RuleFor(x => x.Enabled).NotNull().WithMessage("enabled es requerido.");
        RuleFor(x => x.DisplayName).MaximumLength(80).When(x => x.DisplayName is not null);
        RuleFor(x => x.Slogan).MaximumLength(140).When(x => x.Slogan is not null);
        RuleFor(x => x.PrimaryColor)
            .Matches("^#[0-9a-fA-F]{6}$")
            .When(x => x.PrimaryColor is not null)
            .WithMessage("primaryColor debe ser null o un hex #RRGGBB.");
        RuleFor(x => x.BackgroundColor)
            .Matches("^#[0-9a-fA-F]{6}$")
            .When(x => x.BackgroundColor is not null)
            .WithMessage("backgroundColor debe ser null o un hex #RRGGBB.");
        RuleFor(x => x.Theme)
            .Must(t => UpdateMenuBrandingValidator.Themes.Contains(t))
            .When(x => x.Theme is not null)
            .WithMessage("theme debe ser null, light, cream, dark o night.");
        RuleFor(x => x.FontKey).MaximumLength(40).When(x => x.FontKey is not null);
    }
}

public sealed class UpdateNightBrandingHandler
    : IRequestHandler<UpdateNightBrandingCommand, Result<NightBrandingResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<UpdateNightBrandingHandler> _log;

    public UpdateNightBrandingHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<UpdateNightBrandingHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<NightBrandingResponse>> Handle(UpdateNightBrandingCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var day = catalog?.Branding ?? new MenuBranding();
        var current = day.Night;

        // Reemplazo completo salvo las imágenes, que van por sus propios endpoints. enabled=false no
        // borra nada: apagar y volver a encender recupera los overrides intactos.
        var night = new NightBranding
        {
            Enabled = cmd.Enabled ?? true,
            DisplayName = NullIfBlank(cmd.DisplayName)?.Trim(),
            Slogan = NullIfBlank(cmd.Slogan)?.Trim(),
            LogoUrl = current?.LogoUrl,
            HeroUrl = current?.HeroUrl,
            PrimaryColor = NullIfBlank(cmd.PrimaryColor)?.ToLowerInvariant(),
            Theme = NullIfBlank(cmd.Theme),
            BackgroundColor = NullIfBlank(cmd.BackgroundColor)?.ToLowerInvariant(),
            FontKey = NullIfBlank(cmd.FontKey),
        };

        await _repo.UpdateNightBrandingAsync(cmd.TenantId, night, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        var overridden = new List<string>();
        if (night.DisplayName is not null) overridden.Add("displayName");
        if (night.Slogan is not null) overridden.Add("slogan");
        if (night.PrimaryColor is not null) overridden.Add("primaryColor");
        if (night.Theme is not null) overridden.Add("theme");
        if (night.BackgroundColor is not null) overridden.Add("backgroundColor");
        if (night.FontKey is not null) overridden.Add("fontKey");

        _log.LogInformation(
            "catalog.admin.branding_night.updated tenantId={TenantId} enabled={Enabled} theme={Theme} overriddenFields={Fields} requesterId={RequesterId}",
            cmd.TenantId, night.Enabled, night.Theme, string.Join(",", overridden), cmd.RequesterId);

        day.Night = night;
        return Result.Success(NightBrandingMapping.ToResponse(day));
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ─── Upload image (logo | hero) ─────────────────────────────────────────────────

public sealed record UploadNightBrandingImageCommand(
    Guid TenantId,
    string Kind,            // "logo" | "hero"
    Stream FileStream,
    string ContentType,
    long FileSizeBytes,
    Guid RequesterId) : IRequest<Result<NightBrandingResponse>>;

public sealed class UploadNightBrandingImageHandler
    : IRequestHandler<UploadNightBrandingImageCommand, Result<NightBrandingResponse>>
{
    private const long MaxBytes = 4 * 1024 * 1024; // mismas reglas que el día
    private static readonly HashSet<string> AllowedTypes =
        new(["image/jpeg", "image/png", "image/webp"], StringComparer.OrdinalIgnoreCase);

    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IImageProcessingService _imaging;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<UploadNightBrandingImageHandler> _log;

    public UploadNightBrandingImageHandler(ICatalogRepository repo, ICatalogCacheService cache,
        IImageProcessingService imaging, IBlobStorageService blob, ILogger<UploadNightBrandingImageHandler> log)
    { _repo = repo; _cache = cache; _imaging = imaging; _blob = blob; _log = log; }

    public async Task<Result<NightBrandingResponse>> Handle(UploadNightBrandingImageCommand cmd, CancellationToken ct)
    {
        var kind = cmd.Kind?.ToLowerInvariant();
        if (kind is not ("logo" or "hero"))
            return Result.Failure<NightBrandingResponse>(Error.Validation("Branding.InvalidKind", "kind debe ser logo o hero."));
        if (!AllowedTypes.Contains(cmd.ContentType))
            return Result.Failure<NightBrandingResponse>(
                Error.Validation("Image.InvalidFormat", "Formato no soportado. Use JPG, PNG o WebP."));
        if (cmd.FileSizeBytes > MaxBytes)
            return Result.Failure<NightBrandingResponse>(
                Error.Validation("Image.TooLarge", "El archivo excede el límite de 4 MB."));

        // Mismo procesado que el día (logo ≤ 1024 sin pérdida, portada ≤ 2048×1152 q86), path propio.
        string url;
        try
        {
            await using var resized = kind == "logo"
                ? await _imaging.ResizeLogoAsync(cmd.FileStream, ct)
                : await _imaging.ResizeHeroAsync(cmd.FileStream, ct);
            var path = $"branding/{cmd.TenantId}/night-{kind}-{Guid.NewGuid():N}.webp";
            url = await _blob.UploadAsync(path, resized, "image/webp", ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "catalog.admin.branding_night.image_upload_failed tenantId={TenantId} kind={Kind}", cmd.TenantId, kind);
            return Result.Failure<NightBrandingResponse>(Error.Failure("Image.UploadFailed", "No se pudo subir la imagen."));
        }

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var day = catalog?.Branding ?? new MenuBranding();
        var night = NightBrandingMapping.Copy(day.Night);   // no toca enabled
        var oldUrl = kind == "logo" ? night.LogoUrl : night.HeroUrl;
        if (kind == "logo") night.LogoUrl = url; else night.HeroUrl = url;

        await _repo.UpdateNightBrandingAsync(cmd.TenantId, night, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        // Solo el blob anterior de Night, nunca el de día.
        if (!string.IsNullOrEmpty(oldUrl)) await _blob.DeleteByUrlAsync(oldUrl, ct);

        _log.LogInformation(
            "catalog.admin.branding_night.image_uploaded tenantId={TenantId} kind={Kind} bytes={Bytes} requesterId={RequesterId}",
            cmd.TenantId, kind, cmd.FileSizeBytes, cmd.RequesterId);

        day.Night = night;
        return Result.Success(NightBrandingMapping.ToResponse(day));
    }
}

// ─── Delete image (logo | hero) → vuelve a heredar la de día ─────────────────────

public sealed record DeleteNightBrandingImageCommand(Guid TenantId, string Kind, Guid RequesterId)
    : IRequest<Result<NightBrandingResponse>>;

public sealed class DeleteNightBrandingImageHandler
    : IRequestHandler<DeleteNightBrandingImageCommand, Result<NightBrandingResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<DeleteNightBrandingImageHandler> _log;

    public DeleteNightBrandingImageHandler(ICatalogRepository repo, ICatalogCacheService cache,
        IBlobStorageService blob, ILogger<DeleteNightBrandingImageHandler> log)
    { _repo = repo; _cache = cache; _blob = blob; _log = log; }

    public async Task<Result<NightBrandingResponse>> Handle(DeleteNightBrandingImageCommand cmd, CancellationToken ct)
    {
        var kind = cmd.Kind?.ToLowerInvariant();
        if (kind is not ("logo" or "hero"))
            return Result.Failure<NightBrandingResponse>(Error.Validation("Branding.InvalidKind", "kind debe ser logo o hero."));

        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var day = catalog?.Branding ?? new MenuBranding();
        var night = NightBrandingMapping.Copy(day.Night);
        var oldUrl = kind == "logo" ? night.LogoUrl : night.HeroUrl;
        if (kind == "logo") night.LogoUrl = null; else night.HeroUrl = null;

        await _repo.UpdateNightBrandingAsync(cmd.TenantId, night, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);
        if (!string.IsNullOrEmpty(oldUrl)) await _blob.DeleteByUrlAsync(oldUrl, ct);

        _log.LogInformation(
            "catalog.admin.branding_night.image_deleted tenantId={TenantId} kind={Kind} requesterId={RequesterId}",
            cmd.TenantId, kind, cmd.RequesterId);

        day.Night = night;
        return Result.Success(NightBrandingMapping.ToResponse(day));
    }
}
