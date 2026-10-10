// Adaptoare Legacy → schema Fazei 1 (și înapoi, pentru teste de paritate).
const slug = s => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const BRANDS = ['Seldus', 'West', 'Kadda', 'Goodmob', 'Larisa', 'Savini', 'Grohe', 'Roca'];

export function planToFloor(PLAN){
  return { id: 'floor-1', name: 'Etaj', ceilingHeight: PLAN.inaltime,
    rooms: PLAN.camere.map(r => ({ id: r.id, name: r.nume, type: r.tip, rect: { x0: r.x0, z0: r.z0, x1: r.x1, z1: r.z1 } })),
    walls: PLAN.pereti.map((w, i) => ({ id: `wall-${i + 1}`, a: [...w.a], b: [...w.b], thickness: w.ext ? .25 : .15, exterior: !!w.ext,
      openings: w.goluri.map((g, j) => ({ id: `op-${i + 1}-${j + 1}`, kind: g.tip === 'usa' ? 'door' : 'window', offset: g.la, width: g.l, ...(g.intrare ? { entrance: true } : {}) })) })) };
}
export function floorToPlan(floor, name){
  return { nume: name, inaltime: floor.ceilingHeight,
    camere: floor.rooms.map(r => ({ id: r.id, nume: r.name, tip: r.type, ...r.rect })),
    pereti: floor.walls.map(w => ({ a: [...w.a], b: [...w.b], ...(w.exterior ? { ext: true } : {}), goluri: w.openings.map(o => ({ tip: o.kind === 'door' ? 'usa' : 'fereastra', la: o.offset, l: o.width, ...(o.entrance ? { intrare: true } : {}) })) })) };
}
export function optsToCatalog(OPTS, verifiedAt){
  const suppliers = [{ id: 'ikea-ro', name: 'IKEA', country: 'RO', website: 'https://www.ikea.com/ro/ro/' }, { id: 'dedeman', name: 'Dedeman', country: 'RO', website: 'https://www.dedeman.ro/' }];
  const products = [], variants = [], offers = [];
  for (const [group, g] of Object.entries(OPTS)){
    g.v.forEach((v, i) => {
      const brand = v.mag === 'IKEA' ? 'IKEA' : (BRANDS.find(b => v.nume.includes(b)) || 'UNKNOWN');
      const pid = `${group}-${slug(v.nume.split(',')[0])}`, vid = `${group}-${i}`;
      if (!products.find(p => p.id === pid)) products.push({ id: pid, group, name: v.nume.split(',')[0], brand, category: g.eticheta, model3d: g.model || 'none' });
      const productPage = /\/p\//.test(v.url);
      variants.push({ id: vid, productId: pid, name: v.nume, legacyIndex: i, dimensionsCm: v.w ? { w: v.w, d: v.d, h: v.h } : null,
        dimensionsConfidence: !v.w ? 'UNKNOWN' : v.aprox ? 'MEDIUM' : productPage ? 'HIGH' : 'MEDIUM', style: v.s || {}, ...(v.chairs ? { chairs: v.chairs } : {}), ...(g.inclus ? { includedWith: g.inclus } : {}) });
      offers.push({ id: `offer-${vid}`, variantId: vid, supplierId: v.mag === 'IKEA' ? 'ikea-ro' : 'dedeman', price: v.pret, currency: 'RON', availability: 'UNKNOWN', affiliateUrl: null,
        provenance: { source: v.mag, sourceUrl: v.url, verifiedAt, verificationType: productPage ? 'product_page' : 'category_listing', confidence: productPage ? 'HIGH' : 'MEDIUM' } });
    });
  }
  return { suppliers, products, variants, offers };
}
export const selToSelections = (SEL, OPTS) => Object.fromEntries(Object.keys(OPTS).map(k => [k, `${k}-${Math.min(SEL[k] || 0, OPTS[k].v.length - 1)}`]));
export const ovrToPlacements = (OVR, SEL) => Object.entries(OVR).map(([id, o]) => { const [group, roomId] = id.split('#'); return { id, roomId, productGroup: group, variantId: `${group}-${SEL[group] || 0}`, x: o.x, z: o.z, rotation: o.rot, source: 'manual' }; });
