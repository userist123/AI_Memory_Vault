---
id: "knw-leg-m172-2021-0001"
type: knowledge
category: legal.defense.classified_information
tags:
  - mapn
  - ordin-m172-2021
  - informatii-clasificate
  - medii-de-stocare
  - cdc
  - csnr
  - dcism
  - infosec
  - air-gap
created: 2026-09-06
updated: 2026-09-06
status: normalized
provenance:
  source_type: ai
  source_ref: "06_INBOX/Legi/M 172-2021.docx (Ordinul M.172/2021 al Ministrului Apararii Nationale)"
  source_date: 2026-09-06
  original_path: "06_INBOX/Legi/M 172-2021.docx"
  extraction_date: 2026-09-06
  redaction: not_applicable
confidence: high
verification: unverified
lifecycle: NORMALIZED
provenance_status: complete
relations:
  - type: related_to
    target_id: knw-leg-hg585-2002-0001
  - type: related_to
    target_id: knw-leg-l153-2017-0001
  - type: related_to
    target_id: 99522c1a-b212-4571-b4d8-7dbbba2a3462
  - type: related_to
    target_id: c1a01101-7291-49fa-9481-22904c10c001
  - type: related_to
    target_id: 4c5884fe-f24e-426e-a012-1414dbddae23
---

# Ordinul Ministrului Apărării Naționale nr. M.172/2021 — Normele privind Protecția Informațiilor Clasificate în MApN

## Cadrul de Aplicare și Autoritatea Militară de Securitate

Ordinul nr. M.172/2021 din 19 august 2021 stabilește normele interne ale **Ministerului Apărării Naționale (MApN)** pentru protecția informațiilor naționale, NATO, UE și Echivalente clasificate. Se aplică întregului personal militar și civil din armată, unităților militare (UM), marilor unități, categoriilor de forțe și operatorilor economici care execută contracte clasificate în beneficiul MApN.

Autoritatea Desemnată de Securitate (ADS) pentru sfera de competență a MApN este **Direcția Contrainformații și Securitate Militară (DCiSM)** din cadrul Direcției Generale de Informații a Apărării (DGIA).

---

## 1. Structurile Speciale de Gestionare a Documentelor Clasificate (Art. 43–48)

În cadrul MApN, gestiunea documentelor clasificate se realizează prin trei tipuri distincte de entități:
1. **CDC (Centrul de Documente Clasificate)**: Înființat pentru gestionarea exclusivă a documentelor ce conțin informații naționale clasificate (SSID, SS, S, SSv);
2. **CSNR (Punctul de Control Subregistru)**: Înființat pentru gestionarea documentelor naționale, NATO, UE și Echivalente clasificate, precum și a celor transmise către parteneri externi;
3. **CDC autorizat**: Înființat pentru gestionarea informațiilor naționale, NATO și UE clasificate până la nivelul NATO RESTRICTED / UE RESTREINT inclusiv.

### Rolul Personalului CDC / CSNR (Art. 46)
- Primirea, înregistrarea, verificarea integrității sigiliilor și prezentarea documentelor către comandantul unității;
- Distribuirea pe bază de semnătură titularilor autorizați;
- Împachetarea, sigilarea cu ștampile speciale și expedierea prin curier militar;
- Justificarea documentelor distruse prin proces-verbal de distrugere sau clasate;
- Gestionarea mapelor tip și a literaturii militare clasificate.

---

## 2. Sistemul de Registre de Evidență și Evidența Electronică Omologată (Art. 49–57)

### 2.1. Registrele Obligatorii (Art. 49)
1. *Fișa de consultare* a documentului Strict secret de importanță deosebită (Anexa 1 la HG 585);
2. *Registrul de evidență SSID* (Anexa 4 la HG 585);
3. *Registrul de evidență SS și S* (Anexa 5 la HG 585);
4. *Registrul de evidență SSv* (Anexa 1 la HG 781);
5. *Registrul unic de evidență* a registrelor, condicilor, borderourilor și caietelor pentru însemnări (Anexa 7 la HG 585);
6. *Condica de predare-primire* a documentelor clasificate (Anexa 8 la HG 585);
7. *Registrul de evidență a informațiilor multiplicate* (Anexa 9 la HG 585);
8. **Registrul pentru evidența și distribuirea mediilor de stocare a informațiilor (Anexa nr. 9 la Norme)**;
9. *Cererea pentru multiplicare/copiere* (Anexa nr. 10 la Norme);
10. *Procesul-verbal de distrugere* (Anexa nr. 11 la Norme);
11. *Registrul pentru evidența literaturii militare* (Anexa nr. 12 la Norme).

### 2.2. Elaborarea în Formă Electronică (Art. 51) — Cheie pentru Digitalizare
> „Registrele de evidență [...] se pot elabora în formă electronică, folosind o **aplicație informatică pentru fiecare tip de evidență, la nivelul MApN, omologată de DCiSM**, cu obligativitatea tipăririi registrelor la sfârșitul anului calendaristic și înregistrarea în Registru unic.”

Această prevedere oferă temeiul juridic direct pentru dezvoltarea de aplicații dedicate (precum `Registru-de-transferuri` în C#/WPF), cu condiția omologării tehnice de către DCiSM.

---

## 3. Regimul Mediilor Fizice de Stocare a Informațiilor (Art. 193–199, Anexele 9 și 18)

M.172/2021 cuprinde cerințe extrem de detaliate privind mediile amovibile (stick-uri USB, hard disk-uri externe/interne, SSD-uri, CD/DVD):

### 3.1. Registrul pentru Evidența și Distribuirea Mediilor de Stocare (Anexa nr. 9)
Fiecare mediu fizic introdus într-o unitate militară trebuie luat în evidență înainte de utilizare, specificând:
- Nr. crt. și Numărul de evidență atribuit mediului;
- **Tipul mediului de stocare** (USB flash drive, SSD, HDD, CD-R, DVD-R etc.);
- **Capacitatea de stocare** (GB, TB, MB);
- **Seria / Numărul de fabricare fizic al producătorului** (Hardware Serial Number — identificator imutabil);
- Nivelul de clasificare maxim permis pentru mediul respectiv;
- Destinatarul (Gradul, Numele, Prenumele, Data distribuirii, Semnătura de primire);
- Data și semnătura de restituire;
- Numărul și data procesului-verbal de declasificare / distrugere;
- Observații.

### 3.2. Fișa Mediului de Stocare (Anexa nr. 18)
Pentru mediile care conțin informații NATO/UE/Echivalente/Naționale clasificate CONFIDENȚIAL și superior se întocmește obligatoriu **Fișa mediului de stocare**, care consemnează:
- Nr. înregistrare atribuit de expeditor / Ri;
- Nivelul maxim de clasificare;
- Cantitatea de informații și numărul de fișiere stocate;
- **Dispunerea exactă a fișierelor pe mediul de stocare**: calea completă, directoare, denumire fișier, extensie (doc, pdf, ppt), mărime fișier în KB, nivel de clasificare individual per fișier, referință document, titlu document;
- Fisierele neclasificate se consemnează doar ca număr global pe ultimul rând;
- Semnăturile obligatorii: Gestionar CSNR/CDC (întocmit) și Șeful CSNR/CDC (verificat).

### 3.3. Reguli Operative de Transfer și Marcare a Mediilor (Art. 194–199)
- **Mediile reinscriptibile**: Se folosesc strict pentru lucru în SIC acreditate și la transferul de informații între acestea. Este strict interzisă utilizarea mediilor externe în alte rețele sau echipamente neacreditate.
- **Monotematică**: Pe un mediu de stocare se înscriu pe cât posibil doar informații dintr-un singur domeniu sau problematică.
- **Formula de marcare fizică a mediului (Art. 199)**:
  $$rac{	ext{Indicativ Numeric UM și Tip CSNR/CDC (ex: 01150Ri)}}{	ext{Acronim Nivel Clasificare / Nr. Registru / Data Înregistrării}}$$

---

## 4. INFOSEC în MApN și Responsabilitățile AOSSIC (Cap. II, Art. 146–154)

Structura de securitate din fiecare unitate militară coordonează **Autoritatea Operațională pentru Securitatea Sistemelor Informatice și Comunicații (AOSSIC)**:
- Asigură cooperarea între componentele protecției fizice, personalului și digitale;
- Avizează Documentația de Acreditare de Securitate (DAS) a rețelelor SIC;
- Participă la comisiile de analiză a riscurilor cibernetice;
- Avizează cererile de cont și acces la SIC acreditate;
- Ține evidența strictă a calculatoarelor și dispozitivelor private sau ale contractanților introduse în unitate;
- Asigură pregătirea de securitate a personalului utilizator.

---

## 5. Legătura cu Invariantele Arhitecturale P16–P18 din AI Memory Vault

Cerințele M.172/2021 privind mediile de stocare stau la baza invariantelor tehnice implementate în sistemele noastre:
- **P16 (Hardware Telemetry Immutability)**: Seria fizică de fabricație a suportului (Hardware Serial Number), VID, PID și capacitatea fizică sunt citite direct la nivel de kernel/OS și sunt strict read-only în UI, prevenind falsificarea hardware-ului din Anexa 9.
- **P17 (Friendly Name Isolation)**: Utilizatorul poate asocia o etichetă logică prietenoasă volumului și gestionarul responsabil, fără a altera vreodată identificatorii hardware unici.
- **P18 (Forensics & Chain of Custody Integrity)**: Orice transfer de fișiere leagă automat amprenta hardware imutabilă a mediului de stocare în jurnalul de audit tamper-evident garantat prin SHA-256.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Legislatie_HG585_2002_Protectia_Informatiilor_Clasificate]]
- [[Legislatie_Legea_Cadru_153_2017_Salarizare_Publica]]
- [[HG585_MS111_Compliance_Requirements]]
- [[Registru_Transferuri_Development_Standards]]
- [[Local_PIN_Auth_And_SQLCipher_Pattern]]
- [[WPF_Security_Strategy]]
