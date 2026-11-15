using System.Globalization;
using System.Security.Cryptography;

namespace VetPos.Core.Security;

/// <summary>
/// PINs are stored as salted PBKDF2-SHA256 hashes ("pbkdf2$iterations$salt$hash"), never as the
/// PIN itself. Checking is constant-time.
/// </summary>
public static class PinHasher
{
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Create(CultureInfo.InvariantCulture, $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public static bool Verify(string pin, string stored)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>A PIN is 4 to 8 digits. Returns a message when it is not.</summary>
    public static string? CheckPin(string? pin) =>
        pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit) ? null : "A PIN is 4 to 8 digits.";
}
