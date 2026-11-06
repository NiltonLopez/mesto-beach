using System.Security.Cryptography;
using MestoBeach.Identity.Application.Common;
using OtpNet;
using Microsoft.Extensions.Options;
using MestoBeach.Identity.Infrastructure.Configuration;
using BC = BCrypt.Net.BCrypt;

namespace MestoBeach.Identity.Infrastructure.Services;

public sealed class TotpService : ITotpService
{
    private readonly TotpOptions _opts;

    public TotpService(IOptions<TotpOptions> opts) => _opts = opts.Value;

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20); // 160 bits (RFC 4648 standard)
        return Base32Encoding.ToString(bytes);
    }

    public string EncryptSecret(string base32Secret)
    {
        var key = Convert.FromHexString(_opts.EncryptionKeyHex);
        var iv = RandomNumberGenerator.GetBytes(12);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(base32Secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plaintext, ciphertext, tag);

        return $"{Convert.ToHexString(iv)}:{Convert.ToHexString(ciphertext)}:{Convert.ToHexString(tag)}";
    }

    public string DecryptSecret(string encryptedSecret)
    {
        var parts = encryptedSecret.Split(':');
        if (parts.Length != 3) throw new InvalidOperationException("Invalid encrypted TOTP secret format.");

        var key = Convert.FromHexString(_opts.EncryptionKeyHex);
        var iv = Convert.FromHexString(parts[0]);
        var ciphertext = Convert.FromHexString(parts[1]);
        var tag = Convert.FromHexString(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    public bool ValidateCode(string encryptedSecret, string code)
    {
        try
        {
            var base32Secret = DecryptSecret(encryptedSecret);
            var secretBytes = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(secretBytes, step: 30, totpSize: 6);

            // Accept current step and ±1 (tolerance window)
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    public string BuildOtpAuthUrl(string email, string base32Secret)
    {
        var encodedEmail = Uri.EscapeDataString(email);
        return $"otpauth://totp/MestoBeach%3A{encodedEmail}?secret={base32Secret}&issuer=MestoBeach&algorithm=SHA1&digits=6&period=30";
    }

    public string GenerateEmergencyCode()
    {
        // 15 bytes → convert to decimal string, zero-padded to 24 digits
        var bytes = RandomNumberGenerator.GetBytes(15);
        // Use BigInteger-style computation for a uniform 24-digit number
        var bigInt = new System.Numerics.BigInteger(bytes, isUnsigned: true);
        var modulus = System.Numerics.BigInteger.Pow(10, 24);
        return (bigInt % modulus).ToString("D24");
    }

    public string HashEmergencyCode(string code) => BC.HashPassword(code, 12);

    public bool VerifyEmergencyCode(string code, string hash) => BC.Verify(code, hash);
}
