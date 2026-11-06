# Casa3D — studiul de însorire

Cerința proprietarului (2026-11-06): „continuă cu Casa3D, inovare, fii ambițios”. Faza 4 din roadmap-ul A1 (planșa ISO 7200)
e blocată până la textul cumpărat al standardului, deci am ales o funcție nouă, independentă de ea.
Ramura `claude/casa3d-insorire`, worktree `D:/w/sun`, bază `origin/main` `0d97d13c3`.

## Ce face
- **Soarele real** pentru localitatea și orientarea casei: algoritmul NOAA (Meeus) în `app/core/solar.ts`, cu refracție,
  răsărit/apus/amiază solară și ora locală prin `Intl` (ora de vară inclusă). Verificat pe București: răsărit/apus 21 dec
  07:49/16:39, 21 iun 05:31/21:04, înălțimea la amiază 90 − φ ± δ.
- **Amplasamentul** (`app/core/site-location.ts`, cheia nouă `Snapshot.site`): 31 de orașe (centre aproximative ±0,05°) sau
  coordonate proprii, fus orar, nordul față de plan (0–359°). Coordonatele se rotunjesc la 0,01° (~1 km), pe server
  (`lib/repo.ts checkSnapshot`), ca linkul clientului să nu dea adresa exactă.
- **Analiza prin ferestre** (`app/core/insolation.ts`): raze din centrul fiecărei ferestre și din celulele pardoselii spre
  soare. Pereții sunt prisme cu grosimea lor: lumina trece doar dacă tot drumul prin perete rămâne în golul ferestrei, deci
  glaful și buiandrugul umbresc; aripile casei se umbresc între ele; tavanul oprește razele care urcă deasupra unei camere.
  Ușile sunt închise (opace), pasajele și pereții de sticlă lasă lumina, pereții de demolat lipsesc.
- **Norma**: Ordinul MS nr. 119/2014, art. 3 (modificat prin Ordinul nr. 994/2018) — minimum 1½ ore de însorire la solstițiul
  de iarnă pentru încăperile de locuit (camere de zi și dormitoare, tipurile `living` și `dormitor`). Verdict pe cameră și
  avertisment `SUN_LOW` în „Verificări” (advisor), calculat pe 21 decembrie.
- **Interfața** (etapa Concept, `app/components/SunStudy.tsx`): oraș, nord, zi (21 dec / 20 mar / 21 iun / orice dată), oră cu
  redare animată; pe plan harta orelor de soare (relativă la maximul zilei, cu legendă), petele de soare din momentul ales
  (proiecția ferestrei prin ambele fețe ale peretelui) și săgeata nordului; în 3D lumina soarelui real (`solarLighting`).

## Decizii
- Fără geocodare: adresa clientului nu pleacă la un serviciu terț; orașul sau coordonatele le alege designerul.
- Punctul normei = centrul ferestrei în planul geamului (mijlocul grosimii peretelui); durata = totalul zilei (raportăm și
  cel mai lung interval). Pas 5 minute pentru ferestre, 15 minute și celule de 30 cm pentru hartă (20–40 ms pe cel mai
  mare șablon).
- Ziua normei e fixă (`NORM_REFERENCE_DATE` = 2026-12-21): declinația soarelui la solstițiu variază sub 0,01° între ani.
- Terasa, balconul deschis și curtea (`spaceKind`) sunt „afară” și fără acoperiș; balconul acoperit e în afara anvelopei,
  dar tavanul lui umbrește. Un fus orar necunoscut e refuzat la salvare (fără întoarcere tăcută la UTC).
- Recalculul se face doar când se schimbă geometria care umbrește (pereți, goluri, camere, tavan) sau amplasamentul;
  mutarea mobilierului nu reface studiul (nici în panou, nici în Verificări).

## Limite (scrise și în interfață)
Vecinii, streașina, balcoanele de deasupra, copacii, relieful și mobila nu sunt modelați; nivelurile de deasupra nu umbresc
nivelul curent. Petele de soare (momentul ales) se taie la dreptunghiul camerelor cu contur poligonal și nu țin cont de umbra altor
aripi; harta orelor (raze) e cea corectă. Calculul e orientativ și nu înlocuiește studiul de însorire semnat, cerut la autorizare.

## Verificare
`npx tsc --noEmit -p .` curat; `npx vitest run --maxWorkers=2` 143 fișiere / 990 teste trec (20 noi în
`tests/insolation.test.ts`); `next build` reușit; în aplicația reală (dev, port 3220): studiul pornit, persistat după reîncărcare, RO și EN,
harta iarnă/vară, rotirea nordului la 180° mută dormitorul pe nord (0 h, „sub minim”) și apare avertismentul în Verificări,
umbrele 3D se mută între 06:40 și 20:00.

## Revizie
Runda 1 (vault-reviewer, Opus): 4 defecte reproduse — terasa din fața ferestrei o transforma în fereastră interioară;
răsărit/apus greșite în zilele cu schimbarea orei și în fusurile îndepărtate; fus invalid înlocuit tăcut; etichete „07:60”;
plus prelungirea colțului care umbrea fals lângă o îmbinare dreaptă și recalculul la fiecare editare. Toate reparate, cu
teste de regresie. Rezidual acceptat: petele de soare aproximative pentru camerele poligonale (vezi Limite).

## Surse
- Ordinul MS nr. 119/2014, art. 3 (textul publicat, ex.: https://www.sannicolau-mare.ro/wp-content/uploads/2017/03/ORDIN-NR.119-DIN-04.02.2014-al-MINISTERULUI-SĂNĂTĂȚII-pentru-aprobarea-Normelor-de-igienă-şi-sănătate-publică-privind-mediul-de-viaţă-al-populaţiei.pdf);
  modificarea prin Ordinul nr. 994/2018 citată în studiile de însorire publicate de primării. Textul consolidat în vigoare: NEVERIFICAT.
- NOAA Solar Calculator (algoritmul, după J. Meeus, „Astronomical Algorithms”).
