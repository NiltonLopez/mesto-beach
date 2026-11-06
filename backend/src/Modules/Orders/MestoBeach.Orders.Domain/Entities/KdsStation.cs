using MestoBeach.Shared.Venue;

namespace MestoBeach.Orders.Domain.Entities;

/// <summary>A KDS station that receives orders for specific catalog categories.</summary>
public sealed class KdsStation
{
    /// <summary>Perfil de flujo (ADR-037): el de cuatro columnas de siempre.</summary>
    public const string FullPass = "FULL_PASS";
    /// <summary>Perfil de flujo (ADR-037): barra de dos estados, permite <c>QUEUED → READY</c>.</summary>
    public const string Bar = "BAR";

    public static readonly IReadOnlyList<string> FlowProfiles = [FullPass, Bar];

    /// <summary>Default de <c>active_in</c>: encendida en todos los modos.</summary>
    public static List<string> AllModes() => Enum.GetNames<VenueMode>().ToList();

    private KdsStation() { } // EF Core

    public KdsStation(Guid id, Guid tenantId, string name, List<string> categoryIds)
        : this(id, tenantId, name, categoryIds, true, FullPass, AllModes()) { }

    public KdsStation(
        Guid id, Guid tenantId, string name, List<string> categoryIds,
        bool isActive, string flowProfile, List<string> activeIn)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        CategoryIds = categoryIds;
        IsActive = isActive;
        FlowProfile = flowProfile;
        ActiveIn = activeIn;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public List<string> CategoryIds { get; private set; } = [];
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Interruptor general (ADR-037). Apagada no recibe comandas en ningún modo.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary><c>FULL_PASS</c> | <c>BAR</c> (ADR-037; supone OQ-1904 = sí: un perfil por estación).</summary>
    public string FlowProfile { get; private set; } = FullPass;

    /// <summary>Modos en los que se enciende (<c>RESTAURANT</c> | <c>NIGHT</c>). Nunca vacía.</summary>
    public List<string> ActiveIn { get; private set; } = AllModes();

    public bool IsBar => FlowProfile == Bar;

    /// <summary>«Encendida» se calcula, no se guarda: <c>is_active && active_in ∋ mode</c>.</summary>
    public bool IsOnIn(VenueMode mode) => IsActive && ActiveIn.Contains(mode.ToString());

    public void UpdateName(string name)
    {
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCategoryIds(List<string> categoryIds)
    {
        CategoryIds = categoryIds;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFlowProfile(string flowProfile)
    {
        if (!FlowProfiles.Contains(flowProfile))
            throw new ArgumentException($"Perfil de flujo desconocido: {flowProfile}.", nameof(flowProfile));
        FlowProfile = flowProfile;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActiveIn(List<string> activeIn)
    {
        if (activeIn.Count == 0)
            throw new ArgumentException("active_in no puede quedar vacía.", nameof(activeIn));
        ActiveIn = activeIn;
        UpdatedAt = DateTime.UtcNow;
    }
}
