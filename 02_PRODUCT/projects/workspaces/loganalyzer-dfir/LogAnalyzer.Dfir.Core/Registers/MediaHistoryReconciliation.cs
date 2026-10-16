using System.Globalization;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// The verdict on one station export offered to the officer's reconciliation: <see cref="Accepted"/> only when the file is canonical and valid, the detached CMS
/// matches its exact bytes, the signer is on the reconciling station's officer list (pinned by certificate fingerprint), and the certificate chains to an
/// imported qualified CA and is not revoked in an imported CRL. Anything less is refused (fail closed): a tampered or unsigned export is never reconciled.
/// </summary>
public sealed record MediaHistoryCheck(bool Accepted, string FileName, string Headline, IReadOnlyList<VerificationLine> Lines, MediaHistoryDoc? Doc, string Sha256,
                                       string SignerFingerprint, string SignerName)
{
    /// <summary>The station's name; for a refused export the name read from the file is not trusted and is marked as such (review R3).</summary>
    public string StationLabel => Doc?.Label is { Length: > 0 } l ? (Accepted ? l : l + " (nume din fișier neverificat)") : FileName;
    public string ToPlainText() => (Accepted ? "ACCEPTAT" : "RESPINS") + $" — {FileName}: {Headline}" + Environment.NewLine +
        string.Join(Environment.NewLine, Lines.Select(l => $"[{l.Mark}] {l.Text}"));
}

public static class MediaHistoryVerify
{
    /// <param name="cms">The detached CMS (".p7s"); null = the signature file is missing.</param>
    public static MediaHistoryCheck Verify(string fileName, byte[] json, byte[]? cms, ICmsDetachedVerifier verifier, IReadOnlyList<SecurityOfficerRow> pinnedOfficers,
                                           QualifiedSignatureTrust trust)
    {
        var lines = new List<VerificationLine>();
        string sha = SignedMediaRegister.Sha256Hex(json), fp = "", name = "";
        MediaHistoryDoc? doc = null;
        MediaHistoryCheck Done(bool ok, string headline) => new(ok, fileName, headline, lines, doc, sha, fp, name);

        try { doc = MediaHistoryExport.Parse(json); }
        catch (FormatException ex) { lines.Add(new(false, "Fișierul exportului: " + ex.Message + ".")); return Done(false, "fișierul nu este un export valid (modificat sau trunchiat)"); }
        var problems = MediaHistoryExport.Problems(doc);
        if (problems.Count > 0) { lines.Add(new(false, "Conținutul exportului: " + string.Join("; ", problems) + ".")); return Done(false, "conținutul exportului nu este valid"); }
        lines.Add(new(true, $"Exportul stației „{doc.Label}” (Hardware ID {doc.StationId}) din {doc.ExportedUtc}: {doc.Media.Count} medii, {doc.Transfers.Count} transferuri; SHA-256 {sha}."));

        if (cms is null || cms.Length == 0)
        {
            lines.Add(new(false, "Lipsește semnătura ofițerului (fișierul .p7s)."));
            return Done(false, "export nesemnat: nu se folosește");
        }
        CmsResult c;
        try { c = verifier.Verify(cms, json); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException or FormatException) { c = new CmsResult(false, null, ex.Message); }
        if (!c.Ok || c.Signer is null)
        {
            lines.Add(new(false, "Semnătura exportului: " + (c.Problem.Length > 0 ? c.Problem : "nu se verifică") + "."));
            return Done(false, "semnătura nu corespunde exportului (exportul a fost modificat după semnare sau semnătura este a altui fișier)");
        }
        fp = OfficerFingerprint.Of(c.Signer); name = OfficerFingerprint.CommonName(c.Signer);
        lines.Add(new(true, "Semnătura criptografică corespunde exact conținutului exportului."));

        if (!pinnedOfficers.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp)))
        {
            lines.Add(new(false, $"Semnatarul „{name}” (amprenta {fp}) NU este pe lista ofițerilor de securitate a acestei stații."));
            return Done(false, "semnatarul nu este un ofițer de securitate înregistrat");
        }
        lines.Add(new(true, $"Semnatarul „{name}” este ofițer de securitate pe lista acestei stații (amprenta {fp})."));
        var v = trust.Validate(c.Signer, doc.Exported!.Value);
        if (v.Status != QualifiedCheck.Ok)
        {
            lines.Add(new(false, "Certificatul semnatarului (lanț și revocare): " + v.Message + "."));
            return Done(false, v.Status == QualifiedCheck.Failed ? "certificatul semnatarului nu este acceptat: " + v.Message
                                                                : "lanțul sau revocarea certificatului nu pot fi confirmate — importați certificatul CA și CRL-ul emitentului");
        }
        lines.Add(new(true, "Lanțul certificatului și revocarea: " + v.Message + "."));
        return Done(true, $"export ACCEPTAT, semnat de „{name}”");
    }
}

public enum CrossSeverity { Grav = 0, Ridicat = 1, DeVerificat = 2, Nedeterminat = 3, Conform = 4 }

/// <summary>One line of the reconciliation, sorted by <see cref="Severity"/>.</summary>
public sealed record CrossStationFinding(CrossSeverity Severity, string Kind, string Station, string Medium, string Transfer, string Text)
{
    public string SeverityText => CrossStationReconciliation.SeverityText(Severity);
}

public sealed record CrossStationSettings
{
    public required DateTimeOffset FromUtc { get; init; }
    public required DateTimeOffset ToUtc { get; init; }
    /// <summary>Two connections of the same serial on two stations closer than this cannot be one medium carried by hand: "posibilă clonare a serialului".</summary>
    public TimeSpan MinMoveTime { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>The medium must be seen at the source within this distance from the register time.</summary>
    public TimeSpan TransferTolerance { get; init; } = TimeSpan.FromHours(2);
    /// <summary>The medium must reach the destination within this time after the register time.</summary>
    public TimeSpan DestinationWindow { get; init; } = TimeSpan.FromHours(72);
    /// <summary>Stations that must have exported (names or Hardware IDs), besides those named in the transfer register.</summary>
    public IReadOnlyList<string> ExpectedStations { get; init; } = [];
    /// <summary>The anchor code written on each station's proces-verbal (station name or Hardware ID → code), typed by the officer; optional per station.</summary>
    public IReadOnlyDictionary<string, string> PaperAnchors { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CrossStationResult
{
    public List<CrossStationFinding> Findings { get; } = [];
    public List<string> Limits { get; } = [];
    public List<MediaHistoryCheck> Accepted { get; } = [];
    public List<MediaHistoryCheck> Rejected { get; } = [];
    public int TransfersChecked { get; set; }
    public List<CrossStationFinding> Ordered => [.. Findings.OrderBy(f => f.Severity).ThenBy(f => f.Station, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Medium, StringComparer.Ordinal).ThenBy(f => f.Transfer, StringComparer.Ordinal).ThenBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Text, StringComparer.Ordinal)];
    public int Count(CrossSeverity s) => Findings.Count(f => f.Severity == s);
    public string Summary => $"GRAV {Count(CrossSeverity.Grav)} · RIDICAT {Count(CrossSeverity.Ridicat)} · DE VERIFICAT {Count(CrossSeverity.DeVerificat)} · " +
                             $"NEDETERMINAT {Count(CrossSeverity.Nedeterminat)} · CONFORM {Count(CrossSeverity.Conform)} · exporturi acceptate {Accepted.Count}, respinse {Rejected.Count} · transferuri confruntate {TransfersChecked}";
}

/// <summary>
/// "Confruntare între stații" (owner decision 2026-10-16): the security officer imports the signed media-history exports of every station and, for every approved
/// transfer row (source station, destination station, medium, files, time), checks that the medium was seen at the source in the window, at the destination after
/// it, that the files are on the destination (SHA-256), and lists every OTHER station where the medium was connected, with the file activity known there. Media
/// absent from the signed register, a serial seen on two stations too close in time to be carried (possible clone) and stations with an older register are listed.
/// Pure: no I/O. What the exports cannot show is said in <see cref="CrossStationResult.Limits"/>.
/// </summary>
public static class CrossStationReconciliation
{
    public static string SeverityText(CrossSeverity s) => s switch
    {
        CrossSeverity.Grav => "GRAV", CrossSeverity.Ridicat => "RIDICAT", CrossSeverity.DeVerificat => "DE VERIFICAT", CrossSeverity.Nedeterminat => "NEDETERMINAT", _ => "CONFORM",
    };

    public const string KindRejected = "export_respins", KindMissing = "statie_fara_export", KindStale = "registru_invechit", KindTransferOk = "transfer_confirmat",
                        KindNotAtSource = "nevazut_la_sursa", KindNotAtDestination = "nevazut_la_destinatie", KindFilesMissing = "fisiere_negasite", KindElsewhere = "introdus_in_alta_statie",
                        KindFilesElsewhere = "fisiere_in_alta_statie", KindUnregistered = "mediu_neinregistrat", KindClone = "posibila_clonare", KindRowIncomplete = "rand_incomplet",
                        KindContradictory = "inregistrari_contradictorii", KindIndicators = "istoric_posibil_incomplet", KindAnchorMismatch = "ancorare_nepotrivita",
                        KindStaleExport = "export_invechit", KindAnchorUnknownStation = "cod_pentru_statie_necunoscuta";

    private sealed record Conn(MediaHistoryDoc Station, MediumHistory Medium, DateTimeOffset Start, DateTimeOffset? End);
    private sealed record Row(MediaTransferEntry Entry, string Serial, DateTimeOffset? TimeUtc, MediaHistoryDoc? Source, MediaHistoryDoc? Destination, List<string> Hashes);

    public static CrossStationResult Run(IReadOnlyList<MediaHistoryCheck> checks, SignedMediaRegisterDoc? latestRegister, string latestRegisterSha256, CrossStationSettings s)
    {
        var res = new CrossStationResult();
        foreach (var c in checks) (c.Accepted && c.Doc is not null ? res.Accepted : res.Rejected).Add(c);
        foreach (var c in res.Rejected)
            res.Findings.Add(new(CrossSeverity.Grav, KindRejected, c.StationLabel, "", "", $"Exportul „{c.FileName}” a fost RESPINS și nu se folosește: {c.Headline}. Cereți stației un export nou, semnat la colectare."));

        // one export per station: the most recent one (an older one adds nothing the newer one does not have); stations in a fixed order (review R1:
        // the result must not depend on the order in which the files were loaded)
        var stations = new List<MediaHistoryDoc>();
        foreach (var g in res.Accepted.Select(c => c.Doc!).GroupBy(d => Key(d)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var latest = g.OrderByDescending(d => d.Exported).ThenByDescending(d => d.AnchorSeq).ThenBy(d => SignedMediaRegister.Sha256Hex(MediaHistoryExport.Canonical(d)), StringComparer.Ordinal).First();
            stations.Add(latest);
            if (g.Count() > 1) res.Limits.Add($"Stația {latest.Label} a predat {g.Count()} exporturi; se folosește cel mai recent ({latest.ExportedUtc}).");
        }

        MediaHistoryDoc? Find(string name)
        {
            var n = (name ?? "").Trim();
            return n.Length == 0 ? null : stations.FirstOrDefault(d => d.StationName.Equals(n, StringComparison.OrdinalIgnoreCase) || d.StationId.Equals(n, StringComparison.OrdinalIgnoreCase));
        }

        // ---- limits that hold for every result ----
        res.Limits.Add("Confruntarea arată unde a fost conectat un mediu și ce activitate pe fișiere a înregistrat fiecare stație; fără auditarea accesului la fișiere o copiere nu se vede, doar conectarea.");
        res.Limits.Add("Windows înregistrează fiecare conectare (Partition/Diagnostic 1006), dar doar ultima deconectare: sfârșitul unei conectări mai vechi nu este cunoscut.");
        res.Limits.Add("Seria USB este scrisă de producător și poate fi copiată pe alt mediu: aceeași serie pe două stații în același timp este „posibilă clonare”, nu dovadă.");
        res.Limits.Add($"Orele din registrul transferurilor sunt tastate de om: mediul trebuie văzut la sursă la ±{s.TransferTolerance.TotalHours:0.#} h și la destinație în {s.DestinationWindow.TotalHours:0.#} h; timpul minim de mutare între stații este {s.MinMoveTime.TotalMinutes:0.#} min.");
        foreach (var d in stations)
        {
            if (d.Exported is { } e && e < s.ToUtc)
                res.Limits.Add($"Exportul stației {d.Label} este din {d.ExportedUtc}, înainte de sfârșitul perioadei ({SignedMediaRegister.FormatUtc(s.ToUtc)}): ce s-a întâmplat după export nu se vede.");
            if (d.HistoryFrom is { } h && h > s.FromUtc)
                res.Limits.Add($"Istoricul mediilor pe stația {d.Label} începe la {d.HistoryFromUtc}, după începutul perioadei: conectările mai vechi nu se mai văd.");
            if (d.FileAuditing != "da")
                res.Limits.Add($"Stația {d.Label} {(d.FileAuditing == "nu" ? "NU are" : "nu a confirmat")} auditarea accesului la fișiere (A14): activitatea pe fișiere a mediilor nu se vede acolo, doar conectările.");
            int noSerial = d.Media.Count(m => m.NormalizedSerial.Length == 0);
            if (noSerial > 0) res.Limits.Add($"Stația {d.Label}: {noSerial} medii fără serie unică nu pot fi urmărite între stații.");
            if (d.UnattributedFiles.Count > 0) res.Limits.Add($"Stația {d.Label}: {d.UnattributedFiles.Count} activități pe fișiere de pe un suport amovibil nu au putut fi legate de un anumit mediu (fără serie în înregistrare).");
            foreach (var n in d.Notes) res.Limits.Add($"Stația {d.Label}: {n}.");
        }

        // ---- tamper indicators and the paper anchor code (review R5) ----
        res.Limits.Add("Un administrator poate șterge istoricul dispozitivelor unei stații; ștergerea lasă de regulă urme (104/1102, A11) raportate mai sus. " +
                       "Indicatorii de integritate sunt calculați de stația verificată însăși, la export; un administrator care oprește serviciul de jurnale și șterge înregistrări individuale poate lăsa indicatorii curați; " +
                       "codul de ancorare tastat de pe procesul-verbal dovedește doar că jurnalul aplicației nu a fost rescris, nu că istoricul dispozitivelor din Windows este complet.");
        // a typed code whose station matches no imported export is a typing mistake or a missing export, never silently ignored
        foreach (var (name, _) in s.PaperAnchors.Where(p => p.Key.Trim().Length > 0 && !string.IsNullOrWhiteSpace(p.Value)).OrderBy(p => p.Key.Trim(), StringComparer.OrdinalIgnoreCase))
            if (Find(name) is null)
                res.Findings.Add(new(CrossSeverity.DeVerificat, KindAnchorUnknownStation, name.Trim(), "", "",
                    $"Atenție, cod de pe procesul-verbal pentru o stație necunoscută: {name.Trim()} (verificați numele) — niciun export acceptat nu are acest nume sau Hardware ID, deci codul nu a fost comparat."));
        foreach (var d in stations)
        {
            var dirty = Dirty(d);
            if (dirty.Count > 0)
                res.Findings.Add(new(dirty.Any(x => x.Status == "NECONFORM") ? CrossSeverity.Ridicat : CrossSeverity.DeVerificat, KindIndicators, d.Label, "", "",
                    $"Stația {d.Label}: istoricul acestei stații poate fi incomplet: " +
                    string.Join("; ", dirty.Select(x => $"{x.Id} {x.Status}{(x.Detail.Trim().Length > 0 ? " (" + x.Detail.Trim().TrimEnd('.') + ")" : "")}")) + "."));
            var typed = s.PaperAnchors.FirstOrDefault(p => p.Key.Trim().Length > 0 && (p.Key.Trim().Equals(d.StationName, StringComparison.OrdinalIgnoreCase) || p.Key.Trim().Equals(d.StationId, StringComparison.OrdinalIgnoreCase))).Value;
            if (string.IsNullOrWhiteSpace(typed))
                res.Limits.Add($"Codul de ancorare al stației {d.Label} ({(d.AnchorCode.Length > 0 ? d.AnchorCode : "lipsă din export")}) nu a fost comparat cu procesul-verbal: scrieți codul de pe hârtie ca să se vadă dacă jurnalul aplicației a fost rescris.");
            else if (!LogAnalyzer.Dfir.Case.AuditAnchor.TryNormalize(typed, out var paper))
                res.Findings.Add(new(CrossSeverity.DeVerificat, KindAnchorMismatch, d.Label, "", "",
                    $"Stația {d.Label}: codul de ancorare tastat de pe procesul-verbal („{typed.Trim()}”) nu este un cod valid (greșeală de tastare?): nu a putut fi comparat."));
            else if (!(LogAnalyzer.Dfir.Case.AuditAnchor.TryNormalize(d.AnchorCode, out var own) && own == paper))
                res.Findings.Add(new(CrossSeverity.Grav, KindAnchorMismatch, d.Label, "", "",
                    $"Stația {d.Label}: codul de ancorare din export ({(d.AnchorCode.Length > 0 ? d.AnchorCode : "lipsă")}) NU este cel de pe procesul-verbal ({paper}): jurnalul aplicației a fost rescris sau exportul nu provine din controlul consemnat."));
        }

        // ---- stale register ----
        if (latestRegister is null)
            res.Limits.Add("Pe stația ofițerului nu există un registru de medii semnat în vigoare: versiunile stațiilor și mediile neînregistrate nu pot fi stabilite.");
        else
            foreach (var d in stations)
            {
                if (d.RegisterVersion == latestRegister.Version && string.Equals(d.RegisterSha256, latestRegisterSha256, StringComparison.OrdinalIgnoreCase)) continue;
                var text = d.RegisterVersion == 0 ? $"Stația {d.Label} nu are instalat niciun registru de medii semnat (ultimul registru semnat este versiunea {latestRegister.Version}): registru învechit."
                         : d.RegisterVersion < latestRegister.Version ? $"Stația {d.Label} are registrul de medii versiunea {d.RegisterVersion}; ultimul registru semnat este versiunea {latestRegister.Version}: registru învechit."
                         : d.RegisterVersion > latestRegister.Version ? $"Stația {d.Label} are registrul versiunea {d.RegisterVersion}, mai nou decât al ofițerului ({latestRegister.Version}): importați ultima versiune pe stația ofițerului și repetați confruntarea."
                         : $"Stația {d.Label} are registrul versiunea {d.RegisterVersion}, dar cu altă amprentă ({Short(d.RegisterSha256)}) decât al ofițerului ({Short(latestRegisterSha256)}): versiune ramificată, de verificat.";
                res.Findings.Add(new(d.RegisterVersion > latestRegister.Version ? CrossSeverity.DeVerificat : CrossSeverity.Ridicat, KindStale, d.Label, "", "", text));
            }

        // ---- transfer rows (union of the stations' registers, one per registration number and medium) ----
        var serialByInv = latestRegister?.Entries.Where(e => e.InventoryNumber.Trim().Length > 0).GroupBy(e => e.InventoryNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                                       .ToDictionary(g => g.Key, g => MediaRegister.NormalizeSerial((g.FirstOrDefault(e => e.IsActive) ?? g.First()).UsbSerial), StringComparer.OrdinalIgnoreCase)
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Review R1: every station may hold the same transfer number. All copies are grouped by number; copies that differ in route, medium, time or files are
        // "înregistrări contradictorii" (GRAV). The row of the SOURCE station (the station that holds it and is named as its source) is the one evaluated, so
        // the result does not depend on the order in which the exports were loaded.
        var candidates = new List<(MediaHistoryDoc Holder, Row Row, string Signature)>();
        var zoneUnknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in stations)
            foreach (var t in d.Transfers)
            {
                var serial = MediaRegister.NormalizeSerial(t.MediumSerial);
                if (serial.Length == 0 && serialByInv.TryGetValue(t.MediumRegistration.Trim(), out var invSerial)) serial = invSerial;
                DateTimeOffset? at = null;
                if (TransferRegister.TryDateTime(t.DateTime, out var local))
                {
                    at = ToUtc(local, d.TimeZone, out var known);
                    if (!known) zoneUnknown.Add(d.Label);
                }
                var hashes = t.Sha256.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(h => h.Length == 64 && h.All(Uri.IsHexDigit))
                                     .Select(h => h.ToLowerInvariant()).Distinct().OrderBy(h => h, StringComparer.Ordinal).ToList();
                var row = new Row(t, serial, at, Find(t.SourceStation), Find(t.DestinationStation), hashes);
                var sig = string.Join("|", t.SourceStation.Trim().ToUpperInvariant(), t.DestinationStation.Trim().ToUpperInvariant(), serial, t.MediumRegistration.Trim().ToUpperInvariant(),
                                      at is { } a ? SignedMediaRegister.FormatUtc(a) : "?" + t.DateTime.Trim(), TransferRegister.ParseDirection(t.Direction)?.ToString() ?? t.Direction.Trim().ToLowerInvariant(),
                                      string.Join(",", hashes));
                candidates.Add((d, row, sig));
            }
        foreach (var z in zoneUnknown) res.Limits.Add($"Fusul orar al stației {z} nu este cunoscut aici: orele din registrul ei au fost luate ca UTC.");

        var rows = new List<Row>();
        foreach (var g in candidates.GroupBy(c => c.Row.Entry.RegistrationNumber.Trim().Length > 0 ? "#" + c.Row.Entry.RegistrationNumber.Trim().ToUpperInvariant() : "=" + c.Signature)
                                    .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var bySource = g.Where(c => ReferenceEquals(c.Row.Source, c.Holder)).OrderBy(c => Key(c.Holder), StringComparer.Ordinal).ThenBy(c => c.Signature, StringComparer.Ordinal).ToList();
            var chosen = bySource.Count > 0 ? bySource[0] : g.OrderBy(c => Key(c.Holder), StringComparer.Ordinal).ThenBy(c => c.Signature, StringComparer.Ordinal).First();
            rows.Add(chosen.Row);
            var versions = g.GroupBy(c => c.Signature).ToList();
            if (versions.Count < 2) continue;
            var no = chosen.Row.Entry.RegistrationNumber.Trim();
            var holders = g.Select(c => c.Holder.Label).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var described = g.OrderBy(c => c.Holder.Label, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Signature, StringComparer.Ordinal).Select(c =>
                $"{c.Holder.Label}: {c.Row.Entry.SourceStation} → {c.Row.Entry.DestinationStation}, mediul {(c.Row.Serial.Length > 0 ? c.Row.Serial : c.Row.Entry.MediumRegistration)}, " +
                $"ora {c.Row.Entry.DateTime}, {c.Row.Entry.Direction}, amprente {(c.Row.Hashes.Count > 0 ? string.Join(", ", c.Row.Hashes.Select(Short)) : "niciuna")}");
            res.Findings.Add(new(CrossSeverity.Grav, KindContradictory, string.Join(", ", holders), chosen.Row.Serial, no,
                $"Există înregistrări contradictorii pentru transferul {no} între stațiile {Join(holders)}: {string.Join("; ", described)}. " +
                (bySource.Count > 0 ? $"Se evaluează rândul stației sursă {chosen.Holder.Label}." : $"Niciuna dintre stații nu este sursa numită în rândul ei; se evaluează rândul stației {chosen.Holder.Label}, de verificat pe registrul pe hârtie.")));
        }

        var approved = new List<Row>();
        foreach (var r in rows)
        {
            if (r.TimeUtc is { } t0 && (t0 < s.FromUtc - s.TransferTolerance || t0 > s.ToUtc + s.TransferTolerance)) continue;
            var missing = new List<string>();
            if (r.TimeUtc is null) missing.Add("data și ora");
            if (r.Entry.Approver.Trim().Length == 0) missing.Add("aprobarea");
            if (r.Serial.Length == 0) missing.Add("seria mediului");
            if (r.Entry.SourceStation.Trim().Length == 0 && r.Entry.DestinationStation.Trim().Length == 0) missing.Add("stația sursă și stația destinație");
            if (missing.Count > 0)
            {
                res.Findings.Add(new(CrossSeverity.DeVerificat, KindRowIncomplete, "", r.Serial, r.Entry.RegistrationNumber,
                    $"Transferul {r.Entry.RegistrationNumber} nu poate fi confruntat: lipsesc {string.Join(", ", missing)}."));
                continue;
            }
            approved.Add(r);
        }
        res.TransfersChecked = approved.Count;

        // ---- missing stations ----
        var expected = s.ExpectedStations.Select(x => x.Trim()).Where(x => x.Length > 0)
                        .Concat(approved.SelectMany(r => new[] { r.Entry.SourceStation.Trim(), r.Entry.DestinationStation.Trim() }).Where(x => x.Length > 0))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        // review R2: an expected station whose export was taken before the end of the period does not show the end of the period
        foreach (var d in expected.Select(Find).Where(d => d is not null).Distinct())
            if (d!.Exported is { } ex && ex < s.ToUtc)
                res.Findings.Add(new(CrossSeverity.DeVerificat, KindStaleExport, d.Label, "", "",
                    $"Stația {d.Label}: export învechit — exportul este din {d.ExportedUtc}, înainte de sfârșitul perioadei ({SignedMediaRegister.FormatUtc(s.ToUtc)}); ce s-a întâmplat după nu se vede. Colectați un export nou."));
        foreach (var name in expected.Where(n => Find(n) is null))
        {
            var rejected = res.Rejected.Any(c => c.Doc is { } rd && (rd.StationName.Equals(name, StringComparison.OrdinalIgnoreCase) || rd.StationId.Equals(name, StringComparison.OrdinalIgnoreCase)));
            res.Findings.Add(new(CrossSeverity.Nedeterminat, KindMissing, name, "", "",
                $"Stația {name} {(rejected ? "are doar un export respins" : "nu a predat exportul istoricului mediilor")}: ce s-a întâmplat pe ea nu se vede în confruntare."));
            res.Limits.Add($"Stația {name} nu are un export acceptat: conectările mediilor pe ea nu se văd.");
        }

        // ---- every connection of every medium, by serial ----
        var conns = stations.SelectMany(d => d.Media.Where(m => m.NormalizedSerial.Length > 0)
                                .SelectMany(m => m.Connections.Where(w => w.Start is not null).Select(w => new Conn(d, m, w.Start!.Value, w.End))))
                            .ToList();
        var bySerial = conns.GroupBy(c => c.Medium.NormalizedSerial).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Start).ToList(), StringComparer.Ordinal);
        List<Conn> At(string serial, MediaHistoryDoc d) => bySerial.TryGetValue(serial, out var l) ? l.Where(c => ReferenceEquals(c.Station, d)).ToList() : [];

        // ---- per approved transfer ----
        var arrival = new Dictionary<Row, DateTimeOffset?>(ReferenceEqualityComparer.Instance);
        foreach (var r in approved)
        {
            var t = r.TimeUtc!.Value; var no = r.Entry.RegistrationNumber; var ok = new List<string>(); bool bad = false;
            // source
            if (r.Entry.SourceStation.Trim().Length == 0) ok.Add("sursa nu este o stație cu aplicația");
            else if (r.Source is null) { /* reported as a missing station */ ok.Add($"sursa {r.Entry.SourceStation} fără export"); bad = true; }
            else if (At(r.Serial, r.Source).FirstOrDefault(c => (c.Start - t).Duration() <= s.TransferTolerance) is { } src) ok.Add($"văzut la sursa {r.Source.Label} la {U(src.Start)}");
            else
            {
                bad = true;
                res.Findings.Add(new(CrossSeverity.Ridicat, KindNotAtSource, r.Source.Label, r.Serial, no,
                    $"Transferul {no}: mediul {r.Serial} NU apare pe stația sursă {r.Source.Label} în fereastra transferului ({U(t)} ±{s.TransferTolerance.TotalHours:0.#} h)" +
                    (At(r.Serial, r.Source) is { Count: > 0 } other ? $"; acolo a fost conectat la {string.Join(", ", other.Take(5).Select(c => U(c.Start)))}." : "; mediul nu apare deloc pe stația sursă.")));
            }
            // destination
            DateTimeOffset? arrived = null;
            if (r.Entry.DestinationStation.Trim().Length == 0) ok.Add("destinația nu este o stație cu aplicația");
            else if (r.Destination is null) { ok.Add($"destinația {r.Entry.DestinationStation} fără export"); bad = true; }
            else if (At(r.Serial, r.Destination).FirstOrDefault(c => c.Start >= t - s.TransferTolerance && c.Start <= t + s.DestinationWindow) is { } dst)
            { arrived = dst.Start; ok.Add($"văzut la destinația {r.Destination.Label} la {U(dst.Start)}"); }
            else
            {
                bad = true;
                res.Findings.Add(new(CrossSeverity.Ridicat, KindNotAtDestination, r.Destination.Label, r.Serial, no,
                    $"Transferul {no}: mediul {r.Serial} NU a fost văzut pe stația destinație {r.Destination.Label} după transfer ({U(t)}, în {s.DestinationWindow.TotalHours:0.#} h)."));
            }
            arrival[r] = arrived;
            // files on the destination
            if (r.Hashes.Count == 0) ok.Add("registrul nu are amprente SHA-256, prezența fișierelor nu se poate verifica");
            else if (r.Destination is { } dd)
            {
                var known = dd.PresentFiles.Select(p => p.Sha256).Concat(dd.Media.SelectMany(m => m.Files).Select(f => f.Sha256)).Where(h => h.Length == 64).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var found = r.Hashes.Count(known.Contains);
                if (found == r.Hashes.Count) ok.Add($"fișierele sunt pe destinație ({found}/{r.Hashes.Count} amprente)");
                else
                {
                    bad = true;
                    res.Findings.Add(new(dd.FileAuditing == "da" || dd.PresentFiles.Count > 0 ? CrossSeverity.DeVerificat : CrossSeverity.Nedeterminat, KindFilesMissing, dd.Label, r.Serial, no,
                        $"Transferul {no}: pe stația destinație {dd.Label} s-au găsit {found} din {r.Hashes.Count} fișiere după amprentă" +
                        (dd.PresentFiles.Count == 0 ? " (stația nu a exportat amprentele fișierelor clasificate prezente)." : ".")));
                }
            }
            // review R5: a confirmation always says whether the history of each station it relies on may have been cleared
            var integrity = new[] { r.Source, r.Destination }.Where(x => x is not null).Distinct().Select(x => IntegrityText(x!)).ToList();
            if (!bad) res.Findings.Add(new(CrossSeverity.Conform, KindTransferOk, r.Destination?.Label ?? r.Source?.Label ?? "", r.Serial, no,
                $"Transferul {no} confirmat: {string.Join("; ", ok)}." + (integrity.Count > 0 ? " " + string.Join("; ", integrity) + "." : "")));
        }

        // ---- the medium on any OTHER station ----
        var enrolledAt = latestRegister is null ? null : Find(latestRegister.StationId);
        foreach (var (serial, list) in bySerial)
        {
            var mine = approved.Where(r => r.Serial == serial).ToList();
            var regEntry = latestRegister?.Entries.Where(e => MediaRegister.NormalizeSerial(e.UsbSerial) == serial).ToList() ?? [];
            bool Explained(Conn c) =>
                mine.Any(r => (ReferenceEquals(r.Source, c.Station) && (c.Start - r.TimeUtc!.Value).Duration() <= s.TransferTolerance)
                           || (ReferenceEquals(r.Destination, c.Station) && c.Start >= r.TimeUtc!.Value - s.TransferTolerance && c.Start <= r.TimeUtc!.Value + s.DestinationWindow))
                || (enrolledAt is not null && ReferenceEquals(c.Station, enrolledAt) && regEntry.Any(e => SignedMediaRegister.TryUtc(e.EnrolledUtc) is { } en && (c.Start - en).Duration() <= TimeSpan.FromDays(1)));
            foreach (var g in list.Where(c => c.Start >= s.FromUtc && c.Start <= s.ToUtc && !Explained(c)).GroupBy(c => c.Station))
            {
                var st = g.Key; var times = g.Select(c => c.Start).OrderBy(x => x).ToList();
                // during a transfer: between the register time and the arrival at the destination (or the end of the destination window)
                var during = mine.FirstOrDefault(r => times.Any(x => x >= r.TimeUtc!.Value - s.TransferTolerance && x <= (arrival.GetValueOrDefault(r) ?? r.TimeUtc!.Value + s.DestinationWindow)));
                var files = g.First().Medium.Files;
                var rowHashes = mine.SelectMany(r => r.Hashes).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var copied = files.Where(f => f.Sha256.Length == 64 && rowHashes.Contains(f.Sha256)).ToList();
                var text = $"Mediul {serial} a fost introdus și în stația {st.Label}, care nu este sursa sau destinația unui transfer aprobat la acea oră: " +
                           $"{string.Join(", ", times.Take(10).Select(U))}{(times.Count > 10 ? $" și încă {times.Count - 10}" : "")}" +
                           (during is not null ? $" — în timpul transferului {during.Entry.RegistrationNumber} ({during.Entry.SourceStation} → {during.Entry.DestinationStation}, {U(during.TimeUtc!.Value)})" : "") + ".";
                if (files.Count > 0)
                    text += " Activitate pe fișiere cunoscută acolo: " + string.Join("; ", files.Take(10).Select(f => $"{f.File} {(f.Direction == MediaHistoryExport.ToMedium ? "scris pe mediu" : "deschis de pe mediu")}" +
                                                                                                         $"{(f.TimeUtc.Length > 0 ? " la " + f.TimeUtc : "")}{(f.Sha256.Length == 64 ? " (SHA-256 " + Short(f.Sha256) + ")" : "")}")) +
                            (files.Count > 10 ? $" și încă {files.Count - 10}" : "") + ".";
                else if (st.FileAuditing != "da") text += " Stația nu are auditarea accesului la fișiere: nu se știe dacă s-au copiat fișiere.";
                res.Findings.Add(new(during is not null || files.Count > 0 ? CrossSeverity.Grav : CrossSeverity.Ridicat, KindElsewhere, st.Label, serial, during?.Entry.RegistrationNumber ?? "", text));
                if (copied.Count > 0)
                    res.Findings.Add(new(CrossSeverity.Grav, KindFilesElsewhere, st.Label, serial, string.Join(", ", mine.Where(r => r.Hashes.Any(h => copied.Any(c => c.Sha256.Equals(h, StringComparison.OrdinalIgnoreCase)))).Select(r => r.Entry.RegistrationNumber).Distinct()),
                        $"Fișierele transferului (după amprentă) apar pe stația {st.Label}, care nu este sursa sau destinația: {string.Join("; ", copied.Take(10).Select(f => $"{f.File} {(f.Direction == MediaHistoryExport.ToMedium ? "scris pe mediu" : "deschis de pe mediu")} la {(f.TimeUtc.Length > 0 ? f.TimeUtc : "oră necunoscută")}"))}."));
            }
        }

        // ---- media absent from the signed register ----
        if (latestRegister is not null)
            foreach (var g in stations.SelectMany(d => d.Media.Where(m => m.NormalizedSerial.Length > 0).Select(m => (d, m))).GroupBy(x => x.m.NormalizedSerial)
                                      .Where(g => !latestRegister.Entries.Any(e => MediaRegister.NormalizeSerial(e.UsbSerial) == g.Key)))
                res.Findings.Add(new(CrossSeverity.Ridicat, KindUnregistered, string.Join(", ", g.Select(x => x.d.Label).Distinct()), g.Key, "",
                    $"Mediul {g.Key} ({g.First().m.Description}) nu este în registrul de medii semnat, dar a fost văzut pe: " +
                    string.Join("; ", g.Select(x => $"{x.d.Label} (ultima conectare {(x.m.LastConnectedUtc.Length > 0 ? x.m.LastConnectedUtc : "necunoscută")})")) + "."));

        // ---- possible clone: other attributes than the register, or two stations too close in time ----
        foreach (var (serial, list) in bySerial)
        {
            var entries = latestRegister?.Entries.Where(e => MediaRegister.NormalizeSerial(e.UsbSerial) == serial).ToList() ?? [];
            foreach (var (d, m) in list.Select(c => (c.Station, c.Medium)).Distinct())
                if (entries.Count > 0 && entries.All(e => SignedMediaMatcher.Mismatches(e, new SeenMedium(m.Serial, m.VidPid, m.CapacityBytes, m.BitLockerVolumeId, m.Description, m.ConnectedNow, [])).Count > 0))
                    res.Findings.Add(new(CrossSeverity.Ridicat, KindClone, d.Label, serial, "",
                        $"Posibilă clonare a serialului: pe stația {d.Label} mediul cu seria {serial} are alte date decât în registru ({string.Join(", ", SignedMediaMatcher.Mismatches(entries[0], new SeenMedium(m.Serial, m.VidPid, m.CapacityBytes, m.BitLockerVolumeId, m.Description, m.ConnectedNow, [])))})."));
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i]; var b = list[j];
                    if (ReferenceEquals(a.Station, b.Station)) continue;
                    // the gap between leaving one station and arriving at the other; a negative gap means both were connected at the same time
                    var gap = a.End is { } ae ? b.Start - ae : b.Start - a.Start;
                    if (gap >= s.MinMoveTime) continue;
                    // review R4: a timing argument holds only when both clocks are known to be right (S05); otherwise it is only to verify
                    var clocks = new[] { ClockNote(a.Station), ClockNote(b.Station) }.Where(n => n is not null).ToList();
                    res.Findings.Add(new(clocks.Count > 0 ? CrossSeverity.DeVerificat : CrossSeverity.Ridicat, KindClone, $"{a.Station.Label}, {b.Station.Label}", serial, "",
                        $"Posibilă clonare a serialului {serial}: conectat pe {a.Station.Label} la {U(a.Start)}{(a.End is { } e2 ? $" (deconectat {U(e2)})" : "")} și pe {b.Station.Label} la {U(b.Start)} — " +
                        (gap < TimeSpan.Zero ? "în același timp pe ambele stații" : $"la {gap.TotalMinutes:0.#} min, mai puțin decât timpul minim de mutare ({s.MinMoveTime.TotalMinutes:0.#} min)") +
                        (clocks.Count > 0 ? "; " + string.Join("; ", clocks) + ", deci diferența de timp nu este sigură" : "") + "."));
                }
        }
        return res;
    }

    /// <summary>The indicators of a station that are not clean, the ones the export lacks counted as "lipsă" (an export without them is never taken as clean).</summary>
    private static List<StationIndicator> Dirty(MediaHistoryDoc d) =>
        [.. MediaHistoryExport.IndicatorIds.Select(id => d.Indicators.FirstOrDefault(i => i.Id == id)
                                                     ?? new StationIndicator { Id = id, Status = MediaHistoryExport.StatusMissing, Detail = "exportul nu conține indicatorul" })
                                           .Where(i => !i.Clean)];

    private static string IntegrityText(MediaHistoryDoc d) => Dirty(d) is { Count: > 0 } x
        ? $"{d.Label}: indicatori de integritate NECURAȚI ({string.Join(", ", x.Select(i => i.Id))}), istoricul poate fi incomplet"
        : $"{d.Label}: indicatori de integritate curați";

    /// <summary>Why the station's clock cannot be trusted for a timing argument (S05 not CONFORM or not known); null when it is fine.</summary>
    private static string? ClockNote(MediaHistoryDoc d)
    {
        var c = d.Indicators.FirstOrDefault(i => i.Id == "S05");
        if (c is not null && c.Status == MediaHistoryExport.StatusConform) return null;
        return $"ceasul stației {d.Label} poate fi decalat (S05 {(c is null ? MediaHistoryExport.StatusMissing : c.Status)}{(c is not null && c.Detail.Trim().Length > 0 ? ": " + c.Detail.Trim().TrimEnd('.') : "")})";
    }

    private static string Join(IReadOnlyList<string> names) => names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " și " + names[^1];

    private static string Key(MediaHistoryDoc d) => (d.StationId.Length > 0 ? d.StationId : d.StationName).ToUpperInvariant();
    private static string U(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
    private static string Short(string h) => h.Length > 16 ? h[..16] : h;

    /// <summary>A register time typed in the station's zone, as UTC; <paramref name="known"/> false when the zone is unknown (the time is then taken as UTC).</summary>
    public static DateTimeOffset ToUtc(DateTime local, string zoneId, out bool known)
    {
        known = false;
        TimeZoneInfo? zone = null;
        try { if (zoneId.Trim().Length > 0) zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim()); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone is null) return new DateTimeOffset(unspecified, TimeSpan.Zero);
        known = true;
        // an hour skipped when the clock goes forward does not exist: the standard offset is used
        var offset = zone.IsInvalidTime(unspecified) ? zone.BaseUtcOffset : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
