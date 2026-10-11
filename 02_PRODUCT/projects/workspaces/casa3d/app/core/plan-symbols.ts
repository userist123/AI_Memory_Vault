// Simbolurile de mobilier pentru plan, desenate ca într-un plan de arhitectură (pat cu perne, canapea cu spătar și brațe,
// masă cu scaune, dulap cu bară, bucătărie cu plită și chiuvetă, obiecte sanitare), nu dreptunghiuri cu nume de produs.
// Coordonate locale în metri, centrate în piesă: x pe lățime, y pe adâncime, fața piesei spre +y (rotația 0 privește spre +z).
export interface PlanSymbol { outline: string; detail: string; fill?: 'soft' | 'none' }

const rect = (x0: number, y0: number, x1: number, y1: number) => `M${x0} ${y0}H${x1}V${y1}H${x0}Z`;
const rrect = (x0: number, y0: number, x1: number, y1: number, r: number) => { const q = Math.min(r, (x1 - x0) / 2, (y1 - y0) / 2);
  return `M${x0 + q} ${y0}H${x1 - q}Q${x1} ${y0} ${x1} ${y0 + q}V${y1 - q}Q${x1} ${y1} ${x1 - q} ${y1}H${x0 + q}Q${x0} ${y1} ${x0} ${y1 - q}V${y0 + q}Q${x0} ${y0} ${x0 + q} ${y0}Z`; };
const line = (x0: number, y0: number, x1: number, y1: number) => `M${x0} ${y0}L${x1} ${y1}`;
const circle = (cx: number, cy: number, r: number) => `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`;
const ellipse = (cx: number, cy: number, rx: number, ry: number) => `M${cx - rx} ${cy}a${rx} ${ry} 0 1 0 ${2 * rx} 0a${rx} ${ry} 0 1 0 ${-2 * rx} 0`;
const r3 = (v: number) => Math.round(v * 1000) / 1000;
const fmt = (s: string) => s.replace(/-?\d+\.\d+/g, m => String(r3(Number(m))));

/** Simbolul piesei, după grupă și dimensiunile reale (m). */
export function planSymbol(group: string, w: number, d: number, chairs = 4): PlanSymbol {
  const X = w / 2, Y = d / 2, box = rect(-X, -Y, X, Y), parts: string[] = [];
  switch (group){
    case 'pat': { // tăblia la spate, perne, plapuma întoarsă
      const n = w > 1.2 ? 2 : 1, pw = (w - .16 - (n - 1) * .06) / n;
      for (let i = 0; i < n; i++){ const x0 = -X + .08 + i * (pw + .06); parts.push(rrect(x0, -Y + .1, x0 + pw, -Y + .38, .05)); }
      parts.push(line(-X, -Y + .06, X, -Y + .06), line(-X, -Y + .5, X, -Y + .5), line(X - .35, -Y + .5, X, -Y + .85));
      return { outline: fmt(rrect(-X, -Y, X, Y, .03)), detail: fmt(parts.join('')), fill: 'soft' }; }
    case 'canapea': case 'fotoliu': { // spătar, brațe, perne de șezut
      const back = Math.min(.22, d * .28), arm = Math.min(.18, w * .12), seats = group === 'fotoliu' ? 1 : Math.max(2, Math.round((w - 2 * arm) / .7));
      parts.push(rrect(-X, -Y, X, -Y + back, .04), rrect(-X, -Y + back, -X + arm, Y, .04), rrect(X - arm, -Y + back, X, Y, .04));
      const sw = (w - 2 * arm) / seats; for (let i = 1; i < seats; i++) parts.push(line(-X + arm + i * sw, -Y + back, -X + arm + i * sw, Y - .04));
      return { outline: fmt(rrect(-X, -Y, X, Y, .06)), detail: fmt(parts.join('')), fill: 'soft' }; }
    case 'masa': { // blatul și scaunele pe laturile lungi (în afara blatului)
      const per = Math.max(1, Math.round(chairs / 2)), long = w >= d, L = long ? w : d, cs = .42;
      for (let i = 0; i < per; i++){ const t = -L / 2 + L * (i + .5) / per;
        if (long){ parts.push(rrect(t - cs / 2, -Y - .3, t + cs / 2, -Y - .3 + cs, .06), rrect(t - cs / 2, Y + .3 - cs, t + cs / 2, Y + .3, .06)); }
        else { parts.push(rrect(-X - .3, t - cs / 2, -X - .3 + cs, t + cs / 2, .06), rrect(X + .3 - cs, t - cs / 2, X + .3, t + cs / 2, .06)); } }
      return { outline: fmt(rrect(-X, -Y, X, Y, .02)), detail: fmt(parts.join('')) }; }
    case 'masuta': return { outline: fmt(rrect(-X, -Y, X, Y, .03)), detail: fmt(rrect(-X + .05, -Y + .05, X - .05, Y - .05, .02)) };
    case 'dulap': { // dulap cu bară de umerașe (linie întreruptă pe axă) și uși
      const doors = Math.max(1, Math.round(w / .5)); for (let i = 1; i < doors; i++) parts.push(line(-X + i * w / doors, Y - .03, -X + i * w / doors, Y));
      parts.push(line(-X + .05, 0, X - .05, 0), line(-X, Y - .03, X, Y - .03));
      return { outline: fmt(box), detail: fmt(parts.join('')) }; }
    case 'bucatarie': { // blatul, plita (4 ochiuri) și chiuveta pe module de 60 cm, frontul
      const run = w >= d ? w : d, along = w >= d; parts.push(along ? line(-X, Y - .03, X, Y - .03) : line(X - .03, -Y, X - .03, Y));
      const hob = -run / 2 + Math.min(run - .6, run * .7), sink = -run / 2 + Math.max(.45, run * .25);
      for (const [dx, dy] of [[-.14, -.12], [.14, -.12], [-.14, .12], [.14, .12]] as const){ parts.push(along ? circle(hob + dx, dy - .02, .08) : circle(dy - .02, hob + dx, .08)); }
      parts.push(along ? rrect(sink - .22, -.18, sink + .22, .16, .05) : rrect(-.18, sink - .22, .16, sink + .22, .05));
      return { outline: fmt(box), detail: fmt(parts.join('')) }; }
    case 'frigider': return { outline: fmt(box), detail: fmt(line(-X, -Y, X, Y) + line(-X, Y - .04, X, Y - .04)) };
    case 'dus': { // cădiță cu pantă spre sifon
      parts.push(rect(-X + .04, -Y + .04, X - .04, Y - .04), line(-X + .04, -Y + .04, X - .04, Y - .04), line(X - .04, -Y + .04, -X + .04, Y - .04), circle(0, 0, .04));
      return { outline: fmt(box), detail: fmt(parts.join('')) }; }
    case 'lavoar': { parts.push(ellipse(0, Y * .15, X * .7, Y * .55), circle(0, -Y + .07, .02)); return { outline: fmt(rrect(-X, -Y, X, Y, .04)), detail: fmt(parts.join('')) }; }
    case 'wc': { const tank = Math.min(.18, d * .3); parts.push(rect(-X, -Y, X, -Y + tank), ellipse(0, -Y + tank + (d - tank) / 2, X * .82, (d - tank) / 2 - .01));
      return { outline: '', detail: fmt(parts.join('')) }; }
    case 'cada': { // cada: marginea și cuva rotunjită, sifonul la capăt
      parts.push(rrect(-X + .07, -Y + .07, X - .07, Y - .07, Math.min(.25, Y * .7)), circle(-X + .2, 0, .025));
      return { outline: fmt(rrect(-X, -Y, X, Y, .04)), detail: fmt(parts.join('')) }; }
    case 'bideu': { parts.push(ellipse(0, Y * .1, X * .8, Y * .8), circle(0, -Y + .08, .02)); return { outline: '', detail: fmt(parts.join('')) }; }
    case 'dulapOglinda': case 'boiler': // pe perete, deasupra altor piese: doar conturul punctat
      return { outline: '', detail: fmt(box), fill: 'none' };
    case 'caloriferBaie': return { outline: fmt(box), detail: '' };
    case 'birou': return { outline: fmt(box), detail: fmt(line(-X, Y - .03, X, Y - .03)) };
    case 'scaunBirou': { const r = Math.min(X, Y); parts.push(circle(0, .03, r * .78), `M${-r * .7} ${-r * .55}Q0 ${-r * 1.05} ${r * .7} ${-r * .55}`); return { outline: '', detail: fmt(parts.join('')) }; }
    case 'oglinda': return { outline: fmt(box), detail: '' };
    case 'biblioteca': { const sh = Math.max(1, Math.round(w / .4)); for (let i = 1; i < sh; i++) parts.push(line(-X + i * w / sh, -Y, -X + i * w / sh, Y));
      return { outline: fmt(box), detail: fmt(parts.join('')) }; }
    default: // corpuri: comodă, comodă TV, noptieră, pantofar — conturul și fața
      return { outline: fmt(box), detail: fmt(line(-X, Y - .03, X, Y - .03)) };
  }
}
/** Unghiul SVG (grade) pentru o rotație din plan: fața locală +y ajunge spre direcția din frontRect. */
export const symbolAngle = (rotation: number) => -rotation * 180 / Math.PI;
