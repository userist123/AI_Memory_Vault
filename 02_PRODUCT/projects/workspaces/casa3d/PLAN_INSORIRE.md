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

## Etapa 2 — lumina ca material de proiectare (cerința „mai inovator”, 2026-11-07)
- **Remedii generative** (`app/core/sun-design.ts`): pentru o cameră de locuit sub normă, aplicația generează variante —
  fereastră lărgită (până la 3 m, centrată sau lipită de capătul liber), parapet coborât la 40 cm, ambele, fereastră nouă de
  1,2 sau 2 m pe fiecare porțiune exterioară a camerei, la fiecare 30 cm — le simulează pe 21 decembrie (3–22 ms pe căutare,
  cu pașii soarelui în cache) și păstrează frontul Pareto (mai mult soare pentru mai puțin geam; fereastra nouă costă în plus
  1,5 m² echivalent pentru buiandrug). Dacă una respectă norma, se arată doar cele care o respectă. Golurile respectă 30 cm
  de capătul peretelui și între goluri. Previzualizarea folosește mecanismul existent al editorului (plan, hartă și 3D cu
  fereastra nouă), aplicarea e un singur pas de anulare.
- **Idee de reorganizare** (`swapIdeas`): când nicio fereastră nu ajunge (de exemplu o cameră cu un singur perete spre nord),
  aplicația caută o bucătărie sau un birou al nivelului, de mărime potrivită, care respectă norma, și propune mutarea
  funcțiunii (fără aplicare automată: mobilierul se reface).
- **Soarele pe mobilier** (`app/core/sun-effects.ts sunOnPieces`): soare direct pe comoda TV (reflexii pe ecran), pe birou
  (orbire) și pe pat (primul soare dimineața), cu intervalele zilei alese.
- **Căldura solară** (`solarGain`): radiația directă pe cer senin (Meinel, masa de aer Kasten–Young) × cosinusul incidenței ×
  partea însorită a geamului (9 puncte) × g = 0,6; kWh pe zi și vârful în W pe cameră; „risc de supraîncălzire” vara peste
  0,2 kWh/m² pe zi — prag orientativ ales de noi, nu dintr-o normă. Difuzul și norii nu sunt incluși (zi senină = limită de sus).
- Verificat în aplicația reală pe șablonul demo cu nordul la 315°: Dormitor 0 h 20 → fereastră nouă spre sud-est, 6 h 45,
  aplicată, salvată pe server și anulată cu „Anulează”; Living (ferestre NV/NE) primește răspunsul onest că norma nu se poate
  atinge; pe 21 iunie patul are soare de la 06:10 și televizorul 19:10–21:00.
- Revizie etapa 2 (2 runde, vault-reviewer): runda 1 FAIL — „Aplică” punea în proiect o copie veche a planului și ștergea
  scările, stâlpii, cotele și nișele adăugate între timp; reparat: aplicarea înlocuiește doar peretele schimbat și refuză o
  propunere depășită (perete mutat sau goluri, nișe, obiecte de perete schimbate). Tot atunci: ferestrele ocolesc nișele și
  obiectele de pe perete, golurile rămân sub 3 m, dominanța Pareto standard, vârful de putere al camerei la același moment.
  Runda 2 PARTIAL — nișele adăugate după căutare; reparat (cheia de recalcul și verificarea la aplicare le includ), cu teste.
  Suita: 144 fișiere / 1002 teste; next build reușit.

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
