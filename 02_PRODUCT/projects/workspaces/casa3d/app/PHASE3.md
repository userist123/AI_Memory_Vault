# Faza 3 — raport STOP GATE (Design Brief + variante AI)

| Verificare | Rezultat |
|---|---|
| `npm run typecheck` | ✅ |
| `npm test` | ✅ 52/52 (13 noi pentru Faza 3) |
| `npm run build` | ✅ |
| Test în browser | ✅ brief → 3 variante → previzualizare → respingere → aplicare → revizie |

## Teste obligatorii din promptul fazei
| Cerință | Test | Rezultat |
|---|---|---|
| Brief | validare stil, magazine, text curățat, limite | ✅ |
| Generare propunere | 3 variante fără erori, doar produse din catalog, cost crescător | ✅ |
| Produs invalid | ID inventat → INVALID_PRODUCT (ERROR), aplicare blocată; la fel pentru material | ✅ |
| Mobilier prea mare | masă 130 cm aleasă explicit → DOES_NOT_FIT (ERROR) | ✅ |
| Depășire buget | buget 10.000 lei → OVER_BUDGET (WARNING), aplicarea cere confirmare | ✅ |
| Respingere | decizie salvată, proiectul neschimbat | ✅ |
| Aprobare | re-validare pe server, aplicare, revizie automată cu notă | ✅ |
| Aplicare în Digital Twin | piesele din plan au exact variantele propuse | ✅ |
| AI (răspuns simulat) | produs inventat de AI prins de validare; răspuns ne-JSON → motorul de reguli, cu notă | ✅ |

## Fluxul (conform constituției)
1. Brief structurat (stil, persoane, copii, animale, accesibilitate, priorități, buget, magazine, culori, de evitat).
2. Propunere: AI (Claude, dacă `ANTHROPIC_API_KEY` e setat) sau motorul de reguli. AI-ul primește doar lista de ID-uri din catalog și nu dă coordonate.
3. Validare: schemă → produse/materiale (existență, grupă, magazin permis) → amplasare de către motorul geometric → încadrare → coliziuni, uși, ferestre, circulație → buget.
4. Utilizatorul vede costul, diferența față de acum, „ce se schimbă” (preț și dimensiuni), motivele și problemele, și poate previzualiza în plan și 3D fără să salveze.
5. Aplicare: ERROR blochează; WARNING cere bifarea confirmării; serverul re-validează pe proiectul curent, aplică și creează revizie.

## Transparență
- Fiecare propunere e marcată „propusă de AI (model)” sau „motorul de reguli (fără AI)”; sursa și modelul se salvează în baza de date (tabelul `proposals`), împreună cu brief-ul, răspunsul brut și deciziile.
- Motivele motorului de reguli sunt derivate din atributele din catalog (preț, culoare, material, dimensiuni) și din brief — nu sunt formulări generice.

## Limite
- Calea cu AI real a fost testată cu răspunsuri simulate; în mediul de test nu există cheie API. Pe server, setează `ANTHROPIC_API_KEY` (și opțional `ANTHROPIC_MODEL`).
- Variantele schimbă produse și finisaje; nu modifică planul (pereți, camere).
- Motorul de reguli folosește euristici simple de stil (culoare, lemn, material); un AI configurat poate face combinații mai fine, dar trece prin aceleași verificări.
