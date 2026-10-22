# Faza 2 — raport STOP GATE (cantități + buget)

| Verificare | Rezultat |
|---|---|
| `npm run typecheck` | ✅ |
| `npm test` | ✅ 39/39 (15 noi pentru Faza 2) |
| `npm run build` | ✅ |
| Test în browser | ✅ buget țintă, schimbare finisaj, export CSV, reîncărcare |

## Teste obligatorii din promptul fazei
| Cerință | Test | Rezultat |
|---|---|---|
| Calcul suprafață | living 5,6 × 3,8 m → 21,28 m², perimetru 18,8 m, pereți nete 41,35 m² | ✅ |
| Cantitate cu pierderi | parchet 21,28 m² + 10% → 12 pachete × 1,995 m² | ✅ |
| Preț unitar | fiecare linie are preț, sursă, dată, încredere | ✅ |
| Subtotal | categorii + manoperă + transport/montaj cunoscute | ✅ |
| TVA | TVA conținut = total − total / 1,21 (cota 21% din 1 aug. 2025) | ✅ |
| Total | subtotal + rezervă | ✅ |
| Depășire buget | țintă 10.000 lei → „over”, diferență negativă | ✅ |
| Modificare produs → recalculare | canapea schimbată: diferența exactă de preț; parchet schimbat: diferența exactă pe pachete | ✅ |

## Ce se calculează din geometrie
- Pardoseală (parchet sau gresie) pe camere, cu pierderi și ambalaje întregi.
- Pereți nete (fără uși 2,1 m și ferestre 1,3 m) + tavan → litri de vopsea în 2 straturi, după randamentul din fișa produsului → găleți întregi.
- Faianță: baie până la 2,1 m; bucătărie 60 cm × lungimea mobilierului de bucătărie din plan.
- Plintă: perimetru minus uși, doar în camerele uscate.
- Adeziv: (gresie + faianță) × consumul din fișa produsului → saci întregi.
- Iluminat: un corp pe cameră + unul la fiecare 12 m² (modificabil manual).
- Manoperă pe lucrări, cu interval minim–mediu–maxim.

## Proveniență
- 16 materiale (Dedeman, IKEA) din paginile de produs, verificate 01.10.2026; 15 cu încredere mare, 1 medie (preț din listă).
- 6 tarife de manoperă din ghiduri de preț publice 2026 → încredere „orientativ” (LOW), cu interval și surse afișate.
- Transport IKEA Zona 1 din termenii oficiali FY26 (încredere medie).
- Necunoscute (nu intră în total, sunt semnalate): transport Dedeman, montaj mobilier. Se completează din oferte.
- Montaj bucătărie IKEA 160 lei/ml: încredere LOW, fără link verificat — de confirmat.

## Limite
- Prețurile sunt instantanee la data verificării; nu există încă feed live (Faza 4).
- Manopera variază mult între orașe și echipe; intervalul e afișat tocmai de aceea.
- Nu se calculează încă: glet, șapă, uși interioare, instalații electrice/sanitare, chit de rosturi.
