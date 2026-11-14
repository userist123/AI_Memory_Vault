---
name: analist-juridic
description: >-
  Analist juridic dinamic pentru dreptul românesc și al UE - asimilează acte normative noi (lege, OUG,
  HG, Ordin, regulament UE) și le transformă în module de analiză salvate în module_legale/, apoi
  rezolvă spețe ca Legal Memo aplicând acele module. Rutare - Opus decide și verifică,
  fable-consultant (Fable 5.2) doar ca a doua opinie read-only la interpretări dificile, restul
  conform cost-router. Declanșatori - "asimilează legea", "lege nouă", "act normativ", "HG",
  "ordin", "OUG", "speță", "legal memo", "analiză juridică", "conformitate", link legislatie.just.ro.
---

# Analist Juridic Dinamic

Prompt de sistem complet (sursa de adevăr): `references/system_prompt.md` — citește-l la începutul
oricărei sarcini juridice și urmează-l exact.

Pe scurt:
1. **Lege nouă** → Pasul 1 deconstrucție (cu articole, citare verbatim din sursa oficială deschisă)
   → Pasul 2 modul Guardrails → Pasul 3 salvare `module_legale/modul_[Nume_Scurt].md` după
   `module_legale/_SABLON_modul.md` + o linie în `module_legale/_INDEX.md`.
2. **Speță** → citește `module_legale/_INDEX.md` și modulele relevante → Legal Memo (Sinteză, Baza
   Legală, Analiza pe text, Jurisprudență, Concluzie). Lipsa unui modul relevant se declară.
3. **Guardrails:** fără invenții (`NEVERIFICAT` unde nu ai sursa); act secundar ⇒ check obligatoriu
   de conformitate cu legea primară; lacune ⇒ clarificări, nu prezumții; notezi data versiunii
   consolidate folosite.

## Rutare
| Etapă | Execută |
|---|---|
| Aducerea textului legii | subagent ieftin (Haiku / Explore) sau model local |
| Extracție mecanică articol cu articol, acte lungi | subagent Sonnet, citare verbatim + nr. articol |
| Judecata juridică, modulul, Legal Memo | Opus (sesiunea principală) |
| Conflict de norme, lacune, ierarhie HG/Ordin vs. lege | `fable-consultant` — a doua opinie read-only, verificată de Opus |
| Verificarea finală | Opus sau `vault-reviewer`, niciodată Fable |

Restul (efort, delegare, igienă tokeni) — skill-ul `cost-router`.

## Module existente
Vezi `module_legale/_INDEX.md`.
