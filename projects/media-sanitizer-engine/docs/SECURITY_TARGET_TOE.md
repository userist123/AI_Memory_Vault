# ȚINTA DE SECURITATE (SECURITY TARGET)
## Platformă Autonomă de Sanitizare a Mediilor cu Informații Clasificate (TOE-SSE-v1)

> ⚠️ **AVERTISMENT PRIVIND STATUTUL OPERAȚIONAL:**  
> Acest document reflectă specificațiile tehnice, constrângerile de evaluare și delimitările funcționale.
> Sistemul se află în **curs de remediere după audit static (08.10.2026)**.
> Nicio funcționalitate nu este considerată validată operațional pentru medii clasificate fără testare pe banc hardware fizic dedicat și avizare formală de securitate.

---

### 1. Identificarea TOE (Target of Evaluation)
- **Denumire TOE:** Secure Sanitization Engine — UEFI Offline Boot Environment (SSE-UEFI)
- **Versiune Software:** 1.0.0-REMEDIATED (DRAFT)
- **Standarde de referință tehnice aplicabile:**
  - **NIST SP 800-88 Rev. 2 (Guidelines for Media Sanitization):**
    - Secțiunea 2.4 (Tipuri de Sanitizare: Clear, Purge, Destroy);
    - Secțiunea 4.1.2 (Clear vs. Purge pe suporturi electronice flash);
    - Secțiunea 4.5 & Anexa A (Condiții specifice pentru Cryptographic Erase: cheile MEK/FEK trebuie să fi protejat toate datele țintă pe parcursul întregului ciclu de viață; ștergerea cheilor fără atestarea istoricului criptografic nu constituie Purge);
    - Tabelele A-2 și A-3 (Tehnici recomandate pentru SSD și memorii magnetice).
  - **IEEE 2883-2022 (Standard for Sanitizing Storage):**
    - Clauza 5.2 (Sanitize Commands: Block Erase, Overwrite, Cryptographic Erase);
    - Clauza 6.3 (Verificarea post-sanitizare: eșantionarea logică LBA validează exclusiv spațiul de adrese accesibil prin OS și nu garantează verificarea celulelor de rezervă sau over-provisioning);
    - Clauza 7 (Cerințe de atestare și evidență a lanțului de custodie).
  - **NVM Express Base Specification Rev. 2.2 / Command Set 1.2:**
    - Secțiunea 5.24 (Sanitize Command - Opcodes, Sanitize Action, No-Deallocate);
    - Secțiunea 5.16 / Table 278 (Sanitize Status Log - `SSTAT` bits 2:0: `000b` = Never Sanitized, `001b` = Completed Successfully, `010b` = In Progress, `011b` = Failed);
  - **Cadrul de conformitate național și internațional:**
    - **HG nr. 585/2002** (Standardele naționale de protecție a informațiilor clasificate în România - proceduri de casare, declasificare și distrugere a suporturilor);
    - **Metodologia INFOSEC 14 (ORNISS)** (Cerințe de securitate pentru sisteme informatice și de comunicații care vehiculează informații clasificate);
    - **Directiva NATO AC/35-D/2005-REV3 & AC/35-D/2002-REV5** (Protecția informațiilor clasificate în CIS);
    - **Decizia Consiliului UE (2013/488/UE)** (Reguli de securitate pentru protejarea informațiilor clasificate ale UE).

---

### 2. Clarificări Riguroase privind Metodele de Sanitizare și Distincția AES-CTR / Crypto Erase

#### 2.1. Suprascriere (Overwrite) vs. Cryptographic Erase (CE)
- **Suprascriere cu flux pseudoaleator (ex. AES-CTR / CSPRNG):**  
  Generarea unui flux de date pseudoaleatoare cu AES în mod CTR și scrierea acestui flux pe sectoarele logice ale discului constituie exclusiv operațiunea de **SUPRASCRIERE LOGICĂ (CLEAR)**.  
  - Distrugerea cheii AES utilizate pentru *generarea* fluxului de suprascriere **NU** transformă procesul în Cryptographic Erase și **NU** sanitizează datele originale care ar rămâne în zone nesuprascrise (blocuri defecte retrase, over-provisioning sau uzură wear-leveling).
- **Cryptographic Erase (PURGE):**  
  Presupune distrugerea ireversibilă a cheilor criptografice de criptare a mediului (Media Encryption Key - MEK / Flash Encryption Key - FEK) care **au protejat efectiv toate datele țintă pe parcursul întregului ciclu de viață al suportului**.  
  - Este strict interzisă autorizarea metodei Crypto Erase doar pentru că un SSD raportează suport generic AES-256 sau pentru că unitatea a fost parțial criptată înainte dezafectării. Dacă istoricul criptografic complet nu este atestat documentar, se impune comanda internă **Block Erase** sau **Distrugere Fizică Mecanică**.
- **Absența garanțiilor absolute:**  
  Sunt eliminate orice formulări de tip „entropie perfectă”, „imposibilitate absolută cu orice mijloace” sau „distrugere cuantică”. Nivelul de protecție reziduală este evaluat statistic și criptografic conform limitelor demonstrate de standardele NIST SP 800-88r2 și IEEE 2883-2022.

#### 2.2. Limitele Verificării Eșantionate LBA (Logical Block Addressing)
- Verificarea eșantionată LBA efectuată de platformă execută citiri directe pe interfața de bloc (`/dev/nvmeX` sau `/dev/sdX`).
- **Limitare tehnologică documentată:** Această verificare poate atesta doar că spațiul logic accesibil prin magistrală returnează octeți purjați (0x00). **Ea nu demonstrează și nu poate citi direct blocurile de rezervă NAND, celulele retrase din uz sau zonele ascunse nealocate de controller**. Garanția distrugerii acestora este dată de conformitatea implementării comenzii de firmware NVMe Sanitize Block Erase conform NVM Express Base Spec 2.2.

---

### 3. Delimitarea TOE și Mediul Operațional

```text
+---------------------------------------------------------------------------------+
| MEDIU OPERAȚIONAL SECURIZAT (Stație dedicată de lucru / Cameră INFOSEC)         |
|  - Conexiuni fizice deconectate de la rețele (Air-gapped)                       |
|  - Alimentare electrică protejată (UPS / sursă stabilă)                         |
|  - Control acces fizic securizat (Personal autorizat)                           |
|                                                                                 |
|  +---------------------------------------------------------------------------+  |
|  | TOE (Target of Evaluation)                                                |  |
|  |  [UEFI Secure Boot Firmware - Chei OEM autorizate]                        |  |
|  |       │                                                                   |  |
|  |       ▼                                                                   |  |
|  |  [Kernel Minimal Imutabil (Read-Only Initramfs)]                          |  |
|  |       │ (Fără stivă de rețea, fără interpretoare shell externe expuse)   |  |
|  |       ▼                                                                   |  |
|  |  [Motorul de Sanitizare și Verificare (SSE Engine)]                       |  |
|  |       ├── Descoperire Hardware Reală (/sys/block, excludere root/loop)    |  |
|  |       ├── Motor Politici Securitate (Reguli RO / NATO / UE)               |  |
|  |       ├── Mașină de Stări Tranzacțională & Driver Linux IOCTL Native      |  |
|  |       ├── Eșantionare & Verificare LBA pe spațiul logic accesibil         |  |
|  |       └── Jurnal de Evidență Tamper-Evident SHA-256 Chained               |  |
|  +---------------------------------------------------------------------------+  |
+---------------------------------------------------------------------------------+
         │ Export unidirecțional către stick USB dedicat
         ▼
  [Manifest Tehnic Semnat TPM (RSA-PSS-SHA256)] ──► [Aplicație Windows (Non-TOE)]
                                                           ├── Validare Criptografică Manifest
                                                           ├── Autentificare Duală Smartcards
                                                           └── Generare PAdES (CMS PKCS#7)
```

---

### 4. Modelul de Amenințări (Threats)

| Identificator | Descriere Amenințare |
|---|---|
| **T.TARGET_MISMATCH** | Operatorul selectează din eroare un alt disc fizic (discul de boot sau un alt mediu util). Atenuată prin excluderea automată a discului `/proc/mounts` și confirmarea manuală a sufixului seriei. |
| **T.INCOMPLETE_ERASE** | O comandă la nivel de fișier sau o suprascriere logică lasă intacte celulele spare/over-provisioning. Atenuată prin impunerea comenzii interne NVMe Block Erase pe controller. |
| **T.TRANSLATION_BYPASS** | O punte USB-NVMe sau USB-SATA traduce eronat sau ignoră comanda Sanitize. Atenuată prin blocarea automată a punților USB nevalidate. |
| **T.POWER_INTERRUPT** | Întreruperea alimentării în timpul ștergerii lasă discul parțial purjat. Atenuată prin buclă de polling de stare cu timeout și comutare în `INCOMPLETE_ABORTED`. |
| **T.EVIDENCE_TAMPERING** | Modificarea post-execuție a raportului sau jurnalului de audit. Atenuată prin lanț de hash-uri SHA-256 și semnare asimetrică TPM (RSA-PSS) peste câmpurile canonice. |
| **T.UNAUTHORIZED_EXECUTION**| Un operator unic execută sanitizarea fără martor pe medii de nivel înalt. Atenuată prin impunerea controlului dual (operator + martor). |

---

### 5. Politici de Securitate Organizațională (OSP)

- **OSP.DUAL_CONTROL:** Pentru mediile SECRET și STRICT SECRET (și echivalente NATO/UE), este obligatorie autorizarea a două persoane distincte (Operator + Martor).
- **OSP.FAIL_TO_DESTRUCT:** Orice mediu defect, cu erori de firmware sau cu starea `FAILED` primește automat verdictul de **NECONFORM — Necesită Distrugere Mecanică**.
- **OSP.NO_LOGICAL_ONLY_ON_FLASH:** Nu se consideră sanitizare de nivel Purge simpla suprascriere logică pe memorii SSD Flash; este obligatorie comanda internă *NVMe Sanitize (Block Erase)* executată de controller.
- **OSP.ACCURATE_REPORTING:** Simulatorul de testare emite exclusiv `SIMULATED_NOT_SANITIZED` și marchează rapoartele DEMO. Niciun raport nu va atesta purjarea dacă discul nu a fost șters fizic.
