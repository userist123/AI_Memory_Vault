# PROCEDURĂ OPERAȚIONALĂ STANDARD (SOP-INFOSEC-02)
## Sanitizarea și Atestarea Digitală a Mediilor de Stocare Clasificate

**Nivel de clasificare vizat:** Până la STRICT SECRET / NATO SECRET / COSMIC TOP SECRET / EU TOP SECRET  
**Cadru normativ:** HG nr. 585/2002, Metodologia INFOSEC 14 (ORNISS), Directiva NATO AC/35-D, NIST SP 800-88r2, IEEE 2883-2022  
**Platformă tehnică utilizată:** Secure Sanitization Engine (TOE-SSE-v1)

---

### 1. SCOP ȘI DOMENIU DE APLICARE
Prezenta procedură stabilește etapele obligatorii pentru distrugerea logică ireversibilă (sanitizarea) datelor de pe mediile de stocare electronice (NVMe SSD, SATA SSD, HDD), în vederea declasificării, reutilizării în cadrul aceluiași Sistem Informatic și de Comunicații (SIC) sau casării fără hârtie.

---

### 2. COMPONENȚA COMISIEI ȘI CONTROLUL DUAL
Pentru orice mediu care a vehiculat informații SECRET sau STRICT SECRET, operațiunea se execută obligatoriu de către o comisie compusă din:
1. **Operator INFOSEC** — personal tehnic autorizat să opereze stația de sanitizare.
2. **Martor / Responsabil de Securitate SIC** — ofițer de securitate desemnat să supravegheze operațiunea.

Ambii membri trebuie să fie dotați cu **legitimație militară/de serviciu cu cip (Smartcard)** ce conține un certificat calificat activ pentru semnătură electronică.

---

### 3. ETAPE OPERAȚIONALE OBLIGATORII

#### Etapa I — Pregătirea și Identificarea Suportului
1. Suportul de stocare se demontează din sistemul gazdă și se notează seria fizică (SN) de pe etichetă.
2. Se conectează suportul exclusiv la un port intern nativ (PCIe / M.2 sau SATA) al stației de sanitizare offline.  
   *(Conectarea prin adaptoare sau punți USB externe este interzisă de politică și va bloca procedura).*

#### Etapa II — Execuția pe Stația Bootabilă Offline (TOE)
1. Se pornește stația de la mediul bootabil securizat USB/UEFI în regim complet offline (Air-gapped).
2. Se selectează discul din lista afișată (descoperit direct prin `/sys/block`).
3. **Măsură anti-eroare:** Operatorul introduce manual ultimele 4 caractere din seria fizică a discului.
4. Ambii membri ai comisiei își introduc indicativele pentru autorizarea duală.
5. Sistemul emite comanda hardware nativă `NVMe Sanitize (Block Erase)`.  
   *(Metodele ATA/SCSI și Cryptographic Erase necesită module dedicate de driver; comanda de bază suportată în versiunea curentă este NVMe Block Erase pe magistrală PCIe/M.2 nativă).*
6. Sistemul monitorizează logul `SSTAT` conform NVMe Base Spec 2.2 Table 278 până la finalizarea cu succes (`0x1 = COMPLETED_SUCCESS`).
7. La finalizare, sistemul execută verificarea eșantionată a suprafeței logice accesibile (LBA check).  
   *Notă tehnică:* Verificarea LBA atestă returnarea de zerouri pe spațiul logic accesibil prin OS; nu constituie citire a blocurilor de rezervă sau over-provisioning (a căror alterare depinde exclusiv de comanda internă NVMe Sanitize).
8. Se exportă fișierul manifest semnat de platformă (TPM RSA-PSS-SHA256) pe stick-ul USB dedicat de transfer.

#### Etapa III — Semnarea Calificată și Înregistrarea (Aplicația Windows)
1. Stick-ul de transfer se introduce în stația de lucru de birou conectată la Registratura Electronică.
2. Se deschide aplicația grafică `Punte INFOSEC`.
3. Se selectează fișierul manifest exportat.
4. Operatorul și Martorul își introduc cartelele cu cip în cititoarele USB și tastează codurile PIN.
5. Se completează Numărul de Inventar SIC al suportului.
6. Se apasă butonul **„Semnează Calificat și Transmite la Registratură”**.

---

### 4. REZULTAT ȘI ARHIVARE FĂRĂ HÂRTIE (ZERO PAPER)
- Sistemul generează automat fișierul `PV_SANITIZARE_<SN>.pdf` semnat conform standardului PAdES (recunoscut nativ de Adobe Acrobat Reader) și pachetul de metadate JSON.
- Documentul are valoare deplină de înscris autentic conform Regulamentului UE 910/2014 (eIDAS) și Legii nr. 455/2001.
- **Tipărirea pe suport de hârtie este interzisă**, fișierul fiind arhivat direct în baza de date a Registraturii Electronice a SIC.
