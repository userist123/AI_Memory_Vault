---
id: "1902defd-fcf5-4ec2-a8ea-0b4f3999a273"
title: "F4 - prefixe duplicate și dependențe Python: decizie pe cifre (WP-15)"
type: "decision"
lifecycle: "REVIEW"
created: "2026-11-10T08:00:00Z"
updated: "2026-11-10T08:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "20_TESTS/test_agent_docs_memory_interfaces.py"
  created_by: "claude-code"
tags:
  - "modernization"
  - "governance"
  - "decision"
---

# F4 — prefixe duplicate și dependențe Python (WP-15)

Cifrele au fost numărate pe ramura `modernize/f4-wp15-prefixes` cu `grep -rl` peste `.py`, `.yml`, `.md`, `.toml` și `.json`, fără `.git`. Coloana „referințe în cod” numără doar `.py`, `.yml` și `.toml`.

## 1. Prefixele duplicate

| Director | Fișiere | Fișiere care îl referă | din care în cod | Folosit la runtime |
|---|---:|---:|---:|---|
| `01_ARCHITECTURE` | 1116 | 766 | 70 | da (cunoaștere durabilă) |
| `01_KNOWLEDGE` | 2 | 181 | 35 | **da**: `memory/storage/path_resolver.py:80` (`"knowledge": "01_KNOWLEDGE"`), `memory/storage/file_engine.py`, `memory/controller.py` |
| `08_RESEARCH` | 97 | 74 | 27 | da (CI) |
| `08_OBSERVABILITY` | 32 | 29 | 14 | da (rapoarte și audit) |

**Decizie: nu se redenumește nimic.**

- `01_KNOWLEDGE` nu e un duplicat de documentație. E o rădăcină de date a motorului de stocare: `path_resolver` mapează categoria `knowledge` pe ea. O redenumire ar însemna o migrare de date și o schimbare a contractului de stocare, nu o curățenie de nume. Costul înseamnă 35 de fișiere de cod plus notele scrise de motor. Câștigul e doar estetic.
- `08_RESEARCH` și `08_OBSERVABILITY` au roluri distincte, iar prima e folosită de CI. Redenumirea ar atinge 41 de fișiere de cod.
- `01_ARCHITECTURE` rămâne neschimbat, conform corecției 7 din checkpoint (aproximativ 767 de referințe).
- Prefixul numeric indică ordinea de citire, nu unicitatea. Două directoare cu același număr și roluri diferite sunt acceptate. Documentăm asta aici, în loc să mutăm fișiere.

**Variantă respinsă:** unirea `01_KNOWLEDGE` în `01_ARCHITECTURE/knowledge/`. Ar amesteca o rădăcină de date scrisă de runtime cu documentație scrisă de oameni, ar rupe căile din `path_resolver` și ar cere migrarea notelor existente.

## 2. Dependențele Python

- `requirements.txt` și `requirements-memory-v6.txt` sunt identice: 12 linii, `diff` gol.
- `requirements.txt` e instalat de 16 workflow-uri. `requirements-memory-v6.txt` e instalat de 4: `memory-consolidation`, `memory-v6-tests`, `planning-influence-mve` și `r001-enforcement`. Ambele mai sunt citite de `reproducibility_gate.py`, `package_inventory.py`, `build_r001_inventory.py` și de testul `test_the_mcp_dependency_is_pinned_in_both_requirement_files`.

**Decizie:** ambele fișiere rămân. Testul nou `test_the_two_requirement_files_cannot_drift_apart` din `20_TESTS/test_agent_docs_memory_interfaces.py` cere ca ele să conțină aceleași pin-uri. Așa devin, prin test, o singură sursă de adevăr, fără să atingem cele 4 workflow-uri, cele 3 scripturi de verificare și pin-ul `mcp` citit literal.

**Variante respinse:**
- `-r requirements.txt` în fișierul v6: rupe testul existent care citește pin-ul `mcp` literal și hash-urile din `reproducibility_gate`.
- Ștergerea fișierului v6: atinge 4 workflow-uri și 3 scripturi, fără niciun câștig funcțional.
