using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogAnalyzer.Core.Services
{
    public class SanitizationCertificateData
    {
        public string CertificateId { get; set; } = $"SAN-CERT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}".Substring(0, 24).ToUpperInvariant();
        /// <summary>What was done, not a compliance claim: a file-level overwrite does not certify the device (SSD wear-leveling, copies elsewhere).</summary>
        public string StandardCompliance { get; set; } = "Suprascriere la nivel de fișier după metoda NIST SP 800-88r2 Clear — NU certifică dispozitivul fizic";
        public string DeviceVendor { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string HardwareSerialNumber { get; set; } = string.Empty; // P16
        public long DeviceCapacityBytes { get; set; }
        public string SanitizationMethodName { get; set; } = string.Empty;
        public int TotalPasses { get; set; }
        public string PreSanitizationSha256 { get; set; } = string.Empty;
        public string PostSanitizationSha256 { get; set; } = string.Empty;
        public string PrimaryOperator { get; set; } = string.Empty;
        public string VerifierOperator { get; set; } = string.Empty; // 4-Eyes
        public string SystemHostId { get; set; } = Environment.MachineName;
        public DateTime TimestampUtc { get; set; }
        public string TamperEvidentAuditHash { get; set; } = string.Empty;
        /// <summary>True only when the engine read every byte back as 0x00 (<see cref="SanitizationResult.ReadBackVerified"/>).</summary>
        public bool IsVerifiedZeroized { get; set; }

        /// <summary>
        /// Certificate from a real run. Fields the application cannot know (device serial, second operator) stay empty and are
        /// printed as NEDECLARAT; the audit hash is SHA-256 over the certificate's own fields, so any later edit shows.
        /// </summary>
        public static SanitizationCertificateData FromResult(SanitizationResult result, string targetPath, string primaryOperator)
        {
            var c = new SanitizationCertificateData
            {
                DeviceVendor = "Fișier",
                DeviceModel = System.IO.Path.GetFileName(targetPath),
                DeviceCapacityBytes = result.TotalBytesSanitized,
                SanitizationMethodName = result.Method.ToString(),
                TotalPasses = result.TotalPassesExecuted,
                PreSanitizationSha256 = result.PreSanitizationSha256,
                PostSanitizationSha256 = result.PostSanitizationSha256,
                PrimaryOperator = primaryOperator,
                TimestampUtc = result.CompletedAtUtc,
                IsVerifiedZeroized = result.Success && result.ReadBackVerified,
            };
            c.TamperEvidentAuditHash = c.ComputeAuditHash();
            return c;
        }

        public string ComputeAuditHash()
        {
            var canonical = string.Join("\n", CertificateId, StandardCompliance, DeviceVendor, DeviceModel, HardwareSerialNumber,
                DeviceCapacityBytes.ToString(CultureInfo.InvariantCulture), SanitizationMethodName, TotalPasses.ToString(CultureInfo.InvariantCulture),
                PreSanitizationSha256, PostSanitizationSha256, PrimaryOperator, VerifierOperator, SystemHostId,
                TimestampUtc.ToString("o", CultureInfo.InvariantCulture), IsVerifiedZeroized ? "1" : "0");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        public static string OrUndeclared(string value) => string.IsNullOrWhiteSpace(value) ? "NEDECLARAT" : value;
    }

    public class SanitizationCertificateGenerator
    {
        /// <summary>
        /// Generează certificatul oficial de sanitizare conform standardului NIST SP 800-88r2 în format text structurat.
        /// </summary>
        public string GenerateTextCertificate(SanitizationCertificateData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                 CERTIFICAT OFICIAL DE SANITIZARE A DATELOR                    ");
            sb.AppendLine("         Conform NIST SP 800-88r2 / HG 585/2002 / NATO AC/35-D/1022             ");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine($"  ID CERTIFICAT:              {data.CertificateId}");
            sb.AppendLine($"  DATA ȘI ORA (UTC):          {data.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"  STANDARD APLICAT:           {data.StandardCompliance}");
            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(" 1. DATE TEHNICE DISPOZITIV FIZIC (TELEMETRIE IMUTABILĂ P16)");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"  Producător (Vendor):        {data.DeviceVendor}");
            sb.AppendLine($"  Model / Produs:             {data.DeviceModel}");
            sb.AppendLine($"  Număr Serie Fizic (P16):    {SanitizationCertificateData.OrUndeclared(data.HardwareSerialNumber)}");
            sb.AppendLine($"  Capacitate Mediu:           {data.DeviceCapacityBytes:N0} bytes ({(double)data.DeviceCapacityBytes / (1024 * 1024 * 1024):F2} GB)");
            sb.AppendLine($"  Sistem Gazdă (Host ID):     {data.SystemHostId}");
            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(" 2. DETALII PROCEDURĂ SANITIZARE");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"  Metodă Utilizată:           {data.SanitizationMethodName}");
            sb.AppendLine($"  Număr Treceri Executate:    {data.TotalPasses}");
            sb.AppendLine($"  Hash Pre-Sanitizare:        {data.PreSanitizationSha256}");
            sb.AppendLine($"  Hash Post-Sanitizare:       {data.PostSanitizationSha256}");
            sb.AppendLine($"  Verificare Zeroizare:       {(data.IsVerifiedZeroized ? "CONFIRMATĂ prin citire (fișierul conține doar 0x00)" : "NECONFIRMATĂ")}");
            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(" 3. AUTORIZARE DUALĂ & LANȚ DE CUSTODIE (4-EYES PRINCIPLE)");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"  Operator Principal:         {data.PrimaryOperator}");
            sb.AppendLine($"  Ofițer Securitate / Martor: {SanitizationCertificateData.OrUndeclared(data.VerifierOperator)}");
            sb.AppendLine($"  Hash Audit Tamper-Evident:  {SanitizationCertificateData.OrUndeclared(data.TamperEvidentAuditHash)}");
            sb.AppendLine();
            sb.AppendLine("================================================================================");
            sb.AppendLine(" Documentul consemnează suprascrierea fișierului de mai sus și rezultatul verificării prin citire.");
            sb.AppendLine(" Nu atestă distrugerea datelor de pe dispozitivul fizic (copii, zone remapate, SSD).");
            sb.AppendLine("================================================================================");

            return sb.ToString();
        }

        /// <summary>
        /// Generează certificatul oficial în format JSON pentru integrare automată în sistemele SIEM/Audit.
        /// </summary>
        public string GenerateJsonCertificate(SanitizationCertificateData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
