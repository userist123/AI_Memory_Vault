# WP18 — Proba de utilizare cu o persoană fără pregătire IT (S6, decizia D7)

> Se rulează cu o persoană reală din organizație (ofițer de control sau ofițer de serviciu), pe o stație cu aplicația instalată și cu un cont
> propriu (card + PIN). Observatorul nu ajută și nu explică; notează doar ce s-a întâmplat. Fără această rulare, pasul S6 rămâne `PARTIAL`.
>
> Rezultatul se consemnează în tabelul de la sfârșit și în checkpoint-ul `00_GOVERNANCE/coordination/tasks/todo-claude-wp18.md`.

## Pregătire (observatorul, înainte)

- Stația are politica semnată potrivită rolului (`CONTROL` pentru scenariul A, `CSIRT` pentru B); insigna „ROL:” din antet arată rolul.
- Persoana are cont în aplicație; nivelul de limbaj este cel implicit (Simplu); nimeni nu i-a arătat aplicația înainte.
- Pentru A: profilul de proceduri și antetul unității sunt completate de administrator (altfel se notează ce lipsea).
- Pentru B: un folder cu probe (jurnale, Prefetch, SRUM) este pe un suport, adus „de la stația afectată”.
- Se cronometrează fiecare sarcină; se notează fiecare moment în care persoana se oprește mai mult de 30 de secunde sau întreabă ceva.

## Scenariul A — ofițerul de control, stație `CONTROL`

| # | Sarcina spusă persoanei (exact așa) | Reușită = | Timp | Opriri / întrebări | Reușit? |
|---|---|---|---|---|---|
| A1 | „Porniți aplicația și spuneți-mi ce rol are stația și dacă e conectată la rețea.” | citește insigna „ROL:” și modul, fără ajutor | | | |
| A2 | „Verificați această stație de la ultimul control.” | ajunge pe ecranul de rezultat (cele cinci răspunsuri) prin pașii ghidați | | | |
| A3 | „Spuneți-mi dacă există o problemă, cât de gravă e și dacă putem avea încredere în probe.” | răspunde din ecranul de rezultat, cu cuvintele lui | | | |
| A4 | „Deschideți una dintre verificările neconforme și arătați-mi dovezile.” | deschide rândul, găsește „Ce știm / Ce suspectăm / Ce nu putem demonstra” și dovezile | | | |
| A5 | „Faceți raportul pentru procesul-verbal și salvați o copie pe suportul de control.” | PDF produs, copia salvată; observă că originalul rămâne în caz | | | |

Reușită a scenariului: toate cele cinci sarcini terminate fără ajutor și fără să fi apăsat „Avansat”. Se notează fiecare termen pe care persoana l-a
întrebat („ce înseamnă …?”): el intră în glosar.

## Scenariul B — ofițerul de serviciu, stație `CSIRT`

| # | Sarcina spusă persoanei (exact așa) | Reușită = | Timp | Opriri / întrebări | Reușit? |
|---|---|---|---|---|---|
| B1 | „Primiți probele de pe acest suport.” | parcurge cei trei pași; vede ce s-a găsit și ce lipsește înainte de analiză | | | |
| B2 | „Spuneți-mi ce s-a întâmplat.” | citește cronologia simplă și lanțul incidentului | | | |
| B3 | „Este grav? Putem avea încredere în probe?” | răspunde de pe Acasă (nivel de atenție, încredere), fără să caute în „Avansat” | | | |
| B4 | „Ce ar trebui să facem acum?” | găsește „Ce fac acum?”; la o acțiune de izolare citește ce se va întâmpla înainte să confirme | | | |
| B5 | „Faceți raportul pentru conducere.” | alege „Pentru conducere” și obține PDF-ul | | | |

## Scenariul C — analistul (același caz ca B)

| # | Sarcina | Reușită = | Reușit? |
|---|---|---|---|
| C1 | „Treceți pe Expert și arătați-mi, pentru o constatare, id-ul probei, amprenta, parserul și locatorul.” | le găsește în același loc, în stratul tehnic | |

## Scenariul D — administratorul fără pregătire IT

| # | Sarcina | Reușită = | Reușit? |
|---|---|---|---|
| D1 | „Adăugați o persoană nouă și înregistrați-i cardul.” | termină din „Autentificare și conturi” fără documentație | |
| D2 | „Aduceți lista certificatelor revocate de pe acest suport.” | găsește butonul după numele lui simplu | |

## Consemnare

| Data | Persoana (funcție, fără nume) | Scenariu | Sarcini reușite | Termeni întrebați | Ce a blocat | Observator |
|---|---|---|---|---|---|---|
| | | | | | | |

Rezultatul se trece și în `todo-claude-wp18.md` (S6: `VERIFIED` numai dacă toate sarcinile scenariului au reușit; altfel `PARTIAL`, cu lista a ce a blocat).
