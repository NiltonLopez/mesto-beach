using MestoBeach.Catalog.Domain;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Catalog.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MestoBeach.Catalog.Infrastructure.Persistence;

// ─── MongoDB document model ───────────────────────────────────────────────────

internal sealed class CatalogDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("tenantId")] public string TenantId { get; set; } = string.Empty;
    [BsonElement("categories")] public List<CategoryDoc> Categories { get; set; } = [];
    [BsonElement("items")] public List<ItemDoc> Items { get; set; } = [];
    [BsonElement("modifierGroups")] public List<ModifierGroupDoc> ModifierGroups { get; set; } = [];
    [BsonElement("branding")] public BrandingDoc? Branding { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }

    [BsonIgnoreExtraElements]
    internal sealed class BrandingDoc
    {
        [BsonElement("displayName")] public string DisplayName { get; set; } = string.Empty;
        [BsonElement("slogan")] public string Slogan { get; set; } = string.Empty;
        [BsonElement("logoUrl")] public string? LogoUrl { get; set; }
        [BsonElement("heroUrl")] public string? HeroUrl { get; set; }
        [BsonElement("primaryColor")] public string PrimaryColor { get; set; } = "#f97316";
        [BsonElement("theme")] public string Theme { get; set; } = "light";
        [BsonElement("backgroundColor")] public string? BackgroundColor { get; set; }
        [BsonElement("fontKey")] public string FontKey { get; set; } = "manrope";
        // Overrides del modo NIGHT (ADR-039). Ausente = nunca configurado = { enabled: true }.
        [BsonElement("night"), BsonIgnoreIfNull] public NightBrandingDoc? Night { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class NightBrandingDoc
    {
        // Ausente = true: solo un false explícito apaga Night.
        [BsonElement("enabled"), BsonIgnoreIfNull] public bool? Enabled { get; set; }
        [BsonElement("displayName")] public string? DisplayName { get; set; }
        [BsonElement("slogan")] public string? Slogan { get; set; }
        [BsonElement("logoUrl")] public string? LogoUrl { get; set; }
        [BsonElement("heroUrl")] public string? HeroUrl { get; set; }
        [BsonElement("primaryColor")] public string? PrimaryColor { get; set; }
        [BsonElement("theme")] public string? Theme { get; set; }
        [BsonElement("backgroundColor")] public string? BackgroundColor { get; set; }
        [BsonElement("fontKey")] public string? FontKey { get; set; }
    }

    [BsonIgnoreExtraElements]
    internal sealed class ModifierGroupDoc
    {
        [BsonElement("id")] public string Id { get; set; } = string.Empty;
        [BsonElement("name")] public string Name { get; set; } = string.Empty;
        [BsonElement("type")] public string Type { get; set; } = "MULTI";   // SINGLE|MULTI|REMOVE
        [BsonElement("required")] public bool Required { get; set; }
        [BsonElement("minSelect")] public int MinSelect { get; set; }
        [BsonElement("maxSelect")] public int? MaxSelect { get; set; }
        [BsonElement("options")] public List<OptionDoc> Options { get; set; } = [];
        [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
        [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }

        [BsonIgnoreExtraElements]
        internal sealed class OptionDoc
        {
            [BsonElement("id")] public string Id { get; set; } = string.Empty;
            [BsonElement("name")] public string Name { get; set; } = string.Empty;
            [BsonElement("priceDeltaCents")] public int PriceDeltaCents { get; set; }
            [BsonElement("available")] public bool Available { get; set; } = true;
            [BsonElement("isDefault")] public bool IsDefault { get; set; }
        }
    }

    internal sealed class CategoryDoc
    {
        [BsonElement("id")] public string Id { get; set; } = string.Empty;
        [BsonElement("name")] public string Name { get; set; } = string.Empty;
        [BsonElement("displayOrder")] public int DisplayOrder { get; set; }
        // ADR-041. Ausente = ambos modos; se materializa al leer, sin migración de datos.
        [BsonElement("visibleIn"), BsonIgnoreIfNull] public List<string>? VisibleIn { get; set; }
        [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    }

    internal sealed class ItemDoc
    {
        [BsonElement("id")] public string Id { get; set; } = string.Empty;
        [BsonElement("categoryId")] public string CategoryId { get; set; } = string.Empty;
        [BsonElement("name")] public string Name { get; set; } = string.Empty;
        [BsonElement("description")] public string? Description { get; set; }
        [BsonElement("priceCents")] public int PriceCents { get; set; }
        [BsonElement("images")] public List<string> Images { get; set; } = [];
        [BsonElement("available")] public bool Available { get; set; }
        [BsonElement("stockEnabled")] public bool StockEnabled { get; set; }
        [BsonElement("stockQty")] public int StockQty { get; set; }
        [BsonElement("lowStockThreshold")] public int LowStockThreshold { get; set; }
        [BsonElement("displayOrder")] public int DisplayOrder { get; set; }
        [BsonElement("modifierGroupIds")] public List<string> ModifierGroupIds { get; set; } = [];
        [BsonElement("allowNotes")] public bool AllowNotes { get; set; }
        // Estación de KDS propia del plato; ausente en los platos anteriores a esta función,
        // que siguen heredando la de su categoría.
        [BsonElement("stationId")] public string? StationId { get; set; }
        // ADR-041. Ausente = ambos modos; se materializa al leer, sin migración de datos.
        [BsonElement("visibleIn"), BsonIgnoreIfNull] public List<string>? VisibleIn { get; set; }
        [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
        [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
        [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }
    }
}

// ─── Repository ───────────────────────────────────────────────────────────────

public sealed class CatalogRepository : ICatalogRepository
{
    private readonly IMongoCollection<CatalogDocument> _col;

    public CatalogRepository(IMongoClient mongoClient, IOptions<CatalogOptions> opts)
    {
        var db = mongoClient.GetDatabase(opts.Value.MongoDatabaseName);
        _col = db.GetCollection<CatalogDocument>(opts.Value.MongoCollectionName);

        // Ensure unique index on tenantId
        var idxModel = new CreateIndexModel<CatalogDocument>(
            Builders<CatalogDocument>.IndexKeys.Ascending(d => d.TenantId),
            new CreateIndexOptions { Unique = true, Background = true });
        _col.Indexes.CreateOne(idxModel);
    }

    private static FilterDefinition<CatalogDocument> ByTenant(Guid tenantId) =>
        Builders<CatalogDocument>.Filter.Eq(d => d.TenantId, tenantId.ToString());

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<TenantCatalog?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct)
    {
        var doc = await _col.Find(ByTenant(tenantId)).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    // ── Categories ────────────────────────────────────────────────────────────

    public async Task AddCategoryAsync(Guid tenantId, CatalogCategory cat, CancellationToken ct)
    {
        var catDoc = new CatalogDocument.CategoryDoc
        {
            Id = cat.Id.ToString(),
            Name = cat.Name,
            DisplayOrder = cat.DisplayOrder,
            VisibleIn = PersistedVisibleIn(cat.VisibleIn),
            CreatedAt = cat.CreatedAt
        };

        var update = Builders<CatalogDocument>.Update
            .Push(d => d.Categories, catDoc)
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(d => d.TenantId, tenantId.ToString());

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task UpdateCategoryAsync(Guid tenantId, Guid categoryId,
        string? name, int? displayOrder, List<string>? visibleIn, CancellationToken ct)
    {
        var filter = Builders<CatalogDocument>.Filter.And(
            ByTenant(tenantId),
            Builders<CatalogDocument>.Filter.ElemMatch(
                d => d.Categories,
                c => c.Id == categoryId.ToString()));

        var updateDefs = new List<UpdateDefinition<CatalogDocument>>
        {
            Builders<CatalogDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
        };

        if (name is not null)
            updateDefs.Add(Builders<CatalogDocument>.Update
                .Set("categories.$[elem].name", name));
        if (displayOrder is not null)
            updateDefs.Add(Builders<CatalogDocument>.Update
                .Set("categories.$[elem].displayOrder", displayOrder.Value));
        if (visibleIn is not null)
            updateDefs.Add(Builders<CatalogDocument>.Update
                .Set("categories.$[elem].visibleIn", visibleIn));

        var combined = Builders<CatalogDocument>.Update.Combine(updateDefs);
        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",categoryId.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), combined,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    public async Task DeleteCategoryAsync(Guid tenantId, Guid categoryId, CancellationToken ct)
    {
        var pull = Builders<CatalogDocument>.Update
            .PullFilter(d => d.Categories,
                c => c.Id == categoryId.ToString())
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        await _col.UpdateOneAsync(ByTenant(tenantId), pull, cancellationToken: ct);
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    public async Task AddItemAsync(Guid tenantId, CatalogItem item, CancellationToken ct)
    {
        var doc = MapItemToDoc(item);

        var update = Builders<CatalogDocument>.Update
            .Push(d => d.Items, doc)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        await _col.UpdateOneAsync(ByTenant(tenantId), update, cancellationToken: ct);
    }

    public async Task UpdateItemAsync(
        Guid tenantId, Guid itemId,
        string? name, string? description, int? priceCents, Guid? categoryId,
        bool? available, bool? stockEnabled, int? stockQty, int? lowStockThreshold,
        int? displayOrder, List<Guid>? modifierGroupIds, bool? allowNotes,
        Guid? stationId, bool clearStation, List<string>? visibleIn, CancellationToken ct)
    {
        var updates = new List<UpdateDefinition<CatalogDocument>>
        {
            Builders<CatalogDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow),
            Builders<CatalogDocument>.Update.Set("items.$[elem].updatedAt", DateTime.UtcNow)
        };

        if (name is not null)        updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].name", name));
        if (description is not null) updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].description", description));
        if (priceCents is not null)    updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].priceCents", priceCents.Value));
        if (categoryId is not null)  updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].categoryId", categoryId.Value.ToString()));
        if (available is not null)   updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].available", available.Value));
        if (stockEnabled is not null)updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].stockEnabled", stockEnabled.Value));
        if (stockQty is not null)    updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].stockQty", stockQty.Value));
        if (lowStockThreshold is not null) updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].lowStockThreshold", lowStockThreshold.Value));
        if (displayOrder is not null)updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].displayOrder", displayOrder.Value));
        if (modifierGroupIds is not null) updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].modifierGroupIds", modifierGroupIds.Select(g => g.ToString()).ToList()));
        if (allowNotes is not null)  updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].allowNotes", allowNotes.Value));
        if (clearStation)            updates.Add(Builders<CatalogDocument>.Update.Set<string?>("items.$[elem].stationId", null));
        else if (stationId is not null) updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].stationId", stationId.Value.ToString()));
        if (visibleIn is not null)   updates.Add(Builders<CatalogDocument>.Update.Set("items.$[elem].visibleIn", visibleIn));

        var combined = Builders<CatalogDocument>.Update.Combine(updates);
        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",itemId.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), combined,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    public async Task SoftDeleteItemAsync(Guid tenantId, Guid itemId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var update = Builders<CatalogDocument>.Update
            .Set("items.$[elem].deletedAt", now)
            .Set("items.$[elem].updatedAt", now)
            .Set(d => d.UpdatedAt, now);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",itemId.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    // ── Modifier groups (tenant add-on library) ────────────────────────────────

    public async Task AddModifierGroupAsync(Guid tenantId, ModifierGroup group, CancellationToken ct)
    {
        var update = Builders<CatalogDocument>.Update
            .Push(d => d.ModifierGroups, MapGroupToDoc(group))
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(d => d.TenantId, tenantId.ToString());

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task UpdateModifierGroupAsync(Guid tenantId, ModifierGroup group, CancellationToken ct)
    {
        var doc = MapGroupToDoc(group);
        var update = Builders<CatalogDocument>.Update
            .Set("modifierGroups.$[elem].name", doc.Name)
            .Set("modifierGroups.$[elem].type", doc.Type)
            .Set("modifierGroups.$[elem].required", doc.Required)
            .Set("modifierGroups.$[elem].minSelect", doc.MinSelect)
            .Set("modifierGroups.$[elem].maxSelect", doc.MaxSelect)
            .Set("modifierGroups.$[elem].options", doc.Options)
            .Set("modifierGroups.$[elem].updatedAt", DateTime.UtcNow)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",group.Id.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    public async Task DeleteModifierGroupAsync(Guid tenantId, Guid groupId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Remove the group from the library and detach it from every item that referenced it.
        var update = Builders<CatalogDocument>.Update
            .PullFilter(d => d.ModifierGroups, g => g.Id == groupId.ToString())
            .Pull("items.$[].modifierGroupIds", groupId.ToString())
            .Set(d => d.UpdatedAt, now);

        await _col.UpdateOneAsync(ByTenant(tenantId), update, cancellationToken: ct);
    }

    // ── Branding ────────────────────────────────────────────────────────────────

    // Día y Night se escriben por caminos disjuntos (SDD-002 §branding.night): el día hace $set por
    // campo y nunca toca branding.night; Night hace solo $set branding.night.
    public async Task UpdateBrandingAsync(Guid tenantId, MenuBranding branding, CancellationToken ct)
    {
        await EnsureBrandingObjectAsync(tenantId, ct);
        var update = Builders<CatalogDocument>.Update
            .Set("branding.displayName", branding.DisplayName)
            .Set("branding.slogan", branding.Slogan)
            .Set("branding.logoUrl", branding.LogoUrl)
            .Set("branding.heroUrl", branding.HeroUrl)
            .Set("branding.primaryColor", branding.PrimaryColor)
            .Set("branding.theme", branding.Theme)
            .Set("branding.backgroundColor", branding.BackgroundColor)
            .Set("branding.fontKey", branding.FontKey)
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(d => d.TenantId, tenantId.ToString());

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task UpdateNightBrandingAsync(Guid tenantId, NightBranding night, CancellationToken ct)
    {
        await EnsureBrandingObjectAsync(tenantId, ct);
        var doc = new CatalogDocument.NightBrandingDoc
        {
            Enabled = night.Enabled,
            DisplayName = night.DisplayName,
            Slogan = night.Slogan,
            LogoUrl = night.LogoUrl,
            HeroUrl = night.HeroUrl,
            PrimaryColor = night.PrimaryColor,
            Theme = night.Theme,
            BackgroundColor = night.BackgroundColor,
            FontKey = night.FontKey,
        };
        var update = Builders<CatalogDocument>.Update
            .Set("branding.night", doc)
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(d => d.TenantId, tenantId.ToString());

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    /// <summary>
    /// Un $set sobre "branding.x" falla si el documento tiene <c>branding: null</c>; lo quita antes para
    /// que Mongo cree el subdocumento. Si branding ya es un objeto (o no existe), no hace nada.
    /// </summary>
    private Task EnsureBrandingObjectAsync(Guid tenantId, CancellationToken ct)
    {
        var filter = Builders<CatalogDocument>.Filter.And(
            ByTenant(tenantId),
            Builders<CatalogDocument>.Filter.Type("branding", BsonType.Null));
        return _col.UpdateOneAsync(filter, Builders<CatalogDocument>.Update.Unset("branding"), cancellationToken: ct);
    }

    // ── Images ────────────────────────────────────────────────────────────────

    public async Task AddImageAsync(Guid tenantId, Guid itemId, string imageUrl, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var update = Builders<CatalogDocument>.Update
            .Push("items.$[elem].images", imageUrl)
            .Set("items.$[elem].updatedAt", now)
            .Set(d => d.UpdatedAt, now);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",itemId.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    public async Task RemoveImageAsync(Guid tenantId, Guid itemId, int imageIndex, CancellationToken ct)
    {
        // Read current images, remove at index, write back
        var doc = await _col.Find(ByTenant(tenantId)).FirstOrDefaultAsync(ct);
        var item = doc?.Items.FirstOrDefault(i => i.Id == itemId.ToString());
        if (item is null) return;

        item.Images.RemoveAt(imageIndex);
        var now = DateTime.UtcNow;

        var update = Builders<CatalogDocument>.Update
            .Set("items.$[elem].images", item.Images)
            .Set("items.$[elem].updatedAt", now)
            .Set(d => d.UpdatedAt, now);

        var arrayFilters = new[]
        {
            new BsonDocumentArrayFilterDefinition<BsonDocument>(
                new BsonDocument("elem._id",itemId.ToString()))
        };

        await _col.UpdateOneAsync(ByTenant(tenantId), update,
            new UpdateOptions { ArrayFilters = arrayFilters }, ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static TenantCatalog MapToDomain(CatalogDocument doc)
    {
        var categories = doc.Categories.Select(c =>
        {
            var cat = new CatalogCategory(Guid.Parse(c.Id), c.Name, c.DisplayOrder);
            cat.SetVisibleIn(ReadVisibleIn(c.VisibleIn));
            return cat;
        }).ToList();

        var items = doc.Items.Select(i =>
        {
            var item = new CatalogItem(
                Guid.Parse(i.Id), Guid.Parse(i.CategoryId),
                i.Name, i.Description, i.PriceCents,
                i.Available, i.StockEnabled, i.StockQty,
                i.LowStockThreshold, i.DisplayOrder);
            foreach (var url in i.Images) item.AddImage(url);
            item.SetModifiers(
                (i.ModifierGroupIds ?? []).Select(Guid.Parse).ToList(),
                i.AllowNotes);
            if (Guid.TryParse(i.StationId, out var stationId)) item.SetStation(stationId);
            item.SetVisibleIn(ReadVisibleIn(i.VisibleIn));
            if (i.DeletedAt.HasValue) item.SoftDelete();
            return item;
        }).ToList();

        var groups = (doc.ModifierGroups ?? []).Select(MapGroupToDomain).ToList();

        var b = doc.Branding;
        var branding = b is null ? new MenuBranding() : new MenuBranding
        {
            DisplayName = b.DisplayName,
            Slogan = b.Slogan,
            LogoUrl = b.LogoUrl,
            HeroUrl = b.HeroUrl,
            PrimaryColor = string.IsNullOrWhiteSpace(b.PrimaryColor) ? "#f97316" : b.PrimaryColor,
            Theme = string.IsNullOrWhiteSpace(b.Theme) ? "light" : b.Theme,
            BackgroundColor = string.IsNullOrWhiteSpace(b.BackgroundColor) ? null : b.BackgroundColor,
            FontKey = string.IsNullOrWhiteSpace(b.FontKey) ? "manrope" : b.FontKey,
            Night = b.Night is null ? null : new NightBranding
            {
                Enabled = b.Night.Enabled ?? true,
                DisplayName = b.Night.DisplayName,
                Slogan = b.Night.Slogan,
                LogoUrl = b.Night.LogoUrl,
                HeroUrl = b.Night.HeroUrl,
                PrimaryColor = string.IsNullOrWhiteSpace(b.Night.PrimaryColor) ? null : b.Night.PrimaryColor,
                Theme = string.IsNullOrWhiteSpace(b.Night.Theme) ? null : b.Night.Theme,
                BackgroundColor = string.IsNullOrWhiteSpace(b.Night.BackgroundColor) ? null : b.Night.BackgroundColor,
                FontKey = string.IsNullOrWhiteSpace(b.Night.FontKey) ? null : b.Night.FontKey,
            },
        };

        return new TenantCatalog
        {
            TenantId = Guid.Parse(doc.TenantId),
            Categories = categories,
            Items = items,
            ModifierGroups = groups,
            Branding = branding,
            UpdatedAt = doc.UpdatedAt
        };
    }

    /// <summary>Ausente/null o vacío en Mongo = ambos modos (ADR-041); lo demás, tal cual se guardó.</summary>
    private static List<string> ReadVisibleIn(List<string>? stored) =>
        stored is { Count: > 0 } ? [.. stored] : VisibleInModes.Both();

    /// <summary>
    /// Al crear, el campo solo se escribe si restringe algún modo: el default (ambos) se materializa al
    /// leer, igual que en los registros anteriores a la función.
    /// </summary>
    private static List<string>? PersistedVisibleIn(List<string> visibleIn) =>
        VisibleInModes.IsBoth(visibleIn) ? null : [.. visibleIn];

    private static ModifierGroup MapGroupToDomain(CatalogDocument.ModifierGroupDoc g) =>
        new(Guid.Parse(g.Id), g.Name,
            Enum.TryParse<ModifierType>(g.Type, true, out var t) ? t : ModifierType.Multi,
            g.Required, g.MinSelect, g.MaxSelect,
            g.Options.Select(o => new ModifierOption(
                Guid.Parse(o.Id), o.Name, o.PriceDeltaCents, o.Available, o.IsDefault)).ToList());

    private static CatalogDocument.ModifierGroupDoc MapGroupToDoc(ModifierGroup g) =>
        new()
        {
            Id = g.Id.ToString(),
            Name = g.Name,
            Type = g.Type.ToString().ToUpperInvariant(),
            Required = g.Required,
            MinSelect = g.MinSelect,
            MaxSelect = g.MaxSelect,
            Options = g.Options.Select(o => new CatalogDocument.ModifierGroupDoc.OptionDoc
            {
                Id = o.Id.ToString(), Name = o.Name, PriceDeltaCents = o.PriceDeltaCents,
                Available = o.Available, IsDefault = o.IsDefault
            }).ToList(),
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt
        };

    private static CatalogDocument.ItemDoc MapItemToDoc(CatalogItem item) =>
        new()
        {
            Id = item.Id.ToString(),
            CategoryId = item.CategoryId.ToString(),
            Name = item.Name,
            Description = item.Description,
            PriceCents = item.PriceCents,
            Images = [.. item.Images],
            Available = item.Available,
            StockEnabled = item.StockEnabled,
            StockQty = item.StockQty,
            LowStockThreshold = item.LowStockThreshold,
            DisplayOrder = item.DisplayOrder,
            ModifierGroupIds = item.ModifierGroupIds.Select(g => g.ToString()).ToList(),
            AllowNotes = item.AllowNotes,
            StationId = item.StationId?.ToString(),
            VisibleIn = PersistedVisibleIn(item.VisibleIn),
            DeletedAt = item.DeletedAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
}
