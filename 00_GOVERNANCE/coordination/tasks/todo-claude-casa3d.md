# todo-claude-casa3d
STATUS: IN_PROGRESS        UPDATED: 2026-10-21T23:40:00+03:00
TASK: Casa3D — continuare din todo-antigravity-casa3d.md (NEXT: IBAN fixtures, fuziune PR, pini 3D pe /share)
BRANCH / PR: gemini/casa3d-nextgen-functionality-uiux / PR #304
DONE:
- Deblocat `Structural and path hygiene`: fixtures cu IBAN RO valid mod-97 eliminate din sursă.
  - `tests/commercial.test.ts`, `tests/contract.test.ts`: IBAN exemplu public DE89… (garda vizează doar RO).
  - `tests/studio-profile.test.ts`: IBAN RO construit la rulare (`roIban`), aceleași aserțiuni (valid, lowercase, grupare, o cifră greșită respinsă).
  - Garda nu a fost slăbită, allowlist neatins.
- Dovezi: `python 30_SCRIPTS/verification/personal_data_guard.py` -> PASS exit 0; vitest 107/107 fișiere, 634/634 teste; `tsc --noEmit` exit 0.
- `npm ci` rulat în `casa3d/app` (node_modules lipsea în acest checkout).
NEXT (in order):
- Verificare CI pe PR #304 după push.
- Fuziune PR #305 în main + consolidare #304/#257/#258 — necesită decizia owner-ului (merge = acțiune de autoritate).
- Pini/comentarii 3D pentru beneficiar pe `/share/[token]`.
BLOCKERS: none
NOTES:
- Fișierele necomise ale Antigravity (CASA3D_*_AUDIT/INVENTORY/STATUS/EVIDENCE*.md, edit în todo-antigravity-casa3d.md) au fost lăsate neatinse.
KEY FILES:
- 02_PRODUCT/projects/workspaces/casa3d/app/tests/{studio-profile,commercial,contract}.test.ts
- 30_SCRIPTS/verification/personal_data_guard.py
