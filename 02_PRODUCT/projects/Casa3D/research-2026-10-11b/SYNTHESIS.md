---
id: "f09fc736-92ae-472e-9f6b-395293bfbed8"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, synthesis, roadmap, design, lifecycle, rendering]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Synthesis of research_design_depth.md, research_lifecycle.md, research_product_models.md in this folder (three web research passes, 2026-10-11); URLs and unverified items are listed in each file"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "b7c41f0e-5a2d-4d8e-9f61-2c3a8e7d4b19"
---
# Casa3D: a treia cercetare — design mai adânc, ciclul proiectului, piese fidele (2026-10-11)

Cerința proprietarului: „fă-o mai complexă partea de design, dar ai grijă și de ștergerea proiectului și alte
detalii care nu țin de design”. Trei cercetări, fiecare cu sursele ei în acest dosar.

## Ce reiese

1. **Ciclul proiectului e cea mai mare gaură în afara designului.** Casa3D are ștergere doar în API (fără buton),
   definitivă, iar randările rămân pe disc. Norma (Canva, Notion, Drive): coș 30 de zile cu restaurare; Houzz Pro
   șterge definitiv fără anulare — Casa3D poate fi mai bun. Linkurile clientului trebuie oprite imediat la ștergere.
   Duplicarea nu copiază datele clientului. Exportul complet (GDPR art. 20) = arhivă cu JSON/CSV + fișierele, nu doar PDF.
2. **Designul: diferențiatorul e verificarea cu cifre**, pe care niciun concurent nu o vinde întreagă: covor față de
   canapea/pat, înălțimea tabloului (centru ~145 cm), perdea (10–15 cm peste ramă, ½–⅔ spre tavan), lustra peste masă
   (76–91 cm), canapea–măsuță 35–45 cm, TV ≈ 0,65–0,84 × distanța, culoar 90 cm, lux pe cameră. Ca avertismente cu
   interval, nu erori. Apoi: panou de stil (mood board) legat de plan și deviz, variante A/B pe cameră cu diferența de
   cost, iluminat pe straturi, tavane (scafă LED, casetat), lambriu/riflaj.
3. **Piesa „exact ca la comandă” = generator pe linie de produs + atribute vizuale pe produs** (brațe, picioare,
   uși/sertare, mânere, soclu/suspendat, forma cuvei, clapeta). Modelele IKEA de pe site nu au licență de folosire;
   BIM-urile producătorilor (Roca, Geberit) doar pentru randare, nu servite în browser. Fotografii de interior:
   cameră la 1,0–1,5 m, verticale drepte, 16–24 mm, baia în o-perspectivă, 2–3 vederi pentru încăperi mici.

## Ordinea de implementare

| # | Ce | Sursă | Mărime |
|---|---|---|---|
| 1 | **Coș 30 de zile**: ștergere = mutare în coș (linkurile clientului oprite), restaurare, ștergere definitivă cu numele tastat (și randările de pe disc), curățare automată după 30 de zile; anulare imediată; duplicare fără datele clientului; căutare și sortare în lista de proiecte | lifecycle 1,4,5 | medie |
| 2 | **Export complet al proiectului** (arhivă: proiect JSON, revizii, comenzi CSV, randări, poze șantier) | lifecycle 6 | medie |
| 3 | **Profilul firmei** (nume, logo, CUI, IBAN, contact) pe planșe, ofertă, dosar de predare | lifecycle 14 | mică |
| 4 | **Salvare sigură pe două dispozitive**: versiune la salvare, „reîncarcă sau suprascrie”, nu câștigă tăcut ultimul | lifecycle 12 | mică |
| 5 | **Reguli de design cu cifre** în consilier + „aplică” (covor, tablou, perdea, lustră, TV, distanțe, lux) | design 1,10 + tabel | medie |
| 6 | **Panou de stil** legat de plan și deviz, schemă de culori 60-30-10 pe cameră | design 2,3,5 | medie |
| 7 | **Variante A/B pe cameră** cu diferența de cost și alegerea clientului pe link | design 4 | mare |
| 8 | **Iluminat pe straturi + lux estimat pe cameră** | design 6 | medie |
| 9 | **Tavane și pereți**: scafă LED, tavan casetat, riflaj, lambriu, baghete (plan, 3D, Blender, deviz) | design 7,9 | mare |
| 10 | **Piese fidele**: generatoare pe linie de produs cu atribute vizuale; cameră la 1,3 m, baia o-perspectivă | product models | mare |

Riscuri: o ștergere care lasă linkul clientului activ sau fișiere pe disc; copii de proiect cu datele clientului;
valori de design prezentate ca norme (sunt practică, nu lege); licențe de modele 3D neverificate (nu se folosesc
modele IKEA descărcate).
