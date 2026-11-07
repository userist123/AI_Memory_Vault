# Fluxul de lucru al cazului (master plan 6.1)

Cod: `LogAnalyzer.Dfir.Core/Case/CaseWorkflow.cs`. Interfață: tab-ul „Flux de lucru” din „Investigație completă”
(`InvestigationViewModel.Workflow.cs`). Teste: `CaseWorkflowTests` (20) și `WorkflowViewModelTests` (2).

## Ce face

- **Etape:** Nou → Triere → Investigație → Revizuire → Închis.
  - Din Revizuire, revizorul poate returna cazul la Investigație, cu motiv.
  - Un caz închis îl redeschide doar aprobatorul, cu motiv.
  - Orice altă tranziție este refuzată. Refuzul se scrie în auditul cazului ca `workflow.refused`.
- **Roluri:** investigator, revizor, aprobator, auditor. O persoană poate avea mai multe roluri. Regula celor patru ochi se verifică pe persoane, nu pe roluri.
  - Primul participant îl poate adăuga oricine. Acesta este fondatorul cazului.
  - Până la numirea unui aprobator, participanții îi gestionează doar fondatorul. După numire, îi gestionează doar aprobatorul.
  - Ultimul aprobator nu poate fi retras.
  - Cât cazul este în Revizuire sau Închis, nu se retrage niciun participant.
- **Sarcini:** cod T001…, titlu, persoana căreia îi este atribuită și, opțional, constatarea la care se referă.
  - O sarcină o încheie persoana căreia îi este atribuită.
  - O sarcină o anulează autorul ei sau aprobatorul, cu motiv.
  - Cât există sarcini deschise, cazul nu poate trece în Revizuire.
- **Revizuirea constatărilor:** deciziile posibile sunt confirmată, fals pozitiv, necesită probe suplimentare și exceptată.
  - Deciziile se iau numai în etapa Revizuire și cer justificare.
  - Excepția o acordă numai aprobatorul.
  - Într-un caz de echipă, cine a fost vreodată investigator, sau cine a trimis cazul la revizuire, nu decide asupra propriilor constatări.
  - O decizie nouă o înlocuiește pe cea veche în starea curentă; ambele rămân în jurnal.
- **Lista de închidere** (o închidere face doar aprobatorul):
  - cazul este în Revizuire;
  - scopul cazului este confirmat;
  - lanțurile de custodie, de audit și al fluxului sunt intacte (un lanț vechi dă avertisment, un lanț rupt blochează închiderea);
  - reverificarea probelor la deschidere a ieșit bine;
  - nu există sarcini deschise;
  - fiecare constatare de severitate ridicată sau critică are o decizie validă;
  - excepțiile acordate apar ca avertisment;
  - există un aprobator;
  - închide altă persoană decât cele din partea de investigație.
- **Închiderea:** textul pentru registrul de custodie cuprinde capetele lanțurilor de custodie și de audit, plus capătul fluxului (Seq și hash).
- **Caz cu un singur analist:** pe o stație izolată cu o singură persoană, închiderea fără a doua persoană este permisă (`WorkflowPolicy.SoloCaseAllowed`). Lista de închidere o marchează ca avertisment: „închis fără a doua persoană”.
  - Persoanele se numără din tot istoricul cazului. Cine a fost adăugat o dată și apoi retras face cazul „de echipă” pentru totdeauna.
  - Excepția se închide de tot cu `SoloCaseAllowed: false`.

## Jurnalul

- **Fișier și format:** `Logs/workflow.jsonl`, un `HashChain` (seq, prevHash, hash), în care doar se adaugă. Nimic nu se editează pe loc: o corecție este un eveniment nou.
- **Copie de control:** fiecare eveniment scris se copiază și în auditul cazului (`workflow.<tip>`). La citire, numărul de evenimente din jurnal se compară cu cel din audit:
  - un jurnal lipsă apare ca „jurnal lipsă”;
  - un jurnal mai scurt decât auditul apare ca „jurnal trunchiat”;
  - în ambele cazuri jurnalul e considerat compromis: nu se mai scrie nimic în el și închiderea este blocată.
- **Concurență:** o scriere ține un fișier `.lock` exclusiv cât verifică și adaugă. Două stații pe aceeași partajare nu pot scrie simultan. Citirile reîncearcă dacă un scriitor ține fișierul o clipă.

## Limite acceptate (revizuire independentă, 2 runde, 2026-11-07)

1. **Trunchiere simultană:** dacă se taie același număr de evenimente de la sfârșitul ambelor fișiere, `workflow.jsonl` și `audit_chain.jsonl`, ambele lanțuri rămân valide. Auditul cazului nu este sigilat cu cheie, spre deosebire de auditul autentificării.
   - Ce detectează totuși trunchierea: capătul fluxului trecut în registrul de închidere, pe hârtie, și ancora externă a capetelor (`CaseWorkspace.Anchor`).
   - Remediul complet ar fi sigilarea auditului cazului (`HashChain.RegisterHeadSeal`). Este o decizie de produs a proprietarului: cheia cere cont de administrator.
2. **Identitatea:** rolurile sunt atât de sigure cât este `OperatorIdentity.Who`. Fără autentificare cerută, acesta este contul Windows.
3. **Lista de constatări:** lista de închidere verifică lista de constatări primită de la apelant, adică rezultatul analizei deschise. Dacă o reanaliză dă alte coduri, deciziile vechi nu se transferă automat.

## Verificat în laborator (2026-11-07/08, capturi în `E:\LA-Lab\shots`, în afara depozitului)

- **Insula, LAB-WS02:** primul participant; Nou → Triere; sarcina T001; lista de închidere blocată de 4 condiții; constatarea F-0082 (ridicată) fără decizie.
- **Arhipelag, LAB-WS01:**
  - Investigație → Revizuire.
  - Închiderea de către investigator este refuzată („nu are rolul de aprobator”).
  - După numirea aprobatorului, fondatorul nu mai poate adăuga participanți.
  - Returnarea cazului cere rolul de revizor.
- **Continent, LAB-WS01:** același caz se citește identic: Revizuire, jurnal intact, constatările ridicate fără decizie. Închiderea este refuzată.
