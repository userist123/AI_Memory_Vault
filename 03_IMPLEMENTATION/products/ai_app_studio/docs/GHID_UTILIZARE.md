# AI App Studio — ghid de utilizare

AI App Studio rulează pe PC-ul tău (Windows). Scrii ce aplicație vrei, AI-ul (Claude) face un plan, tu îl aprobi,
iar aplicația apare live în rama de telefon, tabletă sau desktop. Totul rămâne pe PC (127.0.0.1).

## 1. Instalare (o singură dată)

1. Deschide PowerShell în folderul produsului (`03_IMPLEMENTATION\products\ai_app_studio`).
2. Rulează:
   ```
   powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
   ```
   Scriptul verifică și pregătește tot: dependențele Python (`uv sync`), interfața (`pnpm install`), fereastra
   studioului (`cargo build`), pornește Docker Desktop, construiește imaginile de build lipsă, verifică dacă
   Claude CLI e autentificat și pune pe Desktop scurtătura **AI App Studio**.
3. La final vezi un tabel. Fiecare rând trebuie să fie `OK`. Pentru `LIPSĂ` sau `EROARE`, coloana „Ce_faci” spune
   exact ce ai de făcut; apoi rulezi `setup.ps1` din nou (poți să-l rulezi de oricâte ori).

Ce trebuie să existe pe PC: uv, Node.js + pnpm, Docker Desktop, Claude Code CLI (autentificat), opțional Rust
(pentru fereastra proprie; fără el se deschide o fereastră Chromium) și WSL (doar pentru emulatorul Android).

## 2. Pornire

- Dublu-clic pe **AI App Studio** de pe Desktop. Se deschide fereastra studioului; consola rămâne minimizată.
- Echivalent din PowerShell: `.\scripts\start_studio.ps1`
- Închiderea ferestrei oprește tot ce a pornit studioul.

`.\scripts\run_studio.ps1` fără parametri nu mai pornește nimic: îți afișează comanda corectă.

## 3. Faci o aplicație, pas cu pas

1. În caseta din stânga sus scrie ce vrei, de exemplu: *„Fă-mi o aplicație de notițe pentru telefon”*.
2. În lista de sub casetă alege **New project** (sau un proiect existent ca să-l modifici).
3. Apasă **Start run**.
4. În câteva secunde apare jos, la **APPROVALS**, cardul **Plan gate** cu pașii propuși. Citește-i și apasă
   **Approve plan** (sau **Edit** ca să ceri altceva).
5. AI-ul scrie fișierele (le vezi live în mijloc), construiește aplicația și o arată în dreapta.
6. Pe bara de sus vezi starea (`running`, `finished`, `failed`) și costul rulării (ex. `$0.24 / $2.00`).

Durata tipică pentru o aplicație simplă: sub un minut. Bugetul maxim pe rulare este de 2 USD.

### Proiectele tale
Pe bara de sus, lângă numele aplicației, lista **Proiect** arată toate proiectele, fiecare cu ora ultimei construiri
(sau „neconstruit încă”). Alegi alt proiect din listă și cronologia, fișierele și ramele trec pe proiectul ales.
**Proiect nou** deschide un formular mic: nume și șablon (**Gol**, **Cu file (tabs)**, **Listă și detalii**).
**Șterge** cere confirmarea ta (aprobare); fișierele merg în coș. Cât timp rulează AI-ul, lista și ștergerea sunt
blocate; dacă bara rămâne pe `running` deși serverul nu mai are nicio rulare (de exemplu după o repornire), lista se
deblochează singură în câteva secunde.

### Telefon, tabletă, desktop
Tab-urile din dreapta: **Phone 390×844**, **Tablet 834×1112**, **Desktop 1440×900** arată aceeași aplicație în rame
diferite. Dacă nu s-a construit încă nimic, rama spune „Nicio aplicație construită încă”.

### Android (emulator)
Tab-ul **Emulator** arată emulatorul Android doar când rulează. Cere-i AI-ului, de exemplu: *„Fă și varianta Android și
arat-o pe telefon”*. Necesită WSL pregătit o dată cu `wsl\install.ps1` și cel puțin 8 GB liberi pe C:.

### Modificări
Alege proiectul în listă, scrie ce vrei schimbat (*„adaugă culori pentru notițe”*) și apasă din nou **Start run**.

## 4. Chat cu AI-ul

Panoul **Chat cu AI-ul** (în stânga) îți permite să vorbești cu AI-ul despre proiectul ales, tură cu tură:
1. Alege proiectul în lista panoului (implicit e proiectul deschis).
2. Scrie ce mai vrei (*„fă butonul + verde”*) și apasă **Trimite**.
3. Fiecare mesaj pornește o rulare obișnuită: vezi pașii în cronologie, iar dacă AI-ul propune un plan, cardul de
   aprobare apare chiar sub mesajul tău.
4. AI-ul își amintește turele anterioare. Eticheta de lângă titlu arată cum: în profilul complet continuă aceeași
   sesiune Claude (ca un chat normal; conversația se salvează și în istoricul tău Claude, în `~/.claude/projects`), în
   profilul izolat primește un rezumat al turelor anterioare și nu se salvează nimic în istoricul Claude.

Cât timp rulează ceva, butonul de trimitere e blocat: o singură rulare odată. Textul scris în caseta de sus
(**Start run**) rămâne păstrat pentru fiecare proiect când treci de la unul la altul.

## 5. Export

Sub listă ai trei butoane pentru proiectul ales:
- **Export web (zip)** — site-ul gata de publicat;
- **Export APK** — aplicația Android (debug) de instalat pe telefon. Dacă proiectul nu are încă un APK construit (sau
  cel existent e mai vechi decât ultimele modificări), studioul îl construiește singur la clic — poate dura câteva
  minute; panoul arată progresul;
- **Export desktop (exe)** — aplicația Windows (.exe, nesemnată).

Studioul îți cere o confirmare (legată de amprenta exactă a fișierului, sha256), verifică să nu existe parole sau chei
în el, apoi îți afișează calea fișierului exportat și butonul **Deschide folderul**.

## 6. Probleme și soluții

| Ce vezi (exact) | Ce înseamnă | Ce faci |
|---|---|---|
| `the UI modes of this script load the vite dev server and need -Dev (production uses the packaged app)` | versiune veche a scriptului, pornită fără parametri | folosește scurtătura sau `.\scripts\start_studio.ps1` |
| Rularea apare `finished in 1s` (acum: `failed in 1s`), 0 tokeni, mesajul „Claude CLI nu a pornit corect” | Claude CLI nu a trimis inițializarea (cauza reparată: hook-urile tale SessionStart erau tratate ca eroare) | dacă reapare: în PowerShell rulează `claude`, verifică login-ul, apoi Start run din nou |
| Ecran negru în dreapta | era tab-ul **Emulator** fără emulator pornit, deschis automat de „Follow the AI” | reparat: la final studioul arată aplicația în tab-ul Phone; Emulatorul are acum un mesaj explicativ |
| Fereastra arată o interfață veche (lipsește „Use my Claude setup”) | fereastra Tauri fusese construită cu o copie veche a interfeței | reparat: pornirea rulează `cargo build`; manual: `cargo build` în `apps\desktop\src-tauri` |
| `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY` | `pnpm install` voia să șteargă `node_modules` fără să poată întreba | `setup.ps1` rezolvă singur; manual: `pnpm install --config.confirmModulesPurge=false` |
| Multe rânduri `ConnectionAbortedError: [WinError 10053]` în consolă | fereastra reîncărca previzualizarea; inofensiv | reparat (nu mai apar) |
| `Docker Desktop is not running ...` la build | Docker e oprit | pornește Docker Desktop, așteaptă „Engine running”, apoi Start run |
| `build image aas-web-build:dev is not available locally ...` | lipsesc imaginile de build | rulează `.\scripts\setup.ps1` |
| `the Claude Code CLI was not found when the studio started` | Claude Code nu e instalat sau nu e în PATH | instalează Claude Code, apoi repornește studioul |
| Planul nu apare / `Planul nu a fost aprobat la timp` | cardul Plan gate a așteptat prea mult | apasă **Approve plan** imediat ce apare cardul |
| `port 17875 is already in use` | studioul e deja deschis | folosește fereastra deschisă sau închide-o și pornește din nou |
| `S-a atins bugetul maxim al rulării` | rularea a consumat 2 USD | pornește încă o rulare pe același proiect |
