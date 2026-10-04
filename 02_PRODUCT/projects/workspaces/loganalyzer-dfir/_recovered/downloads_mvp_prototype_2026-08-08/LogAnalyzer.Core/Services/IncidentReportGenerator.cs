using System.Linq;
using System.Text;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class IncidentReportGenerator : IIncidentReportGenerator
    {
        public string GenereazaMarkdown(IncidentReport raport)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"# Raport de Incident - {raport.Rezumat.TitluIncident}");
            sb.AppendLine();
            sb.AppendLine($"**Generat la:** {raport.Rezumat.GeneratLa:dd.MM.yyyy HH:mm}  ");
            sb.AppendLine($"**Analist:** {raport.Rezumat.Analist}");
            sb.AppendLine();
            sb.AppendLine("## Rezumat incident");
            sb.AppendLine(string.IsNullOrWhiteSpace(raport.Rezumat.Rezumat) ? "(fara rezumat introdus)" : raport.Rezumat.Rezumat);
            sb.AppendLine();

            if (raport.IntegritateFisiere.Count > 0)
            {
                sb.AppendLine("## Integritate loguri");
                sb.AppendLine("| Fisier | Status | Motive |");
                sb.AppendLine("|--------|--------|--------|");
                foreach (var integ in raport.IntegritateFisiere)
                    sb.AppendLine($"| {integ.NumeFisier} | {integ.Status} | {string.Join("; ", integ.Motive)} |");
                sb.AppendLine();
            }

            sb.AppendLine("## Cronologie cheie");
            sb.AppendLine("| Timp | Sursa | Categorie | User/Host | MITRE |");
            sb.AppendLine("|------|-------|-----------|-----------|-------|");
            foreach (var e in raport.EvenimenteCheie.Take(200))
                sb.AppendLine($"| {e.Timestamp:dd.MM HH:mm:ss} | {e.Source} | {e.Category} | {e.UserOrHost} | {e.MitreTags} |");
            sb.AppendLine();

            sb.AppendLine("## Alerte cheie");
            foreach (var a in raport.AlerteCheie)
            {
                sb.AppendLine($"### {a.Title} ({a.Severity})");
                sb.AppendLine($"- **MITRE:** {a.MitreTechniqueId} - {a.MitreTechniqueName}  ");
                sb.AppendLine($"- **Status:** {a.Status} | **Clasificare:** {a.Resolution}  ");
                sb.AppendLine($"- **Descriere:** {a.Explanation}");
                if (!string.IsNullOrWhiteSpace(a.AnalystNotes))
                    sb.AppendLine($"- **Notite analist:** {a.AnalystNotes}");
                sb.AppendLine();
            }

            var iocRelevante = raport.IocuriRelevante.Where(i => i.Status == IocStatus.Suspect || i.Status == IocStatus.Malitios).ToList();
            if (iocRelevante.Count > 0)
            {
                sb.AppendLine("## Indicatori de Compromitere relevanti");
                sb.AppendLine("| Valoare | Tip | Status | Aparitii |");
                sb.AppendLine("|---------|-----|--------|----------|");
                foreach (var ioc in iocRelevante)
                    sb.AppendLine($"| {ioc.Value} | {ioc.Type} | {ioc.Status} | {ioc.Occurrences} |");
                sb.AppendLine();
            }

            sb.AppendLine("## Notitele analistului SOC");
            sb.AppendLine(string.IsNullOrWhiteSpace(raport.NotiteAnalist) ? "(fara notite)" : raport.NotiteAnalist);

            return sb.ToString();
        }
    }
}
