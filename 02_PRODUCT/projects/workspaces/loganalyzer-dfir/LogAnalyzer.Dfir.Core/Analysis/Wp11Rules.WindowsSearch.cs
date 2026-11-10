using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Analysis;

public static partial class Wp11Rules
{
    // ---------------------------------------------------------------- WSEARCH-GATHER-ONLY
    // Windows Search keeps a row per known file in Windows-gather.db (SystemIndex_Gthr, DocumentID) and the properties of the same file in Windows.db
    // (SystemIndex_1_PropertyStore, WorkId = DocumentID). Experiment (Windows 11 26100): after a file was deleted its Windows.db rows were gone, the gatherer row stayed.
    // A gatherer row whose DocumentID has no WorkId in the Windows.db read AT THE SAME MOMENT is a file the indexer knew and no longer describes: possibly deleted or
    // moved after indexing, or not processed yet. "Same moment" is a key the collector / the remote import writes only when both databases came from one shadow copy; two
    // databases from different moments would give hundreds of false "deleted", so without a common key nothing is emitted (fail closed). The rule is Low and proves no
    // deletion (no technique): it points at files to check in $MFT / $UsnJrnl / the Recycle Bin.

    /// <summary>Event field the Windows Search parsers stamp (only for a pair copied from one shadow copy) with the id of that moment.</summary>
    public const string SearchMomentField = "Moment index";
    /// <summary>Set to "da" by the Windows.db parser on one event of a read that is not complete (cap, damage, journal not applied): the comparison would be false.</summary>
    public const string SearchIncompleteField = "Moment index incomplet";

    private const string SearchStoreSource = "WindowsSearch", SearchGatherSource = "WindowsSearchGather";

    private static void WindowsSearchGatherOnly(Ctx c, List<Finding> o)
    {
        var tagged = c.Events.Where(e => (e.Source.Equals(SearchStoreSource, StringComparison.OrdinalIgnoreCase) || e.Source.Equals(SearchGatherSource, StringComparison.OrdinalIgnoreCase))
                                         && F(e, SearchMomentField).Length > 0).ToList();
        if (tagged.Count == 0) return;
        var items = new List<AdItem>();
        // a moment key is only meaningful on one host: the same key on two hosts is two different moments
        foreach (var moment in tagged.GroupBy(e => (Host: e.Host.Trim().ToUpperInvariant(), Key: F(e, SearchMomentField)), new MomentComparer()))
        {
            var store = moment.Where(e => e.Source.Equals(SearchStoreSource, StringComparison.OrdinalIgnoreCase)).ToList();
            var gather = moment.Where(e => e.Source.Equals(SearchGatherSource, StringComparison.OrdinalIgnoreCase)).ToList();
            // one of the two missing, or the Windows.db read incomplete: no comparison
            if (store.Count == 0 || gather.Count == 0 || store.Any(e => F(e, SearchIncompleteField) == "da")) continue;
            var known = store.Select(e => F(e, "WorkId").Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
            foreach (var g in gather)
            {
                var doc = F(g, "DocumentID").Trim();
                if (doc.Length == 0 || known.Contains(doc) || F(g, "IsScopePattern") == "da") continue;
                var item = g;
                items.Add(new AdItem(Severity.Low, T(g), T(g), string.IsNullOrEmpty(g.Path) ? doc : g.Path,
                    Ref(g, "rând Windows-gather.db DocumentID " + doc), () => MakeSearchGatherOnly(c, item, store.Count)));
            }
        }
        EmitCapped(c, o, items, "WSEARCH-GATHER-ONLY", "File Activity", "", "fișier cunoscut doar de gatherer", "fișiere cunoscute doar de gatherer",
            why: "Ce s-a întâmplat: multe rânduri Windows-gather.db nu au intrare în Windows.db din același moment. De ce contează: pot fi fișiere șterse sau mutate recent, ori fișiere noi încă neprocesate de indexator. Ce verificați: folderele comune ale căilor și $UsnJrnl pentru aceeași perioadă.",
            alternative: "Indexatorul are mult de lucru în așteptare (fișiere noi încă neprocesate), de exemplu după o copiere mare.");
    }

    private sealed class MomentComparer : IEqualityComparer<(string Host, string Key)>
    {
        public bool Equals((string Host, string Key) x, (string Host, string Key) y) => x.Host == y.Host && x.Key.Equals(y.Key, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Host, string Key) v) => HashCode.Combine(v.Host, v.Key.ToUpperInvariant());
    }

    private static Finding MakeSearchGatherOnly(Ctx c, TimelineEvent e, int storeRows)
    {
        var path = e.Path; var doc = F(e, "DocumentID").Trim(); var when = T(e);
        return new Finding
        {
            FindingId = c.NextId(), RuleId = "WSEARCH-GATHER-ONLY",
            Title = $"Fișier cunoscut indexatorului Windows Search, fără intrare în magazinul de proprietăți: {PlainPhrases.Q(PlainPhrases.Leaf(path))}",
            Severity = Severity.Low, Category = "File Activity",
            Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "", SemanticType = SemanticType.Observation,
            FirstSeenUtc = when, LastSeenUtc = when, File = path,
            PlainWhat = $"indexatorul Windows Search (gatherer) cunoștea fișierul {PlainPhrases.Q(PlainPhrases.Leaf(path))}, dar magazinul de proprietăți din același moment nu îl mai are",
            Description = $"Rândul Windows-gather.db cu DocumentID {doc}: {path}, ultima modificare {Time(when)}. Același DocumentID nu apare ca WorkId între cele {storeRows} intrări ale Windows.db copiat în același moment. " +
                          "Ce s-a întâmplat: fișierul era cunoscut indexatorului, dar nu mai are intrare în magazinul de proprietăți la același moment; posibil șters sau mutat după indexare, ori încă neprocesat de indexator. " +
                          "De ce contează: într-un experiment pe Windows 11, după ștergerea unui fișier rândul din Windows-gather.db a rămas, iar cele din Windows.db au dispărut; rândul rămâne însă puțin (în laborator a dispărut în mai puțin de o oră), deci arată mai ales ștergeri recente. " +
                          "Ce verificați: $MFT, $UsnJrnl și Coșul de reciclare pentru această cale.",
            ClassificationReason = "DocumentID din Windows-gather.db fără WorkId în Windows.db, ambele copiate din aceeași copie de umbră (cheie de moment comună); nu dovedește ștergerea.",
            SupportingEvidence = [Ref(e, "rând Windows-gather.db")],
            AlternativeExplanations =
            [
                "Fișierul a fost adăugat recent și indexatorul nu i-a scris încă proprietățile în Windows.db.",
                "Fișierul a fost mutat sau redenumit; rândul vechi a rămas în gatherer.",
                "Rândul poate fi plantat într-o bază gather modificată de un atacator; baza vine de pe stația analizată.",
            ],
            MissingEvidence = ["Dovada ștergerii sau mutării ($MFT, $UsnJrnl, Coșul de reciclare).", "Cât timp rămâne exact un rând gather după ștergere nu se știe (în laborator: mai puțin de o oră)."],
            RecommendedNextSteps = ["Căutați calea în $MFT și $UsnJrnl și în Coșul de reciclare.", "Verificați dacă fișierul mai există sub alt nume sau în alt folder."],
        };
    }
}
