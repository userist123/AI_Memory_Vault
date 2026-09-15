# MANDAT — ANTIGRAVITY: ce sunt, de fapt, piețele pe care le putem atinge

> Emis: 2026-09-14, 01:00 · **Termen: 06:00**
> O singură întrebare. Nu o listă de etape.
> Mandatul precedent a fost închis în douăzeci și unu de minute și verificat integral: 338 de piețe în intervalul 0,4–0,6, 158 de câștiguri, 46,75% față de 49,84% declarat, Wilson [41,5%, 52,1%], PnL −27,89. Toate reproduse din fixture.

---

## Ce tocmai am aflat, și de ce deschide altceva

Piața era calibrată tot timpul. Abaterea reală, pe 483 de piețe, este de **minus 1,1 puncte procentuale**. Cei 19 puncte erau două mecanisme de selecție, amândouă invizibile la n=50: treizeci și nouă de piețe in-play deschise după startul meciului, cu prima cotație la 70,7% din durata lui, și opt greve Ethereum corelate pe același eveniment, rezolvate simultan.

Asta e un rezultat bun. Dar uită-te ce spune despre corpus, nu despre calibrare:

**Din cincizeci de piețe alese fără nicio intenție, treizeci și nouă erau micro-piețe de pariuri live pe meciuri de baseball, iar opt erau aceeași întrebare pusă de opt ori.** Rămân trei.

Sistemul din `03_IMPLEMENTATION/packages/polymarket/` a fost construit pentru piețe de predicție pe evenimente — consiliu de predicție, calibrare, avantaj față de piață, abstenție, dimensionare Kelly. Dacă populația accesibilă este, în covârșitoare majoritate, totaluri de baseball deschise în inning-ul șapte și grile de praguri pe prețul Ethereum, atunci sistemul nu are subiect. Nu e prost construit — e construit pentru altceva decât ce se află acolo.

Nu știm care e cazul. Nimeni nu s-a uitat.

---

## Sarcina

**Caracterizează populația de piețe accesibile. Nu câte sunt — ce sunt.**

Atât. Până la 06:00.

Și, dacă răspunsul e „corpusul accesibil nu conține destule piețe de tipul pentru care a fost construit sistemul", mandatul e îndeplinit complet. Acela ar fi cel mai important lucru aflat în toată campania.

---

## Ce face răspunsul un răspuns

**1. O taxonomie măsurată, nu intuită.**

Pe un eșantion suficient de mare din era CLOB — folosește `/markets/keyset`, pe care tot tu l-ai găsit — clasifică piețele pe categorii reale: sport pre-meci, sport in-play, prag pe preț de criptomonedă, politică, macro, altele. Clasificarea trebuie să iasă dintr-un câmp sau dintr-o regulă reproductibilă pe text, nu din citirea ta. Scrie regula, dă rata ei de eroare pe un eșantion verificat manual, și dă distribuția.

**2. Cât din corpus este o singură întrebare pusă de mai multe ori.**

Grila Ethereum a fost opt piețe pe un eveniment. Câte grupuri de genul ăsta există și ce fracțiune din corpus ocupă? Definește „același eveniment" printr-o regulă explicită — `event_key` există deja în setul tău de 483 — și raportează câte *evenimente independente* corespund celor N piețe. **Aceea este dimensiunea reală a eșantionului pentru orice evaluare**, nu numărul de piețe, iar diferența poate fi de câteva ori.

**3. Câte piețe permit o decizie înainte ca răspunsul să fie în mare parte cunoscut.**

O piață deschisă la 70% din meci nu e o piață de predicție; e un pariu live. Pentru fiecare piață, raportul dintre momentul primei cotații și viața evenimentului — nu a pieței. Câte oferă o fereastră în care un model ar avea de prezis ceva? Dacă răspunsul e 8%, spune 8%.

**4. Ce rămâne după ce le aplici pe toate trei.**

Un singur număr, cu felul cum a fost obținut: câte evenimente independente, netriviale, pre-rezultat, cu bandă de prețuri utilizabilă, sunt accesibile prin API-ul public. **Acela e corpusul.** Tot ce s-a construit în douăzeci și una de faze va fi evaluat pe el sau pe nimic.

**5. Onestitatea cifrei.**

Dacă numărul e mic, spune-l mic. Dacă e sub o sută de evenimente independente, atunci orice rezultat de backtest de aici încolo are un interval de încredere mai lat decât orice efect pe care l-am spera, și e mai bine să știm asta acum decât după ce construim un model peste el.

---

## Ce nu face parte din sarcină

- **Nu repara cele trei breșe de scurgere.** Rămân documentate.
- **Nu modifica niciun modul din `03_IMPLEMENTATION/packages/polymarket/`.**
- **Nu construi un model și nu rula un backtest.** Măsurăm subiectul, nu performanța.
- **Nu extinde corpusul doar ca să crească numărul.** O mie de piețe in-play pe baseball nu valorează mai mult decât cincizeci.

---

## Reguli

- Scripturi în `scratch/`, fixture-uri și rapoarte în `07_EVALUATION/polymarket/`.
- Proveniență pe fiecare captură: URL, marcă temporală UTC, cod HTTP, număr de înregistrări, SHA-256.
- Fără autentificare. Rate limit uman; la 429 te oprești și raportezi.
- Ontologia neatinsă.
- Ramură proprie din `origin/main`.
- Anunță pe nume fiecare fișier adăugat.
- Fiecare cifră din raport, găsibilă cu `grep` într-o captură.

---

## Despre verificare

Trei livrări consecutive în care fiecare cifră a rezistat recalculării. La ultima am refăcut independent calibrarea, intervalul Wilson, PnL-ul și gruparea Ethereum înainte să deschid raportul tău, și au coincis toate.

Asta schimbă ce e util să verific. Nu mai caut erori de aritmetică. Caut afirmații care depășesc datele — și singura din ultima livrare a fost implicită: verdictul „artefact confirmat" e corect, dar concluzia că piața e eficientă se sprijină pe un corpus despre care nu știm încă ce este. De aici mandatul.

**Termen 06:00.** Verific la 01:41, 02:31, 03:21, 04:11, 05:01, 05:51. Verificările sunt pentru mine. Dacă ajungi la un răspuns întemeiat la ora trei, oprește-te la ora trei.
