using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Proves the obfuscated detection fixtures (<see cref="TestFixtureText"/>) decode to the
/// exact bytes the product used to see. The expected SHA-256 values were recorded from the
/// original plaintext literals before they were encoded; matching them byte-for-byte means
/// the encoding changed nothing except the bytes stored in the compiled assembly. The hashes
/// below are safe to keep in source (a SHA-256 digest is not an attack signature).
/// </summary>
public sealed class TestFixtureTextGuardTests
{
    [Theory]
    [InlineData("4424a82ae3b88b16495d49f8a40f006ddc22d6bfc912d3db744c19e547459bcc")]
    public void Amsi_reflection_bypass_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.AmsiReflectionBypass));

    [Theory]
    [InlineData("3a8857779cf8e68d996a1d942a99e4631de4b70478c78348a4336eb73cf82152")]
    public void Encoded_iex_block_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.EncodedIexBlock));

    [Theory]
    [InlineData("3f0877351625d76ea1af6ae12b80fc5d3fe74f3272bfcc2455d4049adddaced6")]
    public void Download_cradle_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.DownloadCradle));

    [Theory]
    [InlineData("bf42b85802ed67d60e0846078dfa68244463a269f072d3a5636c9f2446822c94")]
    public void Webclient_proxy_iex_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.WebClientProxyIex));

    [Theory]
    [InlineData("195af892cab98e141f25dd1b0b0816219c033dbde3d395882bc5ba3322d9b950")]
    public void Reflection_type_probe_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.ReflectionTypeProbe));

    [Theory]
    [InlineData("c12d48d40e31ecc2422f2ebdd80923a0939589f21c90d795a85b6bf28ef3b7aa")]
    public void Encoded_cred_store_read_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.EncodedCredStoreRead));

    [Theory]
    [InlineData("de66633a616e0a1d9e94952e2b25a14c39c4e6f5f5c50515923ecc1f9848f245")]
    public void Cred_tool_claim_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.CredToolClaim));

    [Theory]
    [InlineData("f79120505932db8664767a6d21b91498415c851df87f1f04f40fe727c1688248")]
    public void Cred_tool_exe_is_byte_identical(string sha) =>
        Assert.Equal(sha, TestFixtureText.Sha256(TestFixtureText.CredToolExe));
}
