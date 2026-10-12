using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Core.Services
{
    /// <summary>Outcome of a licence check. Only <see cref="Valid"/> activates the application.</summary>
    public enum LicenseCheck { Valid, Malformed, LegacyScheme, NoTrustedKey, BadSignature, WrongHardware, Expired, WrongProduct }

    /// <summary>What a signed licence states.</summary>
    public sealed record LicenseClaims(string LicenseId, string HardwareId, DateTime ExpiresUtcDate, string Product);

    /// <summary>
    /// Licence verification (security audit #3). A licence is <c>LA1.&lt;payload&gt;.&lt;signature&gt;</c> (base64url): the payload is JSON
    /// {v, id, hw, exp, product}, the signature is ECDSA P-256 / SHA-256 (IEEE P1363) over the payload bytes, made with the licence
    /// private key that only exists offline (<c>LogAnalyzer.KeyGen license-keygen</c> / <c>license-sign</c>). The editions hold only the
    /// public key(s), compiled in from <c>LogAnalyzer.Core/Licensing/license-public-keys.txt</c> (one base64 SubjectPublicKeyInfo per
    /// line; two lines allow a rotation). With no public key compiled in, every licence is refused (fail closed).
    /// The former "KEY|yyyy-MM-dd" scheme (salted SHA-256, salt in public source) is recognised only to tell the user it is no longer valid.
    /// </summary>
    public class LicenseService
    {
        public const string Prefix = "LA1";
        public const string Product = "LogAnalyzer";
        public const string PublicKeysResource = "license-public-keys.txt";

        private static readonly Regex LegacyFormat = new(@"^[0-9A-Fa-f]{20}\|\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly JsonSerializerOptions PayloadJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

        private readonly string _licenseFilePath;
        private readonly IReadOnlyList<string> _trustedKeys;
        private readonly Func<string> _hardwareId;

        /// <summary>Production: public keys compiled into this assembly, license.lic next to the executable, hardware id of this PC.</summary>
        public LicenseService() : this(EmbeddedPublicKeys(), null, null) { }

        public LicenseService(IReadOnlyList<string> trustedPublicKeys, string? licenseFilePath = null, Func<string>? hardwareId = null)
        {
            _trustedKeys = trustedPublicKeys.Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            _licenseFilePath = licenseFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.lic");
            _hardwareId = hardwareId ?? GetHardwareId;
        }

        /// <summary>True when at least one licence public key is compiled into this build.</summary>
        public bool HasTrustedKey => _trustedKeys.Count > 0;

        public static IReadOnlyList<string> EmbeddedPublicKeys()
        {
            using var st = typeof(LicenseService).Assembly.GetManifestResourceStream(PublicKeysResource);
            if (st is null) return [];
            using var r = new StreamReader(st);
            return r.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        }

        public string GetHardwareId()
        {
            string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
            string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cpuId + boardId)))[..16];
        }

        private sealed record Payload(
            [property: JsonPropertyName("v")] int V,
            [property: JsonPropertyName("id")] string Id,
            [property: JsonPropertyName("hw")] string Hw,
            [property: JsonPropertyName("exp")] string Exp,
            [property: JsonPropertyName("product")] string Product);

        /// <summary>Signs a licence (KeyGen / License Manager, offline). Expiry is a calendar date: the licence is valid through that whole day (UTC).</summary>
        public static string Sign(ECDsa privateKey, string hardwareId, DateTime expiryDate, string? licenseId = null)
        {
            var hw = NormaliseHardwareId(hardwareId);
            if (!IsHardwareId(hw)) throw new ArgumentException("Hardware ID invalid: 16 caractere hexazecimale, exact cum apare în fereastra de activare.", nameof(hardwareId));
            var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, licenseId ?? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), hw,
                expiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Product), PayloadJson);
            var sig = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{Prefix}.{B64Url(payload)}.{B64Url(sig)}";
        }

        public static bool IsHardwareId(string s) => s.Length == 16 && s.All(Uri.IsHexDigit);

        private static string NormaliseHardwareId(string s) => (s ?? "").Trim().ToUpperInvariant();

        /// <summary>Pure check (no file, no WMI): signature by a trusted key, this hardware id, not expired at <paramref name="utcNow"/>.</summary>
        public LicenseCheck Check(string hardwareId, string fullInput, DateTime utcNow, out LicenseClaims? claims)
        {
            claims = null;
            var text = (fullInput ?? "").Trim();
            if (LegacyFormat.IsMatch(text)) return LicenseCheck.LegacyScheme;
            var parts = text.Split('.');
            if (parts.Length != 3 || parts[0] != Prefix) return LicenseCheck.Malformed;
            byte[] payloadBytes, sig;
            try { payloadBytes = FromB64Url(parts[1]); sig = FromB64Url(parts[2]); }
            catch (FormatException) { return LicenseCheck.Malformed; }
            if (_trustedKeys.Count == 0) return LicenseCheck.NoTrustedKey;
            if (!_trustedKeys.Any(k => VerifyWith(k, payloadBytes, sig))) return LicenseCheck.BadSignature;

            Payload? p;
            try { p = JsonSerializer.Deserialize<Payload>(payloadBytes, PayloadJson); }
            catch (JsonException) { return LicenseCheck.Malformed; }
            if (p is null || p.V != 1 || p.Hw is null || p.Exp is null || p.Id is null) return LicenseCheck.Malformed;
            if (!DateTime.TryParseExact(p.Exp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)) return LicenseCheck.Malformed;
            claims = new LicenseClaims(p.Id, p.Hw, exp, p.Product ?? "");
            if (!string.Equals(p.Product, Product, StringComparison.Ordinal)) return LicenseCheck.WrongProduct;
            if (!string.Equals(NormaliseHardwareId(p.Hw), NormaliseHardwareId(hardwareId), StringComparison.Ordinal)) return LicenseCheck.WrongHardware;
            if (utcNow >= exp.Date.AddDays(1)) return LicenseCheck.Expired;
            return LicenseCheck.Valid;
        }

        private static bool VerifyWith(string publicKeyBase64, byte[] payload, byte[] sig)
        {
            try
            {
                using var k = ECDsa.Create();
                k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
                return k.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return false; }
        }

        public bool VerifyLicenseString(string hwId, string fullInput, DateTime utcNow) => Check(hwId, fullInput, utcNow, out _) == LicenseCheck.Valid;

        /// <summary>Plain Romanian explanation shown in the activation window.</summary>
        public static string Explain(LicenseCheck c) => c switch
        {
            LicenseCheck.Valid => "Licența este validă pentru această stație.",
            LicenseCheck.LegacyScheme =>
                "Această cheie este din schema veche de licențiere (cheie de 20 de caractere urmată de „|” și dată). Cheile din schema veche nu mai sunt valabile, " +
                "pentru că oricine le putea calcula. Trimiteți Hardware ID-ul de mai sus celui care v-a dat licența și cereți o licență nouă, semnată (începe cu „LA1.”).",
            LicenseCheck.NoTrustedKey =>
                "Această versiune a aplicației nu conține nicio cheie publică de licență, deci nu poate accepta nicio licență. " +
                "Cereți o versiune a aplicației construită cu cheia publică a celui care emite licențele.",
            LicenseCheck.BadSignature => "Semnătura licenței nu este validă: licența a fost modificată sau nu a fost emisă de cel care emite licențele pentru această aplicație.",
            LicenseCheck.WrongHardware => "Licența a fost emisă pentru altă stație (alt Hardware ID). Cereți o licență pentru Hardware ID-ul afișat mai sus.",
            LicenseCheck.Expired => "Licența a expirat. Cereți o licență nouă pentru Hardware ID-ul afișat mai sus.",
            LicenseCheck.WrongProduct => "Licența este pentru alt produs.",
            _ => "Textul introdus nu este o licență: o licență începe cu „LA1.” și se copiază întreagă, exact cum a fost primită.",
        };

        public bool ValidateAndSaveKey(string fullInput) => ValidateAndSaveKey(fullInput, out _);

        /// <summary>Checks the licence for this PC and, only when it is valid, saves it to license.lic.</summary>
        public bool ValidateAndSaveKey(string fullInput, out LicenseCheck result)
        {
            result = Check(_hardwareId(), fullInput, DateTime.UtcNow, out _);
            if (result != LicenseCheck.Valid) return false;
            try { File.WriteAllText(_licenseFilePath, fullInput.Trim()); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result = LicenseCheck.Malformed; return false; }
        }

        /// <summary>Checked at every start: license.lic exists and holds a licence valid for this PC now.</summary>
        public bool IsActivated() => CheckSaved() == LicenseCheck.Valid;

        public LicenseCheck CheckSaved()
        {
            try
            {
                if (!File.Exists(_licenseFilePath)) return LicenseCheck.Malformed;
                return Check(_hardwareId(), File.ReadAllText(_licenseFilePath), DateTime.UtcNow, out _);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return LicenseCheck.Malformed; }
        }

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromB64Url(string s)
        {
            if (s.Length == 0 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new FormatException();
            var t = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
        }

        private static string GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                    foreach (var obj in searcher.Get()) return obj[property]?.ToString() ?? "";
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or PlatformNotSupportedException) { }
            return "UNKNOWN_HW_ID_001";
        }
    }
}
