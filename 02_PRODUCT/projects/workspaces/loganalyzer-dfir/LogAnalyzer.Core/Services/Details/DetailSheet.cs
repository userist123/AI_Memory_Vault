using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services.Details
{
    public sealed record DetailField(string Section, string Name, string Value);

    public sealed record RelatedItem(DateTime TimeUtc, string Kind, string Summary, string Link, object Source);

    /// <summary>Everything the detail window and its PDF show for one item: fields, meaning, related items, raw form.</summary>
    public sealed class DetailSheet
    {
        public string Title { get; init; } = "";
        public string Kind { get; init; } = "";
        public DateTime? TimeUtc { get; init; }
        public List<DetailField> Fields { get; } = new();
        public List<string> Meaning { get; } = new();
        public List<RelatedItem> Related { get; } = new();
        public string Raw { get; set; } = "";
        public object? Source { get; init; }

        public string TimeText => TimeUtc is { } t
            ? $"{t:yyyy-MM-dd HH:mm:ss} UTC  ·  {TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), TimeZoneInfo.Local):yyyy-MM-dd HH:mm:ss} ora locală"
            : "fără timestamp (nu se completează cu ora curentă)";

        public string ToPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Title}  [{Kind}]");
            sb.AppendLine(TimeText);
            foreach (var g in Fields.GroupBy(f => f.Section))
            {
                sb.AppendLine().AppendLine(g.Key.ToUpperInvariant());
                foreach (var f in g) sb.AppendLine($"  {f.Name}: {f.Value}");
            }
            if (Meaning.Count > 0)
            {
                sb.AppendLine().AppendLine("CE ÎNSEAMNĂ");
                foreach (var m in Meaning) sb.AppendLine("  " + m);
            }
            if (Related.Count > 0)
            {
                sb.AppendLine().AppendLine("CORELATE");
                foreach (var r in Related) sb.AppendLine($"  {r.TimeUtc:yyyy-MM-dd HH:mm:ss}  {r.Kind}  {r.Summary}  ({r.Link})");
            }
            if (Raw.Length > 0) sb.AppendLine().AppendLine("BRUT").AppendLine(Raw);
            return sb.ToString();
        }
    }

    /// <summary>Builds a <see cref="DetailSheet"/> for any item the application shows in a grid.</summary>
    public static class DetailSheetBuilder
    {
        /// <summary>Events loaded in the session, used to find related events. Set by the main view model.</summary>
        public static Func<IEnumerable<ParsedEvent>>? EventCorpus { get; set; }

        public static DetailSheet Build(object item) => item switch
        {
            ParsedEvent ev => ForEvent(ev),
            RegistryArtifact ra => ForRegistry(ra),
            TimelineItem ti => ForTimeline(ti),
            _ => ForObject(item),
        };

        // ---- Windows events ------------------------------------------------------------------------------------

        private static DetailSheet ForEvent(ParsedEvent ev)
        {
            var sheet = new DetailSheet
            {
                Title = $"Eveniment {ev.EventId} — {ev.ProviderName}",
                Kind = "Eveniment Windows",
                TimeUtc = ev.TimeCreated == default ? null : ev.TimeCreated.Kind == DateTimeKind.Local ? ev.TimeCreated.ToUniversalTime() : ev.TimeCreated,
                Source = ev,
            };
            Add(sheet, "Antet", "Event ID", ev.EventId.ToString());
            Add(sheet, "Antet", "Furnizor", ev.ProviderName);
            Add(sheet, "Antet", "Nivel", ev.Level);
            Add(sheet, "Antet", "Calculator", ev.MachineName);

            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryParseXml(ev.XmlData, out var doc))
            {
                sheet.Raw = doc!.ToString();
                ReadEventXml(doc!, sheet, data);
            }
            else if (!string.IsNullOrWhiteSpace(ev.XmlData))
            {
                sheet.Raw = ev.XmlData!;
                Add(sheet, "Date", "Conținut", ev.XmlData);
            }
            if (!string.IsNullOrWhiteSpace(ev.Message)) Add(sheet, "Mesaj", "Text", ev.Message);

            EventMeaning.Explain(ev.EventId, ev.ProviderName ?? "", data, sheet.Meaning);
            if (!string.IsNullOrWhiteSpace(ev.OfficialDescription)) sheet.Meaning.Add("Descriere: " + ev.OfficialDescription);
            if (!string.IsNullOrWhiteSpace(ev.PotentialCriticality)) sheet.Meaning.Add("Criticitate potențială: " + ev.PotentialCriticality);
            if (!string.IsNullOrWhiteSpace(ev.TacticalExample)) sheet.Meaning.Add("Exemplu de abuz: " + ev.TacticalExample);
            if (!string.IsNullOrWhiteSpace(ev.ReferenceUrl)) sheet.Meaning.Add("Referință: " + ev.ReferenceUrl);

            FindRelated(ev, data, sheet);
            return sheet;
        }

        private static void ReadEventXml(XDocument doc, DetailSheet sheet, Dictionary<string, string> data)
        {
            var root = doc.Root!;
            var system = root.Elements().FirstOrDefault(e => e.Name.LocalName == "System");
            if (system is not null)
            {
                foreach (var e in system.Elements())
                {
                    string name = e.Name.LocalName;
                    if (e.HasAttributes && !e.HasElements && string.IsNullOrEmpty(e.Value))
                        foreach (var a in e.Attributes()) Add(sheet, "System", $"{name}.{a.Name.LocalName}", a.Value);
                    else Add(sheet, "System", name, e.Value);
                }
            }
            foreach (var section in root.Elements().Where(e => e.Name.LocalName is "EventData" or "UserData" or "RenderingInfo"))
            {
                int unnamed = 0;
                foreach (var d in section.Descendants().Where(x => !x.HasElements))
                {
                    var name = d.Attribute("Name")?.Value ?? (d.Name.LocalName == "Data" ? $"Data[{unnamed++}]" : d.Name.LocalName);
                    var value = d.Value;
                    data[name] = value;
                    Add(sheet, section.Name.LocalName, name, Annotate(name, value));
                }
            }
        }

        /// <summary>Adds a plain-language reading next to codes the investigator would otherwise look up.</summary>
        private static string Annotate(string name, string value)
        {
            var n = name.ToLowerInvariant();
            if (n == "logontype" && EventMeaning.LogonTypes.TryGetValue(value, out var lt)) return $"{value} ({lt})";
            if ((n is "status" or "substatus" or "failurereason") && EventMeaning.NtStatus.TryGetValue(value.ToLowerInvariant(), out var st)) return $"{value} ({st})";
            if (n.EndsWith("sid", StringComparison.Ordinal) && EventMeaning.WellKnownSids.TryGetValue(value, out var sid)) return $"{value} ({sid})";
            if (n is "tokenelevationtype" && EventMeaning.ElevationTypes.TryGetValue(value, out var el)) return $"{value} ({el})";
            return value;
        }

        private static readonly string[] CorrelationKeys =
            { "TargetLogonId", "SubjectLogonId", "LogonId", "NewProcessId", "ProcessId", "IpAddress", "TargetUserName", "SubjectUserName", "WorkstationName" };

        private static void FindRelated(ParsedEvent ev, Dictionary<string, string> data, DetailSheet sheet)
        {
            var corpus = EventCorpus?.Invoke();
            if (corpus is null) return;
            var keys = CorrelationKeys.Where(k => data.TryGetValue(k, out var v) && IsMeaningful(v))
                                      .Select(k => (Key: k, Value: data[k])).ToList();
            var window = TimeSpan.FromMinutes(10);
            foreach (var other in corpus)
            {
                if (ReferenceEquals(other, ev) || other.TimeCreated == default) continue;
                if ((other.TimeCreated - ev.TimeCreated).Duration() > window) continue;
                string? reason = null;
                if (keys.Count > 0 && other.XmlData is { Length: > 0 } x)
                    foreach (var (k, v) in keys)
                        if (x.Contains($">{v}<", StringComparison.OrdinalIgnoreCase)) { reason = $"{k} = {v}"; break; }
                if (reason is null) continue;
                sheet.Related.Add(new RelatedItem(other.TimeCreated, $"EID {other.EventId}", Shorten(other.Message ?? other.ProviderName ?? "", 140), reason, other));
                if (sheet.Related.Count >= 200) break;
            }
            sheet.Related.Sort((a, b) => a.TimeUtc.CompareTo(b.TimeUtc));
        }

        private static bool IsMeaningful(string v) =>
            !string.IsNullOrWhiteSpace(v) && v is not ("-" or "0x0" or "0" or "::1" or "127.0.0.1" or "SYSTEM" or "LOCAL SERVICE" or "NETWORK SERVICE" or "0x3e7" or "0x3e5" or "0x3e4");

        // ---- registry / timeline ----------------------------------------------------------------------------

        private static DetailSheet ForRegistry(RegistryArtifact ra)
        {
            var sheet = new DetailSheet { Title = $"Registry — {ra.ValueName ?? ra.KeyPath}", Kind = "Artefact registry", TimeUtc = ra.LastWriteTime?.ToUniversalTime(), Source = ra };
            Add(sheet, "Cheie", "Cale", ra.KeyPath);
            Add(sheet, "Cheie", "Hive", ra.HiveType);
            Add(sheet, "Valoare", "Nume", ra.ValueName);
            Add(sheet, "Valoare", "Date", ra.ValueData);
            Add(sheet, "Evaluare", "Categorie", ra.Category);
            Add(sheet, "Evaluare", "Nivel de suspiciune", ra.SuspicionLevel);
            if (ra.LastWriteTime is null) sheet.Meaning.Add("Ora ultimei scrieri a cheii nu este disponibilă în această sursă.");
            else sheet.Meaning.Add("Ora afișată este ultima scriere a CHEII (LastWriteTime), nu neapărat momentul în care a fost setată această valoare.");
            var path = (ra.KeyPath ?? "").ToLowerInvariant();
            if (path.Contains(@"\run")) sheet.Meaning.Add("Cheie de pornire automată: programul din valoare rulează la fiecare autentificare/pornire. Mecanism frecvent de persistență (MITRE T1547.001).");
            if (path.Contains(@"\services\")) sheet.Meaning.Add("Configurație de serviciu Windows: ImagePath/ServiceDll indică ce rulează ca serviciu (MITRE T1543.003).");
            if (path.Contains("userassist")) sheet.Meaning.Add("UserAssist: programe lansate din interfața grafică de utilizator (numele sunt codate ROT13).");
            sheet.Raw = JsonSerializer.Serialize(ra, new JsonSerializerOptions { WriteIndented = true });
            return sheet;
        }

        private static DetailSheet ForTimeline(TimelineItem ti)
        {
            var sheet = new DetailSheet { Title = string.IsNullOrEmpty(ti.Title) ? ti.Category : ti.Title, Kind = "Element de cronologie", TimeUtc = ti.Timestamp == default ? null : ti.Timestamp.ToUniversalTime(), Source = ti };
            Add(sheet, "Element", "Sursă", ti.Source);
            Add(sheet, "Element", "Categorie", ti.Category);
            Add(sheet, "Element", "Severitate", ti.Severity);
            Add(sheet, "Element", "Utilizator / stație", ti.UserOrHost);
            Add(sheet, "Element", "MITRE", ti.MitreTags);
            Add(sheet, "Descriere", "Text", ti.Description);
            var corpus = EventCorpus?.Invoke();
            if (corpus is not null && ti.Timestamp != default)
                foreach (var ev in corpus.Where(e => (e.TimeCreated - ti.Timestamp).Duration() <= TimeSpan.FromMinutes(2)).Take(100))
                    sheet.Related.Add(new RelatedItem(ev.TimeCreated, $"EID {ev.EventId}", Shorten(ev.Message ?? "", 140), "±2 minute", ev));
            sheet.Raw = JsonSerializer.Serialize(ti, new JsonSerializerOptions { WriteIndented = true });
            return sheet;
        }

        // ---- anything else ----------------------------------------------------------------------------------

        private static DetailSheet ForObject(object item)
        {
            var type = item.GetType();
            var sheet = new DetailSheet { Title = DisplayName(item), Kind = type.Name, TimeUtc = FindTime(item), Source = item };
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
            {
                object? v;
                try { v = p.GetValue(item); }
                catch (TargetInvocationException) { continue; }
                if (v is null) continue;
                if (v is string or ValueType) { Add(sheet, "Proprietăți", p.Name, Format(v)); continue; }
                if (v is IEnumerable e)
                {
                    int i = 0;
                    foreach (var x in e)
                    {
                        if (i++ >= 200) break;
                        Add(sheet, p.Name, $"#{i}", x is string or ValueType ? Format(x) : Flatten(x));
                    }
                    continue;
                }
                foreach (var sp in v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(sp => sp.GetIndexParameters().Length == 0))
                {
                    object? sv;
                    try { sv = sp.GetValue(v); } catch (TargetInvocationException) { continue; }
                    if (sv is string or ValueType) Add(sheet, p.Name, sp.Name, Format(sv));
                }
            }
            try { sheet.Raw = JsonSerializer.Serialize(item, item.GetType(), new JsonSerializerOptions { WriteIndented = true, MaxDepth = 16 }); }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException) { sheet.Raw = item.ToString() ?? ""; }
            return sheet;
        }

        private static string DisplayName(object item)
        {
            foreach (var n in new[] { "Title", "Name", "IncidentId", "FindingId", "Path", "ProgramPath", "Summary" })
                if (item.GetType().GetProperty(n)?.GetValue(item) is string s && s.Length > 0) return s;
            return item.GetType().Name;
        }

        private static DateTime? FindTime(object item)
        {
            foreach (var n in new[] { "TimeUtc", "TimeCreated", "Timestamp", "CreatedUtc", "CreatedAt", "FirstSeenUtc", "Time" })
            {
                var v = item.GetType().GetProperty(n)?.GetValue(item);
                switch (v)
                {
                    case DateTime d when d != default: return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
                    case DateTimeOffset o: return o.UtcDateTime;
                }
                if (v?.GetType().GetProperty("Utc")?.GetValue(v) is DateTimeOffset u) return u.UtcDateTime;
            }
            return null;
        }

        private static string Flatten(object x) =>
            string.Join("; ", x.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => { try { var v = p.GetValue(x); return v is string or ValueType ? $"{p.Name}={Format(v)}" : null; } catch (TargetInvocationException) { return null; } })
                .Where(s => s is not null));

        private static string Format(object? v) => v switch
        {
            null => "",
            DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset o => o.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            bool b => b ? "da" : "nu",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
        };

        private static bool TryParseXml(string? xml, out XDocument? doc)
        {
            doc = null;
            if (string.IsNullOrWhiteSpace(xml) || !xml.TrimStart().StartsWith('<')) return false;
            try { doc = XDocument.Parse(xml); return doc.Root is not null; }
            catch (System.Xml.XmlException) { return false; }
        }

        private static void Add(DetailSheet s, string section, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) s.Fields.Add(new DetailField(section, name, value));
        }

        private static string Shorten(string s, int n)
        {
            s = s.ReplaceLineEndings(" ");
            return s.Length <= n ? s : s[..n] + "…";
        }
    }
}
