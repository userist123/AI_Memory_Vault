using System;
using System.Security.Cryptography;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Signed edition policy: only a valid signature from the embedded key can select the connected mode; everything else is AirGapped.</summary>
    public class EditionPolicyTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        private static readonly ConnectivitySnapshot Online = new(ConnectivityState.Internet, "test", Array.Empty<string>(), Now.UtcDateTime);

        private static (ECDsa Key, string Pub) NewKey()
        {
            var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (k, Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));
        }

        private static string Policy(ECDsa k, AppMode mode = AppMode.Network, long version = 1, string notBefore = "2026-10-01", string? notAfter = null, string audience = "*") =>
            EditionPolicy.Sign(k, mode, version, notBefore, notAfter, audience, "owner");

        [Fact]
        public void Valid_connected_policy_selects_network_and_records_the_hash()
        {
            var (k, pub) = NewKey();
            var r = EditionPolicyVerifier.Verify(Policy(k), pub, "PC1", Now);
            Assert.True(r.Valid, r.Reason);
            var d = EditionPolicyVerifier.Decide(r, Online, null);
            Assert.Equal(AppMode.Network, d.Mode);
            Assert.Contains(r.PolicySha256![..12], d.Reason);
        }

        [Fact]
        public void Missing_policy_or_missing_key_fails_closed_to_airgapped()
        {
            var (k, pub) = NewKey();
            Assert.Equal(AppMode.AirGapped, EditionPolicyVerifier.Decide(EditionPolicyVerifier.Verify(null, pub, "PC1", Now), Online, null).Mode);
            Assert.Equal(AppMode.AirGapped, EditionPolicyVerifier.Decide(EditionPolicyVerifier.Verify(Policy(k), null, "PC1", Now), Online, null).Mode);
            Assert.Equal(AppMode.AirGapped, EditionPolicyVerifier.Decide(EditionPolicyVerifier.Verify("not json", pub, "PC1", Now), Online, null).Mode);
        }

        [Fact]
        public void Tampered_or_foreign_signed_policy_is_refused()
        {
            var (k, pub) = NewKey();
            var (other, _) = NewKey();
            var text = Policy(k, AppMode.AirGapped);
            Assert.False(EditionPolicyVerifier.Verify(text.Replace("\"airgapped\"", "\"connected\""), pub, "PC1", Now).Valid);   // operator edits the mode
            Assert.False(EditionPolicyVerifier.Verify(Policy(other), pub, "PC1", Now).Valid);                                    // signed by another key
        }

        [Fact]
        public void Expired_future_other_station_and_rolled_back_policies_are_refused()
        {
            var (k, pub) = NewKey();
            Assert.False(EditionPolicyVerifier.Verify(Policy(k, notAfter: "2026-10-05"), pub, "PC1", Now).Valid);
            Assert.False(EditionPolicyVerifier.Verify(Policy(k, notBefore: "2026-11-01"), pub, "PC1", Now).Valid);
            Assert.False(EditionPolicyVerifier.Verify(Policy(k, audience: "PC2"), pub, "PC1", Now).Valid);
            Assert.True(EditionPolicyVerifier.Verify(Policy(k, audience: "pc1"), pub, "PC1", Now).Valid);
            Assert.False(EditionPolicyVerifier.Verify(Policy(k, version: 2), pub, "PC1", Now, highWaterVersion: 3).Valid);
        }

        [Fact]
        public void Operator_requests_are_ignored_and_reported()
        {
            var (k, pub) = NewKey();
            var none = EditionPolicyVerifier.Verify(null, pub, "PC1", Now);
            var d = EditionPolicyVerifier.Decide(none, Online, EditionPolicyVerifier.RequestedOverride(new[] { "--mode=network" }, null));
            Assert.Equal(AppMode.AirGapped, d.Mode);
            Assert.Contains("ignorată", d.Reason);
            // a valid airgapped policy also wins over a request for network
            var air = EditionPolicyVerifier.Verify(Policy(k, AppMode.AirGapped), pub, "PC1", Now);
            Assert.Equal(AppMode.AirGapped, EditionPolicyVerifier.Decide(air, Online, "LogAnalyzer.mode=network").Mode);
        }

        [Fact]
        public void Classified_profile_has_no_optional_feature_unless_compiled_in()
        {
            var p = new ClassifiedEditionProfile();
            foreach (var f in Enum.GetValues<EditionFeature>()) Assert.False(p.Has(f));
            Assert.True(new ClassifiedEditionProfile(new[] { EditionFeature.AiAnalysis }).Has(EditionFeature.AiAnalysis));
            foreach (var f in Enum.GetValues<EditionFeature>()) Assert.True(new UnclassifiedEditionProfile().Has(f));
        }

        [Fact]
        public void Unavailable_stand_ins_refuse_without_doing_anything()
        {
            var h = new UnavailableHostDefense();
            var r = h.IsolateHostFromNetwork();
            Assert.False(r.Success);
            Assert.Equal(LogAnalyzer.Core.Interfaces.DefenseActionResult.Unavailable, r.Status);
            Assert.Contains("ediția clasificată", r.Message);
            Assert.False(new UnavailableSyslogReceiver().IsSyslogListenerActive);
            Assert.Null(new NoLiveEventSourceFactory().Create());
        }
    }
}
