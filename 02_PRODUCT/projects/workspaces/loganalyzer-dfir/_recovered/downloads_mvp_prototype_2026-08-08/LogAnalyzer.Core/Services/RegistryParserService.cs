using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class RegistryParserService : IRegistryParser
    {
        private static readonly Regex KeyHeaderRegex = new Regex(@"^\[(?<key>.+)\]$", RegexOptions.Compiled);
        private static readonly Regex ValueLineRegex = new Regex("^\"(?<name>[^\"]*)\"\\s*=\\s*(?<data>.+)$", RegexOptions.Compiled);

        public IEnumerable<RegistryArtifact> ParseNtUserDat(string filePath)
        {
            if (!File.Exists(filePath))
                yield break;

            LogEroare($"NTUSER.DAT detectat la '{filePath}' - parsare hive binar neimplementata in acest MVP.");
            yield break;
        }

        public IEnumerable<RegistryArtifact> ParseRegFile(string filePath)
        {
            if (!File.Exists(filePath))
                yield break;

            string[] linii;
            try { linii = File.ReadAllLines(filePath); }
            catch (Exception ex)
            {
                LogEroare($"Nu s-a putut citi fisierul .reg '{filePath}': {ex.Message}");
                yield break;
            }

            string cheieCurenta = string.Empty;

            foreach (var linieRaw in linii)
            {
                RegistryArtifact? artefact = null;
                try
                {
                    var linie = linieRaw.Trim();
                    if (string.IsNullOrWhiteSpace(linie) || linie.StartsWith("Windows Registry Editor"))
                        continue;

                    var matchCheie = KeyHeaderRegex.Match(linie);
                    if (matchCheie.Success)
                    {
                        cheieCurenta = matchCheie.Groups["key"].Value;
                        continue;
                    }

                    var matchValoare = ValueLineRegex.Match(linie);
                    if (matchValoare.Success && !string.IsNullOrEmpty(cheieCurenta))
                    {
                        artefact = new RegistryArtifact
                        {
                            KeyPath = cheieCurenta,
                            ValueName = matchValoare.Groups["name"].Value,
                            ValueData = matchValoare.Groups["data"].Value,
                            Category = DeterminaCategorie(cheieCurenta),
                            SuspicionLevel = DeterminaSuspiciune(cheieCurenta),
                            SourceFile = filePath
                        };
                    }
                }
                catch (Exception ex)
                {
                    LogEroare($"Linie ignorata (parsare esuata) in '{filePath}': {ex.Message}");
                    continue;
                }

                if (artefact != null)
                    yield return artefact;
            }
        }

        private static string DeterminaCategorie(string cheie)
        {
            if (cheie.Contains("Run", StringComparison.OrdinalIgnoreCase)) return "Persistenta (Run Key)";
            if (cheie.Contains("Services", StringComparison.OrdinalIgnoreCase)) return "Servicii";
            if (cheie.Contains("UserAssist", StringComparison.OrdinalIgnoreCase)) return "Activitate Utilizator";
            return "Necategorizat";
        }

        private static string DeterminaSuspiciune(string cheie)
            => cheie.Contains("Run", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Info";

        private static void LogEroare(string mesaj)
        {
            try
            {
                var caleLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "erori_ingestie.log");
                File.AppendAllText(caleLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {mesaj}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
