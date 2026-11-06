namespace MestoBeach.Identity.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
}

public sealed class TotpOptions
{
    public const string SectionName = "Totp";

    /// <summary>32-byte hex key for AES-256-GCM encryption of TOTP secrets.</summary>
    public string EncryptionKeyHex { get; set; } = string.Empty;
}

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";
    public string ClientId { get; set; } = string.Empty;
}

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string AdminUrl { get; set; } = "http://localhost:3003";
}
