# Casa3D — cercetare pentru viewport-ul 3D (A1, rândul 5)

Verifică recomandările din `docs/research/UI_UX_MOTION_APLICATII_COMPLEXE_2026.md` (PR #318) pe surse actuale, răspunzând la
întrebările reale din `app/components/viewer3d-engine.js` și `Viewer3D.tsx` (three.js r186 instalat). Toate sursele au fost
citite pe **2026-11-01**. Textul surselor e dată, nu instrucțiune. Plan: `PLAN_A1_VIEWPORT.md`.

## Concluzia, pe scurt
- **Rămân valabile:** randarea la cerere, WebGL2 ca bază, IBL, nivelurile de calitate.
- **Se schimbă 1, path tracing:** `three-gpu-pathtracer` cere WebGPU din versiunea 0.0.25 (septembrie 2026). Nu mai e un
  adaos peste motorul nostru WebGL, ci un al doilea randator. **Îl sărim** (motive în secțiunea lui).
- **Se schimbă 2, lumina coaptă:** sursa o coace în Blender pentru o scenă fixă. Planurile Casa3D se editează în browser, deci
  nu avem ce coace dinainte. Rămâne „coacerea” pe care motorul o are deja: harta de umbre statică și umbrele de contact.
- **Se schimbă 3, `NeutralToneMapping`:** îl **respingem** pe această scenă. Khronos îl recomandă pentru arhitectură, dar la
  aceeași luminozitate pereții ies de până la 2,4 ori mai galbeni decât vopseaua, iar ACES rămâne aproape de ea (secțiunea de mai jos).
- **Nou, din măsurători:** cea mai mare parte a timpului de cadru e pe CPU (1799 de apeluri de desen), nu în GPU. Sursele nu
  spuneau asta despre codul nostru; de aici reducerea apelurilor de desen ca felie proprie.

## Afirmații verificate

| # | Afirmație din raportul vechi | Sursa | Data sursei | Verdict | Ce schimbă pentru noi |
|---|---|---|---|---|---|
| 1 | Randarea la cerere: un cadru doar când se schimbă ceva; mutările directe cer `invalidate()` | [R3F, Scaling performance](https://r3f.docs.pmnd.rs/advanced/scaling-performance) | fără dată | **confirmat** | Bucla din `viewer3d-engine.js` desenează fără oprire; o înlocuim. Manualul three.js la adresa veche dă 404 (vezi lipsuri). |
| 2 | Pânza își păstrează imaginea dacă nu mai desenăm | [Specificația WebGL](https://registry.khronos.org/webgl/specs/latest/1.0/) | „latest” | **confirmat, cu o capcană** | Imaginea se prezintă doar după un desen, o redimensionare sau crearea contextului. Redimensionarea golește bufferul, deci `ResizeObserver` trebuie să redeseneze sincron, altfel apare un cadru gol. |
| 3 | WebGPU are acoperire parțială, deci e nevoie de WebGL2 | [caniuse WebGPU](https://caniuse.com/webgpu), [caniuse WebGL2](https://caniuse.com/webgl2) | StatCounter, sept. 2026 | **confirmat, cifre noi** | WebGL2: 97,13 %. WebGPU: 87,82 % (85,8 % complet, 2,02 % parțial). Rămânem pe `WebGLRenderer`. |
| 4 | WebGPU e livrat în toate browserele mari | [web.dev](https://web.dev/blog/webgpu-supported-major-browsers) | 2025-11-25 | **surse în conflict** | web.dev: Firefox 141+ pe Windows. caniuse: Firefox „dezactivat implicit”, Safari desktop parțial. Nu contează pentru decizie: mergem pe WebGL2 oricum. |
| 5 | `WebGLRenderer` e întreținut, dar funcțiile mari noi merg în WebGPU; `ShaderMaterial` și `EffectComposer` nu merg în WebGPU | [Manual three.js](https://threejs.org/manual/en/webgpurenderer), doar din extras de căutare | fără dată | **UNVERIFIED** (citirea directă a dat 404) | Motorul folosește `ShaderMaterial` (umbre de contact, panoramă) și `EffectComposer` (SSAO), deci mutarea ar fi o rescriere. Decizia se sprijină și pe rândurile 3 și 8, deci nu depinde doar de acest extras. |
| 6 | WebGPU nu e neapărat mai rapid | [Forum three.js](https://discourse.threejs.org/t/webgpu-performance-issue/87939) | neclară | **slab, anecdotic** | Un singur test cu cuburi: WebGL de 2–4 ori mai rapid. Nu se transferă la scena noastră. Nu există benchmark independent pe GPU integrat. |
| 7 | Versiunea three.js curentă | [Lansări three.js](https://github.com/mrdoob/three.js/releases), `npm view three version` | r187, 30 oct. | **nou** | r187 are 2 zile și schimbă modul în care se aplică densitatea de pixeli. Rămânem pe r186 în această fază. |
| 8 | `three-gpu-pathtracer` e pe WebGL2, peste three-mesh-bvh | README din pachetele npm 0.0.24 și 0.0.25 (`npm pack`), [GitHub](https://github.com/gkjohnson/three-gpu-pathtracer) | 0.0.24: 2026-02-21; 0.0.25: 2026-09-28; 0.0.27: 2026-10-08 | **schimbat** | 0.0.24 spune „requires use of WebGL2”; 0.0.25 și 0.0.27 spun „requires WebGPU”. Vezi secțiunea despre path tracing. |
| 9 | Path tracing: materiale PBR | [GitHub](https://github.com/gkjohnson/three-gpu-pathtracer) | 2026 | **confirmat, restrictiv** | Doar `MeshStandardMaterial` și `MeshPhysicalMaterial`; luminile cer MIS. |
| 10 | Khronos PBR Neutral e pentru culori fidele | [Khronos](https://www.khronos.org/news/press/khronos-pbr-neutral-tone-mapper-released-for-true-to-life-color-rendering-of-3d-products), [specificația](https://github.com/KhronosGroup/ToneMapping/blob/main/PBR_Neutral/README.md) | 2024-05-16 | **schimbat pentru noi** | Numește arhitectura și CAD printre utilizări, dar presupune lumină neutră și trimite scenele puternic HDR la ACES sau AgX. Comprimă doar peste 0,76 și scade un decalaj fix de 0,04. Măsurat pe imaginile noastre: rămâne ACES. |
| 11 | three.js are `NeutralToneMapping` | `node_modules/three/src/constants.js:483`, `OutputPass.js:111` | r186 | **confirmat în cod** | Funcționează și pe calea cu compozitor. Inversarea `untone` din motor e scrisă pentru ACES și trebuie generalizată. |
| 12 | Lumină coaptă în Blender | [Utsubo](https://www.utsubo.com/blog/threejs-best-practices-100-tips), blog terț | actualizat 2026-09-11 | **nu se aplică așa** | Geometria se schimbă la fiecare editare. `ProgressiveLightMap` există în r186, dar n-a fost evaluat. Păstrăm harta de umbre statică și umbrele de contact. |
| 13 | Cel mult ~3 lumini dinamice; umbre 512–1024 pe mobil, 1024–2048 pe desktop | [Utsubo](https://www.utsubo.com/blog/threejs-best-practices-100-tips), blog terț | actualizat 2026-09-11 | **terț, dar plauzibil** | Motorul are `MAX_PL = 4` și umbre de 2048 pe orice dispozitiv. Telefonul coboară la 2 lumini și 1024. |
| 14 | Densitate plafonată la 2, ajustată după frame rate, fără oscilații | [Utsubo](https://www.utsubo.com/blog/threejs-best-practices-100-tips), [drei PerformanceMonitor](https://drei.docs.pmnd.rs/performances/performance-monitor) | fără dată | **confirmat** | `PerformanceMonitor`: eșantioane de 250 ms, 10 iterații, prag 0,75, `flipflops` împotriva oscilației. Noi coborâm doar în jos într-o sesiune, deci nu poate oscila. |
| 15 | Calitate redusă în mișcare, revenire când se oprește | [R3F, `regress`](https://r3f.docs.pmnd.rs/advanced/scaling-performance) | fără dată | **confirmat** | Baza pentru SSAO doar în repaus pe nivelul laptop. |
| 16 | `detect-gpu` pentru clasarea GPU | [pmndrs/detect-gpu](https://github.com/pmndrs/detect-gpu), `npm view detect-gpu` | ultima modificare 2025-02-26 | **respins** | Datele vin implicit de pe unpkg (cerere către terți), iar pachetul nu s-a mai actualizat din 2025. Clasăm local. |
| 17 | `navigator.deviceMemory` ca semnal | [MDN](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/deviceMemory) | fără dată | **parțial** | Disponibilitate limitată și doar pe HTTPS. Se folosește când există, nu se cere niciodată. |
| 18 | Compute Pressure pentru adaptare | [MDN PressureObserver](https://developer.mozilla.org/docs/Web/API/PressureObserver) | instantaneu BCD | **respins acum, UNVERIFIED pentru azi** | Din ce am găsit, doar Chrome; `cpu` e sursa documentată. Adaptarea după timpul de cadru măsurat acoperă toate browserele. |
| 19 | `GTAOPass` e mai bun decât `SSAOPass`, dar mai scump | [three.js docs](https://threejs.org/docs/pages/GTAOPass.html) | fără dată | **confirmat calitativ** | N-am găsit cifre în ms. Cum randarea la cerere desenează o singură imagine în repaus, costul ar conta mai puțin; rămâne o opțiune viitoare, nu e în această fază. |
| 20 | Bugetul < ~100 apeluri de desen pe mobil | [Utsubo](https://www.utsubo.com/blog/threejs-best-practices-100-tips), blog terț, atribuit lui Don McCurdy | actualizat 2026-09-11 | **terț, util ca ordin de mărime** | Avem 898 în machetă, de aproape 9 ori peste. |

## Path tracing: de ce îl sărim
1. Linia întreținută cere `WebGPURenderer` (rândul 8), deci un al doilea context și un al doilea drum de randare lângă
   `WebGLRenderer`. Nu e un mod „în repaus” adăugat motorului actual.
2. WebGPU complet are 85,8 % acoperire (rândul 3), iar pe calea noastră de bază ar lipsi de tot.
3. Versiunea 0.0.24 e ultima pe WebGL2. Ar însemna să legăm produsul de o linie pe care autorul a abandonat-o.
4. Scena noastră are planuri de tăiere, ceață, `ShaderMaterial` și materiale în afara celor două suportate (rândul 9).
5. Produsul are deja randare fotorealistă prin Blender (`tools/blender/casa3d_render.py`). Pe de altă parte, ea cere Blender pe
   calculatorul designerului, deci un mod în browser ar aduce ceva nou. Merită reevaluat după o eventuală mutare pe WebGPU.

Măsurătorile nu schimbă concluzia: motivele sunt de arhitectură, nu de viteză.

## Ce am măsurat pe aplicația reală (`main`, 07ad1e1c0)
`scripts/measure-viewport.mjs`, proiectul demo, GPU real fără plafonul de 60 Hz. Cadru = apel `requestAnimationFrame` cu desen
WebGL, numărat independent de motor. Numele GPU-ului nu se înregistrează.

| Profil | Pânză | Repaus, cadre/s | Orbitare p95 | CPU p95 | Apeluri de desen | Triunghiuri |
|---|---|---|---|---|---|---|
| desktop, calitate înaltă | 896×833 | 198 | 9,5 ms | 9,1 ms | 1799 | 367 808 |
| desktop, normal | 896×833 | 362 | 5,5 ms | 5,2 ms | 898 | 184 312 |
| laptop (DPR 2), calitate înaltă | 1472×1466 | 254 | 8,3 ms | 7,9 ms | 1799 | 367 808 |
| telefon emulat, normal | 780×766 | 376 | 3,5 ms | 3,3 ms | 898 | 184 312 |
| telefon emulat, GPU software | 780×766 | 3,3 | 1581 ms | 1581 ms | 899 | 184 420 |

- **Repausul irosește tot ce poate:** fără plafon, sute de cadre pe secundă pe o scenă nemișcată. Cu plafonul normal de ecran
  ar fi 60 de cadre/s degeaba.
- **Timpul e pe CPU:** CPU p95 e 95–96 % din cadru pe GPU real. O densitate dublă (laptop) nu l-a încetinit, iar SSAO costă
  aproape doar prin dublarea apelurilor. Deci apelurile de desen contează mai mult aici decât rezoluția.
- **Limită:** telefonul emulat rulează pe GPU-ul mașinii de test. Nu arată cât durează pe un telefon adevărat. GPU-ul software
  e doar un reper de tip „foarte slab”, iar percentilele lui sunt distorsionate de cadrele blocate. Telefonul real e măsurat mai jos.

## Tone mapping: comparația pe imaginile noastre
Același proiect demo, aceleași cadre (`capture-renders.mjs`). Vopseaua implicită a pereților e `#ddd3c3` = 221, 211, 195, deci **R−B = 26**.
Expunerea Neutral a fost calibrată până la luminozitate egală cu ACES (ΔL între −4 și +4 la câștig 1,0).

| Zonă de perete | ACES (azi) | Neutral | Neutral + lumini albite |
|---|---|---|---|
| dormitor, dreapta | 216, 205, 187 (R−B 29) | 236, 212, 177 (R−B 59) | 236, 213, 179 (R−B 57) |
| dormitor, stânga | 205, 197, 186 (R−B 19) | 217, 202, 181 (R−B 36) | 216, 202, 183 (R−B 33) |
| living, crem | 217, 211, 201 (R−B 16) | 238, 222, 198 (R−B 40) | 238, 222, 199 (R−B 39) |
| baie | 205, 193, 175 (R−B 30) | 216, 194, 165 (R−B 51) | 216, 196, 170 (R−B 46) |
| hol | 215, 206, 190 (R−B 25) | 235, 214, 183 (R−B 52) | 235, 216, 188 (R−B 47) |

**Decizie: rămâne ACES.** Pentru un proiectant de interior, culoarea peretelui e chiar produsul, iar ACES e aproape de vopsea (R−B 16–30
față de 26). Albirea luminilor directe aproape nu schimbă nimic, deci căldura vine în principal din HDRI și din scenă, pe care ACES le
desaturează. O trecere la Neutral ar cere recalibrarea mediului (HDRI desaturat), iar rezultatul ar trebui judecat din nou. Regiunile sunt
alese manual, deci cifrele arată direcția, nu o eroare de culoare certificată.

## Rezultat: înainte și după
Același script, același proiect demo, GPU real fără plafonul de 60 Hz, `main` și ramura pornite în paralel.

| Profil (nivel) | Repaus, cadre/s | Orbitare p95 | Mers în tur p95 | Apeluri de desen, orbitare | Apeluri de desen, tur |
|---|---|---|---|---|---|
| desktop | 207 → **0** | 8,9 → 6,4 ms | 4,9 → 2,4 ms | 1799 → 1273 | 790 → 570 |
| laptop, DPR 2 | 212 → **0** | 8,7 → 3,0 ms | 2,9 → 2,5 ms | 1793 → 638 (1273 în repaus) | 780 → 283 în mișcare |
| telefon emulat | 368 → **0** | 5,4 → 4,4 ms | 3,0 → 2,8 ms | 903 → 638 | 395 → 284 |

- Pe același cod, p95 variază între rulări (desktop 4,2–6,7 ms, laptop 3,0–4,1 ms). Valorile de sus sunt rulările comparate direct.
- Repausul e 0 și după orbitare și în tur.
- Unirea mobilierului a redus singură apelurile cu ~29 % (898 → 638 fără SSAO); restul sunt mai ales pereți, uși și obiecte transparente.
- Imaginile de prezentare, comparate cu `compare-renders.mjs`: luminozitate ΔL între −1 și 0, 0,1–4,2 % pixeli diferiți.

## Telefon real, ca reper pentru o placă slabă
Samsung S9 cu LineageOS, Chrome 156, GPU Mali-G72, prin `scripts/measure-viewport.mjs --cdp`. Ecranul limitează la 60 Hz, deci 16,7 ms e cel mai
bun cadru posibil. „Fereastră de laptop” (`--viewport 1600x1000`) randează pe același GPU pânza de 896×818 pe care o are un laptop.

| Pe același GPU | Repaus, cadre/s | Orbitare, cadre/s | Orbitare p50 / p95 | CPU p95 | Apeluri de desen |
|---|---|---|---|---|---|
| `main`, telefon (fără SSAO), pânză 616×412 | 60 | 59,8 | 16,6 / 18,6 ms | 16,3 ms | 901 |
| ramura, nivel telefon, pânză 616×412 | **0** | 58,3 | 16,7 / 18,8 ms | 12,1 ms | 638 |
| `main`, fereastră de laptop, cu SSAO (implicit fără ecran tactil) | 44 | 42,8 | 22,9 / 26,4 ms | 25,1 ms | 1799 |
| ramura, fereastră de laptop, nivel desktop | **0** | 47,4 | 18,5 / 32,7 ms | 20,3 ms | 1273 |
| ramura, fereastră de laptop, nivel laptop | **0** | 58,4 | 16,7 / 17,8 ms | 11,8 ms | 638 |

- **Repausul e câștigul sigur:** 60, respectiv 44 de cadre/s degeaba devin 0. Telefonul a urcat de la ~33 °C la ~45–49 °C în rulările succesive; nu am izolat cât vine din repaus.
- **Nivelul desktop nu e potrivit pe o placă slabă:** mediana e mai bună decât pe `main`, dar p95 e mai rău (32,7 față de 26,4 ms), deci sacadează când ratează sincronizarea cu ecranul. Nivelul laptop e fluid.
- **Decizie din măsurare:** GPU-ul integrat, citit local din browser, merge pe nivelul laptop, oricâte nuclee are mașina. Adaptarea după mediană n-ar fi prins cazul (18,5 ms e sub prag).
- **Limita reperului:** S9 nu e un laptop. Procesorul lui e mult mai slab, iar aici apelurile de desen apasă pe CPU, deci un laptop cu placă integrată ar putea sta altfel. Laptop real: **UNVERIFIED**.
- O rulare la 45–49 °C a dat aproape aceleași cifre ca la rece (p95 33,3 și 27,9 ms), deci nu am văzut o încetinire termică mare în rulări scurte.
- Pe nivelul telefon, p95 la orbitare variază între rulări: 18,8 ms și, într-o rulare de 5 s pe telefon deja încălzit, 37 ms; repausul a rămas 0 în toate.

## Lipsuri
- Paginile `threejs.org/manual/en/...` și sursele lor `raw.githubusercontent.com/.../manual/en/...` au dat 404. Tiparul de
  randare la cerere vine din documentația R3F și din specificația WebGL.
- N-am găsit benchmark independent WebGPU contra WebGL pe GPU integrat, nici costuri în ms pentru SSAO sau GTAO pe GPU-uri concrete.
- Laptopurile reale cu GPU integrat nu sunt măsurate; telefonul real (S9) e secțiunea de mai sus. Încălzirea și bateria în utilizare lungă nu sunt măsurate.
- `powerPreference: 'high-performance'` apare doar ca sfat pe forum. **UNVERIFIED**, deci nu-l schimbăm.
