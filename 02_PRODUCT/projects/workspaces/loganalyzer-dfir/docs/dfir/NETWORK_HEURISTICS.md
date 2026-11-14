# Euristici de rețea: beaconing, scanări, procesul din spatele conexiunii (master plan 6.5)

Cod: `LogAnalyzer.Dfir.Core/Network/NetworkHeuristics.cs`, apelat la fiecare analiză. Ieșiri: constatările `NET-BEACON` și `NET-SCAN` și
`Analysis/network_links.json`, cu legăturile conexiune–proces. Teste: `NetworkHeuristicsTests`.

## Sursele

- **Conexiunile** se citesc din `Zeek:conn`, `PCAP:flow` și Sysmon 3 (conexiuni inițiate de stație).
- **Fiecare familie de surse se evaluează separat.** Ordinea este Zeek, apoi PCAP, apoi Sysmon. Aceeași conexiune văzută de doi senzori dă o singură constatare; descrierea spune unde mai apare.
- **Rânduri ignorate:** cele fără oră, difuzarea limitată 255.255.255.255, multicast, loopback și adresele rezervate. O adresă x.y.z.255 rămâne: fără mască de rețea nu se poate deosebi de o stație.
- **Adresele** se scriu într-o singură formă (IPv4 mapat în IPv6 devine IPv4, IPv6 cu litere mici), ca senzorul și Sysmon să se potrivească.
- **La limita de 200 de constatări pe regulă** apare o lacună: restul candidaților nu sunt raportați.

## Beaconing (`NET-BEACON`)

- **Condiții:** cel puțin 8 conexiuni de la aceeași sursă către aceeași destinație și port; mediana intervalelor între 10 s și 6 h; cel puțin 80% dintre intervale în ±20% din mediană.
- **Ce arată descrierea:** numărul de conexiuni, mediana, proporția de intervale regulate, abaterea standard, perioada, senzorul și procesul, dacă este cunoscut.
- **Procesul** se numește doar dacă toate conexiunile care au un proces numesc același program. Dacă sunt mai multe, se listează toate și nu se alege niciunul.
- **Severitatea:** medie. Pentru porturile serviciilor de infrastructură către adrese private, care comunică periodic prin construcție (DNS, Kerberos, NTP, LDAP, SMB, RPC, DHCP, GC), severitatea este scăzută și motivul este scris.
- **Limită:** un beacon cu întârzieri aleatoare mari (jitter) poate să nu fie găsit.

## Scanări (`NET-SCAN`)

- **Pe verticală:** cel puțin 20 de porturi diferite pe aceeași țintă, într-o fereastră glisantă de 60 s.
- **Pe orizontală:** același port pe cel puțin 20 de stații diferite, în aceeași fereastră.
- **Limită:** o scanare lentă nu se vede. Un scaner autorizat dă aceeași constatare; aceasta este prima explicație alternativă.

## Procesul din spatele conexiunii

- **Cum se leagă:** o conexiune văzută de senzor (Zeek, PCAP) se leagă de procesul care a deschis-o numai prin **tuplul exact** (IP și port sursă, IP și port destinație) și o diferență de timp de cel mult ±120 s față de evenimentul Sysmon 3.
  - Între mai multe evenimente potrivite se alege cel mai apropiat în timp, iar motivul se scrie.
  - Fiecare legătură își păstrează motivele: tuplul, diferența de timp, ProcessGuid.
- **Fără tuplu și oră, nu există legătură.** Procesul nu se ghicește după adresă.
- **Lacună:** dacă există conexiuni de la senzori, dar nicio conexiune Sysmon 3, apare o lacună. Conexiunile nu pot fi atribuite unui proces.

## Ce nu face

- Nu citește conținutul conexiunilor și nu decriptează TLS.
- Nu corectează decalajul de ceas dintre senzor și stație, în afara ferestrei de ±120 s.
