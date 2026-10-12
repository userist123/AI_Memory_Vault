using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Privacy;

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
    private static readonly Regex Sid = new(@"\bS-1-5-21(?:-\d+){3,4}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    private static readonly Regex ProfilePath = new(@"(?<pre>(?:[A-Za-z]:)?\\(?:Users|Documents and Settings)\\)(?<u>[^\\/:*?""<>|\r\n]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Rx);
    private static readonly Regex Unc = new(@"(?<pre>\\\\)(?<h>[A-Za-z0-9][A-Za-z0-9._-]{0,62})(?=\\)", RegexOptions.CultureInvariant, Rx);
    private static readonly Regex DomainUser = new(@"\b(?<d>[A-Za-z][A-Za-z0-9-]{0,14})\\(?<u>[A-Za-z0-9._-]{1,64})\b", RegexOptions.CultureInvariant, Rx);

    /// <summary>The pseudonym given to each original value; kept by the operator, never part of the masked output.</summary>
    public IReadOnlyDictionary<string, string> Mapping =>
        _users.Concat(_hosts).Concat(_ips).Concat(_mails).Concat(_sids).Concat(_serials).Concat(_domains).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

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
        return this;
    }

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
            foreach (var k in HostFields) if (e.Fields.TryGetValue(k, out var v) && v.Length > 0) m.KnownHost(v);
        }
        return m;
    }

    public static readonly string[] UserFields = ["TargetUserName", "SubjectUserName", "User", "UserName", "Account", "AccountName", "SamAccountName", "MemberName"];
    public static readonly string[] HostFields = ["Computer", "WorkstationName", "Workstation", "Hostname", "HostName", "ComputerName", "TargetServerName", "ClientName"];

    /// <summary>Masks every personal identifier in <paramref name="text"/>. Idempotent: masking masked text changes nothing.</summary>
    public string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = Email.Replace(text, m => Pseudo(_mails, m.Value, "email", "@mascat.invalid"));
        s = Sid.Replace(s, m => Pseudo(_sids, m.Value, "SID"));
        s = Ipv6.Replace(s, m => MaskIpv6(m.Value));
        s = Ipv4.Replace(s, m => m.Value is "127.0.0.1" or "0.0.0.0" or "255.255.255.255" ? m.Value : Pseudo(_ips, m.Value, "IP"));
        s = Fqdn.Replace(s, m => MaskFqdn(m.Value));
        s = ProfilePath.Replace(s, m => IsBuiltIn(m.Groups["u"].Value) || IsPseudonym(m.Groups["u"].Value) ? m.Value : m.Groups["pre"].Value + User(m.Groups["u"].Value));
        s = Unc.Replace(s, m => IsPseudonym(m.Groups["h"].Value) ? m.Value : m.Groups["pre"].Value + Host(m.Groups["h"].Value.Contains('.') ? m.Groups["h"].Value[..m.Groups["h"].Value.IndexOf('.')] : m.Groups["h"].Value));
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

    private string MaskIpv6(string candidate)
    {
        var zoneAt = candidate.IndexOf('%');
        var addr = zoneAt >= 0 ? candidate[..zoneAt] : candidate;
        if (!System.Net.IPAddress.TryParse(addr, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return candidate;
        if (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.IPv6Any)) return candidate;
        if (addr.Split(':', StringSplitOptions.RemoveEmptyEntries).Length < 2) return candidate;   // "::Add" is a scope operator, not an address
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

    /// <summary>Masks every text field of a timeline row (summary, user, host, process path, IP, DNS, fields) into a new row.</summary>
    public TimelineEvent Mask(TimelineEvent e)
    {
        foreach (var h in HostFields.Select(k => e.Fields.TryGetValue(k, out var hv) ? hv : "").Append(e.Host))
            if (h.Contains('.') && !Ipv4.IsMatch(h) && !IsBuiltIn(h)) KnownDomain(h[(h.IndexOf('.') + 1)..]);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in e.Fields)
            fields[k] = UserFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Length > 0 && !IsBuiltIn(v) && !v.EndsWith('$') ? User(v.Contains('\\') ? v[(v.LastIndexOf('\\') + 1)..] : v)
                      : HostFields.Contains(k, StringComparer.OrdinalIgnoreCase) && v.Length > 0 && !IsBuiltIn(v) ? Mask(v.Contains('.') && !Ipv4.IsMatch(v) ? Host(v[..v.IndexOf('.')]) : v)
                      : k.Contains("serial", StringComparison.OrdinalIgnoreCase) && v.Trim().Length > 0 && !IsPseudonym(v) ? Pseudo(_serials, v, "Serial")
                      : Mask(v);
        return new TimelineEvent
        {
            Time = e.Time, Source = e.Source, EvidenceId = e.EvidenceId, EventId = e.EventId, Provider = e.Provider,
            Host = e.Host.Length > 0 && !IsBuiltIn(e.Host) ? Host(e.Host.Contains('.') ? e.Host[..e.Host.IndexOf('.')] : e.Host) : e.Host,
            User = e.User.Length > 0 && !IsBuiltIn(e.User) && !e.User.EndsWith('$') ? (e.User.Contains('\\') ? Mask(e.User) : User(e.User)) : e.User,
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

    private static readonly Regex PseudoRx = new(@"^(?:Utilizator|Statie|IP|SID|email|Serial|Domeniu)-\d+", RegexOptions.CultureInvariant, Rx);
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
