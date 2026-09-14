# PROGRAM DE CERCETARE — ANTIGRAVITY: MetaTrader 5, universul complet de instrumente și studiul preînregistrat

> Emis: 2026-09-14 · **Fără termen** — lucrezi până e gata și raportezi atunci.
> Un singur livrabil mare. Structura de mai jos este ordinea de lucru, nu o listă de sarcini mici.
> **Cercetare pe date. Nicio tranzacție, nici măcar pe cont demo.**

---

## 0. Miza, și ce s-a învățat azi

Întrebarea: **există, printre instrumentele accesibile prin MetaTrader 5, strategii care bat costurile reale pe date pe care nu le-ai văzut când le-ai construit?**

Punctul de plecare nu e neutru. Autoritățile naționale din UE au constatat că **între 74% și 89% dintre conturile de retail pierd bani pe CFD-uri** (ESMA). Un rezultat pozitiv trebuie să bată rata asta de bază, nu doar zero.

Programul Polymarket de azi a eșuat la verificare din motive care se vor repeta aici dacă nu sunt blocate din start:

| ce s-a întâmplat pe Polymarket | ce interzice acest program |
|---|---|
| valori p scrise de mână (`raw_p = 0.001`) și intervale construite prin formulă | orice p și orice interval se calculează; un `grep` după constante literale în locul lor respinge livrarea |
| 299 din 483 de prețuri erau un substitut (0,5), nedetectat | secțiunea B2: calitatea datelor se măsoară **înainte** de orice analiză |
| „descoperiri" din comparații între lucruri care nu erau comparabile (props ale unor jucători diferiți) | fiecare comparație are o regulă de comparabilitate scrisă în preînregistrare |
| `run_all.py` tipărea „reprodus identic" fără să compare nimic | `run_all.py` compară octet cu octet și iese cu cod de eroare la orice diferență |
| raportul, tabelul și mesajul dădeau patru valori diferite pentru aceeași cifră | fiecare cifră din raport e generată din tabel, nu copiată de mână |
| recensământul folosea metoda pe care propriul test o arătase nesigură | o metodă infirmată într-o secțiune nu mai poate fi folosită în alta |

**Riscul specific al acestui program este selecția.** „Toate acțiunile și perechile" înseamnă sute de instrumente × mai multe strategii × mai mulți parametri — mii de teste. Din mii de teste, câteva zeci arată excelent **din pură întâmplare**. „Cele mai ok", alese după ce s-au văzut rezultatele, sunt exact acelea. Tot programul e construit ca să le distingă de un avantaj real.

---

## Ce trebuie să facă Owner înainte (nu tu)

Tu nu creezi conturi, nu introduci parole și nu atingi date de autentificare.

1. Instalează terminalul MetaTrader 5.
2. Deschide un **cont demo** la un broker **autorizat ASF sau cu pașaport UE**, verificat în registrul public ASF.
3. Se autentifică în terminal și lasă terminalul pornit.
4. Instalează pachetul Python `MetaTrader5` (funcționează doar pe Windows, cu terminalul pornit).
5. Îți dă, dacă o are, **specificația de comision** a brokerului, care nu apare în datele MT5.

Scripturile tale se conectează la terminalul deja autentificat cu `mt5.initialize()`, **fără** argumente de login. Nicio parolă, niciun număr de cont și niciun nume de server nu ajung în depozit. În proveniență, brokerul se identifică doar prin numele companiei.

---

## PARTEA A — Preînregistrarea (se comite ÎNAINTE de orice legătură între semnale și randamente viitoare)

Fișier: `07_EVALUATION/metatrader/research/PREREGISTRATION.md`, comis și urcat înainte de primul script care calculează un randament după un semnal. Ordinea commit-urilor se verifică.

### A1. Universul — regula, nu lista

„Cele mai ok" se definește **exclusiv prin criterii care nu privesc performanța**, fixate înainte:

- mod de tranzacționare complet (nu doar închidere);
- istoric disponibil pe D1 de cel puțin N ani, cu N fixat;
- **cost relativ**: spread median ÷ ATR(14) median sub un prag fixat;
- fără goluri de date peste pragul din B2.

Aplici regula pe tot recensământul și raportezi câte instrumente trec, pe categorie. **Niciun instrument nu intră sau nu iese după ce ai văzut un randament.**

### A2. Strategiile — puține, clasice, cu parametri fixați

Maximum cinci familii, fiecare cu o grilă mică scrisă dinainte:

| familie | idee |
|---|---|
| S1 momentum pe serie de timp | semnul randamentului pe ultimele L zile |
| S2 revenire la medie | abatere față de o medie mobilă, în unități de volatilitate |
| S3 breakout | depășirea maximului/minimului pe L zile |
| S4 carry, doar FX | diferența de swap între cele două valute |
| S0 controale | cumpără-și-ține; **intrări aleatoare cu aceeași frecvență și aceeași durată de deținere** ca strategia testată |

Controlul S0 cu intrări aleatoare e obligatoriu: o strategie care nu bate intrările aleatoare cu aceeași expunere nu are niciun avantaj de semnal, oricât de bine ar arăta.

### A3. Execuția și costurile

- Semnal la închiderea barei `t`, execuție la **deschiderea barei `t+1`**. Nimic de la bara `t+1` nu intră în semnal.
- Cost pe tranzacție = spread **măsurat** (B3) + comision (de la Owner; dacă lipsește, testul se rulează la mai multe niveluri declarate și se raportează toate) + swap real pe durata deținerii.
- Fără levier în evaluare: randamentele se raportează pe expunere nominală. Levierul nu creează avantaj, doar îl amplifică.

### A4. Împărțirea în timp

- **Dezvoltare**: tot istoricul, mai puțin ultimele 18 luni.
- **Validare**: lunile 18–6 dinainte de sfârșit, prin walk-forward.
- **Holdout blocat**: ultimele 6 luni. **Nu se atinge până în partea H.** Se evaluează o singură dată, doar pentru supraviețuitori, și se consemnează momentul.

### A5. Statistica

- **Metrica principală**: randament net anualizat după costuri și raportul Sharpe net, per strategie × instrument.
- **Comparații multiple**: pe întreaga grilă strategie × parametru × instrument, test **SPA al lui Hansen** sau **Reality Check al lui White**, cu bootstrap staționar pe blocuri. Numărul de replicări, lungimea medie a blocului și sămânța, fixate.
- **Dependență**: instrumentele corelate (EURUSD/GBPUSD, acțiuni din același sector) nu sunt observații independente. Grupare pe clustere de corelație, cu regulă scrisă.
- **Efect minim relevant**: Sharpe net minim, declarat numeric, pe validare.
- **Ce înseamnă nul**: nicio strategie nu bate controalele S0 după corecție. Acesta e un rezultat valid și probabil.

---

## PARTEA B — Recensământul și calitatea datelor

Director: `07_EVALUATION/metatrader/research/`.

### B1. Recensământul instrumentelor

Prin `symbols_get()` și `symbol_info()`:

- câte simboluri, pe categorie (FX major/minor/exotic, indici, mărfuri, acțiuni, crypto), cu regula de clasificare scrisă;
- mod de tranzacționare, dimensiune contract, pas minim, spread curent, swap long/short;
- **adâncimea reală a istoricului** pe D1, H1 și M1, măsurată pe fiecare instrument, nu presupusă. Brokerii limitează istoricul, iar limita diferă pe interval de timp.

### B2. Calitatea datelor — înaintea oricărei analize

Pe fiecare instrument, măsoară și raportează:

- goluri de timp față de programul de tranzacționare al instrumentului;
- bare cu volum zero și **bare înghețate** (open = high = low = close pe mai multe bare la rând) — echivalentul prețului 0,5 de pe Polymarket;
- marcaje de timp duplicate sau neordonate; fusul orar al serverului, stabilit empiric;
- **acțiuni**: salturi de preț la split-uri și dividende — sunt seriile ajustate sau nu? Stabilește pe cazuri cunoscute, nu presupune;
- **prețuri care nu se pot tranzacționa**: spread zero sau spread negativ.

Un instrument care pică pragurile nu intră în univers. Toate excluderile, listate cu motiv.

### B3. Costurile măsurate

- Pe un eșantion de instrumente din fiecare categorie: tick-uri cu bid/ask (`copy_ticks_range`) pe mai multe săptămâni, **distribuția spread-ului pe oră a zilei**. Spread-ul la deschiderea barei, unde se execută strategia, poate fi de câteva ori cel median.
- Swap-ul real din `symbol_info`, cu ziua de swap triplu.
- Rezultatul devine modelul de cost din A3, cu parametri comiși.

### B4. Stocare

- D1 și H1 pentru tot universul, comise ca fragmente de maximum 20 MB, fiecare cu SHA-256 în `MANIFEST.json`.
- Tick-urile doar pentru eșantionul din B3.
- M1 nu se comite; dacă e folosit, se descrie în proveniență și se justifică.
- Proveniența: compania brokerului, build-ul terminalului, momentul UTC al extragerii, simbolul, intervalul de timp, numărul de bare, SHA-256.

---

## PARTEA C — Motorul de backtest

`07_EVALUATION/metatrader/research/engine/` — cod de cercetare, nu de producție.

- Vectorizat, determinist, fără stare ascunsă.
- Execuție la deschiderea barei următoare, costuri din B3, swap pe fiecare noapte deținută.
- **Testele motorului** în `20_TESTS/metatrader/`, pe date sintetice construite anume:
  - **testul anti-privire-în-viitor**: o strategie care primește intenționat semnalul de la `t+1` trebuie să arate un avantaj enorm pe date aleatoare, **iar motorul trebuie să-l respingă**. Dacă nu îl respinge, motorul are o scurgere;
  - pe o serie de mers aleator fără derivă, fiecare strategie trebuie să piardă aproximativ costurile, nu să câștige;
  - costul unei tranzacții dus-întors, verificat manual pe un caz mic.

---

## PARTEA D — Analiza pe dezvoltare și validare

Exact cum spune preînregistrarea.

- Fiecare strategie × parametru × instrument: randament net, Sharpe net, număr de tranzacții, expunere, cost total plătit, comparație cu S0.
- Testul SPA sau Reality Check pe toată grila, cu valoarea p calculată.
- **Rezultatele nule primele.**
- Raportează și **câte combinații ar fi părut profitabile fără corecție**. Cifra asta arată cât de ușor ar fi fost să te păcălești.

---

## PARTEA E — Holdout-ul

Doar pentru combinațiile care trec corecția pe validare. O singură evaluare. Dacă nicio combinație nu trece, holdout-ul **nu se deschide** și asta se consemnează.

---

## PARTEA F — Reproductibilitate (condiție de acceptare)

- Toate scripturile, comise în `07_EVALUATION/metatrader/research/`. Nimic în `scratch/` sau într-un director local al agentului.
- Separare strictă: `fetch_*.py` (cer terminalul) și `analyze_*.py` (lucrează doar pe fișierele comise).
- `run_all.py` **regenerează toate tabelele într-un director temporar, le compară octet cu octet cu cele comise și iese cu cod diferit de zero la orice diferență**, cu lista diferențelor. Niciun mesaj de succes tipărit fără comparație.
- Toate sursele de aleatoriu, cu sămânță fixă. Rulat de două ori, trebuie să dea aceiași octeți.
- **Fiecare cifră din raport e generată din tabele** de un script, nu scrisă de mână.

---

## PARTEA G — Raportul final

`07_EVALUATION/metatrader/research/RESEARCH_REPORT_V1.md`:

1. **Ce s-a stabilit că nu se poate sau nu există.**
2. Recensământul: instrumente pe categorie, câte trec regula de univers, câte sunt excluse pentru calitatea datelor și de ce.
3. Costurile măsurate, pe categorie și pe oră.
4. Rezultatele, nule întâi: câte combinații au fost testate, câte păreau profitabile fără corecție, câte supraviețuiesc corecției, câte bat S0.
5. Holdout-ul, dacă a fost deschis.
6. `DEVIATIONS`: fiecare abatere de la preînregistrare, cu motivul.
7. Recomandarea, argumentată doar din rezultate.
8. Lista fișierelor, fiecare cu SHA-256, **toate prezente pe disc și comise**.

---

## Reguli care nu se negociază

- **Niciun apel `order_send`, `order_check` sau alt apel de tranzacționare, nicăieri.** Un `grep` după ele pe scripturile comise trebuie să nu găsească nimic.
- **Nicio parolă, niciun număr de cont și niciun server** în cod, fișiere, proveniență sau commit-uri.
- Niciun modul din `03_IMPLEMENTATION/packages/` modificat. Ontologia neatinsă.
- Ramură proprie din `origin/main`. Înainte de push: suita completă verde, `30_SCRIPTS/verification/validate_repository_layout.py` și `30_SCRIPTS/verification/repository_hygiene.py` fără erori.
- Se aplică regulile permanente din `07_EVALUATION/polymarket/ANTIGRAVITY_RESEARCH_PROGRAM_V1.md`:
  - nicio ieșire de script citată ca verificată fără script comis;
  - nicio descompunere descrisă fără cifrele ei;
  - scripturile care descarcă date se comit împreună cu răspunsul brut.
- **Nu scrie în raport că ceva „funcționează", „e profitabil" sau „e garantat"** decât cu intervalul și valoarea p corectată lângă afirmație.

---

## Cum voi verifica

1. Commit-ul preînregistrării precede orice commit care calculează randamente după semnal.
2. `run_all.py` rulat offline de două ori: aceiași octeți, cod de ieșire zero; apoi modific un tabel comis și cer cod de ieșire diferit de zero.
3. `grep` după valori p și intervale scrise ca literali, după `order_send` și după date de autentificare.
4. Recalculez independent: regula de univers pe recensământ, costul unei strategii pe un instrument, testul anti-privire-în-viitor, și cel puțin o valoare SPA.
5. Numărul de bare înghețate și de goluri, pe un eșantion ales de mine.
6. Că holdout-ul nu a fost citit înainte de partea E — din ordinea commit-urilor și din codul de analiză.

Dacă programul se oprește într-un punct din motive legitime — de exemplu brokerul nu oferă destul istoric — continuă cu ce se poate măsura și spune clar ce nu s-a putut face și de ce.

---

*Nu este sfat de investiții. Programul măsoară dacă datele susțin o afirmație; orice decizie cu bani reali e a lui Owner.*
