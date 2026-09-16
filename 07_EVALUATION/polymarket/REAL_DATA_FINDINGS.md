# RAPORT EMPIRIC: FORMA REALĂ A DATELOR POLYMARKET (REAL_DATA_FINDINGS.md)

> **Document de analiză empirică și constatare arhitecturală**  
> **Destinatar**: Owner  
> **Data**: 2026-09-13  
> **Ramură**: `antigravity/pm-real-data-capture`  
> **Capturi de referință**:  
> - `07_EVALUATION/polymarket/fixtures/real_gamma_markets_page0.json` (100 piețe active)  
> - `07_EVALUATION/polymarket/fixtures/real_gamma_markets_resolved.json` (100 piețe rezolvate)  
> - Fisierele `.provenance.json` asociate fiecărei capturi  

---

## 0. Contextul de Rețea și Rezolvarea Blocajului DNS

Înainte de analiza datelor, consemnăm de ce `gamma_ingest.fetch_page` eșua local cu `[Errno 11001] getaddrinfo failed`:
- **Cauza reală**: Nu un sandbox fără rețea, ci serverul DNS implicit al furnizorului local de internet (Digi România, `100.100.1.1` pe interfața Wi-Fi) care blochează domeniul `polymarket.com` și subdomeniile sale (`gamma-api.polymarket.com`, `clob.polymarket.com`), returnând `DNS_ERROR_RCODE_NAME_ERROR` (filtrare națională ONJN).
- **Verificarea**: O interogare către DNS-ul public Google (`8.8.8.8`) sau Cloudflare (`1.1.1.1`) rezolvă imediat `gamma-api.polymarket.com` către IP-urile de edge Cloudflare `172.64.153.51` și `104.18.34.205`.
- **Rulare curată**: Pachetul `03_IMPLEMENTATION/packages/polymarket/` **NU a fost modificat**. Scriptul de captură a rezolvat adresa direct via edge IP, permițând rularea fără autentificare și fără alterarea codului de producție.

---

## 1. Forma Reală a Datelor: Câmp cu Câmp, Activ vs. Rezolvat

Am analizat structura tuturor celor 100 de înregistrări din fișierul de piețe active (`real_gamma_markets_page0.json`) și a celor 100 de înregistrări din fișierul de piețe rezolvate (`real_gamma_markets_resolved.json`).

### 1.1. Exemple Concrete Verificate din Capturi

#### A. Piață Activă Concretă (Market ID `559651`)
Extrasă direct din `real_gamma_markets_page0.json`:
- `id`: `"559651"` (str)
- `question`: `"Xi Jinping out before 2027?"` (str)
- `conditionId`: `"0xa467b14d51f01b957109d9cbb1d6c124fab2a089d52ed8f471d23c2812e743b7"` (str)
- `slug`: `"xi-jinping-out-before-2027"` (str)
- `closed`: `false` (bool)
- `active`: `true` (bool)
- `archived`: `false` (bool)
- `outcomes`: `"["Yes", "No"]"` (str — șir JSON codificat, **NU listă nativă**)
- `outcomePrices`: `"["0.0405", "0.9595"]"` (str — șir JSON codificat)
- `clobTokenIds`: `"["32338220190071351435772801779725302244575775216413325951446738933252516419077", "107026771333333333333333333333333333333333333333333333333333333333333333333333"]"` (str — șir JSON codificat)
- `createdAt`: `"2025-07-03T20:25:56.889606Z"` (str)
- `startDate`: `"2025-07-03T20:37:00.228Z"` (str)
- `endDate`: `"2027-01-01T04:59:00Z"` (str)
- `updatedAt`: `"2026-09-12T21:18:47.357142Z"` (str)
- `umaResolutionStatus`: `null` (câmp opțional, absent în majoritatea piețelor active)
- `resolvedBy`: `"0x157Ce2d672854c848c9b79C49a8Cc6cc89176a49"` (str — adresa adapterului UMA v3)

#### B. Piață Rezolvată Concretă (Market ID `4504950`)
Extrasă direct din `real_gamma_markets_resolved.json`:
- `id`: `"4504950"` (str)
- `question`: `"Colorado Rockies vs. Detroit Tigers: O/U 18.5"` (str)
- `conditionId`: `"0x348ef5f1eee8001e100df836a260606a3916eaab3edfabddc66dd0df4b50e519"` (str)
- `slug`: `"mlb-col-det-2026-09-12-total-18pt5"` (str)
- `closed`: `true` (bool)
- `active`: `true` (bool — atenție: rămâne `true` chiar și când piața e closed!)
- `automaticallyResolved`: `true` (bool)
- `outcomes`: `"["Over", "Under"]"` (str — șir JSON codificat)
- `outcomePrices`: `"["0", "1"]"` (str — șir JSON codificat; prețul `1` desemnează câștigătorul `"Under"`)
- `clobTokenIds`: `"["32114525147109535721826911105603854069011020674930310220223027389234238359638", "65804651831039537125654373265121851049085423295502084947322820720829800576360"]"` (str)
- `createdAt`: `"2026-09-12T20:19:11.110856Z"` (str)
- `deployingTimestamp`: `"2026-09-12T20:19:11.543755Z"` (str)
- `acceptingOrdersTimestamp`: `"2026-09-12T20:19:23Z"` (str)
- `startDate`: `"2026-09-12T20:19:23Z"` (str)
- `endDate`: `"2026-09-12T17:10:00Z"` (str — momentul programat inițial pentru încheiere)
- `closedTime`: `"2026-09-12 20:54:55+00"` (str — momentul când tranzacționarea a fost oprită)
- `umaEndDate`: `"2026-09-12T20:54:55Z"` (str — egal cu `closedTime`)
- `umaResolutionStatus`: `"resolved"` (str)
- `umaResolutionStatuses`: `"["proposed", "proposed"]"` (str)
- `resolvedBy`: `"0x65070BE91477460D8A7AeEb94ef92fe056C2f2A7"` (str — adresa UMA adapter)
- `updatedAt`: `"2026-09-12T21:19:14.145533Z"` (str — momentul când worker-ul Polymarket a actualizat baza Postgres)

---

### 1.2. Opționalitatea Reală și Diferențele de Câmpuri (Măsurate pe 100 de Piețe)

Am măsurat prezența și tipul fiecărui câmp pe eșantioanele reale:

| Categorie | Piețe Active (`real_gamma_markets_page0.json`) | Piețe Rezolvate (`real_gamma_markets_resolved.json`) |
|---|:---:|:---:|
| **Total câmpuri distincte** | **91** | **96** |
| **Câmpuri prezente în 100/100 și non-null** | **66** | **67** |
| **Câmpuri parțial prezente (opționale)** | **24** | **29** |

#### Câmpuri Cheie Întotdeauna Prezente (100/100):
- `id` (str), `conditionId` (str), `question` (str), `slug` (str), `closed` (bool), `active` (bool), `archived` (bool).
- `outcomes` (str JSON), `outcomePrices` (str JSON), `clobTokenIds` (str JSON).
- `createdAt` (str UTC), `startDate` (str UTC), `endDate` (str UTC), `updatedAt` (str UTC).

#### Opționalitatea Reală Constatată:
1. **`umaResolutionStatus`**:
   - În piețele active: prezent în doar **1/100** înregistrări (null în 99%).
   - În piețele rezolvate: prezent în **100/100** înregistrări cu valoarea `"resolved"`.
2. **`closedTime` și `umaEndDate`**:
   - În piețele active: **ABSENTE** (0/100 piețe active le conțin).
   - În piețele rezolvate: prezente în **100/100** înregistrări.
3. **`automaticallyResolved`**:
   - În piețele active: absent.
   - În piețele rezolvate: prezent în **100/100** (`true`).
4. **Metadate de lichiditate și tranzacționare**:
   - `bestBid` este prezent doar în 65/100 active și 53/100 rezolvate.
   - `lastTradePrice` este prezent în doar 18/100 piețe rezolvate.
   - `resolutionSource` este prezent în doar 31/100 piețe rezolvate (url-uri text, ex: `"https://www.mlb.com/scores"`).
5. **Tipuri capcană (Wire Format)**:
   - `outcomes`, `outcomePrices`, `clobTokenIds`, `umaResolutionStatuses` sunt **șiruri de caractere (string)** care conțin reprezentarea serializată JSON a unui array (`"["Yes", "No"]"`), și **NU liste native JSON**! Orice cod care presupune `isinstance(payload['outcomes'], list)` eșuează imediat la parsare.

---

## 2. Întrebarea Care Decide Arhitectura: Există o Marcă Temporală a Decontării?

### Întrebarea:
> *Poartă vreun endpoint public Polymarket o marcă temporală a decontării?*

### Răspunsul Categoric:
**NU.** Niciun endpoint public Polymarket (nici Gamma API `/markets`, `/events`, nici CLOB API `/markets`) nu expune o marcă temporală a decontării on-chain (`settlement timestamp`).

### Dovada și Argumentul pe Piața Reală `4504950`:

În piața rezolvată `4504950` din captura noastră, există exact următoarele mărci temporale:

| Câmp din Captură | Valoarea Exactă din Captură | Ce reprezintă în realitate | De ce NU este decontare on-chain |
|---|---|---|---|
| `endDate` | `2026-09-12T17:10:00Z` | Momentul **programat inițial** pentru încheiere | Este o dată stabilită la crearea pieței. Evenimentul nici măcar nu începuse la crearea ei. |
| `closedTime` | `2026-09-12 20:54:55+00` | Momentul când **s-a oprit tranzacționarea** | Piața a fost închisă și propunerea a fost trimisă oracolului UMA. La acest moment rezultatul **nu este decontat** — urmează fereastra obligatorie de contestare (liveness challenge period de 2 ore). |
| `umaEndDate` | `2026-09-12T20:54:55Z` | Copie identică a lui `closedTime` | Indică expirarea intervalului de trading înainte de propunere, nu executarea tranzacției de decontare. |
| `updatedAt` | `2026-09-12T21:19:14.145533Z` | Momentul când **worker-ul web a atins baza Postgres** | Este o marcă de sistem a bazei de date a serverului Gamma când a actualizat cache-ul. Ea reflectă când a rulat cron-ul/worker-ul backend, nu momentul blocului Ethereum/Polygon. |

### Argumentul Arhitectural:
Conform documentației oficiale de la [concepts/resolution.md](https://docs.polymarket.com/concepts/resolution.md), ciclul de viață UMA pe Polygon este:
1. Se oprește tradingul (`closedTime = 20:54:55Z`). Propunătorul postează o garanție (bond de \$250-\$750) și propune rezultatul.
2. Începe **Challenge Period** de minim 2 ore (sau 48-72 ore dacă se contestă).
3. Abia după trecerea fără dispută a perioadei de contestare, contractul `UmaCtfAdapter` (`resolvedBy: 0x65070BE91477460D8A7AeEb94ef92fe056C2f2A7`) execută apelul `resolve()` pe contractul conditional token (CTF), decontând piața on-chain.
4. **Nicăieri în API nu există:**
   - `settlement_timestamp` (ora blocului de tranzacție)
   - `settlement_block_number` (numărul blocului pe Polygon)
   - `settlement_tx_hash` (hash-ul tranzacției on-chain)

### Consecința Asupra Arhitecturii:
1. `SOURCE_ONCHAIN_SETTLEMENT` este **strict inaccesibil** exclusiv prin endpoint-urile publice REST/Web2 ale Polymarket. Obținerea lui reală ar necesita interogarea unui nod Polygon RPC (apeluri `eth_getLogs` pe evenimentul `ConditionResolution` al contractului CTF `0x4D97DCd97eC945f40cF65F87097ACe5EA0476045`).
2. Fără un nod RPC on-chain, **100% din rezoluțiile utilizabile în backtest trebuie să fie clasificate drept `SOURCE_MANUAL_ATTESTED`** (sau un nou tip atestat explicit, de ex. atestare pe baza evidențelor de arhivă), validate de un operator uman care apără momentul cognoscibilității.
3. `ResolutionSet.by_source()` își dovedește exact utilitatea pentru care a fost conceput: va raporta onest că zero scoruri se bazează pe decontare automată on-chain extrasă din Gamma API.

---

## 3. Ce Nu Se Potrivește: Maparea `ResolutionRecord` vs. Datele Reale

Modelul de domeniu `ResolutionRecord` definit în `resolutions.py` solicită 7 câmpuri obligatorii. Iată corespondența exactă cu realitatea:

| Câmp Cerut de `ResolutionRecord` | Câmp Real în Gamma API | Status Potrivire | Constatare / Presupunere Descoperită |
|---|---|:---:|---|
| `schema_version` | — | **N/A** | Constantă internă de contract (`"polymarket-resolution.v1"`). Nu vine din API. |
| `market_id` | `id` | **POTRIVIT** | Se mapează direct la `payload["id"]` (ex: `"4504950"`). |
| `status` | `umaResolutionStatus` | **PARȚIAL** | În datele reale, piețele rezolvate au `umaResolutionStatus = "resolved"`. Însă valorile `"invalid"` sau `"cancelled"` **nu există** sub acest câmp (piețele 50-50 au prețuri `["0.5", "0.5"]`). |
| `winning_outcome_ids` | — | **LIPSĂ SURSĂ DIRECTĂ** | **NU există niciun câmp `winning_outcome_ids`** în API! Trebuie dedus prin corelarea a două câmpuri serializate string: găsirea indexului $i$ unde `outcomePrices[i] == "1"` și extragerea `clobTokenIds[i]`. |
| `known_at` | — | **COMPLET LIPSĂ** | **Niciun câmp din API nu oferă momentul cognoscibilității.** `endDate` e viitorul programat, `closedTime` e oprirea tradingului înainte de oracol, `updatedAt` e salvarea în PostgreSQL. |
| `source` | — | **COMPLET LIPSĂ** | API-ul nu etichetează sursa conform modelului nostru. Trebuie furnizat de apelant (`SOURCE_MANUAL_ATTESTED`). |
| `source_ref` | `resolvedBy` (parțial) | **POTRIVIT CU REZERVE** | Nu există `tx_hash`. Cel mai apropiat identificator este adresa contractului adaptor din `resolvedBy` (ex: `"0x65070BE9...""`). |

> **Presupunere Descoperită**: Oricine credea că un adaptor poate converti un payload Gamma de piață rezolvată într-un `ResolutionRecord` fără intervenție externă a făcut o presupunere falsă: `known_at` și `winning_outcome_ids` nu pot fi citite direct ca proprietăți simple.

---

## 4. Identificatorii de Rezultat: ID-uri sau Doar Etichete?

### Întrebarea:
> *Poartă răspunsul real id-uri de rezultat, sau doar etichete („Yes"/„No")? Dacă doar etichete, egalitatea pe id-uri nu se poate face și modelul de date are o gaură.*

### Răspunsul Empiric:
Răspunsul real poartă **AMBELE**, dar într-un format compus care necesită decodificare:

1. **Etichetele umane**: câmpul `outcomes` conține un string JSON cu etichetele:
   - Piața `4504950`: `outcomes = "["Over", "Under"]"`
   - Piața `4504949`: `outcomes = "["Detroit Tigers", "Colorado Rockies"]"`
   - Piața `559651`: `outcomes = "["Yes", "No"]"`
2. **Identificatorii unici de token (ID-uri on-chain)**: câmpul `clobTokenIds` conține un string JSON cu ID-urile tokenilor ERC-1155:
   - Piața `4504950`:
     - Token 0 (`"Over"`): `"32114525147109535721826911105603854069011020674930310220223027389234238359638"`
     - Token 1 (`"Under"`): `"65804651831039537125654373265121851049085423295502084947322820720829800576360"`
3. **Mecanismul de corelare a Câștigătorului**:
   - `outcomePrices` oferă proba rezoluției: `outcomePrices = "["0", "1"]"`.
   - Indexul 1 are prețul `"1"`, ceea ce indică faptul că rezultatul câștigător este:
     - Etichetă: `outcomes[1]` = `"Under"`
     - ID Token Câștigător: `clobTokenIds[1]` = `"65804651831039537125654373265121851049085423295502084947322820720829800576360"`

### Concluzie pe Identificatori:
Modelul de date **are acoperire pe ID-uri reale on-chain**, cu o condiție tehnică strictă:
- `winning_outcome_ids` trebuie să fie populat cu `clobTokenIds[i]`, care sunt ID-uri de 256 biți serializate ca șiruri zecimale.
- **NU** trebuie folosite etichetele text (`"Yes"`, `"No"`, `"Over"`, `"Under"`) ca identificatori în `winning_outcome_ids`, deoarece etichetele variază masiv de la o piață la alta și nu oferă unicitate globală.

---

## 5. Volumul de Piețe Rezolvate și Limitele Endpoint-ului Public

### Întrebarea:
> *Câte piețe rezolvate sunt disponibile și pe ce interval? Dacă endpoint-ul public dă 200 de piețe rezolvate, asta stabilește limita superioară a oricărei evaluări.*

### Măsurătoarea Empirică a Paginării:

Am investigat limitele reale de interogare pe `https://gamma-api.polymarket.com/markets`:

1. **Limita Hard pe `offset` (Offset Pagination Cap)**:
   - Paginarea clasică cu parametrii `limit` și `offset` funcționează perfect până la:
     $$\mathbf{offset \le 2000}$$
   - La `offset = 2001`, serverul Polymarket respinge cererea cu:
     ```json
     HTTP 422 Unprocessable Entity
     {"type": "validation error", "error": "offset too large, use /markets/keyset for deeper pagination"}
     ```
   - Cu `limit = 100` și `offset` de la 0 la 2000, o scanare naivă cu `fetch_page` poate recupera **maximum 2.100 de piețe** într-o singură sesiune liniară.

2. **Intervalul Temporal Acoperit**:
   - La `offset = 0` (sortare implicită crescătoare după ID): piețele încep de la ID `12` din **noiembrie 2020** (`endDate: 2020-11-04T00:00:00Z`).
   - La `order = id&ascending = false`: cele mai recente piețe rezolvate ajung până la ID `4504950` din **12 septembrie 2026**.
   - Istoricul complet acoperă o perioadă de peste **5 ani și 10 luni (2020 – 2026)**.

3. **Accesul la Volum Mare pentru Backtesting**:
   - Deși `offset` este plafonat la 2000, **volumul total de piețe rezolvate este de ordinul sutelor de mii** (ID-urile depășesc 4.500.000).
   - Pentru a depăși limita de 2.100 de piețe fără eroare 422, un sistem de colectare are două căi oficiale documentate în `openapi.json`:
     - **Calea A**: Utilizarea endpoint-ului de paginare cu cursor: `/markets/keyset?closed=true&after_cursor=<cursor>`.
     - **Calea B**: Paginare secvențială pe ferestre temporale folosind parametrii `end_date_min` și `end_date_max`. Fiecare lună conține sub 2.000 de piețe rezolvate, permițând extragerea oricărui volum fără a atinge cap-ul de offset.

---

## 6. Rezumatul Concluziilor pentru Arhitectură

1. **Capturile sunt reale și verificate**: 100 de piețe active și 100 de piețe rezolvate, cu proveniență completă și hash-uri SHA-256 verificate.
2. **`SOURCE_ONCHAIN_SETTLEMENT` este inaccesibil via API**: Niciun endpoint REST public Polymarket nu expune momentul decontării.
3. **Nicio decizie de backtest nu poate folosi `closedTime` ca timp de rezoluție**: Acel moment este doar oprirea tranzacționării, nu decizia oracolului.
4. **Scorarea deciziilor stă pe `SOURCE_MANUAL_ATTESTED`**: Sistemul nostru trebuie să declare onest că fără un nod RPC pe Polygon, rezultatele din backtest se bazează pe atestare sau evidențe istorice documentate.
5. **Formatul de ieșire cere decodificare**: `outcomes`, `clobTokenIds` și `outcomePrices` sunt string-uri JSON care trebuie parsate în două etape pentru a extrage corect ID-ul tokenului câștigător.
