using System;
using System.Reflection;
using LogAnalyzer.Core.Services.Edition;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Reading and verifying the signed edition policy at startup, shared by the editions that accept one (Network and Internet). The Standalone
    /// edition never calls it: it has no policy key and no policy can change anything there. A policy can only choose the runtime mode inside the
    /// posture of the executable (see StationPostures.ClampMode); it never changes the posture.
    /// </summary>
    internal static class SignedPolicyStartup
    {
        public const string PublicKeyResource = "edition-policy-public-key.b64";

        /// <summary>
        /// Anti-rollback mark (security audit #18): the maximum of the plain <c>policy.highwater</c> file and of the versions recorded in the
        /// hash-chained auth audit. A missing, unreadable or lowered file therefore counts as the audit's maximum.
        /// </summary>
        internal static long HighWater(string highWaterPath, string authAuditPath)
        {
            long file = 0;
            try { if (System.IO.File.Exists(highWaterPath)) long.TryParse(System.IO.File.ReadAllText(highWaterPath).Trim(), out file); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            return Math.Max(Math.Max(file, 0), LogAnalyzer.Dfir.Auth.AuthService.EditionPolicyHighWater(authAuditPath));
        }

        /// <summary>Reads the policy file, verifies it against the key embedded in <paramref name="keyAssembly"/> and records the new high-water mark (best effort).</summary>
        public static PolicyLoadResult Load(Assembly keyAssembly)
        {
            var highWaterPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(EditionPolicyVerifier.DefaultPolicyPath)!, "policy.highwater");
            var highWater = HighWater(highWaterPath, System.IO.Path.Combine(LogAnalyzer.Dfir.Auth.AuthService.DefaultDir, LogAnalyzer.Dfir.Auth.AuthService.AuditFileName));

            string? policyText = null;
            try { if (System.IO.File.Exists(EditionPolicyVerifier.DefaultPolicyPath)) policyText = System.IO.File.ReadAllText(EditionPolicyVerifier.DefaultPolicyPath); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }

            string? key = null;
            using (var st = keyAssembly.GetManifestResourceStream(PublicKeyResource))
                if (st is not null) { using var r = new System.IO.StreamReader(st); key = r.ReadToEnd(); }

            var result = EditionPolicyVerifier.Verify(policyText, key, Environment.MachineName, DateTimeOffset.UtcNow, highWater);
            if (result.Valid && result.Policy!.Version > highWater)
            {
                // Best effort (needs write access to the policy directory, i.e. administrators): later, older policies are refused.
                try { System.IO.File.WriteAllText(highWaterPath, result.Policy.Version.ToString()); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            }
            return result;
        }
    }
}
