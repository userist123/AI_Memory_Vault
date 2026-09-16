# MANDAT — ANTIGRAVITY: cei 19 puncte procentuale

> Emis: 2026-09-14, 00:40 · **Termen: 06:00**
> O singură sarcină. Nu o listă de etape.
> Livrarea precedentă a fost verificată integral — toate cele 13 hash-uri, cascada de 500, cele 36/14 și +14.1774 reproduse independent din fixture-uri. Aceasta pornește de acolo.

---

## Constatarea

Controlul mecanic dă **+28,35%** pe 50 de piețe. Ai scris că „nu este un randament absurd" pentru că `outcome_ids[0]` a fost adesea favoritul.

Am descompus rezultatul pe intervale de preț:

| preț de intrare | piețe | câștiguri | rată reală |
|---|---|---|---|
| 0,0 – 0,2 | 3 | 0 | 0,00 |
| 0,2 – 0,4 | 4 | 1 | 0,25 |
| **0,4 – 0,6** | **33** | **25** | **0,76** |
| 0,6 – 0,8 | 5 | 5 | 1,00 |
| 0,8 – 1,0 | 5 | 5 | 1,00 |

**Treizeci și trei de piețe cumpărate în jur de 50 de cenți au câștigat în 76% din cazuri.** Global: 72% câștiguri la un preț mediu de 0,5314 — o abatere de **+18,9 puncte procentuale** față de ce implică prețul.

Un preț de 50 de cenți pe o piață de predicție *este* o probabilitate declarată de 50%. Dacă realitatea dă 76%, atunci ori piața se înșală sistematic cu 26 de puncte pe cea mai lichidă categorie a ei, ori măsurătoarea noastră nu măsoară ce credem.

A doua variantă e de departe cea mai probabilă, și există deja un indiciu în datele tale: **620 de puncte pe 50 de piețe înseamnă 12,4 cotații per piață.** O piață care a rulat ore întregi nu are douăsprezece cotații. Aceea nu e o bandă de tranzacționare; e un eșantion, și nu știm de unde.

---

## Sarcina

**Stabilește dacă cei 19 puncte procentuale sunt reali sau un artefact, și livrează un set de date care poate susține un răspuns.**

Atât. Un singur lucru, până la 06:00.

Nu e o sarcină de construit. E una de aflat — iar dacă răspunsul e „măsurătoarea era greșită, iată de ce", mandatul e îndeplinit integral.

---

## Ce trebuie să conțină răspunsul ca să fie un răspuns

**1. Ce este, de fapt, primul punct din bandă.**

Pentru fiecare din cele 50 de piețe: momentul primei cotații, raportat la deschiderea pieței și la `closedTime`. În ce parte a vieții pieței cade? Dacă banda începe la 80% din durata pieței, atunci „prețul de intrare" e prețul de după ce rezultatul era în mare parte cunoscut, iar cele 76 de procente se explică singure.

**2. De ce sunt doar 12,4 puncte per piață.**

`interval="all"` a returnat asta. Este toată banda, sau o eșantionare? Compară cu `interval` mai fin sau cu fereastră explicită `startTs`/`endTs` pe aceeași piață și arată diferența de număr de puncte. Dacă endpoint-ul decimează, spune rata.

**3. Cele 22 de puncte post-închidere.**

Le-ai găsit tu. Acum spune ce sunt: cotații reale de după închidere, sau puncte de decontare la 0/1? Dacă o parte din bandă e post-rezoluție, iar prima cotație a vreunei piețe cade acolo, măsurătoarea e contaminată direct, nu subtil.

**4. Testul care separă cele două ipoteze.**

Reia controlul mecanic, dar cumpărând la o cotație aleasă *devreme* în viața pieței — de exemplu prima cotație aflată în prima treime a intervalului dintre deschidere și `closedTime`. Dacă rata de câștig se prăbușește spre preț, artefactul e confirmat. Dacă rămâne la 76%, ai ceva mult mai interesant și trebuie măsurat pe mai multe piețe.

**5. Volumul necesar ca răspunsul să însemne ceva.**

Cincizeci de piețe cu 33 într-un singur interval nu susțin o afirmație de calibrare. Intervalul de încredere pe 25 din 33 e larg cât să includă și 0,58, și 0,89. Extinde corpusul la câteva sute de piețe din era CLOB și dă curba de calibrare cu intervale de încredere, nu doar rate punctuale.

Aici e utilă limita pe care tot tu ai găsit-o: `offset` se oprește la 2000, iar `/markets/keyset` merge mai adânc. Folosește-o.

---

## Ce nu face parte din sarcină

- **Nu repara cele trei breșe de scurgere.** Sunt documentate; repararea e decizie de dimineață.
- **Nu modifica niciun modul din `03_IMPLEMENTATION/packages/polymarket/`.**
- **Nu inventa `acquired_at` / `known_as_of`.** `null` rămâne corect, și ai procedat bine lăsându-le așa pe toate cele 620.
- **Nu construi un model.** Nu prezicem nimic în noaptea asta. Măsurăm dacă ce am măsurat deja înseamnă ceva.

---

## Reguli

- Scripturi în `scratch/`, fixture-uri și rapoarte în `07_EVALUATION/polymarket/`.
- Proveniență pe fiecare captură: URL, marcă temporală UTC, cod HTTP, număr de înregistrări, SHA-256.
- Fără autentificare. Rate limit uman; la 429 te oprești și raportezi.
- Ontologia neatinsă.
- Ramură proprie din `origin/main`.
- Anunță pe nume fiecare fișier adăugat, scripturile din `scratch/` incluse.
- Fiecare cifră din raport, găsibilă cu `grep` într-o captură.

---

## O corecție mică din livrarea precedentă

Ai raportat `OVERNIGHT_CAMPAIGN_REPORT.md` la 10.117 octeți; are 10.476. Hash-ul se potrivește cu fișierul real, deci e o dimensiune măsurată înainte de ultima scriere, nu o cifră inventată. Singura abatere din treisprezece.

Restul raportului a rezistat verificării în întregime, inclusiv cifrele pe care le-am recalculat eu din fixture-uri fără să mă uit la concluziile tale. A doua livrare consecutivă fără abatere de fond.

De asta merită sarcina asta: dacă cifrele sunt corecte, atunci cei 19 puncte procentuale sunt reali ca măsurătoare, și tocmai de aceea trebuie să știm ce anume au măsurat.

---

## Termen

**06:00.** Verific la 01:41, 02:31, 03:21, 04:11, 05:01 și 05:51 — verificările sunt pentru mine, nu praguri pentru tine. Nu împărți sarcina ca să ai ce arăta la fiecare. Un singur răspuns, întemeiat, la ora șase.

Dacă ajungi la concluzie la ora trei, spune-o la ora trei și oprește-te. Un răspuns corect devreme e mai bun decât șase ore de muncă pe el.
