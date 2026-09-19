# Benchmark de regăsire v3 — preînregistrare

**Stare: înghețat, nemăsurat.** Niciun braț de căutare nu a fost rulat pe aceste cazuri. Regula de decizie de mai jos e fixată în același commit cu benchmark-ul, deci **înaintea** oricărui rezultat. După acest commit nu se mai schimbă nici cazurile, nici pragurile.

## De ce există

Benchmark-ul reținut v2 are 32 de cazuri, dintre care 29 măsurabile. Pe el, bugetele de expansiune 5, 10 și 20 au câștigat aceleași 2 cazuri față de graful oprit — la limita zgomotului, fără nicio putere de decizie
([`07_EVALUATION/graph_budget/GRAPH_BUDGET_REPORT.md`](../graph_budget/GRAPH_BUDGET_REPORT.md)).
v3 are 160 de cazuri, dintre care 40 construite special ca să ceară o legătură din graf.

## Cine l-a etichetat și cum

- **Etichetator:** ChatGPT, un evaluator independent. Nu a construit sistemul și nu îi ajustează parametrii. Cine alege bugetul nu are voie să scrie testul pe care îl alege.
- **Intrare:** [`labeling_corpus.json`](labeling_corpus.json) — cele 379 de note pe care căutarea le poate returna la commit-ul `b3ada1b54`, cu extrase de ~700 de caractere, plus cele 466 de legături dintre ele. Cele 552 de lecții-șablon arhivate sunt excluse. Construit de [`30_SCRIPTS/evaluation/build_labeling_corpus.py`](../../30_SCRIPTS/evaluation/build_labeling_corpus.py).
- **Instrucțiunile primite:** [`LABELING_PROMPT.md`](LABELING_PROMPT.md), neschimbate.
- **Etichetatorul nu a avut acces la rezultatele căutării.** A etichetat numai din note, ca recall-ul să nu fie umflat de o etichetare orientată spre ce găsește deja sistemul.

## Hash-uri (conținut LF, fixat prin `.gitattributes`)

| Fișier | SHA-256 |
|---|---|
| `retrieval_benchmark_v3.json` | `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa` — identic cu cel raportat de etichetator |
| `labeling_corpus.json` | `5d70bb980f57bc99b6c341909c2cb3ea151fc2308923d20d60eb9f105b03f945` |

Etichetatorul a primit corpusul cu terminații CRLF; conținutul e identic, doar terminațiile de linie au fost normalizate la LF pentru commit.

## Compoziție

| Clasă | Cazuri | Ce testează |
|---|---:|---|
| `direct` | 60 | o notă anume răspunde; 45 dintre ele parafrazate, fără cuvintele distinctive din titlu |
| `multi_hop` | 40 | nota corectă e accesibilă doar urmând o legătură din graf |
| `conceptual` | 30 | înțelegerea conținutului, nu potrivirea unui cuvânt |
| `abstain` | 30 | răspunsul nu există în vault |

Limbi: 86 în engleză, 74 în română.

## Verificarea independentă a etichetării

Refăcută de Claude (Opus), nu de etichetator, pe fișierul primit:

- toate id-urile din `gold_relevant_notes` există în corpus;
- fiecare fragment din `required_facts` apare exact în extrasul unei note corecte;
- fiecare traseu `multi_hop` corespunde unei legături reale din corpus;
- nicio notă corectă nu apare în mai mult de 2 cazuri.

Două observații, **declarate înaintea măsurătorii**:

1. **R3-066, R3-075, R3-082:** relația declarată diferă de prima muchie găsită între cele două note, dar acele perechi au mai multe muchii (`depends_on` și `related_to`), iar relația declarată există. Nu e o eroare.
2. **R3-061, R3-080, R3-088** sunt formulate corect prin nota de legătură, dar nota-țintă are cel puțin trei cuvinte comune cu întrebarea în extras, deci s-ar putea să fie găsită și fără graf. Fișierul nu se modifică. Rezultatul pe clasa `multi_hop` se raportează **și cu, și fără** aceste trei cazuri.

## Metrici

Aceleași definiții ca în v2 ([`run_production_arms.py`](../heldout_retrieval_benchmark_v2/run_production_arms.py)):

- `candidate_recall` — cel puțin o notă corectă se află printre candidați;
- `context_recall` — cel puțin o notă corectă ajunge în pachetul de context final.

**Cazurile `abstain` sunt nemăsurabile pentru recall**, ca în v2: căutarea nu are un mecanism de abținere, deci orice buget mai mare decât zero adaugă note. Pe ele se raportează doar **costul** — câte note noi aduce expansiunea la o întrebare fără răspuns. Nu intră în decizie.

## Brațele

Toate pe indexul complet, adică exact condițiile din producție:

- graful oprit;
- bugetul implicit (`None`);
- bugetul 5, 10 și 20.

Toate cu activarea prin difuzie **oprită**: decizia privește bugetul. Aceleași brațe cu activarea pornită se raportează separat, informativ.

## Regula de decizie — preînregistrată de etichetator, citată exact

Recomandarea etichetatorului pentru valoarea implicită experimentală: **bugetul 5**. Bugetele 10 și 20 nu au adus niciun câștig în plus față de 5 pe datele existente, dar costă de aproximativ 2× și 4× mai mult.

```
Adoptam budget = 5 numai daca, fata de graph_off:
- castiga >= 8 cazuri la context_recall;
- pierde <= 1 caz;
- testul McNemar exact, bilateral, are p < 0.05;
- media nodurilor noi adaugate per interogare <= 4.50.

Altfel, nu schimbam bugetul implicit pe baza acestui benchmark.
```

Pragul de 8 câștiguri și 1 pierdere dă p = 0,0390625 la testul McNemar exact bilateral.

Ce **nu** se poate concluziona, tot după etichetator: că 5 e optim global, că expansiunea prin graf îmbunătățește regăsirea în general, sau că rezultatele vechi pot fi extrapolate la corpusul nou.

**Decizia rămâne a proprietarului.** Regula spune ce justifică datele, iar schimbarea valorii implicite se face într-un commit separat, după ce proprietarul o aprobă.
