'use client';
// Bucătăria pe module: corpurile de jos în ordine (tip + lățime), desenul frontal la scară, verificările NKBA și blatul
// (bucăți, îmbinări, decupaje pentru chiuvetă și plită).
import { MODULE_TYPES, DEFAULT_W, defaultModules, moduleIssues, worktop, type KitchenModule, type ModuleType } from '@/core/kitchen-modules';
import { usePrefs } from '@/lib/prefs';

const COLOR: Record<ModuleType, string> = { base: '#efe9df', drawers: '#e6dccb', sink: '#cfe0ea', dishwasher: '#dfe3e6', hob: '#3a3a3a', oven: '#5a5a5a', tall: '#d9cdb8', fridge: '#e9eef0', corner: '#e9e2d5', filler: '#f6f3ee' };
export default function KitchenModules({ runCm, modules, onChange }: { runCm: number; modules: KitchenModule[] | undefined; onChange(m: KitchenModule[] | undefined): void }){
  const { t } = usePrefs();
  if (!modules) return <div className="kmods"><button className="btn" onClick={() => onChange(defaultModules(runCm))}>{t('kmod.enable')}</button><small className="muted">{t('kmod.enableSub')}</small></div>;
  const iss = moduleIssues(modules, runCm), top = worktop(modules), sum = modules.reduce((a, m) => a + m.w, 0), W = Math.max(runCm, sum), H = 90;
  const set = (i: number, patch: Partial<KitchenModule>) => onChange(modules.map((m, k) => k === i ? { ...m, ...patch } : m));
  const move = (i: number, d: -1 | 1) => { const j = i + d; if (j < 0 || j >= modules.length) return; const n = [...modules]; [n[i], n[j]] = [n[j]!, n[i]!]; onChange(n); };
  let x = 0;
  return (<div className="kmods"><h4>{t('kmod.title')} <small className="muted">{t('kmod.sum', { sum: Math.round(sum * 10) / 10, run: Math.round(runCm * 10) / 10 })}</small></h4>
    <svg className="kmod-elev" viewBox={`-2 -40 ${W + 4} ${H + 46}`} role="img" aria-label={t('kmod.elevation')}>
      <rect x={0} y={-36} width={runCm} height={2} fill="#8B6A45" />
      {modules.map((m, i) => { const a = x; x += m.w; const tall = m.type === 'tall' || m.type === 'fridge';
        return <g key={i}><rect x={a} y={tall ? -34 : 0} width={m.w} height={tall ? H + 34 : H} fill={COLOR[m.type]} stroke="#23262B" strokeWidth={.6} />
          {m.w >= 25 && <text x={a + m.w / 2} y={tall ? 10 : 40} fontSize={9} textAnchor="middle" fill={m.type === 'hob' || m.type === 'oven' ? '#fff' : '#23262B'}>{t(`kmod.t.${m.type}`)}</text>}
          {m.w >= 25 && <text x={a + m.w / 2} y={H - 6} fontSize={8} textAnchor="middle" fill="#5E636B">{m.w}</text>}</g>; })}
      {top.map((p, i) => <g key={'w' + i}><rect x={p.from} y={-4} width={p.length} height={4} fill="#b9a27f" stroke="#23262B" strokeWidth={.4} />
        {p.cutouts.map((c, k) => <rect key={k} x={p.from + c.at - c.w / 2} y={-4} width={c.w} height={4} fill="#fff" opacity={.7} />)}</g>)}
    </svg>
    {iss.map((i, k) => <div key={k} className="issue WARNING">{t(i.key, i.vars)}</div>)}
    <ol className="kmod-list">{modules.map((m, i) => <li key={i}>
      <select value={m.type} onChange={e => set(i, { type: e.target.value as ModuleType, w: DEFAULT_W[e.target.value as ModuleType] })} aria-label={t('kmod.type')}>{MODULE_TYPES.map(ty => <option key={ty} value={ty}>{t(`kmod.t.${ty}`)}</option>)}</select>
      <input type="number" min={3} max={150} step={1} value={m.w} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && v >= 3 && v <= 150) set(i, { w: Math.round(v * 10) / 10 }); }} aria-label={t('kmod.width')} />
      <button className="btn-x" onClick={() => move(i, -1)} aria-label={t('kmod.left')}>‹</button><button className="btn-x" onClick={() => move(i, 1)} aria-label={t('kmod.right')}>›</button>
      <button className="btn-x" onClick={() => onChange(modules.filter((_, k) => k !== i))} aria-label={t('kmod.remove')}>×</button></li>)}</ol>
    <div className="kmod-act"><button className="btn" onClick={() => onChange([...modules, { type: 'base', w: 60 }])} disabled={modules.length >= 30}>{t('kmod.add')}</button>
      <button className="btn" onClick={() => onChange(defaultModules(runCm))}>{t('kmod.reset')}</button><button className="btn" onClick={() => onChange(undefined)}>{t('kmod.disable')}</button></div>
    {top.length > 0 && <div className="kmod-top"><b>{t('kmod.worktop')}</b><ul>{top.map((p, i) => <li key={i}>{t('kmod.piece', { n: i + 1, len: p.length, stock: p.stock })}{p.cutouts.length ? ' · ' + p.cutouts.map(c => t(`kmod.cut.${c.type}`, { at: c.at, w: c.w })).join(', ') : ''}</li>)}</ul>
      {top.length > 1 && <small className="muted">{t('kmod.joints')}</small>}</div>}
  </div>);
}
