---
id: 9d0973ba-1c8d-556c-a448-afe6f09633a3
type: knowledge
lifecycle: REVIEW
category: architecture/weak_supervision
tags:
- mlops
- huyen
- weak-supervision
- snorkel
- labeling-functions
- data-programming
- active-learning
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-ML-Systems-Ch4"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_MLOps_Feature_Stores_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# MLOps Specializat: Supraveghere Slabă (Weak Supervision) & Programarea Datelor (Snorkel)

**Sursă**: Chip Huyen, *Designing Machine Learning Systems* (Capitolul 4)  
**Domeniu**: Generare de Date de Antrenament, Etichetare Automată & Active Learning

---

## 1. Problema Blocajului de Date Etichetate (The Labeling Bottleneck)

În sistemele ML din lumea reală, obținerea etichetelor manuale de la experți umani este extrem de lentă, costisitoare și greu de scalat la apariția unor clase noi de date.
- **Supravegherea Slabă (Weak Supervision)**: Înlocuiește etichetarea manuală cu scrierea de euristici programatice numite **Funcții de Etichetare (Labeling Functions — LFs)**.

---

## 2. Arhitectura Snorkel & Programarea Datelor (Data Programming)

O funcție de etichetare $LF_i(x)$ este o funcție deterministă care primește o mostră neetichetată $x$ și returnează:
- O etichetă din spațiul claselor $y \in \{-1, +1\}$ (sau clase discrete multiple).
- Sau `ABSTAIN` (nu se pronunță dacă mostra nu întrunește condițiile specifice).

### Tipurile de Funcții de Etichetare
1. **Reguli bazate pe tipare (Pattern / Regex LFs)**: Identifică prezența unor cuvinte cheie specifice sau a unor expresii regulate.
2. **Baze de cunoștințe externe (Distant Supervision)**: Conectează mostrele cu tabele sau ontologii externe.
3. **Modele pre-antrenate mici (Model-based LFs)**: Folosesc clasificatori rapizi de sentimente sau modele de parsare sintactică.
4. **Validatori de schemă / Invarianți de domeniu**: Verifică conformitatea câmpurilor obligatorii.

### Modelul de Generare a Etichetelor (The Label Model)
Deoarece diferite LF-uri pot intra în conflict sau se pot suprapune, Snorkel învață acuratețea și corelațiile fiecărei LF fără a cunoaște adevărul absolut (*unsupervised parameter estimation* via matrice de covarianță), generând o etichetă probabilistică moale (*soft label*) $P(Y \mid x)$ pentru fiecare mostră.

---

## 3. Playbook Operațional: Ce fac când am mii de note neclasificate în Vault?

1. **Nu le etichetez manual una câte una**: Scriu 3–5 Funcții de Etichetare (LFs) pe baza regulilor canonice de clasificare (`Classification_Protocol.md`).
2. **Aplic modelul de vot ponderat cu abstention**: Notele pe care LF-urile cad de acord cu încredere mare sunt promovate automat în `CLASSIFIED`, iar cele în conflict sunt trimise în coada de revizuire umană (`REVIEW`).
3. **Respect invarianta `I-001`**: Etichetele generate automat rămân la `verification: unverified` până când utilizatorul uman le atestă formal.
