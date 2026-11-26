namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// S07 (planner #9 item 4; HG 585 art. 309, 311(1); matrix row 18): the approved software of the procedure profile has not been altered. Each profile row with a
    /// path and a SHA-256 was hashed on the station (the I/O is in <see cref="ApprovedSoftwareHasher"/>); a file whose hash differs is NECONFORM, a file that could
    /// not be read (missing, access denied, too big, too many matches) is DE VERIFICAT, and without a checkable row the check is NEDETERMINAT, never CONFORM.
    /// </summary>
    private static void ApprovedSoftwareIntegrityCheck(StationFacts f, ControlReport r)
    {
        const string id = "S07", area = "Software", title = "Programele aprobate nu au fost modificate (amprenta SHA-256 din profil)";
        const string rec = "Verificați sursa versiunii găsite pe stație; reinstalați programul din pachetul aprobat. Actualizați amprenta din profil doar după o actualizare aprobată și înregistrată.";
        if (f.SoftwareIntegrity is not { } s)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Amprentele programelor aprobate nu au fost verificate la acest control." + Gap(f, "Programe aprobate (amprente SHA-256)"), rec: rec);
            return;
        }
        var notes = (s.NoPath > 0 ? $" {s.NoPath} {(s.NoPath == 1 ? "rând are" : "rânduri au")} amprentă, dar nu au cale: fără cale, neverificabile." : "")
                  + (s.InvalidRows > 0 ? $" {s.InvalidRows} {(s.InvalidRows == 1 ? "rând invalid a fost ignorat" : "rânduri invalide au fost ignorate")} (vezi validarea profilului)." : "");
        if (s.Rows.Count == 0)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Profilul nu conține programe aprobate cu cale și amprentă SHA-256, deci integritatea programelor nu se poate verifica." + notes, rec: rec);
            return;
        }
        static string Short(string h) => h.Length > 16 ? h[..16] : h;
        var bad = new List<string>(); var check = new List<string>(); var ok = new List<string>();
        int files = 0;
        foreach (var row in s.Rows)
        {
            if (row.Problem.Length > 0) check.Add($"DE VERIFICAT: {row.Name}, {row.PathPattern}: {row.Problem}");
            bool Same(ApprovedSoftwareFile x) => x.Problem.Length == 0 && x.Sha256.Equals(row.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
            // review 2026-11-19: a pattern with wildcards names a place, not one file. When the approved file is among the matches, the other matches are
            // other files (DE VERIFICAT, not a modification); only when NO match has the approved hash is the program considered modified.
            bool approvedPresent = row.PathPattern.IndexOfAny(['*', '?']) >= 0 && row.Files.Any(Same);
            foreach (var file in row.Files)
            {
                files++;
                if (file.Problem.Length > 0) check.Add($"DE VERIFICAT: {row.Name}, {file.Path}: {file.Problem}");
                else if (Same(file)) ok.Add($"{row.Name}, {file.Path}: amprenta corespunde");
                else if (approvedPresent)
                    check.Add($"DE VERIFICAT: {row.Name}, {file.Path}: alt fișier sub același model (amprenta {Short(file.Sha256)}); fișierul aprobat a fost găsit alături");
                else bad.Add($"NECONFORM: {row.Name}, {file.Path}: așteptat {Short(row.ExpectedSha256)}, observat {Short(file.Sha256)}");
            }
        }
        var counts = $"{s.Rows.Count} {(s.Rows.Count == 1 ? "rând verificat" : "rânduri verificate")}, {files} {(files == 1 ? "fișier citit" : "fișiere citite")}: {ok.Count} cu amprenta corespunzătoare, {bad.Count} modificate";
        var evidence = bad.Concat(check).Concat(ok);
        if (bad.Count > 0)
            Add(r, id, area, title, ControlStatus.Neconform, $"{counts}. Un program aprobat are altă amprentă SHA-256 decât cea din profil: a fost modificat sau este altă versiune." + notes, evidence, rec);
        else if (check.Count > 0)
            Add(r, id, area, title, ControlStatus.DeVerificat, $"{counts}, {check.Count} de verificat (lipsă, necitit, prea mare, prea multe potriviri sau alt fișier sub același model)." + notes, evidence, rec);
        else
            Add(r, id, area, title, ControlStatus.Conform, $"{counts}." + notes, evidence, rec);
    }
}
