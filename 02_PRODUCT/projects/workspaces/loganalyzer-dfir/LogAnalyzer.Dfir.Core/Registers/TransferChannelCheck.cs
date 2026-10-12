using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.Profile;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>Whether a transfer goes through a channel the procedure profile approves (control research P2; profile <c>TransferChannels</c>).</summary>
/// <param name="Defined">The profile defines at least one approved channel; without one, nothing can be checked (never "approved").</param>
/// <param name="Approved">An approved channel matches the source zone, the destination zone and the medium.</param>
/// <param name="Message">One plain sentence for the operator and the controller.</param>
/// <param name="Channel">The name of the matching channel, when there is one.</param>
public sealed record TransferChannelResult(bool Defined, bool Approved, string Message, string? Channel = null)
{
    /// <summary>The transfer must be stopped and confirmed by the operator: a channel list exists and this transfer is not on it.</summary>
    public bool NeedsConfirmation => Defined && !Approved;
}

/// <summary>
/// Compares one transfer row with the approved transfer channels of the procedure profile. A channel matches when its source zone equals the row's
/// source (or source station), its destination zone equals the row's destination (or destination station) and its medium names the row's medium
/// type (one contains the other, or they share a word of at least 3 letters, ignoring case and diacritics). Pure; nothing is written.
/// </summary>
public static class TransferChannelCheck
{
    public const string NotDefinedMessage = "Profilul de proceduri nu definește canale de transfer aprobate, deci aplicația nu poate spune dacă acest transfer folosește un canal aprobat. Completați tabelul „Canale de transfer aprobate” din profil.";

    public static TransferChannelResult Check(TransferRow row, ProcedureProfile? profile)
    {
        var channels = profile?.TransferChannels.Where(c => c.FromZone.Trim().Length > 0 && c.ToZone.Trim().Length > 0).ToList() ?? [];
        if (channels.Count == 0) return new(false, false, NotDefinedMessage);

        var from = new[] { row.Source, row.SourceStation }.Where(s => s.Trim().Length > 0).ToList();
        var to = new[] { row.Destination, row.DestinationStation }.Where(s => s.Trim().Length > 0).ToList();
        var zonePairs = channels.Where(c => from.Any(f => Same(f, c.FromZone)) && to.Any(t => Same(t, c.ToZone))).ToList();
        var fromText = from.Count > 0 ? from[0] : "(sursă necompletată)";
        var toText = to.Count > 0 ? to[0] : "(destinație necompletată)";
        if (zonePairs.Count == 0)
            return new(true, false,
                $"Nu există un canal de transfer aprobat de la „{fromText}” la „{toText}”. Canalele aprobate în profil sunt: " +
                string.Join("; ", channels.Select(c => $"{c.Name} ({c.FromZone} → {c.ToZone}, {c.Medium})")) + ".");

        var match = zonePairs.FirstOrDefault(c => MediumMatches(c.Medium, row.MediumType));
        if (match is null)
            return new(true, false,
                $"Suportul „{(row.MediumType.Trim().Length > 0 ? row.MediumType : "necompletat")}” nu este aprobat pentru transferul de la „{fromText}” la „{toText}”. " +
                $"Pentru acest drum sunt aprobate: {string.Join("; ", zonePairs.Select(c => $"{c.Medium} (canalul {c.Name})"))}.");
        return new(true, true, $"Canal aprobat: {match.Name} ({match.FromZone} → {match.ToZone}, suport {match.Medium}).", match.Name);
    }

    private static bool Same(string a, string b) => Fold(a) == Fold(b) && Fold(a).Length > 0;

    internal static bool MediumMatches(string approved, string used)
    {
        string a = Fold(approved), u = Fold(used);
        if (a.Length == 0 || u.Length == 0) return false;
        if (a.Contains(u, StringComparison.Ordinal) || u.Contains(a, StringComparison.Ordinal)) return true;
        static IEnumerable<string> Words(string s) => s.Split([' ', '/', ',', ';', '-', '(', ')', '.'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3);
        return Words(a).Intersect(Words(u)).Any();
    }

    /// <summary>Lower case, trimmed, without diacritics: „Disc extern” and „disc EXTERN” are the same.</summary>
    private static string Fold(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
