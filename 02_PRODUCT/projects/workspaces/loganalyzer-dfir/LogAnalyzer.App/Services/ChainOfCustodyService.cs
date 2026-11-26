using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogAnalyzer.UI.Services;

public sealed record CustodyEntry(DateTimeOffset Timestamp, string Actor, string Action, string Details, string PreviousHash, string Hash);

public sealed class ChainOfCustodyService
{
    private readonly string _path;
    private readonly object _sync = new();

    /// <remarks>No previous hash is cached: each append reads it from the file under the write handle (two app instances share this log).</remarks>
    public ChainOfCustodyService(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public CustodyEntry Append(string actor, string action, string details)
    {
        lock (_sync)
        {
            CustodyEntry? entry = null;
            // the previous hash is read under the file's write handle, so a second process or object cannot fork the chain
            LogAnalyzer.Dfir.IO.SharedAppend.WriteAfterLastLine(_path, last =>
            {
                var previous = PreviousHashOf(last);
                var timestamp = DateTimeOffset.UtcNow;
                var canonical = $"{timestamp:O}|{actor}|{action}|{details}|{previous}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                entry = new CustodyEntry(timestamp, actor, action, details, previous, hash);
                return JsonSerializer.Serialize(entry) + Environment.NewLine;
            });
            return entry!;
        }
    }

    private static string PreviousHashOf(string? last)
    {
        if (string.IsNullOrWhiteSpace(last)) return "GENESIS";
        try { return JsonSerializer.Deserialize<CustodyEntry>(last)?.Hash is { Length: > 0 } h ? h : "GENESIS"; }
        catch (JsonException) { return "GENESIS"; }
    }

    public bool Verify()
    {
        if (!File.Exists(_path)) return true;
        var previous = "GENESIS";
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            CustodyEntry? entry;
            try { entry = JsonSerializer.Deserialize<CustodyEntry>(line); }
            catch (JsonException) { return false; }   // a damaged or truncated line breaks the chain; it is reported, not thrown
            if (entry is null || entry.PreviousHash != previous) return false;
            var canonical = $"{entry.Timestamp:O}|{entry.Actor}|{entry.Action}|{entry.Details}|{entry.PreviousHash}";
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(entry.Hash))) return false;
            previous = entry.Hash;
        }
        return true;
    }
}
