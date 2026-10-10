using System;
using System.Linq;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Core.Services.Edition;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>WP18 S2: the role profile is data. At most five primary intents, written for users; unavailable ones stay listed with the reason.</summary>
    public class RoleProfileTests
    {
        private static readonly IEditionProfile P1 = new ClassifiedEditionProfile();
        private static readonly IEditionProfile P23 = new UnclassifiedEditionProfile();

        [Theory]
        [InlineData(StationRole.Control, EditionKind.Classified, AppMode.AirGapped)]
        [InlineData(StationRole.Control, EditionKind.Unclassified, AppMode.AirGapped)]
        [InlineData(StationRole.Csirt, EditionKind.Unclassified, AppMode.AirGapped)]
        [InlineData(StationRole.Csirt, EditionKind.Unclassified, AppMode.Network)]
        public void Every_profile_has_at_most_five_primary_intents_with_title_description_and_page(StationRole role, EditionKind kind, AppMode mode)
        {
            var edition = kind == EditionKind.Classified ? P1 : P23;
            var p = RoleProfiles.For(role, edition, mode);
            Assert.Equal(role, p.Role);
            Assert.Equal(RoleProfiles.MaxPrimary, p.PrimaryIntents.Count);
            Assert.Empty(RoleProfiles.Violations(p, edition));
            foreach (var i in p.PrimaryIntents.Concat(p.MoreIntents))
            {
                Assert.False(string.IsNullOrWhiteSpace(i.Title));
                Assert.False(string.IsNullOrWhiteSpace(i.Description));
                Assert.InRange(i.TargetTab, 0, RoleProfiles.TabHome);
                if (!i.Enabled) Assert.False(string.IsNullOrWhiteSpace(i.Reason));
            }
            Assert.Equal(p.PrimaryIntents.Select(i => i.Key).Distinct().Count(), p.PrimaryIntents.Count);
            Assert.NotEmpty(p.Navigation);
        }

        [Fact]
        public void Control_profile_offers_the_control_intents_and_no_network_intent()
        {
            var p = RoleProfiles.For(StationRole.Control, P1, AppMode.AirGapped);
            Assert.Equal(new[] { "Verifică această stație", "Verifică un suport (USB, CD/DVD)", "Cine a lucrat și când", "Deschide un control anterior", "Raport pentru proces-verbal" },
                p.PrimaryIntents.Select(i => i.Title));
            Assert.All(p.PrimaryIntents, i => Assert.True(i.Enabled, i.Reason));
            Assert.DoesNotContain(p.PrimaryIntents.Concat(p.MoreIntents), i => i.Intent.RequiresNetwork);
            Assert.Equal(RoleProfiles.TabStationControl, p.PrimaryIntents[0].TargetTab);
            Assert.Equal(IntentSource.OpenExistingCase, p.PrimaryIntents[3].Intent.Source);
        }

        [Fact]
        public void Csirt_profile_on_a_connected_unclassified_station_has_everything_enabled()
        {
            var p = RoleProfiles.For(StationRole.Csirt, P23, AppMode.Network);
            Assert.Equal("Primește probe de la o stație", p.PrimaryIntents[0].Title);
            Assert.Equal(IntentSource.ImportEvidence, p.PrimaryIntents[0].Intent.Source);
            Assert.All(p.PrimaryIntents.Concat(p.MoreIntents), i => Assert.True(i.Enabled, i.Reason));
            Assert.Contains(p.MoreIntents, i => i.Key == "domain_mail" && i.Enabled);
            Assert.All(p.Navigation, n => Assert.True(n.Enabled));
        }

        [Fact]
        public void Csirt_without_network_keeps_the_domain_intent_visible_but_disabled_with_the_reason()
        {
            var p = RoleProfiles.For(StationRole.Csirt, P23, AppMode.AirGapped);
            var domain = Assert.Single(p.MoreIntents, i => i.Key == "domain_mail");
            Assert.False(domain.Enabled);
            Assert.Contains("izolată", domain.Reason);
            var nav = Assert.Single(p.Navigation, n => n.Tab == RoleProfiles.TabDomainMail);
            Assert.False(nav.Enabled);
            Assert.Contains(nav.Reason, nav.Tooltip);
            // what_now needs host response, which the unclassified edition has: enabled even without network
            Assert.True(Assert.Single(p.PrimaryIntents, i => i.Key == "what_now").Enabled);
        }

        [Fact]
        public void Classified_edition_disables_host_and_network_intents_with_the_edition_text()
        {
            var p = RoleProfiles.For(StationRole.Csirt, P1, AppMode.AirGapped);   // the resolver never yields this, the profile must still be safe
            var whatNow = Assert.Single(p.PrimaryIntents, i => i.Key == "what_now");
            Assert.False(whatNow.Enabled);
            Assert.Equal(EditionText.NotAvailableClassified, whatNow.Reason);
            Assert.Empty(RoleProfiles.Violations(p, P1));
        }
    }
}
