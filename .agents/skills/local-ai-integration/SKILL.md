---
name: local-ai-integration
description: Încarcă acest skill când integrezi modele AI locale (Ollama — DeepSeek-R1, LLaMA, Gemma) în aplicații: analiză de loguri, jurnal de trading, clasificare, agenți. Impune output structurat validat, fallback-uri și așteptări realiste de la modele mici.
---

# Local AI Integration

Modelele locale sunt gratuite și private (critic în mediul tău), dar mici și inconsecvente. Arhitectura compensează ce lipsește modelului.

## Principiu de bază: AI-ul propune, codul dispune

- Output-ul modelului e INPUT nevalidat, ca orice input de user: parsează, validează cu schemă, respinge și re-cere la eșec.
- Folosește formatul JSON forțat (`format: json` în Ollama / structured outputs) + validare Pydantic/Zod. Retry cu mesaj de eroare inclus, max 2-3 încercări, apoi fallback determinist.
- Nicio acțiune ireversibilă (ordin de tranzacționare, ștergere, trimitere) declanșată direct de output AI fără regulă de cod sau confirmare umană deasupra.

## Alegerea modelului per sarcină (nu un model pentru tot)

| Sarcină | Model potrivit | De ce |
|---|---|---|
| Clasificare/etichetare (loguri, tranzacții) | Model mic 3-8B (LLaMA 3.2, Gemma) | Rapid, suficient pentru taxonomii fixe |
| Raționament/analiză (de ce a eșuat X) | DeepSeek-R1 distilat | Chain-of-thought nativ; acceptă latența |
| Sumarizare (sesiuni, rapoarte) | 7-8B cu context mare | Echilibru viteză/calitate |
- Măsoară pe sarcina TA: un set de 20-30 cazuri de test cu răspuns așteptat, rulat la fiecare schimbare de model/prompt. Fără benchmark propriu, alegerea modelului e superstiție.

## Prompting pentru modele mici (diferit de modelele mari)

- Instrucțiuni scurte și imperative; modelele mici se pierd în prompturi de 2 pagini.
- Few-shot cu 2-3 exemple EXACTE de input→output bate orice descriere abstractă.
- Un prompt = o sarcină. Lanț de prompturi simple > un prompt complex.
- Temperatura 0-0.2 pentru extragere/clasificare; nu lăsa default.

## Operare Ollama

- Verifică disponibilitatea serviciului la start (`/api/tags`) + tratare explicită „model neîncărcat" (primul call e lent — cold load).
- Timeout-uri setate (modelele locale pot îngheța); streaming pentru UX la răspunsuri lungi.
- `keep_alive` configurat conform folosirii: permanent pentru servicii, scurt pentru unelte ocazionale (eliberezi VRAM).
- Loghează fiecare call: model, prompt hash, latență, tokens — altfel nu poți depana degradările.

## Date sensibile

- Localul există ca datele să NU plece: nu amesteca fallback pe API-uri cloud pentru date de serviciu/clasificate. Fallback-ul cloud e permis doar pentru date personale neclasificate, explicit.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
