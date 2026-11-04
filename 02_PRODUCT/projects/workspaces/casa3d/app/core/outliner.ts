// Arborele din outliner (A1-3): niveluri → camere / grupe → piese; structura cu golurile și nișele sub peretele lor.
import type { MirrorGroup, MirrorItem } from './plan-describe';

/** `label` = textul vizibil; `desc` = descrierea completă pentru cititorul de ecran și tooltip (lipsă = `label`). */
export type OutlinerNode = { key: string; label: string; desc?: string; level: number; item?: MirrorItem; children: OutlinerNode[] };
export type OutlinerRow = { node: OutlinerNode; depth: number; parent: string | null; index: number; setSize: number };

const HEADERS = new Set(['loose', 'structure', 'notes']);
const leaf = (i: number, item: MirrorItem): OutlinerNode => ({ key: `L${i}:${item.kind}:${item.id}`, label: item.label, level: i, item, children: [] });
const header = (i: number, g: MirrorGroup): OutlinerNode => ({ key: `L${i}:g:${g.id}`, label: g.title, level: i, children: [] });

export function buildOutliner(levels: readonly { name: string; groups: readonly MirrorGroup[] }[]): OutlinerNode[] {
  return levels.map((lv, i) => {
    const root: OutlinerNode = { key: `L${i}`, label: lv.name, level: i, children: [] };
    for (const g of lv.groups) {
      const first = g.items[0];
      if (!HEADERS.has(g.id) && first?.kind === 'room') {
        // camera e nodul grupei, cu restul pieselor drept copii
        const room = leaf(i, first); room.label = g.title; room.desc = first.label; room.children = g.items.slice(1).map(it => leaf(i, it)); root.children.push(room);
        continue;
      }
      const h = header(i, g); root.children.push(h);
      for (const it of g.items) {
        const wall = g.id === 'structure' && it.parent ? h.children.find(n => n.item?.kind === 'wall' && n.item.id === it.parent) : undefined;
        (wall ?? h).children.push(leaf(i, it));
      }
    }
    return root;
  });
}

/** Cheia nodului selectat și cheile strămoșilor (rădăcina întâi), căutate doar sub nivelul dat. */
export function findPath(nodes: readonly OutlinerNode[], level: number, sel: { kind: string; id: string } | null): { key: string; ancestors: string[] } | null {
  if (!sel) return null;
  const walk = (ns: readonly OutlinerNode[], up: string[]): { key: string; ancestors: string[] } | null => {
    for (const n of ns) {
      if (n.item?.kind === sel.kind && n.item.id === sel.id) return { key: n.key, ancestors: up };
      const r = walk(n.children, [...up, n.key]); if (r) return r;
    }
    return null;
  };
  const root = nodes.find(n => n.key === `L${level}`);
  return root ? walk(root.children, [root.key]) : null;
}

/** Rândurile vizibile, în adâncime; copiii apar doar sub nodurile deschise (aria-posinset / aria-setsize). */
export function visibleRows(nodes: readonly OutlinerNode[], expanded: ReadonlySet<string>): OutlinerRow[] {
  const out: OutlinerRow[] = [];
  const walk = (ns: readonly OutlinerNode[], depth: number, parent: string | null) => {
    ns.forEach((node, k) => {
      out.push({ node, depth, parent, index: k + 1, setSize: ns.length });
      if (expanded.has(node.key)) walk(node.children, depth + 1, node.key);
    });
  };
  walk(nodes, 0, null);
  return out;
}
