# Matricea de acoperire HG 585 / ORNISS → LogAnalyzer (control stație) — 2026-10-23

Scop: ce cere cadrul legal public pentru controlul unei stații care prelucrează informații clasificate,
ce verifică deja aplicația (95 de verificări automate C/P/A/U/D/N/S + 16 verificări manuale M01–M16)
și ce lipsește. Urmează o propunere de flux și de pagini, construită pe tiparele instrumentelor existente.

## Starea surselor (citiți înainte de matrice)

- Sunt publice: HG 585/2002 (cap. VIII INFOSEC, art. 236–337), HG 781/2002, Legea 182/2002,
  INFOSEC 2 (Ord. ORNISS 16/2014), DS 2 / ghidul PrOpSec (Ord. ORNISS 18/2014) și modelul ORNISS de
  documentație de acreditare pentru un SIC „secret de serviciu” (cu anexele 1–21).
- NU sunt publice: anexele INFOSEC 3, INFOSEC 4, DS 5, DS 9, DS 11; DS 1, DS 14 și INFOSEC 6 sunt
  declarate clasificate. Consecință: **pragurile numerice pentru secrete de stat (parolă, retenție,
  frecvența scanărilor) nu sunt publice**. Aplicația nu trebuie să pretindă că le verifică; pragurile
  se introduc de unitate în Profilul de proceduri, din CSS/PrOpSec aprobate.
- Stări: V = text oficial deschis; V\* = Monitorul Oficial citit printr-o oglindă, de reverificat pe PDF;
  M = modelul ORNISS secret de serviciu (valorile sunt exemple, nu praguri); S = sursă secundară.
- Textul HG 585 citit este cel consolidat până în 2005; modificările ulterioare nu au fost verificate.
- Documentul complementar mai vechi: `HG585_ACCREDITATION_REQUIREMENTS.md` (2026-10-08, 159 de citate).

Legendă acoperire: **DA** = verificare automată existentă; **MANUAL** = există în chestionarul M01–M16;
**PARȚIAL** = acoperit doar în parte; **LIPSĂ** = nu există; **DOC** = se poate verifica doar în documente.

## 1. Matricea

| # | Cerință (ce se verifică) | Sursă | Stare sursă | Aplicația azi | Acoperire | Ce lipsește |
|---|---|---|---|---|---|---|
| **Acces și autentificare** |
| 1 | Acces doar cu certificat/autorizație de nivel corespunzător + need-to-know | HG 585 art. 33, 274, 272; L.182 art. 28 | V | Registru utilizatori (nivel, valabil de la/până la); M11; U05 cine a lucrat cu drepturi | PARȚIAL | Comparație automată: fiecare cont care s-a autentificat în perioadă ↔ abilitare valabilă la data logării, ca verificare cu id propriu |
| 2 | Conturile foștilor utilizatori dezactivate | Model pct. 140–141 | M | C01–C06, U02 | PARȚIAL | Cont activ fără rând (sau cu abilitare expirată) în Registru utilizatori → NECONFORM |
| 3 | Politica de parole (lungime, expirare, istoric, complexitate, schimbare la prima logare) | Model pct. 4.5.4 (ex.) | M | P01–P03 | PARȚIAL | Istoric parole și complexitate; pragurile din Profil, nu fixe |
| 4 | Parolă BIOS, boot doar de pe disc intern | Model pct. 4.5.4 | M | M07 (manual); P15 Secure Boot | MANUAL | Citire WMI de la producător acolo unde există (Dell/HP/Lenovo), altfel rămâne manual |
| 5 | Parolele implicite schimbate | Model pct. 55 | M | C03, C04 | PARȚIAL | — |
| 6 | Separarea rolurilor; nimeni nu deține toate parolele | HG 585 art. 275–276 | V | Rol în Autentificare și conturi | DOC | — |
| **Jurnalizare și audit** |
| 7 | Evidența automată a accesului, ținută necondiționat prin software | HG 585 art. 291(1) | V | A01, A10 (SACL), A11, A14, A15 | DA | — |
| 8 | Retenția registrelor de acces: ≥3 ani (S/SS), ≥10 ani (SSID) | HG 585 art. 291(2)-(3) | V | A02, A03–A04 (acoperirea perioadei controlate) | PARȚIAL | Verificare a arhivei exportate: există jurnale salvate care acoperă ultimii 3/10 ani (registrul exporturilor M06 → verificare automată) |
| 9 | Tentativele și încălcările înregistrate și analizate periodic | Model pct. 58 | M | U04 (parole greșite), A05 (jurnale șterse), A06, A12 | DA | — |
| 10 | Ceasul stației verificat | Model pct. 4.5.8 | M | A07, S05, M10 | DA | — |
| 11 | Tipărire/copiere auditate + registru de multiplicare | Model pct. 132 | M | P29, A08, M12 | PARȚIAL | Registru de multiplicare ↔ jurnal de tipărire (ca la transferuri) |
| **Medii amovibile și transferuri** |
| 12 | Fiecare mediu: număr de înregistrare, marcaj, evidență | HG 585 art. 294; Model pct. 125–127 | V | Monitor (nr. de înregistrare), D04 registru semnat, Registru de transferuri | DA | — |
| 13 | Conectarea unui mediu neînregistrat = incident | Model pct. 91 | M | D01, D04, D05, M16 | DA | Propunere automată de raport de incident (vezi #33) |
| 14 | Transfer: aprobare, AV la intrare/ieșire, consemnare | HG 585 art. 285, 287; Model pct. 129 | V/M | Registru de transferuri (martor), D03, A08, M04 | DA | — |
| 15 | Controale/inventar periodic al suporturilor (anual la SSID) | HG 585 art. 73, 295 | V | M03 (manual) | MANUAL | Inventar anual ghidat: listă din registru, bifare „prezent / etichetă / responsabil”, PV de inventar |
| 16 | Interzise mediile și dispozitivele personale | HG 585 art. 333–334 | V | Monitor; N05; P31; M15 | PARȚIAL | Lista „dispozitive aprobate” în Profil → orice altceva NECONFORM, nu doar alertă |
| **Configurare și întărire** |
| 17 | Doar software autorizat de agenția de acreditare | HG 585 art. 310–311 | V | S01 + Profil „Software aprobat”; P17, P37 | DA | — (depinde de profil completat) |
| 18 | Integritatea versiunilor software verificată periodic | HG 585 art. 309, 311(1) | V | — | LIPSĂ | Amprente SHA-256 ale executabilelor aprobate în profil; abatere → NECONFORM |
| 19 | Instalare doar de administratorul de sistem | Model pct. 22, 136–137 | M | U01, U05, C02, S01, S02 | DA | — |
| 20 | Orice modificare a SIC de cel puțin 2 persoane | HG 585 art. 277 | V | Patru ochi doar la transferuri | LIPSĂ | Registru de intervenții (Anexa 14) cu executant + martor; schimbările din S01/S02 ↔ intrare în registru |
| 21 | Sigilii pe carcasă, verificate periodic | Model pct. 4.5.1 | M | M08 | MANUAL | Numerele sigiliilor în registru; la control se confirmă numărul, nu doar „intact” |
| 22 | Mentenanță la distanță interzisă | HG 585 art. 315–316 | V | P06, C06, N07, N08 | DA | — |
| 23 | După reparație: verificarea dispozitivelor de securitate | HG 585 art. 329(2) | V | — | LIPSĂ | Control „după intervenție” (profil scurt) declanșat din registrul de intervenții |
| **Antivirus și actualizări** |
| 24 | AV rezident, scanare la introducere, scanare periodică, semnături actualizate | HG 585 art. 312–313; Model pct. 143–147 | V/M | P08–P10, P38, P39, S06, D03, M05 | DA | — |
| 25 | Patch-uri OS | INFOSEC 2 art. 45 | V\* | S03, S04 | DA | — |
| **Conectivitate și periferice** |
| 26 | Fără internet / interconectare doar aprobată | INFOSEC 2 art. 37–39; HG 585 art. 248 | V\*/V | N01–N13 | DA | — |
| 27 | Wireless, Bluetooth, camere, microfoane | HG 585 art. 105; DS 2 art. 17, 25 | V/V\* | N03, N04, P19, P31, M09 | DA | — |
| **Registre și marcare** |
| 28 | Banner/marcaj de clasificare | HG 585 art. 59 | V | P26 | DA | — |
| 29 | Registrul de intervenții hardware/software (Anexa 14) | Model Anexa 14 | M | — | LIPSĂ | Registru nou (vezi #20, #23) |
| 30 | Registrul copiilor de siguranță (Anexa 21) | Model pct. 142 | M | — | LIPSĂ | Registru simplu + verificare: ultima copie mai veche decât termenul din profil |
| **Personal** |
| 31 | Instruire periodică, angajament de confidențialitate | HG 585 art. 35, 274(2), 278 | V | — | LIPSĂ | Câmpuri în Registru utilizatori: data ultimei instruiri, angajament semnat; expirate → DE VERIFICAT |
| **Incidente** |
| 32 | Incidentele înregistrate și raportate (raport după Anexa 18) | Model pct. 89–91; INFOSEC 2 art. 51–52 | M/V\* | M13; IncidentPdfReport | PARȚIAL | Registru de incidente (păstrat 5 ani, HG 781 art. 11) |
| 33 | Raportare imediată la administratorul de securitate | Model pct. 89 | M | Alerte în monitor | PARȚIAL | Din alerta D05/A05/A12: „Întocmește raport de incident” precompletat după Anexa 18 |
| **Acreditare și documentație** |
| 34 | SIC acreditat; certificat cu termen de valabilitate; CSS și PrOpSec aprobate | HG 585 art. 240, 260–263, 307–308, 322 | V | M01, M02 | MANUAL | Data expirării acreditării în Profil → avertizare cu 60 de zile înainte |
| **Fizic / TEMPEST / distrugere** |
| 35 | Zonă de securitate, acces fizic, TEMPEST, poziția monitorului | HG 585 art. 98–105, 279–280, 302–303 | V | — | DOC | Întrebări în chestionarul manual (M17+) |
| 36 | Ștergere/distrugere doar conform PrOpSec, cu PV semnat de 2 persoane | HG 585 art. 79, 296–298 | V | M14; P25 | MANUAL | PV de distrugere generat din registrul de medii |
| **Control periodic — documentul de constatare** |
| 37 | Control intern anual și ori de câte ori e nevoie; „document de constatare” | HG 585 art. 193–197 | V | ControlReportPdf + semnătura ofițerului + ancoră | DA | — |
| 38 | Fiecare neconformitate cu **măsură concretă și termen** | SRI Q&A 2020 | S | — | **LIPSĂ** | Registru de neconformități (responsabil, măsură, termen, stare, dovada închiderii) inclus în PV |
| 39 | Comparația cu controlul anterior (ce s-a remediat, ce e nou) | bună practică (art. 197, analiză anuală) | — | Controlul anterior servește doar la alegerea perioadei | **LIPSĂ** | Secțiune „față de controlul din dd.MM.yyyy”: nou neconform / remediat / la fel |

## 2. Concluzie

- **Partea tehnică a stației e acoperită bine**: jurnalizare (art. 291), medii și transferuri (art. 285–295),
  software neautorizat (art. 310), conectivitate și AV au verificări automate cu dovadă.
- **Golul real e procesul de control, nu verificările**: documentul de constatare cere măsură și termen
  pentru fiecare neconformitate (#38), iar aplicația nu le are; nu compară cu controlul anterior (#39);
  nu ține registrele cerute de model în afara celor de medii/transferuri/utilizatori (#20, #29, #30, #32).
- Al doilea gol: **personalul** (#31) și **acreditarea** (#34) — date simple, cu termen, care se pot
  verifica automat odată ce sunt înregistrate.
- Cerințele fizice și TEMPEST (#35) rămân întrebări manuale; aplicația trebuie să le pună, nu să le ghicească.

## 3. Propunere: cum ar arăta aplicația care simplifică munca

Tipare preluate (surse în raportul de cercetare): DISA STIG Viewer / STIG Manager (stări pe regulă,
Accept/Reject), CIS-CAT (manual separat de scor), Policy Analyzer și Wazuh SCA (delta între rulări),
SafetyCulture (chestionar cu dovadă și acțiune corectivă cu termen), POA&M (registru de deficiențe),
NIST 800-53A (metoda: examinare / interviu / test).

**Pentru ofițer — un control în 6 pași, pe o singură pagină „Control stație”:**
1. Alege stația și profilul (pragurile unității din CSS/PrOpSec; versiunea profilului apare în PV).
2. Controlul automat (cele 95 de verificări) — un clic, doar citire.
3. Chestionarul manual M01–M16 (+ fizic/TEMPEST), fiecare cu metoda (examinare / interviu / test)
   și dovadă atașată (scan, fotografie, număr de sigiliu). Răspunsurile preluate de la controlul
   anterior apar „preluat, de confirmat”, niciodată confirmate automat.
4. **Față de controlul anterior**: nou neconform / remediat / neschimbat.
5. **Neconformități**: fiecare primește măsură, responsabil și termen (obligatoriu înainte de PV);
   cele vechi se închid doar dacă verificarea lor iese acum conformă.
6. PV (document de constatare) cu constatări, măsuri și termene, semnat; data controlului următor se propune automat.

**Pentru utilizatorul stației — ecranul „Azi” (Simplu):**
starea stației (text + culoare), 3–5 sarcini (ex. „stick-ul KINGSTON conectat ieri nu are transfer
înregistrat”, „instruirea expiră în 20 de zile”), neconformitățile atribuite lui cu termen, butonul
„Raportează un incident”. Nimic din ce ține de setări de securitate nu se schimbă de aici.

**Ordinea propusă de implementare (valoare / efort):**
1. Registrul de neconformități cu măsură și termen în PV (#38) — cerință directă a documentului de constatare.
2. Delta față de controlul anterior (#39).
3. Comparația automată cont logat ↔ abilitare valabilă (#1, #2) + câmpuri de instruire (#31).
4. Registrul de intervenții cu martor + controlul „după intervenție” (#20, #23, #29).
5. Acreditare cu termen și alertă (#34); registrul de incidente cu raport precompletat (#32, #33).
6. Amprente pentru software aprobat (#18); inventarul anual al suporturilor (#15); copii de siguranță (#30).

## 4. Decizii ale proprietarului

- 2026-10-23: **nivelurile acoperite sunt de la secret de serviciu în sus** (SSv, S, SS, SSID). Nivelul stației
  este o setare a profilului; cerințele care diferă pe nivel se aplică după el: retenția registrelor de acces
  (#8: SSv = cât fixează documentația de acreditare, S/SS ≥ 3 ani, SSID ≥ 10 ani), inventarul suporturilor
  (#15: SSv/S controale punctuale, SS inventar periodic, SSID cel puțin anual), distrugerea (#36: la SSID
  doar distrugere), TEMPEST (#35: doar secrete de stat), internetul (#26: interzis la SSID). La SSv
  documentația este cea din modelul ORNISS (RAR, CSSS, PrOpSec aprobate de conducător, avizate de funcționarul de securitate).

## 5. Ce mai trebuie confirmat de proprietar sau de structura de securitate

- Pragurile reale (parolă, retenție, frecvența scanărilor) din CSS/PrOpSec ale unității — nu sunt publice.
- Forma documentului de constatare folosită în unitate (nu există model public pentru control intern de stație).
- Dacă semnătura PV rămâne olografă sau și electronică.
