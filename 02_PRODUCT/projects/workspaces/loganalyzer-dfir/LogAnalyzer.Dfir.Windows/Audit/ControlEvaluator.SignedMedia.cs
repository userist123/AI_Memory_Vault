using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Windows.Audit;

public static partial class ControlEvaluator
{
    /// <summary>
    /// D04 (owner decision 2026-10-16): every removable medium the station has seen (connected now and history) must be in the ACTIVE signed media register
    /// (OFICIAL, signed by the security officer's qualified card certificate on the transfer station). A medium not in it, or a retired / destroyed one seen
    /// after its date, is NECONFORM; the same serial with other attributes (VID/PID, capacity, BitLocker volume id) is "posibilă clonare a serialului" (DE
    /// VERIFICAT). No register in force, or a register altered outside the application, is NECONFORM as soon as a medium was seen.
    /// </summary>
    private static void SignedMediaRegisterCheck(StationFacts f, ControlReport r)
    {
        const string id = "D04", area = "Dispozitive", title = "Mediile de stocare văzute pe stație sunt în registrul semnat de ofițer";
        const string rec = "Înrolați toate mediile pe stația de transfer, cereți ofițerului de securitate să semneze registrul cu cardul și importați versiunea nouă pe fiecare stație; un mediu neînregistrat se predă ofițerului.";
        // owner decision 2026-10-16: the register is switched off on the station (MediaRegisterFeature); the device monitor (D05) follows the media instead
        if (!f.SignedMediaRegisterInUse)
        {
            Add(r, id, area, title, ControlStatus.NuSeAplica,
                $"Nu se aplică: {MediaRegisterFeature.NotInUseReason}. Mediile de stocare și celelalte dispozitive sunt urmărite de monitorul de dispozitive în timp real (verificarea D05), cu numărul de înregistrare al fiecărui mediu.");
            return;
        }
        if (f.ObservedMedia is not { } seen)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Lista mediilor de stocare ale stației nu a putut fi citită, deci nu se poate compara cu registrul semnat." + Gap(f, "Medii amovibile (registrul semnat)"), rec: rec);
            return;
        }
        if (f.SignedMedia is not { } reg)
        {
            Add(r, id, area, title, ControlStatus.Nedeterminat, "Registrul de medii semnat nu a fost verificat la acest control.", seen.Select(m => $"{m.Description} SN {m.Serial}"), rec);
            return;
        }
        var regLine = reg.Usable ? $"Registrul semnat în vigoare: versiunea {reg.Doc!.Version}, emis de „{reg.Doc.StationId}” la {reg.Doc.IssuedUtc}, {reg.Doc.Active.Count()} medii active (SHA-256 {reg.Sha256})." : "";
        if (!reg.Usable)
        {
            var why = reg.Problem.Length > 0 ? reg.Problem : "registrul nu poate fi folosit";
            bool tampered = reg.Sha256.Length > 0 || why.Contains("în afara aplicației", StringComparison.Ordinal);
            Add(r, id, area, title, seen.Count > 0 || tampered ? ControlStatus.Neconform : ControlStatus.DeVerificat,
                (seen.Count > 0 ? $"Stația a văzut {seen.Count} {(seen.Count == 1 ? "mediu amovibil" : "medii amovibile")}, dar " : "") + "nu există un registru de medii semnat valabil: " + why + "." +
                (seen.Count > 0 ? " Niciun mediu nu poate fi recunoscut ca autorizat." : " Nu s-a văzut niciun mediu amovibil; absența observațiilor nu dovedește că nu au fost folosite."),
                seen.Select(m => $"{m.Description} SN {(m.Serial.Length > 0 ? m.Serial : "(fără serie)")}{(m.ConnectedNow ? " — conectat acum" : "")}"), rec);
            return;
        }
        // review M9: an unregistered medium seen only before the signed register existed is history (DE VERIFICAT), not a violation. The boundary is the first
        // OFICIAL installation from the sealed audit; without it, on Standalone, the application's install date (activity before it is the station's history).
        var historyBefore = reg.FirstInstalledUtc ?? (f.Posture == LogAnalyzer.Core.Services.Edition.StationPosture.Standalone ? f.AppInstalledUtc : null);
        var tolerance = SignedMediaMatcher.ResolveTolerance(f.MediaCapacityTolerance, out var toleranceWarning);
        var matches = seen.Select(m => SignedMediaMatcher.Match(reg.Doc!.Entries, m, historyBefore, tolerance)).ToList();
        var toleranceNote = (f.MediaCapacityToleranceWarning.Length > 0 ? " " + f.MediaCapacityToleranceWarning : toleranceWarning is not null ? " " + toleranceWarning : "")
            + (Math.Abs(tolerance - SignedMediaMatcher.DefaultCapacityTolerance) > 1e-12 ? $" Toleranța de capacitate este setată la {tolerance:P1} (implicit {SignedMediaMatcher.DefaultCapacityTolerance:P0}); de verificat în laborator." : "");
        var bad = matches.Where(m => m.IsViolation).ToList();
        var check = matches.Where(m => m.ToVerify).ToList();
        var ok = matches.Where(m => m.Kind == SignedMediaMatchKind.Registered).ToList();
        bool isolated = f.Posture != LogAnalyzer.Core.Services.Edition.StationPosture.Internet;
        var status = bad.Count > 0 ? (isolated ? ControlStatus.Neconform : ControlStatus.DeVerificat) : check.Count > 0 || seen.Count == 0 ? ControlStatus.DeVerificat : ControlStatus.Conform;
        string detail = regLine + " " + (seen.Count == 0
            ? "Nu s-a văzut niciun mediu amovibil în ce s-a putut citi; absența observațiilor nu dovedește că nu au fost folosite."
            : $"{matches.Count} {(matches.Count == 1 ? "mediu văzut" : "medii văzute")}: {ok.Count} în registru" +
              (bad.Count > 0 ? $", {bad.Count(b => b.Kind == SignedMediaMatchKind.NotRegistered)} mediu neînregistrat, {bad.Count(b => b.Kind == SignedMediaMatchKind.UsedAfterRetirement)} retrase sau distruse folosite după data lor" : "") +
              (check.Count > 0 ? $", {check.Count} de verificat (posibilă clonare a serialului, dată necunoscută sau istoric dinaintea registrului semnat)" : "") + ".");
        detail += toleranceNote;
        Add(r, id, area, title, status, detail.Trim(),
            bad.Select(m => "NECONFORM: " + m.Text).Concat(check.Select(m => "DE VERIFICAT: " + m.Text)).Concat(ok.Select(m => "în registru: " + m.Text)), rec);
    }

    private static string Gap(StationFacts f, string artifact) =>
        f.Gaps.FirstOrDefault(g => g.Artifact == artifact) is { } g ? $" ({g.Reason})" : "";
}
