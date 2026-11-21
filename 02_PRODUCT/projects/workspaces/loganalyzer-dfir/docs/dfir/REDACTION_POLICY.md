# Politica de redactare la export (master plan 6.15)

Cod: `LogAnalyzer.Dfir.Core/Privacy/Redaction.cs` și `PiiMasker` (categoriile și restaurarea celor păstrate). Interfață: pagina „Raport și arhivă” din
„Investigație completă”, secțiunea „Export cu date personale mascate”. Teste: `RedactionPolicyTests` (6).

## Ce face

- **Ce se mască** se alege pe categorii: utilizatori, stații și domenii, adrese IP și MAC, e-mailuri, SID-uri, numere de serie.
  - Mascarea se face întâi pe tot textul, apoi categoriile păstrate se pun la loc. Așa, un text care amestecă o adresă și un nume rămâne coerent.
  - Aceeași valoare primește același pseudonim în raportul PDF și în cronologia CSV.
  - Un nume de cont în formă de e-mail (UPN) urmează politica pentru e-mailuri. Dacă stațiile rămân mascate, un nume de domeniu din interiorul unei valori puse la loc (de exemplu `C:\Users\ion.CONTOSO`) se mască din nou.
- **Reversibilă sau ireversibilă:**
  - **Ireversibilă** (implicit): tabela pseudonim → valoare nu se scrie nicăieri; exportul nu poate fi inversat de nimeni.
  - **Reversibilă:** tabela categoriilor mascate se păstrează în `Reports/Redactions/RED-….map.dpapi`, criptată cu DPAPI pentru contul Windows care a făcut exportul, cu o entropie legată de identificatorul înregistrării.
- **Previzualizare:** înainte de export se văd primele rânduri care se schimbă (constatări și cronologie), „în caz” lângă „în export”.
- **Înregistrarea redactării:** fiecare export mascat scrie `Reports/Redactions/RED-yyyyMMdd-HHmmss.json`, o singură dată, doar pentru citire. Înregistrarea conține:
  - politica, operatorul, tipul exportului;
  - amprenta SHA-256 a fișierului exportat;
  - numărul de valori înlocuite pe categorie;
  - amprenta stării cazului din care s-a făcut exportul (indexul probelor, constatările și cronologia);
  - versiunea generatorului;
  - amprenta tabelei, pentru exporturile reversibile.

  Auditul cazului primește `redaction.recorded`, cu amprenta înregistrării.
- **Dezvăluirea unui pseudonim** (doar pentru exporturi reversibile):
  - acceptă doar un pseudonim (Utilizator-3, IP-2 …): o valoare originală scrisă din greșeală nu ajunge în audit;
  - cere un motiv; motivul și operatorul se curăță de caractere de control și ghilimele înainte de audit;
  - urmează doar tabela cu numele fixat de identificatorul înregistrării;
  - merge doar pentru contul Windows care a făcut exportul, pe aceeași stație;
  - refuză o tabelă a cărei amprentă nu mai corespunde înregistrării.

  Auditul primește `redaction.revealed`, cu pseudonimul, operatorul și motivul. Valoarea originală nu intră niciodată în audit.
- **Probele nu se schimbă:** redactarea lucrează pe copii ale rândurilor și scrie doar fișierul exportat și înregistrarea. Testul `Redacting_never_changes_the_case_evidence_or_analysis…` compară amprentele tuturor fișierelor cazului, înainte și după.

- **Caz doar pentru citire** (sigilat sau închis): înregistrarea nu se scrie în el. Exportul reversibil este refuzat de la început, pentru că tabela nu ar avea unde să fie păstrată.

## Limite

- `Redactions.List` citește înregistrările așa cum sunt pe disc. Amprenta fiecăreia este în auditul cazului (`record_sha256`), dar lista nu o reverifică. Fișierele sunt doar pentru citire, iar o modificare se vede comparând cu auditul.

- Detectarea datelor personale este cea a `PiiMasker`: numele învățate din câmpurile cunoscute ale cronologiei, plus tiparele (IP, e-mail, SID, căi de profil, seriale). Un nume scris doar în text liber, care nu apare nicăieri într-un câmp cunoscut, poate rămâne nemascat. Previzualizarea există ca să fie văzut.
- O mascare ireversibilă păstrează egalitatea: două apariții ale aceleiași valori au același pseudonim. Aceasta este o alegere, pentru ca exportul să rămână corelabil.
- **Reziduuri acceptate (revizuire independentă, 2 runde, 2026-11-08):**
  - Cu utilizatorii păstrați, o persoană din registrul de utilizatori se pune la loc cu numele complet.
  - Cu stațiile păstrate, sufixul DNS al unui nume complet poate lipsi.
  - Dacă stațiile rămân mascate, un nume de stație care coincide cu o bucată a unui nume de utilizator păstrat (de exemplu stația „popescu” în ion.popescu) se mască în valoarea pusă la loc.
  - Ordinea în care masca învață numele poate face ca un câmp și rezumatul aceluiași rând să difere. Previzualizarea arată asta.
