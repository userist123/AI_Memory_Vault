# Recensământul populației — ce se poate stabili din datele deja capturate

> 2026-09-14 · Claude · fără rețea
> Surse: `fixtures/expanded_dataset_483markets.json` (SHA-256 `10ecd5ab…`) și `fixtures/corpus_depth_sample_500.json` (SHA-256 `1397d989…`), ambele verificate anterior față de proveniența lor.
> Tot ce urmează e calculat din aceste două fișiere. Nicio cifră nu vine dintr-un raport.

---

## 1. Constatarea principală

**Piețele pentru care a fost construit sistemul și piețele care au bandă de prețuri sunt, în datele capturate, două populații disjuncte.**

| eră | piețe | sport / esports | preț crypto | politică | macro | întrebări-eveniment |
|---|---|---|---|---|---|---|
| CLOB — **au bandă de prețuri** | 101 | 64 | 36 | **0** | **0** | 1 |
| pre-CLOB / AMM — **fără bandă** | 399 | 28 | 31 | **46** | 3 | 291 |

În era pre-CLOB se află exact genul de întrebare pentru care există consiliul de predicție, calibrarea și abstenția: *„Will Trump win the 2020 U.S. presidential election?"*, *„Will Coinbase begin publicly trading before Jan 1, 2021?"*, *„Will there be an Emergency Use Authorization granted for a COVID-19 vaccine…"*. Niciuna nu are carnet de ordine, deci niciuna nu are bandă de prețuri, deci niciuna nu poate fi backtestată.

În era CLOB, unde benzile există, eșantionul conține totaluri de baseball, spread-uri NFL, meciuri de CS2 și praguri orare pe prețul Bitcoin.

---

## 2. Cât de independent este eșantionul de 483

`event_key` grupează piețele pe joc, dar **nu** grupează grilele crypto: fiecare prag primește propria cheie (`bitcoin-above-74800-on-september-13`, `bitcoin-above-75000-on-september-13`…), deși toate se rezolvă din același preț la aceeași oră.

Colapsând grilele pe (activ, zi, oră) extras din textul întrebării:

```
483 piețe
 94 valori distincte de event_key
 63 rezultate subiacente independente
    ├── 50 jocuri sportive / esports
    └── 13 fixări orare de preț crypto — toate din aceeași zi, 13 septembrie
```

Cele mai mari grupuri:

| rezultat subiacent | piețe |
|---|---|
| Bitcoin, 13 septembrie, 6 ore distincte | 117 |
| Ethereum, 13 septembrie, 7 ore distincte | 81 |
| NFL — Jets vs. Titans, un singur meci | 56 |
| MLB — White Sox vs. Cardinals, un singur meci | 47 |
| MLB — Guardians vs. Twins, un singur meci | 25 |

**Un singur meci NFL contribuie cu 56 de piețe. O singură zi de Bitcoin, cu 117.**

---

## 3. Ce înseamnă asta pentru verdictul de calibrare

Verdictul rămâne corect: abaterea globală e **−1,08 puncte procentuale**, iar zero e în interval.

Precizia afirmată era însă supraestimată. Intervalul Wilson tratează cele 483 de piețe ca observații independente. Nu sunt.

| metodă | unitate | lățime a intervalului de 95% |
|---|---|---|
| naiv, piețe independente | 483 piețe | ±4,4 puncte |
| **bootstrap pe clustere** | **63 rezultate** | **±6,2 puncte — [−7,4, +5,0]** |

Formularea corectă nu este „piața e calibrată în ±4 puncte", ci **„pe 63 de rezultate, dominate de o zi de crypto și câteva meciuri, nu se poate respinge calibrarea, iar abaterea reală poate fi oriunde între −7 și +5 puncte"**.

Pentru un sistem al cărui întreg rost e să găsească un avantaj față de piață, un interval de ±6 puncte e mai lat decât orice avantaj realist.

---

## 4. Rezerva care contează — și de ce e nevoie de rețea

Cele 101 piețe CLOB provin din interogări `closed=true&order=id&ascending=false`: **cele mai recente piețe închise**, ordonate după identificator.

Fluxul recent e dominat de piețe generate automat, de mare frecvență — câte o grilă de 20 de praguri Bitcoin pe oră, zeci de linii pe fiecare meci. Piețele-eveniment de lungă durată există și în era CLOB, dar într-un eșantion ordonat după `id` sunt înecate numeric.

Așadar „zero piețe politice în era CLOB" înseamnă, strict, **„zero într-un eșantion de 101 ordonat după recență"**, nu „zero în era CLOB". Diferența decide dacă sistemul are subiect.

Singurul lucru care o tranșează e o interogare filtrată pe tip de piață sau pe volum, nu pe `id`. Aceea cere rețea.

---

## 5. Ce nu am putut face aici

- Nicio interogare nouă; totul vine din capturi existente.
- Clasificarea pe tip (secțiunea 1) e o regulă pe textul întrebării, nu un câmp al API-ului. Nu i-am măsurat rata de eroare pe un eșantion verificat manual; `altele` din era pre-CLOB conține cu siguranță întrebări-eveniment neprinse de regula pentru politică.
- Nu am verificat afirmația despre decimarea benzii (`interval="all"`).
