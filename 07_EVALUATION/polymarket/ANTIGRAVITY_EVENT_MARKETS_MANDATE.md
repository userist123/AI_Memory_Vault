# MANDAT — ANTIGRAVITY: există piețe-eveniment cu bandă de prețuri?

> Emis: 2026-09-14, 20:50 · **Termen: 23:00** · **Înlocuiește** `ANTIGRAVITY_POPULATION_MANDATE.md`
> O singură întrebare, care cere rețea. Restul recensământului e deja făcut din datele de pe disc.
> Dacă ajungi la un răspuns întemeiat înainte de termen, oprește-te și raportează atunci.

---

## Ce s-a stabilit fără tine

`POPULATION_CENSUS_FROM_DISK.md`, calculat din capturile tale:

- În eșantionul **CLOB** (101 piețe, cu bandă de prețuri): 64 sport/esports, 36 praguri de preț crypto, **zero politică, zero macro**.
- În eșantionul **pre-CLOB** (399 piețe, fără bandă): 46 politică, 291 alte întrebări-eveniment — *Trump 2020, IPO-ul Coinbase, autorizarea vaccinului COVID*.
- Cele 483 de piețe din setul extins sunt **63 de rezultate independente**: 50 de meciuri și 13 fixări orare de preț, **toate crypto din aceeași zi**. Un meci NFL dă 56 de piețe; o zi de Bitcoin, 117.
- Calibrarea rămâne nerespinsă, dar intervalul pe clustere e **[−7,4, +5,0] puncte**, nu ±4,4.

Piețele pentru care a fost construit sistemul și piețele care pot fi backtestate par a fi populații disjuncte.

**„Par" e cuvântul care contează.** Eșantionul CLOB e ordonat după `id` — cele mai recente piețe închise — iar fluxul recent e înecat de grile generate automat. Piețele-eveniment de lungă durată pot exista în era CLOB și să fie pur și simplu în minoritate numerică.

---

## Sarcina

**Stabilește dacă există, în era CLOB, piețe-eveniment rezolvate — politică, macro, afaceri, știință, geopolitică — cu bandă de prețuri utilizabilă. Și câte, în rezultate independente.**

Asta e tot.

---

## Cum

1. **Nu eșantiona după `id`.** Tocmai asta a produs rezerva. Folosește un filtru pe tip — Gamma expune etichete (`tag`) și evenimente (`/events`) — sau ordonează după volum. Scrie exact ce parametri ai folosit și de ce.
2. **Numără rezultate independente, nu piețe.** Grupează grilele și variantele aceleiași întrebări. Regula de grupare, explicit.
3. **Verifică banda pe fiecare.** O piață-eveniment fără istoric de prețuri nu schimbă nimic.
4. **Dă numărul final**: câte rezultate independente, în era CLOB, de tip eveniment, cu bandă nevidă. Cu intervalul de timp pe care îl acoperă.

---

## Ce răspuns închide mandatul

Oricare dintre ele, cu dovada:

- **„Există N rezultate independente de acest tip"**, cu lista lor — și atunci sistemul are un subiect de mărimea N.
- **„Practic nu există"** — și atunci tot ce s-a construit pentru predicție pe evenimente nu poate fi evaluat pe date Polymarket reale, iar asta e cel mai important lucru aflat în toată campania.

Un număr mic nu e un eșec. Un număr umflat de piețe care sunt de fapt aceeași întrebare este.

---

## Reguli

- **Anunță doar fișierele care există pe disc când raportezi.** Din ultimele două livrări, niciunul din cele 33 de scripturi anunțate nu se află în acest checkout. Ieșirile tale s-au verificat până la ultimul octet de fiecare dată — dar o listă de instrumente care nu există nu declară nimic. Dacă rulezi într-un mediu separat, spune-o, și pune scripturile relevante lângă fixture, sub `07_EVALUATION/polymarket/`, unde nu sunt ignorate de git.
- **Proveniența trebuie să conțină toți parametrii interogării**, inclusiv `interval` și `fidelity` la istoricul de prețuri. Setul de 483 înregistrează endpoint-ul, dar nu intervalul — iar dacă decimarea pe care ai raportat-o e reală, câmpul `entry_price` înseamnă ceva mai îngust decât numele lui.
- Fără autentificare. Rate limit uman.
- Niciun modul din `03_IMPLEMENTATION/packages/polymarket/` modificat; ontologia neatinsă.
- Ramură proprie din `origin/main`.
- Fiecare cifră din raport, găsibilă cu `grep` într-o captură.
