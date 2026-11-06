namespace MestoBeach.Catalog.Domain.Entities;

/// <summary>
/// How a customer selects options of a modifier group.
/// SINGLE = radio (pick exactly one, usually required); MULTI = checkboxes with prices;
/// REMOVE = "sin X" ingredient toggles (no cost).
/// </summary>
public enum ModifierType { Single, Multi, Remove }

/// <summary>One choosable option within a modifier group (e.g. "Michelada" +$3.000).</summary>
public sealed class ModifierOption
{
    public ModifierOption(Guid id, string name, int priceDeltaCents, bool available, bool isDefault)
    {
        Id = id;
        Name = name;
        PriceDeltaCents = priceDeltaCents;
        Available = available;
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int PriceDeltaCents { get; private set; }
    public bool Available { get; private set; }
    /// <summary>Pre-selected by default (only meaningful for SINGLE groups).</summary>
    public bool IsDefault { get; private set; }
}

/// <summary>
/// A reusable group of modifier options, owned at tenant level (the "add-ons library").
/// Items reference groups by id; editing a group updates every item that uses it.
/// </summary>
public sealed class ModifierGroup
{
    public ModifierGroup(
        Guid id, string name, ModifierType type,
        bool required, int minSelect, int? maxSelect,
        List<ModifierOption> options)
    {
        Id = id;
        Name = name;
        Type = type;
        Required = required;
        MinSelect = minSelect;
        MaxSelect = maxSelect;
        Options = options ?? new();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ModifierType Type { get; private set; }
    /// <summary>The customer must choose (≥ minSelect). For SINGLE this means exactly one.</summary>
    public bool Required { get; private set; }
    public int MinSelect { get; private set; }
    public int? MaxSelect { get; private set; }
    public List<ModifierOption> Options { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(string name, ModifierType type, bool required, int minSelect, int? maxSelect, List<ModifierOption> options)
    {
        Name = name;
        Type = type;
        Required = required;
        MinSelect = minSelect;
        MaxSelect = maxSelect;
        Options = options ?? new();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Resolve the price delta for a set of selected option ids that belong to this group.</summary>
    public int DeltaFor(IEnumerable<Guid> selectedOptionIds)
    {
        var set = selectedOptionIds.ToHashSet();
        return Options.Where(o => set.Contains(o.Id)).Sum(o => Type == ModifierType.Remove ? 0 : o.PriceDeltaCents);
    }
}
