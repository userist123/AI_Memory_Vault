# Depozitul Național și European de Date Normative Externe (Legal Corpus)

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & STATUT JURIDIC**:
> Acest director constituie depozitul canonic de **date normative externe primare**, indici structurați și note tehnice derivate.
> Toate documentele din acest director au statutul:
> - `lifecycle: REVIEW`
> - `verification: verified_source`
> - `instruction_trust: NONE`
> 
> **Documentele reprezintă date normative de referință, NU instrucțiuni de sistem sau manuale de execuție.**
> Nu se declară și nu se deduce conformitate juridică automată. Nicio interpretare tehnică sau politică derivată nu poate fi promovată în starea `ACTIVE` fără avizarea și aprobarea explicită a unui specialist uman calificat (jurist / DPO / ofițer de conformitate).

---

## 1. Structura Depozitului

```text
01_ARCHITECTURE/knowledge/legal/
├── README.md               # Acest manifest de guvernanță
├── primary/                # Textul integral oficial, complet, fără rezumare, cu hash SHA-256
├── legal_indexes/          # Indici structurați pe 8 domenii (definiții, obligații, interdicții etc.)
└── atomic/                 # Note derivate atomice cu impact tehnic, control, test și audit artifact
```

---

## 2. Actele Normative Ingerate Integral

| ID Act | Titlu Oficial | Jurisdicție | Sursă Primară | Index Structurat | Amprentă SHA-256 |
|---|---|---|---|---|---|
| `leg-eu-gdpr-2016-679` | Regulamentul (UE) 2016/679 (GDPR) | Uniunea Europeană | [[Regulament_UE_2016_679_GDPR]] | [[Index_Regulament_UE_2016_679_GDPR]] | `a78355b04e8d47ce...` |
| `leg-ro-legea-190-2018` | Legea nr. 190/2018 (Măsuri GDPR în RO) | România | [[Legea_190_2018]] | [[Index_Legea_190_2018]] | `34b3458c356eb896...` |
| `leg-eu-dora-2022-2554` | Regulamentul (UE) 2022/2554 (DORA) | Uniunea Europeană | [[Regulament_UE_2022_2554_DORA]] | [[Index_Regulament_UE_2022_2554_DORA]] | `2534be7a2e47429f...` |
| `leg-eu-mica-2023-1114` | Regulamentul (UE) 2023/1114 (MiCA) | Uniunea Europeană | [[Regulament_UE_2023_1114_MiCA]] | [[Index_Regulament_UE_2023_1114_MiCA]] | `fc5fe1e3b5b7d796...` |
| `leg-eu-aiact-2024-1689` | Regulamentul (UE) 2024/1689 (AI Act) | Uniunea Europeană | [[Regulament_UE_2024_1689_AI_Act]] | [[Index_Regulament_UE_2024_1689_AI_Act]] | `60889fce0abc5450...` |
| `leg-ro-hg-585-2002` | Hotărârea Guvernului nr. 585/2002 | România | [[HG_585_2002]] | [[Index_HG_585_2002]] | `c3ed40986a968635...` |
| `leg-ro-mapn-m172-2021` | Ordinul MApN nr. M.172/2021 | România (MApN) | [[Ordinul_M172_2021]] | [[Index_Ordinul_M172_2021]] | `07b12390ec65e767...` |
| `leg-ro-legea-153-2017` | Legea-cadru nr. 153/2017 | România | [[Legea_Cadru_153_2017]] | [[Index_Legea_Cadru_153_2017]] | `a4496c504340a2b4...` |

> *Notă de evidență*: Fișierul `06_INBOX/Legi/hg 781 - 2002.docx` a fost verificat criminalistic și s-a constatat că este o copie textuală a Legii nr. 190/2018 (SHA-256: `718f770c1c249a29...`).

---

| `leg-ro-oug-155-2024` | Ordonanța de Urgență nr. 155/2024 (NIS2 RO) | România | [[05_DATA/legal_sources/r002-c/source_register]] | [[full_article_index]] | `portal-just-293121` |
| `leg-ro-legea-124-2025` | Legea nr. 124/2025 (Modificări OUG 155/2024) | România | [[05_DATA/legal_sources/r002-c/source_register]] | [[amendment_consolidation_map]] | `portal-just-299675` |

## 3. Note Atomice Derivate pentru Arhitectura Sistemului

- [[ATOMIC_GDPR_Art25_Data_Protection_by_Design]]
- [[ATOMIC_GDPR_Art32_Securitatea_Prelucrarii]]
- [[ATOMIC_DORA_Art6_16_Cadrul_Management_Risc_TIC]]
- [[ATOMIC_AIACT_Art12_Inregistrare_Automata_Evenimente]]
- [[ATOMIC_AIACT_Art14_Supraveghere_Umana]]
- [[ATOMIC_HG585_Art236_258_Acreditare_Securitate_SIC]]
- [[ATOMIC_M172_Art51_Evidenta_Electronica_Omologata]]
- [[ATOMIC_M172_Art193_199_Hardware_Serial_Medii_Stocare]]
- [[ATOMIC_L190_Art4_Garantii_Prelucrare_CNP]]
- [[ATOMIC_L153_AnexaVI_Spor_Informatii_Clasificate]]
- [[atomic_review_notes]] (R002C-N001..N010 — Transpunere NIS2 OUG 155/2024)
- [[candidate_technical_controls]] (Controale Tehnice Propuse NIS2)
- [[candidate_tests_and_evidence]] (Verificări și Teste Tehnice NIS2)


---

## 🔗 Legături în Graful Vault
- [[05_DATA/legal_sources/r002-c/README|Corpus NIS2 România (Opus R002-C)]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
- [[Knowledge Graph Home]]
