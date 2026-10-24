import { getDb } from './db';
import { HttpError } from './repo';
import { checkProfile, ProfileError, type StudioProfile } from '../core/studio-profile';

export async function getProfile(owner: string): Promise<StudioProfile> {
  const { q } = await getDb(), { rows } = await q('select data from studio_profile where owner=$1', [owner]); return (rows[0]?.data as StudioProfile) ?? {};
}
export async function saveProfile(owner: string, input: unknown): Promise<StudioProfile> {
  let p: StudioProfile; try { p = checkProfile(input); } catch (e: any){ if (e instanceof ProfileError) throw new HttpError(400, e.message); throw e; }
  const { q } = await getDb(); await q('insert into studio_profile(owner, data) values($1,$2) on conflict(owner) do update set data=excluded.data, updated_at=now()', [owner, JSON.stringify(p)]); return p;
}
