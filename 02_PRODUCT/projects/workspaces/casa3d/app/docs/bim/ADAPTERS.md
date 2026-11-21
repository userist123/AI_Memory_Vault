# Adaptoare Casa3D — contract (ghidul BIM §4.2, faza 7)

Registrul: `core/adapters.ts` (`ADAPTERS`, `EXTERNAL_ADAPTERS`). Teste de compatibilitate: `tests/bim-adapters.test.ts`.

## Principii

1. **Nucleul nu se schimbă pentru un adaptor.** Snapshot (`core/types.ts`) e sursa de adevăr (CONSTITUTION art. 1); disciplinele
   și identitatea vin din `core/elements.ts`; validarea din `lib/repo.ts` `checkSnapshot`.
2. **Importul nu scrie direct în proiect.** Un adaptor de import întoarce *candidați* cu proveniență (metodă, amprenta fișierului,
   strat/pagină, încredere). Utilizatorul îi aprobă; aplicarea trece prin `checkSnapshot`. Valorile lipsă rămân lipsă (art. 4).
3. **Exportul e determinist.** Aceleași intrări → același text (data vine din parametri). Id-urile stabile se păstrează acolo unde
   formatul permite (IFC: GlobalId derivat din id-ul elementului).
4. **Fidelitatea e declarată.** Fiecare adaptor listează ce păstrează (`preserves`) și ce pierde (`loses`).
5. **Licența și locul de rulare sunt declarate** (`implementation`, `runsIn`). Planurile nu se trimit în cloud (art. 14).
   O dependență nouă se adaugă doar după verificarea licenței, versiunii și a condițiilor de utilizare (ghidul §4.2, §10.8).

## Adăugarea unui adaptor

```ts
// core/adapters.ts
{ id: 'format-export', direction: 'export', category: 'cad' | 'bim' | 'model3d' | 'render' | 'report',
  format: 'Nume format + versiune', extension: 'ext', status: 'experimental',
  implementation: 'cine implementează + licența', runsIn: 'browser' | 'server' | 'external',
  preserves: ['...'], loses: ['...'],
  run: (snapshot, catalog, { lang, name, timestamp }) => text }
```

Testele din `tests/bim-adapters.test.ts` rulează automat orice adaptor de export nou pe proiectul comun
(`tests/fixtures/bim-snap.ts`) și cer: rezultat nevid, determinism, licență și fidelitate declarate. Un adaptor rămâne
`experimental` (marcat așa în interfață, art. 10) până trece testele de acceptare ale fazei lui (art. 9).

## Adaptoare existente

| Id | Direcție | Format | Stare | Unde rulează | Implementare |
|---|---|---|---|---|---|
| `dxf-export` | export | DXF R12 ASCII | stabil | server (`/api/projects/[id]/export.dxf`) | cod propriu |
| `ifc4-export` | export | IFC4 STEP | experimental | server (`/api/projects/[id]/export.ifc`) | cod propriu |
| `coordination-report` | export | Markdown | experimental | browser (panoul de coordonare) | cod propriu |
| `dxf-import` | import | DXF ASCII | experimental | browser (panoul de import) | cod propriu |
| `blender-scene` | export | casa3d-scene JSON → Blender | stabil | extern (Blender, GPL, rulat de utilizator) | cod propriu |
| `glb-export` | export | glTF binar | stabil | browser | three.js GLTFExporter (MIT) |

## Verificare independentă a IFC

Exportul IFC a fost deschis cu IfcOpenShell 0.9.0 (LGPL-3.0; instalat doar într-un mediu Python temporar pentru verificare,
nu e dependență a produsului): `ifcopenshell.validate` fără nicio problemă de schemă, geometria construită pentru toate
elementele cu volum (proiectul de test: 12/12; proiectul demo din aplicație: 42/42), axa traseelor fără diametru construită
ca curbă. Scriptul de verificare nu face parte din repository; pașii sunt în `FAZA0_INVENTAR.md` §4.

## Neacoperit (decizie, nu omisiune)

- **DWG**: format proprietar; bibliotecile libere compatibile comercial lipsesc (ODA e cu plată, LibreDWG e GPLv3). Se cere
  utilizatorului DXF.
- **Import IFC**: cere un parser IFC complet (de ex. IfcOpenShell, sidecar Python, LGPL) — de evaluat separat.
- **PDF vectorial și OCR / recunoașterea planurilor scanate**: fără țintă de acuratețe și set de test încă; art. 14 cere rulare
  locală. Rămân pe pista ghidului, neimplementate.
- **GIS / amplasament**: în afara produsului actual (amenajări și renovări).
