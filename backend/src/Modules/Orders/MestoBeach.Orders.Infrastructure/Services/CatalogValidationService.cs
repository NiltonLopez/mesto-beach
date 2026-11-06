using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MestoBeach.Shared.Venue;
using StackExchange.Redis;
using System.Text.Json;

namespace MestoBeach.Orders.Infrastructure.Services;

// ─── Minimal MongoDB read model (mirrors Catalog module's document shape) ─────

[BsonIgnoreExtraElements]
internal sealed class CatalogReadDocument
{
    [BsonId, BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tenantId")] public string TenantId { get; set; } = string.Empty;
    // ADR-041: solo para el visibleIn de la categoría de cada ítem.
    [BsonElement("categories")] public List<CategoryDoc> Categories { get; set; } = [];
    [BsonElement("items")] public List<CatalogItemDoc> Items { get; set; } = [];
    [BsonElement("modifierGroups")] public List<ModifierGroupDoc> ModifierGroups { get; set; } = [];

    [BsonIgnoreExtraElements]
    internal sealed class CatalogItemDoc
    {
        // Catalog persists each item's identifier as the subdocument `_id`, stored as a
        // 36-char GUID string (e.g. "d931e155-…"), NOT a field named "id" and NOT an ObjectId.
        // Map `_id` AND force String representation — a string id member otherwise defaults
        // to ObjectId representation and throws "not a valid 24 digit hex string".
        [BsonId]
        [BsonRepresentation(MongoDB.Bson.BsonType.String)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("categoryId")] public string CategoryId { get; set; } = string.Empty;
        [BsonElement("name")] public string Name { get; set; } = string.Empty;
        [BsonElement("priceCents")] public int PriceCents { get; set; }
        [BsonElement("available")] public bool Available { get; set; }
        [BsonElement("modifierGroupIds")] public List<string> ModifierGroupIds { get; set; } = [];
        [BsonElement("allowNotes")] public bool AllowNotes { get; set; }
        // Estación propia del plato. Los platos anteriores a esta función no traen el campo:
        // se lee como null y el ruteo sigue cayendo en la categoría, que es lo correcto.
        [BsonElement("stationId")] public string? StationId { get; set; }
        // ADR-041. Ausente o null = ambos modos.
        [BsonElement("visibleIn")] public List<string>? VisibleIn { get; set; }
        [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class CategoryDoc
    {
        // Igual que los ítems: Catalog guarda el id como `_id` string (GUID de 36 caracteres).
        [BsonId]
        [BsonRepresentation(MongoDB.Bson.BsonType.String)]
        public string Id { get; set; } = string.Empty;

        // ADR-041. Ausente o null = ambos modos.
        [BsonElement("visibleIn")] public List<string>? VisibleIn { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class ModifierGroupDoc
    {
        [BsonElement("id")] public string Id { get; set; } = string.Empty;
        [BsonElement("name")] public string Name { get; set; } = string.Empty;
        [BsonElement("type")] public string Type { get; set; } = "MULTI";
        [BsonElement("required")] public bool Required { get; set; }
        [BsonElement("minSelect")] public int MinSelect { get; set; }
        [BsonElement("maxSelect")] public int? MaxSelect { get; set; }
        [BsonElement("options")] public List<OptionDoc> Options { get; set; } = [];

        [BsonIgnoreExtraElements]
        internal sealed class OptionDoc
        {
            [BsonElement("id")] public string Id { get; set; } = string.Empty;
            [BsonElement("name")] public string Name { get; set; } = string.Empty;
            [BsonElement("priceDeltaCents")] public int PriceDeltaCents { get; set; }
            [BsonElement("available")] public bool Available { get; set; } = true;
        }
    }
}

// ─── Redis cache DTO (mirrors Catalog.Domain.TenantCatalog serialization) ────

internal sealed class CatalogCacheDto
{
    // La caché ya trae Categories (es el TenantCatalog serializado); solo interesa su visibleIn.
    public List<CategoryCacheDto> Categories { get; set; } = [];
    public List<CatalogItemCacheDto> Items { get; set; } = [];
    public List<ModifierGroupCacheDto> ModifierGroups { get; set; } = [];

    internal sealed class CatalogItemCacheDto
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public bool Available { get; set; }
        public List<Guid> ModifierGroupIds { get; set; } = [];
        public bool AllowNotes { get; set; }
        public Guid? StationId { get; set; }
        // ADR-041. Ausente (entrada de caché anterior) o null = ambos modos.
        public List<string>? VisibleIn { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    internal sealed class CategoryCacheDto
    {
        public Guid Id { get; set; }
        public List<string>? VisibleIn { get; set; }
    }

    internal sealed class ModifierGroupCacheDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        // TenantCatalog serializes the ModifierType enum as a number (0=Single,1=Multi,2=Remove).
        public int Type { get; set; }
        public bool Required { get; set; }
        public int MinSelect { get; set; }
        public int? MaxSelect { get; set; }
        public List<ModifierOptionCacheDto> Options { get; set; } = [];
    }

    internal sealed class ModifierOptionCacheDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PriceDeltaCents { get; set; }
        public bool Available { get; set; }
    }
}

// ─── Service ──────────────────────────────────────────────────────────────────

public sealed class CatalogValidationService : ICatalogValidationService
{
    private readonly IDatabase _redis;
    private readonly IMongoCollection<CatalogReadDocument> _col;
    private readonly ILogger<CatalogValidationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogValidationService(
        IConnectionMultiplexer redis,
        IMongoClient mongoClient,
        IOptions<OrdersCatalogOptions> opts,
        ILogger<CatalogValidationService> logger)
    {
        _redis = redis.GetDatabase();
        var db = mongoClient.GetDatabase(opts.Value.MongoDatabaseName);
        _col = db.GetCollection<CatalogReadDocument>(opts.Value.MongoCollectionName);
        _logger = logger;
    }

    private static string CacheKey(Guid tenantId) => $"catalog:{tenantId}";

    public async Task<CatalogValidationResult> ValidateAsync(
        Guid tenantId, IEnumerable<string> catalogItemIds, VenueMode venueMode, CancellationToken ct = default)
    {
        var ids = catalogItemIds.Distinct().ToHashSet();
        var mode = venueMode.ToString();

        // L1: Redis
        var cached = await _redis.StringGetAsync(CacheKey(tenantId));
        if (cached.HasValue)
        {
            _logger.LogDebug("catalog.validation.cache_hit {TenantId}", tenantId);
            var dto = JsonSerializer.Deserialize<CatalogCacheDto>(cached.ToString(), JsonOpts);
            if (dto is not null)
            {
                var groups = dto.ModifierGroups.ToDictionary(
                    g => g.Id.ToString(),
                    g => new ModifierGroupSnapshot(
                        g.Id.ToString(), g.Name, TypeFromInt(g.Type), g.Required,
                        g.MinSelect, g.MaxSelect,
                        g.Options.Select(o => new ModifierOptionSnapshot(
                            o.Id.ToString(), o.Name, o.PriceDeltaCents, o.Available)).ToList()),
                    StringComparer.OrdinalIgnoreCase);

                var categoryVisibleIn = (dto.Categories ?? [])
                    .GroupBy(c => c.Id.ToString())
                    .ToDictionary(g => g.Key, g => g.First().VisibleIn, StringComparer.OrdinalIgnoreCase);

                return Validate(ids, dto.Items
                    .Where(i => i.DeletedAt is null)
                    .Select(i => (
                        new CatalogItemSnapshot(
                            i.Id.ToString(), i.Name, i.PriceCents, i.CategoryId.ToString(), i.Available,
                            ResolveGroups(i.ModifierGroupIds.Select(g => g.ToString()), groups),
                            i.AllowNotes, i.StationId?.ToString()),
                        IsOrderable(i.VisibleIn, i.CategoryId.ToString(), categoryVisibleIn, mode))));
            }
        }

        // L2: MongoDB (2-second timeout enforced by caller via CancellationToken)
        _logger.LogInformation("catalog.validation.mongo_fallback {TenantId}", tenantId);
        try
        {
            var filter = Builders<CatalogReadDocument>.Filter
                .Eq(d => d.TenantId, tenantId.ToString());

            var doc = await _col.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
                throw new CatalogUnavailableException();

            var groups = doc.ModifierGroups.ToDictionary(
                g => g.Id,
                g => new ModifierGroupSnapshot(
                    g.Id, g.Name, (g.Type ?? "MULTI").ToUpperInvariant(), g.Required,
                    g.MinSelect, g.MaxSelect,
                    g.Options.Select(o => new ModifierOptionSnapshot(
                        o.Id, o.Name, o.PriceDeltaCents, o.Available)).ToList()),
                StringComparer.OrdinalIgnoreCase);

            var categoryVisibleIn = (doc.Categories ?? [])
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First().VisibleIn, StringComparer.OrdinalIgnoreCase);

            // L2 sigue sin escribir la caché.
            return Validate(ids, doc.Items
                .Where(i => i.DeletedAt is null)
                .Select(i => (
                    new CatalogItemSnapshot(
                        i.Id, i.Name, i.PriceCents, i.CategoryId, i.Available,
                        ResolveGroups(i.ModifierGroupIds, groups),
                        i.AllowNotes, i.StationId),
                    IsOrderable(i.VisibleIn, i.CategoryId, categoryVisibleIn, mode))));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("catalog.validation.mongo_timeout {TenantId}", tenantId);
            throw new CatalogUnavailableException();
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "catalog.validation.mongo_error {TenantId}", tenantId);
            throw new CatalogUnavailableException();
        }
    }

    // ModifierType enum order in Catalog.Domain: Single=0, Multi=1, Remove=2.
    private static string TypeFromInt(int type) => type switch
    {
        0 => "SINGLE",
        2 => "REMOVE",
        _ => "MULTI"
    };

    private static List<ModifierGroupSnapshot> ResolveGroups(
        IEnumerable<string> groupIds,
        IReadOnlyDictionary<string, ModifierGroupSnapshot> library) =>
        groupIds
            .Select(id => library.TryGetValue(id, out var g) ? g : null)
            .Where(g => g is not null)
            .Select(g => g!)
            .ToList();

    /// <summary>
    /// Regla única de ADR-041: pedible en M ⇔ item.visibleIn ∋ M ∧ category(item).visibleIn ∋ M.
    /// null/ausente en el ítem o en la categoría = ambos; categoría no encontrada = ambos.
    /// </summary>
    private static bool IsOrderable(
        List<string>? itemVisibleIn, string categoryId,
        IReadOnlyDictionary<string, List<string>?> categoryVisibleIn, string mode) =>
        Includes(itemVisibleIn, mode)
        && (!categoryVisibleIn.TryGetValue(categoryId, out var catVisibleIn) || Includes(catVisibleIn, mode));

    private static bool Includes(List<string>? visibleIn, string mode) =>
        visibleIn is null || visibleIn.Contains(mode, StringComparer.OrdinalIgnoreCase);

    private static CatalogValidationResult Validate(
        IReadOnlySet<string> requestedIds,
        IEnumerable<(CatalogItemSnapshot Item, bool Orderable)> allItems)
    {
        var byId = allItems.ToDictionary(i => i.Item.Id);
        var valid = new List<CatalogItemSnapshot>();
        var invalid = new List<string>();
        var notInMode = new List<NotInModeItem>();

        foreach (var id in requestedIds)
        {
            // No existe o borrado → inválido; existe pero no es de este modo (aunque esté agotado)
            // → NotInMode; pedible pero no disponible → inválido.
            if (!byId.TryGetValue(id, out var entry))
                invalid.Add(id);
            else if (!entry.Orderable)
                notInMode.Add(new NotInModeItem(entry.Item.Id, entry.Item.Name));
            else if (!entry.Item.Available)
                invalid.Add(id);
            else
                valid.Add(entry.Item);
        }

        return new CatalogValidationResult(valid, invalid, notInMode);
    }
}
