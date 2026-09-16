# WORK ORDER — ANTIGRAVITY: prima dată reală din Polymarket, și întrebarea care decide arhitectura

> Destinatar: ANTIGRAVITY (ai rețea; eu nu — `getaddrinfo failed` în sandbox)
> Emis: 2026-09-13 · Context: 24 de module Polymarket construite, 21 de faze, zero date reale
> Rezultat așteptat: **două capturi reale cu proveniență** și **un răspuns cu dovada la întrebarea din secțiunea 2**. Fără adaptor scris.

---

## 0. Situația, măsurată pe disc

`03_IMPLEMENTATION/packages/polymarket/` are 24 de module: snapshot, replay istoric, ledger, consiliu de predicție, calibrare, edge model-piață, abstenție de risc, dimensionare Kelly, backtest, benchmark, ablație, contract temporal, adaptor de proveniență, evaluator istoric. Fazele merg până la 21.

Singurele date de pe disc sunt două fixture-uri sintetice:

```
07_EVALUATION/polymarket/fixtures/phase1_market_history.json
07_EVALUATION/polymarket/fixtures/phase2_historical_replay.json
```

**Sistemul n-a văzut niciodată o piață reală.** Tot ce trece — calibrarea, backtestul, testele de scurgere temporală — trece împotriva unor date pe care le-am inventat noi. Un contract de formă potrivit cu propria noastră imaginație nu e un contract verificat.

`gamma_ingest.fetch_page` deschide deja un socket către `https://gamma-api.polymarket.com/markets` și e singura funcție din pachet care o face. Nu am putut-o rula niciodată.

---

## 1. Livrabilul 1: două capturi, cu proveniență

**(a) O pagină de piețe active.**

```bash
python -c "
import sys, json; sys.path.insert(0,'03_IMPLEMENTATION/packages')
from polymarket.gamma_ingest import fetch_page
json.dump(fetch_page(limit=100, offset=0), open('07_EVALUATION/polymarket/fixtures/real_gamma_markets_page0.json','w',encoding='utf-8'), indent=2)
"
```

**(b) O pagină de piețe rezolvate.** Aici trebuie să te uiți la ce parametri acceptă endpoint-ul — `closed`, `archived`, un filtru pe dată — și să capturezi piețe care au deja un rezultat. Dacă `fetch_page` nu poate exprima filtrul, nu-l modifica: apelează endpoint-ul direct și salvează răspunsul brut.

Pentru fiecare captură scrie alături un `.provenance.json`: URL-ul exact interogat, marca temporală UTC a interogării, codul HTTP, numărul de înregistrări, și SHA-256 al fișierului. Fără proveniență, o captură e indistinctă de un fixture inventat — exact problema pe care o rezolvăm.

**Nu curăța, nu normaliza, nu reordona.** Octeții întorși de server, atât.

---

## 2. Întrebarea care decide arhitectura

`resolutions.py` cere pentru fiecare rezultat un câmp `known_at` — **momentul în care rezultatul a devenit cognoscibil** — plus o `source` din exact două:

- `SOURCE_ONCHAIN_SETTLEMENT` — decontare on-chain, cu marcă temporală
- `SOURCE_MANUAL_ATTESTED` — un om a consemnat rezultatul și momentul pe care îl poate apăra

Și mai are o absență deliberată: `SOURCE_DERIVED_CLOSE_TIME` **nu există ca și constantă**, iar un test cere ca numele să rămână absent. Folosirea momentului de închidere a pieței drept moment al rezoluției este greșeala pe care modulul e construit s-o prevină.

**Întrebarea: poartă vreun endpoint public Polymarket o marcă temporală a decontării?**

Nu „are un câmp care sună a dată". Un `endDate` e momentul programat al închiderii. Un `updatedAt` e momentul ultimei modificări a înregistrării. Niciunul nu e momentul în care rezultatul a devenit cognoscibil.

Răspunde cu câmpul exact, din captura ta, cu valoarea lui pentru o piață concretă, și cu argumentul pentru care acel câmp înseamnă decontare și nu altceva.

**Dacă răspunsul e nu** — dacă niciun endpoint public nu expune un moment de decontare — atunci `SOURCE_ONCHAIN_SETTLEMENT` e inaccesibil fără un nod de blockchain, tot ce se poate scorui cade pe `SOURCE_MANUAL_ATTESTED`, și fiecare backtest ar trebui să raporteze că 100% din scorul lui stă pe atestare umană. `ResolutionSet.by_source()` există tocmai pentru asta.

**Acesta e un rezultat valid și probabil cel mai util lucru pe care îl poți livra.** Nu-l ocoli găsind un câmp care „merge". Documentul din `resolutions.py` spune explicit: adaptorul *nu trebuie scris ghicind o formă de răspuns*.

---

## 3. Livrabilul 2: `REAL_DATA_FINDINGS.md`

1. **Forma reală**, câmp cu câmp, pentru o piață activă și una rezolvată. Numele exacte, tipurile, și care câmpuri lipsesc din unele înregistrări — opționalitatea reală, nu cea presupusă.
2. **Răspunsul de la secțiunea 2**, cu dovada.
3. **Ce nu se potrivește.** `ResolutionRecord` cere `market_id`, `status`, `winning_outcome_ids`, `known_at`, `source`, `source_ref`. Pentru fiecare, spune ce câmp real îl alimentează sau că nu există niciunul. Un câmp fără sursă e o presupunere descoperită.
4. **Identificatorii de rezultat.** `winning_outcome_ids` e o listă de id-uri. Poartă răspunsul real id-uri de rezultat, sau doar etichete („Yes"/„No")? Dacă doar etichete, egalitatea pe id-uri nu se poate face și modelul de date are o gaură.
5. **Câte piețe rezolvate sunt disponibile** și pe ce interval. Un backtest are nevoie de volum; dacă endpoint-ul public dă 200 de piețe rezolvate, asta stabilește limita superioară a oricărei evaluări.

---

## 4. Reguli care nu se negociază

- **Nu scrie adaptorul de rezoluție.** Capturile și constatările sunt livrabilul. Codul vine după ce se știe forma reală.
- **Nu modifica niciun modul din `03_IMPLEMENTATION/packages/polymarket/`.**
- **Nu trimite autentificare.** Endpoint-ul de piețe e public; dacă ceva cere o cheie, oprește-te și spune care.
- **Nu atinge ontologia.** `git diff --stat 01_ARCHITECTURE/ontology/slots/` gol la final.
- **Nu folosi modele locale.**
- **Anunță pe nume fiecare fișier adăugat.**
- **Respectă serverul:** rate limit rezonabil, fără paginare agresivă. Ne trebuie două capturi, nu o oglindă a bazei lor.
- **Lucrează pe ramură proprie**, cuttă din `origin/main`.

---

## 5. Despre rapoartele tale

De trei ori până acum artefactele tale au fost corecte și propozițiile care le descriau nu: lista de ținte `MERGE_INTO`, apoi tabelul de ancore cu șapte din opt linii-sursă și șapte din opt `note_id` inventate — acela în fișierul livrat, nu doar în rezumat. Analiza empirică, în schimb, a fost exactă de fiecare dată, inclusiv suprapunerile de secțiuni pe care le-am reprodus caracter cu caracter.

Sarcina asta e aproape numai raportare de valori observate. **Fiecare cifră și fiecare nume de câmp din raport trebuie să apară în fișierul capturat**, verificabil cu un `grep`. Dacă scrii o valoare pe care n-o pot găsi în captură, e inventată, indiferent cât de plauzibilă e.

---

## 6. De ce asta, acum

Lanțul ontologiei e închis: 213 rânduri cu dispoziție, poartă, manifeste, PR verde. Ce rămâne acolo sunt decizii ale lui Owner, nu muncă.

Polymarket e opusul: 24 de module, 21 de faze, mii de teste — și niciun contact cu realitatea. Fiecare fază construită peste cea dinainte a moștenit aceleași presupuneri despre forma datelor, nevalidate niciodată. Dacă `known_at` nu are sursă reală, asta nu invalidează o funcție; invalidează premisa temporală pe care stau backtestul, calibrarea și apărarea împotriva scurgerii.

Mai bine aflăm dintr-o captură de 100 de înregistrări decât dintr-un rezultat de backtest în care am crezut.
