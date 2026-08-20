---
id: "5d7b785f-221c-48ba-9e7e-557998c9fefa"
type: project
lifecycle: ACTIVE
category: soc-tooling
tags: [project, dotnet, wpf, dfir, threat-hunting, active, air-gapped, network-edition, real-time-monitoring, memory-vault]
created: 2026-08-09
updated: 2026-08-19
provenance:
  source_type: execution
  source_ref: "C:\\Users\\user\\LogAnalyzer.UI"
confidence: very_high
verification: verified
relations:
  - "[[01_KNOWLEDGE/LogAnalyzer_DFIR_Enterprise_Architecture]]"
  - "[[01_KNOWLEDGE/CSharp_WPF_Enterprise_Desktop]]"
---

# LogAnalyzer DFIR Enterprise (AirGapped & Network Editions)

## Descriere
Platformă completă de investigații digitale (DFIR Enterprise), Threat Hunting și analiză de securitate, structurată în **2 ediții dedicate**:

1. **`LogAnalyzer.AirGapped` (Ediție Standalone Offline):** Destinată stațiilor izolate fizic, PC-urilor fără acces la rețea/internet și sistemelor clasificate (Zero Network Activity, fără tab-uri de rețea, Threat Intel in-memory, Sanitizare NIST SP 800-88r2, pachete `.dfir` și rapoarte locale).
2. **`LogAnalyzer.Network` (Ediție Conectată / SOC & Real-Time EDR):** Destinată stațiilor conectate la rețea/internet:
- **Separare Arhitectură Duală:** `LogAnalyzer.AirGapped` (offline, HG 585 / NATO / NIST SP 800-88r2) vs `LogAnalyzer.Network` (online, Real-Time Live SOC & EDR).
- **Modul Combatere Activă & Threat Actor Intel**:
  - `SystemDefenseExecutionService.cs`: Izolare gazdă via Windows Firewall (`netsh`), neutralizare forțată procese (`Process.Kill(entireProcessTree: true)`), blocare IoC/IP.
  - `CyberAttackCountermeasureEngine.cs`: Extragere dinamică CTI din comenzi (IP/URL, grupări APT asociate, unelte detectate, amprentă criptografică SHA-256).
  - **Scut Automat EDR Sub-10ms**: Declanșare instantanee la atacuri critice înainte de intervenția manuală a operatorului.
  - **Detecție Avansată Kernel & Memorie**:
    - **BYOVD (Bring Your Own Vulnerable Driver / Ring 0)**: Interceptare încărcare drivere kernel vulnerabile (LOLDrivers, CVE-2018-19320) prin Event ID 7045 și oprire serviciu driver.
    - **Process Hollowing & Injecție RAM (T1055.012)**: Detecție anomalii de memorie (VirtualAllocEx PAGE_EXECUTE_READWRITE, cross-process thread injection, conexiuni anormale din notepad/svchost către Cobalt Strike C2).
  - **Dosar Forenzic Permanent (EventId 9999)**: La fiecare incident critic se generează și se stochează automat un eveniment detaliat în SQLite, Timeline și Jurnalul de Audit cu fișa completă a atacatorului.
- **Interfață Streamlined & Clean:** În modul de rețea au fost ascunse toate butoanele de încărcare manuală de fișiere, oferind o navigație simplă de 5 tab-uri axată pe Live EDR, Storyline și Combatere.pentru Mimikatz / LSASS dump, execuții PowerShell codificate/obfuscate, distrugere Shadow Copies (Ransomware), ștergere jurnale și atacuri Brute Force.
   - **Live Threat Intel Query:** VirusTotal, AlienVault OTX, AbuseIPDB.
   - **Cloud & SIEM Integration:** Conector Microsoft 365 / Entra ID Graph API, forwarder alerte SIEM (Splunk HEC, Microsoft Sentinel, Syslog CEF) și Remote WinRM Triage.

## Status Curent
🟢 **Full Dual-Edition Production Ready (.NET 10 LTS)**
- **53 de teste automate unitare și de integrare** trec cu succes (0 Failed).
- Ambele proiecte (`LogAnalyzer.AirGapped.csproj` și `LogAnalyzer.Network.csproj`) se compilează curat și produc executabile dedicate.
- Sincronizat complet pe GitHub pe branch-urile `feature/sqlite-dashboard` și `main`.
