# Faza 1 — raport STOP GATE

| Verificare | Rezultat |
|---|---|
| `npm run typecheck` | ✅ fără erori |
| `npm test` | ✅ 24/24 |
| `npm run build` (Next.js 15, producție) | ✅ |
| Test în browser (Chromium, server de producție) | ✅ vezi mai jos |

## Teste obligatorii din promptul fazei

| Cerință | Test | Rezultat |
|---|---|---|
| Proiect nou | `repo.test` + browser | ✅ demo cu 19 piese, fără erori |
| Salvare | `repo.test` + browser (salvare automată la 0,7 s) | ✅ |
| Reîncărcare | `repo.test` + browser (redenumire cameră, reload) | ✅ |
| Revizie | `repo.test` + browser (rev. 1, modificare, rev. 2, restaurare rev. 1) | ✅ |
| Modificare cameră | `repo.test`, browser | ✅ |
| Modificare perete | `repo.test` (lățime gol), `core.test` (gol în afara peretelui, perete prea scurt) | ✅ |
| Adăugare mobilier | `core.test` (loc valid găsit automat) | ✅ |
| Coliziune | `core.test` (suprapunere → ERROR, scaunul sub birou permis) | ✅ |
| Spațiu de circulație | `core.test` (măsuța lipită de canapea → WARNING) | ✅ |
| Undo/redo | `core.test` + browser (mutare → anulează → refă) | ✅ |

## Lacune din auditul Fazei 0

| # | Problemă | Stare |
|---|---|---|
| 1 | Mobilier înalt în fața ferestrei la mutare manuală | ✅ reparat: WARNING |
| 2 | Mutarea manuală ignora circulația | ✅ reparat: WARNING (piesa proprie și vecinii) |
| 3 | ID-uri dependente de ordine | ✅ reparat: UUID salvat pe fiecare piesă |
| 4 | Variantă globală pe grupă | ✅ reparat: varianta e per piesă |
| 5 | Motorul modifica selecția utilizatorului | ✅ reparat: lucrează pe copie, varianta folosită se salvează pe piesă |
| 8 | Fără undo/redo | ✅ reparat |
| 6 | Doar camere dreptunghiulare; camere și pereți fără legătură | ⏳ rămâne (documentat în UI); planificat pentru extinderea editorului |
| 7 | Zona ușii fixă de 95 cm | ⏳ rămâne, identic cu prototipul |

## Reguli din constituție aplicate
- ERROR blochează: mutarea revine, iar revizia e refuzată (409) dacă proiectul are erori.
- WARNING cere confirmare: „Păstrez poziția” / „Revin”.
- Prețuri cu sursă, link, dată și încredere; stocul e `UNKNOWN` (nu există încă feed).
- Proiecte private implicit (proprietar anonim prin cookie httpOnly); intrările sunt validate și curățate.

## Ce NU face încă (explicit)
- Autentificare cu cont (acum: proprietar per browser; dacă ștergi cookie-urile pierzi accesul la proiecte).
- Colaborare / partajare, buget complet (Faza 2), AI (Faza 3).
- Camere neregulate (în L) și legarea automată perete–cameră.
- Mod tur pe mobil cu joystick (exista în prototip; editorul e gândit desktop-first, iar pe mobil turul se controlează prin tragere).
