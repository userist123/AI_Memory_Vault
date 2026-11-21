// Export IFC4 (docs/bim/GHID_BIM.md §4.2, Faza 7; CONSTITUTION art. 14: experimental). Scriere STEP (ISO 10303-21) proprie,
// fără dependențe. Coordonate: X = x din plan, Y = −z din plan (ca în DXF), Z = cota; metri.
// Ce intră: proiect → teren → clădire → niveluri; camere (IfcSpace), pereți cu goluri (IfcOpeningElement + IfcDoor/IfcWindow),
// stâlpi, grinzi, fundații, tavane false (IfcCovering), mobilier cu dimensiuni cunoscute, trasee (IfcPipeSegment,
// IfcDuctSegment, IfcCableCarrierSegment). GlobalId e derivat din id-ul elementului: același element → același GlobalId la
// fiecare export. Traseele fără diametru declarat au doar axa (reprezentarea „Axis”), nu un volum inventat; diametrul apare
// în proprietăți numai când e declarat. Ce nu intră (raportat în FAZA0_INVENTAR.md): scări, nișe, obiecte pe perete, finisaje.
import type { Catalog, MepSystem, Snapshot } from './types';
import { floors, elevationOf, SLAB } from './levels';
import { footprintOf } from './validate';
import { resolve } from './catalog';
import { elementIndex } from './elements';
import { DOOR_HEIGHT } from './rules.boq';

const B64 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$';
const fnv = (s: string, seed: number) => { let h = seed >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; } return h; };
/** GlobalId IFC (22 de caractere, 128 de biți) determinist dintr-un text: patru hash-uri FNV-1a cu semințe diferite. */
export function ifcGuid(key: string): string {
  let n = 0n; for (const seed of [0x811c9dc5, 0x01000193, 0x9e3779b9, 0x85ebca6b]) n = (n << 32n) | BigInt(fnv(key, seed));
  let out = ''; for (let i = 0; i < 22; i++) { out = B64[Number(n & 63n)]! + out; n >>= 6n; } return out;
}
/** Text STEP: apostroful dublat, caracterele în afara ASCII tipărit codate \X2\HHHH\X0\. */
export function stepString(s: string): string {
  let o = ''; for (const ch of s) { const c = ch.codePointAt(0)!;
    if (ch === "'") o += "''"; else if (ch === '\\') o += '\\\\'; else if (c >= 32 && c < 127) o += ch;
    else if (c <= 0xffff) o += `\\X2\\${c.toString(16).toUpperCase().padStart(4, '0')}\\X0\\`; else o += `\\X4\\${c.toString(16).toUpperCase().padStart(8, '0')}\\X0\\`; }
  return `'${o}'`;
}
const real = (v: number) => { const r = Math.round(v * 1e6) / 1e6, s = String(r === 0 ? 0 : r); return /[.eE]/.test(s) ? s : s + '.'; };

const RUN_ENTITY: Record<MepSystem, { type: string; predefined: string }> = {
  water_cold: { type: 'IFCPIPESEGMENT', predefined: '.RIGIDSEGMENT.' }, water_hot: { type: 'IFCPIPESEGMENT', predefined: '.RIGIDSEGMENT.' },
  drain: { type: 'IFCPIPESEGMENT', predefined: '.RIGIDSEGMENT.' }, refrigerant: { type: 'IFCPIPESEGMENT', predefined: '.RIGIDSEGMENT.' },
  hvac_supply: { type: 'IFCDUCTSEGMENT', predefined: '.RIGIDSEGMENT.' }, hvac_return: { type: 'IFCDUCTSEGMENT', predefined: '.RIGIDSEGMENT.' },
  electric: { type: 'IFCCABLECARRIERSEGMENT', predefined: '.CONDUITSEGMENT.' },
};

export interface IfcExportResult { text: string; counts: Record<string, number>; skipped: { id: string; reason: string }[] }

export function snapshotToIfc(s: Snapshot, cat: Catalog, meta: { fileName: string; timestamp: string }): IfcExportResult {
  const lines: string[] = []; let n = 0;
  const e = (body: string) => { n++; lines.push(`#${n}=${body};`); return `#${n}`; };
  const counts: Record<string, number> = {}, skipped: { id: string; reason: string }[] = [];
  const count = (t: string) => { counts[t] = (counts[t] ?? 0) + 1; };
  const key = (id: string) => ifcGuid(`${s.name}|${id}`), list = (xs: string[]) => `(${xs.join(',')})`;
  const pt3 = (x: number, y: number, z: number) => e(`IFCCARTESIANPOINT((${real(x)},${real(y)},${real(z)}))`);
  const pt2 = (x: number, y: number) => e(`IFCCARTESIANPOINT((${real(x)},${real(y)}))`);
  const dir3 = (x: number, y: number, z: number) => e(`IFCDIRECTION((${real(x)},${real(y)},${real(z)}))`);
  const ax3 = (o: string, z?: string, x?: string) => e(`IFCAXIS2PLACEMENT3D(${o},${z ?? '$'},${x ?? '$'})`);
  const place = (rel: string | null, x: number, y: number, z: number, rx = 1, ry = 0) => e(`IFCLOCALPLACEMENT(${rel ?? '$'},${ax3(pt3(x, y, z), dir3(0, 0, 1), dir3(rx, ry, 0))})`);

  // ---- antet, unități, context ----
  const units = e(`IFCUNITASSIGNMENT((${e('IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)')},${e('IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)')},${e('IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)')},${e('IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)')}))`);
  const ctx = e(`IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,${ax3(pt3(0, 0, 0))},$)`);
  const body = e(`IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,${ctx},$,.MODEL_VIEW.,$)`);
  const axisCtx = e(`IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Axis','Model',*,*,*,*,${ctx},$,.GRAPH_VIEW.,$)`);
  const project = e(`IFCPROJECT('${key('project')}',$,${stepString(s.name)},$,$,$,$,(${ctx}),${units})`);
  const sitePl = place(null, 0, 0, 0), site = e(`IFCSITE('${key('site')}',$,'Site',$,$,${sitePl},$,$,.ELEMENT.,$,$,$,$,$)`);
  const bldPl = place(sitePl, 0, 0, 0), bld = e(`IFCBUILDING('${key('building')}',$,${stepString(s.name)},$,$,${bldPl},$,$,.ELEMENT.,$,$,$)`);
  e(`IFCRELAGGREGATES('${key('agg-project')}',$,$,$,${project},(${site}))`); e(`IFCRELAGGREGATES('${key('agg-site')}',$,$,$,${site},(${bld}))`);

  // ---- geometrie: extrudare verticală a unui dreptunghi orientat în plan; extrudare a unui cerc pe o direcție ----
  const shape = (items: string[], ident = 'Body', type = 'SweptSolid', c = body) => e(`IFCPRODUCTDEFINITIONSHAPE($,$,(${e(`IFCSHAPEREPRESENTATION(${c},'${ident}','${type}',${list(items)})`)}))`);
  const rectSolid = (len: number, width: number, height: number, cx = 0, cy = 0) =>
    e(`IFCEXTRUDEDAREASOLID(${e(`IFCRECTANGLEPROFILEDEF(.AREA.,$,${e(`IFCAXIS2PLACEMENT2D(${pt2(cx, cy)},$)`)},${real(len)},${real(width)})`)},${ax3(pt3(0, 0, 0))},${dir3(0, 0, 1)},${real(height)})`);
  /** element cu placement propriu: (x, y) în planul IFC, z = baza, direcția axei locale x = (ux, uy) */
  const props = (elId: string, id: string, extra: string[]) => {
    const p = [e(`IFCPROPERTYSINGLEVALUE('ElementId',$,IFCIDENTIFIER(${stepString(id)}),$)`), ...extra];
    const ps = e(`IFCPROPERTYSET('${key('pset|' + id)}',$,'Casa3D_Element',$,${list(p)})`);
    e(`IFCRELDEFINESBYPROPERTIES('${key('rel-pset|' + id)}',$,$,$,(${elId}),${ps})`);
  };
  const disc = new Map(elementIndex(s).map(x => [x.elementId, x.discipline]));
  const label = (name: string, v: string) => e(`IFCPROPERTYSINGLEVALUE('${name}',$,IFCLABEL(${stepString(v)}),$)`);

  const all = floors(s);
  all.forEach((f, li) => {
    const elev = elevationOf(s, li), H = f.ceilingHeight;
    const stPl = place(bldPl, 0, 0, elev), storey = e(`IFCBUILDINGSTOREY('${key('storey|' + f.id)}',$,${stepString(f.name || (li === 0 ? 'Parter' : `Nivel ${li}`))},$,$,${stPl},$,$,.ELEMENT.,${real(elev)})`);
    count('IFCBUILDINGSTOREY'); const contained: string[] = [], spaces: string[] = [];
    const elem = (type: string, id: string, name: string, x: number, y: number, z: number, ux: number, uy: number, rep: string | null, predefined = '.NOTDEFINED.', extra: string[] = []) => {
      const pl = place(stPl, x, y, z, ux, uy), el = e(`${type}('${key(id)}',$,${stepString(name)},$,$,${pl},${rep ?? '$'},${stepString(id)},${predefined})`);
      count(type); contained.push(el); props(el, id, [label('Discipline', disc.get(id) ?? 'architecture'), ...extra]); return { el, pl };
    };
    // camere
    for (const r of f.rooms) { const poly = (r.boundary && r.boundary.length >= 3 ? r.boundary : [[r.rect.x0, r.rect.z0], [r.rect.x1, r.rect.z0], [r.rect.x1, r.rect.z1], [r.rect.x0, r.rect.z1]]) as [number, number][];
      const pts = [...poly, poly[0]!].map(([x, z]) => pt2(x, -z)), prof = e(`IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,${e(`IFCPOLYLINE(${list(pts)})`)})`);
      const solid = e(`IFCEXTRUDEDAREASOLID(${prof},${ax3(pt3(0, 0, 0))},${dir3(0, 0, 1)},${real(H)})`);
      const sp = e(`IFCSPACE('${key(r.id)}',$,${stepString(r.name)},$,$,${place(stPl, 0, 0, 0)},${shape([solid])},${stepString(r.id)},.INTERNAL.,.SPACE.,$)`);
      count('IFCSPACE'); spaces.push(sp); }
    // pereți și goluri
    for (const w of f.walls) { if (w.status === 'demolish') continue; const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]); if (L < 1e-6) continue;
      const ux = (w.b[0] - w.a[0]) / L, uy = -(w.b[1] - w.a[1]) / L;
      const { el, pl } = elem('IFCWALL', w.id, w.structural ? 'Perete structural' : 'Perete', w.a[0], -w.a[1], 0, ux, uy, shape([rectSolid(L, w.thickness, H, L / 2, 0)]), '.STANDARD.',
        w.structural ? [e(`IFCPROPERTYSINGLEVALUE('LoadBearing',$,IFCBOOLEAN(.T.),$)`)] : []);   // în același set: un singur Casa3D_Element pe element
      for (const o of w.openings) { const door = o.kind === 'door', y0 = door ? 0 : o.sill ?? .9, h = door ? Math.min(H, o.height ?? DOOR_HEIGHT) : Math.min(H - y0, o.height ?? 1.3);
        const opPl = e(`IFCLOCALPLACEMENT(${pl},${ax3(pt3(0, 0, y0))})`);
        const op = e(`IFCOPENINGELEMENT('${key('void|' + o.id)}',$,'Gol',$,$,${opPl},${shape([rectSolid(o.width, w.thickness + .1, h, o.offset + o.width / 2, 0)])},$,.OPENING.)`);
        e(`IFCRELVOIDSELEMENT('${key('rel-void|' + o.id)}',$,$,$,${el},${op})`); count('IFCOPENINGELEMENT');
        const fill = door ? e(`IFCDOOR('${key(o.id)}',$,'Usa',$,$,${opPl},$,${stepString(o.id)},${real(h)},${real(o.width)},.DOOR.,$,$)`)
          : e(`IFCWINDOW('${key(o.id)}',$,'Fereastra',$,$,${opPl},$,${stepString(o.id)},${real(h)},${real(o.width)},.WINDOW.,$,$)`);
        count(door ? 'IFCDOOR' : 'IFCWINDOW'); contained.push(fill); e(`IFCRELFILLSELEMENT('${key('rel-fill|' + o.id)}',$,$,$,${op},${fill})`); } }
    // structură
    for (const c of f.columns ?? []) { if ((c.role ?? 'structural') !== 'structural') continue; const h = Math.min(H, c.height ?? H);
      const rep = c.shape === 'round' ? shape([e(`IFCEXTRUDEDAREASOLID(${e(`IFCCIRCLEPROFILEDEF(.AREA.,$,${e(`IFCAXIS2PLACEMENT2D(${pt2(0, 0)},$)`)},${real(c.w / 2)})`)},${ax3(pt3(0, 0, 0))},${dir3(0, 0, 1)},${real(h)})`)]) : shape([rectSolid(c.w, c.d, h)]);
      elem('IFCCOLUMN', c.id, 'Stalp', c.x, -c.z, 0, 1, 0, rep, '.COLUMN.'); }
    for (const b of f.beams ?? []) { const L = Math.hypot(b.b[0] - b.a[0], b.b[1] - b.a[1]); if (L < 1e-6) continue;
      elem('IFCBEAM', b.id, 'Grinda', b.a[0], -b.a[1], H - b.depth, (b.b[0] - b.a[0]) / L, -(b.b[1] - b.a[1]) / L, shape([rectSolid(L, b.width, b.depth, L / 2, 0)]), '.BEAM.'); }
    for (const x of f.footings ?? []) elem('IFCFOOTING', x.id, 'Fundatie', x.x, -x.z, -SLAB - x.depth, 1, 0, shape([rectSolid(x.w, x.d, x.depth)]), x.kind === 'strip' ? '.STRIP_FOOTING.' : x.kind === 'pad' ? '.PAD_FOOTING.' : '.NOTDEFINED.');
    for (const z of f.ceilingZones ?? []) { const y = H - z.dropCm / 100;
      elem('IFCCOVERING', z.id, 'Tavan fals', (z.rect.x0 + z.rect.x1) / 2, -(z.rect.z0 + z.rect.z1) / 2, y - .0125, 1, 0, shape([rectSolid(z.rect.x1 - z.rect.x0, z.rect.z1 - z.rect.z0, .0125)]), '.CEILING.'); }
    // mobilier (doar cu dimensiuni cunoscute)
    const rooms = new Set(f.rooms.map(r => r.id));
    for (const p of s.placements) { if (!rooms.has(p.roomId)) continue; const fp = footprintOf(cat, p), rv = resolve(cat, p.variantId), h = p.size ? p.size.h / 100 : rv?.h ?? 0;
      if (!fp || !(h > 0)) { skipped.push({ id: p.id, reason: 'unknown-size' }); continue; }
      elem('IFCFURNITURE', p.id, rv?.product.name ?? p.group, (fp.x0 + fp.x1) / 2, -(fp.z0 + fp.z1) / 2, 0, 1, 0, shape([rectSolid(fp.x1 - fp.x0, fp.z1 - fp.z0, h)]), '.NOTDEFINED.'); }
    // trasee
    for (const r of f.mepRuns ?? []) { const T = RUN_ENTITY[r.system]; if (!T) continue;
      let rep: string;
      if (r.diameterMm) { const rad = r.diameterMm / 2000, items: string[] = [];
        for (let k = 1; k < r.points.length; k++) { const a = r.points[k - 1]!, b = r.points[k]!, d = [b[0] - a[0], -(b[2] - a[2]), b[1] - a[1]], L = Math.hypot(d[0]!, d[1]!, d[2]!); if (L < 1e-6) continue;
          const zx = d[0]! / L, zy = d[1]! / L, zz = d[2]! / L, rx = Math.abs(zz) > .9 ? [1, 0, 0] : [-zy, zx, 0], rn = Math.hypot(rx[0]!, rx[1]!, rx[2]!);
          const pos = ax3(pt3(a[0], -a[2], a[1]), dir3(zx, zy, zz), dir3(rx[0]! / rn, rx[1]! / rn, rx[2]! / rn));
          items.push(e(`IFCEXTRUDEDAREASOLID(${e(`IFCCIRCLEPROFILEDEF(.AREA.,$,${e(`IFCAXIS2PLACEMENT2D(${pt2(0, 0)},$)`)},${real(rad)})`)},${pos},${dir3(0, 0, 1)},${real(L)})`)); }
        rep = shape(items); }
      else rep = shape([e(`IFCPOLYLINE(${list(r.points.map(p => pt3(p[0], -p[2], p[1])))})`)], 'Axis', 'Curve3D', axisCtx);
      const pl = place(stPl, 0, 0, 0), el = e(`${T.type}('${key(r.id)}',$,${stepString(r.system)},$,$,${pl},${rep},${stepString(r.id)},${T.predefined})`);
      count(T.type); contained.push(el);
      props(el, r.id, [label('Discipline', disc.get(r.id) ?? 'plumbing'), label('System', r.system),
        ...(r.diameterMm ? [e(`IFCPROPERTYSINGLEVALUE('NominalDiameterMm',$,IFCPOSITIVELENGTHMEASURE(${real(r.diameterMm / 1000)}),$)`)] : [label('NominalDiameter', 'UNKNOWN')]),
        ...(r.material ? [label('Material', r.material)] : []), ...(r.slopePct != null ? [e(`IFCPROPERTYSINGLEVALUE('SlopePct',$,IFCREAL(${real(r.slopePct)}),$)`)] : [])]); }
    if (spaces.length) e(`IFCRELAGGREGATES('${key('agg-storey|' + f.id)}',$,$,$,${storey},${list(spaces)})`);
    if (contained.length) e(`IFCRELCONTAINEDINSPATIALSTRUCTURE('${key('contained|' + f.id)}',$,$,$,${list(contained)},${storey})`);
    e(`IFCRELAGGREGATES('${key('agg-bld|' + f.id)}',$,$,$,${bld},(${storey}))`);
  });

  const head = ['ISO-10303-21;', 'HEADER;', "FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');",
    `FILE_NAME(${stepString(meta.fileName)},${stepString(meta.timestamp)},(''),(''),'Casa3D','Casa3D','');`, "FILE_SCHEMA(('IFC4'));", 'ENDSEC;', 'DATA;'];
  return { text: [...head, ...lines, 'ENDSEC;', 'END-ISO-10303-21;', ''].join('\n'), counts, skipped };
}
