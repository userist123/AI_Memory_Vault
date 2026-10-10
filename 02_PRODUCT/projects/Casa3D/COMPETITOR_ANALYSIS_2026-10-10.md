---
id: "9d6dcc90-0076-4aad-8458-0f91c20e91d2"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, competitor-analysis, roadmap]
created: "2026-10-10"
updated: "2026-10-10"
provenance:
  source_type: web
  source_ref: "Official feature and pricing pages of Planner 5D, HomeByMe, Floorplanner, RoomSketcher, Homestyler, Coohom, IKEA planners, Sweet Home 3D, Live Home 3D, magicplan, Cedreo, Roomstyler, Interior AI, ReimagineHome, RoomGPT (read 2026-10-10)"
  source_date: "2026-10-10"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "28890ae9-095d-4d32-a374-54cd7996d9aa"
---
# Casa3D: comparație cu aplicațiile similare și plan de completare (2026-10-10)

Cerința proprietarului (2026-10-10): „verifică și alte aplicații de genul și fă-l mai complex decât ele, cu tot ce ar mai trebui să conțină”.
Cercetarea e făcută pe paginile oficiale; afirmațiile neconfirmate sunt marcate UNVERIFIED. Prețurile concurenților sunt orientative, la data citirii.

## Ce au concurenții (rezumat)

| Aplicație | Plan | Import plan / scanare | Catalog | Finisaje | 3D | Export | Cost | Colaborare | AI |
|---|---|---|---|---|---|---|---|---|---|
| Planner 5D | da, până la 6 niveluri | plan din imagine/PDF (Premium) | da, 50% gratuit | da | randări 4K, 360 (Pro) | CAD (Pro) | calculator buget cu magazine locale (Premium) | parțial | da |
| Floorplanner | da, 3–7 niveluri | imagine/PDF cu scară | 260k modele | parțial | da | PDF/DXF pe credite | nu | parțial | nu |
| RoomSketcher | da, pereți curbi, arie automată | conversie AI, LiDAR | parțial | da | 3D Photos, 360, Live 3D (Pro) | PDF la scară | nu | parțial | da |
| Homestyler | da | JPG/PDF/DWG/DXF, calibrare scară, generare automată pereți, LiDAR | foarte mare | da | panorame 12K, video | BOM plătit | parțial | parțial | da |
| IKEA Kreativ / planificatoare | nu | scanare cameră (gratuit) | doar IKEA | nu | parțial | nu | coș IKEA | parțial | ștergere mobilă din poză |
| Sweet Home 3D | da, niveluri, cote, pereți curbi | imagine de fundal | parțial | da | da | PDF, OBJ, SVG | nu | nu | nu |
| magicplan | parțial | scanare telefon | parțial | nu | parțial | PDF/DXF/IFC + deviz XLS | deviz cantități și costuri (contractori) | multi-utilizator | UNVERIFIED |
| Cedreo | da, cote automate | import plan | parțial | da | da | DXF, JPG 300 dpi | nu | parțial | parțial |
| Interior AI / ReimagineHome | nu | nu | parțial | parțial | imagini generate | nu | parțial | nu | da (restilizare foto) |

## Unde Casa3D e deja peste ei
- Produse reale din România (IKEA și Dedeman) cu prețuri în lei, data verificării și linkuri directe. Niciun concurent verificat nu are piața românească.
- Buget complet de renovare pentru proprietar: mobilier, finisaje, manoperă (minim/așteptat/maxim), transport, montaj, rezervă. Doar magicplan face deviz, și e pentru contractori.
- Mobilare automată cu 3 variante validate geometric (uși, ferestre, circulație), pe produse din catalog, nu imagini generate.
- Revizii înghețate și linkuri partajate pe revizie, cu aplicare atomică și protecție la propuneri vechi.

## Ce îi lipsește (ordonat după valoarea pentru un proprietar din România)
1. Export tipăribil/PDF: plan la scară cu cote, tabel de suprafețe, listă de cumpărături, buget. (Floorplanner/RoomSketcher îl dau doar plătit.)
2. Unelte de productivitate în editor: duplicare, mutare cu săgețile, lungimi tastate pentru pereți și camere, unealtă de măsurare.
3. Căutare și filtre în catalog (text, magazin, preț, dimensiuni).
4. Șabloane de apartamente tipice românești (garsonieră, 2 camere, 3 camere).
5. Comparație între revizii (ce s-a adăugat, mutat, schimbat și cât costă diferența).
6. Plan importat din imagine, cu calibrarea scării, peste care se desenează.
7. Captură PNG din 3D și lumină zi/noapte.
8. Strat tehnic: prize, întrerupătoare, puncte de apă, cu numărătoare pentru electrician/instalator.
9. Finisaj pe perete (perete accent).
10. Mai multe niveluri (casă P+1), camere în L în aplicație (nucleul le are deja), comentarii pe linkul partajat.
11. Scanare LiDAR, randări fotorealiste și restilizare AI: nu se fac acum (scanarea cere aplicație nativă, AI-ul e amânat de proprietar).

## Plan de implementare și stare (actualizat 2026-10-10, ramura codex/casa3d-memory)
Cerințele complete, reformulate, sunt în CERINTE_PRODUS_v2.md. Starea de mai jos e verificată cu teste (Vitest), compilare, `next build` și browser fără interfață.
- FĂCUT: cele 4 probleme găsite la rulare (telefon, bife implicite, variantă eșuată, etichete pe linkul partajat).
- FĂCUT: formatare internațională (moneda ofertei, metric/imperial); comparație între revizii (camere, pereți, mobilier, dimensiuni, culori, cost pe monedă); căutare și filtre în catalog (text, magazin, piață, preț, dimensiuni).
- FĂCUT: export vizual A4: copertă cu imagine 3D, plan la scară cu cote, câte o pagină pe cameră (imagine 3D, plan, culori alese, finisaje cu cantități, manoperă, mobilier, subtotal), listă de cumpărături pe magazine, buget.
- FĂCUT: personalizare: culori și materiale pentru pereți, perete accent pe față, pardoseală, tavan, rame, fiecare piesă; înălțime și parapet la goluri; dimensiuni pe comandă (preț necunoscut, nu inventat); produse reale în culori apropiate (ΔE CIE Lab).
- FĂCUT: consilier („așa nu e bine”): circulație, proporții, lumină, ergonomie, culori, buget, cu motiv și reparație.
- FĂCUT: realism 3D: lumină zi/seară/noapte, direcția soarelui, ocluzie ambientală (calitate înaltă), plinte, captură PNG.
- FĂCUT: editor: duplicare, mutare cu săgețile, măsurare, ajutor pentru taste, plan din imagine cu calibrarea scării.
- ÎN LUCRU: interfață în mai multe limbi (română, engleză) cu unități la alegere; șabloane generice (garsonieră, 1, 2, 3 camere).
- URMEAZĂ: strat tehnic (prize, întrerupătoare, puncte de apă) cu numărătoare; mai multe niveluri; camere în L în aplicație; comentarii pe linkul partajat; cataloage pe piețe din alte țări (are nevoie de date reale de la magazine).
- AMÂNAT de proprietar: apelul real la un model AI. Nu se face pe web: scanare LiDAR (cere aplicație nativă).
