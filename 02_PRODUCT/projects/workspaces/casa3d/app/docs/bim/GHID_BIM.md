# Casa3D -- Blueprint pentru workflow BIM automatizat din clipul „Plot"

**Scop:** transformarea unui plan 2D într-un model 3D coordonat,
extensibil la aplicații CAD/BIM și instrumente de vizualizare similare.

**Baza documentului:** analiza vizuală a clipului furnizat (56,6
secunde). Clipul arată etapele și rezultatul vizual, dar nu documentează
complet software-ul, API-urile, prompturile sau automatizările folosite.
Elementele marcate „PROPUNERE" sunt recomandări tehnice pentru Casa3D,
nu afirmații despre conținutul clipului.

------------------------------------------------------------------------

## 1. Esențialul extras din clip

Workflow-ul demonstrat:

1.  **Architecture**
    -   Se pornește de la un plan 2D.
    -   Planul este folosit pentru construirea geometriei arhitecturale.
    -   Pereții sunt ridicați în 3D, păstrând scara și dimensiunile.
2.  **Structure**
    -   Se adaugă fundații și stâlpi.
    -   Elementele structurale sunt poziționate în raport cu modelul
        arhitectural.
3.  **Mechanical**
    -   Sunt introduse echipamente HVAC (în clip este ilustrată o
        unitate interioară).
    -   Sunt modelate traseele mecanice/tubulaturile.
4.  **Plumbing**
    -   Sunt adăugate traseele instalațiilor sanitare.
    -   Clipul evidențiază traseele de apă caldă, marcate cu roșu.
5.  **Clash Detection**
    -   Sunt verificate interferențele dintre conducte, tubulaturi și
        celelalte elemente.
    -   Conflictele sunt marcate vizual.
    -   Modelul este corectat și verificat din nou; cadrul final indică
        „zero clashes".
6.  **Coordinated Model**
    -   Rezultatul este un model coordonat unic, care reunește
        disciplinele prezentate.

**Ce nu poate fi confirmat din clip:** denumirea și versiunea tuturor
programelor utilizate, dacă modelarea este complet automată, precizia
rezultatelor, formatul fișierelor, regulile de clash detection, timpul
real de execuție și dacă „zero clashes" înseamnă zero interferențe
geometrice sau doar zero conflicte rămase în acel exemplu.

## 2. Obiectivul pentru Casa3D

Casa3D trebuie să poată transforma un proiect de construcție din surse
2D/3D într-un **model digital coordonat**, cu discipline separate, dar
aliniate într-un sistem comun de coordonate.

Nu implementa doar o funcție „2D to 3D". Construiește un pipeline
verificabil:

`Import → Interpretare → Validare → Model arhitectural → Structură → Instalații → Coordonare → Clash Detection → Corecții → Verificare → Export`

Principii: - Modelul sursă nu se suprascrie distructiv. - Fiecare
element are identificator stabil, geometrie, dimensiuni, disciplină și
proveniență. - Unitățile și sistemul de coordonate sunt explicite. -
AI-ul poate propune interpretări și modificări, dar nu inventează cote
sau soluții structurale. - Orice modificare automată trebuie să poată fi
revizuită, comparată și anulată. - „Valid" nu înseamnă automat „conform
normativ" sau „sigur pentru execuție".

## 3. Arhitectura funcțională propusă

### 3.1 Ingestion / Import

Surse de intrare: - PDF cu planuri (vectorial sau scanat). - Imagini
PNG/JPG/TIFF. - DXF/DWG, în funcție de licențele și bibliotecile
disponibile. - IFC și modele 3D existente. - Formate native ale
aplicațiilor conectate, numai prin API-uri sau conectori verificați.

Cerințe: - Detectarea unităților și a scării; dacă nu pot fi deduse,
solicitarea confirmării utilizatorului. - Păstrarea fișierului original
și calcularea hash-ului. - Înregistrarea numelui, versiunii, datei
importului și a sursei. - Separarea OCR/vector extraction de
interpretarea semantică. - Raportarea elementelor detectate cu nivel de
încredere și posibilitatea corectării manuale.

### 3.2 Plan Interpretation

Identificarea asistată a: - pereților exteriori și interiori; - ușilor
și ferestrelor; - camerelor și zonelor; - cotelor, axelor și
nivelurilor; - simbolurilor și textelor de pe plan.

Rezultatul nu trebuie să fie doar o imagine segmentată. Produce un model
semantic cu geometrie și relații.

Fiecare obiect interpretat trebuie să includă: - `element_id` stabil; -
`element_type`; - `discipline`; - geometrie și transformare; - unitate
de măsură; - sursa (fișier, pagină/layer/regiune); - metoda de
extragere; - confidence; - stare: `candidate`, `reviewed`, `approved`
sau `rejected`.

Obiectele cu încredere redusă sau cote contradictorii intră în revizie
umană.

### 3.3 Architecture Model

Generează geometria arhitecturală: - niveluri și planșee; - pereți cu
grosime și înălțime; - goluri pentru uși și ferestre; - încăperi și
zone; - acoperiș și scări, dacă sunt definite în sursă.

Păstrează separat: 1. geometria derivată direct din sursă; 2. geometria
inferată; 3. geometria introdusă manual; 4. geometria aprobată.

Nu deduce automat înălțimi, grosimi sau cote lipsă fără marcarea lor ca
ipoteze și fără confirmare.

### 3.4 Structural Model

Modul separat pentru: - fundații/footings; - stâlpi; - grinzi; - planșee
structurale; - pereți structurali; - alte elemente definite de
proiectant.

**Limită de siguranță:** Casa3D poate genera reprezentări și verifica
poziționarea geometrică, dar nu trebuie să dimensioneze sau să declare
sigură structura fără un motor de calcul validat, date complete, reguli
aplicabile și aprobarea unui inginer structurist autorizat.

### 3.5 MEP -- Mechanical, Electrical, Plumbing

Clipul ilustrează Mechanical și Plumbing. Pentru un produs complet,
propun extinderea cu Electrical.

**Mechanical / HVAC** - echipamente și unități; - tubulaturi; - trasee
și accesorii; - zone de mentenanță și spații de acces, dacă sunt
furnizate.

**Plumbing** - apă rece și caldă; - canalizare; - coloane și
distribuții; - obiecte sanitare și echipamente.

**Electrical -- extensie propusă, nu demonstrată în clip** - tablouri; -
circuite și trasee; - prize, întrerupătoare și corpuri de iluminat; -
trasee de cabluri și tubulaturi.

Fiecare rețea trebuie să aibă tip, diametru/secțiune când este
cunoscută, material, cotă, pantă unde este cazul, sistem și
conectivitate. Nu inventa valori tehnice lipsă.

### 3.6 Federated / Coordinated Model

Nu combina toate disciplinele într-un singur obiect monolitic. Păstrează
modele disciplinare distincte și creează o vedere federată: - coordonate
comune; - transformări explicite; - unități comune; - identificatori
păstrați între import/export; - vizibilitate și filtre pe disciplină; -
versiuni și relații între versiuni.

Astfel, modificarea instalației nu trebuie să distrugă modelul
arhitectural sau structural.

### 3.7 Clash Detection

Motorul trebuie să detecteze cel puțin: - **Hard clash:** volume care se
intersectează. - **Clearance clash:** distanță insuficientă față de un
spațiu minim necesar. - **Workflow/rule clash:** conflict între reguli
sau cerințe definite de proiect.

Pentru fiecare conflict înregistrează: - `clash_id`; - elementele
implicate; - disciplinele; - tipul conflictului; - coordonatele/volumul
intersecției; - regula și toleranța aplicată; - severitatea; - versiunea
modelelor verificate; - status: `new`, `reviewed`, `accepted`,
`resolved`, `false_positive`; - responsabil și justificare; -
captură/vedere pentru revizie.

Flux: 1. Rulează verificarea pe versiuni identificate ale modelelor. 2.
Grupează conflictele duplicate. 3. Afișează fiecare conflict în context
3D. 4. Propune opțional soluții candidate, fără aplicare automată. 5.
Cere aprobarea utilizatorului pentru modificări. 6. Reexecută
verificarea. 7. Generează raport comparativ înainte/după.

**Zero clashes** trebuie raportat numai cu domeniul și regulile testate:
de exemplu, „0 hard clashes pentru disciplinele A/B, toleranța X,
versiunea Y". Nu declara modelul fără conflicte dacă anumite discipline,
reguli sau zone nu au fost verificate.

## 4. Integrarea cu Blender și alte aplicații

### 4.1 Blender

Folosește Blender ca motor de vizualizare și modelare procedurală acolo
unde este potrivit: - importul geometriei; - generarea pereților,
golurilor și elementelor repetitive; - organizarea pe colecții și
discipline; - materiale și iluminare; - randări și animații; -
vizualizarea conflictelor; - exportul formatelor suportate de pipeline.

Automatizarea Blender se poate realiza prin Python API (`bpy`), dar
verifică versiunea instalată și API-ul disponibil înainte de
implementare.

Blender nu trebuie tratat implicit ca înlocuitor complet pentru un
sistem BIM nativ. Păstrează separat metadatele și identitatea obiectelor
și verifică fidelitatea la export/import.

### 4.2 Alte categorii de aplicații

Proiectează adaptoare independente pentru: - CAD 2D: desen tehnic și
planuri; - BIM: modele disciplinare și IFC; - modelare 3D: geometrie și
vizualizare; - randare: imagini și animații; - GIS/site planning: teren,
amplasament și coordonate; - documentație: planșe, liste de cantități și
rapoarte; - coordonare: issue tracking și clash reports.

Nu presupune că un produs sau API este gratuit, disponibil ori
compatibil. Verifică licența, versiunea, suportul de automatizare și
condițiile de utilizare înainte de a-l selecta.

### 4.3 Aplicații „de genul acesta"

Construiește un nucleu reutilizabil, nu o soluție rigidă doar pentru
case. Același pipeline poate deservi: - amenajări interioare; -
renovări; - clădiri comerciale; - hale și spații industriale; -
instalații și trasee tehnice; - planificare de amplasament; - prezentări
3D interactive; - aplicații de configurare și vizualizare a
produselor/spațiilor.

Pentru fiecare domeniu se schimbă schemele, regulile și adaptoarele, nu
nucleul de import, provenance, coordonare, versiuni și validare.

## 5. Arhitectură software recomandată

Separă sistemul în servicii/module cu contracte clare:

``` text
Casa3D
├── Project Workspace
│   ├── Project Metadata
│   ├── Versioning
│   └── Approval & Audit
├── Ingestion
│   ├── PDF / Image
│   ├── DXF / DWG Adapter
│   └── IFC / 3D Adapter
├── Plan Intelligence
│   ├── OCR / Vector Extraction
│   ├── Geometry Recognition
│   └── Human Review
├── Model Core
│   ├── Coordinate System
│   ├── Units & Geometry
│   ├── Element Identity
│   └── Semantic Metadata
├── Discipline Modules
│   ├── Architecture
│   ├── Structure
│   ├── Mechanical
│   ├── Plumbing
│   └── Electrical
├── Coordination
│   ├── Federated Model
│   ├── Clash Engine
│   └── Issue Management
├── Application Adapters
│   ├── Blender
│   ├── CAD
│   ├── BIM / IFC
│   └── Rendering
├── AI Orchestrator
│   ├── Task Planning
│   ├── Tool Selection
│   ├── Candidate Generation
│   └── Evidence-linked Explanations
└── Outputs
    ├── 3D Viewer
    ├── Render / Animation
    ├── Drawings
    └── Validation Reports
```

### Contracte de date

Definește un format intern canonic pentru elemente și conflicte. IFC
poate fi folosit ca format interoperabil, dar nu presupune că toate
aplicațiile păstrează toate proprietățile la conversie.

Minimum pentru un element:

``` json
{
  "element_id": "stable-id",
  "discipline": "architecture",
  "element_type": "wall",
  "geometry_ref": "geometry-resource",
  "transform": {},
  "units": "m",
  "properties": {},
  "source_ref": {
    "file_hash": "sha256:...",
    "page_or_layer": "source-location",
    "extraction_method": "vector|ocr|manual|import"
  },
  "confidence": null,
  "review_status": "candidate",
  "model_version": "version-id"
}
```

Valorile de exemplu sunt placeholder-e de schemă, nu date extrase din
clip.

## 6. Interfața utilizatorului

Propun un workspace cu: - viewport 3D central; - navigator de proiect și
niveluri; - tree de discipline; - proprietățile elementului selectat; -
listă de conflicte; - panou de sursă/proveniență; - comparație între
versiuni; - acțiuni de aprobare și rollback.

Moduri de vizualizare: - Architecture; - Structure; - Mechanical; -
Plumbing; - Electrical; - Federated; - Clash Review.

Selectarea unui element în 3D trebuie să permită identificarea sursei,
proprietăților, disciplinei și versiunii.

## 7. Controlul AI și integritatea proiectului

AI poate: - clasifica elemente candidate din plan; - explica elementele
detectate; - propune geometrie pe baza cotelor disponibile; - genera
scripturi procedurale; - identifica posibile interferențe; - propune
alternative pentru revizie; - produce rapoarte legate de datele
modelului.

AI nu poate: - inventa cote sau materiale; - prezenta inferențe ca date
măsurate; - declara conformitate legală/tehnică fără verificări; -
modifica modelul aprobat fără autorizare; - elimina conflicte din raport
doar pentru a obține un rezultat „verde".

Orice modificare trebuie să respecte:

`validate → dry-run → diff → approval → apply → verify → audit`

Înregistrează actorul, momentul, inputurile, versiunea modelului,
acțiunea, rezultatul și aprobarea. Pentru mod offline/air-gapped, nu
utiliza servicii cloud sau apeluri de rețea.

## 8. Teste și criterii de acceptare

### Import și geometrie

-   Importul păstrează unitățile și scara.
-   Conversia PDF/imagine nu pretinde precizie dacă planul nu conține o
    referință dimensională.
-   Geometria generată poate fi comparată cu dimensiunile sursă.
-   Elementele incerte sunt marcate pentru revizie.

### Model disciplinar

-   Fiecare element are ID stabil.
-   Filtrarea pe discipline funcționează.
-   Modificarea unei discipline nu corupe celelalte discipline.
-   Export/import nu pierde în tăcere proprietăți esențiale.

### Clash Detection

-   Teste pozitive: două volume care se intersectează trebuie detectate.
-   Teste negative: volume separate peste toleranță nu trebuie raportate
    ca hard clash.
-   Clearance: distanțele-limită sunt testate explicit.
-   Rezultatele sunt reproductibile pentru aceleași inputuri, versiuni
    și reguli.
-   Fiecare conflict are referințe către elementele implicate.
-   Raportul precizează regulile și disciplinele efectiv verificate.

### Audit și siguranță

-   Nu se suprascrie sursa originală.
-   Modificările aprobate pot fi urmărite și comparate.
-   Rollback-ul restaurează versiunea anterioară.
-   Nu se aplică automat modificări structurale sau MEP cu impact tehnic
    fără aprobarea competentă.

## 9. Plan de implementare în faze

**Faza 0 -- Auditul proiectului existent** - Inspectează repository-ul
Casa3D, arhitectura, stack-ul, testele și funcționalitățile deja
existente. - Identifică module reutilizabile și incompatibilități. - Nu
rescrie componente funcționale fără justificare. - Livrează inventar
REAL / PARTIAL / UNKNOWN și gap analysis.

**Faza 1 -- Model Core** - Unități, coordonate, geometrie, identitate
stabilă, metadate și versioning. - Teste pentru transformări și
conversii.

**Faza 2 -- Import și Architecture** - Importul unui plan de test. -
Calibrarea scării. - Interpretare asistată și revizie. - Generare
pereți, goluri și planșee.

**Faza 3 -- Blender Adapter** - Import/export controlat. - Generare
procedurală prin Python API. - Organizare pe colecții și păstrarea
identificatorilor.

**Faza 4 -- Structure și MEP** - Module disciplinare separate. -
Elemente și trasee cu proprietăți explicite. - Nu implementa calcule de
dimensionare nevalidate.

**Faza 5 -- Federated Model și Clash Detection** - Coordonare între
discipline. - Hard clash și clearance. - Issue lifecycle și rapoarte
reproductibile.

**Faza 6 -- UI și livrabile** - Viewer, filtre, proprietăți, issue
review. - Randări, animații, planșe și rapoarte.

**Faza 7 -- Extensibilitate** - Adaptoare CAD/BIM/GIS și aplicații de
prezentare. - Plugin API documentat. - Teste de compatibilitate și
licențiere.

Nu sări peste faze prin implementarea simultană a tuturor modulelor.
Fiecare fază trebuie să aibă criterii de acceptare și rezultate testate.

## 10. Instrucțiuni de execuție pentru Claude Code

1.  Deschide repository-ul Casa3D și verifică branch-ul, status-ul Git,
    structura, documentația și testele.
2.  Nu presupune stack-ul, framework-ul, API-urile sau funcționalitățile
    existente.
3.  Înainte de modificări, prezintă arhitectura curentă și un gap
    analysis față de acest document.
4.  Propune un plan incremental cu fișiere/module concrete, interfețe,
    riscuri și teste.
5.  Păstrează compatibilitatea cu proiectul existent. Nu elimina
    funcționalități și nu schimba contracte publice fără justificare.
6.  Folosește TDD unde este relevant: teste care demonstrează
    comportamentul, implementare minimă, refactorizare.
7.  Verifică documentația oficială pentru versiunile reale ale
    aplicațiilor și API-urilor. Nu inventa funcții Blender, CAD, BIM sau
    IFC.
8.  Nu instala software, extensii sau dependențe cu licență/impact de
    securitate fără să raportezi motivul, sursa, licența și necesarul de
    aprobare.
9.  Nu trimite proiecte sau planuri tehnice către servicii cloud fără
    aprobare explicită.
10. Nu modifica automat fișierele originale ale proiectului de
    construcție.
11. Rulează testele relevante și raportează comenzile și rezultatele
    reale.
12. La finalul fiecărei faze, raportează:
    -   **REAL:** implementat și verificat;
    -   **PARTIAL:** implementat incomplet sau cu limitări;
    -   **UNKNOWN:** neverificat/neimplementat;
    -   fișiere schimbate;
    -   teste rulate și rezultate;
    -   riscuri și limitări;
    -   următorul pas recomandat.

### Regula de finalizare

Nu declara `DONE`, `FIXED` sau `GREEN` doar pentru că implementarea pare
completă. Aceste etichete sunt permise numai după verificarea efectivă a
testelor și a criteriilor de acceptare. Nu masca erori, nu slăbi testele
și nu elimina security gates pentru a obține CI verde.

------------------------------------------------------------------------

## 11. Rezultatul urmărit

Casa3D trebuie să evolueze de la un instrument de modelare/vizualizare
într-o platformă extensibilă de coordonare digitală a proiectelor:

-   plan 2D → model arhitectural 3D;
-   model arhitectural → structură și instalații;
-   discipline separate → model federat;
-   model federat → verificări de interferențe;
-   verificări → issue-uri trasabile și corecții aprobate;
-   model validat → randări, animații, planșe și exporturi.

**Prioritatea tehnică:** Model Core + import cu proveniență +
Architecture + Blender Adapter. După ce acest nucleu este stabil și
testat, adaugă disciplinele și Clash Detection.
