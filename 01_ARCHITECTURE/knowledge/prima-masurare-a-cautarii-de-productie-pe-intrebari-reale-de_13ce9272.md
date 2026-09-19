---
type: knowledge
category: prima-masurare-a-cautarii-de-productie-pe-intrebari-reale-de
tags:
- candidate
- mcp-proposal
created: '2026-09-19'
updated: '2026-09-19'
provenance:
  source_type: ai
  source_ref: 07_EVALUATION/memory_usage/WORKLIST_RELEVANCE.md
confidence: low
verification: unverified
relations: []
lifecycle: REVIEW
id: 13ce9272-3ee4-4b34-9e49-cbdfb26c5ce4
---
# Prima măsurare a căutării de producție pe întrebări reale de lucru

Am pus 20 de întrebări de lucru, luate din sarcinile deschise din `00_GOVERNANCE/coordination/`, prin serverul MCP `vault-memory` (`memory_search`, setările implicite de producție). Rezultatele complete sunt în `07_EVALUATION/memory_usage/` (raport generat: `WORKLIST_RELEVANCE.md`).

- Din primele trei rezultate ale fiecărei întrebări, 5 din 57 au fost relevante pentru sarcină (judecată manuală, un singur evaluator); 3 din 20 întrebări au avut cel puțin un rezultat relevant; 1 din 20 au întors zero rezultate.
- Fișierele de coordonare din care vin sarcinile nu sunt note indexate: 0 din 8 pot fi întoarse de memorie. Sarcinile deschise sunt deci invizibile pentru căutare.
- Cele 95 rezultate au avut ciclul de viață: ACTIVE 6, ARCHIVED 16, NORMALIZED 1, None 2, REVIEW 67, raw 3; 89 din 95 nu sunt ACTIVE sau VERIFIED.
- Note juridice mari (GDPR, DORA, MiCA, AI Act) apar în primele rezultate pentru întrebări formulate cu cuvinte uzuale românești, fără legătură cu subiectul.
- Notele cu `lifecycle: raw` scris cu litere mici sau fără `lifecycle` trec de excluderea RAW, care compară șirul exact `RAW`.
- `memory_get` pe o notă foarte mare întoarce conținut comprimat (`[PARTIAL]`), din cauza bugetului de context.

Nu s-a schimbat nimic în căutare: aceasta e doar măsurătoarea. Decizia despre ce se corectează e a proprietarului.
