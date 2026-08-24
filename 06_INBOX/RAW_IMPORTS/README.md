---
id: "eeda435b-485b-4ea6-8431-124133540fa1"
type: system
category: inbox
status: active
version: 1.0.0
confidence: high
verification: not_applicable
provenance_status: not_applicable
---

# RAW_IMPORTS

Acest folder pastreaza permanent sursele brute importate din conversatii AI sau alte surse externe, inainte de clasificare.

Conform [[AGENTS.md]] sectiunea 8:

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` ramane permanent aici; nu este niciodata memorie canonica si nu este indexat ca knowledge canonic.

## Continut asteptat

- Export-uri brute de conversatii (ex. Perplexity, ChatGPT, Codex) inainte de procesare.
- Fisiere denumite cu data si sursa, ex. `2026-06-13_perplexity_elite_quant_bot.md`.

## Fisiere derivate din acest folder (deja procesate in 04_MEMORY)

- `04_MEMORY/Preferences/Trading_Bot_Prompt_Language_English.md` — sursa: conversatie 2026-06-13
- `04_MEMORY/Preferences/Multi_File_Project_Structure.md` — sursa: conversatii 2026-06-10/13
- `04_MEMORY/Decisions/MT5_Python_Tkinter_Stack_For_Trading_App.md` — sursa: conversatie 2026-06-10
- `04_MEMORY/Errors/Backtest_Single_Entry_Logic_Flaw.md` — sursa: conversatie 2026-06-10
- `04_MEMORY/Lessons/Define_MultiEntry_Requirements_Before_Backtest.md` — derivat din eroarea de mai sus
- `04_MEMORY/Experiences/AI_Trading_Journal_Zero_Dollar_Stack.md` — sursa: conversatie 2026-04-17
- `04_MEMORY/Lessons/Modularize_Prompts_For_Token_Limited_Models.md` — derivat din experienta de mai sus

Aceste note au fost extrase manual din memoria conversatiilor si marcate `verification: unverified` pana la confirmare explicita de catre utilizator.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
