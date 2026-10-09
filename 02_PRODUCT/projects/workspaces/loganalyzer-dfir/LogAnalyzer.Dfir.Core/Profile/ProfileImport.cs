using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Profile;

/// <summary>Result of an import / paste / load: what was accepted and every problem found, per line. Nothing is dropped silently.</summary>
public sealed class ProfileImportResult
{
    public ProcedureProfile? Profile { get; init; }
    public int RowsAccepted { get; init; }
    public int RowsRejected { get; init; }
    public List<ProfileIssue> Issues { get; init; } = [];
    public bool HasErrors => Issues.Any(i => i.IsError);
}

public static class ProfileImport
{
    /// <summary>The profile's own JSON format. A newer major version is refused; unparseable JSON is one error, not an empty profile.</summary>
    public static ProfileImportResult FromJson(string json)
    {
        ProcedureProfile? p;
        try { p = JsonSerializer.Deserialize<ProcedureProfile>(json, Json.Options); }
        catch (JsonException ex) { return Failed($"JSON invalid: {ex.Message}"); }
        if (p is null) return Failed("JSON gol");
        if (SchemaVersions.Major(p.SchemaVersion) < 1 || SchemaVersions.Major(p.SchemaVersion) > 1)
            return Failed($"schema_version „{p.SchemaVersion}” nu este suportată (profil de procedură 1.x)");
        // Null lists in a hand-edited file are empty tables.
        p.WorkingHours ??= []; p.Holidays ??= []; p.Shifts ??= []; p.ApprovedAccounts ??= []; p.MaintenanceWindows ??= []; p.RotationProcedure ??= [];
        p.ApprovedSoftware ??= []; p.ExpectedPolicies ??= []; p.Zones ??= []; p.TransferChannels ??= []; p.NetworkDestinations ??= [];
        var issues = ProfileOps.Validate(p);
        int rows = ProfileTables.All.Sum(t => ProfileTables.Count(p, t));
        var bad = issues.Where(i => i.IsError && i.Line > 0).Select(i => (i.Table, i.Line)).Distinct().Count();
        return new ProfileImportResult { Profile = p, RowsAccepted = rows - bad, RowsRejected = bad, Issues = issues };

        static ProfileImportResult Failed(string msg) => new() { Issues = [new ProfileIssue(null, 0, msg)] };
    }

    /// <summary>
    /// CSV or tab-separated text (clipboard paste from Excel / a table) for ONE table, added to <paramref name="target"/>. A header row with the
    /// column names is optional (columns are then matched by name); without one the columns are positional. Valid rows are appended; every
    /// invalid row is listed with its line number and is NOT added.
    /// </summary>
    public static ProfileImportResult Text(ProcedureProfile target, ProfileTable table, string text, bool replace = false)
    {
        var issues = new List<ProfileIssue>();
        var lines = SplitRecords(text, out var delimiter);
        var cols = ProfileTables.Columns(table);
        var map = Enumerable.Range(0, cols.Count).ToArray();   // column i of the table <- field map[i] of the line
        int first = 0;
        if (lines.Count > 0)
        {
            var header = lines[0].Fields;
            var byName = cols.Select(c => header.FindIndex(h => h.Trim().Equals(c, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (byName.Count(i => i >= 0) >= Math.Max(1, cols.Count / 2) && byName[0] >= 0 || byName.Count(i => i >= 0) == cols.Count) { map = byName; first = 1; }
        }
        var accepted = new List<string[]>();
        var existing = replace ? [] : ProfileTables.Cells(target, table);
        var zones = target.Zones.Select(z => z.Name.Trim()).ToList();
        if (table == ProfileTable.Zones) zones = [];
        int rejected = 0;
        for (int i = first; i < lines.Count; i++)
        {
            var (lineNo, f) = lines[i];
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            var cells = map.Select(ix => ix >= 0 && ix < f.Count ? f[ix].Trim() : "").ToArray();
            if (f.Count > cols.Count && map.SequenceEqual(Enumerable.Range(0, cols.Count)))
                issues.Add(new ProfileIssue(table, lineNo, $"{f.Count} câmpuri în loc de {cols.Count}; câmpurile în plus ({string.Join(" | ", f.Skip(cols.Count))}) nu sunt folosite", false));
            // Validate against the zones known so far (the zones table may be pasted first, in another call).
            var problems = ProfileTables.ValidateRow(table, cells, table == ProfileTable.Zones ? null : zones);
            if (problems.Any(x => x.IsError))
            {
                rejected++;
                foreach (var (msg, err) in problems) issues.Add(new ProfileIssue(table, lineNo, msg, err));
                continue;
            }
            foreach (var (msg, err) in problems) issues.Add(new ProfileIssue(table, lineNo, msg, err));
            accepted.Add(cells);
        }
        ProfileTables.SetCells(target, table, existing.Concat(accepted));
        return new ProfileImportResult { Profile = target, RowsAccepted = accepted.Count, RowsRejected = rejected, Issues = issues };
    }

    // ---- minimal RFC 4180 reader (quotes, embedded delimiters / newlines); delimiter = tab, semicolon or comma, whichever the header line uses ----

    private sealed record Record(int Line, List<string> Fields);

    private static List<Record> SplitRecords(string text, out char delimiter)
    {
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n', 2)[0];
        delimiter = firstLine.Contains('\t') ? '\t' : firstLine.Contains(';') && !firstLine.Contains(',') ? ';' : ',';
        var records = new List<Record>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false, any = false;
        int line = 1, recordLine = 1;
        void EndField() { fields.Add(sb.ToString()); sb.Clear(); any = true; }
        void EndRecord() { if (any || sb.Length > 0) { EndField(); records.Add(new Record(recordLine, fields)); } fields = []; any = false; recordLine = line; }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { sb.Append(c); if (c == '\n') line++; }
            }
            else if (c == '"' && sb.Length == 0) { quoted = true; any = true; }
            else if (c == delimiter) EndField();
            else if (c == '\r') { }
            else if (c == '\n') { line++; EndRecord(); }
            else sb.Append(c);
        }
        EndRecord();
        return records;
    }
}
