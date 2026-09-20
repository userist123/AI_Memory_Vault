# Preînregistrare Experimentală: Pâlnia Pierderilor de Regăsire (Loss Funnel Diagnostic)

> **Document de Preînregistrare Formală — Programul de Măsurare (Partea 2, PR 1)**  
> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Data**: 2026-09-20  
> **Autor**: ANTIGRAVITY  
> **Commit de bază**: `4761dde9d` (origin/main după fuziunea PR #185)  
> **Stare**: ÎNREGISTRAT ANTERIOR ORICĂREI RULĂRI  

---

## 1. Context și Justificare

Benchmark-ul de regăsire `07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json` conține 130 de cazuri de testare empirice. În evaluarea de referință (cu graful oprit), pipeline-ul de regăsire al `MemoryController` obține succes (`context_recall`) pe 39 din cele 130 de cazuri, eșuând pe restul de 91 de cazuri.

Înainte de a propune sau implementa orice modificare a algoritmului de regăsire (reranker, expandare de interogare, dense retrieval sau relaxare a politicilor de ciclu de viață), este necesară cartografierea cauzală completă a eșecurilor pe baza urmei de telemetrie `RetrievalTrace` (contract OBS-001, v1.1.0). 

O notă de aur ratată la rangul 6 din cauza plafonului de pagină (`PAGINATION_CUT`) și o notă care nu a fost generată niciodată ca și candidat (`NEVER_CANDIDATE`) reprezintă patologii diametral opuse:
- Prima este o problemă de **clasare / ordonare** ce poate fi soluționată de un model de re-clasare (reranker);
- A doua este o problemă structurală de **generare de candidați / vocabular**, unde niciun reranker, oricât de performant, nu poate recupera nota lipsă din mulțimea de candidați.

Pentru a preveni ajustarea retrospectivă a ipotezelor (*HARKing — Hypothesizing After the Results are Known*) și alegerea convenabilă a concluziilor, acest document consemnează formal ipotezele direcționale, pragurile de decizie și metodologia statistică **înainte** de generarea oricărui rezultat diagnostic.

---

## 2. Ipoteze Preînregistrate (cu direcție explicită)

### Ipoteza H1: Categoria dominantă a ratărilor este `NEVER_CANDIDATE`
- **Enunț**: Categoria dominantă a celor 91 de ratări din benchmark v3 este `NEVER_CANDIDATE` (reprezentând cel puțin 40%, estimat $\ge 50\%$ din totalul eșecurilor).
- **Justificare mecanică**: Generatorul actual de candidați din `MemoryController` utilizează indici lexicali (potrivire BM25/TF-IDF pe cuvinte cheie din titlu și conținut). Dintre cele 130 de cazuri din benchmark, 70 aparțin claselor `multi_hop` (40) și `conceptual` (30), concepute deliberat pentru a testa inferențe indirecte sau legături tematice de nivel înalt fără repetarea textuală a termenilor. În absența unei etape de regăsire semantică densă (dense vector retrieval) sau a expandării de query, nota de aur nu acumulează scor lexical suficient pentru a accede în topul de 200 de candidați generați inițial.

### Ipoteza H2: Clasa `multi_hop` este dominată masiv de `NEVER_CANDIDATE`
- **Enunț**: În cadrul clasei de interogare `multi_hop`, proporția eșecurilor din categoria `NEVER_CANDIDATE` este $\ge 60\%$.
- **Justificare mecanică**: Interogările multi-hop necesită asocieri între premise separate (ex. mecanisme neurobiologice mediate sinaptic). Documentul țintă conține frecvent doar concluzia sau un pas intermediar, fără a menționa termenii specifici din premisa întrebării, determinând un scor de suprapunere lexicală nul sau neglijabil la generarea inițială a candidaților.

### Ipoteza H3: Interogările în limba română au un recall inferior celor în engleză
- **Enunț**: Rata de succes (`context_recall`) pentru cazurile formulate în limba română este mai mică decât pentru cazurile formulate în limba engleză, diferența fiind semnificativă statistic ($p < 0.05$).
- **Justificare mecanică**: Corpusul din Vault conține literatură primară preponderent redactată în limba engleză. Tokenizarea și indexarea lexicală nu includ lematizare morfologică sau stemmer dedicat limbii române, iar variațiile flexionare și prezența/absența diacriticelor conduc frecvent la nepotriviri de tokeni între interogarea în română și conținutul notelor.

### Ipoteza H4: Reușitele sunt puternic concentrate la vârful clasamentului (Top 3)
- **Enunț**: În rândul celor 39 de cazuri reușite (`hit@5`), nota corectă este poziționată pe primele 3 poziții (rang 1, 2 sau 3) în cel puțin 75% din instanțe.
- **Justificare mecanică**: Când există o suprapunere lexicală exactă pe termeni cu specificitate înaltă din note verificate și active, semnalele de scoring combinate plasează nota direct pe primele poziții; succesul este categoric și stabil, nu marginal la limita inferioară a ferestrei de paginare (rangurile 4-5).

---

## 3. Praguri de Decizie Formale

Deciziile de arhitectură ulterioare vor urma strict următoarele criterii decizionale cuantificabile:

1. **Adoptare Reranker (Problema de clasare)**:
   - **Condiție**: Dacă $\ge 40\%$ dintre ratări sunt clasificate ca `PAGINATION_CUT` sau `CANDIDATE_LIMIT_CUT` **ȘI** rangul median al notei corecte ratate este $\le 30$.
   - **Concluzie / Acțiune**: Se confirmă că sistemul identifică nota corectă în pool-ul extins de candidați, dar funcția de fuziune a scorurilor eșuează în a o promova în top 5. Se justifică dezvoltarea și măsurarea unui model de reordonare învățat (Cross-Encoder reranker).

2. **Adoptare Dense Retrieval (Problema generării de candidați)**:
   - **Condiție**: Dacă $\ge 40\%$ dintre ratări sunt clasificate ca `NEVER_CANDIDATE`.
   - **Concluzie / Acțiune**: Se confirmă că blocajul structural se situează în amonte de clasare. Niciun reranker nu poate reordona o notă care nu există în lista de intrare. Următorul experiment obligatoriu va viza regăsirea densă pe bază de reprezentări vectoriale (embeddings / hybrid search), nu reclasarea.

3. **Inconcludență / Ipoteze nesusținute**:
   - **Condiție**: Dacă atât (`PAGINATION_CUT` + `CANDIDATE_LIMIT_CUT`), cât și `NEVER_CANDIDATE` reprezintă sub $40\%$ din eșecuri (ex. dominanță a filtrărilor de ciclu de viață sau distribuție fragmentată fără o cauză clară).
   - **Concluzie / Acțiune**: Niciuna dintre cele două direcții nu se adoptă automat; se consemnează în raport că datele empirice nu susțin o cauză dominantă unică și se propun investigații calitative suplimentare.

---

## 4. Metodologie Statistică & Controlul Erorilor Multiple

### 4.1. Intervale de Încredere Wilson Score
Pentru fiecare proporție $\hat{p}$ raportată (pe baza a $n$ observații), intervalul de încredere asimetric la nivel de confidență de 95% va fi calculat prin metoda Wilson score:

$$CI_{95\%} = \frac{\hat{p} + \frac{z^2}{2n} \pm z \sqrt{\frac{\hat{p}(1-\hat{p})}{n} + \frac{z^2}{4n^2}}}{1 + \frac{z^2}{n}}$$

unde $z = 1.960$. Intervalele Wilson vor fi raportate pentru toate defalcările de eșec și ratele de recall.

### 4.2. Corecție pentru Comparații Multiple (Holm–Bonferroni)
Pentru a controla rata de eroare la nivel de familie (*Family-Wise Error Rate, FWER* $\le 0.05$) cauzată de testarea simultană a multiplelor dimensiuni (limbă, clasă de interogare, ciclu de viață, tip de notă):
- Se vor ordona crescător cele $m$ valori $p$ calculate: $p_{(1)} \le p_{(2)} \le \dots \le p_{(m)}$.
- Fiecare valoare $p$ corectată va fi calculată conform procedurii secvențiale descendente Holm:
  $$p_{\text{adj}(i)} = \min\left(1, \max_{k \le i} \left( (m - k + 1) \cdot p_{(k)} \right)\right)$$
- Se vor raporta obligatoriu ambele cifre: $p$ brut și $p_{\text{adj}}$. O ipoteză va fi considerată confirmată statistic dacă și numai dacă $p_{\text{adj}} < 0.05$.

---

## 5. Controale Negative Obligatorii ale Harnessului

Harnessul de testare nu va fi autorizat să emită concluzii despre Vault fără validarea prealabilă a trei controale negative riguroase:

1. **Etichete amestecate (Permutare aleatoare)**:
   - Rularea harnessului cu `gold_relevant_notes` permutate aleator între interogări (în memorie, fără alterarea fișierelor de pe disc).
   - Criteriu de trecere: Recall-ul trebuie să scadă la $\approx 0\%$ (sub 2%). Dacă recall-ul depășește această valoare, harnessul conține un artefact de potrivire falsă.

2. **Injecție de aur (Gold Injection)**:
   - Injectarea forțată a ID-ului notei corecte în lista de rezultate finale a fiecărei căutări.
   - Criteriu de trecere: Recall-ul raportat de harness trebuie să fie exact 100% (130/130). Orice valoare mai mică semnalează un defect de normalizare sau potrivire a identificatorilor.

3. **Măsurarea determinismului (Rulări repetate)**:
   - Executarea completă a benchmark-ului de 130 de cazuri de 3 ori consecutive în condiții identice.
   - Măsurarea ratei de fluctuație cauzată de spargerea egalităților de scor (*score tie-breaking*). Dacă există instabilitate, aceasta va fi raportată explicit ca interval de variație ($\pm \delta$).

---

## 6. Invariante și Angajamente de Integritate

- **Integritate baze înghețate**: Fișierele din `07_EVALUATION/retrieval_benchmark_v3/` rămân neatinse; integritatea lor este verificată prin SHA-256.
- **Transparența eșecului**: Dacă datele experimentale contrazic oricare dintre ipotezele H1–H4, invalidarea va fi afirmată direct și fără echivoc în raportul final.
- **Separarea măsurării de reparație**: Acest program are ca unic scop diagnosticarea și cartografierea empirică a cauzelor. Nicio componentă de recuperare sau modificare de cod a nucleului cognitiv nu va fi introdusă în acest ciclu.

---
*Înregistrat pe branch-ul `antigravity/loss-funnel` înainte de faza de execuție a harnessului de diagnostic.*
