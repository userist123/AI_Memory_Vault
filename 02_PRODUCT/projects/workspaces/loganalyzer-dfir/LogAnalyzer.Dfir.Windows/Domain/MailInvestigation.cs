using System.Globalization;
using System.Text;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.Dfir.Windows.Domain;

public sealed record MailMessage(DateTimeOffset TimeUtc, string Sender, string Recipients, string Subject, string Status, string ClientIp, string Source, string MessageId);

public sealed record InboxRule(string Mailbox, string Name, bool Enabled, string ForwardTo, string RedirectTo, string ForwardAsAttachmentTo,
                               bool DeleteMessage, string MoveToFolder, string Conditions);

public sealed record MailboxForwarding(string Mailbox, string ForwardingSmtpAddress, string ForwardingAddress, bool DeliverToMailboxAndForward);

public sealed class MailInvestigationResult
{
    public List<MailMessage> Messages { get; } = [];
    public List<InboxRule> Rules { get; } = [];
    public List<MailboxForwarding> Forwarding { get; } = [];
    public List<ControlCheck> Checks { get; } = [];
}

/// <summary>
/// E-mail investigation without handling mail credentials: the application writes the official Microsoft PowerShell
/// commands (Exchange on-premises or Exchange Online), the mail administrator runs them with their own sign-in, and
/// the exported CSV files are imported and analysed here.
/// </summary>
public static class MailInvestigation
{
    /// <summary>PowerShell single-quoted literal (the only escape is doubling the quote).</summary>
    public static string Ps(string s) => "'" + s.Replace("'", "''") + "'";

    public static string ExchangeOnlineScript(string mailbox, DateTimeOffset startUtc, DateTimeOffset endUtc, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LogAnalyzer — investigație e-mail Exchange Online (rulați ca administrator Exchange; autentificarea se face în fereastra Microsoft).");
        sb.AppendLine("# Necesită modulul oficial: Install-Module ExchangeOnlineManagement");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$out = {Ps(outDir)}; New-Item -ItemType Directory -Force -Path $out | Out-Null");
        sb.AppendLine("Connect-ExchangeOnline -ShowBanner:$false");
        sb.AppendLine($"$mbx = {Ps(mailbox)}; $start = [datetime]::Parse({Ps(startUtc.UtcDateTime.ToString("o"))}).ToUniversalTime(); $end = [datetime]::Parse({Ps(endUtc.UtcDateTime.ToString("o"))}).ToUniversalTime()");
        sb.AppendLine("# Mesaje trimise și primite (Get-MessageTraceV2 acoperă cel mult 10 zile per interogare; pentru 90 de zile folosiți Start-HistoricalSearch).");
        sb.AppendLine("$sent = @(); $recv = @(); $from = $start");
        sb.AppendLine("while ($from -lt $end) { $to = @($from.AddDays(10), $end) | Sort-Object | Select-Object -First 1");
        sb.AppendLine("  $sent += Get-MessageTraceV2 -SenderAddress $mbx -StartDate $from -EndDate $to -ResultSize 5000");
        sb.AppendLine("  $recv += Get-MessageTraceV2 -RecipientAddress $mbx -StartDate $from -EndDate $to -ResultSize 5000; $from = $to }");
        sb.AppendLine("($sent + $recv) | Select-Object Received, SenderAddress, RecipientAddress, Subject, Status, FromIP, MessageId | Export-Csv (Join-Path $out 'message_trace.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Get-InboxRule -Mailbox $mbx -IncludeHidden | Select-Object @{n='Mailbox';e={$mbx}}, Name, Enabled, @{n='ForwardTo';e={$_.ForwardTo -join ';'}}, @{n='RedirectTo';e={$_.RedirectTo -join ';'}}, @{n='ForwardAsAttachmentTo';e={$_.ForwardAsAttachmentTo -join ';'}}, DeleteMessage, @{n='MoveToFolder';e={$_.MoveToFolder}}, Description | Export-Csv (Join-Path $out 'inbox_rules.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Get-Mailbox -Identity $mbx | Select-Object @{n='Mailbox';e={$mbx}}, ForwardingSmtpAddress, ForwardingAddress, DeliverToMailboxAndForward | Export-Csv (Join-Path $out 'mailbox_forwarding.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Disconnect-ExchangeOnline -Confirm:$false");
        sb.AppendLine("Write-Host \"Exportat în $out. Importați folderul în LogAnalyzer.\"");
        return sb.ToString();
    }

    public static string ExchangeOnPremScript(string mailbox, DateTimeOffset startUtc, DateTimeOffset endUtc, string outDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LogAnalyzer — investigație e-mail Exchange Server local (rulați în Exchange Management Shell, ca administrator Exchange).");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$out = {Ps(outDir)}; New-Item -ItemType Directory -Force -Path $out | Out-Null");
        sb.AppendLine($"$mbx = {Ps(mailbox)}; $start = [datetime]::Parse({Ps(startUtc.UtcDateTime.ToString("o"))}).ToLocalTime(); $end = [datetime]::Parse({Ps(endUtc.UtcDateTime.ToString("o"))}).ToLocalTime()");
        sb.AppendLine("$servers = Get-TransportService");
        sb.AppendLine("$log = foreach ($s in $servers) { Get-MessageTrackingLog -Server $s.Name -Sender $mbx -Start $start -End $end -ResultSize Unlimited; Get-MessageTrackingLog -Server $s.Name -Recipients $mbx -Start $start -End $end -ResultSize Unlimited }");
        sb.AppendLine("$log | Select-Object @{n='Received';e={$_.Timestamp.ToUniversalTime().ToString('o')}}, @{n='SenderAddress';e={$_.Sender}}, @{n='RecipientAddress';e={$_.Recipients -join ';'}}, @{n='Subject';e={$_.MessageSubject}}, @{n='Status';e={$_.EventId}}, @{n='FromIP';e={$_.ClientIp}}, @{n='MessageId';e={$_.MessageId}} | Export-Csv (Join-Path $out 'message_trace.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Get-InboxRule -Mailbox $mbx | Select-Object @{n='Mailbox';e={$mbx}}, Name, Enabled, @{n='ForwardTo';e={$_.ForwardTo -join ';'}}, @{n='RedirectTo';e={$_.RedirectTo -join ';'}}, @{n='ForwardAsAttachmentTo';e={$_.ForwardAsAttachmentTo -join ';'}}, DeleteMessage, @{n='MoveToFolder';e={$_.MoveToFolder}}, Description | Export-Csv (Join-Path $out 'inbox_rules.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Get-Mailbox -Identity $mbx | Select-Object @{n='Mailbox';e={$mbx}}, ForwardingSmtpAddress, ForwardingAddress, DeliverToMailboxAndForward | Export-Csv (Join-Path $out 'mailbox_forwarding.csv') -NoTypeInformation -Encoding UTF8");
        sb.AppendLine("Write-Host \"Exportat în $out. Importați folderul în LogAnalyzer.\"");
        return sb.ToString();
    }

    /// <summary>Imports message_trace.csv, inbox_rules.csv and mailbox_forwarding.csv (whichever exist) and analyses them.</summary>
    public static MailInvestigationResult ImportAndAnalyze(string folder, string mailbox, IReadOnlyCollection<string> internalDomains)
    {
        var r = new MailInvestigationResult();
        foreach (var row in ReadCsv(Path.Combine(folder, "message_trace.csv")))
        {
            if (!DateTimeOffset.TryParse(row.GetValueOrDefault("Received") ?? row.GetValueOrDefault("Timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) continue;
            r.Messages.Add(new MailMessage(t, row.GetValueOrDefault("SenderAddress") ?? "", row.GetValueOrDefault("RecipientAddress") ?? "",
                row.GetValueOrDefault("Subject") ?? "", row.GetValueOrDefault("Status") ?? "", row.GetValueOrDefault("FromIP") ?? "", "message_trace.csv",
                row.GetValueOrDefault("MessageId") ?? ""));
        }
        foreach (var row in ReadCsv(Path.Combine(folder, "inbox_rules.csv")))
            r.Rules.Add(new InboxRule(row.GetValueOrDefault("Mailbox") ?? mailbox, row.GetValueOrDefault("Name") ?? "", Bool(row.GetValueOrDefault("Enabled")),
                row.GetValueOrDefault("ForwardTo") ?? "", row.GetValueOrDefault("RedirectTo") ?? "", row.GetValueOrDefault("ForwardAsAttachmentTo") ?? "",
                Bool(row.GetValueOrDefault("DeleteMessage")), row.GetValueOrDefault("MoveToFolder") ?? "", row.GetValueOrDefault("Description") ?? ""));
        foreach (var row in ReadCsv(Path.Combine(folder, "mailbox_forwarding.csv")))
            r.Forwarding.Add(new MailboxForwarding(row.GetValueOrDefault("Mailbox") ?? mailbox, row.GetValueOrDefault("ForwardingSmtpAddress") ?? "",
                row.GetValueOrDefault("ForwardingAddress") ?? "", Bool(row.GetValueOrDefault("DeliverToMailboxAndForward"))));
        r.Checks.AddRange(Analyze(r, mailbox, internalDomains));
        return r;
    }

    public static IEnumerable<ControlCheck> Analyze(MailInvestigationResult r, string mailbox, IReadOnlyCollection<string> internalDomains)
    {
        bool External(string addr) =>
            addr.Split(';', ',', ' ').Select(a => a.Trim().Trim('"', '<', '>')).Where(a => a.Contains('@'))
                .Any(a => !internalDomains.Any(d => a.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase) || a.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)));
        ControlCheck C(string id, string title, ControlStatus s, string detail, IEnumerable<string> ev, string rec = "") =>
            new(id, "E-mail", title, s, detail, ev.Take(300).ToList(), rec);

        var sent = r.Messages.Where(m => m.Sender.Equals(mailbox, StringComparison.OrdinalIgnoreCase)).ToList();
        var bursts = sent.GroupBy(m => new DateTimeOffset(m.TimeUtc.UtcDateTime.Date.AddHours(m.TimeUtc.UtcDateTime.Hour), TimeSpan.Zero))
                         .Where(g => g.Count() >= 50).OrderByDescending(g => g.Count()).ToList();
        yield return C("EM01", "Rafale de mesaje trimise (50+ într-o oră)", bursts.Count == 0 ? ControlStatus.Conform : ControlStatus.Neconform,
            bursts.Count == 0 ? $"{sent.Count} mesaje trimise în perioadă, fără rafale." :
            $"{bursts.Sum(b => b.Count())} mesaje în {bursts.Count} ore de vârf — tipar specific unui cont compromis folosit pentru spam/phishing.",
            bursts.Select(b => $"{b.Key:yyyy-MM-dd HH}:00 UTC: {b.Count()} mesaje către {b.Select(m => m.Recipients).Distinct().Count()} destinatari; subiecte: {string.Join(" | ", b.Select(m => m.Subject).Distinct().Take(3))}; IP: {string.Join(", ", b.Select(m => m.ClientIp).Where(i => i.Length > 0).Distinct().Take(5))}"),
            "Schimbați parola, revocați sesiunile, verificați regulile și aplicațiile autorizate ale contului.");

        var ips = sent.Where(m => m.ClientIp.Length > 0).GroupBy(m => m.ClientIp).OrderByDescending(g => g.Count()).ToList();
        if (ips.Count > 0)
            yield return C("EM02", "Adresele IP de pe care s-a trimis", ips.Count > 3 ? ControlStatus.DeVerificat : ControlStatus.Conform,
                $"{ips.Count} adrese IP distincte.", ips.Select(g => $"{g.Key}: {g.Count()} mesaje, între {g.Min(m => m.TimeUtc):yyyy-MM-dd HH:mm} și {g.Max(m => m.TimeUtc):yyyy-MM-dd HH:mm} UTC"));

        var risky = r.Rules.Where(x => x.Enabled && (x.ForwardTo.Length + x.RedirectTo.Length + x.ForwardAsAttachmentTo.Length > 0 || x.DeleteMessage ||
                                                      x.MoveToFolder.Contains("RSS", StringComparison.OrdinalIgnoreCase) || x.MoveToFolder.Contains("Archive", StringComparison.OrdinalIgnoreCase) ||
                                                      x.MoveToFolder.Contains("Conversation History", StringComparison.OrdinalIgnoreCase) || x.Name.Length <= 2)).ToList();
        yield return C("EM03", "Reguli de inbox care redirecționează, șterg sau ascund mesaje", risky.Count == 0 ? ControlStatus.Conform :
                risky.Any(x => External(x.ForwardTo + ";" + x.RedirectTo + ";" + x.ForwardAsAttachmentTo)) ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            risky.Count == 0 ? $"{r.Rules.Count} reguli, niciuna riscantă." : $"{risky.Count} reguli riscante. Atacatorii creează astfel de reguli ca să ascundă răspunsurile sau să copieze corespondența.",
            risky.Select(x => $"„{x.Name}”: forward {x.ForwardTo} redirect {x.RedirectTo} atașament {x.ForwardAsAttachmentTo} ștergere {(x.DeleteMessage ? "da" : "nu")} mută în {x.MoveToFolder}; {x.Conditions}"),
            "Ștergeți regulile neautorizate după ce le-ați salvat ca probă.");

        var fwd = r.Forwarding.Where(f => f.ForwardingSmtpAddress.Length > 0 || f.ForwardingAddress.Length > 0).ToList();
        yield return C("EM04", "Redirecționare automată a cutiei poștale", fwd.Count == 0 ? ControlStatus.Conform :
                fwd.Any(f => External(f.ForwardingSmtpAddress.Replace("smtp:", "", StringComparison.OrdinalIgnoreCase))) ? ControlStatus.Neconform : ControlStatus.DeVerificat,
            fwd.Count == 0 ? "Nicio redirecționare." : "Corespondența este copiată automat către altă adresă.",
            fwd.Select(f => $"{f.Mailbox} → {f.ForwardingSmtpAddress} {f.ForwardingAddress} (păstrează copia: {(f.DeliverToMailboxAndForward ? "da" : "nu")})"));

        var externalRcpt = sent.Where(m => External(m.Recipients)).ToList();
        if (externalRcpt.Count > 0)
            yield return C("EM05", "Mesaje trimise în afara organizației", ControlStatus.DeVerificat, $"{externalRcpt.Count} mesaje către destinatari externi.",
                externalRcpt.GroupBy(m => m.Recipients.Split('@').LastOrDefault() ?? "").OrderByDescending(g => g.Count()).Select(g => $"@{g.Key}: {g.Count()} mesaje"));
    }

    private static bool Bool(string? s) => s is not null && (s.Equals("True", StringComparison.OrdinalIgnoreCase) || s == "1");

    private static IEnumerable<Dictionary<string, string>> ReadCsv(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var d in CsvReader.ReadDicts(path))
            yield return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
    }
}
