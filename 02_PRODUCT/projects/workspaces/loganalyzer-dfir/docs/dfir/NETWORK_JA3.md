# Amprenta JA3 a clienților TLS (master plan 6.5)

**Unde se află:**
- **Cod:** `PacketDecoder.Ja3` în `LogAnalyzer.Dfir.Core/Network/PacketDecoder.cs`.
- **Câmpuri:** `JA3` și `JA3Hash`, adăugate la evenimentul „TLS ClientHello” din `PcapngParser`.
- **Indicatori:** tipul `ja3` în listele IOC (`IocMatcher`).
- **Teste:** `Ja3Tests`.

**Ce conține:**
- șirul `versiune,suite de cifrare,extensii,grupuri eliptice,formate de punct`, cu valori zecimale separate prin „-”, în ordinea trimisă de client;
- valorile GREASE (RFC 8701) sunt scoase;
- amprenta este MD5-ul acestui șir.

**Cum se citește:** amprenta numește biblioteca TLS și setările ei, nu un program. Multe programe au aceeași amprentă, iar un client își poate schimba amprenta. O potrivire cu un indicator `ja3` este context pentru analist; textul potrivirii o spune („bibliotecă TLS, nu un program anume”).

**Ce nu face:**
- nu face JA3S (amprenta serverului) și nici JA4;
- QUIC rămâne raportat la nivel de flux, pentru că ClientHello-ul QUIC este criptat în pachetele Initial.

**Verificare:**
- **Unitate:** un ClientHello construit, cu GREASE în suite, extensii și grupuri, dă `771,4865-4866,0-10-11-16,29-23,0`. Un ClientHello trunchiat nu aruncă excepții.
- **Laborator:** nu a fost posibilă. Rețeaua izolată a laboratorului nu are trafic TLS: stațiile nu au Internet, iar DC-ul nu are serviciu pe 443.
