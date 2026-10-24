# PROMPT WP18 — Două roluri de stație (Control PIC pe air-gap / Sprijin CSIRT) și interfață pentru utilizatori netehnici

> **Ce este acest fișier.** Promptul complet, gata de dat unui agent (Claude Code, Antigravity sau Codex), pentru pachetul de lucru WP18.
> Este un program de lucru mare, fără termen, cu o singură țintă: aplicația `LogAnalyzer` își alege singură **rolul de stație**
> după calculatorul sau serverul pe care rulează și arată **butoane și date pe înțelesul unui utilizator fără pregătire tehnică**,
> fără să ascundă vreo dovadă, vreun gol de probă sau vreo stare NEDETERMINAT.
>
> Stare: **aprobat de proprietar la 2026-10-10** (deciziile din §11). Nu s-a modificat cod. Faptele despre codul existent de mai jos sunt `DOCUMENT_VERIFIED` pe
> `docs/dfir/*.md` și `README.md` de pe `main @ 21da5bbf2`; unde am citit cod, scrie `CODE_VERIFIED` cu calea. Numărul WP18 este
> următorul liber după WP17 din coada `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.

---

## 0. Cum folosești promptul

1. Copiază tot ce urmează după linia `=== ÎNCEPUT PROMPT ===` într-o sesiune nouă de agent, cu depozitul deschis la rădăcina vault-ului.
2. Agentul lucrează pe un branch `loganalyzer/wp18-station-roles`, un PR per pas (§8), checkpoint în
   `00_GOVERNANCE/coordination/tasks/todo-<agent>-wp18.md` la fiecare milestone.
3. Deciziile proprietarului sunt în §11. Agentul nu le redeschide; ce nu este acoperit acolo se pune ca întrebare o singură dată,
   grupat, la început.

---

=== ÎNCEPUT PROMPT ===

# WP18 — LogAnalyzer: roluri de stație și interfață pentru utilizatori netehnici

Ești un agent de inginerie care lucrează în vault-ul `AI_Memory_Vault_CODEX_READY`, în proiectul
`02_PRODUCT/projects/workspaces/loganalyzer-dfir/` (aplicație WPF, .NET 10, Windows). Respecți `CLAUDE.md`, `AGENTS.md`,
`00_GOVERNANCE/VAULT_STATE.md` și protocolul de checkpoint din `00_GOVERNANCE/coordination/tasks/README.md`. Lucrezi în bucla
`UNDERSTAND → ROUTE → RETRIEVE → PLAN → EXECUTE → VERIFY → REVIEW → RECORD`. Nu declari nimic DONE fără dovadă proaspătă de verificare.

## 1. Misiunea, în două propoziții

Aplicația trebuie să funcționeze **în două roluri de stație**, alese **după mașina pe care rulează** și nu de utilizator:
**„Stație de control”** pentru controalele pe linia protecției informațiilor clasificate (PIC) pe stații izolate (air-gap), și
**„Stație de sprijin CSIRT”** pentru centrul de răspuns la incidente de securitate cibernetică. În ambele roluri, interfața trebuie
să fie folosibilă de o persoană fără cunoștințe tehnice (ofițer de control, ofițer de serviciu, responsabil de structură), păstrând
în același timp accesul complet la dovezi și la detaliul tehnic pentru analist.

Principiul-cheie, preluat din contractul UX: **nu simplifica dovada; simplifică prezentarea ei.**

Clarificare a proprietarului (2026-10-10): „stație” înseamnă **PC** (laptop sau desktop; un server al centrului este tot un PC din punctul de
vedere al aplicației). „Utilizator netehnic” **nu** înseamnă „operator”: și **administratorii aplicației pot fi oameni fără nicio legătură cu IT-ul**.
Interfața simplă este deci pentru toate conturile, inclusiv pentru ecranele de administrare (conturi, carduri, CA/CRL, politici).

## 2. Citește întâi, în această ordine (nu reconstrui ce există)

| # | Fișier (relativ la workspace) | Ce iei de acolo |
|---|---|---|
| 1 | `README.md` | ce face aplicația azi: filele Control stație (tab 14), Izolare procese (13), Domeniu/e-mail (15), Investigație completă (16), Acasă (22) |
| 2 | `docs/dfir/LOGANALYZER_PRODUCT_UX_CONTRACT.md` | cele 5 straturi, navigarea pe scopuri, cardul de constatare, „Ce știm / Ce suspectăm / Ce nu putem demonstra”, tabelul de traduceri, accesibilitate |
| 3 | `docs/dfir/LOGANALYZER_PROGRAM_REQUIREMENTS.md` | contractul de constatare, cele 10 stări standard, raportare, răspuns controlat |
| 4 | `docs/dfir/EDITIONS.md` | P1 clasificat = executabil separat fără rețea/AI/acțiuni pe gazdă; P2/P3 = o aplicație cu mod ales de **politica semnată** `%ProgramData%\LogAnalyzer\LogAnalyzer.policy` (ECDSA P-256, fail-closed pe air-gap, versiune monotonă, `audience` = numele mașinii) |
| 5 | `docs/dfir/AUTHENTICATION.md` | conturi: **administrator global** și **operator**; card + PIN; administratorul principal și cu parolă |
| 6 | `docs/dfir/CASE_HOME.md` | agregatorul Acasă (nu există stare SAFE), matricea de acoperire, cele 3 intenții de pe Acasă |
| 7 | `docs/dfir/FINDING_CARD.md`, `docs/dfir/FINDING_CONTRACT.md` | cardul de constatare, butoanele `DE CE? / ARATĂ DOVEZILE / VERIFICĂ / CE FAC ACUM?`, nivelurile de dovadă |
| 8 | `docs/dfir/LESSONS_LEARNED_MAPPING.md` §3 | „Control stație”, „Domeniu/utilizator” și „E-mail” produc `ControlCheck`/`ActionEntry`, **nu** `Finding`; au al șaptelea vocabular de stări (`Conform/Neconform/DeVerificat/Nedeterminat`) și dau CONFORM pe absența evenimentelor — bază de reutilizat, nu de refăcut |
| 9 | `docs/dfir/HG585_ACCREDITATION_REQUIREMENTS.md` (rezumat + §1.1) | cele trei profiluri P1/P2/P3, blocajele 2 (clasificare și marcaj), 3 (registru de acces), 7 (roluri, regula celor doi) |
| 10 | `docs/dfir/POLICY_ENGINE.md`, `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8 | deciziile proprietarului 1–34; nu le contrazici |
| 11 | `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md` | coada de execuție; WP15 (profil de proceduri) și WP14 (registru de medii, utilizatori/roluri + autorizații) sunt îmbinate; WP13, WP8, WP9, WP10, WP16, WP17 sunt deschise |
| 12 | `00_GOVERNANCE/coordination/tasks/todo-claude-orchestrator.md`, `todo-claude-wp6a.md` | starea curentă, regulile orchestratorului |

Cod de inspectat înainte de a scrie (CODE_VERIFIED că există la 2026-10-10): `LogAnalyzer.App/ViewModels/HomeViewModel.cs`,
`StationControlViewModel.cs`, `ProcedureProfileViewModel.cs`, `MainViewModel.cs` (`SelectedTabIndex`, Acasă = tab 22),
`LogAnalyzer.App/Edition/Unclassified/` (compoziția neclasificată, cheia publică a politicii), `LogAnalyzer.App.Classified/Edition/`,
`LogAnalyzer.Core/Services/Edition/`, `LogAnalyzer.Dfir.Windows/Audit/ControlEvaluator.cs`, `LogAnalyzer.Dfir.Core/Home/HomeSummary.cs`.

Înainte de orice strat nou, aplică regula producție-consumator din `CLAUDE.md`:
`grep -rl "<modul>" --include='*.cs' . | grep -v "Tests"`. Dacă rezultatul e gol, componenta nu e integrată; nu construi peste ea.

## 3. Vocabular: patru axe care nu se amestecă

Aplicația are deja trei axe. WP18 adaugă a patra. Le ții separate în cod, în UI și în documentație:

| Axă | Valori | Cine decide | Există azi |
|---|---|---|---|
| **Ediție** | P1 clasificat / P2-P3 neclasificat | build-ul (executabil separat) | da (WP-ED) |
| **Mod** | `airgapped` / `connected` | politica semnată (P2/P3); P1 mereu air-gap | da (WP-ED) |
| **Rol de cont** | administrator global / operator | administratorul, la crearea contului | da (WP-AUTH) |
| **Rol de stație** (NOU) | `CONTROL` / `CSIRT` | **PC-ul**, prin politica semnată; P1 mereu `CONTROL` | **nu** — WP18 |

Reguli:
- Rolul de stație **nu lărgește niciodată** capabilitățile date de ediție și mod. `CSIRT` pe un P2 air-gap rămâne fără rețea. `CSIRT` nu există în P1.
- Rolul de stație **nu înlocuiește** rolul de cont. Un operator pe o stație `CSIRT` tot nu administrează conturi.
- **WP18 nu adaugă roluri de cont.** Rămân exact administrator global și operator (decizia proprietarului, 2026-10-10); orice rol nou
  („ofițer de securitate”, regula celor doi) este amânat explicit pentru un pachet ulterior.
- Rolul de stație **nu se schimbă la rulare** și nu are comutator în UI. Se schimbă doar printr-o politică semnată nouă, cu `version` mai mare.

## 4. Cele două roluri

### 4.1 `CONTROL` — „Stație de control” (controale pe linia PIC, air-gap)

Cine o folosește: ofițerul de control / responsabilul cu protecția informațiilor clasificate, adesea fără pregătire IT. Unde rulează:
laptopul de control adus la stația izolată, sau chiar stația izolată (P1 clasificat, sau P2 neclasificat izolat). Rețea: niciodată.

Ce trebuie să facă, cu cuvintele utilizatorului:
1. **„Verifică această stație”** — rulează controlul de conformitate (bază: fila Control stație, `ControlEvaluator`), pe o perioadă aleasă
   simplu (de la ultimul control / 30 zile / 90 zile / interval), cu profilul de proceduri (WP15) și registrul de medii (WP14) ca referință.
2. **„Verifică un suport (USB, CD/DVD)”** — ce suporturi au fost conectate, dacă sunt în registru, dacă transferurile respectă zonele (WP14).
3. **„Cine a lucrat și când”** — conturi, autentificări, sesiuni privilegiate, în afara orelor din profil, cu listă pe utilizator.
4. **„A fost stația conectată la vreo rețea?”** — NetworkList / WLAN / NetworkProfile, fiecare apariție cu dovada ei.
5. **„Au fost șterse jurnale sau schimbată ora?”** — pe baza verificărilor existente, prezentate în limbaj simplu.
6. **„Deschide un control anterior”** și **„Compară cu controlul anterior”** — ce s-a schimbat de la ultima vizită (diferență, nu doar două rapoarte).
7. **„Raport pentru proces-verbal”** — PDF cu antet (unitate, stație, Hardware ID, perioadă, cine a făcut controlul, rol, nivel de marcaj),
   tabel CONFORM / NECONFORM / DE VERIFICAT / NEDETERMINAT, fiecare rând cu „pe ce se bazează”, golurile de probă pe prima pagină,
   loc de semnături. JSON lângă PDF, ambele cu SHA-256 și custodie (deja există `Control/CONTROL_<dată>/`).

Invariant specific: **absența unui eveniment nu produce CONFORM**. Rândurile care azi dau CONFORM pe absență (vezi `LESSONS_LEARNED_MAPPING.md`
§3, exemple D01, A05) se prezintă ca „Nimic găsit în sursele citite · acoperire: <nivel>”, iar starea rămâne DE VERIFICAT sau NEDETERMINAT după
caz. Nu schimba semantica `ControlEvaluator` în WP18 (este revendicată de WP1b/WP14/WP15); schimbă **prezentarea** și adaugă testul care fixează regula.

### 4.2 `CSIRT` — „Stație de sprijin răspuns la incidente”

Cine o folosește: ofițerul de serviciu al centrului (netehnic, decide dacă escaladează) și analistul (tehnic). Unde rulează: serverul sau
stația centrului (P3 conectat sau P2 izolat, după politica semnată).

Ce trebuie să facă, cu cuvintele utilizatorului:
1. **„Primește probe de la o stație”** — import dintr-un folder/suport adus de la stația afectată (EVTX, Prefetch, SRUM, PCAPNG, export de
   control din rolul `CONTROL`), cu verificare de integritate la intrare și afișarea clară a ce **lipsește**.
2. **„Ce s-a întâmplat?”** — cronologia simplă (`08:42 Word a pornit PowerShell`), lanțul incidentului, cardurile de constatare.
3. **„Este grav? Este sigur?”** — nivelul de atenție de pe Acasă (fără SAFE), încrederea în probe, ce nu s-a putut verifica.
4. **„Ce fac acum?”** — recomandări pe pași, cu diferența clară între „recomandare” și „acțiune aplicată și verificată” (`REQUEST → AUTHORIZE → APPLY → VERIFY`).
   Acțiunile pe gazdă (izolare, firewall) rămân doar unde ediția le compilează (P2/P3, `LogAnalyzer.Response`).
5. **„Raport de incident”** — pentru conducere, pentru IT, pentru investigație forensică, pentru audit (profilurile din WP10); întrebarea de
   intrare este „Pentru cine e raportul?”, nu „ce format vrei?”.
6. **„Trimite în Memory Vault”** — doar propunere, doar după verificare, exact ca azi (decizia 10).
7. **„Cazuri deschise”** — lista cazurilor cu stare, nivel de atenție, ultima acțiune, cine lucrează pe el.

## 5. Cum se decide rolul după mașină

- Extinde politica semnată (`LogAnalyzer.policy`) cu câmpul `role` ∈ {`control`, `csirt`}. Câmpul intră în textul semnat, ca și `mode`.
  Politicile existente fără `role` se consideră `control` (fail-closed către rolul mai restrictiv) și decizia spune explicit „rol implicit:
  politica nu îl specifică”.
- Lanțul de decizie, înregistrat în `startup_debug.log` și în tooltip-ul insignei din antet, lângă motivul modului:
  `ediție → politică semnată (semnătură, valabilitate, audience, version) → role → verificări de consistență → rol efectiv`.
- Verificări de consistență, toate **fail-closed** la `CONTROL` + air-gap: P1 cu `role: csirt` = respins cu motiv; `csirt` cu `mode: airgapped` =
  acceptat, dar insigna spune „CSIRT fără rețea”; politică lipsă/invalidă/expirată/alt `audience` = `CONTROL`.
- Nu deduce rolul din numele mașinii, din prezența rețelei, din domeniu sau din hardware. Singura sursă este politica semnată; `audience`
  leagă deja politica de o mașină.
- Rolul efectiv se scrie în: auditul de pornire, `case.json` al oricărui caz creat/deschis pe stația respectivă, antetul fiecărui raport,
  jurnalul de autentificare (sesiunea știe pe ce rol a fost deschisă).
- `release-gate/Sign-EditionPolicy.ps1` primește parametrul `-Role`. Cheia privată rămâne pe stația de emitere, ca azi.

## 6. Interfața pentru utilizatori netehnici (valabil în ambele roluri)

### 6.1 Două niveluri de limbaj, un singur adevăr
- **Simplu** (implicit pentru **toate** conturile, administratori incluși) și **Expert** (comutator în antet, disponibil oricărui cont; alegerea se ține per cont).
  Rolul de cont nu spune nimic despre pregătirea tehnică a persoanei: un administrator poate fi un ofițer fără legătură cu IT-ul.
- Ecranele de administrare (Autentificare și conturi, import CA/CRL, politici, licență) respectă aceleași reguli ca restul aplicației: pași ghidați,
  verbe în limbaj de utilizator („Adaugă o persoană”, „Înregistrează cardul acestei persoane”, „Adu lista de certificate revocate de pe suport”),
  explicația a ce se întâmplă și de ce, erori cu pas următor. Termenii tehnici (CA, CRL, thumbprint) apar în paranteză și în glosar, nu în titlul butonului.
- Nivelul schimbă **cuvintele, ordinea și cât detaliu se vede deschis**, niciodată **ce date există**. Orice ecran Simplu are „Arată detaliile tehnice”
  care deschide stratul Expert al aceluiași obiect, fără navigare în altă parte.
- Tabelul de traduceri din contractul UX (§18 din cerințe) devine o resursă de localizare unică (`ro` primar, `en` suportat), nu șiruri în XAML.
  Extinde-l cu termenii de control: `Hardware ID → Identificatorul stației`, `NetworkList → Rețele la care s-a conectat stația`, `USBSTOR → Suporturi USB conectate`,
  `auditpol → Setările de jurnalizare`, `RecordID → Numărul înregistrării din jurnal`, `CRL → Lista certificatelor revocate`.
- Test obligatoriu: nicio etichetă vizibilă în nivelul Simplu nu conține termenii tehnici din tabel fără traducere; termenii tehnici, hash-urile și
  ID-urile de reguli nu se traduc niciodată.

### 6.2 Reguli pentru butoane
- Un buton spune **ce obții**, în limbaj de utilizator, la verb: „Verifică această stație”, nu „Run ControlEvaluator”; „Arată dovezile”, nu „Evidence”.
- Pe fiecare ecran, **cel mult trei acțiuni principale**, restul în „Mai multe”. Acțiunea principală a ecranului este mereu prima și are tastă de acces.
- Orice buton care modifică ceva (izolare, ștergere, sigilare caz) arată **înainte**: ce se va întâmpla, de ce, impactul, cum se verifică, dacă se poate anula;
  **după**: `Cerut → Aplicat → Verificat` sau motivul pentru care nu s-a verificat. Niciodată „succes” doar pentru că s-a emis comanda.
- Butoanele indisponibile (ediție, mod, rol, drepturi) **rămân vizibile, dezactivate, cu motivul la îndemână** („Nu este disponibil în ediția clasificată”,
  „Reporniți aplicația ca administrator”), ca utilizatorul să înțeleagă ce lipsește, nu să caute o funcție dispărută.

### 6.3 Reguli pentru date
- Fiecare rezultat important are cele cinci straturi: **Simplu → Explicație → Dovezi → Tehnic → Brut**. Primele două se văd deschise în nivelul Simplu.
- Fiecare stare se arată **cu cuvânt, icoană și culoare** (niciodată doar culoare). Vocabularul din UI este cel din contract (Observat, Corelat, Susținut de dovezi,
  Verificat, Deducție, Nedemonstrat, Contrazis, Respins, Necunoscut, Neevaluat) plus, pentru control, CONFORM / NECONFORM / DE VERIFICAT / NEDETERMINAT.
  Fiecare stare are un tooltip cu o propoziție care explică ce înseamnă și ce **nu** înseamnă („NEDETERMINAT: sursa nu a putut fi citită; nu înseamnă că activitatea nu a avut loc”).
- Componenta **„Ce știm / Ce suspectăm / Ce nu putem demonstra”** apare pe fiecare card de constatare și pe fiecare rând de control deschis.
- **Golurile de probă** și **acoperirea** sunt pe prima pagină a oricărui rezumat, înaintea listei de constatări. „Nimic găsit” se scrie mereu cu acoperirea alături.
- Orele se afișează în ora locală a stației cu fusul explicit; ora sursă rămâne disponibilă în stratul Tehnic.
- Numerele mari se rezumă („348.000 de evenimente, 19 secunde”) și se pot deschide.

### 6.4 Fluxuri ghidate (nu ecrane goale)
- Pagina **Acasă** arată, în funcție de rol, cel mult **cinci intenții** ca butoane mari, cu o propoziție sub fiecare (§4.1 și §4.2). Fără caz deschis, nimic altceva.
- Fiecare intenție este un flux în **cel mult trei pași**, cu progres vizibil, cu „Înapoi” și cu posibilitatea de a opri fără să se piardă ce s-a colectat.
- La final, fluxul se închide cu **un singur ecran de rezultat** care răspunde la: există o problemă? cât de gravă? sunt probele de încredere? ce s-a găsit? ce urmează?
- Erorile sunt propoziții pentru om, cu o cauză și un pas următor: „Jurnalul Security nu a putut fi citit pentru că aplicația nu rulează ca administrator. Reporniți-o ca administrator și
  rulați din nou controlul.” Mesajul tehnic (excepția) rămâne în stratul Tehnic și în jurnal.

### 6.5 Accesibilitate și ergonomie
- Navigare completă de la tastatură; ordinea de focus urmează ordinea citirii; `AutomationProperties.Name` pe fiecare control interactiv; contrast conform baseline-ului din WP6a.
- Text scalabil (100–175 %) fără trunchieri în cele cinci ecrane principale; verificat prin test UI la 150 %.
- Fiecare grafic sau graf are alternativă text/tabel (contractul UX §10).
- Rapoartele se citesc tipărite alb-negru: stările nu se pierd fără culoare.

### 6.6 Glosar încorporat
- „Ce înseamnă?” lângă orice termen din tabelul de traduceri deschide o definiție de două propoziții, scrisă pentru nespecialiști, cu termenul tehnic original în paranteză.
- Glosarul este o resursă unică (aceeași sursă ca tabelul de traduceri), nu text împrăștiat prin XAML.

## 7. Modelul de date (contracte, nu UI)

```text
StationRole            { Control, Csirt }
StationRoleDecision    { EffectiveRole, RequestedRole?, Edition, Mode, PolicySha256?, Reasons[], DecidedAtUtc }
RoleProfile            { Role, HomeIntents[≤5], NavigationSections[], DefaultLanguageLevel, DefaultReportProfile, AvailableActions[] }
LanguageLevel          { Simple, Expert }            // per cont, implicit Simple pentru orice cont, persistat lângă preferințele contului
UiTerm                 { Key, Technical, RoRomanian, EnEnglish, GlossaryRo, GlossaryEn }
ControlRowPresentation { ControlCheck, HumanTitle, HumanSummary, Status, Basis[], Coverage, KnowSuspectCannotProve }
```

- `RoleProfile` este **date**, nu cod cu `if (role == …)` împrăștiat prin ViewModel-uri: un singur loc (`LogAnalyzer.Core/Services/Edition/` sau `LogAnalyzer.Dfir.Core/Home/`)
  construiește profilul; UI-ul îl randează. Testele verifică profilul, nu XAML-ul.
- `ControlRowPresentation` este un **adaptor de prezentare** peste `ControlCheck`; nu schimbă `ControlStatus` și nu creează al optulea vocabular. Punctul unde `ControlCheck`
  ar deveni `Finding` (cu dovezi, stări standard, verificare) este **revendicat de WP13/WP14/WP16**; WP18 doar pune adaptorul și documentează legătura.
- Rolul efectiv intră în `case.json` (câmp nou, versiune de schemă minoră, cititor tolerant la lipsă), în antetul PDF-urilor și în jurnalul auditat de pornire.

## 8. Pașii de lucru (un PR per pas; TDD: roșu → verde → refactor)

| Pas | Livrabil | Teste care îl dovedesc | Criteriu de acceptare |
|---|---|---|---|
| **S1** Rol de stație | `StationRole`, `StationRoleDecision`, câmp `role` în politica semnată, `Sign-EditionPolicy.ps1 -Role`, decizia în `startup_debug.log` și în insignă | `LogAnalyzer.Dfir.Tests`: politică fără `role` → Control cu motiv; P1 + `csirt` → respins; `csirt` + air-gap → acceptat cu avertisment; semnătură invalidă → Control + air-gap; `version` mai mic → respins; `LogAnalyzer.Edition.Tests` neschimbat verde | nicio cale în cod prin care operatorul schimbă rolul; decizia completă e vizibilă și auditată |
| **S2** Acasă și navigare pe rol | `RoleProfile` ca date; `HomeViewModel` arată intențiile rolului; secțiunile din bara laterală după rol; filele vechi rămân sub „Avansat” (decizia 5) | `HomeViewModelTests`: 5 intenții `CONTROL`, 7 `CSIRT` reduse la 5 principale + „Mai multe”; fiecare intenție duce la fila corectă; nicio intenție de rețea în P1 sau air-gap | utilizatorul vede pe Acasă doar ce poate face pe acea stație, cu motivul pentru ce nu poate |
| **S3** Niveluri de limbaj, traduceri, glosar | `LanguageLevel` per cont (implicit Simplu pentru toți); resursă unică de termeni; comutator Simplu/Expert în antet; „Ce înseamnă?”; „Arată detaliile tehnice” pe fiecare rezultat; ecranele de administrare trecute prin aceleași reguli | test de lint: niciun șir vizibil în Simplu nu conține termeni tehnici netraduși; test că fiecare `UiTerm` are glosar `ro`; `LogAnalyzer.UI.Tests` pe comutare | schimbarea nivelului nu ascunde goluri, stări NEDETERMINAT sau dovezi |
| **S4** Fluxuri ghidate | fluxul „Verifică această stație” (3 pași) și „Compară cu controlul anterior” pentru `CONTROL`; „Primește probe de la o stație” și „Cazuri deschise” pentru `CSIRT`; ecranul unic de rezultat | teste pe mașina de stări a fluxului (înapoi, oprire, reluare); test că rezultatul răspunde la cele 5 întrebări; test că „Nimic găsit” poartă acoperirea | un utilizator netehnic termină fluxul fără să aleagă vreun termen tehnic |
| **S5** Rapoarte pe rol | „Raport pentru proces-verbal” (`CONTROL`): antet, tabel de stări, bazele fiecărui rând, goluri pe prima pagină, loc de semnături; „Raport de incident” (`CSIRT`) prin profilurile WP10 cu întrebarea „Pentru cine?” | teste PDF (QuestPDF) pe conținut: antet complet, rolul și marcajul prezente, golurile înaintea constatărilor; JSON lângă PDF, ambele cu SHA-256 în custodie | raportul se citește tipărit alb-negru; nu conține „CONFORM” derivat din absență |
| **S6** Accesibilitate și probă de utilizare | checklist WP6a extins; script de 5 sarcini pentru un utilizator netehnic (§10) rulat manual și consemnat | test UI la scalare 150 %; `AutomationProperties.Name` pe toate controalele interactive ale celor 5 ecrane principale | cele 5 sarcini se termină fără ajutor, iar unde nu, se notează ce a blocat |
| **S7** Documentație și checkpoint | `docs/dfir/STATION_ROLES.md` (decizia rolului, matricea ediție×mod×rol, cum se emite politica), secțiune nouă în `README.md`, `EDITIONS.md` actualizat, checkpoint final | — | documentația descrie ce există, nu ce se dorește |

Ordinea este obligatorie S1 → S2 → S3; S4 și S5 pot merge în paralel după S3; S6 și S7 la final. Fiecare PR trece CI-ul Windows
(`.github/workflows/loganalyzer-dfir-build.yml`), testele locale față de `main`, grep-ul producție-consumator și `release-gate/Invoke-ReleaseGate.ps1` în mod raport.

## 9. Invariante pe care nu le încalci

1. P1 nu primește rețea, AI sau acțiuni pe gazdă prin niciun rol. `LogAnalyzer.Edition.Tests` rămâne verde și este extins pentru noile tipuri.
2. Niciun rol, nivel de limbaj sau flux nu ascunde: goluri de probă, acoperire, stări NEDETERMINAT, contradicții, limitări.
3. Nicio stare SAFE / NORMAL / OK și niciun CONFORM derivat din absența evenimentelor.
4. Nicio afirmație de conformitate HG 585 / NATO / ISO în UI sau în rapoarte; se spune „verificare de control după procedura X”, nu „conform HG 585”.
5. Nicio acțiune pe gazdă fără `REQUEST → AUTHORIZE → APPLY → VERIFY` și fără ecranul de dinainte/de după.
6. Rolul este auditat la fiecare pornire; schimbarea lui cere politică nouă, semnată, cu versiune mai mare.
7. Nu se rescrie `ControlEvaluator`, `HomeAggregator`, pipeline-ul sau contractele de constatare; se adaugă adaptoare și date.
8. Codul legacy nelegat (decizia 4) rămâne neatins; filele vechi rămân sub „Avansat” (decizia 5).
9. Fără `wmic`, fără `-ExecutionPolicy Bypass`, fără secrete în repo.
10. O fațadă fără consumator de producție nu se prezintă ca funcție; dacă un pas nu poate fi legat în producție, se raportează `UNWIRED`, nu DONE.

## 10. Scenarii de acceptare (scrise pentru persoana care le va rula)

**A. Ofițerul de control, laptop P1, fără pregătire IT.**
1. Pornește aplicația, se autentifică cu cardul. Pe Acasă vede „Stație de control · fără rețea” și cinci butoane mari.
2. Apasă „Verifică această stație”, alege „de la ultimul control”, confirmă profilul de proceduri afișat pe scurt, apasă „Pornește”.
3. După colectare vede un singur ecran: nivel de atenție, încredere în probe, trei lucruri de verificat, golurile de probă.
4. Deschide un rând NECONFORM, citește „Ce știm / Ce suspectăm / Ce nu putem demonstra” și apasă „Arată dovezile”.
5. Apasă „Raport pentru proces-verbal”, salvează o copie pe suportul de control, vede că originalul a rămas în caz cu SHA-256.
Reușită: toți pașii fără să fi citit vreun termen tehnic netradus și fără să fi apăsat „Avansat”.

**B. Ofițerul de serviciu CSIRT, server P3.**
1. Primește un suport de la o stație afectată, apasă „Primește probe de la o stație”, alege folderul.
2. Vede ce s-a importat, ce lipsește și dacă integritatea e în regulă, înainte de orice analiză.
3. Apasă „Ce s-a întâmplat?”, citește cronologia simplă și lanțul incidentului.
4. Apasă „Ce fac acum?”, vede recomandările; o acțiune de izolare îi arată întâi ce se va întâmpla și cum se verifică.
5. Apasă „Raport de incident”, alege „Pentru conducere”, obține PDF-ul; apasă „Trimite în Memory Vault” și vede că este doar propunere.

**C. Analistul, aceeași stație.** Comută pe Expert și, pentru fiecare pas din B, găsește în același loc EvidenceId, SHA-256, parser/versiune, locator și înregistrarea brută.

**D. Administratorul, persoană fără pregătire IT.** Din ecranul „Autentificare și conturi”, adaugă o persoană și îi înregistrează cardul urmând pașii ghidați, fără să citească documentația. Apoi, cu ajutorul celui care deține cheia de semnare, emite o politică cu `role: csirt` pentru laptopul P1 și vede refuzul cu motiv; emite una cu `role: csirt`, `mode: airgapped` pentru server și vede „CSIRT fără rețea”.

## 11. Deciziile proprietarului (2026-10-10) — nu se redeschid

| # | Subiect | Decizie |
|---|---|---|
| D1 | Unde intră rolul de stație | În politica semnată existentă `LogAnalyzer.policy`, câmp `role`, inclus în textul semnat. Fără al doilea fișier semnat. |
| D2 | Exportul de control adus la o stație `CSIRT` | Intră în cazul DFIR ca **probă importată** (SHA-256, custodie, provenință „control de pe stația X, data Y”). Nu devine `Finding` în WP18; legătura rămâne pentru WP13/WP14/WP16. |
| D3 | Antetul procesului-verbal | Câmpuri configurabile de administrator, cu implicite: unitate/structură, stația (nume, Hardware ID), perioada controlată, persoana care a controlat (nume, funcție), rolul stației, număr de înregistrare (liber), nivel de marcaj, loc de semnături (cel care controlează, cel controlat). Setul de câmpuri se salvează ca profil al unității, lângă profilul de proceduri. |
| D4 | Nivelul de marcaj pe raport | Introdus manual la generarea raportului până la WP13, cu textul „marcaj introdus manual, neverificat de aplicație” în subsolul fiecărei pagini. Când WP13 aduce clasificarea cazului, valoarea manuală devine implicită din caz. |
| D5 | Nivelul de limbaj implicit | **Simplu pentru toate conturile**, administratori incluși. Expert este un comutator, nu o consecință a rolului de cont. |
| D6 | Roluri de cont noi | **Nu în WP18.** Rămân administrator global și operator. „Ofițer de securitate” / regula celor doi se amână pentru un pachet ulterior și se notează ca dependență în `STATION_ROLES.md`. |
| D7 | Proba de utilizare | Cele cinci sarcini din §10 se rulează cu o persoană reală fără pregătire IT din organizație înainte de închiderea WP18. Rezultatul se consemnează în checkpoint; fără această rulare, S6 rămâne `PARTIAL`. |
| D8 | „Stație” | Înseamnă **PC**. Serverul centrului este tratat ca un PC cu rol `CSIRT`; nu există cod sau logică specifică „server”. |

## 12. Ce raportezi la fiecare milestone

Checkpoint `todo-<agent>-wp18.md`: pas, branch/PR, ce s-a făcut, ce urmează, blocaje, fișiere-cheie. În PR: `decision / evidence / risks / unknowns /
confidence / recommended_action`, cu stările `REAL / PARTIAL / UNKNOWN / MISSING / BLOCKED / VERIFIED` și tipul de verificare (`CODE_VERIFIED`, `TEST_VERIFIED`,
`CI_VERIFIED`). Rezultatul testelor se lipește din rulare, nu se descrie. Ce nu a putut fi verificat se scrie `UNVERIFIED`.

=== SFÂRȘIT PROMPT ===
