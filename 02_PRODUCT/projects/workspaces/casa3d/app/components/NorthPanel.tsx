'use client';
// Orientarea casei în Releveu: utilizatorul declară unde e nordul real față de sus-ul planului (busolă cu clic/tragere sau
// grade tastate). Fără declarație nu există nord (nici pe planșe, nici pentru soarele din 3D) — nu se presupune nimic.
import { useRef } from 'react';
import { parseNorth, planUpFacing, type SiteOrientation } from '@/core/north';
import { usePrefs } from '@/lib/prefs';

export default function NorthPanel({ site, onChange }: { site: SiteOrientation | undefined; onChange(s: SiteOrientation | null): void }){
  const { t, lang } = usePrefs(), dial = useRef<SVGSVGElement>(null);
  const setFromPointer = (e: React.PointerEvent) => { const r = dial.current!.getBoundingClientRect(), x = e.clientX - (r.left + r.width / 2), y = e.clientY - (r.top + r.height / 2);
    if (Math.hypot(x, y) < 4) return; const deg = Math.atan2(x, -y) * 180 / Math.PI, snapped = e.shiftKey ? deg : Math.round(deg / 5) * 5; onChange(parseNorth({ northDeg: snapped })); };
  const drag = (e: React.PointerEvent) => { e.preventDefault(); (e.currentTarget as Element).setPointerCapture?.(e.pointerId); setFromPointer(e); };
  const deg = site?.northDeg;
  return (<section className="north-panel" aria-label={t('north.title')}>
    <h3>{t('north.title')}</h3>
    <div className="north-row">
      <svg ref={dial} className="north-dial" viewBox="-32 -32 64 64" width={72} height={72} role="slider" tabIndex={0} aria-label={t('north.dialAria')}
        aria-valuemin={0} aria-valuemax={359.9} aria-valuenow={deg ?? undefined} aria-valuetext={deg == null ? t('north.unset') : t('north.value', { deg, up: planUpFacing(site!, lang === 'en' ? 'en' : 'ro') })}
        onPointerDown={drag} onPointerMove={e => { if (e.buttons & 1) setFromPointer(e); }}
        onKeyDown={e => { const step = e.shiftKey ? 1 : 5; if (e.key === 'ArrowRight' || e.key === 'ArrowUp'){ e.preventDefault(); onChange(parseNorth({ northDeg: (deg ?? 0) + step })); } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown'){ e.preventDefault(); onChange(parseNorth({ northDeg: (deg ?? 0) - step })); } }}>
        <circle r={29} fill="var(--surface-2, #fff)" stroke="var(--line-strong, #999)" strokeWidth={1.2} />
        {[0, 90, 180, 270].map(a => <line key={a} x1={0} y1={-29} x2={0} y2={-24} stroke="var(--ink-3, #777)" strokeWidth={1.2} transform={`rotate(${a})`} />)}
        <text y={-15} fontSize={7} textAnchor="middle" fill="var(--ink-3, #777)">{t('north.planUp')}</text>
        {deg != null ? <g transform={`rotate(${deg})`}><path d="M0 -26 L6 6 L0 2 Z" fill="var(--accent, #c63c29)" /><path d="M0 -26 L-6 6 L0 2 Z" fill="none" stroke="var(--accent, #c63c29)" strokeWidth={1.2} />
          <text y={-12} fontSize={8} fontWeight={700} textAnchor="middle" fill="var(--ink, #222)" transform="rotate(0)">N</text></g>
          : <text y={4} fontSize={8} textAnchor="middle" fill="var(--ink-3, #777)">?</text>}
      </svg>
      <div className="north-fields">
        <label>{t('north.degrees')}<input type="number" min={0} max={359.9} step={1} value={deg ?? ''} placeholder={t('north.unsetShort')}
          onChange={e => { const n = parseNorth({ northDeg: e.target.valueAsNumber }); if (n) onChange(n); }} /></label>
        <p className="muted small">{deg == null ? t('north.hintUnset') : t('north.value', { deg, up: planUpFacing(site!, lang === 'en' ? 'en' : 'ro') })}</p>
        {deg != null && <button type="button" className="btn sm" onClick={() => onChange(null)}>{t('north.clear')}</button>}
      </div>
    </div>
  </section>);
}
