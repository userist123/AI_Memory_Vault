using System.Collections.Generic;
using System.Windows.Controls;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Romanian headers for the editable tables of the registers and the procedure profile (WP18 redesign). The table columns keep their
    /// internal names (JSON/CSV import and export use them); only what the person sees changes. The internal name stays in the header tooltip.
    /// </summary>
    public static class ColumnLabels
    {
        public static readonly IReadOnlyDictionary<string, string> Romanian = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Person"] = "Persoana", ["Accounts"] = "Conturi", ["Account"] = "Cont", ["Sids"] = "Identificatori cont (SID)",
            ["Clearance"] = "Autorizație", ["ClearanceValidFrom"] = "Autorizație valabilă de la", ["ClearanceValidTo"] = "Autorizație valabilă până la",
            ["NeedToKnow"] = "Necesitatea de a cunoaște", ["ZonesAllowed"] = "Zone permise",
            ["RegistrationNumber"] = "Număr de înregistrare", ["Serial"] = "Număr de serie", ["VidPid"] = "Cod producător/produs (VID:PID)",
            ["Type"] = "Tip", ["Classification"] = "Clasificare", ["AssignedUser"] = "Utilizator", ["Zone"] = "Zonă",
            ["ValidFrom"] = "Valabil de la", ["ValidTo"] = "Valabil până la", ["Status"] = "Stare", ["Notes"] = "Observații", ["Note"] = "Observații",
            ["Day"] = "Ziua", ["Start"] = "Început", ["End"] = "Sfârșit", ["Date"] = "Data", ["Name"] = "Denumire",
            ["Kind"] = "Fel", ["Ordinal"] = "A câta din lună", ["Step"] = "Pasul",
            ["Publisher"] = "Producător", ["PathPattern"] = "Cale (model)", ["Sha256"] = "Amprentă (SHA-256)",
            ["PolicyPath"] = "Calea politicii", ["Description"] = "Descriere",
            ["FromZone"] = "Din zona", ["ToZone"] = "În zona", ["Medium"] = "Suport", ["Address"] = "Adresă", ["Purpose"] = "Scop",
        };

        public static string For(string column) => Romanian.TryGetValue(column, out var ro) ? ro : column;

        /// <summary>DataGrid.AutoGeneratingColumn handler: Romanian header, internal name as tooltip.</summary>
        public static void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            var name = e.PropertyName;
            e.Column.Header = new TextBlock { Text = For(name), ToolTip = name == For(name) ? null : $"Nume intern (pentru import/export): {name}" };
        }
    }
}
