using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>
    /// Signed edition policy of the unclassified application (P2 air-gapped / P3 connected). Same scheme as the release-gate
    /// waivers (release-gate/ReleaseGate.Lib.ps1): ECDSA P-256 over SHA-256, IEEE P1363 signature, public key as base64 SPKI,
    /// and a canonical payload built from the fields so JSON formatting cannot affect the signature.
    /// Fails closed: a missing, malformed, unsigned, expired, foreign or rolled-back policy means AirGapped.
    /// </summary>
    public sealed record EditionPolicy(string Schema, AppMode Mode, long Version, string NotBefore, string? NotAfter, string Audience, string Signer,
        StationRole? Role = null)
    {
        public const string SchemaId = "loganalyzer-edition-policy/1";
        public const string Algorithm = "ecdsa-p256-sha256";

        /// <summary>
        /// The exact text that is signed. The station role (WP18) is an optional last line: a policy signed without it keeps its old payload
        /// (so policies issued before WP18 stay valid and mean CONTROL), and a role cannot be added or edited without re-signing.
        /// </summary>
        public static string Payload(AppMode mode, long version, string notBefore, string? notAfter, string audience, string signer, StationRole? role = null)
        {
            var text = string.Join("\n", SchemaId, $"mode={(mode == AppMode.Network ? "connected" : "airgapped")}",
                $"version={version.ToString(CultureInfo.InvariantCulture)}", $"notBefore={notBefore}", $"notAfter={notAfter ?? ""}",
                $"audience={audience}", $"signer={signer}");
            return role is null ? text : text + "\n" + $"role={StationRoles.Name(role.Value)}";
        }

        /// <summary>For the owner's signing tool and the tests: produces the policy file text.</summary>
        public static string Sign(ECDsa privateKey, AppMode mode, long version, string notBefore, string? notAfter, string audience, string signer, StationRole? role = null)
        {
            var sig = privateKey.SignData(Encoding.UTF8.GetBytes(Payload(mode, version, notBefore, notAfter, audience, signer, role)),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var json = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["schema"] = SchemaId,
                ["mode"] = mode == AppMode.Network ? "connected" : "airgapped",
                ["version"] = version, ["notBefore"] = notBefore, ["notAfter"] = notAfter, ["audience"] = audience, ["signer"] = signer,
            };
            if (role is not null) json["role"] = StationRoles.Name(role.Value);
            json["signature"] = new { alg = Algorithm, value = Convert.ToBase64String(sig) };
            return JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public sealed record PolicyLoadResult(bool Valid, EditionPolicy? Policy, string Reason, string? PolicySha256);

    public static class EditionPolicyVerifier
    {
        public const string FileName = "LogAnalyzer.policy";

        public static string DefaultPolicyPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", FileName);

        /// <param name="publicKeyBase64">SPKI of the owner's policy-signing key, embedded in the build. Null/empty: no policy can be valid.</param>
        /// <param name="highWaterVersion">Highest policy version already accepted on this station; lower versions are refused (no rollback).</param>
        public static PolicyLoadResult Verify(string? policyText, string? publicKeyBase64, string machineName, DateTimeOffset nowUtc, long highWaterVersion = 0)
        {
            static PolicyLoadResult Fail(string why) => new(false, null, why, null);
            if (string.IsNullOrWhiteSpace(policyText)) return Fail("Nu există fișier de politică.");
            if (string.IsNullOrWhiteSpace(publicKeyBase64)) return Fail("Aplicația nu conține cheia publică de verificare a politicii.");
            try
            {
                using var doc = JsonDocument.Parse(policyText);
                var r = doc.RootElement;
                string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                if (S("schema") != EditionPolicy.SchemaId) return Fail("Schema politicii este necunoscută.");
                AppMode mode;
                switch (S("mode"))
                {
                    case "airgapped": mode = AppMode.AirGapped; break;
                    case "connected": mode = AppMode.Network; break;
                    default: return Fail("Modul din politică este necunoscut.");
                }
                if (!r.TryGetProperty("version", out var vEl) || !vEl.TryGetInt64(out var version) || version < 1) return Fail("Versiunea politicii lipsește.");
                var notBefore = S("notBefore"); var notAfter = r.TryGetProperty("notAfter", out var na) && na.ValueKind == JsonValueKind.String ? na.GetString() : null;
                var audience = S("audience"); var signer = S("signer");
                if (audience.Length == 0 || signer.Length == 0) return Fail("Politica nu are destinatar sau semnatar.");
                StationRole? role = null;
                if (r.TryGetProperty("role", out var roleEl))
                {
                    if (roleEl.ValueKind != JsonValueKind.String || !StationRoles.TryParse(roleEl.GetString(), out var parsedRole))
                        return Fail("Rolul de stație din politică este necunoscut (se acceptă „control” sau „csirt”).");
                    role = parsedRole;
                }
                if (!r.TryGetProperty("signature", out var sg) || sg.ValueKind != JsonValueKind.Object ||
                    !sg.TryGetProperty("alg", out var alg) || alg.GetString() != EditionPolicy.Algorithm ||
                    !sg.TryGetProperty("value", out var val) || val.ValueKind != JsonValueKind.String)
                    return Fail("Politica nu este semnată cu algoritmul așteptat.");

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
                if (ecdsa.KeySize != 256) return Fail("Cheia de verificare nu este P-256.");
                var payload = Encoding.UTF8.GetBytes(EditionPolicy.Payload(mode, version, notBefore, notAfter, audience, signer, role));
                if (!ecdsa.VerifyData(payload, Convert.FromBase64String(val.GetString()!), HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return Fail("Semnătura politicii nu corespunde conținutului și cheii publice.");

                if (!DateTimeOffset.TryParse(notBefore, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var nb) || nowUtc < nb)
                    return Fail("Politica nu este încă valabilă (notBefore).");
                if (notAfter is not null && (!DateTimeOffset.TryParse(notAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var nAfter) || nowUtc > nAfter))
                    return Fail("Politica a expirat (notAfter).");
                if (audience != "*" && !audience.Equals(machineName, StringComparison.OrdinalIgnoreCase))
                    return Fail($"Politica este destinată altei stații ({audience}).");
                if (version < highWaterVersion)
                    return Fail($"Versiunea politicii ({version}) este mai veche decât cea deja acceptată ({highWaterVersion}).");

                var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policyText)));
                return new PolicyLoadResult(true, new EditionPolicy(EditionPolicy.SchemaId, mode, version, notBefore, notAfter, audience, signer, role), "Politică semnată validă.", sha);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException or InvalidOperationException)
            {
                return Fail("Politica nu a putut fi verificată: " + ex.Message);
            }
        }

        /// <summary>
        /// The mode for this process. The command line (--mode=), the LogAnalyzer.mode file and connectivity detection are never
        /// used to choose Network: only a valid signed policy can. Without one the station is AirGapped.
        /// </summary>
        public static ModeDecision Decide(PolicyLoadResult policy, ConnectivitySnapshot observed, string? ignoredOverride)
        {
            var note = string.IsNullOrWhiteSpace(ignoredOverride) ? "" : $" Cererea operatorului „{ignoredOverride}” a fost ignorată: modul vine doar din politica semnată.";
            return policy.Valid
                ? new ModeDecision(policy.Policy!.Mode, false, $"Mod stabilit de politica semnată v{policy.Policy.Version} (semnatar {policy.Policy.Signer}, SHA-256 {policy.PolicySha256![..12]}).{note}", observed)
                : new ModeDecision(AppMode.AirGapped, false, $"Mod AirGapped (implicit sigur): {policy.Reason}{note}", observed);
        }

        /// <summary>The requested override that is present on the command line or in the mode file, only to log that it was ignored.</summary>
        public static string? RequestedOverride(System.Collections.Generic.IReadOnlyList<string> args, string? stationFile)
        {
            foreach (var a in args)
                if (a.StartsWith(OperatingModeResolver.ArgumentPrefix, StringComparison.OrdinalIgnoreCase)) return a;
            return string.IsNullOrWhiteSpace(stationFile) ? null : "LogAnalyzer.mode=" + stationFile.Trim();
        }
    }
}
