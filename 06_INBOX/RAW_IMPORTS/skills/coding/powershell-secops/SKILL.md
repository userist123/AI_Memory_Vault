---
name: powershell-secops
description: Încarcă acest skill când scrii PowerShell pentru administrare Windows în mediu securizat/guvernamental — colectare loguri EVTX, backup forensic, audit, WSUS/Defender, conformitate INFOSEC. Impune scripturi idempotente, trasabile și sigure pe sisteme de producție.
---

# PowerShell SecOps

Scripturile rulează pe sisteme de producție într-un mediu reglementat (INFOSEC/MApN): fiecare script trebuie să fie trasabil, reversibil unde e posibil și să nu modifice nimic ce nu declară explicit.

## Standard obligatoriu de script

- Header: scop, autor, dată, sisteme țintă, ce MODIFICĂ (sau „read-only").
- `#Requires -Version 5.1` (baseline-ul real din mediu) + `-RunAsAdministrator` unde e cazul.
- `Set-StrictMode -Version Latest` + `$ErrorActionPreference = 'Stop'` + `try/catch` cu context în mesaj.
- `-WhatIf`/`-Confirm` (SupportsShouldProcess) pe ORICE script care modifică ceva.
- Parametri validați (`[ValidateSet]`, `[ValidateScript]`) — niciodată stringuri libere pentru căi/servere critice.
- Transcript de execuție: `Start-Transcript` într-un folder de audit cu timestamp — dovada a ceea ce a rulat.

## Colectare forensic (EVTX, registru) — reguli de integritate

- Copiază, nu atinge originalul: `wevtutil epl` pentru export EVTX, nu clear; NICIODATĂ `Clear-EventLog` într-un context forensic.
- Hash imediat după colectare: SHA-256 pentru fiecare fișier exportat, salvat într-un manifest (`manifest.csv`: fișier, hash, sursă, timestamp, operator).
- Registru: export `HKLM`/`NTUSER.DAT` cu `reg save`/shadow copy pentru hive-uri blocate; documentează metoda în manifest.
- Structură de output consecventă: `NUME-SISTEM_YYYYMMDD-HHMM/{evtx,registry,manifest}`.
- Fus orar: loghează UTC + local explicit; corelarea între sisteme moare fără asta.

## Operare în mediu restricționat

- Presupune OFFLINE: fără `Install-Module` din galerie pe sistemele țintă; scripturile sunt self-contained.
- Semnare de cod / execution policy: documentează cerința la începutul scriptului, nu ocoli cu `-Bypass` în producție fără aprobare.
- Fără credențiale în clar în scripturi — `Get-Credential` interactiv sau conturi de serviciu gestionate.
- Ținte multiple: `Invoke-Command` cu liste de calculatoare din fișier + raport de succes/eșec per mașină la final (nu opri tot la prima eroare — colectează erorile).

## Raportare

- Orice script de audit produce DOUĂ ieșiri: CSV/JSON pentru mașini + sumar lizibil pentru raport (contorizări, excepții, sisteme neaccesibile).
- Exit codes corecte (0 succes, non-zero cu semnificație documentată) — scripturile intră în automatizări mai mari.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
