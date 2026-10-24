// Ordinea câmpurilor din inspector e înghețată între versiuni (PLAN_A1_3; cercetarea UI3: „nu reordona câmpurile
// frecvente”). Fiecare bloc de inspector din components/Editor.tsx începe cu un marcaj `inspector:<tip>`; testul ia, în
// ordinea sursei, cheile t('…') ale blocului. O reordonare pică testul: schimbarea snapshotului e o decizie explicită.
import { describe, expect, test } from 'vitest';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { DICTS } from '@/lib/i18n';

const SRC = readFileSync(path.join(import.meta.dirname, '../components/Editor.tsx'), 'utf8');
const MARK = /(?:\/\/|\{\/\*) inspector:([a-z_]+)/g;
function blocks(): Record<string, string[]> {
  const marks = [...SRC.matchAll(MARK)], out: Record<string, string[]> = {};
  marks.forEach((m, i) => { if (m[1] === 'end') return; const body = SRC.slice(m.index! + m[0].length, marks[i + 1]?.index ?? SRC.length);
    // t('cheie'), t(`cheie.${x}`) — partea variabilă rămâne ca ${…} — și ternarele, oricât de imbricate:
    // t(a ? 'k.a' : b ? 'k.b' : 'k.c') → toate cheile cu punct din expresie (literalii fără punct sunt valori, nu chei)
    out[m[1]!] = [...body.matchAll(/\bt\((?:'([^']+)'|`([^`]+)`|([^`()]*\?[^`()]*))/g)]
      .flatMap(x => x[3] != null ? [...x[3].matchAll(/'([a-zA-Z][\w]*\.[\w.]+)'/g)].map(y => y[1]!) : [x[1] ?? x[2]!]); });
  return out;
}

describe('ordinea câmpurilor din inspector', () => {
  test('fiecare tip de selecție are blocul lui, o singură dată', () => {
    const kinds = [...SRC.matchAll(MARK)].map(m => m[1]).filter(k => k !== 'end');
    expect(new Set(kinds).size).toBe(kinds.length);
    expect([...kinds].sort()).toEqual(['annotation', 'beam', 'ceiling_zone', 'column', 'floor_zone', 'glass_wall', 'niche', 'opening', 'placement', 'railing', 'room', 'stair', 'wall', 'wall_item']);
  });
  test('cheile fixe există în ro și en', () => {
    for (const [k, keys] of Object.entries(blocks())) for (const key of keys) if (!key.includes('${'))
      for (const lang of ['ro', 'en'] as const) expect(key in DICTS[lang], `${k}: ${lang} ${key}`).toBe(true);
  });
  test('snapshotul ordinii', () => {
    expect(blocks()).toMatchInlineSnapshot(`
      {
        "annotation": [
          "tool.dim",
          "tool.text",
          "dim.offset",
          "dim.mode",
          "dim.aligned",
          "dim.ortho",
          "dim.flip",
          "text.content",
          "text.size",
          "text.sizeS",
          "text.sizeM",
          "text.sizeL",
          "text.rotation",
          "common.delete",
        ],
        "beam": [
          "tool.beam",
          "editor.width",
          "beam.depth",
          "common.delete",
        ],
        "ceiling_zone": [
          "tool.ceiling_zone",
          "czone.info",
          "editor.width",
          "editor.depth",
          "czone.drop",
          "czone.cove",
          "czone.led",
          "fin.none",
          "zone.label",
          "czone.qty",
          "czone.rule",
          "common.delete",
        ],
        "column": [
          "tool.shaft",
          "column.boxing",
          "tool.column",
          "column.role",
          "column.structural",
          "tool.shaft",
          "column.boxing",
          "column.height",
          "column.toCeiling",
          "editor.width",
          "editor.depth",
          "column.shape",
          "column.rect",
          "column.round",
          "editor.rotate",
          "common.delete",
        ],
        "floor_zone": [
          "tool.floor_zone",
          "zone.info",
          "zone.material",
          "editor.width",
          "editor.depth",
          "zone.label",
          "zone.rule",
          "common.delete",
        ],
        "glass_wall": [
          "tool.glass_wall",
          "editor.thickness",
          "glassWall.mullions",
          "common.delete",
        ],
        "niche": [
          "tool.niche",
          "editor.width",
          "niche.height",
          "editor.depth",
          "niche.elevation",
          "editor.offsetOnWall",
          "niche.finish",
          "niche.finishAuto",
          "niche.tile",
          "niche.paint",
          "niche.led",
          "niche.flip",
          "common.delete",
        ],
        "opening": [
          "tool.passage",
          "tool.door",
          "tool.window",
          "editor.width",
          "editor.offsetOnWall",
          "door.passage",
          "door.type",
          "door.type.\${k}",
          "door.flush",
          "door.typeHint.\${type}",
          "door.flipSlide",
          "door.flipFold",
          "door.flipHinge",
          "door.flipFace",
          "door.flipSwing",
          "door.slideAuto",
          "door.swingAuto",
          "door.swingSet",
          "editor.deleteOpening",
        ],
        "placement": [
          "common.unknownPrice",
          "editor.provenance",
          "common.unknown",
          "editor.priceRecheck",
          "editor.positionValid",
          "editor.xField",
          "editor.zField",
          "editor.rotate",
          "editor.duplicate",
          "editor.viewProduct",
          "editor.affiliateTag",
          "common.delete",
          "editor.variantsOnly",
          "common.unknownDims",
          "common.approx",
        ],
        "railing": [
          "tool.railing",
          "railing.height",
          "railing.kind",
          "railing.kind.\${k}",
          "railing.rule",
          "common.delete",
        ],
        "room": [
          "editor.name",
          "editor.type",
          "space.label",
          "space.\${k}",
          "editor.width",
          "editor.length",
          "editor.posX",
          "editor.posZ",
          "editor.splitX",
          "editor.splitZ",
          "editor.mergeWith",
          "editor.roomWallsShort",
          "editor.autoRoom",
          "editor.addFurniture",
          "editor.addGroup",
          "editor.groupsFor",
          "roomType.\${room.type}",
          "editor.groupsOther",
          "editor.groupsAll",
          "editor.addVariant",
          "editor.addToRoom",
        ],
        "stair": [
          "stair.title",
          "stair.info",
          "stair.width",
          "stair.length",
          "stair.rotate",
          "stair.delete",
        ],
        "wall": [
          "editor.wall",
          "editor.length",
          "editor.thickness",
          "editor.exteriorWall",
          "wallStatus.label",
          "wallStatus.\${k}",
          "wallStatus.newNote",
          "wallStatus.demolishNote",
          "editor.deleteWall",
        ],
        "wall_item": [
          "wallItem.kind.\${it.kind}",
          "wallItem.kind",
          "wallItem.kind.\${k}",
          "wallItem.label",
          "editor.width",
          "niche.height",
          "wallItem.offset",
          "wallItem.elevation",
          "editor.depth",
          "wallItem.canvas",
          "fin.color",
          "fin.product",
          "wallItem.noProduct",
          "wallItem.rule",
          "wallItem.centerWall",
          "wallItem.centerAbove",
          "niche.flip",
          "common.delete",
        ],
      }
    `);
  });
});
