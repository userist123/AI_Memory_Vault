using System;
using System.Collections.Generic;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>
    /// The role of the PC the application runs on (WP18). It is the fourth axis next to edition (P1 / P2-P3), mode (air-gapped / connected)
    /// and account role (administrator / operator). It comes only from the signed policy; it never widens what the edition and the mode allow,
    /// it cannot be switched at run time, and anything missing, invalid or inconsistent falls back to <see cref="Control"/>.
    /// </summary>
    public enum StationRole
    {
        /// <summary>"Stație de control": compliance checks on the classified-information line, on air-gapped PCs. The restrictive default.</summary>
        Control,
        /// <summary>"Stație de sprijin răspuns la incidente": the PC of the incident-response centre (CSIRT).</summary>
        Csirt,
    }

    public static class StationRoles
    {
        public const string ControlName = "control";
        public const string CsirtName = "csirt";

        public static string Name(StationRole role) => role == StationRole.Csirt ? CsirtName : ControlName;

        /// <summary>
        /// The <c>role</c> field of a policy file WITHOUT verifying the signature. Only for reporting what was requested on an edition that cannot
        /// verify policies (P1); never used to grant a role.
        /// </summary>
        public static StationRole? PeekUnverified(string? policyText)
        {
            if (string.IsNullOrWhiteSpace(policyText)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(policyText);
                return doc.RootElement.TryGetProperty("role", out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String && TryParse(el.GetString(), out var r) ? r : null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        public static bool TryParse(string? text, out StationRole role)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case ControlName: role = StationRole.Control; return true;
                case CsirtName: role = StationRole.Csirt; return true;
                default: role = StationRole.Control; return false;
            }
        }

        /// <summary>The user-facing name of the role (plain language; the technical word stays in the tooltip).</summary>
        public static string Human(StationRole role) =>
            role == StationRole.Csirt ? "Stație de sprijin răspuns la incidente" : "Stație de control";
    }

    /// <summary>
    /// How the station role was decided for this process: edition → signed policy → role → consistency checks → effective role.
    /// Every step is a sentence in <see cref="Reasons"/>; <see cref="Summary"/> is the complete text for the startup log and the badge tooltip.
    /// </summary>
    public sealed record StationRoleDecision(
        StationRole EffectiveRole,
        StationRole? RequestedRole,
        EditionKind Edition,
        AppMode Mode,
        string? PolicySha256,
        IReadOnlyList<string> Reasons,
        DateTimeOffset DecidedAtUtc)
    {
        /// <summary>True when the role is accepted but the station cannot do everything the role is meant for (CSIRT without network).</summary>
        public bool HasWarning => EffectiveRole == StationRole.Csirt && Mode == AppMode.AirGapped;

        /// <summary>Short text for the header badge.</summary>
        public string BadgeText => EffectiveRole == StationRole.Csirt
            ? (Mode == AppMode.AirGapped ? "CSIRT fără rețea" : "CSIRT")
            : "STAȚIE DE CONTROL";

        public string HumanRole => StationRoles.Human(EffectiveRole);

        public string Summary =>
            $"Rol de stație: {HumanRole} ({StationRoles.Name(EffectiveRole)}). Ediție: {(Edition == EditionKind.Classified ? "clasificată (P1)" : "neclasificată (P2/P3)")}. " +
            $"Mod: {(Mode == AppMode.AirGapped ? "izolat (air-gap)" : "conectat")}. " +
            (PolicySha256 is null ? "Politică semnată: niciuna acceptată. " : $"Politică semnată acceptată: SHA-256 {PolicySha256[..12]}. ") +
            string.Join(" ", Reasons);
    }

    public static class StationRoleResolver
    {
        /// <summary>
        /// Decides the station role. Fail-closed: the only way to obtain <see cref="StationRole.Csirt"/> is a valid signed policy, on the
        /// unclassified edition, whose <c>role</c> is <c>csirt</c>. The classified edition is always CONTROL; a policy asking otherwise is
        /// refused with a reason, never silently.
        /// </summary>
        /// <param name="policy">Result of <see cref="EditionPolicyVerifier.Verify"/>; null when the edition does not read policies (P1).</param>
        /// <param name="unverifiedRequest">What an unverified policy file asks for (P1 only reports it; it never grants it).</param>
        public static StationRoleDecision Decide(EditionKind edition, PolicyLoadResult? policy, ModeDecision mode, DateTimeOffset nowUtc,
            StationRole? unverifiedRequest = null)
        {
            var reasons = new List<string>();
            var requested = policy?.Policy?.Role ?? unverifiedRequest;
            string? sha = policy is { Valid: true } ? policy.PolicySha256 : null;

            if (edition == EditionKind.Classified)
            {
                reasons.Add("Ediția clasificată (P1) rulează mereu ca Stație de control: nu are rețea, AI sau acțiuni pe gazdă compilate.");
                if (requested == StationRole.Csirt)
                    reasons.Add("Politica cere rolul CSIRT; cererea a fost respinsă pentru că ediția clasificată nu poate avea acest rol" +
                                (policy is { Valid: true } ? "." : " (politica nici nu este verificată în această ediție)."));
                else if (policy is not null)
                    reasons.Add(policy.Valid ? "Politica nu schimbă nimic în ediția clasificată." : $"Politica nu a fost acceptată: {policy.Reason}");
                return new StationRoleDecision(StationRole.Control, requested, edition, AppMode.AirGapped, sha, reasons, nowUtc);
            }

            if (policy is not { Valid: true })
            {
                reasons.Add($"Fără politică semnată validă rolul este Stație de control (implicit sigur): {policy?.Reason ?? "nu s-a citit nicio politică."}");
                return new StationRoleDecision(StationRole.Control, null, edition, AppMode.AirGapped, null, reasons, nowUtc);
            }

            if (requested is null)
            {
                reasons.Add("Politica semnată nu specifică rolul de stație; se folosește rolul implicit Stație de control.");
                return new StationRoleDecision(StationRole.Control, null, edition, mode.Mode, sha, reasons, nowUtc);
            }

            reasons.Add($"Rolul „{StationRoles.Name(requested.Value)}” este stabilit de politica semnată v{policy.Policy!.Version} (semnatar {policy.Policy.Signer}).");
            if (requested == StationRole.Csirt && mode.Mode == AppMode.AirGapped)
                reasons.Add("Politica cere rolul CSIRT, dar modul este izolat: rolul este acceptat, funcțiile de rețea rămân blocate (CSIRT fără rețea).");
            return new StationRoleDecision(requested.Value, requested, edition, mode.Mode, sha, reasons, nowUtc);
        }
    }

    /// <summary>What the edition composition decides at startup: the mode and the station role, from the same policy read.</summary>
    public sealed record StartupDecision(ModeDecision Mode, StationRoleDecision Role);

    /// <summary>The station role chosen at startup. Set once; never switched while the application runs.</summary>
    public static class StationRoleContext
    {
        private static StationRoleDecision? _current;

        public static StationRoleDecision Current =>
            _current ?? new StationRoleDecision(StationRole.Control, null, EditionKind.Unclassified, AppMode.AirGapped, null,
                new[] { "Rolul de stație nu a fost inițializat; implicit Stație de control." }, DateTimeOffset.MinValue);

        public static StationRole Role => Current.EffectiveRole;
        public static bool IsControl => Role == StationRole.Control;
        public static bool IsCsirt => Role == StationRole.Csirt;

        public static void Initialize(StationRoleDecision decision)
        {
            if (_current is not null && !ReferenceEquals(_current, decision))
                throw new InvalidOperationException("Rolul de stație a fost deja stabilit pentru această sesiune.");
            _current = decision;
        }

        /// <summary>For tests only: the application itself never resets the role.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void ResetForTests() => _current = null;
    }
}
