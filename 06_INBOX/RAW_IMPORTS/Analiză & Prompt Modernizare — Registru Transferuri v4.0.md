# ANALIZĂ & PROMPT DE MODERNIZARE
## Registru Militar de Transferuri & Device Control — de la v3.1 la v4.0 „TACTICAL COMMAND"

Data analizei: 17 august 2026
Repo analizat: `userist123/Registru-de-transferuri` (branch `main`)

---

# PARTEA I — ANALIZA APLICAȚIEI ACTUALE

## 1. Radiografia repo-ului

Repo-ul conține în acest moment **două implementări paralele**:

| Componentă | Stare | Observație |
|---|---|---|
| **Python / PyQt** (`main.py`, `ui/`, `services/`, `database/`) | Funcțională, legacy | Conține 7 tab-uri, device control, cognitive bridge, export — dar e o fundătură pentru obiectivul declarat (aplicație nativă C# WPF) |
| **C# WPF** (`src/RegistruTransferuri/`) | Schelet avansat v3.1 | `net8.0-windows`, code-behind (fără MVVM), temă custom `Colors.xaml`/`Styles.xaml`, SQLCipher, DPAPI, AuditChain + MerkleTree, QuestPDF, DLP inspector, dialog Four-Eyes |
| Artefacte reziduale | Problematice | `__pycache__/*.pyc` comise în git, `audit_log.jsonl` în rădăcina repo-ului (date de audit nu au ce căuta versionate) |

### Ce este deja solid în varianta C# (de păstrat)
- **Alegeri criptografice corecte la nivel de dependențe**: `SQLitePCLRaw.bundle_e_sqlcipher` (bază de date criptată), `System.Security.Cryptography.ProtectedData` (DPAPI), `BouncyCastle`, `QuestPDF` pentru PV-uri.
- **Nucleul de securitate există**: `AuditChain.cs`, `MerkleTree.cs`, `SecureBuffer.cs`, `PayloadDlpInspector.cs`, `DpapiKeyProtector.cs`, `WmiMediaDetector.cs`, `SmartCardSession.cs`.
- **Paleta de culori este deja pe direcția „Dark Tactical Command"** (obsidian `#090D16`/`#0F172A`, violet `#7C3AED`, cyber-blue `#00E5FF`) — bună ca punct de plecare, trebuie doar sistematizată în design tokens.
- **Layout-ul principal** (sidebar 270px + header + content) este corect ca schemă; problema e execuția, nu structura.

## 2. Probleme identificate, pe categorii

### A. Platformă și ciclu de viață (P0 — urgent)
1. **`net8.0-windows` expiră în noiembrie 2026.** .NET 8 iese din suport pe 10 noiembrie 2026 — adică în mai puțin de 3 luni de la data acestei analize. Ținta corectă este **.NET 10 LTS** (lansat noiembrie 2025, suportat până pe 10 noiembrie 2028, conform [anunțului oficial Microsoft](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/)). Pentru o stație air-gapped, un LTS cu 2+ ani de suport rămas este o cerință de conformitate, nu un moft.
2. **Nu folosește tema Fluent nativă WPF.** Din .NET 9, WPF include tema Fluent (Windows 11) cu dark mode integrat prin proprietatea `ThemeMode` (`Light`/`Dark`/`System`), iar [.NET 10 a extins stilurile Fluent](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) la TextBox, DatePicker, GroupBox, RichTextBox, Label etc. și a rezolvat crash-urile de HighContrast. Strategia corectă: **Fluent Dark ca strat de bază + design tokens „Tactical" ca suprascriere**, nu re-stilizarea manuală a fiecărui control de la zero.
3. **Dual codebase.** Menținerea în paralel a versiunii PyQt dublează efortul și riscă divergența schema DB / audit chain. Python-ul trebuie înghețat (tag + folder `legacy/`) și exclus din build.

### B. Arhitectură C# (P0)
1. **Code-behind monolitic, zero MVVM.** `MainWindow.xaml.cs` cu handlere `OnNavChanged` și logică în code-behind face aplicația netestabilă. Pentru o aplicație cu pretenții de audit criptografic, **testabilitatea serviciilor de securitate este o cerință de securitate**, nu una estetică.
2. **Lipsă Dependency Injection / Host.** Serviciile (`AuditChain`, `SanitizationService`, detectorul PnP) trebuie înregistrate în `Microsoft.Extensions.Hosting` cu lifetime-uri explicite, config validat la pornire și logging structurat.
3. **Navigare pe RadioButton + vizibilitate de panouri** în loc de view-model-first navigation — împiedică lazy-loading, stări per-modul și dialogurile modale testabile.
4. **Un singur proiect.** Separarea minimă corectă: `Core` (domeniu + contracte), `Infrastructure` (SQLCipher, WMI/PnP, QuestPDF, DPAPI), `App` (WPF). Testele există (`SecurityTests.cs`) dar nu pot acoperi UI-ul cuplat.

### C. Detecția mediilor de stocare (P1 — diferența dintre „demo" și „Endpoint Protector")
1. **WMI polling / `ManagementEventWatcher` are latență și pierde evenimente.** Pentru „Live PnP fără latență" standardul industrial este `WM_DEVICECHANGE` + `RegisterDeviceNotification` (sau `CM_Register_Notification`) pe interfețele `GUID_DEVINTERFACE_DISK` / `GUID_DEVINTERFACE_VOLUME`, cu WMI doar ca sursă secundară de metadate.
2. **Serialul REAL de firmware** trebuie citit prin `IOCTL_STORAGE_QUERY_PROPERTY` (`StorageDeviceProperty`), nu din `Win32_DiskDrive.SerialNumber` (care poate returna serial de bridge USB sau string gol). VID/PID se extrag din `DeviceInstanceId`. Fără asta, „imuabilitatea P16" e imuabilitatea unei valori greșite.
3. **Amenințarea reală: spoofing.** VID/PID/serial pot fi clonate de un microcontroler ostil (BadUSB). Aplicația nu poate rezolva asta 100% software, dar poate: (a) lega amprenta de un **tuplu compus** (VID+PID+serial+capacitate fizică+geometrie+volum GUID), (b) marca la reconectare orice deviere de tuplu ca incident de securitate în audit chain, (c) consemna explicit în documentație limita de amenințare.

### D. Securitate criptografică (P1)
1. **PIN-urile trebuie derivate cu Argon2id** (parametri: minim 64 MB memorie, 3 iterații), nu SHA-256 simplu — un PIN de 4-8 cifre fără KDF memory-hard se sparge offline în secunde dacă DB-ul e exfiltrat. Alternativă 100% managed: PBKDF2-SHA512 cu ≥600k iterații, dar Argon2id e alegerea corectă.
2. **Lanțul de audit detectează modificarea, nu înlocuirea.** Un administrator local poate restaura un backup vechi al întregii baze de date și lanțul rămâne valid intern (atac de rollback). Mitigări obligatorii: contor monoton + head-hash al lanțului scris periodic în **al doilea mediu** (registru Windows protejat + fișier pe partiție separată + opțional TPM NV counter), verificate la fiecare pornire; export periodic al head-hash-ului pe hârtie în PV (ancorare fizică, perfect adecvată pentru mediu militar).
3. **Four-Eyes trebuie să fie criptografic, nu doar UI.** Semnătura martorului trebuie să fie HMAC/semnătură peste conținutul canonic al transferului (hash fișier + metadate + timestamp + id operator), stocată în blocul de audit — nu doar un flag boolean „a doua persoană a introdus PIN-ul corect".
4. **Cheia SQLCipher**: derivată din secret protejat DPAPI **per-mașină + per-serviciu**, ținută în `SecureBuffer` cu `CryptographicOperations.ZeroMemory()` la dispose — parțial există, trebuie auditat fluxul complet (inclusiv să nu ajungă niciodată în string imutabil).
5. **Hardening de stație** (în afara aplicației, dar de documentat în livrabil): binar semnat, politică WDAC/AppLocker care permite doar binarul semnat, cont de serviciu fără drepturi de admin, protecție ACL pe folderul DB.

### E. Sanitizare — corecții de conformitate (P1)
1. **Referințele normative sunt corecte dar trebuie nuanțate**: NIST SP 800-88 Rev. 2 a fost finalizat pe 26 septembrie 2025 ([NIST CSRC](https://csrc.nist.gov/pubs/sp/800/88/r2/final)) și **nu mai conține tehnici per-media** — deferă explicit tehnicile către IEEE 2883 ([analiză SK Tes](https://www.sktes.com/news/understanding-ieee-2883-2022-clear-purge-and-destruct-explained)). Deci în aplicație: taxonomia Clear/Purge/Destroy + programul de sanitizare = NIST 800-88r2; comenzile efective per interfață (NVMe Sanitize, ATA Secure Erase, Crypto Erase pe SED) = IEEE 2883-2022. PV-ul de sanitizare trebuie să citeze ambele, exact în acest rol.
2. **Onestitate tehnică în UI**: din user-mode, aplicația poate face fiabil *Clear* (overwrite pe zona adresabilă + verificare eșantionată). *Purge* real (NVMe Sanitize / Crypto Erase) cere acces privilegiat la device și suport firmware — aplicația trebuie să orchestreze și să **ateste** operația (cine, când, metoda, verificarea), afișând clar metoda efectiv aplicată și limitele ei, nu să pretindă „Purge" după un simplu overwrite. Asta e diferența dintre un instrument credibil la un audit INFOSEC și unul care pică la prima întrebare.

### F. UX / Design (P1 — subiectul cerut explicit)
1. **Emoji ca iconografie** (🛡️📋➕🧠) — neprofesionist pentru o aplicație militară, randare inconsistentă între versiuni Windows, fără control pe culoare/greutate. Înlocuire cu **Segoe Fluent Icons** (font de sistem, zero dependențe — ideal air-gapped) + path-uri vectoriale pentru insignele de clasificare.
2. **Fără ierarhie tipografică sistematică** — totul e Segoe UI cu FontSize ad-hoc. Hash-urile SHA-256, serialele și numerele de înregistrare cer **font monospace** (Cascadia Mono, inclus în Windows) cu grupare vizuală (4×16 hex) și buton de copiere.
3. **Titlul ferestrei e un paragraf** („ROMÂNIA — REGISTRU MILITAR... (HG 585 / NATO AC/35 / EUCI)"). Titlul aparține header-ului aplicației, nu barei de titlu.
4. **Lipsesc stările**: empty states, skeleton/loading, focus vizibil pe tastatură, stări de eroare pe formulare, confirmări distructive tipizate.
5. **Clasificările nu au sistem vizual dedicat** — cel mai important element semantic al aplicației trebuie să fie un limbaj vizual de sine stătător (benzi de culoare, badge-uri cu contrast garantat, conform grilelor NATO/EUCI).
6. **Fereastra fixă 1440×900 cu MinWidth 1200** — fără grile responsive, fără virtualizare declarată pe DataGrid (la mii de transferuri va îngheța UI-ul).

## 3. Prioritizare recomandată

| Prioritate | Acțiune | Efort | Impact |
|---|---|---|---|
| **P0** | Migrare .NET 8 → .NET 10 LTS + Fluent Dark ca bază de temă | Mic | Suport până în 2028, dark mode nativ |
| **P0** | Restructurare MVVM (CommunityToolkit.Mvvm) + DI (Generic Host) + split în 3 proiecte | Mediu | Testabilitate, mentenanță |
| **P0** | Înghețare cod Python în `legacy/`, curățare `.pyc`/`audit_log.jsonl` din git | Mic | Igienă repo |
| **P1** | PnP event-driven (WM_DEVICECHANGE) + serial firmware prin IOCTL | Mediu | „Live fără latență" real |
| **P1** | Argon2id pentru PIN-uri + Four-Eyes criptografic + anti-rollback (ancoră externă head-hash) | Mediu | Închide cele 3 găuri majore |
| **P1** | Design system v2 complet (tokens, iconografie Fluent, tipografie, stări) | Mediu | Obiectivul „ultra-profesionist" |
| **P2** | Modul sanitizare cu atestare onestă Clear/Purge + PV dual-standard | Mediu | Conformitate NIST r2/IEEE 2883 |
| **P2** | Virtualizare DataGrid, export CSV/PDF pe thread separat, packaging self-contained semnat | Mic | Robustețe operațională |

---

# PARTEA II — PROMPTUL DE MODERNIZARE (gata de folosit)

> Copiază integral blocul de mai jos într-un agent de cod (Codex/Claude Code/etc.) deschis pe repo-ul `Registru-de-transferuri`.

---

```markdown
# MISIUNE: MODERNIZARE „REGISTRU MILITAR DE TRANSFERURI & DEVICE CONTROL" → v4.0 „TACTICAL COMMAND"

Ești un arhitect senior .NET + un designer de produs enterprise. Modernizezi aplicația WPF existentă
din `src/RegistruTransferuri/` (v3.1, net8.0-windows, code-behind) într-o aplicație v4.0 de nivel
profesional militar, păstrând TOATE funcționalitățile existente (registru transferuri HG 585/2002,
device control PnP, four-eyes, audit chain SHA-256, sanitizare, oracol INFOSEC, PV-uri QuestPDF).
Lucrezi incremental, cu build verde după fiecare etapă. Nu rescrii de la zero logica de securitate
existentă (AuditChain, MerkleTree, SecureBuffer, DpapiKeyProtector) — o refactorizezi și o întărești.

## 0. REGULI ABSOLUTE (GUARDRAILS)
- Aplicația rulează AIR-GAPPED: ZERO apeluri de rețea, zero telemetrie, zero NuGet la runtime,
  toate dependențele vendored/self-contained. Nicio funcție nu are voie să presupună internet.
- Datele hardware citite de la Windows (VID, PID, serial firmware, model, capacitate, volume GUID)
  sunt READ-ONLY în UI și în DB (coloane fără UPDATE permis la nivel de repository; orice tentativă
  = eveniment de audit). Singurul câmp editabil pe un mediu: „Denumire Volum / Nr. Înregistrare Mediu".
- Înregistrarea unui mediu se face EXCLUSIV din lista dispozitivelor detectate live. Nu există
  formular de adăugare manuală a unui mediu.
- Niciun secret (PIN, cheie SQLCipher) nu trece prin `string`. Doar `byte[]`/`Span<byte>` cu
  `CryptographicOperations.ZeroMemory` în `finally`.
- Limba UI: română, diacritice corecte peste tot. Terminologie: HG 585/2002, nu traduceri improvizate.

## 1. PLATFORMĂ ȘI STRUCTURĂ
- Target: **net10.0-windows** (LTS). `LangVersion` latest, `Nullable` enable, `TreatWarningsAsErrors` true.
- Split în proiecte:
  - `RegistruTransferuri.Core` — entități, enums (niveluri de clasificare naționale/NATO/EUCI),
    contracte (ITransferService, IMediaDeviceService, IAuditChainService, ISanitizationService,
    IPvGenerator, IOracleService), validatoare, logică de parsare nr. înregistrare din nume fișier
    (regex: `^(?<nr>\d{1,6})-(?<an>\d{2})(?<clasif>SSv|S|SS|NC)?` + variante cu prefix 000/00/0/S/NC),
    FĂRĂ dependențe Windows.
  - `RegistruTransferuri.Infrastructure` — SQLCipher (Microsoft.Data.Sqlite + bundle_e_sqlcipher,
    WAL mode, tranzacții atomice), PnP nativ, QuestPDF, DPAPI, DLP/magic-bytes, Argon2id.
  - `RegistruTransferuri.App` — WPF, MVVM cu **CommunityToolkit.Mvvm** (ObservableObject,
    RelayCommand, Messenger), **Microsoft.Extensions.Hosting** pentru DI/config/logging
    (Serilog → fișier local rulat prin rotație).
  - `RegistruTransferuri.Tests` — xUnit; țintă: 90%+ pe Core.Security și parserul de numere.
- Publish: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`. Documentează
  pașii de semnare Authenticode și o politică AppLocker exemplu în `docs/DEPLOYMENT.md`.
- Mută implementarea Python în `legacy/python/`, scoate-o din solution, șterge `__pycache__` și
  `audit_log.jsonl` din git (adaugă în .gitignore).

## 2. HARDENING DE SECURITATE (obligatoriu, în această ordine)
1. **PIN-uri**: Argon2id (Konscious.Security.Cryptography) — m=64MB, t=3, p=4, salt 16B unic,
   ieșire 32B. Migrare lazy la primul login reușit pentru hash-urile vechi. Lockout progresiv:
   5 eșecuri → 30s, apoi exponențial; fiecare eșec = eveniment în audit chain.
2. **Four-Eyes criptografic**: la transferuri Secret/Strict Secret/SSID, semnătura martorului =
   HMAC-SHA256(cheie derivată din PIN-ul martorului prin Argon2id, payload canonic JSON:
   {transferId, sha256Fișier, clasificare, mediuId, operatorId, timestampUtc}). HMAC-ul se
   stochează în blocul de audit și se re-verifică la validarea lanțului. Martorul ≠ operatorul
   (verificat pe id, nu pe nume).
3. **Anti-rollback pentru audit chain**: la fiecare bloc nou, scrie tuplul {înălțime lanț, head-hash}
   în DOUĂ ancore externe: (a) HKLM protejat prin ACL, (b) fișier `anchor.bin` semnat HMAC în alt
   director/partiție configurabil(ă). La pornire: compară; dacă DB-ul e în urma ancorelor →
   banner roșu „POSIBIL ROLLBACK DETECTAT" + blocare scriere până la confirmare cu rol Administrator
   + eveniment de audit. Butonul existent „Verifică integritatea" verifică și ancorele, și HMAC-urile
   Four-Eyes, și Merkle root-ul, cu raport detaliat pe blocul exact care a picat.
4. **Cheia SQLCipher**: secret random 32B generat la prima rulare → protejat DPAPI
   (scope LocalMachine + entropie suplimentară per-aplicație) → deschis în SecureBuffer doar pe
   durata conexiunii. PRAGMA key prin parametru, niciodată prin interpolare de string SQL.
5. **DLP/magic bytes**: păstrează PayloadDlpInspector, extinde: MZ/PE, ELF, Mach-O, scripturi cu
   shebang, macro-uri OLE (`D0 CF 11 E0` cu stream VBA), arhive imbricate (scanare recursivă cu
   limită de adâncime 5 și limită de dimensiune decomprimată — protecție zip-bomb), extensie dublă
   (`raport.pdf.exe`). Rezultatul inspecției intră în PV și în audit.

## 3. DEVICE CONTROL LIVE (înlocuiește polling-ul WMI)
- Detecție prin `WM_DEVICECHANGE` + `RegisterDeviceNotification` pe fereastra principală
  (HwndSource hook) pentru GUID_DEVINTERFACE_DISK, _VOLUME, _CDROM — latență sub 1 secundă,
  fără timer de polling. WMI rămâne doar pentru îmbogățire de metadate la eveniment.
- Identitate hardware: `IOCTL_STORAGE_QUERY_PROPERTY` → StorageDeviceDescriptor (serial firmware,
  model, vendor); VID/PID din DeviceInstanceId (`USB\VID_xxxx&PID_xxxx\serial`); capacitate din
  `IOCTL_DISK_GET_LENGTH_INFO`; BusType (USB/SATA/NVMe/SD) din același descriptor. Folosește
  CsWin32 sau Vanara.PInvoke, nu declarații DllImport scrise de mână împrăștiate prin cod.
- Amprenta unui mediu = SHA-256 peste tuplul canonic (VID|PID|SerialFirmware|Model|CapacitateBytes|
  BusType). La reconectare, dacă orice element diferă la același serial → stare „SUSPECT — 
  identitate hardware modificată", blocare automată + eveniment de audit de severitate maximă.
- Politici per mediu: Autorizat R/W, Doar Citire, În Așteptare, Blocat/Revocat + plafon maxim de
  clasificare. La selecția mediului într-un transfer, plafonul se validează HARD (nu doar warning).

## 4. SANITIZARE — CONFORMITATE ONESTĂ
- Taxonomie și program: NIST SP 800-88 Rev. 2 (septembrie 2025). Tehnici per interfață:
  IEEE 2883-2022. PV-ul citează ambele exact în aceste roluri (r2 NU mai definește tehnici).
- Implementează REAL: Clear = overwrite single-pass pe zona adresabilă + verificare prin eșantionare
  aleatorie 10% + citire completă opțională. Purge: dacă device-ul expune interfața necesară și
  procesul are privilegii — orchestrare; altfel afișezi clar: „Purge indisponibil pe această stație
  pentru acest mediu — folosiți procedura X", fără să minți în PV. Câmpul „Metodă efectiv aplicată"
  este obligatoriu în certificat.
- Certificat de sanitizare QuestPDF: mediu (identitate completă read-only), metoda, standardul,
  operator + martor (Four-Eyes obligatoriu la sanitizare), rezultatul verificării, careu semnături,
  hash-ul certificatului intră în audit chain.

## 5. DESIGN SYSTEM „TACTICAL COMMAND v2" (rescrie Themes/)
Strategie: activează tema Fluent nativă WPF ca strat de bază (`<Application ThemeMode="Dark">`,
disponibilă din .NET 9/10) și suprascrie prin resource dictionaries proprii. NU re-stiliza de la
zero controale pe care Fluent Dark le acoperă deja.

### 5.1 Design tokens (ResourceDictionary `Tokens.xaml`, sursă unică de adevăr)
Culori (pornind de la paleta v3.1, rafinată):
- Fundal: Deep `#0A0E17`, Base `#0F1626`, Surface `#141C30`, Elevated `#1B2540`, Overlay `#0A0E17` @ 85%
- Linii: Border `#22304C`, BorderSubtle `#1A2439`, Divider `#182136`
- Accent primar (violet): `#8B5CF6`, hover `#7C3AED`, pressed `#6D28D9`, glow `#8B5CF6` @ 25%
- Accent secundar (cyan tactic): `#22D3EE` — DOAR pentru date live (indicatori PnP, hash-uri, pulse)
- Semantice: Success `#34D399`, Warning `#FBBF24`, Danger `#F87171`, Info `#60A5FA`
- Text: Primary `#F1F5F9`, Secondary `#94A3B8`, Muted `#5B6B85`, OnAccent `#FFFFFF`
- Culori de clasificare (benzi + badge-uri, contrast AA garantat pe text):
  - Neclasificat/UNCLASSIFIED: `#34D399` | Restricted/Serviciu: `#60A5FA`
  - Confidențial: `#FBBF24` | Secret: `#FB923C` | Strict Secret/CTS: `#F87171` (+ hașură diagonală subtilă pe banner)
Spațiere: scară 4px (4/8/12/16/24/32/48). Raze: 4 (input), 8 (card), 12 (dialog), full (badge/pill).
Elevație: fără drop-shadow difuz mare; folosește border 1px + glow accent discret la focus/hover.

### 5.2 Tipografie
- UI: Segoe UI Variable (fallback Segoe UI). Scara: Display 28/SemiBold, Title 20/SemiBold,
  Subtitle 16/SemiBold, Body 13/Regular, Caption 11/Regular, Overline 10/SemiBold + letter-spacing
  1.5 + UPPERCASE (pentru etichete de secțiune tip „REGIM AIR-GAPPED").
- Date tehnice (hash SHA-256, seriale, VID/PID, nr. înregistrare): **Cascadia Mono** (inclus în
  Windows 11), grupare hash pe 4 grupuri × 16 caractere pe 2 rânduri sau trunchiere mediană
  `a3f9…e21c` cu tooltip complet + buton „copiază" cu feedback (icon check 1.5s).

### 5.3 Iconografie
- Elimină TOATE emoji-urile. Segoe Fluent Icons (font de sistem) pentru navigare și acțiuni:
  Registru `\uE9F9`, Transfer nou `\uE710`, Medii/Shield `\uEA18`, Oracol `\uE99A`, Statistici
  `\uE9D2`, Audit `\uE9D9` (scroll), Operatori `\uE716`, USB `\uE88E`, blocat `\uE72E`,
  verificat `\uE73E`. Insigne de clasificare: Path-uri vectoriale proprii (scut cu nivel), nu font.

### 5.4 Layout și navigare
- Fereastră: WindowChrome custom (bară de titlu proprie 40px integrată în temă, butoane
  min/max/close stilizate), titlu scurt „Registru Transferuri — INFOSEC", MinSize 1280×800,
  totul redimensionabil cu grile fluide, SizeToContent nicăieri.
- Sidebar 264px, colapsabil la 56px (doar iconuri + tooltip): sus sigla + denumirea unității
  (configurabilă), navigare cu indicator activ = bară verticală 3px violet + fundal Surface,
  tranziție 150ms. Jos: badge stație (REGIM AIR-GAPPED cu dot verde pulsând subtil 2s ease-in-out,
  hostname, versiune), card operator logat (inițiale în avatar rotund, nume, clearance ca badge).
- Header 64px: titlul modulului curent + breadcrumb, dreapta: căutare globală (Ctrl+K), indicator
  integritate lanț (scut verde „Lanț valid · N blocuri" / roșu pulsând la eșec), ceas UTC+local.
- Tranziții între module: fade + translateY 8px, 180ms, ease-out. Fără animații pe DataGrid-uri mari.

### 5.5 Componente cheie (stiluri implicite)
- **Card KPI** (Dashboard): valoare Display, etichetă Overline, delta cu săgeată colorată semantic,
  sparkline discret (Polyline, fără librării de charting externe).
- **DataGrid**: header sticky fundal Surface + Overline, rânduri 44px, zebra invizibilă (hover
  Elevated), coloană clasificare = badge pill cu banda de culoare, virtualizare
  (EnableRowVirtualization, VirtualizingPanel.VirtualizationMode=Recycling) OBLIGATORIE,
  sortare cu indicator, selecție cu bară laterală violet.
- **Inspector detalii transfer**: panou lateral drept 420px slide-in (200ms), nu dialog modal —
  cu secțiuni: identitate transfer, fișier + SHA-256 mono, mediu (read-only cu lacăt 🔒 redat ca
  icon Fluent `\uE72E`, NU emoji), semnături Four-Eyes, acțiuni (PV, anulare motivată).
- **Formulare** (Transfer nou): layout 2 coloane, label deasupra, stări complete
  (default/hover/focus ring violet 2px/error cu mesaj sub câmp/disabled), validare live
  (INotifyDataErrorInfo prin toolkit), zona de drop fișier cu progres hash SHA-256 REAL
  (IProgress<double>, calcul pe Task.Run cu buffer 1MB, UI niciodată blocat), autocompletarea
  nr. înregistrare + clasificare din numele fișierului cu chip „detectat automat — verificați".
- **Dialog Four-Eyes**: modal cu overlay blur imitat (fundal Overlay 85%), doi pași vizuali
  (operator ✓ → martor), PIN cu PasswordBox custom 6 celule, avertisment clar la clasificări înalte.
- **Empty states**: fiecare listă goală are icon Fluent mare muted + titlu + acțiune primară.
- **Toast-uri**: colț dreapta-jos, semantic-colorate, auto-dismiss 5s, stivuite max 3.
- **Confirmări distructive** (anulare transfer, revocare mediu, sanitizare): dialog cu buton
  Danger, motivare obligatorie (min 10 caractere), consemnate în audit.

### 5.6 Accesibilitate și calitate vizuală
- Contrast minim AA (4.5:1) pe orice text; focus vizibil pe tastatură peste tot; navigare
  completă din tastatură; AutomationProperties pe controalele custom.
- Respectă `SystemParameters.ClientAreaAnimation` — dezactivează animațiile dacă OS-ul o cere.
- DPI-aware PerMonitorV2. Testează la 100%/125%/150%.

## 6. MODULELE (păstrezi toate cele 7, cu îmbunătățirile de mai sus)
1. Registru Transferuri: filtre pe clasificare ca segmented control cu badge-uri de culoare,
   căutare instant, export CSV/PDF pe thread separat cu progres, inspector lateral.
2. Înregistrare Transfer: wizard vizual în 4 pași (Fișier → Mediu → Clasificare/Detalii →
   Semnare Four-Eyes) cu stepper orizontal, dar pe un singur ecran scrollabil.
3. Medii Amprentate: tabel live (insert animat 200ms la conectare, dim + tag „deconectat" la
   scoatere), buton „Amprentează" activ DOAR pe rânduri live neamprentate, politici ca dropdown
   cu confirmare, plafon clasificare ca badge editabil doar de Administrator, acces sanitizare.
4. Seif Cognitiv & Oracol: păstrezi CognitiveVaultBridgeService; UI tip chat minimal cu răspunsuri
   în carduri citabile (articolul de lege ca sursă), export note canonice în 06_INBOX/RAW_IMPORTS/.
5. Dashboard: 4 KPI + grafic volume lunare (bare, desenate nativ) + top medii utilizate +
   ultimele evenimente de audit.
6. Jurnal Audit: timeline vertical cu blocuri înlănțuite vizual (linie + noduri hash), verificare
   integritate cu raport pe bloc, inspector JSON canonic per eveniment.
7. Gestiune Operatori & Sistem: CRUD operatori (doar rol Administrator), clearance ca badge,
   resetare PIN cu Four-Eyes, backup criptat + restore cu avertisment anti-rollback explicit.

## 7. DEFINITION OF DONE
- `dotnet build -warnaserror` verde; `dotnet test` verde; zero emoji în XAML; zero string-uri
  de secrete; zero apeluri de rețea (verificat cu audit pe System.Net în cod).
- Flux complet demonstrabil: conectare stick USB → apare live < 1s → amprentare → transfer nou cu
  parsare automată din numele fișierului → hash cu progres → DLP pass → Four-Eyes → PV PDF generat →
  verificare integritate lanț OK → deconectare stick → rândul se marchează deconectat.
- Scenarii negative testate: mediu neamprentat respins, plafon clasificare respectat HARD,
  executabil deghizat blocat, rollback DB detectat la pornire, al doilea PIN identic cu primul
  operator refuzat.
- README actualizat + docs/SECURITY.md (model de amenințare, inclusiv limita BadUSB) +
  docs/DEPLOYMENT.md (publish self-contained, semnare, AppLocker).

Livrează pe etape, în această ordine: (1) restructurare proiecte + .NET 10 + DI/MVVM schelet,
(2) design system + shell UI, (3) device control nativ, (4) hardening securitate, (5) module,
(6) sanitizare + PV-uri, (7) teste + documentație. După fiecare etapă: build + rezumat al diff-ului.
```

---

## Note finale pentru tine (nu fac parte din prompt)

1. **Ordinea contează**: dacă dai promptul unui agent, lasă-l să facă etapa 1 (structură) separat și verifică-i build-ul înainte să-l lași la design — altfel amestecă refactoring cu re-stilizare și rezultă haos în diff-uri.
2. **Fluent + tokens custom** e compromisul corect pentru air-gapped: temă de sistem întreținută de Microsoft ([disponibilă din .NET 9, extinsă în .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)), zero librării UI terțe de auditat, iar identitatea „Tactical" vine din tokens, nu din dependențe.
3. **Cea mai valoroasă îmbunătățire de securitate din tot documentul** este ancora anti-rollback + Four-Eyes criptografic — fără ele, lanțul SHA-256 e o demonstrație, nu o probă. Cu ele, poți susține integritatea registrului în fața unui control INFOSEC.
4. Referințe normative verificate la zi: [NIST SP 800-88 Rev. 2 — final, 26 sept. 2025](https://csrc.nist.gov/pubs/sp/800/88/r2/final), [IEEE 2883-2022 — standard activ](https://standards.ieee.org/ieee/2883/10277/), [.NET 10 LTS — suport până în nov. 2028](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
