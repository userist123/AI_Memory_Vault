'use client';
// Alegerea unui produs de finisaj ca dintr-un mostrar: mostra (culoarea și modelul materialului), numele, furnizorul și prețul.
// Închis arată alegerea curentă; deschis arată toate mostrele categoriei, cu căutare când sunt multe.
import { useId, useMemo, useState } from 'react';
import type { Material } from '@/core/types';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';

const BASE: Partial<Record<Material['category'], string>> = { parquet: '#c4a27a', floor_tile: '#d9d6cf', wall_tile: '#e6e2da', paint: '#efece6', baseboard: '#f3f1ec', wallpaper: '#ddd8cf',
  wall_panel: '#e9e6df', decorative_plaster: '#d8d2c8', brick_cladding: '#9a6a52', stone_cladding: '#cfc8bb', cornice: '#f3f1ec', curtain: '#cdbfa8', sheer: '#f4f2ec', blind: '#efede7', rug: '#c8b79a',
  door: '#efece6', countertop: '#d8d2c8', moulding: '#f3f1ec' };
const line = (a: number) => `rgba(40,30,20,${a})`;
/** Mostra: culoarea produsului și un desen al materialului (lamele, plăci, cărămidă, țesătură), doar din CSS. */
export function swatchStyle(m: Material | undefined): React.CSSProperties {
  if (!m) return { background: 'repeating-linear-gradient(45deg,#f1eee8 0 6px,#e8e4dc 6px 7px)' };
  const c = m.specs?.color ?? BASE[m.category] ?? '#e2ddd4', k = m.category, sz = m.specs?.sizeCm;
  if (k === 'parquet') return { background: `repeating-linear-gradient(0deg, ${line(.18)} 0 1px, transparent 1px 8px), repeating-linear-gradient(90deg, ${line(.05)} 0 1px, transparent 1px 7px), ${c}` };
  if (k === 'floor_tile' || k === 'wall_tile'){ const r = sz ? Math.max(sz[0], sz[1]) / Math.min(sz[0], sz[1]) : 1;
    return { backgroundColor: c, backgroundImage: `linear-gradient(${line(.2)} 1px, transparent 1px), linear-gradient(90deg, ${line(.2)} 1px, transparent 1px)`, backgroundSize: r >= 2 ? '50% 25%' : '50% 50%' }; }
  if (k === 'brick_cladding') return { background: `repeating-linear-gradient(0deg, #d9d2c6 0 2px, transparent 2px 9px), ${c}` };
  if (k === 'stone_cladding') return { background: `radial-gradient(circle at 30% 30%, ${line(.08)} 0 18%, transparent 19%), radial-gradient(circle at 72% 64%, ${line(.1)} 0 22%, transparent 23%), ${c}` };
  if (k === 'wallpaper') return { background: `linear-gradient(115deg, transparent 40%, ${line(.12)} 41% 42%, transparent 43%), linear-gradient(70deg, transparent 62%, ${line(.08)} 63% 64%, transparent 65%), ${c}` };
  if (k === 'wall_panel') return { background: `repeating-linear-gradient(90deg, ${line(.16)} 0 1px, transparent 1px 6px), ${c}` };
  if (k === 'decorative_plaster') return { background: `radial-gradient(${line(.07)} 1px, transparent 1.5px) 0 0/4px 4px, ${c}` };
  if (k === 'curtain' || k === 'sheer' || k === 'rug') return { background: `repeating-linear-gradient(90deg, ${line(.06)} 0 1px, transparent 1px 3px), repeating-linear-gradient(0deg, ${line(.05)} 0 1px, transparent 1px 3px), ${c}` };
  if (k === 'blind') return { background: `repeating-linear-gradient(0deg, ${line(.1)} 0 1px, transparent 1px 5px), ${c}` };
  return { background: c };
}

export default function MaterialPicker({ value, options, onChange, none, cur, label }: { value: string | null | undefined; options: Material[]; onChange(v: string): void; none?: string; cur: string; label?: string }){
  const { t, lang } = usePrefs(), [open, setOpen] = useState(false), [q, setQ] = useState(''), lid = useId();
  const sel = options.find(m => m.id === value), unit = (u: string) => u === 'm2' ? 'm²' : u === 'buc' ? t('fin.unit.pc') : u;
  const shown = useMemo(() => { const s = q.trim().toLowerCase(); return s ? options.filter(m => `${m.name} ${m.supplier}`.toLowerCase().includes(s)) : options; }, [q, options]);
  const pick = (v: string) => { onChange(v); setOpen(false); setQ(''); };
  const title = (m: Material) => m.name.split(',')[0];
  return (<div className={`mpick ${open ? 'open' : ''}`}>
    <button type="button" className="mpick-cur" aria-expanded={open} aria-controls={lid} aria-label={label} onClick={() => setOpen(o => !o)}>
      <i className="sw" style={swatchStyle(sel)} />
      <span><b>{sel ? title(sel) : none ?? '—'}</b>{sel && <small>{sel.supplier} · {formatMoney(sel.unitPrice, cur, lang)} / {unit(sel.unit)}</small>}</span>
    </button>
    {open && <div className="mpick-list" id={lid}>
      {options.length > 8 && <input type="text" value={q} placeholder={t('mpick.search')} onChange={e => setQ(e.target.value)} autoFocus />}
      <div className="mpick-grid" role="listbox" aria-label={label}>
        {none != null && <button type="button" role="option" aria-selected={!value} className="mpick-tile" onClick={() => pick('')}><i className="sw" style={swatchStyle(undefined)} /><b>{none}</b></button>}
        {shown.map(m => <button key={m.id} type="button" role="option" aria-selected={m.id === value} className="mpick-tile" title={m.name} onClick={() => pick(m.id)}>
          <i className="sw" style={swatchStyle(m)} /><b>{title(m)}</b><small>{m.supplier} · {formatMoney(m.unitPrice, cur, lang)}/{unit(m.unit)}</small></button>)}
        {shown.length === 0 && <p className="muted">{t('mpick.none')}</p>}
      </div>
    </div>}
  </div>);
}
