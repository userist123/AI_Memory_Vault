using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using static LogAnalyzer.Dfir.Analysis.Wp11Rules;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// NTFS-TIMESTOMP (T1070.006): the creation times of a file look changed after the file was created. Decided by <see cref="NtfsTimestomp"/> (shared with AF08).
/// High = the USN journal confirms it; Medium = only the $MFT shows it. A file name is always shown through <see cref="MftParser.SafeText"/>.
/// </summary>
public static partial class Wp14Rules
{
    internal const string TimestompRuleId = NtfsTimestomp.RuleId;
    internal const int TimestompFindingCap = 50;

    internal static void TimestompFindings(Ctx c, List<Finding> o)
    {
        var hits = NtfsTimestomp.Detect(c.Events);
        if (hits.Count == 0) return;
        foreach (var h in hits.Take(TimestompFindingCap)) o.Add(TimestompFinding(c, h));
        if (hits.Count <= TimestompFindingCap) return;

        var rest = hits.Skip(TimestompFindingCap).ToList();
        int high = rest.Count(h => h.Level == NtfsTimestomp.Level.High), low = rest.Count(h => h.Level == NtfsTimestomp.Level.Low), medium = rest.Count - high - low;
        o.Add(new Finding
        {
            FindingId = c.NextId(), RuleId = TimestompRuleId, Title = "Încă fișiere cu orele de creare care par schimbate",
            PlainWhat = $"are încă {rest.Count} fișiere cu orele de creare mai vechi decât momentul în care Windows le-a creat; nu sunt detaliate aici",
            Severity = Severity.Low, Category = "Defense Evasion", Classification = Classification.Candidate, Confidence = Confidence.Low, MitreTechniqueId = "T1070.006",
            Description = $"Pe lângă cele {TimestompFindingCap} constatări de mai sus mai sunt {rest.Count} fișiere cu același tipar ({high} confirmate de jurnalul USN, {medium} doar din $MFT, {low} doar din $MFT cu nume scurt, posibil tunelare). " +
                          "Au fost numărate, nu detaliate: plafonul constatărilor este " + TimestompFindingCap + ". Ele sunt în aceleași evenimente $MFT și USN.",
            SupportingEvidence = rest.Take(20).Select(h => Ref(h.Mft, "înregistrare $MFT cu orele $SI mai vechi decât $FILE_NAME")).ToList(),
            SemanticType = SemanticType.Observation,
            Limitations = ["Constatare de sinteză: fiecare dintre aceste fișiere are aceleași limite ca o constatare obișnuită NTFS-TIMESTOMP."],
        });
    }

    private static Finding TimestompFinding(Ctx c, NtfsTimestomp.Hit h)
    {
        var name = MftParser.SafeText(h.Path);
        bool high = h.Level == NtfsTimestomp.Level.High, low = h.Level == NtfsTimestomp.Level.Low;
        bool rename = h.Kind == NtfsTimestomp.Kind.Rename;
        const string Closing = "Constatarea arată ce a rămas în urmă, nu cine a schimbat orele și de ce.";
        string description;
        if (high && !rename)
            description = $"Ora de creare din $STANDARD_INFORMATION ({NtfsTimestomp.Iso(h.SiCreated)}) este cu mult înaintea celei din $FILE_NAME ({NtfsTimestomp.Iso(h.FnCreated)}), iar jurnalul USN arată o schimbare a orelor într-o operație separată, după crearea fișierului {name}. " +
                          "Orele de creare par să fi fost schimbate după crearea fișierului. Ora de schimbare a înregistrării $MFT nu este mai veche decât crearea, deci nu este tiparul unei copieri cu păstrarea metadatelor (robocopy /COPY:DAT, Copy-Item). " + Closing;
        else if (high)
            description = $"Jurnalul USN arată pentru fișierul {name}, în ordine, crearea, o operație separată de schimbare a atributelor de bază și o redenumire sau mutare. Ora de creare din $STANDARD_INFORMATION ({NtfsTimestomp.Iso(h.SiCreated)}) este cu peste 24 de ore înaintea creării din jurnal ({NtfsTimestomp.Iso(h.CreateAt!.Value)}). " +
                          "Redenumirea sau mutarea a copiat orele $STANDARD_INFORMATION în $FILE_NAME, de aceea $MFT singur nu mai arată nicio nepotrivire. Orele de creare par să fi fost schimbate după crearea fișierului. " + Closing;
        else if (low)
            description = $"În $MFT, fișierul {name} are ora de creare din $STANDARD_INFORMATION ({NtfsTimestomp.Iso(h.SiCreated)}) cu mult înaintea celei din $FILE_NAME ({NtfsTimestomp.Iso(h.FnCreated)}), dar înregistrarea are și un nume scurt 8.3, deci este posibilă tunelarea: " +
                          "o salvare cu fișier temporar moștenește ora de creare a fișierului vechi. " + (h.HasUsn ? "Jurnalul USN al acestui fișier nu arată o schimbare separată a orelor după creare. " : "Nu există jurnal USN pentru acest fișier care să confirme sau să infirme. ") + Closing;
        else
            description = $"În $MFT, fișierul {name} are ora de creare din $STANDARD_INFORMATION ({NtfsTimestomp.Iso(h.SiCreated)}) cu mult înaintea celei din $FILE_NAME ({NtfsTimestomp.Iso(h.FnCreated)}), iar ora de schimbare a înregistrării nu arată o copiere. " +
                          (h.HasUsn ? "Jurnalul USN al acestui fișier nu arată o schimbare separată a orelor după creare, deci nu confirmă. " : "Nu există jurnal USN pentru acest fișier care să confirme. ") +
                          "Orele de creare par să fi fost schimbate după crearea fișierului, dar arhivatoarele care restaurează ora de creare produc același tipar. " + Closing;
        var refs = new List<EvidenceRef> { Ref(h.Mft, "înregistrare $MFT: orele $STANDARD_INFORMATION și $FILE_NAME") };
        if (h.UsnCreate is not null) refs.Add(Ref(h.UsnCreate, "jurnal USN: crearea fișierului"));
        if (h.UsnChange is not null) refs.Add(Ref(h.UsnChange, "jurnal USN: operație separată „Basic info change”"));
        if (h.UsnRename is not null) refs.Add(Ref(h.UsnRename, "jurnal USN: redenumire sau mutare"));
        return new Finding
        {
            FindingId = c.NextId(), RuleId = TimestompRuleId,
            Title = "Orele de creare ale unui fișier par schimbate după crearea lui",
            PlainWhat = $"are fișierul «{name}» cu ora de creare mai veche decât momentul în care Windows l-a creat; orele par să fi fost schimbate după crearea fișierului",
            Severity = high ? Severity.High : low ? Severity.Low : Severity.Medium, Category = "Defense Evasion",
            Classification = Classification.Candidate, Confidence = high ? Confidence.Medium : Confidence.Low,
            MitreTechniqueId = "T1070.006", File = h.Path,
            FirstSeenUtc = rename ? new DateTimeOffset(h.CreateAt!.Value, TimeSpan.Zero) : high ? new DateTimeOffset(h.FnCreated, TimeSpan.Zero) : null,
            Description = description,
            ClassificationReason = high ? "Corelație între $MFT (orele $SI / $FN) și jurnalul USN (creare, o operație separată de schimbare a atributelor de bază" + (rename ? ", redenumire)." : ").") : low ? "Un singur indicator ($MFT) și un nume scurt 8.3: tunelarea NTFS poate produce același tipar." : "Un singur indicator ($MFT); jurnalul USN nu confirmă.",
            SemanticType = SemanticType.Observation,
            SupportingEvidence = refs,
            AlternativeExplanations =
            [
                .. low ? (string[])["Tunelare NTFS: un fișier creat cu numele unuia șters sau redenumit în ultimele 15 secunde moștenește ora lui de creare (apare doar cu nume 8.3 pe volum), de exemplu la salvarea unui document cu fișier temporar."] : [],
                "Un arhivator care restaurează ora de creare (de exemplu 7-Zip cu -mtc, WinRAR cu ore de creare salvate) produce același tipar.",
                "O unealtă de sincronizare sau de restaurare care setează orele după scriere poate produce aceeași operație separată.",
            ],
            MissingEvidence =
            [
                "Ce proces a schimbat orele (jurnalul USN nu păstrează procesul sau contul).",
                high ? "Copia fișierului original sau o copie de rezervă, pentru comparație." : h.HasUsn ? "Un jurnal USN mai complet (acesta nu arată o schimbare separată după creare)." : "Jurnalul USN al volumului, care poate confirma sau infirma schimbarea după creare.",
            ],
            RecommendedNextSteps =
            [
                "Verificați dacă fișierul a fost adus printr-o arhivă sau o copiere și cu ce unealtă.",
                "Căutați în jurul orei din jurnalul USN execuția unor programe (Prefetch, 4688, Sysmon 1) care ar fi putut schimba orele.",
            ],
            Limitations =
            [
                "Uneltele care schimbă și $FILE_NAME sau și ora de schimbare a înregistrării nu se văd în acest tipar.",
                "Fără jurnal USN, o schimbare urmată de redenumire sau mutare pe același volum nu se vede (Windows copiază orele $SI în $FILE_NAME).",
            ],
        };
    }
}
