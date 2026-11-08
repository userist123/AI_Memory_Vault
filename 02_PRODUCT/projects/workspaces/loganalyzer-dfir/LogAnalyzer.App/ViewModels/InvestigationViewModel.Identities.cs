using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LogAnalyzer.Dfir.Identity;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>Master plan 6.10, „Identități” tab: accounts by SID across DCs, servers and workstations, their sessions, directory and GPO changes, and the gaps.</summary>
    public partial class InvestigationViewModel
    {
        public ObservableCollection<IdentityRecord> IdentityRows { get; } = new();
        public ObservableCollection<string> IdentityDetail { get; } = new();
        public ObservableCollection<string> IdentityGaps { get; } = new();
        public ObservableCollection<string> IdentityDirectory { get; } = new();
        [ObservableProperty] private IdentityRecord? _selectedIdentity;
        [ObservableProperty] private string _identityLine = "Nicio identitate: cazul nu are evenimente de securitate Windows.";
        [ObservableProperty] private bool _identityPrivilegedOnly;

        internal void FillIdentities()
        {
            IdentityRows.Clear(); IdentityDetail.Clear(); IdentityGaps.Clear(); IdentityDirectory.Clear();
            var rep = _result?.Identity;
            if (rep is null)
            {
                IdentityLine = _result is null ? "Nicio identitate." : "Identitățile nu au fost reconstituite pentru acest caz (analizat cu o versiune anterioară). Reanalizați cazul din „Rulări și diferențe”.";
                return;
            }
            foreach (var i in rep.Identities.Where(i => !IdentityPrivilegedOnly || i.Privileged)) IdentityRows.Add(i);
            foreach (var g in rep.Gaps) IdentityGaps.Add($"⚠ {g.What} {g.Why}");
            foreach (var c in rep.DirectoryChanges.OrderByDescending(c => c.IsGpo).Take(500))
                IdentityDirectory.Add($"{(c.IsGpo ? "GPO · " : "")}{c.Utc} · {c.Dc} · {c.Operation} · {c.ObjectDn}{(c.Attribute.Length > 0 ? $" · {c.Attribute}" : "")} · de {c.ActorName} ({c.ActorSid}) · {c.EvidenceId} {c.Locator}");
            int gpo = rep.DirectoryChanges.Count(c => c.IsGpo);
            IdentityLine = $"{LogAnalyzer.Dfir.Language.RoText.Count(rep.Identities.Count, "identitate", "identități")} ({rep.Identities.Count(i => i.Privileged)} privilegiate), "
                + $"{LogAnalyzer.Dfir.Language.RoText.Count(rep.Sessions.Count, "sesiune", "sesiuni")}, {LogAnalyzer.Dfir.Language.RoText.Count(rep.DirectoryChanges.Count, "modificare de director", "modificări de director")}"
                + (gpo > 0 ? $" (dintre care {gpo} GPO)" : "")
                + (rep.DomainControllers.Count > 0 ? $"; controlere de domeniu: {string.Join(", ", rep.DomainControllers)}" : "; fără jurnale de controler de domeniu")
                + (rep.DuplicatesDropped > 0 ? $"; {rep.DuplicatesDropped} evenimente duplicate numărate o dată" : "") + ".";
            SelectedIdentity = IdentityRows.FirstOrDefault();
        }

        partial void OnIdentityPrivilegedOnlyChanged(bool value) => FillIdentities();

        partial void OnSelectedIdentityChanged(IdentityRecord? value)
        {
            IdentityDetail.Clear();
            var rep = _result?.Identity;
            if (value is null || rep is null) return;
            IdentityDetail.Add(value.KnownByNameOnly
                ? $"{value.DisplayName}: văzut doar după nume, fără SID. Nu este legat de alt cont doar pentru că numele seamănă."
                : $"{value.DisplayName} · SID {value.Sid}" + (value.SidHistory.Count > 0 ? $" · istoric SID: {string.Join(", ", value.SidHistory)}" : ""));
            if (value.Names.Count > 1) IdentityDetail.Add("Nume în timp (SID-ul rămâne cheia): " + string.Join(" → ", value.Names.Select(n => n.Name + (n.FirstSeenUtc.Length > 0 ? $" ({n.FirstSeenUtc[..16].Replace('T', ' ')})" : ""))));
            if (value.Privileged) IdentityDetail.Add("★ Privilegiat: " + value.PrivilegedReason);
            IdentityDetail.Add($"{value.Logons} autentificări reușite, {value.FailedLogons} eșuate · stații: {string.Join(", ", value.Hosts)} · între {value.FirstSeenUtc} și {value.LastSeenUtc}");
            foreach (var s in rep.Sessions.Where(s => s.Sid == value.Key).Take(60))
                IdentityDetail.Add($"Sesiune pe {s.Host} (LogonId {s.LogonId}, tip {s.LogonType}{(s.SourceIp.Length > 0 ? ", din " + s.SourceIp : "")}): {(s.StartUtc.Length > 0 ? s.StartUtc : "început necunoscut")} → {(s.EndUtc.Length > 0 ? s.EndUtc : "sfârșit necunoscut")}"
                    + (s.Elevated ? " · cu privilegii speciale" : "") + $" · încredere {s.Confidence}" + (s.Note.Length > 0 ? $" · {s.Note}" : ""));
            foreach (var a in value.Activity.Take(300))
                IdentityDetail.Add($"{(a.Utc.Length > 0 ? a.Utc : "fără oră")} · {a.Host} · {a.EventId} · {a.Text} · {a.EvidenceId} {a.Locator}");
            if (value.Activity.Count > 300 || value.ActivityTruncated) IdentityDetail.Add($"… lista este scurtată; toată activitatea este în Analysis/identity.json și în cronologie.");
        }
    }
}
