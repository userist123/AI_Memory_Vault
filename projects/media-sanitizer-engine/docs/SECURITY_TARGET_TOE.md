# ȚINTA DE SECURITATE (SECURITY TARGET)
## Platformă Autonomă de Sanitizare a Mediilor cu Informații Clasificate (TOE-SSE-v1)

### 1. Identificarea TOE (Target of Evaluation)
- **Denumire TOE:** Secure Sanitization Engine — UEFI Offline Boot Environment (SSE-UEFI)
- **Versiune Software:** 1.0.0-PROTOTYPE
- **Standarde de referință:** 
  - Metodologia INFOSEC 14 (ORNISS)
  - HG nr. 585/2002 pentru aprobarea Standardelor naţionale de protecţie a informaţiilor clasificate în România
  - Directiva NATO AC/35-D/2005-REV3 & AC/35-D/2002-REV5
  - Decizia Consiliului UE (2013/488/UE)
  - NIST SP 800-88 Rev. 2 (Guidelines for Media Sanitization)
  - IEEE 2883-2022 (Standard for Sanitizing Storage) & IEEE 2883.1-2025
  - NVM Express Base Specification Rev. 2.2 / Command Set 1.2
  - ATA/ATAPI-8 / ACS-4 Sanitize Features

---

### 2. Delimitarea TOE și Mediul Operațional

```text
+---------------------------------------------------------------------------------+
| MEDIU OPERAȚIONAL SECURIZAT (Stație dedicată de lucru / Cameră INFOSEC)         |
|  - Conexiuni fizice deconectate de la rețele (Air-gapped)                       |
|  - Alimentare electrică protejată (UPS / sursă stabilă)                         |
|  - Control acces fizic securizat (Personal autorizat ORNISS)                    |
|                                                                                 |
|  +---------------------------------------------------------------------------+  |
|  | TOE (Target of Evaluation)                                                |  |
|  |  [UEFI Secure Boot Firmware]                                              |  |
|  |       │                                                                   |  |
|  |       ▼                                                                   |  |
|  |  [Kernel Minimal Imutabil (Read-Only Initramfs)]                          |  |
|  |       │ (Fără stivă de rețea, fără shell/interpretor expus)               |  |
|  |       ▼                                                                   |  |
|  |  [Motorul de Sanitizare și Verificare (SSE Engine)]                       |  |
|  |       ├── Submodul Descoperire Hardware & Detecție Topologie / Punte      |  |
|  |       ├── Submodul Politici de Securitate (Reguli RO / NATO / UE)         |  |
|  |       ├── Submodul Mașină de Stări & Execuție Comenzi Native              |  |
|  |       ├── Submodul Eșantionare & Verificare LBA                           |  |
|  |       └── Submodul Jurnal de Evidență Tamper-Evident (SHA-256 Chained)    |  |
|  +---------------------------------------------------------------------------+  |
+---------------------------------------------------------------------------------+
         │ Export unidirecțional către stick USB autorizat
         ▼
  [Manifest Tehnic Semnat Ed25519] ──► [Aplicație Windows Auxiliară (Non-TOE)]
```

---

### 3. Modelul de Amenințări (Threats)

| Identificator | Descriere Amenințare |
|---|---|
| **T.TARGET_MISMATCH** | Operatorul selectează din eroare un alt disc fizic (ex: discul de boot al sistemului sau un mediu neclasificat ce conține alte date active). |
| **T.INCOMPLETE_ERASE** | O comandă software de nivel înalt (ex: scriere de zerouri prin filesystem) lasă intacte zonele de memorie over-provisioned, blocurile de rezervă sau zonele ascunse (HPA, DCO). |
| **T.TRANSLATION_BYPASS** | O punte USB-SATA sau USB-NVMe raportează fals acceptarea comenzilor firmware native (Sanitize) fără a le transmite fizic către memoria flash/platane. |
| **T.POWER_INTERRUPT** | Întreruperea alimentării electrice sau resetarea sistemului lasă un mediu într-o stare parțial ștearsă ce ar putea fi marcată eronat ca „sanitizată”. |
| **T.EVIDENCE_TAMPERING** | Modificarea post-execuție a raportului sau jurnalului de audit pentru a pretinde că un suport clasificat a fost distrus corespunzător. |
| **T.UNAUTHORIZED_EXECUTION**| Un operator unic sau neautorizat inițiază operații pe medii de nivel înalt (STRICT SECRET / COSMIC TOP SECRET) fără aprobarea martorului / ofițerului de securitate (lipsa controlului dual). |

---

### 4. Politici de Securitate Organizațională (OSP)

- **OSP.DUAL_CONTROL:** Pentru mediile ce conțin informații SECRET și STRICT SECRET (sau echivalent NATO/UE), este obligatorie autentificarea a cel puțin doi utilizatori distincți (Operator + Responsabil de Securitate / Martor).
- **OSP.FAIL_TO_DESTRUCT:** Orice mediu defect, inaccesibil, care raportează erori de firmware în timpul comenzii Sanitize, ori aflat în spatele unei punți nevalidate primește automat verdictul de **NECONFORM — Necesită Distrugere Fizică Mecanică**.
- **OSP.NO_OVERWRITE_ON_FLASH:** Nu se acceptă metoda de simplă suprascriere logică prin scrieri repetate de blocuri pe medii Flash/SSD; este mandatorie comanda internă *NVMe Sanitize (Block Erase / Crypto Erase)* sau *ATA Sanitize*.
- **OSP.SIGNED_EVIDENCE:** Orice finalizare de lucrare trebuie însoțită de un manifest tehnic semnat digital și un lanț de audit SHA-256 verificabil.

---

### 5. Cerințe Funcționale de Securitate (SFRs — stil Common Criteria)

1. **FCS_COP.1 (Operații Criptografice):** Calculare SHA-256 pentru fiecare tranziție de stare din jurnal și semnare Ed25519 a raportului final.
2. **FDP_ACC.1 (Controlul Accesului la Comenzi):** Interzicerea emiterii comenzilor de ștergere dacă nu este încărcat un profil de politică valid semnat.
3. **FIA_UAU.2 (Autentificare Utilizatori):** Autentificarea cu roluri diferențiate (Operator vs. Martor) înainte de deblocarea stării de execuție.
4. **FRU_FLT.2 (Toleranță la Erori):** Tratarea pierderii semnalului sau a deconectării discului ca eșec critic ireversibil, cu refuzul de validare.
5. **FMT_MOF.1 (Managementul Funcțiilor de Securitate):** Imutabilitatea parametrilor de configurare în timpul rulării sesiunii de boot.
