---
id: "c1a01101-7291-49fa-9481-22904c10b002"
type: knowledge
lifecycle: REVIEW
category: ai-integration
tags:
  - ollama
  - local-ai
  - structured-outputs
  - privacy
  - prompt-engineering
created: 2026-08-17T23:00:00Z
updated: 2026-08-17T23:00:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/coding/local-ai-integration/SKILL.md"
confidence: very_high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:00:00Z
relations:
  - target: "[[Confidence_Model]]"
    type: supports
  - target: "[[Rules]]"
    type: implements
---

# Integrarea Modelelor AI Locale (Ollama & Securitate la Nivel de Client)

## TL;DR
Modelele locale (Ollama: Qwen, DeepSeek, LLaMA, Gemma) garantează confidențialitatea datelor (critic în mediul INFOSEC), dar au varianță și limitări de capacitate. Principiul de fier este: „AI-ul propune, codul dispune” — output-ul AI este tratat ca input nevalidat de utilizator, obligând validarea strictă pe schemă (Pydantic/Zod) și zero acțiuni ireversibile fără confirmare umană.

## Key Facts
- **Validare & Output Structurat**: Formatare forțată `format: "json"` în apelurile Ollama + validare automată a schemelor; retry determinist (max 2–3 încercări cu mesajul de eroare atașat).
- **Matricea de Selecție a Modelelor per Sarcină**:
  - *Clasificare & Etichetare rapidă:* Modele mici 3–8B (LLaMA 3.2, Qwen 2.5 3B/7B).
  - *Raționament profund & Audit:* DeepSeek-R1 / Qwen Coder 32B pe noduri cloud/GPU.
  - *Sumarizare & Raportare:* Modele 7–8B cu ferestre mari de context.
- **Strategii de Prompting pentru Modele Mici**:
  - Instrucțiuni scurte, imperative; un singur obiectiv per apel de sistem.
  - *Few-shot prompting* obligatoriu (2–3 exemple concrete de perechi Input -> Output).
  - Temperatură `0.0–0.2` pentru operațiuni analitice și extracție de metadate.
- **Garanția Confidențialității**: Datele clasificate sau operaționale nu părăsesc niciodată mediul local; fallback-ul către API-uri cloud este strict blocat pentru date de serviciu.

---

## 1. Reguli Operaționale Ollama
- Verificarea stării serviciului la pornire (`/api/tags`) cu gestionarea explicită a latenței de cold-start (încărcarea modelului în VRAM).
- Configurare `keep_alive`: extinsă (`10m`–permanent) pentru fluxuri active, eliberare rapidă de VRAM după finalizarea loturilor.
- Jurnalizare completă a fiecărui apel: model, hash prompt, durată, latență și număr de tokeni generați.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
