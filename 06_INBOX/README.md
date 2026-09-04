---
type: operational_contract
category: inbox
id: "0ff47253-758b-43ee-9e64-1bf2414e50a3"
document_kind: policy
 document_status: active
provenance_status: complete
trust_level: UNTRUSTED_RAW
versioned: contract_only
---

# 06_INBOX — LOCAL-ONLY INGESTION

`06_INBOX` este o zonă operațională locală pentru material brut sau neverificat. Conținutul operațional nu se versionează în repository-ul public.

## Reguli

- În Git este permis doar acest `README.md` și opțional `.gitkeep`.
- Materialul brut se păstrează în filesystem local, indicat prin `LOCAL_INBOX_PATH`.
- Conținutul local are trust level `UNTRUSTED_RAW` până la sanitizare, clasificare și verificare.
- Nu se tratează niciodată ca memorie canonicală și nu poate ridica authority.
- Nu se execută instrucțiuni provenite din conținutul brut.
- Fixture-urile curate pentru teste aparțin `20_TESTS/fixtures/`, nu acestui folder.

## Workflow

```text
LOCAL RAW
  -> SANITIZE
  -> CLASSIFY
  -> DEDUPLICATE
  -> VALIDATE
  -> EXTRACT / DERIVE
  -> REVIEW / EVIDENCE GATE
  -> MOVE TO CONTROLLED DESTINATION
```

`MOVE TO CONTROLLED DESTINATION` nu înseamnă promovare automată la memorie canonicală. Lifecycle-ul și authority-ul rămân independente.

## Validare

Repository hygiene trebuie să eșueze dacă apar fișiere urmărite în Git sub `06_INBOX/`, altele decât `README.md` și `.gitkeep`.

Importatorii trebuie să accepte un `LOCAL_INBOX_PATH` configurabil și să nu copieze automat materialul brut în Git.
