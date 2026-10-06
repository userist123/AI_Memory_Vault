using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Core.Services
{
    public class LicenseService
    {
        private readonly string _licenseFilePath;
        private readonly string _salt = "INFOSEC_ROMANIA_SOC_2026_SECURE_KEY";

        public LicenseService()
        {
            _licenseFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.lic");
        }

        public string GetHardwareId()
        {
            string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
            string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
            string rawId = cpuId + boardId;
            return CalculateSha256(rawId).Substring(0, 16).ToUpper();
        }

        // Generhează cheia ținând cont de Hardware ID și data de expirare
        public string GenerateKey(string hwId, DateTime expiryDate)
        {
            string datePart = expiryDate.ToString("yyyyMMdd");
            return CalculateSha256(hwId.Trim().ToUpper() + datePart + _salt).Substring(0, 20).ToUpper();
        }

        /// <summary>Șirul de licență complet, în formatul acceptat de fereastra de activare: "CHEIE|AAAA-LL-ZZ".</summary>
        public string BuildLicenseString(string hwId, DateTime expiryDate)
            => $"{GenerateKey(hwId, expiryDate)}|{expiryDate:yyyy-MM-dd}";

        /// <summary>
        /// Verificare pură (fără fișiere, fără WMI): cheia corespunde Hardware ID-ului dat și nu e expirată la <paramref name="utcNow"/>.
        /// Singura sursă de adevăr folosită de activare, de verificarea la pornire și de generatorul de licențe.
        /// </summary>
        public bool VerifyLicenseString(string hwId, string fullInput, DateTime utcNow)
        {
            var parts = (fullInput ?? "").Trim().Split('|');
            if (parts.Length != 2) return false;

            string inputKey = parts[0].Trim();
            if (!DateTime.TryParseExact(parts[1].Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out DateTime expiryDate)
                && !DateTime.TryParse(parts[1], out expiryDate)) return false;

            // Licența expirată nu mai este acceptată
            if (utcNow > expiryDate) return false;

            return inputKey.Equals(GenerateKey(hwId, expiryDate), StringComparison.OrdinalIgnoreCase);
        }

        // Validează formatul introdus de client: "CHEIE|AAAA-LL-ZZ"
        public bool ValidateAndSaveKey(string fullInput)
        {
            try
            {
                if (!VerifyLicenseString(GetHardwareId(), fullInput, DateTime.UtcNow)) return false;
                // Salvăm întregul șir (Cheie + Dată) în fișierul licenței
                File.WriteAllText(_licenseFilePath, fullInput.Trim());
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Verifică la fiecare pornire dacă licența este validă și în termen
        public bool IsActivated()
        {
            if (!File.Exists(_licenseFilePath)) return false;

            try
            {
                return VerifyLicenseString(GetHardwareId(), File.ReadAllText(_licenseFilePath), DateTime.UtcNow);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private string GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                    foreach (var obj in searcher.Get()) return obj[property]?.ToString() ?? "";
                }
            }
            catch { }
            return "UNKNOWN_HW_ID_001";
        }

        private string CalculateSha256(string rawData)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}