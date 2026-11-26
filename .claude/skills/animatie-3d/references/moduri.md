# Modurile speciale - detalii

Citești secțiunea modului ales ÎNAINTE să editezi `scena.py`. Toate se aleg cu `MODE` din secțiunea SCENA; parametrii comuni (ASPECT, SECONDS, MATERIAL, COLOR, STUDIO...) sunt în SKILL.md.

## Turntable de produs (`MODE = "turntable"`)

| Parametru | Ce face |
|---|---|
| `MODEL` | cale absolută `.glb`/`.gltf`/`.obj`/`.fbx`/`.stl`. Modelul e așezat pe podea, centrat pe axa de rotație și adus la mărime standard. `None` = rotește `TEXT`/`SVG` |
| `STUDIO` | `dark`: podea neagră lucioasă cu reflexie, luminile bat doar produsul (light linking). `light`: cicloramă albă fără linie de orizont, umbră moale, look de e-commerce |
| `TURNS` | câte ture complete pe durată (1 = o tură în `SECONDS`). Orice număr întreg = buclă perfectă |
| `TT_FILL` | cât din cadru ocupă produsul; `None` = calibrat pe randare (16:9 0,65; 1:1 0,72; 9:16 0,74) |
| `BG_EMIT` | studio light: cât de alb e fundalul. 4,5 = RGB real ~250 sus / 255 pe podea (măsurat) |
| `CONTACT_SHADOW` | studio light: umbra moale de sub produs (0,93 = ~220 lângă bază, se stinge lin). Dacă mărești `BG_EMIT` mult, crești și asta (0,95-0,97) |

- **Text/SVG pe turntable** stă automat pe un **soclu rotund** fix (alb mat pe light, negru lucios pe dark). Fără
  soclu, un cuvânt lat plutește peste o umbră ovală mare.
- Încadrarea ține cont de volumul măturat de rotație, calculat din vârfuri, nu din bbox. Pe alb nu există vignetă
  și nici bloom: albul ar trece pragul și ar spăla produsul.
- **Bucla se verifică automat:** `finalize.py` vede `loop` în `events.json` și rulează singur verificarea
  (ultimul→primul cadru trebuie să fie la fel de apropiat ca două cadre vecine). Sunetul, dacă îl vrei: drona
  merge fără fade în buclă; altfel clip mut.
- **Preview pentru turntable:** 5 cadre pe o tură (`1`, `N/4`, `N/2`, `3N/4`, `N`, unde N = SECONDS×FPS), plus
  cadrul 1 la rezoluție plină. Nu există „intrare”, deci regula cu cadrele 0,4-2,2 s nu se aplică.
- **Măsurarea albului:** `signalstats` dă Y în **limited range** (alb pur = 235, NU 255) și induce în eroare. Media
  RGB reală o iei cu:
  `ffmpeg -v error -i f.png -vf "crop=W:H:X:Y,scale=1:1:flags=area" -f rawvideo -pix_fmt rgb24 - | od -An -tu1`
- `MODEL`: copiezi fișierul în `assets/` din proiect și îl referi cu cale absolută. Materialele vin din model
  (`MATERIAL` nu se aplică). Modele de test CC0: Khronos
glTF-Sample-Assets (ex. `WaterBottle.glb`, deja în `_assets/`) și Poly Haven. Licența o notezi în `brief.md`.

## Mockup de telefon/laptop (`MODE = "mockup"`)

Aplicația sau site-ul clientului pe un dispozitiv 3D procedural care intră și se așază aproape frontal.

| Parametru | Ce face |
|---|---|
| `DEVICE` | `phone` (71x147x8 mm, ecran ~19,5:9) sau `laptop` (16:10, capac deschis la 110°) |
| `SCREEN` | cale ABSOLUTĂ către captura clientului: `.png`/`.jpg` sau video `.mp4`/`.mov`/`.webm` (redat pe ecran). Pune fișierul în `assets/` |
| `SCREEN_FIT` | `cover` umple ecranul și taie surplusul; `contain` arată tot, cu benzi negre |
| `SCREEN_GLARE` | reflexia de sticlă peste ecran (0,06; 0 = fără) |
| `SCREEN_SHINE` | dâra de lumină care traversează ecranul la așezare (0,10; 0 = fără) |

- Folosește și `STUDIO`, `COLOR`, `BACKGROUND`, `BG_EMIT`, `CARD_W`, `SWEEP_W`, `OUTRO = "fade"` și `HDRI_ID`.
  Durata potrivită: `SECONDS` ≈ 4,5.
- **Culorile ecranului rămân exacte:** ecranul ocolește AgX (randare în Standard + Convert to Display în
  compositor, cu mască AOV doar pe ecran). Nu „repara” culorile ecranului din `COLOR`/grading.
- Scriptul tipărește `ANIM3D_SCREEN` cu colțurile ecranului în pixeli (pentru un text/overlay pus după).
- Captura: telefon ~1170x2532, laptop ~1920x1200. Verifici în preview că textul de pe ecran se citește la
  rezoluție plină (cadrul de la ~3 s).
- Limite cunoscute: telefonul în 9:16 declanșează `ANIM3D_SAFEZONE` jos (e înalt, acceptabil pentru un mockup);
  laptopul în 9:16 iese o bandă subțire, deci pentru laptop alegi 16:9 sau 1:1. Detaliile dispozitivului sunt
  procedurale (fără logo de producător, intenționat).

## Vedere explodată (`MODE = "exploded"`)

Produsul stă asamblat, piesele se depărtează decalat, țin poza desfăcută, apoi se reasamblează. Rotație lentă
±18°, camera se retrage odată cu piesele și revine (produsul e mare în ambele stări).

| Parametru | Ce face |
|---|---|
| `MODEL` | obligatoriu: model cu **piese separate** (mesh-uri distincte în `.glb`). Un model dintr-o bucată e desfăcut pe bucăți libere, iar fragmentele mici (șuruburi) se lipesc de cea mai apropiată piesă mare; dacă nu rămân ≥ 2 piese mari, scriptul se oprește cu mesaj clar |
| `EXPLODE_AXIS` | `radial`: cea mai mare piesă rămâne pe loc, restul pleacă dinspre ea (piesele stivuite pe aceeași direcție primesc pași diferiți). `z`: teardown, piesa de jos rămâne pe podea, restul urcă stivuite |
| `EXPLODE` | distanța (1 normal, 1,5 larg, 0,6 discret) |
| `EXPLODE_BACK` | `True` se reasamblează (~78% din durată); `False` rămâne desfăcut |

- Folosește și `STUDIO` (dark/light), `BG_EMIT`, `CONTACT_SHADOW`, `TT_FILL`, `CARD_W`, `HDRI_ID`. Durata:
  `SECONDS` = 6 (sub 5 s mișcarea e grăbită și apare `ANIM3D_WARN`).
- Sunetul merge din `events.json`: whoosh la desfacere, impact când ajung piesele, shimmer la reasamblare.
- Preview: cadrele 1, ~0,25N, ~0,4N (desfăcut complet), ~0,65N, ~0,85N, N. Verifici că nicio piesă nu trece
  prin alta pe drum (la radial, piesele interioare pot traversa carcasa: atunci `EXPLODE_AXIS = "z"`).
- Materialele vin din model. Modelele cu transmisie/sheen (ex. ToyCar) pot ieși prea luminoase sub bloom: scazi
  `CARD_W` sau alegi `STUDIO = "light"`.

## Cifre care numără / infografic (`MODE = "stats"`)

1-4 cifre-cheie 3D care numără de la 0 la valoare, fiecare cu bară de accent și etichetă. Pe 16:9 și 1:1 stau pe
un rând, pe 9:16 una sub alta, în zona sigură. Scena e cea premium a reveal-ului: podea neagră lucioasă,
contur cald/rece pe cifre, sweep și puls de bloom când se oprește ultima cifră.

| Parametru | Ce face |
|---|---|
| `STATS` | listă de `(valoare, etichetă)`, ex. `[("1.250+", "clienți mulțumiți"), ("98%", "rată de succes"), ("4,8", "nota medie")]`. Stil românesc: punct = mii, virgulă = zecimale; prefixul/sufixul (`+`, `%`, `€`, ` ani`) rămân fixe. Fără cifre = text static |
| `FONT_LABEL` | fontul etichetelor (implicit `segoe`); cifrele folosesc `FONT` |
| `MATERIAL`, `COLOR` | materialul cifrelor (aur implicit); `COLOR` colorează barele de accent |

- Durata: `SECONDS` = 5 (prima cifră intră la ~0,1 s, ultima se oprește la ~2,4 s cu 3 cifre (+0,35 s pe cifră), apoi ținere de citit). Etichetele lungi trec singure
  pe 2 rânduri.
- Corpul textului se schimbă pe cadru printr-un handler, așa că randarea merge **cadru cu cadru** (automat).
- Preview: în locul cadrului 1 (aproape gol) iei ~0,5 s; verifici ~1 s (cifrele la jumătatea numărătorii), iar la proba la rezoluție plină adaugi un cadru din numărătoare (~1,2 s) lângă ultimul cadru.
  Valorile finale trebuie să fie exact cele din `STATS`, iar fețele tuturor cifrelor aurii uniform (nu negre cu
  contur, nici crem arse); altfel ajustezi `CARD_W`.

## Din poză, model 3D (local, TripoSR)

Când proprietarul are doar o **poză** a produsului și nu are un model 3D. Totul rulează pe acest PC (GPU), iar
poza nu pleacă nicăieri. Mediul e instalat în `E:/claude-work/animatie-3d/_tools/TripoSR` (Python 3.11 +
PyTorch CUDA 12.8, model MIT ~1,7 GB, decupare `u2net` Apache 2.0). Rulezi din **PowerShell** (cu acordul
proprietarului):
```
& "E:\claude-work\animatie-3d\_tools\TripoSR\.venv\Scripts\python.exe" "<S>\img2model.py" "$P\assets\poza.jpg" "$P\assets\produs.glb" [--tilt 8] [--mc-res 320]
```
- Ieșirea e `.glb` cu culori pe vârfuri, așezat pe podea, cu fața pozei spre cameră, înălțime 2. Merge direct în
  `MODE = "turntable"` cu `MODEL = ...produs.glb`. Scrie și `produs_input.png`, cu decuparea pe care a văzut-o
  modelul; o citești.
- `--tilt` = câte grade de SUS a fost făcută poza (negativ = de jos). Fără el, un produs pozat de sus iese înclinat
  față-spate. Verifici cadrul de profil din turntable (¼ de tură): trebuie să stea drept.
- Post-procesare (implicită, verificată prin randare 2026-11-22): netezire Taubin `--smooth 50` (volumul se schimbă
  < 0,2%), normale netede și material PBR în `.glb` (`--roughness 0.35`; 0,5 pentru textil/mat), bucățile mici
  plutitoare aruncate. `--despec 1` șterge petele albe de reflexie din poză, dar poate face găuri și șterge
  detaliile albe: îl folosești doar la obiecte lucioase fără alb. Golurile și spatele ghicit de TripoSR rămân.
- Durata: ~5-10 s pe GPU, după încărcare (prima rulare a descărcat modelul). Placa e împărțită cu alte programe:
  la „out of memory” scriptul reîncearcă singur cu bucăți mai mici, apoi pe CPU (`oom_retries` în raport).
- **Calitatea, spusă cinstit proprietarului:** forma și culorile ies corecte, dar suprafața e mată și ușor
  noduroasă. Reflexiile din poză devin pete/adâncituri, spatele e ghicit, iar textul sau logo-urile fine nu se
  citesc. E bună pentru schițe, social media rapid și obiecte mate compacte. Pentru un produs lucios de catalog
  ceri un model 3D real.
- Poza ideală: produsul întreg, frontal, la nivelul lui, fundal simplu, lumină moale, fără reflexii puternice.
- Dacă Windows are puțină memorie (sub ~6 GB liberi), procesul se poate opri cu „access violation”. Raportezi
  memoria liberă și ce procese mari rulează; nu le oprești fără acordul proprietarului.


## Tipografie cinetică (`MODE = "kinetic"`)

2-5 fraze 3D una după alta, de tipul cârlig -> beneficiu -> îndemn: „Ai nevoie de un site?” / „Îl facem în 7
zile.” / „Scrie-ne azi!”. Fiecare frază intră pe litere, stă cât să fie citită (~18 caractere/s, min. 0,7 s),
pleacă în sus, iar următoarea intră cât pleacă ultimele litere, fără gol negru. Ultima rămâne pe ecran, cu sweep și
puls de bloom. Camera urmărește lin înălțimea fiecărei frazei; frazele lungi se rup singure pe 2 rânduri (peste 18
caractere pe orizontal, peste 12 pe vertical).

| Parametru | Ce face |
|---|---|
| `PHRASES` | lista de fraze. Scurte: 2-6 cuvinte pe frază; ultima e îndemnul (CTA) |
| `KINETIC_STYLES` | stilul de intrare al fiecărei fraze, pe rând: `rise`, `pop`, `drop`, `flip` (implicit `rise`, `pop`, `drop`) |
| `MATERIAL`, `FONT`, `BACKGROUND`, `OUTRO` | ca la reveal; `OUTRO` se aplică ultimei fraze |

- Durata: ~2,5 s pe frază + 1 s de ținere la final (3 fraze ≈ 8 s). Dacă `SECONDS` e prea mic, `ANIM3D_WARN`
  spune exact cât să pui.
- Preview: `--frames auto` dă cadre doar pe ultima frază. Pentru tipografia cinetică alegi manual câte 2 cadre pe frază
  (la intrare și la mijlocul ținerii), ca să vezi fiecare frază întreagă și că literele frazei anterioare au plecat
  TOATE.
- Sunetul: tick la prima frază, whoosh și impact pe ultima.

## Particule care formează textul/logo-ul (`MODE = "particles"`)

Mii de particule luminoase pornesc dintr-un nor (sau vârtej), zboară decalat (parțial de la stânga la dreapta, ca
și cum textul „s-ar scrie”) și se așază pe suprafața textului/logo-ului SVG. În ~0,4 s textul solid apare și
particulele se sting. Restul scenei e rețeta reveal: podea, lumini, sweep, bloom, `SUBTITLE`, `OUTRO`.

| Parametru | Ce face |
|---|---|
| `PARTICLES` | numărul de particule (6000; la memorie puțină 3000) |
| `PARTICLE_STYLE` | `nor` (nor împrăștiat care se adună) sau `vartej` (spirală în jurul axei verticale) |
| `PARTICLE_KEEP` | ce parte rămâne ca sclipiri pe text (0-0,05; peste ~0,1 textul pare pătat) |

- Determinist, fără simulare și fără bake: pozițiile vin din Geometry Nodes calculate doar din numărul cadrului,
  deci orice cadru randat separat iese la fel.
- Culoarea particulelor urmează `MATERIAL` (aur: portocaliu cald emisiv).
- Durata: 4 s (textul apare complet la ~2,5 s).
- Limite: la 1080p, chiar înainte de apariția textului, literele din particule sunt „pufoase”. Sweep-ul de la impact
  poate da o pată albă pe fețele plane ale unui logo SVG. Netestate: `ALPHA`, Cycles, `sticla` (crossfade-ul prin
  alfa e nesigur pe sticlă).

## Reclamă de produs (`MODE = "commercial"`)

Spot CGI de produs în 5 cadre, cu tăieturi între camere, fără text ca subiect. Rețeta vine din cercetarea din
2026-11-22: Fable și Codex au ajuns independent la aceeași structură; surse: morphic.com/resources/how-to/commercial-previs,
contra.com (spoturi CGI 2026), tutorialul „Full Product Animation Masterclass” (The Blenderender).

| Cadru | Din durată | Lentilă | Ce se întâmplă |
|---|---|---|---|
| 1 macro | 0-16% | 100 mm f/2.8 | detaliu (capac/umăr), glisare laterală liniară; fără ingrediente |
| 2 reveal | 16-31% | 50 mm | arc de ~30° + apropiere (EXPO ease-out); produsul levitează, ingredientele orbitează |
| 3 orbită | 31-44% | 85 mm f/2.8 | aproape static, rack focus: ingredient din față -> produs |
| 4 energie | 44-54% | 35 mm, de jos | crane up; ingredientele „explodează” spre exterior |
| 5 packshot | 54-100% | 70 mm f/8 | produsul aterizează (impact + bloom), `TEXT` apare lângă el (16:9: dreapta; 9:16: deasupra), push-in |

| Parametru | Ce face |
|---|---|
| `MODEL` | produsul tău (.glb etc.); `None` = produs procedural |
| `PRODUCT` | produsul procedural: `bottle` (sticlă cu lichid, capac, etichetă), `jar` (borcan cosmetic), `can` (doză), `box` (cutie) |
| `MATERIAL` | `sticla` = sticlă transparentă cu lichid colorat (`COLOR`); `plastic`; `aur`/`crom` = metal. Implicitul `aur` dă o sticlă METALICĂ: pentru sticlă clară pui `sticla` |
| `COLOR` | culoarea de brand: lichid, etichetă, doză, cutie, paleta ingredientelor |
| `INGREDIENTS` | `True`: 14 obiecte stilizate (sfere, cuburi); **listă de modele reale Poly Haven CC0**, ex. `["lemon", "food_lime_01"]` (descărcate o dată în `_assets/models/`, licența în `LICENTE.txt`); `False` = fără. Disponibile: `lemon`, `food_lime_01`, `food_kiwi_01`, `food_apple_01`, `food_avocado_01`, `food_pomegranate_01`, `food_ginger_01`, `food_lychee_01`, `food_pears_asian_01`, `bananas`, `croissant` |
| `TEXT` | numele brandului sau CTA-ul din packshot; la produsele procedurale apare și **curbat pe etichetă** (diacritice ok); `""` = fără text |
| `LABEL_IMAGE`, `LABEL_FIT` | designul etichetei sau logo-ul clientului (PNG cu transparență / JPG, cale absolută) pus pe eticheta produsului procedural, în locul numelui scris. `contain` (implicit) = logo-ul întreg pe fața etichetei, fundal în `COLOR`; `cover` = eticheta completă (design pe toată eticheta). Nu se aplică la `MODEL` |
| `SPLASH` | `True`: la aterizarea din packshot, splash de lichid în culoarea lichidului (coroană cu „degete” + ~30-60 de picături balistice, ~0,8 s; determinist, fără simulare; worker Fable 2026-11-22). Pentru băuturi. Coroana rămâne rotundă și la `box` |
| `CONDENSATION` | picături de condens pe produs (0 = fără, 1 = normal). Macro-ul se mută pe corpul produsului; câteva picături mari alunecă încet |
| `STUDIO`, `BG_COLOR` | `dark` = podea neagră lucioasă + fundal gradient (`BG_COLOR`); `light` = cicloramă albă (testat: logo-ul din packshot iese automat în culoarea de brand închisă, conturul e atenuat, lichidul e aproape opac) |

- Durata: `SECONDS` = 10 (8-12 s). Cadrele se scalează cu durata.
- Ritmul e „build -> snap -> breathe”, pe care cele trei cercetări (Fable, Codex, Gemini) l-au găsit independent:
  cadre tot mai scurte până la vârf, apoi un packshot lung. Fundalul își schimbă culoarea/intensitatea pe fiecare
  tăietură (rece -> spre culoarea brandului -> cald), lichidul din sticlă se clatină la aterizare, iar `sound.py` pune
  un whoosh scurt cu ~0,25 s ÎNAINTEA fiecărei tăieturi (J-cut, din `events.json` -> `cuts`).
- Format: 16:9 pune logo-ul din packshot lângă produs; 9:16 și 1:1 îl pun DEASUPRA (verificat pe toate trei).
- Cycles (`--engine cycles`) umple sticla cu un lichid mai credibil, dar culoarea iese spre roz-somon, iar
  contururile sticlei sunt mai moi decât în EEVEE. Timp: ~8 s/cadru (10 s de clip ≈ 40 min, față de ~10 min în EEVEE).
  Îl alegi doar dacă lichidul e vedeta; implicit rămâne EEVEE.
- Lumini de reclamă: key moale, 2 benzi verticale în spate (contur pe sticlă), rim rece + rim cald, softbox pe
  fiecare cameră (slab pe macro). Light linking: podeaua nu primește pete.
- Verificare: `framing_report` sare peste cadrul macro, unde produsul e tăiat intenționat, și citește camera activă
  pe fiecare cadru (markere). Preview: câte un cadru din mijlocul fiecărui plan + 2 în packshot.
- Pe `STUDIO = "light"` NU pui `CONDENSATION`: pe eticheta deschisă picăturile fac pete albe și textul etichetei
  pare murdar (test agent 2026-11-22).
- Fructe: `food_kiwi_01` e un kiwi ÎNTREG (maro, seamănă cu un cartof). Pentru „verde/proaspăt” alegi `food_lime_01`,
  `food_apple_01` sau `food_avocado_01`.
- Schimbarea parametrilor în `scena.py`: cu Edit, linie cu linie (`MODE = "commercial"`). Un regex care înlocuiește
  până la capătul liniei mănâncă și comentariul.
- Condensul (Geometry Nodes, determinist, worker Fable): în reclamă e SUBTIL. Picăturile prind puține reflexii de la
  luminile din spate, iar pe eticheta albă aproape nu se văd. Iese clar pe sticlă cu lichid opac, iluminată lateral
  (testul din `_tools/fable_condens/out/macro_*.png`). Pe sticla transparentă picăturile devin mărgele lucioase
  semitransparente, pentru că refracția EEVEE prin ele ar arăta fundalul ca pete închise.
- Limite: ingredientele reale vin doar din lista Poly Haven de mai sus (fructe, ghimbir, croissant). Frunze,
  boabe de cafea sau flori nu există încă acolo ca modele potrivite. Lichidul se vede doar pal prin sticlă (EEVEE refractă o singură
  suprafață).
