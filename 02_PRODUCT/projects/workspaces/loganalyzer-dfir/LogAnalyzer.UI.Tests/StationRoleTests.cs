using System;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>
    /// WP18 S1: the station role (CONTROL / CSIRT) comes only from the signed policy's <c>role</c> field. Anything missing, invalid, refused or
    /// inconsistent fails closed to CONTROL, and the decision text says why. The role never widens what the edition and the mode allow.
    /// </summary>
    public class StationRoleTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        private static readonly ConnectivitySnapshot Offline = new(ConnectivityState.NoNetwork, "test", Array.Empty<string>(), Now.UtcDateTime);

        private static (ECDsa Key, string Pub) NewKey()
        {
            var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (k, Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
        }

        private static string Policy(ECDsa k, AppMode mode, StationRole? role, long version = 1) =>
            EditionPolicy.Sign(k, mode, version, "2026-10-01", null, "*", "owner", role);

        private static StationRoleDecision Decide(EditionKind edition, PolicyLoadResult policy) =>
            StationRoleResolver.Decide(edition, policy, EditionPolicyVerifier.Decide(policy, Offline, null), Now);

        [Fact]
        public void Policy_without_role_is_valid_and_gives_control_with_an_explicit_reason()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k, AppMode.Network, null), pub, "PC1", Now);
            Assert.True(r.Valid, r.Reason);
            Assert.Null(r.Policy!.Role);
            var d = Decide(EditionKind.Unclassified, r);
            Assert.Equal(StationRole.Control, d.EffectiveRole);
            Assert.Null(d.RequestedRole);
            Assert.Contains(d.Reasons, x => x.Contains("nu specifică rolul", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(r.PolicySha256, d.PolicySha256);
        }

        [Fact]
        public void Signed_csirt_role_on_connected_unclassified_station_is_accepted()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k, AppMode.Network, StationRole.Csirt), pub, "PC1", Now);
            Assert.True(r.Valid, r.Reason);
            var d = Decide(EditionKind.Unclassified, r);
            Assert.Equal(StationRole.Csirt, d.EffectiveRole);
            Assert.Equal(StationRole.Csirt, d.RequestedRole);
            Assert.Equal(AppMode.Network, d.Mode);
            Assert.False(d.HasWarning);
            Assert.Contains("CSIRT", d.BadgeText);
        }

        [Fact]
        public void Csirt_role_on_an_airgapped_station_is_accepted_but_flagged_without_network()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k, AppMode.AirGapped, StationRole.Csirt), pub, "PC1", Now);
            var d = Decide(EditionKind.Unclassified, r);
            Assert.Equal(StationRole.Csirt, d.EffectiveRole);
            Assert.Equal(AppMode.AirGapped, d.Mode);
            Assert.True(d.HasWarning);
            Assert.Contains("fără rețea", d.BadgeText);
        }

        [Fact]
        public void Role_is_part_of_the_signed_text_so_it_cannot_be_added_or_edited_by_hand()
        {
            var (k, pub) = NewKey();
            var withRole = Policy(k, AppMode.Network, StationRole.Control);
            Assert.False(EditionPolicyVerifier.Verify(withRole.Replace("\"control\"", "\"csirt\""), pub, "PC1", Now).Valid);
            var noRole = Policy(k, AppMode.Network, null);
            var injected = noRole.Replace("\"audience\"", "\"role\": \"csirt\",\n  \"audience\"");
            Assert.False(EditionPolicyVerifier.Verify(injected, pub, "PC1", Now).Valid);
        }

        [Fact]
        public void Unknown_role_value_makes_the_policy_invalid_and_the_station_falls_back_to_control_and_airgapped()
        {
            var (k, pub) = NewKey();
            var text = Policy(k, AppMode.Network, StationRole.Csirt).Replace("\"csirt\"", "\"server\"");
            var r = EditionPolicyVerifier.Verify(text, pub, "PC1", Now);
            Assert.False(r.Valid);
            Assert.Contains("rol", r.Reason, StringComparison.OrdinalIgnoreCase);
            var d = Decide(EditionKind.Unclassified, r);
            Assert.Equal(StationRole.Control, d.EffectiveRole);
            Assert.Equal(AppMode.AirGapped, d.Mode);
        }

        [Fact]
        public void Missing_invalid_or_rolled_back_policy_falls_back_to_control()
        {
            var (k, pub) = NewKey();
            Assert.Equal(StationRole.Control, Decide(EditionKind.Unclassified, EditionPolicyVerifier.Verify(null, pub, "PC1", Now)).EffectiveRole);
            var (other, _) = NewKey();
            Assert.Equal(StationRole.Control, Decide(EditionKind.Unclassified, EditionPolicyVerifier.Verify(Policy(other, AppMode.Network, StationRole.Csirt), pub, "PC1", Now)).EffectiveRole);
            var old = EditionPolicyVerifier.Verify(Policy(k, AppMode.Network, StationRole.Csirt, version: 2), pub, "PC1", Now, highWaterVersion: 3);
            Assert.False(old.Valid);
            Assert.Equal(StationRole.Control, Decide(EditionKind.Unclassified, old).EffectiveRole);
        }

        [Fact]
        public void Classified_edition_is_always_control_and_a_csirt_request_is_refused_with_a_reason()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k, AppMode.AirGapped, StationRole.Csirt), pub, "PC1", Now);
            Assert.True(r.Valid);
            var mode = new ModeDecision(AppMode.AirGapped, false, "P1", Offline);
            var d = StationRoleResolver.Decide(EditionKind.Classified, r, mode, Now);
            Assert.Equal(StationRole.Control, d.EffectiveRole);
            Assert.Equal(StationRole.Csirt, d.RequestedRole);
            Assert.Contains(d.Reasons, x => x.Contains("clasificat", StringComparison.OrdinalIgnoreCase) && x.Contains("respins", StringComparison.OrdinalIgnoreCase));
            // and with no policy at all
            var none = StationRoleResolver.Decide(EditionKind.Classified, null, mode, Now);
            Assert.Equal(StationRole.Control, none.EffectiveRole);
            Assert.Null(none.RequestedRole);
        }

        [Fact]
        public void Context_is_set_once_and_defaults_to_control_when_uninitialised()
        {
            StationRoleContext.ResetForTests();
            Assert.Equal(StationRole.Control, StationRoleContext.Current.EffectiveRole);
            var (k, pub) = NewKey();
            var d = Decide(EditionKind.Unclassified, EditionPolicyVerifier.Verify(Policy(k, AppMode.Network, StationRole.Csirt), pub, "PC1", Now));
            StationRoleContext.Initialize(d);
            Assert.Equal(StationRole.Csirt, StationRoleContext.Current.EffectiveRole);
            Assert.Throws<InvalidOperationException>(() => StationRoleContext.Initialize(Decide(EditionKind.Unclassified, EditionPolicyVerifier.Verify(null, pub, "PC1", Now))));
            StationRoleContext.ResetForTests();
        }

        [Fact]
        public void Decision_summary_names_edition_mode_policy_and_every_reason()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k, AppMode.Network, StationRole.Csirt), pub, "PC1", Now);
            var d = Decide(EditionKind.Unclassified, r);
            Assert.Contains("neclasificat", d.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("conectat", d.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(r.PolicySha256![..12], d.Summary);
            foreach (var reason in d.Reasons) Assert.Contains(reason, d.Summary);
        }
    }
}
