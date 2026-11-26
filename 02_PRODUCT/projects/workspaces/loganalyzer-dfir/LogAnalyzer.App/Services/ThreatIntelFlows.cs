using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogAnalyzer.Dfir.Detection;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Master plan 6.12 on this station: the package store under %ProgramData%\LogAnalyzer\threat_intel and the trust it is checked with — the same
    /// CMS verifier, qualified-certificate anchors and CRLs, and security-officer list as the signed media register. A CERT package reaches the
    /// station on approved media and is signed for it by the station's security officer; nothing else is accepted.
    /// </summary>
    public static class ThreatIntelFlows
    {
        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "threat_intel");

        /// <summary>
        /// Why the store must not be used ("" = it may): its folder is writable by accounts other than Administrators and SYSTEM, or not owned by
        /// them, after an attempt to restrict it (review 2026-11-08: the ACL was applied only to a new folder and never checked again, and the seal
        /// key lives in that folder).
        /// </summary>
        public static string StoreProblem { get; private set; } = "";

        /// <summary>
        /// The store, with the restricted ACL of the other station stores (Administrators and SYSTEM full control, Users read only) checked on every
        /// use and its history sealed with the machine chain key. A store whose rights cannot be confirmed is refused: nothing is staged, approved
        /// or used from it.
        /// </summary>
        public static ThreatIntelStore Store(string? root = null)
        {
            var dir = root ?? DefaultRoot;
            Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows()) { StoreProblem = ""; return new(dir); }
            var acl = LogAnalyzer.Dfir.Auth.AuthStoreProtection.CheckDirectory(dir);
            if (!acl.Protected && IsElevated())
            {
                var why = LogAnalyzer.Dfir.HashSets.HashSetLibrary.ApplyRestrictedAcl(dir);
                acl = why.Length == 0 ? LogAnalyzer.Dfir.Auth.AuthStoreProtection.CheckDirectory(dir) : new LogAnalyzer.Dfir.Auth.StoreAclResult(false, [why]);
            }
            StoreProblem = acl.Protected ? "" : "depozitul pachetelor are drepturi nesigure (" + string.Join("; ", acl.Problems) + "); nu se folosește. Porniți aplicația ca administrator ca să fie restricționat";
            byte[]? key = null;
            if (acl.Protected)
                try { key = LogAnalyzer.Dfir.Auth.AuthStoreSecurity.Windows().ChainKey(dir).Key; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { key = null; }
            return new(dir, key, @sealed: true, refusal: StoreProblem);
        }

        private static bool IsElevated()
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        public static ICmsDetachedVerifier Verifier => SignedCmsVerifier.Instance;
        public static QualifiedSignatureTrust Trust() => QualifiedSignatureTrust.FromDirectory();
        public static IReadOnlyList<string> TrustedPublishers() => OfficerFlows.Trusted(OfficerStore.Shared.Load()).Select(o => o.Fingerprint).ToList();

        /// <summary>The active, verified package for the detection engine (or why there is none). Never throws: a read error is the reason.</summary>
        public static ActiveThreatIntel Active(string? root = null)
        {
            try
            {
                var store = Store(root);
                return store.LoadActive(Verifier, Trust(), TrustedPublishers());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                return ActiveThreatIntel.None("pachetul de informații despre amenințări nu a putut fi citit: " + ex.Message);
            }
        }
    }
}
