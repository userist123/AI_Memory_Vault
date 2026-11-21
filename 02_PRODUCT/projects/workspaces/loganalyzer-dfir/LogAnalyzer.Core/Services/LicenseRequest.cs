using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogAnalyzer.Core.Services
{
    /// <summary>
    /// The offline licence request file (<c>cerere_licenta_&lt;hwid&gt;.json</c>). It carries exactly four keys: product, module (of the executable),
    /// hw and requestedUtc. No user name, no machine name, nothing else. Only a file is written; nothing is sent anywhere.
    /// </summary>
    public static class LicenseRequest
    {
        public const int MaxBytes = 4096;
        public static readonly string[] Keys = ["product", "module", "hw", "requestedUtc"];

        public sealed record Content(string Product, string Module, string HardwareId, string RequestedUtc);

        public static string FileName(string hardwareId) => "cerere_licenta_" + hardwareId.Trim().ToUpperInvariant() + ".json";

        public static string Build(string hardwareId, string module, DateTime utcNow)
        {
            var hw = (hardwareId ?? "").Trim().ToUpperInvariant();
            if (!LicenseService.IsHardwareId(hw)) throw new ArgumentException("Hardware ID invalid.", nameof(hardwareId));
            if (!LicenseFeatures.IsKnownModule(module)) throw new ArgumentException("Modul necunoscut.", nameof(module));
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("product", LicenseService.Product);
                w.WriteString("module", module);
                w.WriteString("hw", hw);
                w.WriteString("requestedUtc", utcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>Strict parse of a request file: exactly the four keys, valid hardware id and module; null otherwise.</summary>
        public static Content? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxBytes) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                int n = 0;
                foreach (var p in root.EnumerateObject())
                {
                    if (Array.IndexOf(Keys, p.Name) < 0 || p.Value.ValueKind != JsonValueKind.String) return null;
                    n++;
                }
                if (n != Keys.Length) return null; // a duplicate key would make the count differ from the distinct-key check below
                string Get(string k) => root.GetProperty(k).GetString() ?? "";
                var hw = Get("hw").Trim().ToUpperInvariant();
                var module = Get("module");
                if (Get("product") != LicenseService.Product || !LicenseService.IsHardwareId(hw) || !LicenseFeatures.IsKnownModule(module)) return null;
                return new Content(LicenseService.Product, module, hw, Get("requestedUtc"));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
        }
    }
}
