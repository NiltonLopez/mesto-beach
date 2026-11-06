using System.Security.Cryptography;
using BC = BCrypt.Net.BCrypt;
using MestoBeach.Identity.Application.Common;

namespace MestoBeach.Identity.Infrastructure.Services;

public sealed class PasswordService : IPasswordService
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BC.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) => BC.Verify(password, hash);
}

/// <summary>
/// Generates temporary passwords for the direct-access onboarding flow.
/// Uses an unambiguous alphabet (no O/0/I/l/1) and guarantees the staff password
/// policy: at least one uppercase, one lowercase and one digit.
/// </summary>
public sealed class PasswordGenerator : IPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string All = Upper + Lower + Digits;
    private const int Length = 12;

    public string GenerateTemporary()
    {
        var chars = new char[Length];
        // Guarantee policy: first three positions cover each required class.
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        for (var i = 3; i < Length; i++)
            chars[i] = Pick(All);

        // Fisher–Yates shuffle so the guaranteed classes are not always at the front.
        for (var i = Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
