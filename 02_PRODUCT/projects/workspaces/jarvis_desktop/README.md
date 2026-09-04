# JARVIS Desktop

Command center nativ C# WPF pentru Jarvis. Fereastra desktop este singura interfata folosita de utilizator: porneste discret gateway-ul cognitiv unificat, afiseaza telemetria, memoria, agentii si canalul de chat.

## Pornire

```powershell
dotnet run --project .\Jarvis.Desktop.csproj
```

Executabilul publicat este in `bin\Release\net8.0-windows\publish\Jarvis.Desktop.exe`.

Aplicatia nu foloseste `start.bat`. Hostul C# porneste procesul Python al nucleului in background, fara consola, pe `http://127.0.0.1:3000` si il inchide impreuna cu fereastra.

## Configurare

Hostul gaseste automat radacina vault-ului cand proiectul ramane in structura existenta. Optional:

- `JARVIS_VAULT_ROOT` indica radacina vault-ului;
- `JARVIS_PYTHON` indica executabilul Python;
- modelul local implicit este `qwen2.5-coder:7b`.

Nucleul foloseste memoria SQLite/WAL, OODA, supervisorul multi-agent, skill-urile si memoria canonica deja existente in vault.

## Program Forge

Forge este un workbench pentru programe mari, nu un modul de automatizare a locuintei:

- `DECOMPOSE MISSION` cere nucleului un grafic de misiune cu faze, responsabili, livrabile, acceptanta si riscuri;
- `GENERATE BLUEPRINT` produce arhitectura, contracte, tree-ul repository-ului si planul de livrare;
- `DESIGN VERTICAL SLICE` cere cod organizat pe fisiere pentru C#/.NET sau C++;
- `EXPORT PACKAGE` scrie artefactele in `projects\generated_programs`, iar `VERIFY PACKAGE` genereaza un quality-gate local;
- exportul este sigur: fisierele sunt limitate la directorul pachetului si manifestul retine limba, modul si continutul generat.

Program Forge foloseste memoria si agentii existenti ca strat cognitiv; implementarea completa a unui produs ramane impartita in slice-uri verificabile, cu aprobare umana inainte de actiuni externe.

## Mod vocal

La pornire, JARVIS încearcă să activeze ascultarea nativă Windows. Poți spune `Jarvis` urmat de comandă sau poți apăsa `MIC OFF` / `MIC ON`.

- recunoaștere vocală continuă cu wake-word;
- răspunsul primit de la nucleul cognitiv este rostit automat;
- `SAY JARVIS` așteaptă wake-word-ul, `LISTENING` așteaptă comanda, `SPEAKING` indică răspunsul vocal;
- dacă Windows nu are un recognizer sau microfon disponibil, canalul text rămâne funcțional și interfața raportează motivul.
