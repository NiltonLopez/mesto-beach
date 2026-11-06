using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Cross-tenant customer account for the Mesto Beach loyalty network.
/// Not scoped to a single tenant — one account valid across all restaurants.
/// </summary>
public sealed class CustomerAccount : AggregateRoot
{
    private CustomerAccount() : base(Guid.Empty) { } // EF Core

    // Constructor for email+password registration
    public CustomerAccount(
        Guid id,
        string email,
        string name,
        string passwordHash,
        string? phone = null,
        DateOnly? birthDate = null) : base(id)
    {
        Email = email.ToLowerInvariant();
        Name = name;
        PasswordHash = passwordHash;
        Phone = phone;
        BirthDate = birthDate;
        LoyaltyPoints = 0;
        CreatedAt = DateTime.UtcNow;
        LastLogin = DateTime.UtcNow;
    }

    // Constructor for Google OAuth registration (no password)
    public CustomerAccount(
        Guid id,
        string email,
        string name,
        string googleId,
        bool isGoogleAccount) : base(id)
    {
        Email = email.ToLowerInvariant();
        Name = name;
        GoogleId = googleId;
        LoyaltyPoints = 0;
        CreatedAt = DateTime.UtcNow;
        LastLogin = DateTime.UtcNow;
    }

    public static CustomerAccount CreateFromGoogle(Guid id, string email, string name, string googleId) =>
        new(id, email, name, googleId, true);

    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? PasswordHash { get; private set; }
    public string? GoogleId { get; private set; }
    public int LoyaltyPoints { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastLogin { get; private set; }

    public bool HasPassword => PasswordHash is not null;
    public bool HasGoogle => GoogleId is not null;

    public void RecordLogin() => LastLogin = DateTime.UtcNow;

    public void UpdateProfile(string? name, string? phone, DateOnly? birthDate)
    {
        if (name is not null) Name = name;
        if (phone is not null) Phone = phone == string.Empty ? null : phone;
        if (birthDate.HasValue) BirthDate = birthDate;
    }

    public void UpdatePassword(string passwordHash) => PasswordHash = passwordHash;

    public void AddLoyaltyPoints(int points)
    {
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        LoyaltyPoints += points;
    }

    public void TryDeductLoyaltyPoints(int points)
    {
        if (points < 0 || LoyaltyPoints < points)
            throw new InvalidOperationException("Puntos insuficientes.");
        LoyaltyPoints -= points;
    }
}
