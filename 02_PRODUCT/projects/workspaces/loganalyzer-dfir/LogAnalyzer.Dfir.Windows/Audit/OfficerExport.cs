namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// Usability 2026-10-16: „Exportă pentru ofițer” puts in one folder what the security officer takes away after a control: the report (and the signed report,
/// when there is one), the copy of the application's log for the CD-R. The media history is NOT part of it: it goes to the officer only through the signed export of #297
/// („Exportă istoricul mediilor pentru ofițer”). This class holds the folder name and the short „citește-mă” text.
/// </summary>
public static class OfficerExport
{
    /// <summary>
    /// The button of the signed media-history export (#297, control page): the only way the media history goes to the officer (coordinator decision on
    /// PR #298: never as an unsigned file in this bundle).
    /// </summary>
    public const string SignedMediaHistoryButton = "Exportă istoricul mediilor pentru ofițer";
    public const string ReadmeFile = "citeste_ma.txt";

    /// <summary>„Pentru_ofiter_STATIE_20261016_143000”: one folder per export, never overwritten.</summary>
    public static string FolderName(string host, DateTimeOffset nowUtc)
    {
        var safe = string.Concat((host ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"Pentru_ofiter_{(safe.Length == 0 ? "statie" : safe)}_{nowUtc:yyyyMMdd_HHmmss}";
    }

    /// <summary>The „citește-mă” of the folder: what each file is and what the officer does with it.</summary>
    public static string Readme(string host, DateTimeOffset nowUtc, IEnumerable<string> files, string anchorLine) => string.Join(Environment.NewLine,
        new[]
        {
            $"LogAnalyzer — export pentru ofițerul de securitate · stația {host} · {nowUtc:yyyy-MM-dd HH:mm} UTC",
            "",
            "Ce conține acest folder:",
        }
        .Concat(files.Select(f => "• " + f))
        .Concat(
        [
            "",
            "Raportul de control (PDF) devine oficial doar cu semnătura electronică calificată a ofițerului de securitate.",
            $"Istoricul mediilor NU este în acest folder: se exportă semnat cu cardul ofițerului, cu butonul „{SignedMediaHistoryButton}” (pagina de control sau Acasă), și se confruntă între stații pe stația ofițerului.",
            "Folderul LogAnalyzer_ancorare_…: copia jurnalului aplicației; se arde pe un CD-R (se scrie o singură dată) și se păstrează la ofițer.",
            anchorLine.Length > 0 ? anchorLine + " — se trece pe procesul-verbal." : "",
            "",
        ]));
}
