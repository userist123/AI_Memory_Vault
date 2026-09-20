# Preînregistrare: Experimentul de Normalizare a Tokenizatorului (EXP-TOKEN-001)

> **Document de Preînregistrare Formală — Programul de Măsurare (Partea 3, PR 1)**  
> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Ramură**: `antigravity/tokenizer-and-reranker-envelope`  
> **Dată / Timestamp Preînregistrare**: 2026-09-20T23:20:00Z  
> **Stare**: ÎNREGISTRAT ÎNAINTEA ORICĂREI RULĂRI EXPERIMENTALE  

---

## 1. Context și Justificare Teoretică

În diagnosticul pâlniei pierderilor (Partea 2, PR 3), s-a constatat că tokenizatorul lexical din `hybrid_retrieval.py`:
```python
TOKEN_RE = re.compile(r"[a-z0-9][a-z0-9_\-\.]*")
```
este strict ASCII și tratează caracterele românești (`ă`, `â`, `î`, `ș`, `ț`, respectiv variantele majuscule sau sedilă) drept delimitatori de cuvinte. În consecință:
- `învățare` devine `['nv', 'are']`
- `știință` devine `['tiin']`
- `regăsire` devine `['reg', 'sire']`
- `ședință` devine `['edin']`

Fragmentul `are` rezultat este unul dintre cele mai frecvente cuvinte din limba română (forma verbului *a avea*), introducând zgomot suplimentar și distorsionând frecvențele inverse de document (IDF) în BM25.

Cu toate acestea, măsurătoarea empirică din PR 3 pe benchmark v3 a arătat că performanța pe felia românească (14.75% recall, 9/61) nu a fost semnificativ inferioară celei pe felia engleză (17.39% recall, 12/69), cu $p_{\text{adj}} = 1.0$. Ipoteza explicativă este simetria defectului: atât interogarea cât și documentul sunt fragmentate în mod identic, permițând potriviri pe fragmente comune. Totuși, în cazurile în care interogarea este scrisă fără diacritice (`invatare`) iar nota este scrisă cu diacritice (`învățare`), sau invers, suprapunerea lexicală devine nulă.

Pentru a preveni capcana de a "repara" ceva intuitiv fără câștig măsurabil sau cu degradare colaterală pe limba engleză, acest experiment preînregistrează formal ipoteza, brațele, pragurile și criteriul decizional înainte de execuție.

---

## 2. Punctul de Operare de Evaluare

Toate măsurătorile se execută pe punctul de operare de producție al sistemului:
- **Principal**: `Principal.AI_AGENT`
- **Page Size**: `page_size = 5`
- **Prag Ciclu de Viață**: ACTIV (`AGENT_LIFECYCLE_FLOOR` = `{ACTIVE}`)
- **Set de Date**: Cele 130 de cazuri măsurabile (non-abstain) din `07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json` (SHA-256 înghețat: `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`).
  - Felia Română (RO): **61 cazuri**
  - Felia Engleză (EN): **69 cazuri**
  - Total: **130 cazuri**

---

## 3. Ipoteza Preînregistrată cu Direcție

- **Ipoteza H-TOKEN-1**: Normalizarea tokenizatorului crește Context Recall@5 pe felia românească cu **cel puțin $N = 5.00$ puncte procentuale** (reprezentând cel puțin $+3$ cazuri nete rezolvate din cele 61 cazuri românești, de la 14.75% la $\ge 19.75\%$).
  - *Justificarea valorii $N = 5.00$ pp*: Pe un eșantion de $n = 61$, o variație de 1–2 cazuri (1.64–3.28 pp) se situează în marja de zgomot stochastic și nu justifică modificarea tokenizatorului și riscurile de invalidare a indecșilor existenți. O creștere de $\ge 3$ cazuri nete ($\ge 4.92 \approx 5.0$ pp) reprezintă un prag minim de relevanță tehnică și economică.

---

## 4. Pragul de Pierdere Acceptabilă pe Limba Engleză

- **Condiția de Non-Regresie pe Engleză**:
  - Pe felia engleză ($n = 69$, nivel de referință 12/69 = 17.39%), degradarea maximă admisibilă este de **cel mult 1 caz pierdut** ($\Delta_{\text{EN}} \ge -1.45$ puncte procentuale, adică Recall EN $\ge 15.94\%$), cu condiția ca degradarea să fie statistic nesemnificativă ($p > 0.10$ pe testul McNemar exact).
  - Dacă felia engleză pierde $\ge 2$ cazuri ($\Delta_{\text{EN}} \le -2.90$ pp) sau dacă testul McNemar indică o degradare cu $p \le 0.10$, varianta respectivă este **RESPINSĂ AUTOMAT**, indiferent de mărimea câștigului obținut pe limba română (principiul protecției nucleului bilingv).

---

## 5. Brațele Experimentale

Experimentul compară 3 brațe izolate, fără a altera codul sursă de pe `main`:

1. **Brațul 1 (Linie de Referință — Baseline)**:
   - Tokenizatorul actual din producție:
     ```python
     TOKEN_RE = re.compile(r"[a-z0-9][a-z0-9_\-\.]*")
     ```
   - Caracterele non-ASCII sunt delimitatori.

2. **Brațul 2 (Litere Unicode — Unicode Preserving)**:
   - Tokenizator care recunoaște și păstrează caracterele Unicode specifice limbii române (`ă`, `â`, `î`, `ș`, `ț`, majuscule, sedile și virgule subscrise):
     ```python
     UNICODE_TOKEN_RE = re.compile(r"[a-z0-9\u0103\u00e2\u00ee\u0219\u021b\u015f\u0163][a-z0-9\u0103\u00e2\u00ee\u0219\u021b\u015f\u0163_\-\.]*", re.IGNORECASE)
     ```
   - Păstrează cuvintele românești întregi, fără fragmentare internă (`învățare` $\to$ `['învățare']`).

3. **Brațul 3 (Foldare Diacritice — Diacritics Stripping / Accent Folding)**:
   - Elimină diacriticele în mod simetric atât din interogare, cât și din textul notei înainte de tokenizare:
     - Mapare precisă a caracterelor românești: `ă/â -> a`, `î -> i`, `ș/ş -> s`, `ț/ţ -> t` (plus normalizare Unicode NFKD).
     - Aplică ulterior tokenizarea lowercase standard: `învățare` $\to$ `['invatare']`.
   - Acest braț testează ipoteza că în corpus coexistă ambele grafii (cu și fără diacritice) și că unificarea lor elimină asimetria de căutare.

---

## 6. Metodologie Statistică și Teste

1. **Metrica Primară**: Context Recall@5 (proporția de cazuri în care cel puțin o notă din `gold_relevant_notes` se regăsește în primele 5 rezultate returnate agentului).
2. **Intervale de Încredere**: Interval Wilson score asimetric 95% pentru toate proporțiile.
3. **Testul Statistic Primar**: **Testul McNemar exact pereche** (two-tailed binomial test) pe tabelul de contingență $2 \times 2$ al celor 130 de cazuri evaluate comparativ între fiecare braț experimental și linia de referință:
   - $b$ = numărul de cazuri în care Baseline a eșuat, dar Brațul Experimental a reușit (câștiguri).
   - $c$ = numărul de cazuri în care Baseline a reușit, dar Brațul Experimental a eșuat (pierderi).
   - Valoarea $p$ se calculează exact:
     $$p = 2 \sum_{k=0}^{\min(b, c)} \binom{b+c}{k} 0.5^{b+c}$$
     (sau $p = 1.0$ dacă $b = c$).

---

## 7. Criteriul Decizional Preînregistrat de Adoptare

O variantă de tokenizator este calificată pentru **ADOPTARE** dacă și numai dacă întrunește simultan:
1. $\Delta_{\text{RO}} \ge +5.00$ puncte procentuale ($\ge 3$ cazuri nete pe română).
2. $\Delta_{\text{EN}} \ge -1.45$ puncte procentuale (maxim 1 caz pierdut pe engleză, $p_{\text{McNemar}} > 0.10$).
3. $\Delta_{\text{Total}} \ge +1.54$ puncte procentuale ($\ge 2$ cazuri nete câștigate pe ansamblul celor 130 de cazuri).

Dacă ambele brațe experimentale (Brațul 2 și Brațul 3) îndeplinesc criteriile:
- Se alege varianta cu cel mai mare câștig net $\Delta_{\text{Total}}$.

Dacă niciun braț nu întrunește criteriul 1 sau dacă condiția de non-regresie pe engleză este încălcată:
- **Verdict: MENȚINERE BASELINE (Respingere)**. Tokenizatorul actual se menține neschimbat în producție.

---

## 8. Interdicție Specifică
Tokenizatorul de producție din `03_IMPLEMENTATION/packages/retrieval/hybrid_retrieval.py` **NU se modifică** în acest PR. Evaluarea rulează pe o copie injectabilă a indexului / funcției de tokenizare în mod complet izolat.
