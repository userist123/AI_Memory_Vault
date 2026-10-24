import type { LegacyPlan, EngineGroup } from '../../core/layout';
import type { Floor, Catalog } from '../../core/types';
export function planToFloor(p: LegacyPlan): Floor;
export function floorToPlan(f: Floor, name: string): LegacyPlan;
export function optsToCatalog(o: Record<string, EngineGroup>, verifiedAt: string): Catalog;
export function selToSelections(sel: Record<string, number>, o: Record<string, EngineGroup>): Record<string, string>;
export function ovrToPlacements(ovr: Record<string, { x: number; z: number; rot: number }>, sel: Record<string, number>): any[];
