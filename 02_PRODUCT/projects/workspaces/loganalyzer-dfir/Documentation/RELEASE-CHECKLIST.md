# Checklist de release — LogAnalyzer

Se parcurge în ordine, înainte de orice distribuție externă. Recuperat din LogAnalyzer.UI 137a626 și adaptat pentru edițiile AirGapped și Network.

## 1. Build și teste

- [ ] `dotnet build LogAnalyzer.slnx -c Release`: zero erori și niciun warning nou
- [ ] `dotnet test LogAnalyzer.slnx -c Release`: toate testele trec
- [ ] Testele pe corpusul real trec local, cu `LADFIR_CORPUS` setat
- [ ] CI verde pe PR-ul de release (`loganalyzer-dfir-build.yml`: `build-test` și `package` pentru ambele ediții)

## 2. Artefacte

- [ ] Publish cu `win-x64-singlefile` reușit pentru AirGapped și Network, local și în CI
- [ ] Executabilul pornește pe o mașină Windows curată, fără .NET instalat
- [ ] `Categories/*.json`, `Data/` și `LatoFont/` sunt prezente în output și se încarcă în UI
- [ ] Niciun `license.lic`, `.pdb` sau instrument de emitere a licențelor în pachet
- [ ] Dimensiune rezonabilă pentru self-contained (~150 MB)

## 3. Fluxuri funcționale (smoke test pe mașină curată)

- [ ] Activare offline: licența emisă din `LogAnalyzer.LicenseManager` pentru Hardware ID-ul afișat este acceptată
- [ ] Aceeași licență este acceptată și când e generată cu `LogAnalyzer.KeyGen` și cu `Generate-LicenseKey.ps1` (`CoreLicenseKeyTests` fixează formatul)
- [ ] O licență expirată sau emisă pentru alt Hardware ID este respinsă
- [ ] Import EVTX și CSV de triere; timeline-ul se populează
- [ ] Sigma/YARA rulează pe setul importat
- [ ] Rapoartele HTML și PDF se generează corect
- [ ] Exportul STIX 2.1 / MISP trece validarea
- [ ] Chain of custody: SHA-256 la intake, probe read-only, jurnalul de custodie este complet

## 4. Securitate

- [ ] Ghidurile vault-ului trec: `personal_data_guard.py`, `repository_hygiene.py`, `exempt_area_secret_scan.py`, `workflow_security_audit.py`
- [ ] Baza SQLCipher nu se poate deschide fără cheie
- [ ] Nicio dată din cazuri reale (corpus, rapoarte de incident) în repository sau în pachet

## 5. Release

- [ ] Versiunea confirmată în artefact
- [ ] PR de release revizuit și integrat în `main`
- [ ] Tag pe `main` și GitHub Release creat manual, cu artefactele din CI
- [ ] (Post-MVP) Semnare Authenticode
