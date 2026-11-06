namespace MestoBeach.Sessions.Infrastructure.Configuration;

public sealed class SessionsOptions
{
    public const string SectionName = "Sessions";
    public string BlobContainerName { get; init; } = "branding";
    public string BlobConnectionString { get; init; } = string.Empty;
}

public sealed class SessionsJwtOptions
{
    public const string SectionName = "Jwt";
    public string PublicKeyPem { get; init; } = string.Empty;
    public string PrivateKeyPem { get; init; } = string.Empty;
}
