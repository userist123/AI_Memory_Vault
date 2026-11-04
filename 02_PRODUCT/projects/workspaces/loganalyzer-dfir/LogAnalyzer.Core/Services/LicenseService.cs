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
using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.Core.Services
{
    /// <summary>Outcome of a licence check. Only <see cref="Valid"/> activates the application.</summary>
    public enum LicenseCheck { Valid, Malformed, LegacyScheme, NoTrustedKey, BadSignature, WrongHardware, Expired, WrongProduct, HardwareIdUnavailable, WrongModule, FeatureNotAllowed }

    /// <summary>
    /// The processor or base-board identity could not be read through WMI (independent review, finding 6). There is no fallback id: a constant
    /// would make one licence valid on every station where WMI fails.
    /// </summary>
    public sealed class HardwareIdUnavailableException(string message) : InvalidOperationException(message);

    /// <summary>
    /// What a signed licence states. <c>Module</c> is "insula" | "arhipelag" | "continent"; for a legacy LA1 licence it is the compiled module of this
    /// executable, <c>Features</c> is the base set of that module and <c>IsLegacy</c> is true.
    /// </summary>
    public sealed record LicenseClaims(string LicenseId, string HardwareId, DateTime ExpiresUtcDate, string Product,
        string Module = "", IReadOnlyList<string>? FeatureList = null, string Holder = "", bool IsLegacy = true)
    {
        public IReadOnlyList<string> Features { get; init; } = FeatureList ?? [];

        /// <summary>True when the licence grants the capability (a name from <see cref="LicenseFeatures"/>).</summary>
        public bool Has(string feature) => Features.Contains(feature, StringComparer.Ordinal);
    }

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
        public const string PrefixV2 = "LA2";
        public const int MaxHolderLength = 120;
        /// <summary>Largest LA2 payload accepted (bytes of the decoded JSON); larger is Malformed before any parsing.</summary>
        public const int MaxPayloadBytes = 8192;
        public const string Product = "LogAnalyzer";
        public const string PublicKeysResource = "license-public-keys.txt";

        private static readonly Regex LegacyFormat = new(@"^[0-9A-Fa-f]{20}\|\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly JsonSerializerOptions PayloadJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

        private readonly string _licenseFilePath;
        private readonly IReadOnlyList<string> _trustedKeys;
        private readonly Func<string> _hardwareId;
        private readonly StationPosture? _module;
        private LicenseClaims? _savedClaims;

        /// <summary>Production: public keys compiled into this assembly, license.lic next to the executable, hardware id of this PC.</summary>
        public LicenseService() : this(EmbeddedPublicKeys(), null, null) { }

        public LicenseService(IReadOnlyList<string> trustedPublicKeys, string? licenseFilePath = null, Func<string>? hardwareId = null, StationPosture? module = null)
        {
            _module = module;
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

        /// <summary>The hardware id of this PC; throws <see cref="HardwareIdUnavailableException"/> when WMI cannot be read.</summary>
        public string GetHardwareId() => ComputeHardwareId(GetWmiProperty("Win32_Processor", "ProcessorId"), GetWmiProperty("Win32_BaseBoard", "SerialNumber"));

        public const string HardwareIdUnavailableMessage =
            "Hardware ID-ul acestei stații nu poate fi citit (interogarea WMI a procesorului sau a plăcii de bază a eșuat), deci licența nu poate fi activată " +
            "sau verificată. Reporniți stația sau serviciul Windows Management Instrumentation și încercați din nou; dacă problema rămâne, contactați administratorul.";

        public const string HardwareIdPlaceholderMessage =
            "Hardware ID-ul acestei stații nu poate fi calculat: nici procesorul, nici placa de bază nu raportează o valoare care identifică stația " +
            "(valoare goală sau text generic al producătorului, de exemplu „To be filled by O.E.M.” sau „Default string”). Un astfel de Hardware ID ar fi " +
            "același pe multe stații, deci licența nu poate fi activată sau verificată. Contactați administratorul sau furnizorul stației.";

        /// <summary>
        /// Values firmware reports when the real one was never written (review of the remediations #5): empty, a known placeholder text, or only
        /// zeros / only F's (ignoring spaces, dashes, dots and colons). Such a value identifies no station.
        /// </summary>
        public static bool IsPlaceholderHardwareValue(string? value)
        {
            if (value is null) return true;
            var v = value.Trim();
            if (v.Length == 0) return true;
            if (PlaceholderHardwareValues.Contains(v)) return true;
            var compact = new string(v.Where(c => c is not (' ' or '-' or '.' or ':' or '_')).ToArray());
            return compact.Length > 0 && (compact.All(c => c == '0') || compact.All(c => c is 'F' or 'f'));
        }

        private static readonly HashSet<string> PlaceholderHardwareValues = new(StringComparer.OrdinalIgnoreCase)
        {
            "To be filled by O.E.M.", "To be filled by OEM", "O.E.M.", "OEM", "Default string", "None", "System Serial Number",
            "Base Board Serial Number", "Not Specified", "Not Applicable", "Not Available", "N/A", "NA", "Unknown", "Invalid", "Serial", "SerialNumber",
        };

        /// <summary>
        /// The 16-hex-character id from the processor and base-board values. A value that could not be read (null) is refused, never replaced; when
        /// both values are empty or placeholders the id would be the same on many stations and is refused too. Otherwise the id is computed exactly
        /// as before from both raw values, so licences already issued stay valid.
        /// </summary>
        public static string ComputeHardwareId(string? cpuId, string? boardId)
        {
            if (cpuId is null || boardId is null) throw new HardwareIdUnavailableException(HardwareIdUnavailableMessage);
            if (IsPlaceholderHardwareValue(cpuId) && IsPlaceholderHardwareValue(boardId)) throw new HardwareIdUnavailableException(HardwareIdPlaceholderMessage);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cpuId + boardId)))[..16];
        }

        /// <summary>Runs the hardware-id provider; null when it cannot be read.</summary>
        private string? TryHardwareId()
        {
            try { return _hardwareId(); }
            catch (HardwareIdUnavailableException) { return null; }
        }

        private sealed record Payload(
            [property: JsonPropertyName("v")] int V,
            [property: JsonPropertyName("id")] string Id,
            [property: JsonPropertyName("hw")] string Hw,
            [property: JsonPropertyName("exp")] string Exp,
            [property: JsonPropertyName("product")] string Product);

        private sealed record PayloadV2(
            [property: JsonPropertyName("v")] int V,
            [property: JsonPropertyName("id")] string Id,
            [property: JsonPropertyName("hw")] string Hw,
            [property: JsonPropertyName("exp")] string Exp,
            [property: JsonPropertyName("product")] string Product,
            [property: JsonPropertyName("mod")] string Mod,
            [property: JsonPropertyName("feat")] string[] Feat,
            [property: JsonPropertyName("holder")] string Holder);

        /// <summary>Signs an LA2 licence (module, capabilities, holder). Refuses what the verifier would refuse, so a bad licence is never issued.</summary>
        public static string SignV2(ECDsa privateKey, string hardwareId, DateTime expiryDate, string module, string holder,
            IEnumerable<string>? features = null, string? licenseId = null)
        {
            var hw = NormaliseHardwareId(hardwareId);
            if (!IsHardwareId(hw)) throw new ArgumentException("Hardware ID invalid: 16 caractere hexazecimale, exact cum apare în fereastra de activare.", nameof(hardwareId));
            if (!LicenseFeatures.IsKnownModule(module)) throw new ArgumentException("Modul necunoscut: insula, arhipelag sau continent.", nameof(module));
            holder = (holder ?? "").Trim();
            if (holder.Length == 0 || holder.Length > MaxHolderLength) throw new ArgumentException($"Titularul trebuie să aibă între 1 și {MaxHolderLength} caractere.", nameof(holder));
            var feat = (features ?? LicenseFeatures.DefaultFor(module)).ToArray();
            if (feat.Length == 0 || feat.Distinct(StringComparer.Ordinal).Count() != feat.Length) throw new ArgumentException("Lista de capabilități este goală sau are duplicate.", nameof(features));
            foreach (var f in feat)
            {
                if (!LicenseFeatures.IsKnown(f)) throw new ArgumentException($"Capabilitate necunoscută: {f}.", nameof(features));
                if (!LicenseFeatures.IsAllowedFor(module, f)) throw new ArgumentException($"Capabilitatea {f} nu este permisă modulului {module}.", nameof(features));
            }
            var payload = JsonSerializer.SerializeToUtf8Bytes(new PayloadV2(2, licenseId ?? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), hw,
                expiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Product, module, feat, holder), PayloadJson);
            var sig = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{PrefixV2}.{B64Url(payload)}.{B64Url(sig)}";
        }

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
            if (parts.Length != 3 || (parts[0] != Prefix && parts[0] != PrefixV2)) return LicenseCheck.Malformed;
            var v2 = parts[0] == PrefixV2;
            if (v2 && parts[1].Length > MaxPayloadBytes * 2) return LicenseCheck.Malformed; // base64url of far more than 8 KB; refused before decoding
            byte[] payloadBytes, sig;
            try { payloadBytes = FromB64Url(parts[1]); sig = FromB64Url(parts[2]); }
            catch (FormatException) { return LicenseCheck.Malformed; }
            if (v2 && payloadBytes.Length > MaxPayloadBytes) return LicenseCheck.Malformed;
            if (_trustedKeys.Count == 0) return LicenseCheck.NoTrustedKey;
            if (!_trustedKeys.Any(k => VerifyWith(k, payloadBytes, sig))) return LicenseCheck.BadSignature;
            if (v2) return CheckV2(hardwareId, payloadBytes, utcNow, out claims);

            Payload? p;
            try { p = JsonSerializer.Deserialize<Payload>(payloadBytes, PayloadJson); }
            catch (JsonException) { return LicenseCheck.Malformed; }
            if (p is null || p.V != 1 || p.Hw is null || p.Exp is null || p.Id is null) return LicenseCheck.Malformed;
            if (!DateTime.TryParseExact(p.Exp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)) return LicenseCheck.Malformed;
            var compiled = CompiledModule;
            claims = new LicenseClaims(p.Id, p.Hw, exp, p.Product ?? "", compiled, LicenseFeatures.DefaultFor(compiled), "", true);
            if (!string.Equals(p.Product, Product, StringComparison.Ordinal)) return LicenseCheck.WrongProduct;
            if (!string.Equals(NormaliseHardwareId(p.Hw), NormaliseHardwareId(hardwareId), StringComparison.Ordinal)) return LicenseCheck.WrongHardware;
            if (utcNow >= exp.Date.AddDays(1)) return LicenseCheck.Expired;
            return LicenseCheck.Valid;
        }

        /// <summary>
        /// LA2 payload, read token by token so that a duplicated key, an unknown key or a wrong type is Malformed (never the last-one-wins of a
        /// deserializer). Order of the verdicts after the signature: structure (Malformed / FeatureNotAllowed), product, hardware, expiry, module.
        /// </summary>
        private LicenseCheck CheckV2(string hardwareId, byte[] payload, DateTime utcNow, out LicenseClaims? claims)
        {
            claims = null;
            int? v = null; string? id = null, hw = null, exp = null, product = null, mod = null, holder = null; List<string>? feat = null;
            try
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var r = new Utf8JsonReader(payload, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 8 });
                if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return LicenseCheck.Malformed;
                while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                {
                    if (r.TokenType != JsonTokenType.PropertyName) return LicenseCheck.Malformed;
                    var name = r.GetString()!;
                    if (!seen.Add(name)) return LicenseCheck.Malformed;
                    if (!r.Read()) return LicenseCheck.Malformed;
                    switch (name)
                    {
                        case "v": if (r.TokenType != JsonTokenType.Number || !r.TryGetInt32(out var vv)) return LicenseCheck.Malformed; v = vv; break;
                        case "id": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; id = r.GetString(); break;
                        case "hw": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; hw = r.GetString(); break;
                        case "exp": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; exp = r.GetString(); break;
                        case "product": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; product = r.GetString(); break;
                        case "mod": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; mod = r.GetString(); break;
                        case "holder": if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed; holder = r.GetString(); break;
                        case "feat":
                            if (r.TokenType != JsonTokenType.StartArray) return LicenseCheck.Malformed;
                            feat = [];
                            while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                            {
                                if (r.TokenType != JsonTokenType.String) return LicenseCheck.Malformed;
                                feat.Add(r.GetString()!);
                            }
                            if (r.TokenType != JsonTokenType.EndArray) return LicenseCheck.Malformed;
                            break;
                        default: return LicenseCheck.Malformed;
                    }
                }
                if (r.TokenType != JsonTokenType.EndObject || r.Read()) return LicenseCheck.Malformed; // truncated, or trailing content
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return LicenseCheck.Malformed; }

            if (v != 2 || id is null || hw is null || exp is null || product is null || mod is null || feat is null || holder is null) return LicenseCheck.Malformed;
            if (!DateTime.TryParseExact(exp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) return LicenseCheck.Malformed;
            if (!LicenseFeatures.IsKnownModule(mod)) return LicenseCheck.Malformed;
            if (holder.Trim().Length == 0 || holder.Length > MaxHolderLength) return LicenseCheck.Malformed;
            if (feat.Count == 0 || feat.Distinct(StringComparer.Ordinal).Count() != feat.Count || feat.Any(f => !LicenseFeatures.IsKnown(f))) return LicenseCheck.Malformed;
            if (feat.Any(f => !LicenseFeatures.IsAllowedFor(mod, f))) return LicenseCheck.FeatureNotAllowed;

            claims = new LicenseClaims(id, hw, expiry, product, mod, feat.AsReadOnly(), holder, false);
            if (!string.Equals(product, Product, StringComparison.Ordinal)) return LicenseCheck.WrongProduct;
            if (!string.Equals(NormaliseHardwareId(hw), NormaliseHardwareId(hardwareId), StringComparison.Ordinal)) return LicenseCheck.WrongHardware;
            if (utcNow >= expiry.Date.AddDays(1)) return LicenseCheck.Expired;
            if (!string.Equals(mod, CompiledModule, StringComparison.Ordinal)) return LicenseCheck.WrongModule;
            return LicenseCheck.Valid;
        }

        /// <summary>The licence module id of this executable (its compiled posture).</summary>
        public string CompiledModule => LicenseFeatures.ModuleId(_module ?? StationPostureContext.Current);

        /// <summary>The licence the saved license.lic states, when it is valid for this PC now; null otherwise.</summary>
        public LicenseClaims? CurrentLicense()
        {
            try
            {
                if (!File.Exists(_licenseFilePath) || TryHardwareId() is not { } hw) return _savedClaims = null;
                var r = Check(hw, File.ReadAllText(_licenseFilePath), DateTime.UtcNow, out var c);
                return _savedClaims = r == LicenseCheck.Valid ? c : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return _savedClaims = null; }
        }

        /// <summary>
        /// True when the licence file as it is NOW is valid for this PC and grants the capability. The file is read and its signature and expiry verified at every
        /// call (cheap), so a deleted, replaced or expired license.lic is never answered from memory; a missing or unreadable file grants nothing (false for every
        /// capability, fail closed: the running window decides from the rights it read at start, see MainViewModel).
        /// </summary>
        public bool Has(string feature) => CurrentLicense() is { } c && c.Has(feature);

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
            LicenseCheck.HardwareIdUnavailable => HardwareIdUnavailableMessage,
            LicenseCheck.WrongModule => "Licența este pentru alt modul decât cel al acestui program. Cereți o licență pentru modulul acestui program.",
            LicenseCheck.FeatureNotAllowed => "Licența conține o capabilitate pe care modulul ei nu o permite, deci nu este acceptată. Cereți o licență nouă celui care o emite.",
            _ => "Textul introdus nu este o licență: o licență începe cu „LA1.” sau „LA2.” și se copiază întreagă, exact cum a fost primită.",
        };

        /// <summary>Like <see cref="Explain(LicenseCheck)"/>, but WrongModule names both modules: "Licența este pentru modulul X; acest program este modulul Y."</summary>
        public static string Explain(LicenseCheck c, LicenseClaims? claims, StationPosture compiledModule)
        {
            if (c == LicenseCheck.WrongModule && claims is not null && LicenseFeatures.PostureOf(claims.Module) is { } licensed)
                return $"Licența este pentru modulul {ModuleCatalog.ModuleName(licensed)}; acest program este modulul {ModuleCatalog.ModuleName(compiledModule)}.";
            return Explain(c);
        }

        /// <summary>The compiled module of this executable.</summary>
        public StationPosture CompiledPosture => _module ?? StationPostureContext.Current;

        public bool ValidateAndSaveKey(string fullInput) => ValidateAndSaveKey(fullInput, out _);

        /// <summary>The claims of the licence last passed to <see cref="ValidateAndSaveKey(string, out LicenseCheck)"/> (for a precise refusal message); null when it did not parse.</summary>
        public LicenseClaims? LastClaims { get; private set; }

        /// <summary>Checks the licence for this PC and, only when it is valid, saves it to license.lic.</summary>
        public bool ValidateAndSaveKey(string fullInput, out LicenseCheck result)
        {
            if (TryHardwareId() is not { } hw) { result = LicenseCheck.HardwareIdUnavailable; return false; }
            result = Check(hw, fullInput, DateTime.UtcNow, out var claims);
            LastClaims = claims;
            if (result != LicenseCheck.Valid) return false;
            try { File.WriteAllText(_licenseFilePath, fullInput.Trim()); _savedClaims = null; VerifiedClaims = claims; return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result = LicenseCheck.Malformed; return false; }
        }

        /// <summary>Checked at every start: license.lic exists and holds a licence valid for this PC now.</summary>
        public bool IsActivated() => CheckSaved() == LicenseCheck.Valid;

        public LicenseCheck CheckSaved()
        {
            try
            {
                if (!File.Exists(_licenseFilePath)) return LicenseCheck.Malformed;
                if (TryHardwareId() is not { } hw) return LicenseCheck.HardwareIdUnavailable;
                var r = Check(hw, File.ReadAllText(_licenseFilePath), DateTime.UtcNow, out var claims);
                if (r == LicenseCheck.Valid) VerifiedClaims = claims;
                return r;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return LicenseCheck.Malformed; }
        }

        /// <summary>
        /// The claims of the licence verified at start (<see cref="CheckSaved"/>) or just activated (<see cref="ValidateAndSaveKey(string, out LicenseCheck)"/>); null when no licence
        /// was verified in this process. Review of R2b: the main window takes its rights from here, so a licence that expires, is renamed or is half-written between the
        /// start and the end of sign-in never turns into the module's full set; it keeps exactly the rights that were verified.
        /// </summary>
        public LicenseClaims? VerifiedClaims { get; private set; }

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromB64Url(string s)
        {
            if (s.Length == 0 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new FormatException();
            var t = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
        }

        /// <summary>The first value of the WMI property; null when WMI cannot be queried or returns no instance (never a constant placeholder).</summary>
        private static string? GetWmiProperty(string wmiClass, string property)
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
            return null;
        }
    }
}
