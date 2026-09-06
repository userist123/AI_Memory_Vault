# R002-C — NIS2 Romania Legal Knowledge Ingestion

Working branch: `r002-c/nis2-romania-legal-ingestion-20260906`

Base main SHA: `b42dd9a97d4620849426916aed11df478b3076d0`

## Source boundary

Only these two acts are primary legal sources for R002-C:

1. OUG nr. 155/2024.
2. Legea nr. 124/2025.

Official Portal Legislativ references:
- OUG: https://legislatie.just.ro/Public/DetaliiDocument/293121
- Legea: https://legislatie.just.ro/Public/DetaliiDocumentAfis/299675

The corpus deliberately does not ingest later amending legislation as a primary source. Later amendments are recorded only as a version-gap warning where they affect interpretation of the two requested acts.

## Artifact separation

- `primary/` = source snapshots only.
- `indexes/` = structural mappings/indexes.
- `derived/notes/` = atomic legal knowledge, always REVIEW.
- `derived/controls/` = candidate technical controls, not compliance declarations.
- `derived/tests/` = candidate evidence/tests, not proof of legal compliance.
- `review/` = legal-review queue and unresolved applicability questions.
- `R002-C_HANDOFF.md` = final handoff and status.

## Trust rules

Every primary source is marked:

```yaml
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
requires_legal_review: true
```

Derived interpretations never become ACTIVE automatically. No artifact in this directory is a legal compliance declaration or legal advice.

---

## 🔗 Legături Sinaptice & Navigare Graf

### R002-C Corpus Navigation
- [[R002-C_HANDOFF]] — Status și evidență predare R002-C
- [[source_register]] — Registrul surselor oficiale OUG 155/2024 și Legea 124/2025
- [[atomic_review_notes]] — Note atomice de revizuire juridică (R002C-N001..N010)
- [[candidate_technical_controls]] — Controale tehnice propuse (AI Memory Vault, LogAnalyzer, Trading)
- [[candidate_tests_and_evidence]] — Evidențe și teste de verificare tehnică
- [[full_article_index]] — Index integral articole OUG 155/2024
- [[amendment_consolidation_map]] — Harta modificărilor Legea 124/2025 vs OUG 155/2024
- [[legal_review_required]] — Registru articole supuse avizării juridice umane
- [[not_applicable_or_not_yet_determined]] — Regimuri exceptate și neaplicabile

### Sinapse Juridice Transversale (EU & RO Normative Bridge)
- [[01_ARCHITECTURE/knowledge/legal/README|Depozitul Național și European de Date Normative Externe]]
- [[Regulament_UE_2022_2554_DORA]] & [[ATOMIC_DORA_Art6_16_Cadrul_Management_Risc_TIC]] — Art. 2(3) OUG 155/2024 (Exceptare/coordonare entități financiare DORA)
- [[Regulament_UE_2016_679_GDPR]] & [[ATOMIC_GDPR_Art32_Securitatea_Prelucrarii]] — Notificare incidente și protecția datelor cu caracter personal
- [[Regulament_UE_2024_1689_AI_Act]] & [[ATOMIC_AIACT_Art12_Inregistrare_Automata_Evenimente]] — Cerințe de securitate cibernetică pentru sisteme AI de mare risc
- [[HG_585_2002]] & [[ATOMIC_HG585_Art236_258_Acreditare_Securitate_SIC]] — Art. 2(4) OUG 155/2024 (Sisteme de procesare a informațiilor clasificate)
- [[Ordinul_M172_2021]] & [[ATOMIC_M172_Art193_199_Hardware_Serial_Medii_Stocare]] — Norme tehnice MApN pentru medii de stocare și sisteme naționale

### MOC Hubs
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
- [[Knowledge Graph Home]]
