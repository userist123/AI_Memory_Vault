using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>An account seen on the station (local account or user profile).</summary>
public sealed record ObservedAccount(string Account, string Sid, string Source, DateTimeOffset? LastSeenUtc);

/// <summary>A storage medium seen on the station (USB history, mounted volume, optical drive).</summary>
public sealed record ObservedMedium(string Serial, string VidPid, string Model, string VolumeLabel, string Type, string Source, DateTimeOffset? LastSeenUtc);

public sealed record MergeCounts(int Added, int AlreadyPresent);

/// <summary>
/// Fills the registers from what exists on the station. Pure: only appends rows that are not yet there, never changes or removes an existing row.
/// What the station cannot know (clearance, need-to-know, zones, assigned user) stays empty for the administrator.
/// </summary>
public static class RegisterAutoFill
{
    public static MergeCounts MergeUsers(UsersRegister reg, IEnumerable<ObservedAccount> observed, DateTimeOffset now)
    {
        int added = 0, present = 0;
        foreach (var o in observed)
        {
            var account = (o.Account ?? "").Trim();
            var sid = (o.Sid ?? "").Trim();
            if (account.Length == 0 && sid.Length == 0) continue;
            bool exists = reg.Rows.Any(r =>
                (account.Length > 0 && r.AccountList.Any(a => a.Equals(account, StringComparison.OrdinalIgnoreCase) || RegisterRules.SameAccount(a, account))) ||
                (sid.Length > 0 && r.SidList.Any(s => s.Equals(sid, StringComparison.OrdinalIgnoreCase))));
            if (exists) { present++; continue; }
            reg.Rows.Add(new UserRow { Person = account.Length > 0 ? account : sid, Accounts = account, Sids = sid });
            added++;
        }
        if (added > 0) reg.UpdatedUtc = now;
        return new MergeCounts(added, present);
    }

    public static MergeCounts MergeMedia(MediaRegister reg, IEnumerable<ObservedMedium> observed, string stationUnitCode, DateTimeOffset now)
    {
        int added = 0, present = 0;
        var date = now.ToString("yyyy-MM-dd");
        foreach (var o in observed)
        {
            var serial = MediaRegister.NormalizeSerial(o.Serial ?? "");
            var label = (o.VolumeLabel ?? "").Trim();
            bool conforms = MediaLabel.TryParse(label, out var parts);
            // The registration number of a medium is its full label (unit code included), so that equal numbers of two units never collide.
            var regNo = conforms ? parts.Raw.ToUpperInvariant() : "";

            bool exists = reg.Rows.Any(r =>
                (serial.Length > 0 && MediaRegister.NormalizeSerial(r.Serial) == serial) ||
                (regNo.Length > 0 && r.RegistrationNumber.Trim().Equals(regNo, StringComparison.OrdinalIgnoreCase)));
            if (exists) { present++; continue; }

            var notes = $"observat pe stație {date}, eticheta „{label}”";
            var classification = "";
            if (!conforms) notes += " · eticheta nu urmează convenția xxxx-yyyyL";
            else
            {
                // Only letters the register itself knows as a level are copied; otherwise the register would reject the row.
                if (SecrecyLevels.TryParse(parts.Classification, out _)) classification = parts.Classification;
                else notes += $" · clasificarea „{parts.Classification}” din etichetă nu este un nivel cunoscut al registrului";
                if (!string.IsNullOrWhiteSpace(stationUnitCode) && parts.UnitCode != stationUnitCode.Trim())
                    notes += $" · cod de unitate diferit de al stației ({parts.UnitCode} ≠ {stationUnitCode.Trim()})";
            }

            reg.Rows.Add(new MediaRow
            {
                RegistrationNumber = regNo,
                Serial = o.Serial?.Trim() ?? "",
                VidPid = RegisterRules.NormalizeVidPid(o.VidPid ?? "") ?? "",
                Type = MediaRegister.NormalizeType((o.Type ?? "").Trim().Equals("CD/DVD", StringComparison.OrdinalIgnoreCase) ? "DVD" : o.Type ?? "") ?? "other",
                Classification = classification,
                Status = "active",
                Notes = notes,
            });
            added++;
        }
        if (added > 0) reg.UpdatedUtc = now;
        return new MergeCounts(added, present);
    }
}
