using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public enum Nis2Stage
    {
        EarlyWarning_24h,   // Avertizare timpurie (24 ore de la luarea la cunoștință)
        Notification_72h,   // Notificare de incident (72 ore de la luarea la cunoștință)
        FinalReport_1Month  // Raport final (cel mult o lună de la notificarea de 72 ore)
    }

    /// <summary>The reporting entity, as the organisation enters it. Empty fields are printed as „[DE COMPLETAT]”, never invented.</summary>
    public sealed record Nis2Entity(string Name = "", string FiscalCode = "", string Category = "", string Sector = "", string ContactName = "", string ContactEmail = "", string ContactPhone = "");

    /// <summary>One response measure recorded in the case: only these may be declared as applied or in progress.</summary>
    public sealed record Nis2ResponseAction(DateTime TimeUtc, string Action, string Who, bool Completed);

    /// <summary>Legal deadlines (Directive 2022/2555 art. 23; OUG 155/2024 art. 15): all run from the moment the entity became aware.</summary>
    public static class Nis2Deadlines
    {
        public static DateTime EarlyWarning(DateTime awarenessUtc) => awarenessUtc.AddHours(24);
        public static DateTime CrossBorder(DateTime awarenessUtc) => awarenessUtc.AddHours(6);
        public static DateTime Notification(DateTime awarenessUtc) => awarenessUtc.AddHours(72);
        public static DateTime Final(DateTime notificationSentUtc) => notificationSentUtc.AddMonths(1);
    }

    /// <summary>
    /// Draft of the information an essential/important entity reports to the national CSIRT through PNRISC (docs/dfir/RESEARCH_2026-10-11_CSIRT_RAPORTARE.md §1).
    /// It is a field package to verify and paste, NOT the official form (whose exact fields are not public). Nothing is asserted that the case does not
    /// record: missing facts are marked „[DE COMPLETAT: …]”, and mitigation measures are only those in the case's response log (audit 2026-10-11: the
    /// earlier version always declared four measures as applied, counted deadlines from detection and addressed an e-mail address).
    /// </summary>
    public class Nis2NotificationService
    {
        private static string Fill(string? v, string what) => string.IsNullOrWhiteSpace(v) ? $"[DE COMPLETAT: {what}]" : v!;
        private static string When(DateTime? t, string what) => t is { } v ? $"{v:yyyy-MM-dd HH:mm} UTC ({v.ToLocalTime():yyyy-MM-dd HH:mm} ora locală)" : $"[DE COMPLETAT: {what}]";
        private static string YesNo(bool? b, string what) => b switch { true => "DA", false => "NU", _ => $"[DE COMPLETAT: {what} — da / nu / necunoscut]" };

        public string GenerateDnscDraft(
            Nis2Stage stage,
            Nis2Entity entity,
            string caseReference,
            string incidentTitle,
            string affectedSystems,
            DateTime? awarenessUtc,
            AttackStoryline? storyline,
            int totalEvents,
            int totalAlerts,
            IReadOnlyList<Nis2ResponseAction>? actions = null,
            IReadOnlyList<IocItem>? iocs = null,
            bool? suspectedMalicious = null,
            bool? crossBorder = null,
            DateTime? notificationSentUtc = null,
            DateTime? nowUtc = null)
        {
            var sb = new StringBuilder();
            var now = nowUtc ?? DateTime.UtcNow;
            actions ??= Array.Empty<Nis2ResponseAction>();

            sb.AppendLine("================================================================================");
            sb.AppendLine("PROIECT — INFORMAȚII PENTRU RAPORTAREA UNUI INCIDENT SEMNIFICATIV (Directiva NIS2 / OUG 155/2024)");
            sb.AppendLine("Se transmite echipei naționale CSIRT prin platforma PNRISC, de persoana desemnată de entitate.");
            sb.AppendLine("Acesta NU este formularul oficial: verificați fiecare câmp și completați tot ce este marcat [DE COMPLETAT].");
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            string stageName = stage switch
            {
                Nis2Stage.EarlyWarning_24h => "AVERTIZARE TIMPURIE — în cel mult 24 de ore de la luarea la cunoștință",
                Nis2Stage.Notification_72h => "NOTIFICARE DE INCIDENT — în cel mult 72 de ore de la luarea la cunoștință",
                _ => "RAPORT FINAL — în cel mult o lună de la transmiterea notificării de 72 de ore",
            };
            sb.AppendLine($"Etapa: {stageName}");
            sb.AppendLine($"Referința internă a cazului: {Fill(caseReference, "numărul cazului")}");
            sb.AppendLine($"Marcaj TLP propus: TLP:AMBER+STRICT (de confirmat de entitate)");
            sb.AppendLine($"Proiect generat: {now:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();

            sb.AppendLine("--- Termene (calculate din momentul luării la cunoștință) ---");
            sb.AppendLine($"Momentul în care entitatea a luat cunoștință de incident: {When(awarenessUtc, "data și ora la care entitatea a aflat de incident")}");
            if (awarenessUtc is { } a)
            {
                sb.AppendLine($"Avertizare timpurie: până la {Nis2Deadlines.EarlyWarning(a):yyyy-MM-dd HH:mm} UTC");
                sb.AppendLine($"Notificare de incident: până la {Nis2Deadlines.Notification(a):yyyy-MM-dd HH:mm} UTC");
                if (crossBorder == true) sb.AppendLine($"Informații despre impactul transfrontalier: în cel mult 6 ore de la aflarea lor (OUG 155/2024 art. 15 alin. 3); dacă ați aflat odată cu incidentul: până la {Nis2Deadlines.CrossBorder(a):yyyy-MM-dd HH:mm} UTC");
                var elapsed = now - a;
                sb.AppendLine($"Timp scurs de la luarea la cunoștință: {elapsed.TotalHours:0.#} ore");
            }
            else sb.AppendLine("Termenele nu se pot calcula până nu completați momentul luării la cunoștință (NU momentul detectării de către aplicație).");
            if (stage == Nis2Stage.FinalReport_1Month)
                sb.AppendLine(notificationSentUtc is { } n ? $"Raport final: până la {Nis2Deadlines.Final(n):yyyy-MM-dd} (o lună de la notificarea transmisă la {n:yyyy-MM-dd HH:mm} UTC)"
                                                           : "Raport final: [DE COMPLETAT: data transmiterii notificării de 72 de ore] — termenul este o lună de la acea dată.");
            sb.AppendLine();

            sb.AppendLine("--- 1. Entitatea ---");
            sb.AppendLine($"Denumire: {Fill(entity.Name, "denumirea entității")}");
            sb.AppendLine($"Cod de identificare fiscală: {Fill(entity.FiscalCode, "CUI")}");
            sb.AppendLine($"Categorie: {Fill(entity.Category, "esențială / importantă")}");
            sb.AppendLine($"Sector (anexele I/II NIS2): {Fill(entity.Sector, "sectorul și subsectorul")}");
            sb.AppendLine($"Persoană de contact: {Fill(entity.ContactName, "nume")} · {Fill(entity.ContactEmail, "e-mail")} · {Fill(entity.ContactPhone, "telefon")}");
            sb.AppendLine();

            sb.AppendLine("--- 2. Incidentul ---");
            sb.AppendLine($"Titlu: {Fill(incidentTitle, "titlu scurt")}");
            sb.AppendLine($"Sisteme afectate: {Fill(affectedSystems, "stațiile / serviciile afectate")}");
            sb.AppendLine($"Suspiciune de acțiune ilicită sau rău-intenționată: {YesNo(suspectedMalicious, "decizia analistului")}");
            sb.AppendLine($"Posibil impact transfrontalier: {YesNo(crossBorder, "decizia analistului, cu statele afectate")}");
            if (storyline is { Nodes.Count: > 0 })
            {
                sb.AppendLine($"Prima activitate observată în probe: {storyline.Nodes.Min(x => x.Timestamp):yyyy-MM-dd HH:mm} UTC");
                sb.AppendLine($"Evaluarea aplicației (de confirmat de analist): {storyline.RiskLevel}");
            }
            sb.AppendLine($"Volum analizat: {totalEvents} evenimente, {totalAlerts} alerte corelate.");
            if (stage != Nis2Stage.EarlyWarning_24h)
            {
                sb.AppendLine("Utilizatori afectați (estimare), durata și descrierea perturbării: [DE COMPLETAT]");
                sb.AppendLine("Date cu caracter personal afectate (dacă da: notificare separată ANSPDCP în 72 de ore, GDPR art. 33): [DE COMPLETAT]");
            }
            sb.AppendLine();

            if (storyline is { Nodes.Count: > 0 } && stage != Nis2Stage.EarlyWarning_24h)
            {
                sb.AppendLine("--- 3. Cronologia reconstituită din probe ---");
                foreach (var node in storyline.Nodes)
                    sb.AppendLine($"• {node.Timestamp:yyyy-MM-dd HH:mm:ss} UTC — {node.StageName}: {node.Title}" + (string.IsNullOrWhiteSpace(node.TechniqueId) ? "" : $" (MITRE {node.TechniqueId})"));
                sb.AppendLine();
            }

            if (stage != Nis2Stage.EarlyWarning_24h && iocs is { Count: > 0 })
            {
                sb.AppendLine("--- 4. Indicatori de compromitere ---");
                foreach (var i in iocs.Take(200)) sb.AppendLine($"• {i.Type}: {i.Value}");
                sb.AppendLine();
            }

            sb.AppendLine("--- 5. Măsuri de răspuns (doar cele înregistrate în caz) ---");
            if (actions.Count == 0)
                sb.AppendLine("Nicio măsură nu este înregistrată în caz. [DE COMPLETAT: măsurile aplicate și cele în curs, cu ora și cine le-a aplicat]");
            else
            {
                foreach (var g in actions.GroupBy(x => x.Completed).OrderByDescending(g => g.Key))
                {
                    sb.AppendLine(g.Key ? "Aplicate:" : "În curs:");
                    foreach (var x in g.OrderBy(x => x.TimeUtc)) sb.AppendLine($"• {x.TimeUtc:yyyy-MM-dd HH:mm} UTC — {x.Action} ({x.Who})");
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- 6. Pașii următori ---");
            sb.AppendLine(stage switch
            {
                Nis2Stage.EarlyWarning_24h => "Urmează notificarea de incident, în cel mult 72 de ore de la luarea la cunoștință, cu evaluarea inițială a gravității și a impactului.",
                Nis2Stage.Notification_72h => "Urmează raportul final, în cel mult o lună de la această notificare (sau un raport de progres, dacă incidentul este încă în curs).",
                _ => "[DE COMPLETAT: cauza principală, măsurile de remediere finalizate, eventualul impact transfrontalier, sesizarea organelor de urmărire penală (da/nu)]",
            });
            sb.AppendLine();
            sb.AppendLine("Proiect generat de LogAnalyzer din datele cazului. Responsabilitatea conținutului transmis aparține entității.");
            return sb.ToString();
        }
    }
}
