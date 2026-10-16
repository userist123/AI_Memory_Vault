using System;
using System.IO;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Reporting;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// "Exportă istoricul mediilor pentru ofițer" (owner decision 2026-10-16): after the control, one click builds this station's history of removable media
    /// (every medium, its connections, the file activity on it, the transfer register rows with media, the signed register in force and the anchor), the officer
    /// present signs it with the card, and it is written on the officer's medium; the export is recorded in the sealed audit first.
    /// </summary>
    public partial class StationControlViewModel
    {
        [RelayCommand]
        private void ExportMediaHistory()
        {
            if (_report is null) { Status = "Rulați întâi controlul: istoricul mediilor folosește activitatea pe fișiere găsită de control (A15) și registrul semnat în vigoare."; return; }
            System.Collections.Generic.IReadOnlyList<LogAnalyzer.Dfir.Devices.DeviceRecord> devices;
            var notes = new System.Collections.Generic.List<string>();
            try
            {
                var inv = LogAnalyzer.Dfir.Windows.Devices.DeviceInventory.Collect();
                devices = inv.Devices;
                foreach (var p in inv.Problems) notes.Add("inventarul dispozitivelor: " + p);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Status = "Mediile stației nu au putut fi citite: " + ex.Message; return; }
            var doc = MediaHistoryFlows.FromControl(_report, devices, HardwareIdOrUnknown(), DateTimeOffset.UtcNow, notes);
            var cert = PickCardCertificate("Semnează exportul istoricului mediilor (ofițerul de securitate)");
            if (cert is null) return;
            var dlg = new OpenFolderDialog { Title = "Alegeți folderul de pe mediul ofițerului unde se scrie exportul semnat" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var signed = MediaHistoryFlows.Sign(doc, cert, OfficerFlows.Trusted(OfficerStore.Shared.Load()));
                // review of PR #298: the files leave the station through the audited export as well (intent listing them first, then their SHA-256)
                string path = "";
                var name = LogAnalyzer.Dfir.Registers.MediaHistoryExport.FileName(signed.Doc);
                if (ExportAudit.RunBundle(ExportAudit.LiveCaseId, "istoric-medii-semnat", dlg.FolderName, [name, name + LogAnalyzer.Dfir.Registers.MediaHistoryExport.SignatureExtension], () =>
                    {
                        path = MediaHistoryFlows.Export(dlg.FolderName, signed, AuthApp.Record);
                        return [path, path + LogAnalyzer.Dfir.Registers.MediaHistoryExport.SignatureExtension];
                    }) is { } refused)
                { Status = "Istoricul mediilor NU a fost exportat: " + refused; return; }
                OfficerInfo = $"Istoricul mediilor a fost exportat și semnat de {OfficerFingerprint.CommonName(cert)}: {path} (și semnătura .p7s).{Environment.NewLine}" +
                              $"SHA-256: {signed.Sha256}. Medii: {doc.Media.Count}; transferuri cu medii: {doc.Transfers.Count}; registrul semnat: " +
                              (doc.RegisterVersion > 0 ? $"versiunea {doc.RegisterVersion}" : "niciunul în vigoare") + "." + Environment.NewLine +
                              "Ofițerul confruntă exporturile tuturor stațiilor pe stația lui: „Medii de stocare” → „Confruntare între stații”.";
                Status = "Exportul istoricului mediilor a fost semnat și scris.";
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                Status = "Istoricul mediilor NU a fost exportat: " + ex.Message;
            }
        }
    }
}
