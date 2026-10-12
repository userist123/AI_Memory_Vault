import { describe, expect, it } from 'vitest';
import { initialViewerState, reduceViewer, renderItems, DEFAULT_CAMERA, JOYSTICK_SPEED } from '../src/view-state';
import { emptyTwin, rectangleRoom, type Twin } from '../src/twin';

const twin = (): Twin => ({ ...emptyTwin('t'), rooms: [rectangleRoom('r', 'R', 0, 0, 4, 3)],
  placements: [{ id: 'bed', catalogId: 'bed-160', roomId: 'r', x: 0, y: 0, w: 1.6, d: 2, h: 0.5, rotation: 0 }] });

describe('ViewerState', () => {
  it('is shared by both renderers: the same items feed plan and 3d', () => {
    let s = initialViewerState('fp1');
    s = reduceViewer(s, { type: 'select', ids: ['bed'] });
    const plan = renderItems(twin(), s);
    s = reduceViewer(s, { type: 'setMode', mode: '3d' });
    expect(renderItems(twin(), s)).toEqual(plan);
    expect(plan[0]).toMatchObject({ id: 'bed', selected: true, preview: false, h: 0.5 });
  });
  it('a twin change clears preview, selection and highlights', () => {
    let s = initialViewerState('fp1');
    s = reduceViewer(s, { type: 'select', ids: ['bed'] });
    s = reduceViewer(s, { type: 'preview', proposalId: 'p' });
    s = reduceViewer(s, { type: 'highlight', issues: [{ severity: 'WARNING', code: 'CIRCULATION_LOW', refs: ['r'], message: '' }, { severity: 'ERROR', code: 'DOES_NOT_FIT', refs: ['r'], message: '' }] });
    expect(s.highlights).toEqual({ r: 'ERROR' });
    s = reduceViewer(s, { type: 'twinChanged', fingerprint: 'fp2' });
    expect(s).toMatchObject({ twinFingerprint: 'fp2', selectedIds: [], highlights: {} });
    expect(s.previewProposalId).toBeUndefined();
  });
  it('marks preview-only and changed placements as preview items', () => {
    let s = reduceViewer(initialViewerState('fp1'), { type: 'preview', proposalId: 'p' });
    const base = twin();
    const prev: Twin = { ...base, placements: [{ ...base.placements[0]!, x: 1 }, { id: 'ai-n-1', catalogId: 'n', roomId: 'r', x: 2, y: 2, w: 0.4, d: 0.4, h: 0.5, rotation: 0 }] };
    const items = renderItems(base, s, prev);
    expect(items.map(i => [i.id, i.preview])).toEqual([['ai-n-1', true], ['bed', true]]);
    s = reduceViewer(s, { type: 'preview' });
    expect(renderItems(base, s, prev).map(i => i.id)).toEqual(['bed']);
  });
  it('clamps zoom, pitch and distance and keeps yaw in range', () => {
    let s = initialViewerState('fp');
    s = reduceViewer(s, { type: 'zoom', factor: 100 }); expect(s.plan.zoom).toBe(8);
    s = reduceViewer(s, { type: 'zoom', factor: 0.0001 }); expect(s.plan.zoom).toBe(0.25);
    s = reduceViewer(s, { type: 'orbit', dYaw: -50, dPitch: 400 }); expect(s.camera).toMatchObject({ yaw: 355, pitch: 89 });
    s = reduceViewer(s, { type: 'dolly', factor: 0.001 }); expect(s.camera.distance).toBe(1.5);
    s = reduceViewer(s, { type: 'resetCamera' }); expect(s.camera).toEqual(DEFAULT_CAMERA);
  });
  it('joystick walks the target in the camera frame, deterministically and bounded by dt', () => {
    let s = reduceViewer(initialViewerState('fp'), { type: 'orbit', dYaw: -45, dPitch: 0 }); // yaw 0: forward = +y
    const a = reduceViewer(s, { type: 'joystick', axes: { x: 0, y: 1 }, dt: 0.1 });
    expect(a.camera.target).toEqual({ x: 0, y: 0.25, z: 0 });
    expect(a.input.touch).toBe(true);
    const b = reduceViewer(s, { type: 'joystick', axes: { x: 1, y: 0 }, dt: 0.1 });
    expect(b.camera.target).toEqual({ x: 0.25, y: 0, z: 0 });
    const c = reduceViewer(s, { type: 'joystick', axes: { x: 0, y: 5 }, dt: 10 }); // clamped deflection and dt
    expect(c.camera.target.y).toBe(JOYSTICK_SPEED * 0.5);
    expect(reduceViewer(s, { type: 'joystick', axes: { x: 0, y: 1 }, dt: 0.1 })).toEqual(a);
  });
});
