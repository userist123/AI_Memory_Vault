---
id: "b7c41f0e-5a2d-4d8e-9f61-2c3a8e7d4b19"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, synthesis, roadmap]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Synthesis of research_design.md, research_clients_trades.md, research_matrix.md, research_rules.md in this folder (four web research passes, 2026-10-11); the business-workflow pass stopped early on the account spend limit and is covered by the matrix"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "3e458b8c-e0f1-4b06-b0d7-9a3ffae2ae1f"
---
# Casa3D: ce mai lipsește, după a doua cercetare (2026-10-11)

Patru cercetări pe internet (proiectare, clienți și meseriași, matricea concurenței, reguli și cifre RO/UE),
fiecare cu sursele ei în acest dosar. A cincea (afacerea designerului) s-a oprit la limita de cheltuieli a
contului; matricea acoperă aceleași aplicații (Houzz Pro, Programa, DesignFiles, Studio Designer, Mydoma).

## Ce spun toate cercetările în același timp

1. **Clientul nu știe ce se întâmplă.** Cele mai puternice plângeri nu sunt întârzierea, ci tăcerea:
   „când vine?”, „cât am cheltuit față de ofertă?”, „ce s-a schimbat și cât costă?”. Bugetul depășit vine
   mai ales din modificări pe parcurs nescrise (Houzz 2026: 37%; sondaj UK Renno: 65,6%). Casa3D are deja
   datele (stări de comandă, ETA, devizul, revizii), dar le vede doar designerul.
2. **Meseriașii lucrează după ghicit.** Cote neverificate, înălțimi de prize necomunicate, plan de faianță
   nedecis înainte de placare, electrocasnice alese prea târziu. Casa3D are prizele/punctele cu înălțimi,
   dar nu are plan de faianță (setting-out), cote verificate pe șantier, cartuș și set numerotat de planșe.
3. **Nimeni nu face bine finalul proiectului.** Dosar de predare (produse, garanții, întreținere,
   proces-verbal) nu există la niciun concurent; în RO procesul-verbal pornește garanțiile, iar defectele
   vizibile nenotate se consideră acceptate.
4. **Verificările de meserie sunt diferențiatorul.** Niciun instrument de business nu verifică planul;
   instrumentele CAD nu verifică regulile de bucătărie/baie. Valorile NKBA, NP 051, I5-2022 și fișele
   producătorilor (adeziv, chit, vopsea) sunt citite din surse primare; I7 (electrice) și Legea 50 doar din
   surse secundare → avertismente editabile, nu erori.
5. **Ce au toți concurenții de business și lipsește:** pontaj (timp pe proiect), plăți pe etape/facturi,
   modificări de contract (change orders), mood board ca obiect separat.

## Ordinea de implementare

| # | Ce | De ce (sursă) | Mărime |
|---|---|---|---|
| 1 | **Pe linkul clientului: livrări și buget** — fiecare produs cu stare și dată estimată (fără prețuri de achiziție/note interne); buget ofertat vs. actual | tăcerea despre livrări; buget depășit | mică |
| 2 | **Modificări cu preț (change orders)** — designerul descrie modificarea + diferența de cost; clientul o aprobă/respinge pe link; bugetul ține cont | cauza nr. 1 a depășirii | medie |
| 3 | **Termene de decizie** — pentru fiecare produs neaprobat, „decide până la” = data necesară pe șantier − timpul de livrare (implicit pe categorie, editabil) | electrocasnice alese târziu; oboseala deciziilor | mică |
| 4 | **Dosar de predare (PDF)** — produse montate cu furnizor, cod, nr. comandă, garanție implicită pe categorie, îngrijire, proces-verbal cu listă de remedieri deschise | oportunitate nefăcută de nimeni; regula RO a PV | medie |
| 5 | **Verificări de meserie** — bucătărie (triunghi ≤ 7,9 m, laturi 1,2–2,7 m, culoar 1,07/1,22 m, mașina de vase ≤ 0,91 m de chiuvetă), baie (spațiu în fața WC/lavoar, duș ≥ 0,76 m), zone electrice baie (avertisment), ventilație I5-2022, consumuri adeziv/chit/vopsea în deviz | reguli din surse primare | medie |
| 6 | **Plan de faianță** — punct de start, tăieturi < 1/3 placă semnalate, listă de tăieturi, grila pe planșă, număr real de plăci | meseriași; design | medie |
| 7 | **Cartuș + set numerotat de planșe + tabel de revizii** | planșe pentru echipe | mică |
| 8 | **Releveu pe șantier** — laturi + diagonale, verificare de închidere și unghiuri drepte, „cote verificate” + înghețarea desenului | cote greșite | medie |
| 9 | **Pontaj pe proiect** — ore pe etape, față de onorariu | matrice: standard la concurență | mică |
| 10 | Bucătărie pe module + blat; dulap la comandă cu listă de debitare PAL; export GLB | design | mare (după 1–9) |

Amânate (cer proprietarul): facturi/e-Factura (SmartBill/Oblio), contract cu semnătură, plăți pe etape
cu sume reale. Nu se construiesc acum: scanare LiDAR în browser, calcul de lumină de nivel DIALux, IFC/DWG
nativ, VR, funcții AI.

Riscuri: o dată de livrare veche pe linkul clientului strică încrederea (se arată „actualizat la”);
prețurile de achiziție și adaosul nu ajung niciodată la client; aprobarea cu nume scris are greutate
juridică limitată (se spune ca atare); valorile I7 rămân avertismente editabile până la citirea textului oficial.
