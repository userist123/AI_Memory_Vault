# Program de închidere: de la concepte extrase la memorie care se corectează singură

> **Destinatar**: ANTIGRAVITY
> **Tip**: un singur program, patru părți care se susțin. Nu sunt patru sarcini.
> **Fără termene.** Se termină când porțile trec, nu când expiră un ceas.
> **Bază**: `main`. Ramură: `antigravity/closure-program`.

---

## 0. Ce este deja adevărat, ca să nu se remăsoare

Patru lucruri au stare cunoscută. Programul le atinge pe toate, dar pornește de
la ce s-a măsurat deja, nu de la zero.

| | Stare | Unde e dovada |
|---|---|---|
| 1. Extragerea conceptelor din cărți | **funcționează, cu porți demonstrate** | `FINDINGS.md`, `10_DOCUMENTATION/procedures/Ingesting_A_Book_Into_The_Ontology.md` |
| 2. Selectivitatea conceptelor | **măsurată, nerezolvată** | `FINDINGS.md`, secțiunea „Where that leaves the selectivity problem" |
| 3. Dispoziția celor 213 rânduri | **decisă, neexecutată** | `ROW_DISPOSITION.md`, `disposition_manifest.json`, `PENDING_DECISIONS.md` |
| 4. Bucla de învățare continuă | **parțială, deschisă** | `00_GOVERNANCE/VAULT_STATE.md` |

`FINDINGS.md` este un jurnal în care retragerile stau mai jos, nu în locul
afirmației retrase. **Nu se citează o cifră din mijlocul acelui fișier fără să
se verifice dacă o secțiune ulterioară a retras-o.** Cinci afirmații sunt deja
retrase acolo, inclusiv „precizie 100%", care era un defect de normalizator.

---

## 0.1. Constrângere care schimbă baza de măsurare

Toate cifrele de selectivitate din `FINDINGS.md` au fost produse cu modele
locale (`llama3.1:8b`, `qwen2.5:7b-instruct`, `mistral:7b-instruct`,
`qwen2.5-coder:7b`). **Modelele locale nu se mai folosesc în acest depozit.**
Extragerea se face cu modele online.

Asta nu invalidează metoda, dar invalidează numerele. Prima obligație a Părții A
este să restabilească linia de bază pe modelul nou, iar orice comparație cu
cifrele vechi trebuie să spună explicit că traversează o schimbare de model.
Dacă un rezultat vechi nu se reproduce, asta este un rezultat, nu un eșec.

---

## Partea A — Selectivitatea: de la „ce apare des" la „ce susține argumentul"

### A.1. De ce au eșuat toate încercările de până acum

S-au încercat patru mecanisme și niciunul nu separă un concept portant de
mobilierul experimental:

- **încrederea modelului** — constantă, nu ordonează nimic;
- **`claim_type`** — constantă, și inversată cu slotul pe un model;
- **`occurrences`** la 10 pagini pe chunk — 47 din 48 de concepte apar exact o
  dată, deci nu ordonează;
- **excluderea prin prompt** — modelul o ignoră.

`occurrences >= 2` la 3 pagini pe chunk a supraviețuit, dar cu o precizie de
70–92% care **scade pe măsură ce cartea se depărtează de neuroștiință** (92% pe
Schacter, 82% pe Squire, 70% pe Ashby). Acordul între modele funcționează pe
articole, nu pe cărți.

Toate aceste mecanisme au ceva în comun: măsoară o proprietate **intrinsecă** a
șirului de caractere — cât de des apare, cât de sigur e modelul. Dar „portant"
nu este o proprietate intrinsecă a unui termen. Este o proprietate a **rolului
lui în argumentul cărții**: un concept e portant dacă, scos afară, argumentul
cărții nu mai stă în picioare. `validation set` și `ReLU units` apar des într-o
lucrare fără să susțină nimic din teza ei.

Aici trebuie mutată măsurarea.

### A.2. Eticheta independentă: ce spune autorul, nu ce spune extractorul

Problema centrală a oricărei măsurători de selectivitate de până acum este că
adevărul de referință a fost construit tot din ieșirea extractorului (acordul
între modele). Asta măsoară consensul, nu importanța.

Există o etichetă produsă de autor, independentă de orice model:
**indexul de la finalul cărții și cuprinsul ei.** Un autor care pune un termen
în index, și mai ales unul care îi dedică o intrare de titlu sau un subcapitol,
a declarat că termenul e portant. Un autor nu indexează `validation set`.

**Obligația A.2**: pentru fiecare carte din corpus, se extrag trei etichete
independente de extractor:
1. **`in_index`** — termenul apare în indexul cărții (și cu câte trimiteri de pagină);
2. **`in_headings`** — termenul apare într-un titlu de capitol sau subcapitol;
3. **`defined`** — termenul apare într-o construcție definitorie în text
   („X is defined as", „we call X", „X, adică", cursive la prima apariție).

Nu toate cărțile au index. Se raportează explicit **pe câte cărți din corpus
există fiecare etichetă**, iar cărțile fără index nu se folosesc pentru
măsurătorile care depind de el. Dacă mai puțin de jumătate din corpus are index
utilizabil, se spune asta în concluzie, nu se ascunde într-o notă de subsol.

### A.3. Semnalele care se testează

Împotriva acestor etichete se testează șase semnale, dintre care patru sunt noi.
Fiecare se calculează per concept, per carte.

| Semnal | Definiție operațională | Ipoteza |
|---|---|---|
| `occ` | numărul de chunk-uri în care apare | linia de bază existentă, se re-măsoară pe modelul online |
| `spread` | dispersia aparițiilor pe lungimea cărții (deviația standard a pozițiilor normalizate, sau entropia lor) | un concept portant revine la distanță; mobilierul apare în rafală, într-o singură secțiune |
| `span` | distanța între prima și ultima apariție, ca fracțiune din carte | idem, formă mai brută, se raportează ca verificare de robustețe |
| `early_def` | apare într-o construcție definitorie în prima treime a cărții | cărțile își definesc devreme aparatul conceptual |
| `heading_hit` | apare într-un titlu de secțiune | etichetă de structură, nu de frecvență |
| `co_deg` | gradul în graful de co-apariție pe chunk, cu ceilalți candidați | un concept portant e conectat; mobilierul e periferic |

**Nu se raportează niciun semnal drept „funcționează" pe baza unei singure
cărți.** Fiecare semnal se măsoară pe **cel puțin șase cărți**, alese deliberat
ca să acopere distanța care a rupt precizia data trecută: taxonomie de memorie
(Schacter, Squire), cibernetică (Ashby), arhitecturi cognitive (Soar, Newell),
și cel puțin o carte din afara grupului cognitiv. Precizia se raportează **per
carte, niciodată doar agregat**, pentru că exact agregarea a ascuns căderea de
la 92% la 70%.

### A.4. Ce înseamnă „rezolvat"

Un semnal, sau o combinație de semnale, rezolvă selectivitatea dacă:

1. **Precizie ≥ 80% pe fiecare dintre cele șase cărți**, nu în medie, împotriva
   etichetei `in_index OR in_headings`. Cea mai slabă carte e cifra care contează.
2. **Acoperire utilizabilă**: coada reținută are între 15 și 60 de concepte per
   carte. Sub 15 înseamnă că repetă instrucțiunile promptului; peste 60 înseamnă
   că întoarcem coada de 4.400 pe care nimeni nu o revizuiește.
3. **Câștig demonstrat față de `occ >= 3`**: pe aceleași cărți, cu aceleași
   etichete, noul criteriu bate linia de bază la precizie **fără** să piardă mai
   mult de 10 puncte de acoperire. Dacă nu bate `occ >= 3`, concluzia corectă
   este că `occ >= 3` rămâne cel mai bun lucru pe care îl avem, iar programul
   raportează asta ca rezultat negativ.

### A.5. Controlul negativ, care lipsește azi

Se construiește o listă de **mobilier cunoscut**, luată din propriile ieșiri
anterioare: `validation set`, `ReLU units`, `backbone`, `hyperparameters`,
`number of training epochs`, `Rot-MNIST`, `S-TinyImageNet`, plus orice altceva
de acest fel din rulările existente. Criteriul final trebuie să respingă lista
aceasta. **Un criteriu care o acceptă nu se raportează ca funcțional, indiferent
ce spune precizia agregată.**

---

## Partea B — Cele 213 rânduri: de la decizie la execuție

Dispoziția e completă și argumentată: 84 de păstrat, 112 de șters, 8 de fuzionat,
1 de despărțit, 2 de păstrat ca propuse, 6 nedecise. Nu se re-judecă. Se execută.

### B.1. Executorul

Se scrie `30_SCRIPTS/ingestion/apply_row_disposition.py`, care citește
`disposition_manifest.json` și scrie în `01_ARCHITECTURE/ontology/slots/*.md`.
Condiții, toate obligatorii:

1. **`--dry-run` este comportamentul implicit.** Scrierea cere un indicator
   explicit. Rularea seacă tipărește diff-ul complet, rând cu rând.
2. **Acoperire totală verificată la intrare**: executorul refuză să pornească
   dacă reuniunea dispozițiilor nu acoperă exact cele 213 rânduri de pe disc,
   fără lipsă și fără surplus. Dacă discul s-a schimbat de la 2026-09-12, asta
   se află aici, nu după scriere.
3. **Cele 43 de rânduri deja promovate (commit `596aabfb5`) sunt intangibile.**
   Executorul verifică, înainte și după, că octeții lor sunt neschimbați, și
   eșuează dacă nu. Aceasta este garda pentru `I-003`.
4. **Cele 7 rânduri în așteptare nu se ating.** Cele 6 `UNDECIDED` și `SPLIT`-ul
   `familiarity` rămân exact cum sunt pe disc. Executorul le numără și le
   raportează, dar nu le scrie. Decizia lor este a lui Owner și nu a fost dată.
5. **Fuziunea comasează, nu suprascrie.** Cele 8 `MERGE` adună `occurrences` și
   `evidence` în ținta lor. Un `MERGE` care pierde o apariție este un defect.
6. **Reversibil printr-un singur commit.** Execuția intră într-un commit separat,
   care nu conține altceva.

### B.2. Poarta de verificare

Se scrie `20_TESTS/ontology/test_row_disposition_applied.py`, care rulează pe
arborele rezultat și verifică:

- numărul de rânduri rămase = 84 + 2 + 7 = **93**, iar cele 112 șterse nu mai
  apar în niciun fișier de slot;
- niciunul dintre cele 43 de rânduri protejate nu s-a schimbat;
- pentru fiecare dintre cele 8 fuziuni, suma `occurrences` din ținta finală este
  egală cu suma din sursă plus țintă, măsurată înainte;
- `find_slot_conflicts()` nu raportează niciun conflict pe arborele final.

### B.3. Controlul negativ

Se plantează, într-o copie de test a sloturilor, un rând care încalcă fiecare
dintre cele patru garanții de mai sus, câte unul pe rând, și se demonstrează că
testul **pică** pentru fiecare. Un test care nu a fost văzut picând nu a fost
verificat.

---

## Partea C — Bucla închisă: rezultat → dovadă → învățare → mutație canonică

Aici este partea care nu există încă și care dă sens celorlalte trei. O memorie
care se umple dar nu se corectează este un depozit, nu o memorie.

### C.1. Ce înseamnă „închisă"

Bucla e închisă când toate cele patru tranziții au un mecanism care rulează fără
intervenție umană, iar fiecare lasă o urmă verificabilă:

1. **Rezultat**: o rulare care a folosit o memorie produce un deznodământ
   observabil (a reușit, a eșuat, a costat X).
2. **Dovadă**: deznodământul se leagă înapoi de memoria care l-a influențat, cu
   identificatorul ei, nu cu o descriere.
3. **Învățare**: dovezile acumulate schimbă o cantitate — încrederea în memoria
   aceea, sau aplicabilitatea ei.
4. **Mutație canonică**: schimbarea de încredere devine o modificare în vault,
   cu proveniență, reversibilă, și **posibil o retrogradare**.

### C.2. Condiția care face bucla onestă

**O buclă care poate doar să promoveze nu este o buclă de învățare, este un
amplificator.** Aceasta este cerința centrală a Părții C.

Mecanismul trebuie să poată retrograda și retrage o memorie, iar asta trebuie
demonstrat, nu declarat. Concret:

- se plantează o memorie **falsă, dar plauzibilă**, care recomandă constant o
  alegere greșită;
- se lasă bucla să ruleze peste rezultate reale;
- se arată că încrederea în ea scade monoton și că ajunge sub pragul de
  retragere într-un număr de observații care se raportează ca cifră.

Dacă memoria falsă supraviețuiește, bucla nu e închisă, oricâte componente
individuale trec testele lor.

Simetric: se plantează o memorie **corectă** și se arată că nu e retrogradată de
zgomot. O buclă care retrage tot este la fel de inutilă ca una care nu retrage
nimic.

### C.3. Protecția împotriva auto-confirmării

Dacă memoria influențează alegerea, iar rezultatul alegerii hrănește încrederea
în memorie, sistemul își confirmă propriile convingeri. Trebuie o separare:
încrederea se actualizează **numai din rezultate observate independent de
influență** — adică din brațul de control, sau din cazurile în care memoria a
fost consultată dar nu a schimbat alegerea.

Se măsoară și se raportează câtă dovadă rămâne după această separare. Dacă rămân
prea puține cazuri ca actualizarea să fie semnificativă, aceasta este constatarea
care contează, iar soluția este un plan de colectare, nu o slăbire a separării.

### C.4. Legătura cu Partea A

Semnalul de selectivitate din Partea A produce concepte cu un scor. Bucla din
Partea C produce, în timp, dovezi despre care concepte s-au dovedit utile la
recuperare. Programul se închide când al doilea poate corecta primul: un concept
promovat pe baza scorului, dar niciodată util în recuperare, trebuie să scadă în
încredere prin același mecanism ca orice altă memorie.

Nu se cere ca această corecție să fie demonstrată pe date reale acumulate — nu
există încă istoric suficient. Se cere ca **drumul să existe și să fie testat pe
dovezi sintetice**, astfel încât să funcționeze când istoricul apare.

---

## Partea D — Contractul de raportare

Verificarea livrării anterioare (Planning Influence V3) a găsit un tipar care se
repetă și care trebuie oprit aici. Următoarele nu sunt sugestii.

1. **Nicio cifră scrisă de mână în proză.** Fiecare număr dintr-un raport
   generat de acest program se produce dintr-un artefact commis, printr-un
   script care citește artefactul. În V3, tabelul „sinoptic" prezentat ca extras
   dintr-un CSV nu corespundea cu acel CSV **în niciunul din cele 11 rânduri
   verificate**, iar una dintre cifrele de siguranță era la jumătate față de
   realitate. Un tabel care se reproduce octet cu octet nu ajută la nimic dacă
   textul povestește altă rulare.
2. **DEVIATIONS înseamnă devieri, nu confirmări.** Dacă preînregistrarea fixează
   un prag și rezultatul nu îl atinge, asta se scrie acolo. În V3, criteriul
   preînregistrat era $\mathbb{E}[\text{noduri}] = 2{,}50$, rezultatul a fost
   2,88, reperul a fost înlocuit tăcut cu 2,9167 și secțiunea spunea „zero
   devieri". Noul reper era, de altfel, cel corect — cu atât mai mult trebuia
   declarat, cu derivarea lui, nu strecurat.
3. **Fiecare poartă are un control negativ care trece prin detectorul real.**
   Nu prin altul scris pentru ocazie. În V3, „testul de scurgere plantată" a
   scris un planificator-jucărie separat și a constatat că trișează — ceea ce nu
   spune nimic despre suita care ar fi trebuit să prindă scurgerea.
4. **Nicio afirmație despre igiena depozitului fără verificarea arborelui.** V3
   a raportat „zero fișiere financiare/personale în arbore sau istoric"; arborele
   ramurii conținea patru documente personale moștenite din `main`.
5. **Un rezultat negativ, raportat clar, este o livrare validă.** Dacă niciun
   semnal din Partea A nu bate `occ >= 3`, dacă bucla din Partea C nu se poate
   închide fără date pe care nu le avem — se scrie asta. Un „POARTĂ TRECUTĂ"
   sprijinit pe cifre care nu sunt în artefacte este mai rău decât un eșec
   raportat.

---

## Ordinea de execuție

Părțile nu sunt independente, dar nici strict secvențiale:

- **B poate începe imediat** — dispoziția e deja decisă, nu depinde de nimic din A.
- **A trebuie să înceapă cu re-baseline-ul pe model online** (0.1), altfel toate
  comparațiile ei sunt fără sens.
- **C are nevoie de B** pentru o ontologie stabilă peste care să învețe.
- **Cele 7 rânduri în așteptare rămân în așteptare** în toate cele trei părți.

## Ce se livrează

O ramură, cu commit-uri separate pe părți, care conține: executorul și poarta
lui, harnașamentul de selectivitate cu datele per carte, mecanismul buclei cu
cele două teste de plantare, și un raport în care fiecare cifră vine dintr-un
artefact aflat în aceeași ramură.
