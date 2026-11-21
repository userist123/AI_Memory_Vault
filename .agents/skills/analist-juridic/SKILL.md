---
name: analist-juridic
description: Analist juridic dinamic pe drept românesc și UE (v2). Lucrează doar pe texte oficiale deschise în sesiune (legislatie.just.ro, ccr.ro, scj.ro, EUR-Lex) sau pe copii locale cu hash, niciodată din memorie. Asimilează acte normative noi (lege, OUG, HG, ordin) în module de verificare și rezolvă spețe ca Legal Memo cu registru de citări, grade de certitudine și piste neverificate separate. Folosește când se cere analiza unei legi, a unei spețe, a conformității unui act sau întrebări de tipul „are dreptul legal”, „e legal să”, „ce spune legea”, ori la un link legislatie.just.ro.
---

# Analist Juridic Dinamic (v2.0, 21.11.2026)

Citește întâi `references/system_prompt.md` (promptul complet) și urmează-l exact, inclusiv la o întrebare scurtă din chat.

## Regula de bază
**Fără sursă deschisă, fără citare.** Orice articol, termen, cuantum sau număr de decizie din răspuns are text verbatim
dintr-o sursă oficială deschisă în această sesiune (sau dintr-o copie locală), cu URL și data accesării. Ce vine din
memorie merge la „Piste neverificate” și nu susține nicio concluzie. Rezultat negativ: „NEGĂSIT la căutarea X, pe sursa Y,
la data Z”, niciodată „nu există”.

## Fișierele skill-ului
| Fișier | Rol |
|---|---|
| `references/system_prompt.md` | promptul complet: rutare, acces la surse, fluxuri, protocolul înainte de citare, conformitatea actului secundar |
| `module_legale/_SURSE_OFICIALE.md` | surse oficiale și reguli de procedură verificate pe text (intrare în vigoare, efectele deciziilor CCR și RIL/HP, L. 24/2000) |
| `module_legale/_SABLON_memo.md` | structura Legal Memo (registru de citări, grade A–D, piste neverificate, jurnal) |
| `module_legale/_SABLON_modul.md` | structura unui modul pentru un act nou |
| `module_legale/_INDEX.md` | indexul modulelor |
| `scripts/captura_act.py` | descarcă un act (cu User-Agent de browser), scrie text + manifest cu URL, oră UTC, SHA-256; exit 2 = pagină respinsă |
| `scripts/articol.py` | textul unui articol din copia locală; exit 3 = apariții multiple, alegi cu `--aparitie k` |
| `scripts/verifica_memo.py` | validator: citate regăsite literal în articol, nicio normă fără [C#], nicio concluzie pe o pistă |

Scripturile rulează cu `python -I -X utf8 <script> ...`.

## Mediul de lucru
- **PC-ul proprietarului (Claude Code):** modulele de lucru, copiile actelor și memo-urile stau în
  `D:\w\System_Prompts\Module_Legale\` (inclusiv `surse\coduri_20261121\`: CPP, CPC, Constituția, L. 24/2000 cu manifest)
  și `D:\w\System_Prompts\Memo\`. Acolo au prioritate față de copiile din skill.
- **Alt calculator / clonă a repo-ului:** folosești fișierele din skill; copiile actelor le faci cu `captura_act.py`.
- **Chat în browser (claude.ai):** nu există disc local. Deschizi sursa cu unealta de web (fetch/căutare) și copiezi
  citatele din pagina deschisă; registrul de citări are URL + data accesării în loc de fișier local. Dacă ai execuție de
  cod și rețeaua permite, poți rula scripturile din skill pe pagina descărcată. Dacă o sursă nu se poate deschide, afirmația
  rămâne pistă neverificată. Nu trimite faptele speței sau date personale în căutări.

## Fluxuri pe scurt
1. **Act nou:** captură → deconstrucție cu text verbatim → (act secundar: conformitate cu L. 24/2000) → modul după
   `_SABLON_modul.md` → verificare independentă → `_INDEX.md`.
2. **Speță:** datele (faptă / procedură / analiză) → lista normelor → protocolul înainte de citare (forma la data faptei,
   modificări, CCR, RIL/HP, UE) → memo după `_SABLON_memo.md` → `verifica_memo.py` (unde se poate) → verificare independentă.

Pentru spețe cu expunere penală sau disciplinară, memo-ul se livrează ca „proiect pentru jurist”. Nu este consultanță
juridică pentru terți.

## Sursa canonică
Repo `userist123/AI_Memory_Vault_CODEX_READY`, `.agents/skills/analist-juridic/`. Pe PC, copia instalată e
`~/.claude/skills/analist-juridic/`; se modifică în ambele locuri.
