# Policy Engine — LogAnalyzer DFIR

Status: **P6 implementat** (model, validare, ciclu de viață, importatoare). Execuția (P7) nu există încă: nicio politică
nu este aplicată de cod în acest moment.

## Formatul nativ

`.lapolicy` (YAML), `.yaml` sau `.json`, cu secțiunile `policy:` și `controls:` (spec §11). Încărcătorul
(`Dfir.Core/Policy/PolicyModel.cs`, `PolicyLoader`) respinge orice cheie necunoscută — nimic nu este ignorat în tăcere —
și calculează SHA-256 peste textul exact al sursei. Hash-ul leagă fiecare pas din ciclul de viață.

Tipuri de control (`detection.type`):

| tip | ce citește | validare |
|---|---|---|
| `registry` | `HKLM`/`HKCU`\key\value, `value_type` dword/qword/string/expand_string/multi_string | hive, key, tip |
| `audit` | subcategorie de audit avansat, după nume sau GUID | numele/GUID-ul trebuie să fie în tabelul celor 60 de subcategorii listate de `auditpol /list /subcategory:* /v`; valoarea: No Auditing / Success / Failure / Success and Failure |
| `service` | modul de pornire al unui serviciu | nume prezent |
| `secpol` | politică de cont citită prin NetUserModalsGet (nivel 0 și 3) | doar MinimumPasswordLength, MaximumPasswordAge, MinimumPasswordAge, PasswordHistorySize, LockoutBadCount, LockoutDuration, ResetLockoutCount; valoare numerică |

Testul `Audit_subcategory_guids_match_auditpol` compară tabelul cu ce raportează Windows pe stație (număr și GUID-uri).

## Ciclul de viață

`DRAFT → VALIDATED → TESTED → APPROVED → DEPLOYED → VERIFIED`, `RETIRED` din orice stare (`PolicyStore`).

- Orice tranziție înregistrează operatorul, ora UTC, motivul și hash-ul politicii.
- Aceeași pereche id/versiune cu alt text este blocată (hash diferit).
- Aprobarea de către autor sau de către cel care a înregistrat politica este refuzată (fără auto-aprobare).
- `RequireApplicable` refuză orice politică care nu este APPROVED/DEPLOYED/VERIFIED — un DRAFT nu se aplică niciodată.
- O politică importată intră ca DRAFT (testul `An_imported_policy_is_a_draft_and_cannot_be_applied`).

## Importatoare (`Dfir.Core/Policy/PolicyFormats.cs`, `PolicyImport.cs`)

Fiecare import produce politica nativă (YAML generat + hash propriu) și două liste: **Unsupported** (tot ce motorul nu
citește sau nu aplică, cu sursa exactă) și **Metadata** (secțiuni de metadate ale șablonului). Nicio intrare nu dispare.

| format | sursă | ce devine control | ce rămâne „neacceptat” |
|---|---|---|---|
| Registry.pol (PReg v1) | `Machine`/`User\registry.pol` | valori DWORD/QWORD/SZ/EXPAND_SZ/MULTI_SZ (`set`); `**del.X` → `absent`, remediere manuală | `**delvals`, `**DeleteValues`, alte intrări speciale, REG_BINARY și alte tipuri |
| Șablon de securitate (.inf, GptTmpl.inf, UTF-16 sau UTF-8) | `[System Access]`, `[Registry Values]` | cele 7 setări `secpol`; valorile din registru tip 1/2/4/7 | celelalte chei din `[System Access]`, `[Privilege Rights]`, `[Service General Setting]`, `[Registry Keys]`, `[File Security]`, `[Event Audit]` etc. |
| audit.csv (backup GPO / `auditpol /backup`) | coloanele `Subcategory GUID`, `Setting Value` | o subcategorie = un control `audit` | GUID-uri care nu sunt în tabel |
| Text LGPO | grupuri de 4 linii `Computer|User`, cheie, valoare, `TIP:date|DELETE|DELETEALLVALUES` | ca Registry.pol | `DELETEALLVALUES` |
| Backup GPO (folder sau zip) | `Backup.xml` (nume, GUID), Registry.pol Machine/User, GptTmpl.inf, audit.csv, gpreport.xml | combinația celor de mai sus | idem |

Șirurile `REG_SZ` din șabloanele `.inf` sunt scrise între ghilimele (`=1,"1"`); ghilimelele delimitează literalul și nu fac
parte din valoare (registrul are `ScRemoveOption` = `0`, nu `"0"`). Importatorul le elimină.

Erorile de format sunt raportate, nu reparate: antet PReg greșit, date trunchiate, text LGPO care nu se împarte în grupuri
de 4, tipuri LGPO necunoscute, arhive care nu conțin un backup GPO.

`LGPO.exe` nu este rulat: textul LGPO este citit doar dacă proprietarul îl furnizează.

## Validare pe date reale

Testul `Owner_gpo_backups_import_and_agree_with_gpreport` citește backup-urile GPO ale proprietarului indicate de variabila
`LADFIR_POLICY_SAMPLES` (fișier sau folder; arhivele imbricate sunt deschise în memorie, nimic nu este copiat în depozit).
Fără variabilă, testul este omis cu motivul **POLICY SAMPLES UNAVAILABLE**.

Referința independentă este `gpreport.xml`, generat de GPMC pentru fiecare GPO. Pentru fiecare backup:

- fiecare `AuditSetting` (GUID + SettingValue) are un control `audit` cu aceeași valoare, iar numărul lor este egal;
- fiecare `Account` numeric citit de motor are controlul `secpol` cu același `SettingNumber`;
- fiecare `SecurityOptions` cu `KeyName` `MACHINE\...` are controlul `registry` cu aceeași cheie, valoare și date;
- fiecare `RegistrySetting` non-ADMX are cheia printre intrările Registry.pol citite de parser;
- pentru fiecare Registry.pol: intrări = controale + linii neacceptate (nimic pierdut);
- politica rezultată trece `PolicyValidator` fără erori.

Rezultat pe cele 3 backup-uri furnizate (2026-10-05):

| GPO | controale | neacceptate |
|---|---|---|
| Intranet AppLocker W11 | 27 (registry) | 0 |
| Intranet Computer W11 | 637 (590 registry, 40 audit, 7 secpol) | 46 (35 intrări Registry.pol speciale/binare, 9 chei `[System Access]`, `[Privilege Rights]` 38, `[Service General Setting]` 1) |
| Intranet User W11 | 7 (registry) | 0 |

Limită cunoscută: cele 391 de politici ADMX din gpreport.xml nu conțin calea din registru, deci nu sunt comparate una câte
una cu controalele; acoperirea lor este dovedită doar indirect, prin contabilitatea completă a intrărilor Registry.pol.

## Ce nu există încă

- Execuția (P7): citire stare curentă, diff, impact, aprobare, aplicare, re-citire, NOT_VERIFIED/FAILED, jurnal de audit.
- Drepturi de utilizator, ACL-uri, servicii din șabloane, OSCAL (P8).
