# Reguli de calitate pentru invatarea din carti

> Scop: in vault intra doar idei distilate, scrise in cuvintele mele, legate de o problema reala si verificate in practica. Nimic nu devine memorie activa doar pentru ca a fost citit.

## 0. Principii

| # | Principiu | Consecinta |
|---|---|---|
| P1 | Pornesc de la problema | Citesc selectiv, doar ce raspunde la ea |
| P2 | Cuvinte proprii, structura proprie | Notele nu urmaresc ordinea cartii |
| P3 | Un concept, o nota | Incarcare precisa, fara context inutil |
| P4 | Sursa ramane urmaribila | Titlu, capitol, pagina pentru afirmatiile importante |
| P5 | Conflictele se pastreaza | Nu aleg tacut o varianta |
| P6 | Utilitatea se demonstreaza | Test de utilizare + evaluare cu/fara nota |
| P7 | Fara auto-promovare | Trecerea in active cere aprobarea mea |
| P8 | Doar idei in vault | Textul brut al cartii nu intra in retrieval |

## 1. Flux orientat pe probleme

`PROBLEMA -> CITIRE SELECTIVA -> INCHID CARTEA -> SCRIU DIN MEMORIE -> APLICARE + EXEMPLU -> TEST -> PASTREZ / RESCRIU / STERG`

### 1.1 Reguli de lucru

1. Formulez problema pe care vreau s-o rezolv.
2. Citesc selectiv doar partile relevante.
3. Inchid cartea si scriu ideea din memorie.
4. Adaug aplicarea si un exemplu propriu.
5. Testez nota cu un agent.
6. Pastrez, rescriu sau sterg in functie de rezultat.

## 2. O carte, o nota-harta

Fiecare carte folosita are o nota-harta `type: book_map`.

Harta:
- identifica titlul, autorii si editia;
- leaga problemele relevante;
- marcheaza capitolele folosite;
- leaga notele atomice;
- face vizibile golurile;
- pastreaza conflictele si limitarile.

`coverage` se actualizeaza cand este procesat un capitol nou.

## 3. Un concept, o nota

O nota atomica are un singur scop si poate fi incarcata independent.

Test:
- poate fi descrisa intr-o propozitie;
- are un singur titlu natural;
- poate fi folosita fara alte note;
- daca poate fi impartita fara pierderea sensului, trebuie impartita.

Tipuri: `concept`, `procedure`, `rule`, `pattern`, `pitfall`, `metric`, `example`.

Limite recomandate:
- ideea: maximum 3 propozitii;
- nota: 150-400 cuvinte;
- legaturi: 3-8;
- citate directe: rare si scurte, cu sursa.

## 4. Sursa afirmatiilor

Pentru notele derivate din surse sunt obligatorii:
- `source_title`;
- `chapter`;
- `page_range`.

Afirmatiile cu cifre, formule sau cerinte critice cer pagina exacta.

Fara sursa completa, nota ramane `raw` si nu poate fi promovata.

Daca ideea este proprie, se foloseste `source_title: "experienta proprie"` si se explica contextul.

## 5. Contradictii

Cand doua surse se contrazic, ambele pozitii sunt pastrate.

Se foloseste un registru `CONFLICT-<domain>-<slug>` cu:
- pozitia A;
- pozitia B;
- sursele;
- cauza posibila;
- experimentul care poate rezolva conflictul;
- comportamentul agentului pana la rezolvare.

Conflictul `open` nu se elimina prin rescrierea uneia dintre note.

O nota cu conflict `open` de severitate `high` nu poate deveni `active`.

## 6. Test de utilizare

Agentul rezolva un task realist folosind doar nota si dependentele declarate, fara acces la carte.

Grila:
- Corectitudine: 0-2
- Completitudine: 0-2
- Fara ghicit: 0-2
- Fara surse externe: 0-2
- Reproductibilitate: 0-2

Prag: minimum 8/10 si minimum 1 la Corectitudine.

Rezultat:
- >= 8: `pass`, eligibila pentru `verified`;
- 5-7: imbunatatire si retestare;
- < 5: respingere sau rescriere.

Pentru notele importante: minimum doi agenti diferiti.

O nota modificata se retesteaza.

## 7. Evaluare cu/fara nota

Valoarea unui set de note se masoara.

Conditii:
- A: agent fara note;
- B: agent cu notele rutate;
- set fix de taskuri;
- minimum doua modele;
- minimum trei repetari per task;
- aceeasi grila si aceleasi setari.

Metrici:
- succes;
- calitate;
- erori;
- consistenta;
- tokeni;
- timp;
- justificarea notei folosite.

`Delta = (S_cu - S_fara) / S_fara`

Daca numitorul este zero, se raporteaza diferenta absoluta.

Nu se considera utila o nota doar pentru ca pare utila.

## 8. Ciclu de viata

`raw -> unverified -> verified -> active`

Promovari:
- `raw -> unverified`: schema completa, sursa si pagina;
- `unverified -> verified`: corroborare >= 1 si test de utilizare trecut;
- `verified -> active`: fara conflict high deschis, evaluare cu/fara OK si aprobarea mea;
- regresul, conflictul nou sau editia depasita pot retrage o nota in `unverified`.

Nu exista auto-promovare.

Pentru domenii critice (juridic, medical, financiar, securitate), o singura carte nu este suficienta pentru `active`; este necesara o sursa suplimentara.

## 9. Calitatea notelor

- structura dupa problema, nu dupa ordinea cartii;
- fara rescriere propozitie-cu-propozitie;
- exemple proprii;
- textul brut al cartii nu intra in retrieval si nu se pune in Git public;
- daca verificarea de similaritate ridica dubii, nota se rescrie.

## 10. Checklist per nota

- problema clara;
- un singur concept;
- cuvinte proprii;
- sursa, capitol, pagina;
- exemplu propriu si limite;
- legaturi;
- conflicte, daca exista;
- test de utilizare;
- corroborare;
- aprobare personala pentru `active`.

## 11. Checklist per carte

- nota-harta existenta;
- capitolele folosite marcate;
- notele atomice testate;
- evaluare cu/fara rulata pentru set;
- textul brut exclus din retrieval.

## 12. Rezumat

| Regula | Verificare | Esecul produce |
|---|---|---|
| Pornesc de la problema | problema sau `problem` clar | refac nota |
| O carte, o harta | harta existenta | creez harta |
| Un concept, o nota | test atomic | sparg nota |
| Sursa afirmatiilor | titlu, capitol, pagina | ramane `raw` |
| Contradictii | registru de conflicte | nu promovez |
| Test de utilizare | >= 8/10 | imbunatatesc/retrag |
| Evaluare cu/fara | imbunatatire pozitiva si repetabila | revizuiesc/retrag |

## 13. Instructiuni pentru orice agent

1. Citeste doar notele `active` si `verified` daca sunt cerute explicit.
2. Pentru conflict `open`, prezinta ambele pozitii si spune ca nu este rezolvat.
3. Nu folosi note `raw` ca fapte.
4. Daca lipseste o cunoastere, spune ce lipseste; nu inventa.
5. Raporteaza ce nota a ajutat si ce a lipsit.
6. Nu promova si nu modifica note fara aprobare prin MemoryController.

## 14. Regula pentru cercetarea Book-to-Memory

Orice ipoteza derivata din literatura urmeaza lantul:

`BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM`

Niciun fapt biologic sau mecanism cognitiv nu este implementat in productie doar pentru ca este descris intr-o carte.

Corpusurile de cercetare pot contine doar materialul necesar pentru reproducibilitatea experimentului si nu pot introduce text brut al cartilor in retrieval.

Policy status: ACTIVE
