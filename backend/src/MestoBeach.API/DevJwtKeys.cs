using System.Security.Cryptography;

namespace MestoBeach.API;

/// <summary>
/// Claves JWT de desarrollo. El repositorio no versiona ninguna clave privada: en Development, si
/// no llegan por configuración, se genera un par RSA la primera vez y se reutiliza desde un fichero
/// fuera de git, para que un reinicio no cierre las sesiones abiertas.
/// <para>
/// En cualquier otro entorno la ausencia es un error de arranque. Una clave conocida permitiría
/// falsificar tokens de cualquier rol, así que nunca hay un valor por defecto al que caer.
/// </para>
/// </summary>
internal static class DevJwtKeys
{
    private const string PrivateKey = "Jwt:PrivateKeyPem";
    private const string PublicKey = "Jwt:PublicKeyPem";

    public static void EnsureConfigured(WebApplicationBuilder builder)
    {
        if (IsSet(builder.Configuration[PrivateKey]) && IsSet(builder.Configuration[PublicKey]))
            return;

        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Faltan las claves JWT: define Jwt__PrivateKeyPem y Jwt__PublicKeyPem por variable de entorno.");

        using var rsa = LoadOrCreate(Path.Combine(builder.Environment.ContentRootPath, ".dev-keys", "jwt-dev.pem"));

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PrivateKey] = rsa.ExportRSAPrivateKeyPem(),
            [PublicKey] = rsa.ExportSubjectPublicKeyInfoPem()
        });
    }

    private static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("REPLACE_", StringComparison.Ordinal);

    private static RSA LoadOrCreate(string path)
    {
        var rsa = RSA.Create(2048);

        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sin disco escribible la clave vive solo en memoria: cada arranque cierra las sesiones.
            Serilog.Log.Warning("No se pudo guardar la clave JWT de desarrollo en {Path}: {Motivo}", path, e.Message);
        }

        return rsa;
    }
}
