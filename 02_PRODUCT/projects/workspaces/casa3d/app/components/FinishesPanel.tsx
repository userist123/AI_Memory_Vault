'use client';
// Finisajele unei camere, ca la un designer: pardoseala și modul ei de așezare, pereții (vopsea, faianță, placări pe fiecare latură),
// tavanul (drept, fals, scafă luminoasă, cornișă, spoturi), plinta și iluminatul — cu costul camerei și avertismentele tehnice.
import { useMemo } from 'react';
import type { Snapshot, MaterialsCatalog, RoomFinishes, Room, Material, WallFeature, WallFeatureKind, FloorPattern, Catalog, WindowTreatment } from '@/core/types';
import { finishesOf, roomGeometry, computeBOQ } from '@/core/boq';
import { floorOfRoom } from '@/core/levels';
import { bandsOverlap, defaultLayout, PATTERNS, SIDES, FEATURE_CATEGORY, layoutOf, materialOf, sideGeometry, ceilingOf, clearHeight, finishIssues, pieceSizeCm, FINISH_RULES } from '@/core/finishes';
import { formatMoney, formatArea, formatLength } from '@/core/format';
import { normalizeHex } from '@/core/appearance';
import { lightReport } from '@/core/light-design';
import { roomWindows, curtainPlan, blindPlan, TEXTILE_RULES } from '@/core/textiles';
import { kitchenOf, kitchenRun, kitchenQuantities, kitchenIssues } from '@/core/kitchen';
import KitchenModules from './KitchenModules';
import MaterialPicker from './MaterialPicker';
import { bathOf, bathIssues, METAL_COLOR, TILE_ZONE_M } from '@/core/bath';
import { groups } from '@/core/catalog';
import { fixtureIssues, fixtureCount, FIXTURE_KINDS, FIXTURE_ANCHORS } from '@/core/fixtures';
import { usePrefs } from '@/lib/prefs';

const KINDS = Object.keys(FEATURE_CATEGORY) as WallFeatureKind[];

/** Piesele unei băi, în ordinea în care le alege un designer; fiecare se schimbă cu orice produs real din grupa ei. */
const BATH_GROUPS = ['wc', 'lavoar', 'dus', 'cada', 'bideu', 'dulapOglinda', 'caloriferBaie', 'coloanaBaie', 'boiler'];

export default function FinishesPanel({ room, snap, cat, mc, cur, onFinish, onSwap, onAdd }: { room: Room; snap: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; onFinish(p: Partial<RoomFinishes>): void; onSwap?(placementId: string, variantId: string): void; onAdd?(variantId: string): void }){
  const { t, lang, units } = usePrefs();
  const f = finishesOf(snap, room), g = roomGeometry(snap, room), fl = floorOfRoom(snap, room.id), wet = room.type === 'baie' || room.type === 'bucatarie';
  const by = (...c: Material['category'][]) => mc.materials.filter(m => c.includes(m.category));
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
  const run = kitchenRun(snap, cat, room.id), ks = kitchenOf(f), kq = run ? kitchenQuantities(run, ks, materialOf(mc, ks.countertop)) : null, kIss = kitchenIssues(snap, cat, mc, room.id, f);
  const setK = (p: Partial<NonNullable<RoomFinishes['kitchen']>>) => onFinish({ kitchen: { ...(f.kitchen || {}), ...p } });
  const fxs = f.fixtures || [], fxIss = fixtureIssues(snap, cat, mc, room, f), setFx = (i: number, p: Partial<NonNullable<RoomFinishes['fixtures']>[number]>) => onFinish({ fixtures: fxs.map((x, k) => k === i ? { ...x, ...p } : x) });
  const FX_CATS = { pendant: ['pendant', 'lighting'], sconce: ['wall_light'], track: ['led_profile', 'spot'] } as const;
  const bathGroups = useMemo(() => groups(cat), [cat]);
  const bs = bathOf(f), bIss = bathIssues(snap, mc, room, f, cat), setB = (p: Partial<NonNullable<RoomFinishes['bath']>>) => onFinish({ bath: { ...(f.bath || {}), ...p } });
  const treat = (id: string) => (f.windows || []).find(t => t.openingId === id) ?? { openingId: id };
  const setTreat = (id: string, p: Partial<WindowTreatment>) => onFinish({ windows: [...(f.windows || []).filter(t => t.openingId !== id), { ...treat(id), ...p }] });
  const sideLen = (s: WallFeature['side']) => formatLength(sideGeometry(fl, room, s).lengthM, units, lang);
  const patternOk = (p: FloorPattern) => tile ? p !== 'herringbone' && p !== 'chevron' || pieceSizeCm(fm)[0] >= 2 * pieceSizeCm(fm)[1] : true;

  return (<div className="finishes">
    <dl className="figs">
      <div><dt>{t('figs.floor')}</dt><dd>{formatArea(g.floorArea, units, lang)}</dd></div>
      <div><dt>{t('figs.walls')}</dt><dd>{formatArea(g.wallNet, units, lang)}</dd></div>
      <div><dt>{t('figs.ceiling')}</dt><dd>{formatArea(g.ceiling, units, lang)}</dd></div>
      <div><dt>{t('figs.perimeter')}</dt><dd>{formatLength(g.perimeter, units, lang)}</dd></div>
      <div className="wide"><dt>{t('figs.materials')}</dt><dd>{formatMoney(cost.mat, cur, lang)}</dd></div>
      <div className="wide"><dt>{t('figs.labour')}</dt><dd>~{formatMoney(cost.lab, cur, lang)}</dd></div>
    </dl>
    {issues.length > 0 && <div>{issues.map((i, k) => <div key={k} className="issue WARNING">{t(i.key, { ...i.vars, ...(i.vars?.pattern ? { pattern: t(`fin.pattern.${i.vars.pattern}`) } : {}), ...(i.vars?.side && i.vars.side !== '-' ? { side: t(`fin.side.${i.vars.side}`) } : {}) })}</div>)}</div>}

    <fieldset className="fin-group"><legend>{t('fin.floor')}</legend>
      <label className="f"><span>{t('editor.floorFinish')}</span><MaterialPicker cur={cur} value={f.floor} options={by('parquet', 'floor_tile')} onChange={v => onFinish({ floor: v, floorLayout: defaultLayout(materialOf(mc, v)) })} /></label>
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
      <label className="f"><span>{t('editor.wallPaint')}</span><MaterialPicker cur={cur} value={f.wallPaint} options={by('paint')} onChange={v => onFinish({ wallPaint: v })} /></label>
      {wet && <label className="f"><span>{t('editor.wallTile')}</span><MaterialPicker cur={cur} value={f.wallTile || ''} options={by('wall_tile')} none={t('editor.noTile')} onChange={v => onFinish({ wallTile: v || null })} /></label>}
      {features.map((w, i) => <div key={i} className="fin-row">
        <div className="grid2">
          <label className="f"><span>{t('fin.side')}</span><select value={w.side} onChange={e => setFeature(i, { side: e.target.value as WallFeature['side'] })}>
            {SIDES.map(s => <option key={s} value={s}>{t(`fin.side.${s}`)} · {sideLen(s)}</option>)}</select></label>
          <label className="f"><span>{t('fin.kind')}</span><select value={w.kind} onChange={e => { const k = e.target.value as WallFeatureKind; setFeature(i, { kind: k, material: firstOf(k) }); }}>
            {KINDS.filter(k => by(FEATURE_CATEGORY[k]).length).map(k => <option key={k} value={k}>{t(`fin.kind.${k}`)}</option>)}</select></label>
        </div>
        <label className="f"><span>{t('fin.product')}</span><MaterialPicker cur={cur} value={w.material} options={by(FEATURE_CATEGORY[w.kind])} onChange={v => setFeature(i, { material: v })} /></label>
        <div className="grid2">
          {w.kind !== 'rail' && <label className="f"><span>{t('fin.from')}</span><input type="number" min={0} max={fl.ceilingHeight} step={.05} value={w.fromM ?? ''} placeholder="0"
            onChange={e => setFeature(i, { fromM: e.target.value === '' ? undefined : Math.max(0, Math.min(fl.ceilingHeight - .05, Number(e.target.value) || 0)) })} /></label>}
          <label className="f"><span>{t(w.kind === 'rail' ? 'fin.railHeight' : 'fin.height')}</span><input type="number" min={.05} max={fl.ceilingHeight} step={.05} value={w.heightM ?? ''} placeholder={t(w.kind === 'rail' ? 'fin.railDefault' : 'fin.fullHeight')}
            onChange={e => setFeature(i, { heightM: e.target.value === '' ? undefined : Math.max(.05, Math.min(fl.ceilingHeight, Number(e.target.value) || fl.ceilingHeight)) })} /></label>
          {(w.kind === 'paint' || w.kind === 'panel' || w.kind === 'rail') && <label className="f"><span>{t('fin.color')}</span><input type="color" value={w.color || '#f3f1ec'} onChange={e => { const h = normalizeHex(e.target.value); if (h) setFeature(i, { color: h }); }} /></label>}
          <button className="btn" style={{ alignSelf: 'end' }} onClick={() => onFinish({ wallFeatures: features.filter((_, k) => k !== i) })}>{t('fin.remove')}</button>
        </div>
      </div>)}
      {features.length < 24 && <button className="btn" onClick={() => onFinish({ wallFeatures: [...features, { side: freeSide ?? 'N', kind: 'wallpaper', material: firstOf('wallpaper'), ...(freeSide ? {} : { fromM: Math.min(fl.ceilingHeight - .3, 1.2) }) }] })}>{t('fin.addFeature')}</button>}
      {bandsOverlap(features, fl.ceilingHeight) && <div className="issue WARNING">{t('fin.bandsOverlap')}</div>}
      {room.type !== 'baie' && room.type !== 'bucatarie' && <label className="f"><span>{t('editor.baseboard')}</span><MaterialPicker cur={cur} value={f.baseboard || ''} options={by('baseboard')} none={t('editor.noBaseboard')} onChange={v => onFinish({ baseboard: v || null })} /></label>}
    </fieldset>

    <fieldset className="fin-group"><legend>{t('fin.ceiling')}</legend>
      <label className="f"><span>{t('fin.ceilingType')}</span><select value={c.type} onChange={e => setCeiling({ type: e.target.value as 'flat' | 'drop' | 'cove' })}>
        {(['flat', 'drop', 'cove'] as const).map(k => <option key={k} value={k}>{t(`fin.ceiling.${k}`)}</option>)}</select></label>
      {c.type !== 'flat' && <div className="grid2">
        <label className="f"><span>{t('fin.drop')}</span><input type="number" min={5} max={FINISH_RULES.maxDropCm} value={c.dropCm} onChange={e => setCeiling({ dropCm: Math.max(5, Math.min(FINISH_RULES.maxDropCm, Number(e.target.value) || 10)) })} /></label>
        {c.type === 'cove' && <label className="f"><span>{t('fin.cove')}</span><input type="number" min={10} max={FINISH_RULES.maxCoveCm} value={c.coveCm} onChange={e => setCeiling({ coveCm: Math.max(10, Math.min(FINISH_RULES.maxCoveCm, Number(e.target.value) || 25)) })} /></label>}
      </div>}
      {c.type !== 'flat' && <div className="prov">{t('fin.clearHeight', { h: formatLength(clearHeight(fl, f), units, lang) })}</div>}
      {c.type === 'cove' && <label className="f"><span>{t('fin.led')}</span><MaterialPicker cur={cur} value={c.led || ''} options={by('led_strip')} none={t('fin.none')} onChange={v => setCeiling({ led: v || null })} /></label>}
      <label className="f"><span>{t('fin.cornice')}</span><MaterialPicker cur={cur} value={c.cornice || ''} options={by('cornice')} none={t('fin.none')} onChange={v => setCeiling({ cornice: v || null })} /></label>
      <div className="grid2">
        <label className="f"><span>{t('fin.spots')}</span><MaterialPicker cur={cur} value={c.spot || ''} options={by('spot')} none={t('fin.none')} onChange={v => setCeiling({ spot: v || null, spots: f.ceiling?.spots ?? 4 })} /></label>
        {c.spot && <label className="f"><span>{t('fin.spotCount')}</span><input type="number" min={0} max={40} value={c.spots} onChange={e => setCeiling({ spots: Math.max(0, Math.min(40, Math.round(Number(e.target.value) || 0))) })} /></label>}
      </div>
    </fieldset>

    <fieldset className="fin-group"><legend>{t('fin.lighting')}</legend>
      <div className="prov" role="status">{t(light.unknownLumens ? 'light.estimateUnknown' : 'light.estimate', { lux: light.lux, lm: light.lumens })}{light.target ? ` · ${t('light.target', { min: light.target[0], max: light.target[1] })}` : ''}{light.ccts.length ? ` · ${light.ccts.join(' / ')} K` : ''}</div>
      <div className="prov">{t('light.layers')}: {(['ambient', 'task', 'accent'] as const).map(k => `${t(`light.layer.${k}`)} ${light.layers[k] ? '✓' : '—'}`).join(' · ')}</div>
      <div className="grid2">
        <label className="f"><span>{t('editor.light')}</span><MaterialPicker cur={cur} value={f.light} options={by('lighting')} onChange={v => onFinish({ light: v })} /></label>
        <label className="f"><span>{t('editor.lightCount')}</span><input type="number" min={0} max={20} value={f.lights ?? ''} placeholder={t('common.auto')} onChange={e => onFinish({ lights: e.target.value === '' ? undefined : Math.max(0, Math.min(20, Number(e.target.value) || 0)) })} /></label>
      </div>
      <div className="prov">{t('fix.title')}</div>
      {fxIss.map((i, k) => <div key={`fi${k}`} className="issue WARNING">{t(i.key, i.vars)}</div>)}
      {fxs.map((x, i) => <div key={i} className="band">
        <div className="grid2">
          <label className="f"><span>{t('fix.kind')} {i + 1}</span><select value={x.kind} onChange={e => setFx(i, { kind: e.target.value as any, material: null })}>{FIXTURE_KINDS.map(k => <option key={k} value={k}>{t(`fix.kind.${k}`)}</option>)}</select></label>
          <label className="f"><span>{t('fix.anchor')}</span><select value={x.anchor} onChange={e => setFx(i, { anchor: e.target.value as any })}>{FIXTURE_ANCHORS.map(a => <option key={a} value={a}>{t(`fix.anchor.${a}`)}</option>)}</select></label>
        </div>
        <label className="f"><span>{t('fix.product')}</span><MaterialPicker cur={cur} value={x.material || ''} options={by(...FX_CATS[x.kind])} none={t('fin.none')} onChange={v => setFx(i, { material: v || null })} /></label>
        <div className="grid2">
          <label className="f"><span>{t('fix.count')}</span><input type="number" min={1} max={12} value={x.count ?? ''} placeholder={String(fixtureCount({ ...x, count: undefined } as any))} onChange={e => setFx(i, { count: e.target.value === '' ? undefined : Math.max(1, Math.min(12, Math.round(Number(e.target.value) || 1))) })} /></label>
          <label className="f"><span>{t('fix.color')}</span><input type="color" value={x.color || '#1c1c1c'} onChange={e => { const h = normalizeHex(e.target.value); if (h) setFx(i, { color: h }); }} /></label>
        </div>
        {x.anchor === 'wall' && <label className="f"><span>{t('fix.side')}</span><select value={x.side || 'N'} onChange={e => setFx(i, { side: e.target.value as any })}>{(['N', 'S', 'E', 'W'] as const).map(sd => <option key={sd} value={sd}>{sd}</option>)}</select></label>}
        <button className="btn" onClick={() => onFinish({ fixtures: fxs.filter((_, k) => k !== i) })}>{t('fix.remove')}</button>
      </div>)}
      {fxs.length < 24 && <button className="btn" onClick={() => onFinish({ fixtures: [...fxs, { kind: 'pendant', anchor: snap.placements.some(p => p.roomId === room.id && p.group === 'masa') ? 'table' : 'center' }] })}>{t('fix.add')}</button>}
    </fieldset>
    {room.type === 'baie' && <fieldset className="fin-group"><legend>{t('bath.title')}</legend>
      {bIss.map((i, k) => <div key={k} className="issue WARNING">{t(i.key, i.vars)}</div>)}
      <div className="grid2">
        <label className="f"><span>{t('bath.metal')}</span><select value={bs.metal} onChange={e => setB({ metal: e.target.value as any })}>{Object.keys(METAL_COLOR).map(x => <option key={x} value={x}>{t(`bath.metal.${x}`)}</option>)}</select></label>
        <label className="f"><span>{t('bath.tileZone')}</span><select value={bs.tileZone} onChange={e => setB({ tileZone: e.target.value as any })}>{Object.keys(TILE_ZONE_M).map(x => <option key={x} value={x}>{t(`bath.tileZone.${x}`)}</option>)}</select></label>
      </div>
      {onSwap && <div className="bath-pieces"><h5>{t('bath.pieces')}</h5>
        {snap.placements.filter(p => p.roomId === room.id && BATH_GROUPS.includes(p.group)).map(p => { const vs = bathGroups[p.group]?.variants ?? [];
          return <label key={p.id} className="f"><span>{bathGroups[p.group]?.label ?? p.group}</span><select value={p.variantId} onChange={e => onSwap(p.id, e.target.value)}>
            {vs.map(v => { const o = cat.offers.find(x => x.variantId === v.id); return <option key={v.id} value={v.id}>{v.name}{o ? ` — ${formatMoney(o.price, cur, lang)}` : ''}</option>; })}</select></label>; })}
        {onAdd && <div className="pcard-act">{BATH_GROUPS.filter(g => bathGroups[g] && !snap.placements.some(p => p.roomId === room.id && p.group === g)).map(g =>
          <button key={g} className="btn" onClick={() => onAdd(bathGroups[g]!.variants[0]!.id)}>+ {bathGroups[g]!.label}</button>)}</div>}
        <p className="prov">{t('bath.piecesNote')}</p></div>}
      <div className="grid2">
        <label className="f"><span>{t('bath.tap')}</span><MaterialPicker cur={cur} value={bs.tap || ''} options={by('bath_tap')} none={t('fin.none')} onChange={v => setB({ tap: v || null })} /></label>
        <label className="f"><span>{t('bath.showerSet')}</span><MaterialPicker cur={cur} value={bs.shower || ''} options={by('shower_set')} none={t('fin.none')} onChange={v => setB({ shower: v || null })} /></label>
        <label className="f"><span>{t('bath.radiator')}</span><MaterialPicker cur={cur} value={bs.towelRadiator || ''} options={by('towel_radiator')} none={t('fin.none')} onChange={v => setB({ towelRadiator: v || null })} /></label>
        <label className="f"><span>{t('bath.mirror')}</span><MaterialPicker cur={cur} value={bs.mirror || ''} options={by('led_mirror')} none={t('fin.none')} onChange={v => setB({ mirror: v || null })} /></label>
      </div>
    </fieldset>}

    {run && <fieldset className="fin-group"><legend>{t('kit.title')}</legend>
      {kIss.filter(i => !i.key.startsWith('kmod.')).map((i, k) => <div key={k} className="issue WARNING">{t(i.key, i.vars)}</div>)}
      <div className="prov">{t('kit.run', { len: formatLength(run.lengthM, units, lang), n: run.modules, fronts: kq!.fronts })}</div>
      <KitchenModules runCm={run.lengthM * 100} modules={f.kitchen?.modules} onChange={m => setK({ modules: m })} />
      <div className="grid2">
        <label className="f"><span>{t('kit.fronts')}</span><input type="color" value={ks.frontColor} onChange={e => { const h = normalizeHex(e.target.value); if (h) setK({ frontColor: h }); }} /></label>
        <label className="f"><span>{t('kit.finish')}</span><select value={ks.frontFinish} onChange={e => setK({ frontFinish: e.target.value as any })}>{(['matt', 'gloss', 'wood'] as const).map(x => <option key={x} value={x}>{t(`kit.finish.${x}`)}</option>)}</select></label>
        <label className="f"><span>{t('kit.handle')}</span><select value={ks.handle} onChange={e => setK({ handle: e.target.value as any })}>{(['bar', 'knob', 'profile', 'none'] as const).map(x => <option key={x} value={x}>{t(`kit.handle.${x}`)}</option>)}</select></label>
        <label className="f"><span>{t('kit.handleColor')}</span><input type="color" value={ks.handleColor} onChange={e => { const h = normalizeHex(e.target.value); if (h) setK({ handleColor: h }); }} /></label>
        <label className="f"><span>{t('kit.upper')}</span><select value={ks.upper} onChange={e => setK({ upper: e.target.value as any })}>{(['open', 'closed', 'none'] as const).map(x => <option key={x} value={x}>{t(`kit.upper.${x}`)}</option>)}</select></label>
        <label className="f"><span>{t('kit.backsplash')}</span><select value={ks.backsplash} onChange={e => setK({ backsplash: e.target.value as any })}>{(['tile', 'countertop', 'glass', 'paint'] as const).map(x => <option key={x} value={x}>{t(`kit.backsplash.${x}`)}</option>)}</select></label>
      </div>
      {(ks.backsplash === 'glass' || ks.backsplash === 'paint') && <label className="f"><span>{t('kit.backsplashColor')}</span><input type="color" value={ks.backsplashColor || '#e8e6e1'} onChange={e => { const h = normalizeHex(e.target.value); if (h) setK({ backsplashColor: h }); }} /></label>}
      <label className="f"><span>{t('kit.countertop')}</span><MaterialPicker cur={cur} value={ks.countertop || ''} options={by('countertop')} none={t('kit.customTop')} onChange={v => setK({ countertop: v || null })} /></label>
      {!ks.countertop && <div className="grid2">
        <label className="f"><span>{t('kit.topColor')}</span><input type="color" value={ks.countertopColor} onChange={e => { const h = normalizeHex(e.target.value); if (h) setK({ countertopColor: h }); }} /></label>
        <label className="f"><span>{t('kit.topMm')}</span><select value={ks.countertopMm} onChange={e => setK({ countertopMm: Number(e.target.value) })}>{[12, 20, 28, 38].map(x => <option key={x} value={x}>{x} mm</option>)}</select></label>
      </div>}
      <div className="grid2">
        <label className="f"><span>{t('kit.sink')}</span><MaterialPicker cur={cur} value={ks.sink || ''} options={by('kitchen_sink')} none={t('fin.none')} onChange={v => setK({ sink: v || null })} /></label>
        <label className="f"><span>{t('kit.tap')}</span><MaterialPicker cur={cur} value={ks.tap || ''} options={by('kitchen_tap')} none={t('fin.none')} onChange={v => setK({ tap: v || null })} /></label>
      </div>
      <label className="f"><span>{t('kit.led')}</span><MaterialPicker cur={cur} value={ks.underLed || ''} options={by('led_strip')} none={t('fin.none')} onChange={v => setK({ underLed: v || null })} /></label>
    </fieldset>}

    <fieldset className="fin-group"><legend>{t('tex.title')}</legend>
      {wins.length === 0 && <div className="prov">{t('tex.noWindows')}</div>}
      {wins.map((w, i) => { const tr = treat(w.openingId), cm = materialOf(mc, tr.curtain ?? tr.sheer), cp = cm ? curtainPlan(w, fl.ceilingHeight, cm, tr.fullness) : null, bm = materialOf(mc, tr.blind), bp = bm ? blindPlan(w, bm) : null;
        return <div key={w.openingId} className="fin-row">
          <strong>{t('tex.window', { n: i + 1, w: formatLength(w.widthM, units, lang), side: t(`fin.side.${w.side}`) })}</strong>
          <div className="grid2">
            <label className="f"><span>{t('tex.curtain')}</span><MaterialPicker cur={cur} value={tr.curtain || ''} options={by('curtain')} none={t('fin.none')} onChange={v => setTreat(w.openingId, { curtain: v || null })} /></label>
            <label className="f"><span>{t('tex.sheer')}</span><MaterialPicker cur={cur} value={tr.sheer || ''} options={by('sheer')} none={t('fin.none')} onChange={v => setTreat(w.openingId, { sheer: v || null })} /></label>
          </div>
          <div className="grid2">
            <label className="f"><span>{t('tex.fullness')}</span><select value={tr.fullness ?? TEXTILE_RULES.defaultFullness} onChange={e => setTreat(w.openingId, { fullness: Number(e.target.value) })}>
              {TEXTILE_RULES.fullness.map(x => <option key={x} value={x}>{t(`tex.fullness.${String(x).replace('.', '_')}`)}</option>)}</select></label>
            <label className="f"><span>{t('tex.blind')}</span><MaterialPicker cur={cur} value={tr.blind || ''} options={by('blind')} none={t('fin.none')} onChange={v => setTreat(w.openingId, { blind: v || null })} /></label>
          </div>
          {cp && <div className="prov">{t('tex.curtainPlan', { rod: formatLength(cp.rodW, units, lang), panels: cp.panels, packs: cp.packs, drop: formatLength(cp.drop, units, lang) })}</div>}
          {bp && <div className="prov">{t('tex.blindPlan', { n: bp.count, cover: formatLength(bp.coverM, units, lang) })}</div>}
        </div>; })}
      <div className="grid2">
        <label className="f"><span>{t('tex.rug')}</span><MaterialPicker cur={cur} value={f.rug?.material || ''} options={by('rug')} none={t('fin.none')} onChange={v => onFinish({ rug: v ? { material: v, ...(f.rug?.rotate ? { rotate: true } : {}) } : null })} /></label>
        {f.rug && <label className="f"><span>{t('tex.rugRotate')}</span><input type="checkbox" checked={!!f.rug.rotate} onChange={e => onFinish({ rug: { material: f.rug!.material, ...(e.target.checked ? { rotate: true } : {}) } })} /></label>}
      </div>
    </fieldset>
  </div>);
}
