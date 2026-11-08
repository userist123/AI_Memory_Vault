using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Privacy;

/// <summary>What kind of personal data a pseudonym stands for (master plan 6.15: the redaction policy chooses which kinds are masked).</summary>
public enum PiiCategory { Users, Hosts, Addresses, Emails, Sids, Serials }

/// <summary>One replacement: the original value and its pseudonym.</summary>
public sealed record PiiEntry(PiiCategory Category, string Original, string Pseudonym);

/// <summary>
/// F10: masks personal data in text meant for sharing (reports, timelines). User names, host names, IP addresses, e-mail addresses, SIDs and the user
/// segment of paths are replaced by consistent pseudonyms: the same value always becomes the same pseudonym within one masker, so a shared timeline
/// and a shared report masked by the same instance still correlate. The mapping stays with the operator (<see cref="Mapping"/>) and is never written
/// into the masked output. Well-known built-in accounts (SYSTEM, LOCAL SERVICE ...) are not personal data and stay as they are.
/// </summary>
public sealed class PiiMasker
{
    private static readonly TimeSpan Rx = TimeSpan.FromMilliseconds(500);
    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "LOCAL SERVICE", "NETWORK SERVICE", "ANONYMOUS LOGON", "-", "Public", "Default", "Default User", "All Users", "DefaultAppPool",
        "NT AUTHORITY", "BUILTIN", "Window Manager", "Font Driver Host", "localhost",
    };

    private readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _serials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _macs = new(StringComparer.OrdinalIgnoreCase);
    private Regex? _known;

    // IPv6 candidates (full, compressed, link-local with zone) are validated with IPAddress.TryParse; clock times and MAC addresses never parse as IPv6.
    private static readonly Regex Ipv6 = new(@"(?<![\w:.%])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?:%[0-9A-Za-z_]+)?(?![\w:])", RegexOptions.CultureInvariant, Rx);
    // USBSTOR / USB instance ids: the last segment is the device serial (or a Windows-generated id unique to the port).
    private static readonly Regex InstanceSerial = new(@"(?<pre>\b(?:USBSTOR|USB)\\[^\\\s""'<>|]+\\)(?<s>[^\\\s""'<>|]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    // 12-30 hex characters with both digits and letters, not part of a longer word, GUID or hash (hashes are 32+ and never reach here).
    private static readonly Regex HexSerial = new(@"(?<![\w-])(?=[0-9A-Fa-f]*[0-9])(?=[0-9A-Fa-f]*[A-Fa-f])[0-9A-Fa-f]{12,30}(?![\w-])", RegexOptions.CultureInvariant, Rx);
    private static readonly Regex Fqdn = new(@"(?<![\w.-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?![\w-])", RegexOptions.CultureInvariant, Rx);
    private static readonly HashSet<string> PrivateSuffix = new(StringComparer.OrdinalIgnoreCase) { "local", "lan", "corp", "internal", "intranet", "home", "domain", "localdomain" };

    private static readonly Regex Email = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant, Rx);
    private static readonly Regex Ipv4 = new(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])", RegexOptions.CultureInvariant, Rx);
    // IPv4-mapped IPv6 written with a dotted tail (::ffff:a.b.c.d, 0:0:0:0:0:ffff:a.b.c.d): one address, so one pseudonym, shared with the plain IPv4.
    private static readonly Regex MappedIpv4 = new(@"(?<![\w:.])(?:(?:0{1,4}:){5}|::(?:0{1,4}:){0,4})ffff:(?<v4>(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d))(?![\d.])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    // MAC addresses: aa:bb:cc:dd:ee:ff, aa-bb-cc-dd-ee-ff and aabb.ccdd.eeff (the same address in any notation is one value).
    private static readonly Regex Mac = new(@"(?<![\w:.-])[0-9A-Fa-f]{2}(?<sep>[:-])(?:[0-9A-Fa-f]{2}\k<sep>){4}[0-9A-Fa-f]{2}(?![\w:-])", RegexOptions.CultureInvariant, Rx);
    private static readonly Regex MacDotted = new(@"(?<![\w.])[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}\.[0-9A-Fa-f]{4}(?![\w.])", RegexOptions.CultureInvariant, Rx);
    // device interface paths use '#': USBSTOR#Disk&Ven_X&Prod_Y&Rev_1.00#serial&0#{guid}
    private static readonly Regex InstanceSerialHash = new(@"(?<pre>\b(?:USBSTOR|USB)#[^#\s""'<>|]+#)(?<s>[^#\s""'<>|]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    private static readonly Regex Sid = new(@"\bS-1-(?:5-21(?:-\d+){3,4}|12-1(?:-\d+){4})\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    private static readonly Regex ProfilePath = new(@"(?<pre>(?:[A-Za-z]:)?\\(?:Users|Documents and Settings)\\)(?<u>[^\\/:*?""<>|\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    private static readonly Regex Unc = new(@"(?<pre>\\\\)(?<h>[A-Za-z0-9][A-Za-z0-9._-]{0,62})(?=\\)", RegexOptions.CultureInvariant, Rx);
    private static readonly Regex DomainUser = new(@"\b(?<d>[A-Za-z][A-Za-z0-9-]{0,14})\\(?<u>[A-Za-z0-9._-]{1,64})\b", RegexOptions.CultureInvariant, Rx);

    /// <summary>The pseudonym given to each original value; kept by the operator, never part of the masked output.</summary>
    public IReadOnlyDictionary<string, string> Mapping =>
        _users.Concat(_hosts).Concat(_ips).Concat(_mails).Concat(_sids).Concat(_serials).Concat(_domains).Concat(_macs).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] OrgWords = ["laborator", "laboratorul", "echipa", "serviciul", "biroul", "departamentul", "directia", "direcția", "unitatea", "compartimentul", "institutul", "centrul", "autoritatea", "sectia", "secția",
        "lab", "team", "group", "department", "office", "unit", "security", "securitate", "forensics", "digital", "cyber", "investigations", "investigatii", "investigații"];
    private static readonly Regex NameToken = new(@"^\p{Lu}\p{Ll}+(?:-\p{Lu}\p{Ll}+)*$", RegexOptions.CultureInvariant, Rx);

    /// <summary>
    /// A signer label for a masked export. A label that reads as a person's display name (two to four capitalised words, none of them an organisation word) is replaced by one user pseudonym and each
    /// of its words is learned, so the name is masked everywhere else too; any other label (a laboratory, a team) is only passed through <see cref="Mask(string)"/>. Heuristic by design: the
    /// safe failure is masking an organisation label, never publishing a person's name.
    /// </summary>
    public string MaskSignerLabel(string label)
    {
        label = (label ?? "").Trim();
        if (label.Length == 0) return label;
        var tokens = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool person = tokens.Length is >= 2 and <= 4 && tokens.All(t => NameToken.IsMatch(t) && !OrgWords.Contains(t, StringComparer.OrdinalIgnoreCase));
        if (!person) return Mask(label);
        var p = User(label);
        foreach (var t in tokens.Where(t => t.Length >= 3)) _users.TryAdd(t, p);
        _known = null;
        return p;
    }

    /// <summary>
    /// A person from the users register (the sentences name the person beside the account): the whole name and each of its name-like words are learned, so
    /// „Popescu Ion (contul ion)” is masked in every sentence. The register is not in the timeline, so the masker would not know these names otherwise.
    /// </summary>
    public PiiMasker KnownPerson(string person)
    {
        person = (person ?? "").Trim();
        if (person.Length < 2) return this;
        var p = User(person);
        foreach (var t in person.Split([' ', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3 && NameToken.IsMatch(t) && !OrgWords.Contains(t, StringComparer.OrdinalIgnoreCase)))
            _users.TryAdd(t, p);
        _known = null;
        return this;
    }

    public static bool IsBuiltIn(string name) => BuiltIn.Contains(name.Trim());

    public string User(string name) => Pseudo(_users, name, "Utilizator");
    public string Host(string name) => Pseudo(_hosts, name, "Statie");

    /// <summary>Declares a user name that must be masked wherever it appears as a word (not only in known fields).</summary>
    public PiiMasker KnownUser(string name)
    {
        name = name.Trim();
        if (name.Contains('\\')) name = name[(name.LastIndexOf('\\') + 1)..];
        if (name.Length >= 2 && !IsBuiltIn(name) && !name.EndsWith('$')) { User(name); _known = null; }
        return this;
    }

    public PiiMasker KnownHost(string name)
    {
        name = name.Trim().TrimEnd('$');
        if (name.Contains('.') && !Ipv4.IsMatch(name))
        {
            KnownDomain(name[(name.IndexOf('.') + 1)..]);   // the DNS suffix of a known host is learned, so every name under it is masked
            name = name[..name.IndexOf('.')];
        }
        else if (name.Contains('.')) name = name[..name.IndexOf('.')];
        if (name.Length >= 2 && !IsBuiltIn(name) && !Ipv4.IsMatch(name)) { Host(name); _known = null; }
        return this;
    }

    /// <summary>Declares an internal DNS domain (e.g. corp.example.com): the domain itself and every host name under it are masked.</summary>
    public PiiMasker KnownDomain(string domain)
    {
        domain = domain.Trim().Trim('.');
        if (domain.Contains('.') && Fqdn.IsMatch(domain) && !_domains.ContainsKey(domain)) Pseudo(_domains, domain, "Domeniu");
        // the parent organisation domain is masked too: a host learned as pc1.corp.example.com also covers mail.example.com
        if (Registrable(domain) is { } reg && !_domains.ContainsKey(reg) && Fqdn.IsMatch(reg)) Pseudo(_domains, reg, "Domeniu");
        return this;
    }

    private static readonly HashSet<string> SecondLevel = new(StringComparer.OrdinalIgnoreCase) { "co", "com", "org", "net", "gov", "edu", "ac", "or", "ne", "go" };

    /// <summary>The organisation's registrable domain: the last two labels, or three under a country second level (firma.co.uk). Null when there is none (a bare suffix such as co.uk).</summary>
    private static string? Registrable(string domain)
    {
        var l = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (l.Length < 2) return null;
        bool cc = l[^1].Length == 2 && SecondLevel.Contains(l[^2]);
        if (cc && l.Length < 3) return null;
        return string.Join('.', l.Skip(l.Length - (cc ? 3 : 2)));
    }

    /// <summary>A host from a field that names the other end of a connection (TargetServerName, ClientName): learned only when it is one of the case's own hosts, or has no domain at all. A public host is not the case's.</summary>
    private void KnownRemoteHost(string name)
    {
        name = name.Trim().TrimEnd('$');
        if (!name.Contains('.')) { KnownHost(name); return; }
        if (!Ipv4.IsMatch(name) && IsOwnHost(name)) KnownHost(name);
    }

    private bool IsOwnHost(string name)
    {
        var first = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        return _hosts.ContainsKey(first) || _domains.Keys.Any(d => name.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] RemoteHostFields = ["TargetServerName", "ClientName"];

    /// <summary>Collects user and host names from the known fields of a timeline so they are masked everywhere, also inside free text.</summary>
    public static PiiMasker FromTimeline(IEnumerable<TimelineEvent> events, string? caseHost = null, string? caseUser = null)
    {
        var m = new PiiMasker();
        if (!string.IsNullOrWhiteSpace(caseHost)) m.KnownHost(caseHost);
        if (!string.IsNullOrWhiteSpace(caseUser)) m.KnownUser(caseUser);
        foreach (var e in events)
        {
            if (e.User.Length > 0) m.KnownUser(e.User);
            if (e.Host.Length > 0) m.KnownHost(e.Host);
            foreach (var k in UserFields) if (e.Fields.TryGetValue(k, out var v) && v.Length > 0) m.KnownUser(v);
            foreach (var k in HostFields.Except(RemoteHostFields)) if (e.Fields.TryGetValue(k, out var v) && v.Length > 0) m.KnownHost(v);
        }
        foreach (var e in events)
            foreach (var k in RemoteHostFields) if (e.Fields.TryGetValue(k, out var v) && v.Length > 0) m.KnownRemoteHost(v);
        return m;
    }

    public static readonly string[] UserFields = ["TargetUserName", "SubjectUserName", "User", "UserName", "Account", "AccountName", "SamAccountName", "MemberName"];
    public static readonly string[] HostFields = ["Computer", "WorkstationName", "Workstation", "Hostname", "HostName", "ComputerName", "TargetServerName", "ClientName"];

    /// <summary>Masks every personal identifier in <paramref name="text"/>, except the categories in <see cref="Keep"/>. Idempotent: masking masked text changes nothing.</summary>
    public string Mask(string text) => Restore(MaskAll(text));

    private string MaskAll(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = Email.Replace(text, m => Pseudo(_mails, m.Value, "email", "@mascat.invalid"));
        s = Sid.Replace(s, m => Pseudo(_sids, m.Value, "SID"));
        s = Mac.Replace(s, m => Pseudo(_macs, MacKey(m.Value), "MAC"));
        s = MacDotted.Replace(s, m => Pseudo(_macs, MacKey(m.Value), "MAC"));
        s = MappedIpv4.Replace(s, m => m.Groups["v4"].Value is "127.0.0.1" or "0.0.0.0" ? m.Value : Pseudo(_ips, m.Groups["v4"].Value, "IP"));
        s = Ipv6.Replace(s, m => MaskIpv6(m.Value));
        s = Ipv4.Replace(s, m => m.Value is "127.0.0.1" or "0.0.0.0" or "255.255.255.255" ? m.Value : Pseudo(_ips, m.Value, "IP"));
        s = Fqdn.Replace(s, m => MaskFqdn(m.Value));
        s = ProfilePath.Replace(s, m => IsBuiltIn(m.Groups["u"].Value) || IsPseudonym(m.Groups["u"].Value) ? m.Value : m.Groups["pre"].Value + User(m.Groups["u"].Value));
        s = Unc.Replace(s, m => IsPseudonym(m.Groups["h"].Value) ? m.Value : m.Groups["pre"].Value + Host(m.Groups["h"].Value.Contains('.') ? m.Groups["h"].Value[..m.Groups["h"].Value.IndexOf('.')] : m.Groups["h"].Value));
        s = InstanceSerialHash.Replace(s, m => IsPseudonym(m.Groups["s"].Value) ? m.Value : m.Groups["pre"].Value + Pseudo(_serials, m.Groups["s"].Value, "Serial"));
        s = InstanceSerial.Replace(s, m => IsPseudonym(m.Groups["s"].Value) ? m.Value : m.Groups["pre"].Value + Pseudo(_serials, m.Groups["s"].Value, "Serial"));
        s = HexSerial.Replace(s, m => Pseudo(_serials, m.Value, "Serial"));
        s = DomainUser.Replace(s, m =>
        {
            var d = m.Groups["d"].Value; var u = m.Groups["u"].Value;
            if (IsBuiltIn(d) || IsBuiltIn(u) || u.EndsWith('$') || IsPseudonym(u) || d.Length == 1) return m.Value;   // "C:\..." is a drive, not a domain
            if (!_users.ContainsKey(u) && !_hosts.ContainsKey(d)) return m.Value;   // only names that a known field confirms as an account
            return (IsPseudonym(d) ? d : Host(d)) + "\\" + User(u);
        });
        var known = _known ??= BuildKnown();
        if (known is not null)
            s = known.Replace(s, m => _users.TryGetValue(m.Value, out var u) ? u : _hosts.TryGetValue(m.Value, out var h) ? h : m.Value);
        return s;
    }

    private static string MacKey(string mac) => new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private string MaskIpv6(string candidate)
    {
        var zoneAt = candidate.IndexOf('%');
        var addr = zoneAt >= 0 ? candidate[..zoneAt] : candidate;
        if (!System.Net.IPAddress.TryParse(addr, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return candidate;
        if (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.IPv6Any)) return candidate;
        if (addr.Split(':', StringSplitOptions.RemoveEmptyEntries).Length < 2) return candidate;   // "::Add" is a scope operator, not an address
        if (ip.IsIPv4MappedToIPv6)
        {
            var v4 = ip.MapToIPv4();
            return System.Net.IPAddress.IsLoopback(v4) || v4.Equals(System.Net.IPAddress.Any) ? candidate : Pseudo(_ips, v4.ToString(), "IP");   // one address, one pseudonym, shared with the plain IPv4
        }
        // the same address in full or compressed notation (and with any zone) is one value
        return Pseudo(_ips, ip.ToString(), "IP");
    }

    private string MaskFqdn(string name)
    {
        var labels = name.Split('.');
        var first = labels[0];
        if (IsPseudonym(first) || IsBuiltIn(first)) return name;
        foreach (var d in _domains.Keys.OrderByDescending(k => k.Length))
        {
            if (name.Equals(d, StringComparison.OrdinalIgnoreCase)) return _domains[d];
            if (name.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)) return Host(first);
        }
        // a known host name with any DNS suffix is that host (host.corp.tld, host.local); "host.log" is a file name, not a name
        if (_hosts.ContainsKey(first) && (labels.Length >= 3 || labels.Length == 2 && PrivateSuffix.Contains(labels[1]))) return Host(first);
        return name;
    }

    /// <summary>Masks every text field of a timeline row (summary, user, host, process path, IP, DNS, fields) into a new row; the categories in <see cref="Keep"/> stay readable.</summary>
    public TimelineEvent Mask(TimelineEvent e)
    {
        var m = MaskRow(e);
        if (Keep.Count == 0) return m;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in m.Fields) fields[k] = Restore(v);
        return new TimelineEvent
        {
            Time = m.Time, Source = m.Source, EvidenceId = m.EvidenceId, EventId = m.EventId, Provider = m.Provider, Host = Restore(m.Host), User = Restore(m.User),
            Process = Restore(m.Process), Pid = m.Pid, Ppid = m.Ppid, Path = Restore(m.Path), RemoteIp = Restore(m.RemoteIp), RemotePort = m.RemotePort,
            Dns = Restore(m.Dns), Task = Restore(m.Task), Service = Restore(m.Service), Hash = m.Hash, Summary = Restore(m.Summary), TimeSemantics = m.TimeSemantics,
            TemporalType = m.TemporalType, Classification = m.Classification, Confidence = m.Confidence, Locator = Restore(m.Locator), Fields = fields,
            Notes = Restore(m.Notes), SourceSha256 = m.SourceSha256, ParserId = m.ParserId, ParserVersion = m.ParserVersion, SemanticType = m.SemanticType,
            TimeZoneBasis = m.TimeZoneBasis, TimeUncertainty = m.TimeUncertainty,
        };
    }

    private TimelineEvent MaskRow(TimelineEvent e)
    {
        foreach (var h in HostFields.Except(RemoteHostFields).Select(k => e.Fields.TryGetValue(k, out var hv) ? hv : "").Append(e.Host))
            if (h.Contains('.') && !Ipv4.IsMatch(h) && !IsBuiltIn(h)) KnownDomain(h[(h.IndexOf('.') + 1)..]);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in e.Fields)
            fields[k] = UserFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Contains('@') ? Mask(v)   // a UPN is an e-mail-shaped value: masked as one, so the e-mail policy governs it
                      : UserFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Length > 0 && !IsBuiltIn(v) && !v.EndsWith('$') ? User(v.Contains('\\') ? v[(v.LastIndexOf('\\') + 1)..] : v)
                      : RemoteHostFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Contains('.') && !Ipv4.IsMatch(v) && !IsOwnHost(v) ? Mask(v)   // a public host stays readable
                      : HostFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Length > 0 && !IsBuiltIn(v) ? Mask(v.Contains('.') && !Ipv4.IsMatch(v) ? Host(v[..v.IndexOf('.')]) : v)
                      : k.Contains("serial", StringComparison.OrdinalIgnoreCase) && v.Trim().Length > 0 && !IsPseudonym(v) ? Pseudo(_serials, v, "Serial")
                      : Mask(v);
        return new TimelineEvent
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider,
            Host = e.Host.Length > 0 && !IsBuiltIn(e.Host) ? Host(e.Host.Contains('.') ? e.Host[..e.Host.IndexOf('.')] : e.Host) : e.Host,
            User = e.User.Length > 0 && !IsBuiltIn(e.User) && !e.User.EndsWith('$') ? (e.User.Contains('\\') || e.User.Contains('@') ? Mask(e.User) : User(e.User)) : e.User,
            Process = Mask(e.Process), Pid = e.Pid, Ppid = e.Ppid, Path = Mask(e.Path), RemoteIp = Mask(e.RemoteIp), RemotePort = e.RemotePort,
            Dns = Mask(e.Dns), Task = Mask(e.Task), Service = Mask(e.Service), Hash = e.Hash, Summary = Mask(e.Summary), TimeSemantics = e.TimeSemantics,
            TemporalType = e.TemporalType, Classification = e.Classification, Confidence = e.Confidence, Locator = Mask(e.Locator), Fields = fields,
            Notes = Mask(e.Notes), SourceSha256 = e.SourceSha256, ParserId = e.ParserId, ParserVersion = e.ParserVersion, SemanticType = e.SemanticType,
            TimeZoneBasis = e.TimeZoneBasis, TimeUncertainty = e.TimeUncertainty,
        };
    }

    private Regex? BuildKnown()
    {
        var names = _users.Keys.Concat(_hosts.Keys).Where(n => n.Length >= 3).OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
        if (names.Count == 0) return null;
        return new Regex(@"(?<![\p{L}\p{N}_-])(?:" + string.Join("|", names) + @")(?![\p{L}\p{N}_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Rx);
    }

    // ---- master plan 6.15: redaction policy (which categories are masked) and the record of what was replaced ----

    /// <summary>Categories left readable in the output (policy „ce se mască”); empty = everything is masked.</summary>
    public HashSet<PiiCategory> Keep { get; } = [];

    private IEnumerable<(PiiCategory Category, Dictionary<string, string> Map)> Maps() =>
    [
        (PiiCategory.Users, _users), (PiiCategory.Hosts, _hosts), (PiiCategory.Hosts, _domains), (PiiCategory.Addresses, _ips), (PiiCategory.Addresses, _macs),
        (PiiCategory.Emails, _mails), (PiiCategory.Sids, _sids), (PiiCategory.Serials, _serials),
    ];

    /// <summary>Every replacement so far: category, original value, pseudonym. The originals stay with the operator; they are never part of masked output.</summary>
    public IReadOnlyList<PiiEntry> Entries => Maps().SelectMany(m => m.Map.Select(kv => new PiiEntry(m.Category, kv.Key, kv.Value))).ToList();

    /// <summary>How many distinct pseudonyms of each category the output carries (a kept category carries none).</summary>
    public IReadOnlyDictionary<PiiCategory, int> Counts =>
        Enum.GetValues<PiiCategory>().ToDictionary(c => c, c => Keep.Contains(c) ? 0 : Maps().Where(m => m.Category == c).Sum(m => m.Map.Values.Distinct().Count()));

    private Regex? _restoreRx;
    private Dictionary<string, string>? _restoreMap;
    private int _restoreVersion = -1;

    /// <summary>Puts back the original of every pseudonym of a kept category. The masker works on everything first, so text that mixes categories stays consistent.</summary>
    private string Restore(string s)
    {
        if (Keep.Count == 0 || string.IsNullOrEmpty(s)) return s;
        int version = Maps().Sum(m => m.Map.Count) * 64 + Keep.Aggregate(0, (a, c) => a | 1 << (int)c);
        if (version != _restoreVersion)
        {
            _restoreMap = new(StringComparer.Ordinal);
            foreach (var (_, map) in Maps().Where(m => Keep.Contains(m.Category)))
                foreach (var kv in map) _restoreMap.TryAdd(kv.Value, kv.Key);   // the first original given a pseudonym is the one restored
            _restoreRx = _restoreMap.Count == 0 ? null
                : new Regex(@"(?<![\p{L}\p{N}_-])(?:" + string.Join("|", _restoreMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?![\p{N}])", RegexOptions.CultureInvariant, Rx);
            _restoreVersion = version;
        }
        if (_restoreRx is null) return s;
        // a restored original can carry a value of a category that stays masked (the domain in C:\Users\ion.CONTOSO): such names are masked again
        var hostsStayMasked = !Keep.Contains(PiiCategory.Hosts);
        return _restoreRx.Replace(s, m => !_restoreMap!.TryGetValue(m.Value, out var o) ? m.Value : hostsStayMasked ? RemaskHosts(o) : o);
    }

    private Regex? _hostRx;
    private int _hostRxCount = -1;

    private string RemaskHosts(string original)
    {
        if (_hostRxCount != _hosts.Count)
        {
            var names = _hosts.Keys.Where(n => n.Length >= 3).OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
            _hostRx = names.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{N}_-])(?:" + string.Join("|", names) + @")(?![\p{L}\p{N}_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Rx);
            _hostRxCount = _hosts.Count;
        }
        if (_hostRx is null) return original;
        return _hostRx.Replace(original, m => _hosts.TryGetValue(m.Value, out var h) ? h : m.Value);
    }

    private static readonly Regex PseudoRx = new(@"^(?:Utilizator|Statie|IP|SID|email|Serial|Domeniu|MAC)-\d+", RegexOptions.CultureInvariant, Rx);
    private static bool IsPseudonym(string s) => PseudoRx.IsMatch(s);

    private string Pseudo(Dictionary<string, string> map, string value, string prefix, string suffix = "")
    {
        value = value.Trim();
        if (value.Length == 0 || IsPseudonym(value)) return value;
        if (!map.TryGetValue(value, out var p))
        {
            p = $"{prefix}-{map.Count + 1}{suffix}";
            map[value] = p;
            _known = null;
        }
        return p;
    }
}
