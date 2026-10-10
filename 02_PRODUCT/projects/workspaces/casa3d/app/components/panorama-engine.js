// Vizualizator 360° (three.js): panorama echirectangulară pe interiorul unei sfere; tragi ca să privești, rotița apropie.
import * as THREE from 'three';

/** Sfera pe care stă panorama, văzută din interior. Longitudinea 0 a imaginii (mijlocul ei, privire spre -z la randare)
 *  trebuie să cadă pe -z și aici: SphereGeometry pune u = 0,5 pe -x, deci sfera se rotește cu -90° în jurul lui y. */
export function panoramaGeometry(){ const geo = new THREE.SphereGeometry(10, 64, 32); geo.scale(-1, 1, 1); geo.rotateY(-Math.PI / 2); return geo; }

export function createPanorama(canvas, url){
  const R = new THREE.WebGLRenderer({ canvas, antialias: true }); R.setPixelRatio(Math.min(devicePixelRatio, 2)); R.outputEncoding = THREE.sRGBEncoding;
  const scene = new THREE.Scene(), cam = new THREE.PerspectiveCamera(75, 1, .1, 100);
  const tex = new THREE.TextureLoader().load(url, () => draw()); tex.encoding = THREE.sRGBEncoding;
  const geo = panoramaGeometry(), mat = new THREE.MeshBasicMaterial({ map: tex }); scene.add(new THREE.Mesh(geo, mat));
  let lon = 0, lat = 0, drag = false, lx = 0, ly = 0;
  // aceeași convenție ca la randare: longitudine 0 = privire spre -z (nord pe plan), 90 = spre +x
  const draw = () => { const phi = (90 - lat) * Math.PI / 180, th = lon * Math.PI / 180; cam.lookAt(Math.sin(phi) * Math.sin(th), Math.cos(phi), -Math.sin(phi) * Math.cos(th)); R.render(scene, cam); };
  const resize = () => { const w = canvas.clientWidth || 1, h = canvas.clientHeight || 1; R.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix(); draw(); };
  const down = e => { drag = true; lx = e.clientX; ly = e.clientY; canvas.setPointerCapture(e.pointerId); };
  const move = e => { if (!drag) return; lon -= (e.clientX - lx) * .15; lat = Math.max(-85, Math.min(85, lat + (e.clientY - ly) * .15)); lx = e.clientX; ly = e.clientY; draw(); };
  const up = () => { drag = false; };
  const wheel = e => { e.preventDefault(); cam.fov = Math.max(35, Math.min(95, cam.fov + e.deltaY * .03)); cam.updateProjectionMatrix(); draw(); };
  canvas.addEventListener('pointerdown', down); canvas.addEventListener('pointermove', move); canvas.addEventListener('pointerup', up); canvas.addEventListener('pointercancel', up); canvas.addEventListener('wheel', wheel, { passive: false });
  const ro = new ResizeObserver(resize); ro.observe(canvas); resize();
  return { look(l, a){ lon = l; lat = a; draw(); }, dispose(){ ro.disconnect(); canvas.removeEventListener('pointerdown', down); canvas.removeEventListener('pointermove', move); canvas.removeEventListener('pointerup', up); canvas.removeEventListener('pointercancel', up); canvas.removeEventListener('wheel', wheel);
    // contextul WebGL se eliberează imediat: browserul ține puține, iar la depășire îl pierde pe cel mai vechi (vizualizatorul 3D)
    geo.dispose(); mat.dispose(); tex.dispose(); R.dispose(); R.forceContextLoss(); } };
}
