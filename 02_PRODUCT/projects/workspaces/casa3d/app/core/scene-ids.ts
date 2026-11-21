// Id-ul elementului din proiect pentru un obiect al vederii 3D (ghidul BIM §4.1): stâlp, grindă (`eid`), punct tehnic (`tech`),
// obiect pe perete (`wallItem`), piesă de mobilier (`pid`, pe grupul piesei). Urcă pe părinți; null dacă obiectul nu e un element
// (pereții și plăcile vin din planul vechi fără id). Folosit de exportScene ca obiectul din Blender să poată fi legat de element.
export interface SceneNode { userData?: Record<string, unknown> | null; parent?: SceneNode | null }
export function elementIdOf(o: SceneNode | null | undefined): string | null {
  for (let a = o; a; a = a.parent) {
    const u = a.userData || {}, id = u.eid || u.tech || u.wallItem || u.pid;
    if (typeof id === 'string' && id) return id.slice(0, 64);
  }
  return null;
}
