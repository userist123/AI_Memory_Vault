# Identități și Active Directory (master plan 6.10)

Cod: `LogAnalyzer.Dfir.Core/Identity/IdentityReconstruction.cs`. Ieșire: `Analysis/identity.json`, scrisă la fiecare analiză și citită la redeschiderea cazului.
Interfață: tab-ul „Identități” din „Investigație completă” (`InvestigationViewModel.Identities.cs`). Teste: `IdentityReconstructionTests` (10).

## Ce face

- **Cheia identității este SID-ul.**
  - O redenumire (4781) adaugă un nume aceleiași identități; nu creează un cont nou.
  - Un SID vechi trecut în istoricul SID (4765, câmpul `SidHistory` din 4738) se leagă de identitatea care îl poartă acum.
  - Evenimentele care au doar nume (4769, 4776) se leagă de un SID numai dacă același nume a apărut cu un singur SID. Un nume purtat de două SID-uri nu se atribuie niciunuia; contul rămâne „văzut doar după nume”.
- **Sesiunile sunt pe stație.** LogonId are sens doar pe stația care l-a emis, deci cheia este stație + LogonId.
  - 4624 deschide sesiunea; 4634 sau 4647 o închide (încredere ridicată).
  - Fără delogare: încredere medie. Delogare fără început: încredere scăzută.
  - Același LogonId reapărut înainte de delogare: încredere scăzută, cu motivul.
  - 4672 marchează sesiunea cu privilegii speciale doar pe stația respectivă.
- **Grupuri și conturi:** creare, activare, dezactivare, ștergere, parole, blocare, redenumire; adăugări și scoateri din grupuri (4728/4732/4756 și perechile lor). Adăugarea într-un grup privilegiat (Administrators, Account/Server/Backup Operators, Domain/Schema/Enterprise/Key Admins, recunoscute după SID sau RID) marchează contul privilegiat. Autorul fiecărei modificări primește și el rândul în activitatea lui.
- **Director și GPO:** 5136/5137/5141 de pe controler; o modificare pe un `groupPolicyContainer` este o modificare de GPO, legată de obiect (DN), atribut și autor (SID), cu locatorul evenimentului.
- **Duplicate:** aceeași înregistrare (stație + EventRecordID) importată de două ori, de exemplu două exporturi ale aceluiași jurnal de pe controler, se numără o dată.
- **Lacune, nu negative:**
  - Conturi de domeniu fără jurnale de controler: lacună, nu „nu s-a întâmplat nimic”.
  - Controler fără modificări de director: auditul „Directory Service Changes” poate fi neactivat.
  - Stație cu autentificări, dar fără nicio delogare.
  - Conturi văzute doar după nume.

## Limite

- Nu există o corecție automată a decalajului de ceas între stații; orele sunt cele din jurnale (UTC). Corelarea între stații se face pe SID și pe locator, nu pe potrivirea exactă a orei.
- Rolurile sunt deduse din evenimente: o stație este „controler” dacă are evenimente care apar doar pe controler (4768, 4769, 4771, 5136, 5137, 5141).
- Nicio linie de aici nu dovedește singură tot traseul unui cont; fiecare duce la evenimentul din care provine.
