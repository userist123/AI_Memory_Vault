# Harta de comunicare între directoare

> Cine importă pe cine, măsurat din cod, nu declarat din intenție.
> Generat cu graful de importuri peste toate fișierele `.py` urmărite de git,
> la 2026-09-17, pe `19.755` fișiere urmărite.
> Când această hartă și un document de arhitectură se contrazic, câștigă harta,
> pentru că ea descrie ce face codul astăzi.

## 1. Regula de bază

`03_IMPLEMENTATION/` este singura sursă din care importă toată lumea. Nimic din
`03_IMPLEMENTATION/` nu importă înapoi din testele, evaluările sau scripturile
care îl consumă. Această asimetrie este invariantul structural al depozitului:
implementarea nu știe cine o folosește.

```
                          ┌──────────────────────┐
     20_TESTS ───515────► │                      │
  07_EVALUATION ──145───► │                      │
    02_PRODUCT ──131────► │  03_IMPLEMENTATION   │
       .agents ──118────► │  (pachete + produse) │
40_EXPERIMENTS ───18────► │                      │
    30_SCRIPTS ───17────► │   nu importă înapoi  │
       scripts ────9────► │   din niciunul       │
10_DOCUMENTATION ──7────► │                      │
    80_ARCHIVE ────4────► └──────────────────────┘
```

Cifrele sunt numărul de instrucțiuni `import` care traversează granița.

## 2. Celelalte muchii măsurate

| Din | Către | Apeluri | Ce înseamnă |
|---|---|---:|---|
| `20_TESTS` | `30_SCRIPTS` | 18 | testele de regresie execută scripturile de verificare (paznicii de igienă, garda de date personale) |
| `20_TESTS` | `scripts` | 3 | idem, pentru scripturile rămase în rădăcină |
| `20_TESTS` | `02_PRODUCT` | 2 | teste peste codul de produs |
| `02_PRODUCT` | `.agents` | 74 | produsele folosesc utilitare din pachetele de skill-uri |
| `07_EVALUATION` | `.agents` | 32 | idem, în harnașamentele de evaluare |
| `cognitive_core` | `03_IMPLEMENTATION` | 1 | shim de compatibilitate: `cognitive_core/` conține un singur fișier, `recall_cli.py`, care redirecționează către pachetul canonic |

O parte din muchiile care ating `.agents` sunt artefacte ale metodei: graful
rezolvă importurile după numele modulului, iar pachetele de skill-uri
vendorizate folosesc nume generice (`tools`, `scripts`, `core`). Muchiile către
`03_IMPLEMENTATION` nu au această problemă, fiindcă numele pachetelor sunt
distincte.

## 3. Direcții interzise

Trei muchii nu trebuie să apară niciodată. Dacă un viitor graf le arată, ceva
s-a rupt:

1. **`03_IMPLEMENTATION` → `20_TESTS` / `07_EVALUATION` / `40_EXPERIMENTS`.**
   Implementarea care importă din propriile teste sau experimente înseamnă că un
   harnașament a devenit dependență de producție. Astăzi: zero astfel de muchii.
2. **`03_IMPLEMENTATION` → `06_INBOX`.** `06_INBOX/` este material extern
   neverificat. Codul de producție nu citește direct din el; ingestia trece prin
   `30_SCRIPTS/skills/skill_ingestion.py`, cu hash și proveniență.
3. **Orice → `80_ARCHIVE`.** Arhiva este terminus. Ea poate importa din
   implementare (4 apeluri, cod istoric păstrat rulabil), dar nimic viu nu
   trebuie să depindă de ea.

## 4. Directoare care nu comunică prin cod

Nu tot ce comunică o face prin `import`. Aceste directoare sunt legate prin
convenție și prin scripturi, iar legătura lor se verifică altfel:

| Director | Cu cine vorbește | Prin ce |
|---|---|---|
| `00_GOVERNANCE/` | toți agenții | `coordination/` — fiecare agent își revendică sarcinile aici înainte să atingă un fișier |
| `01_ARCHITECTURE/ontology/` | pipeline-ul de ingestie | fișierele de slot `slots/*.md` sunt scrise doar de `30_SCRIPTS/ingestion/merge_candidate_concepts.py` |
| `01_ARCHITECTURE/memory/` | `03_IMPLEMENTATION/packages/memory` | note de memorie citite prin `MemoryController.search()`, nu prin scanare de fișiere |
| `04_CONFIG/` | rulările | configurație citită la execuție |
| `05_DATA/`, `50_ARTIFACTS/` | evaluări | date de intrare și artefacte produse |
| `08_OBSERVABILITY/` | tot ce rulează | telemetrie scrisă, niciodată citită de producție |
| `09_SECURITY/` | CI | liste de excepții citite de paznicii din `30_SCRIPTS/verification/` |
| `.github/workflows/` | `20_TESTS`, `30_SCRIPTS` | poartă: ce nu trece aici nu intră pe `main` |

## 5. Ce s-a eliminat pe 2026-09-17

- **`AI_Memory_Vault_OBSIDIAN/`** — stratul vechi de navigare, cu layout-ul
  `00_CORE` / `99_SYSTEM`. Era o clonă separată a aceluiași depozit, 2.304
  fișiere, 49 MB, neurmărită de git. Din 1.010 fișiere de conținut, 994 aveau
  un fișier cu același nume în structura canonică, iar diferența era secțiunea
  de backlink-uri generată automat. Cele 5 documente fără corespondent sunt
  salvate în `80_ARCHIVE/legacy_obsidian_vault/`, împreună cu patch-urile pentru
  stash și pentru modificările necomise.
- **`.claude/worktrees/`** — două worktree-uri moarte, 1,4 GB, al căror `gitdir`
  nu mai există. Din ~19.200 de fișiere fiecare, 10 și respectiv 4 nu se
  regăseau în arborele canonic; cele utile sunt în
  `80_ARCHIVE/rescued_from_stale_worktrees/`.
- **`03_IMPLEMENTATION/products/xau_kinetic/migration_logs/`** — oglindă exactă,
  28 din 28 de fișiere, a lui `99_META/migration_logs/`. Jurnalele de migrare
  ale depozitului nu aparțin în interiorul unui produs. Nicio referință în cod
  nu o folosea.

## 6. Duplicare rămasă, și de ce nu a fost ștearsă

Din 19.755 de fișiere urmărite, 1.245 de grupuri au conținut identic — 2.011
copii în plus, 143 MB. Repartiția contează mai mult decât totalul:

| Zonă | Copii în plus | MB | Decizie |
|---|---:|---:|---|
| `.agents/skills/` (pachete vendorizate) | 1.189 | 137,3 | **Nu se atinge.** `docx`/`docx-official`, `pptx`/`pptx-official` și oglinda `skillsweb/` sunt pachete terțe livrate întregi; ștergerea unei copii rupe skill-ul care o conține. |
| material importat și arhivă | 469 | 2,9 | **Nu se atinge.** `06_INBOX/` și `02_PRODUCT/projects/imported/` păstrează materialul așa cum a venit; proveniența e mai valoroasă decât spațiul. |
| workspace-uri de produs | 164 | 2,1 | Decizia proprietarului de produs. |
| artefacte proprii ale vault-ului | 110 | 0,7 | Partea curățabilă. 28 rezolvate azi. |

Rămâne un caz deschis, care nu e al meu de decis:
`03_IMPLEMENTATION/products/xau_kinetic/standalone/` (49 de fișiere) pare o a
doua copie a lui `engine/` (37) plus `desktop/` (11), cu aproximativ 30 de
fișiere identice octet cu octet și restul divergent. Divergența înseamnă ori că
una dintre copii a primit corecturi pe care cealaltă nu le are, ori că
distribuția împachetată e intenționat diferită. Până se știe care, ștergerea ar
putea pierde o corectură.

## 7. Cum se regenerează harta

Graful de importuri se reconstruiește cu scriptul de analiză peste `git ls-files`;
metoda este descrisă în secțiunea 2 (rezolvare după numele modulului de nivel
înalt, cu pachetele identificate prin `__init__.py`). Numerele din acest
document trebuie recalculate, nu copiate, la orice reorganizare de directoare.
