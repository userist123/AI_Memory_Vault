# Identități și Active Directory (master plan 6.10)

Cod: `LogAnalyzer.Dfir.Core/Identity/IdentityReconstruction.cs`. Ieșire: `Analysis/identity.json`, scrisă la fiecare analiză și citită la redeschiderea cazului.
Interfață: tab-ul „Identități” din „Investigație completă” (`InvestigationViewModel.Identities.cs`). Teste: `IdentityReconstructionTests` (10).

## Ce face

- **Cheia identității este SID-ul.**
  - O redenumire (4781) adaugă un nume aceleiași identități; nu creează un cont nou.
  - Istoricul SID (4765, câmpul `SidHistory` din 4738) este o **legătură, nu o contopire**. SID-ul vechi își păstrează evenimentele și arată la cine apare în istoric; identitatea nouă îl listează. Un SID privilegiat sau bine-cunoscut (SYSTEM, grupuri builtin, RID 500/502/512/518/519/526/527) apărut în istoricul cuiva îl marchează pe acela privilegiat, cu motivul „posibil abuz al istoricului SID (T1134.005)”. Contopirea ar fi permis ca un istoric plantat să ascundă activitatea administratorului sub alt nume.
  - Evenimentele care au doar nume (4769, 4776) se leagă de un SID abia la final, după ce s-a văzut tot cazul, și numai dacă **domeniul și numele** au apărut cu un singur SID. Domeniul se compară după prima etichetă: LAB și LAB.LOCAL sunt considerate același domeniu. Un nume fără domeniu (4776 pe controler) nu se atribuie niciodată.
- **Sesiunile sunt pe stație.** LogonId are sens doar pe stația care l-a emis, deci cheia este stație + LogonId.
  - 4624 deschide sesiunea; 4634 sau 4647 o închide (încredere ridicată).
  - Fără delogare: încredere medie. Delogare fără început: încredere scăzută.
  - Același LogonId reapărut înainte de delogare: încredere scăzută, cu motivul.
  - 4672 marchează cu privilegii speciale doar o sesiune deschisă pe stația respectivă. Un 4672 scris înaintea lui 4624 (în aceeași secundă) se aplică logon-ului care urmează în cel mult 2 secunde, niciodată unei sesiuni închise mai devreme, cu același LogonId reutilizat.
  - Sesiunile sunt limitate la 100.000; peste limită apare o lacună. Activitatea rămâne în cronologie.
- **Grupuri și conturi:** creare, activare, dezactivare, ștergere, parole, blocare, redenumire; adăugări și scoateri din grupuri (4728/4732/4756 și perechile lor). Adăugarea într-un grup privilegiat (Administrators, Account/Server/Backup Operators, Domain/Schema/Enterprise/Key Admins, recunoscute după SID sau RID) marchează contul privilegiat. Autorul fiecărei modificări primește și el rândul în activitatea lui.
- **Director și GPO:** 5136/5137/5141 de pe controler; o modificare pe un `groupPolicyContainer` este o modificare de GPO, legată de obiect (DN), atribut și autor (SID), cu locatorul evenimentului.
- **Duplicate:** aceeași înregistrare (stația cu numele complet + EventRecordID + ora) importată de două ori, de exemplu două exporturi ale aceluiași jurnal de pe controler, se numără o dată. Ora face parte din cheie: după golirea jurnalului numerotarea reîncepe, iar o arhivă veche și jurnalul curent sunt evenimente diferite. web01.contoso și web01.fabrikam sunt stații diferite.
- **Lacune, nu negative:**
  - Conturi de domeniu fără jurnale de controler **ale acelui domeniu**: lacună, nu „nu s-a întâmplat nimic”. Contul de domeniu se recunoaște după SID (S-1-5-21-…) și după un domeniu diferit de numele NetBIOS al stației (cel mult 15 caractere), indiferent de limba numelor de sistem.
  - Controler fără modificări de director: auditul „Directory Service Changes” poate fi neactivat.
  - Stație cu autentificări, dar fără nicio delogare.
  - Conturi văzute doar după nume.

## Calea contului: stație → sesiune → proces → resursă

Cod: `LogAnalyzer.Dfir.Core/Identity/IdentityGraph.cs`, ieșire `Analysis/identity_graph.json`. Interfață: fila „Identități”, arborele „Calea contului”. Raport: secțiunea „Conturi, sesiuni și căi” (publicurile IT/securitate, răspuns la incident, audit, criminalistică, complet). Teste: `IdentityGraphTests`.

- **Sesiune → proces:** un proces (4688, Sysmon 1) intră într-o sesiune numai dacă are aceeași stație și același LogonId, iar ora lui este în intervalul sesiunii. O sesiune fără delogare durează până la următoarea sesiune cu același LogonId pe stație (repornire). La 4688 se folosește TargetLogonId (Windows 10 și mai nou), altfel SubjectLogonId.
- **Proces → resursă:** Sysmon 3 (conexiune), 11 (fișier creat) și 22 (DNS) prin ProcessGuid; 4663 (obiect accesat) prin aceeași sesiune și același PID, altfel sub sesiune; 5140/5145 (partajări) sub sesiune. Aceeași resursă atinsă din nou se numără, nu se repetă.
- **Fiecare rând își spune motivul** (cheile care s-au potrivit) și locatorul evenimentului.
- **Ce nu se leagă se numără ca lacună:** LogonId fără autentificare pe aceeași stație, eveniment în afara oricărei sesiuni, ProcessGuid necunoscut, limitele de 300 de procese pe sesiune și 100 de resurse pe nod. Conturile de sistem (SYSTEM, LOCAL SERVICE, NETWORK SERVICE) nu sunt puse sub o persoană.
- **Jurnal importat de două ori:** fiecare proces și acces se numără o dată (aceeași cheie ca la identități).
- **Verificat pe jurnalele reale** ale DC01 și SRV01 (laborator, 2026-11-08): 133 de procese legate de sesiunile administratorilor (de exemplu powershell, auditpol, whoami), 292 de procese ale conturilor de sistem raportate separat, niciun proces cu LogonId fără sesiune. Jurnalele de laborator nu au Sysmon și nici audit de acces la obiecte, deci nu au resurse.

## Limite

- Nu există o corecție automată a decalajului de ceas între stații; orele sunt cele din jurnale (UTC). Corelarea între stații se face pe SID și pe locator, nu pe potrivirea exactă a orei.
- Rolurile sunt deduse din evenimente: o stație este „controler” dacă are evenimente care apar doar pe controler (4768, 4769, 4771, 5136, 5137, 5141).
- Nicio linie de aici nu dovedește singură tot traseul unui cont; fiecare duce la evenimentul din care provine.
- **Reziduuri acceptate (revizuire independentă, 2 runde, 2026-11-08):**
  - Același controler scris o dată cu numele scurt și altă dată cu FQDN apare ca două stații. Jurnalele EVTX scriu de obicei același `MachineName`.
  - Domeniile se compară după prima etichetă: EMEA.contoso și EMEA.fabrikam se confundă.
  - Același rând CSV importat ca două probe diferite se numără de două ori. Fără EventRecordID, două rânduri identice din probe diferite nu pot fi deosebite de două evenimente reale.
