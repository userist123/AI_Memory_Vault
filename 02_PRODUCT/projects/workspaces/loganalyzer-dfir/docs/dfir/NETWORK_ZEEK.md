# Rețeaua: jurnalele Zeek (master plan 6.5)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Network/ZeekLogParser.cs`, înregistrat în `WindowsParsers.Registry`.
- **Teste:** `ZeekLogParserTests`, care trec prin registru și prin pipeline-ul real.

**Cum se lucrează:**
1. Un senzor Zeek (rulat de analist, în afara aplicației) scrie jurnalele `conn.log`, `dns.log`, `http.log`, `ssl.log`, `notice.log`.
2. Analistul le importă în caz ca probe, în format TSV clasic sau JSON pe linii (`LogAscii::use_json=T`).
3. Aplicația nu rulează Zeek și nu citește captura. Citește doar ce a scris senzorul.

**Ce se citește:**

| Jurnal | Ce intră în cronologie |
|---|---|
| `conn` | adrese, porturi, protocol, serviciu, durată, octeți, stare |
| `dns` | interogarea, tipul, răspunsurile, codul |
| `http` | gazdă, URI, metodă, user agent, cod de stare |
| `ssl` | numele serverului, versiunea, cifrul, JA3 (dacă există), validarea |
| `notice` | tipul și mesajul notificării |

- Tipul jurnalului se ia din `#path` (TSV) sau din coloane (JSON). Un tip necunoscut este numărat ca înregistrare malformată și nu produce evenimente.
- Ora `ts` (epoch UTC cu fracțiune) se păstrează cu microsecunde; textul brut rămâne pe eveniment.
- `-` și `(empty)` devin șiruri goale.
- Linie mai lungă de 64 KB, sau număr de câmpuri diferit de antet: înregistrare malformată.
- Un jurnal TSV fără linia `#close` primește un gol de probă (parțial): poate fi trunchiat.
- Un fișier `.log` obișnuit, fără antet Zeek și fără coloane Zeek, nu este revendicat și nu produce evenimente.

**Limite:** Zeek vede doar rețeaua (fără proces, fără PID), conținutul TLS nu este decodat, iar golurile de captură nu se văd din jurnale.
