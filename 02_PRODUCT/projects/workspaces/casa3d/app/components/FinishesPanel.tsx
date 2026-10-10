'use client';
// Finisajele unei camere, ca la un designer: pardoseala și modul ei de așezare, pereții (vopsea, faianță, placări pe fiecare latură),
// tavanul (drept, fals, scafă luminoasă, cornișă, spoturi), plinta și iluminatul — cu costul camerei și avertismentele tehnice.
import { useMemo } from 'react';
import type { Snapshot, MaterialsCatalog, RoomFinishes, Room, Material, WallFeature, WallFeatureKind, FloorPattern, Catalog, WindowTreatment } from '@/core/types';
import { finishesOf, roomGeometry, computeBOQ } from '@/core/boq';
import { floorOfRoom } from '@/core/levels';
import { defaultLayout, PATTERNS, SIDES, FEATURE_CATEGORY, layoutOf, materialOf, sideGeometry, ceilingOf, clearHeight, finishIssues, pieceSizeCm, FINISH_RULES } from '@/core/finishes';
import { formatMoney, formatArea, formatLength } from '@/core/format';
import { normalizeHex } from '@/core/appearance';
import { lightReport } from '@/core/light-design';
import { roomWindows, curtainPlan, blindPlan, TEXTILE_RULES } from '@/core/textiles';
import { usePrefs } from '@/lib/prefs';

const KINDS = Object.keys(FEATURE_CATEGORY) as WallFeatureKind[];

export default function FinishesPanel({ room, snap, cat, mc, cur, onFinish }: { room: Room; snap: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; onFinish(p: Partial<RoomFinishes>): void }){
  const { t, lang, units } = usePrefs();
  const f = finishesOf(snap, room), g = roomGeometry(snap, room), fl = floorOfRoom(snap, room.id), wet = room.type === 'baie' || room.type === 'bucatarie';
  const by = (...c: Material['category'][]) => mc.materials.filter(m => c.includes(m.category));
  const unitLabel = (u: string) => u === 'm2' ? 'm²' : u === 'buc' ? t('fin.unit.pc') : u;
  const opt = (m: Material) => <option key={m.id} value={m.id}>{m.name} · {formatMoney(m.unitPrice, cur, lang)}/{unitLabel(m.unit)}</option>;
  const fm = materialOf(mc, f.floor), lay = layoutOf(f, fm), tile = fm?.category === 'floor_tile', c = ceilingOf(f);
  const setLayout = (p: Partial<typeof lay>) => onFinish({ floorLayout: { ...lay, ...p } });
  const features = f.wallFeatures || [];
  const setFeature = (i: number, p: Partial<WallFeature>) => onFinish({ wallFeatures: features.map((w, k) => k === i ? { ...w, ...p } : w) });
  const firstOf = (kind: WallFeatureKind) => by(FEATURE_CATEGORY[kind])[0]?.id || '';
  const freeSide = SIDES.find(s => !features.some(w => w.side === s));
  const setCeiling = (p: Partial<NonNullable<RoomFinishes['ceiling']>>) => onFinish({ ceiling: { type: c.type, ...(f.ceiling || {}), ...p } });
  // costul finisajelor acestei camere, din același BOQ ca bugetul (materiale + manoperă estimată)
  const cost = useMemo(() => { const b = computeBOQ(snap, cat, mc), items = b.items.filter(i => i.roomId === room.id && (i.category === 'finishes' || i.category === 'lighting' || i.category === 'textiles'));
    return { mat: items.reduce((a, i) => a + (i.total ?? 0), 0), lab: b.labor.filter(l => l.roomId === room.id).reduce((a, l) => a + l.expected, 0) }; }, [snap, cat, mc, room.id]);
  const issues = finishIssues(snap, mc, fl, room, f), light = lightReport(mc, fl, room, f), wins = roomWindows(fl, room);
  const treat = (id: string) => (f.windows || []).find(t => t.openingId === id) ?? { openingId: id };
  const setTreat = (id: string, p: Partial<WindowTreatment>) => onFinish({ windows: [...(f.windows || []).filter(t => t.openingId !== id), { ...treat(id), ...p }] });
  const sideLen = (s: WallFeature['side']) => formatLength(sideGeometry(fl, room, s).lengthM, units, lang);
  const patternOk = (p: FloorPattern) => tile ? p !== 'herringbone' && p !== 'chevron' || pieceSizeCm(fm)[0] >= 2 * pieceSizeCm(fm)[1] : true;

  return (<div className="finishes">
    <h4>{t('editor.finishes')}</h4>
    <div className="prov">{t('editor.finishSummary', { floor: formatArea(g.floorArea, units, lang), walls: formatArea(g.wallNet, units, lang), ceiling: formatArea(g.ceiling, units, lang), perimeter: formatLength(g.perimeter, units, lang) })}</div>
    <div className="prov" role="status">{t('fin.roomCost', { mat: formatMoney(cost.mat, cur, lang), lab: formatMoney(cost.lab, cur, lang) })}</div>
    {issues.length > 0 && <div>{issues.map((i, k) => <div key={k} className="issue WARNING">{t(i.key, { ...i.vars, ...(i.vars?.pattern ? { pattern: t(`fin.pattern.${i.vars.pattern}`) } : {}), ...(i.vars?.side && i.vars.side !== '-' ? { side: t(`fin.side.${i.vars.side}`) } : {}) })}</div>)}</div>}

    <fieldset className="fin-group"><legend>{t('fin.floor')}</legend>
      <label className="f"><span>{t('editor.floorFinish')}</span><select value={f.floor} onChange={e => onFinish({ floor: e.target.value, floorLayout: defaultLayout(materialOf(mc, e.target.value)) })}>
        <optgroup label={t('fin.parquet')}>{by('parquet').map(opt)}</optgroup><optgroup label={t('fin.tiles')}>{by('floor_tile').map(opt)}</optgroup></select></label>
      <div className="grid2">
        <label className="f"><span>{t('fin.pattern')}</span><select value={lay.pattern} onChange={e => setLayout({ pattern: e.target.value as FloorPattern })}>
          {PATTERNS.filter(patternOk).map(p => <option key={p} value={p}>{t(`fin.pattern.${p}`)}</option>)}</select></label>
        <label className="f"><span>{t('fin.direction')}</span><select value={lay.angle ?? 0} onChange={e => setLayout({ angle: Number(e.target.value) === 90 ? 90 : 0 })}>
          <option value={0}>{t('fin.dirAlongX')}</option><option value={90}>{t('fin.dirAlongZ')}</option></select></label>
      </div>
      {tile && <div className="grid2">
        <label className="f"><span>{t('fin.grout')}</span><input type="number" min={0} max={20} step={.5} value={lay.groutMm ?? ''} onChange={e => setLayout({ groutMm: Math.max(0, Math.min(20, Number(e.target.value) || 0)) })} /></label>
        <label className="f"><span>{t('fin.groutColor')}</span><input type="color" value={lay.groutColor || '#bdb8ae'} onChange={e => { const h = normalizeHex(e.target.value); if (h) setLayout({ groutColor: h }); }} /></label>
      </div>}
      {fm && <div className="prov">{t(fm.specs?.sizeCm ? 'fin.piece' : 'fin.pieceTypical', { l: pieceSizeCm(fm)[0], w: pieceSizeCm(fm)[1] })}{fm.specs?.slip ? ` · ${fm.specs.slip}` : ''}{fm.specs?.rectified ? ` · ${t('fin.rectified')}` : ''}</div>}
    </fieldset>

    <fieldset className="fin-group"><legend>{t('fin.walls')}</legend>
      <label className="f"><span>{t('editor.wallPaint')}</span><select value={f.wallPaint} onChange={e => onFinish({ wallPaint: e.target.value })}>{by('paint').map(opt)}</select></label>
      {wet && <label className="f"><span>{t('editor.wallTile')}</span><select value={f.wallTile || ''} onChange={e => onFinish({ wallTile: e.target.value || null })}><option value="">{t('editor.noTile')}</option>{by('wall_tile').map(opt)}</select></label>}
      {features.map((w, i) => <div key={i} className="fin-row">
        <div className="grid2">
          <label className="f"><span>{t('fin.side')}</span><select value={w.side} onChange={e => setFeature(i, { side: e.target.value as WallFeature['side'] })}>
            {SIDES.map(s => <option key={s} value={s} disabled={s !== w.side && features.some(x => x.side === s)}>{t(`fin.side.${s}`)} · {sideLen(s)}</option>)}</select></label>
          <label className="f"><span>{t('fin.kind')}</span><select value={w.kind} onChange={e => { const k = e.target.value as WallFeatureKind; setFeature(i, { kind: k, material: firstOf(k) }); }}>
            {KINDS.filter(k => by(FEATURE_CATEGORY[k]).length).map(k => <option key={k} value={k}>{t(`fin.kind.${k}`)}</option>)}</select></label>
        </div>
        <label className="f"><span>{t('fin.product')}</span><select value={w.material} onChange={e => setFeature(i, { material: e.target.value })}>{by(FEATURE_CATEGORY[w.kind]).map(opt)}</select></label>
        <div className="grid2">
          <label className="f"><span>{t('fin.height')}</span><input type="number" min={.3} max={fl.ceilingHeight} step={.05} value={w.heightM ?? ''} placeholder={t('fin.fullHeight')}
            onChange={e => setFeature(i, { heightM: e.target.value === '' ? undefined : Math.max(.3, Math.min(fl.ceilingHeight, Number(e.target.value) || fl.ceilingHeight)) })} /></label>
          <button className="btn" style={{ alignSelf: 'end' }} onClick={() => onFinish({ wallFeatures: features.filter((_, k) => k !== i) })}>{t('fin.remove')}</button>
        </div>
      </div>)}
      {freeSide && <button className="btn" onClick={() => onFinish({ wallFeatures: [...features, { side: freeSide, kind: 'wallpaper', material: firstOf('wallpaper') }] })}>{t('fin.addFeature')}</button>}
      {room.type !== 'baie' && room.type !== 'bucatarie' && <label className="f"><span>{t('editor.baseboard')}</span><select value={f.baseboard || ''} onChange={e => onFinish({ baseboard: e.target.value || null })}><option value="">{t('editor.noBaseboard')}</option>{by('baseboard').map(opt)}</select></label>}
    </fieldset>

    <fieldset className="fin-group"><legend>{t('fin.ceiling')}</legend>
      <label className="f"><span>{t('fin.ceilingType')}</span><select value={c.type} onChange={e => setCeiling({ type: e.target.value as 'flat' | 'drop' | 'cove' })}>
        {(['flat', 'drop', 'cove'] as const).map(k => <option key={k} value={k}>{t(`fin.ceiling.${k}`)}</option>)}</select></label>
      {c.type !== 'flat' && <div className="grid2">
        <label className="f"><span>{t('fin.drop')}</span><input type="number" min={5} max={FINISH_RULES.maxDropCm} value={c.dropCm} onChange={e => setCeiling({ dropCm: Math.max(5, Math.min(FINISH_RULES.maxDropCm, Number(e.target.value) || 10)) })} /></label>
        {c.type === 'cove' && <label className="f"><span>{t('fin.cove')}</span><input type="number" min={10} max={FINISH_RULES.maxCoveCm} value={c.coveCm} onChange={e => setCeiling({ coveCm: Math.max(10, Math.min(FINISH_RULES.maxCoveCm, Number(e.target.value) || 25)) })} /></label>}
      </div>}
      {c.type !== 'flat' && <div className="prov">{t('fin.clearHeight', { h: formatLength(clearHeight(fl, f), units, lang) })}</div>}
      {c.type === 'cove' && <label className="f"><span>{t('fin.led')}</span><select value={c.led || ''} onChange={e => setCeiling({ led: e.target.value || null })}><option value="">{t('fin.none')}</option>{by('led_strip').map(opt)}</select></label>}
      <label className="f"><span>{t('fin.cornice')}</span><select value={c.cornice || ''} onChange={e => setCeiling({ cornice: e.target.value || null })}><option value="">{t('fin.none')}</option>{by('cornice').map(opt)}</select></label>
      <div className="grid2">
        <label className="f"><span>{t('fin.spots')}</span><select value={c.spot || ''} onChange={e => setCeiling({ spot: e.target.value || null, spots: f.ceiling?.spots ?? 4 })}><option value="">{t('fin.none')}</option>{by('spot').map(opt)}</select></label>
        {c.spot && <label className="f"><span>{t('fin.spotCount')}</span><input type="number" min={0} max={40} value={c.spots} onChange={e => setCeiling({ spots: Math.max(0, Math.min(40, Math.round(Number(e.target.value) || 0))) })} /></label>}
      </div>
    </fieldset>

    <fieldset className="fin-group"><legend>{t('fin.lighting')}</legend>
      <div className="prov" role="status">{t(light.unknownLumens ? 'light.estimateUnknown' : 'light.estimate', { lux: light.lux, lm: light.lumens })}{light.target ? ` · ${t('light.target', { min: light.target[0], max: light.target[1] })}` : ''}{light.ccts.length ? ` · ${light.ccts.join(' / ')} K` : ''}</div>
      <div className="grid2">
        <label className="f"><span>{t('editor.light')}</span><select value={f.light} onChange={e => onFinish({ light: e.target.value })}>{by('lighting').map(opt)}</select></label>
        <label className="f"><span>{t('editor.lightCount')}</span><input type="number" min={0} max={20} value={f.lights ?? ''} placeholder={t('common.auto')} onChange={e => onFinish({ lights: e.target.value === '' ? undefined : Math.max(0, Math.min(20, Number(e.target.value) || 0)) })} /></label>
      </div>
    </fieldset>
    <fieldset className="fin-group"><legend>{t('tex.title')}</legend>
      {wins.length === 0 && <div className="prov">{t('tex.noWindows')}</div>}
      {wins.map((w, i) => { const tr = treat(w.openingId), cm = materialOf(mc, tr.curtain ?? tr.sheer), cp = cm ? curtainPlan(w, fl.ceilingHeight, cm, tr.fullness) : null, bm = materialOf(mc, tr.blind), bp = bm ? blindPlan(w, bm) : null;
        return <div key={w.openingId} className="fin-row">
          <strong>{t('tex.window', { n: i + 1, w: formatLength(w.widthM, units, lang), side: t(`fin.side.${w.side}`) })}</strong>
          <div className="grid2">
            <label className="f"><span>{t('tex.curtain')}</span><select value={tr.curtain || ''} onChange={e => setTreat(w.openingId, { curtain: e.target.value || null })}><option value="">{t('fin.none')}</option>{by('curtain').map(opt)}</select></label>
            <label className="f"><span>{t('tex.sheer')}</span><select value={tr.sheer || ''} onChange={e => setTreat(w.openingId, { sheer: e.target.value || null })}><option value="">{t('fin.none')}</option>{by('sheer').map(opt)}</select></label>
          </div>
          <div className="grid2">
            <label className="f"><span>{t('tex.fullness')}</span><select value={tr.fullness ?? TEXTILE_RULES.defaultFullness} onChange={e => setTreat(w.openingId, { fullness: Number(e.target.value) })}>
              {TEXTILE_RULES.fullness.map(x => <option key={x} value={x}>{t(`tex.fullness.${String(x).replace('.', '_')}`)}</option>)}</select></label>
            <label className="f"><span>{t('tex.blind')}</span><select value={tr.blind || ''} onChange={e => setTreat(w.openingId, { blind: e.target.value || null })}><option value="">{t('fin.none')}</option>{by('blind').map(opt)}</select></label>
          </div>
          {cp && <div className="prov">{t('tex.curtainPlan', { rod: formatLength(cp.rodW, units, lang), panels: cp.panels, packs: cp.packs, drop: formatLength(cp.drop, units, lang) })}</div>}
          {bp && <div className="prov">{t('tex.blindPlan', { n: bp.count, cover: formatLength(bp.coverM, units, lang) })}</div>}
        </div>; })}
      <div className="grid2">
        <label className="f"><span>{t('tex.rug')}</span><select value={f.rug?.material || ''} onChange={e => onFinish({ rug: e.target.value ? { material: e.target.value, ...(f.rug?.rotate ? { rotate: true } : {}) } : null })}><option value="">{t('fin.none')}</option>{by('rug').map(opt)}</select></label>
        {f.rug && <label className="f"><span>{t('tex.rugRotate')}</span><input type="checkbox" checked={!!f.rug.rotate} onChange={e => onFinish({ rug: { material: f.rug!.material, ...(e.target.checked ? { rotate: true } : {}) } })} /></label>}
      </div>
    </fieldset>
  </div>);
}
