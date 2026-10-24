---
id: "425ec3c0-9011-4e12-9462-1f91388394a3"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, requirements, product-spec]
created: "2026-10-10"
updated: "2026-10-10"
provenance:
  source_type: user
  source_ref: "Cerințele proprietarului din conversația din 2026-10-10, reformulate de Claude Code la cererea lui („refaci tu să fie și să sune mai bine”)"
  source_date: "2026-10-10"
  redaction: not_applicable
  provenance_status: complete
confidence: high
verification: unverified
relations:
  - type: related_to
    target_id: "28890ae9-095d-4d32-a374-54cd7996d9aa"
---
# Casa3D: cerințe de produs v2

Ce își dorește proprietarul, spus clar. Fiecare cerință are criterii după care se poate verifica dacă e îndeplinită.

## Viziunea
Casa3D trebuie să fie cea mai completă aplicație de proiectare a locuinței din lume, nu doar din România. Oricine, din orice țară, își desenează casa și o vede realist. Își alege culorile, materialele și dimensiunile pentru tot ce e în ea. Aplicația îi spune sincer ce nu e bine și de ce, apoi primește un dosar vizual pe care îl înțelege oricine: familia, meseriașul sau magazinul.

## C1. Reper: cele mai bune aplicații din lume
Casa3D se compară cu liderii din fiecare regiune: SUA, Europa, China, Coreea, Japonia și aplicațiile bazate pe AI. Pentru fiecare domeniu (desen, import, catalog, materiale, 3D, export, costuri, colaborare, AI, internaționalizare, accesibilitate) are cel puțin nivelul celui mai bun.
- Analiza comparativă e scrisă și actualizată în COMPETITOR_ANALYSIS_2026-10-10.md.
- Fiecare funcție lipsă are un loc în plan, cu prioritate.

## C2. Produs internațional
- Interfața e în mai multe limbi, cel puțin română și engleză, cu loc pentru altele.
- Unitățile sunt metrice sau imperiale, la alegere.
- Prețurile sunt afișate în moneda ofertei.
- Catalogul e organizat pe piețe (țară, magazin, monedă) și se poate extinde cu magazine din orice țară.
- Nicio monedă, unitate sau magazin nu e scris fix în cod.
- Șabloanele de locuințe sunt generice (garsonieră, 1, 2, 3 camere), nu legate de o țară.

## C3. Totul arată real
- Pereții, podelele, tavanele, ușile, ferestrele și mobilierul au materiale realiste: lemn, textil, metal, sticlă, gresie, vopsea.
- Lumina e credibilă: zi, seară și noapte, soare cu direcție, lumini în camere, umbre.
- Imaginea finală nu arată ca o machetă din cutii. Se verifică vizual, cu capturi comparative înainte și după.

## C4. Totul se poate personaliza
- Pentru orice obiect selectat, se pot schimba culoarea și materialul, iar dimensiunile acolo unde au sens. Obiectele sunt: pereți (inclusiv un singur perete), podea, tavan, uși, ferestre și fiecare piesă de mobilier.
- Dimensiunile se pot tasta exact: lungimea peretelui, camera, ușa și fereastra.
- La mobilier, dimensiunea se alege dintre variantele reale ale produsului. O dimensiune care nu există la produs devine piesă „pe comandă”, cu preț necunoscut, nu inventat.
- Culorile alese ghidează catalogul: când alegi o culoare, primești întâi produsele disponibile în culoarea aceea sau într-una care se potrivește.
- Orice schimbare se vede imediat în plan și în 3D și intră în buget.

## C5. Consilierul: „așa nu e bine”
- O funcție analizează proiectul și spune, pe înțeles, ce nu e bine, de ce și cum se repară.
- Verifică circulația și spațiile libere, uși și ferestre blocate și proporțiile mobilei față de cameră.
- Verifică armonia și contrastul culorilor, numărul de culori, lumina insuficientă pe tipul camerei și ergonomia (distanța televizorului, biroul față de fereastră), plus bugetul depășit.
- Fiecare observație are gravitate (blochează, atenție, sugestie), obiectele vizate evidențiate în plan și o propunere concretă de corectare.
- Regulile sunt deterministe și testate. Nu sunt opinii inventate de un model.

## C6. Export vizual
- Dosarul exportat (PDF prin tipărire) arată vizual casa și ce se face în fiecare cameră.
- Conține o imagine 3D de ansamblu, planul la scară cu cote și tabelul suprafețelor.
- Pentru fiecare cameră are o pagină cu imaginea 3D a camerei, planul ei și lucrările: finisaje cu cantități (pardoseală, vopsea, faianță, plintă, corpuri de iluminat), manopera, mobilierul cu prețuri și totalul camerei.
- La final are lista de cumpărături pe magazine și bugetul total, cu prețurile necunoscute numărate separat.
- Oricine îl înțelege fără explicații.

## C7. Ce rămâne valabil din cerințele anterioare
- Funcționează pe telefon: vederea 3D și joystick-ul pentru plimbare.
- Reviziile sunt înghețate și linkurile partajate sunt doar pentru vizualizare.
- Apelul real la un model AI rămâne amânat până la decizia proprietarului. Tot ce se poate face determinist se face fără AI.
- Fiecare funcție vine cu teste, compilare și verificare în browser, iar un review independent precede orice „gata”.
