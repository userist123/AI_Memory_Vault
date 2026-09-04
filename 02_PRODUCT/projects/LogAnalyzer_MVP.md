---
id: "5d7b785f-221c-48ba-9e7e-557998c9fefa"
type: project
lifecycle: ACTIVE
category: soc-tooling
tags: [project, dotnet, wpf, dfir, threat-hunting, active, air-gapped, network-edition, real-time-monitoring, memory-vault]
created: 2026-08-09
updated: 2026-09-02
provenance:
  source_type: execution
  source_ref: "projects/loganalyzer-dfir"
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
2. **`LogAnalyzer.Network` (Ediție Conectată / SOC & Real-Time EDR):** Destinată stațiilor conectate la rețea/internet.

## Locație Canonică în Vault
- Cod Sursă: `projects/loganalyzer-dfir/`
- Soluție .NET 10 LTS: `projects/loganalyzer-dfir/LogAnalyzer.slnx`

## Status Curent
🟢 **Full Dual-Edition Production Ready (.NET 10 LTS)**
- **69 de teste automate unitare și de integrare** trec cu succes (0 Failed).
- Ambele proiecte (`LogAnalyzer.AirGapped.csproj` și `LogAnalyzer.Network.csproj`) se compilează curat și produc executabile dedicate.
- Integrat complet în depozitul local Vault sub `projects/loganalyzer-dfir/`.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
