using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// Password of the primary administrator only (decision 33). PBKDF2-SHA256 from the .NET BCL (no custom crypto), at least 600 000 iterations,
/// random 16-byte salt, constant-time comparison. Strength rule: length at least 14, not equal to the account name, not in a small deny-list.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 14;
    public const int Iterations = 600_000;
    public const int MinStoredIterations = 600_000;
    public const string Algorithm = "PBKDF2-SHA256";

    private static readonly HashSet<string> DenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123456", "passwordpassword", "administrator1", "administrator123", "administrator12345", "adminadminadmin", "adminadmin1234",
        "qwertyuiopasdf", "qwertyqwertyqwerty", "1234567890123", "12345678901234", "123456789012345", "1234567890123456", "00000000000000",
        "11111111111111", "aaaaaaaaaaaaaa", "letmeinletmein", "welcomewelcome", "changemechangeme", "changeme123456", "iloveyouiloveyou",
        "parola123456789", "parolaparolaparola", "parolaparola12", "bucuresti123456", "romania1234567", "loganalyzer123", "loganalyzer1234",
        "loganalyzeradmin", "loganalyzer12345", "p@ssw0rdp@ssw0rd", "p@ssw0rd123456", "passw0rdpassw0rd", "abcdefghijklmn", "abcdefghijklmno",
        "qazwsxedcrfvtgb", "1q2w3e4r5t6y7u", "zaq12wsxcde34rfv", "correcthorsebatterystaple", "trustno1trustno1", "monkeymonkeymonkey",
    };

    /// <summary>Null when the password is acceptable; otherwise the reason (Romanian, shown to the user).</summary>
    public static string? Check(string password, string account)
    {
        password ??= "";
        if (password.Length < MinLength) return $"parola trebuie să aibă cel puțin {MinLength} caractere";
        var user = account.Contains('\\') ? account[(account.LastIndexOf('\\') + 1)..] : account;
        if (password.Equals(account, StringComparison.OrdinalIgnoreCase) || (user.Length > 0 && password.Equals(user, StringComparison.OrdinalIgnoreCase)))
            return "parola nu poate fi identică cu numele contului";
        if (DenyList.Contains(password) || DenyList.Contains(password.Replace(" ", "")))
            return "parola este prea comună (este în lista de parole interzise)";
        if (password.Distinct().Count() < 5) return "parola folosește prea puține caractere distincte";
        return null;
    }

    public static PasswordRecord Create(string account, string password, DateTimeOffset now)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return new PasswordRecord { Account = account, Algorithm = Algorithm, Iterations = Iterations, Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash), ChangedUtc = now };
    }

    public static bool Verify(PasswordRecord r, string password)
    {
        try
        {
            if (r.Algorithm != Algorithm || r.Iterations < MinStoredIterations) return false;     // a weakened record never verifies
            var expected = Convert.FromBase64String(r.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? ""), Convert.FromBase64String(r.Salt), r.Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Burns the same time as a real verification (unknown account), so response time does not reveal which accounts exist.</summary>
    internal static void DummyVerify() =>
        Rfc2898DeriveBytes.Pbkdf2("x"u8.ToArray(), new byte[16], Iterations, HashAlgorithmName.SHA256, 32);
}
