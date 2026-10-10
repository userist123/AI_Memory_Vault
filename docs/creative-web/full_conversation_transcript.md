# Arhiva Conversației Complete: Deconstructie & Blueprint Terminal Industries, Canvas Scrubbing, Lenis, Shaders & AI Video Analysis

Data: 10 Octombrie 2026  
Subiect: Tehnici Creative Web, Video Scrubbing, GSAP ScrollTrigger, Lenis Smooth Scroll, WebGL/GLSL, Ingestie Video AI & Pipeline Arhitectural

---

## 1. Subiectul Inițial: Analiza Tehnologiilor din Videoclipuri

### Întrebare Utilizator:
> *„vreau sa stiu ce tehnici sunt folosite pentru site-urile astea”* (cu referire la videoclipurile despre Terminal Industries - `terminal-industries.com` și showcase-urile Awwwards cu efecte 3D/particule).

### Analiză și Răspuns:
1. **Grafică 3D și Shadere (WebGL / WebGPU)**:
   - Three.js / React Three Fiber (R3F) pentru randare 3D, iluminare și modele GLTF/GLB.
   - Shadere personalizate (GLSL - vertex & fragment) pentru efectele de particule volumetrice, refracție, nebuloase și ADN luminos.
   - Modele optimizate în Blender (low-poly cu bake de texturi, compresie Draco/Meshopt).
2. **Scrollytelling și Controlul Animațiilor**:
   - GSAP (GreenSock) + ScrollTrigger pentru legarea progresului pe axa de timp (0% - 100%) direct de poziția barei de scroll (`scrub: true`).
3. **Smooth Scroll și Fizică Inerțială**:
   - Lenis Scroll (sau Locomotive Scroll) pentru a intercepta scroll-ul nativ și a aplica o curbă de inerție (lerping), prevenind trepidațiile.
4. **Video Canvas Scrubbing (Apple-Style)**:
   - Folosirea cadrelor pre-randate sau a videoclipurilor encodate All-Intra desenate pe un element `<canvas>` prin `ctx.drawImage()`.
5. **Frontend Modern**:
   - Next.js (App Router), Tailwind CSS, Framer Motion și elemente Glassmorphism.

---

## 2. Inspecția Video și Modul de Replicare pentru un Agent AI

### Întrebare Utilizator:
> *„cum ai verificat videoclipurile sa invat un ai sa faca si el ca tine”*

### Arhitectura Pipeline-ului de Ingestie & Audit Video pentru Agenți AI:
1. **Extragere & Ingestie Media**:
   - **Eșantionare cadre**: `ffmpeg` sau `OpenCV` extrage 1 cadru la fiecare 1-2 secunde.
   - **Visiune & OCR**: Modele VQA / Vision (GPT-4o, Claude 3.5/3.7 Sonnet, Qwen2.5-VL) citesc textul de pe ecran (URL: `terminal-industries.com`, titluri de interfață, indici vizuali).
   - **Transcrierea vocii**: `Whisper` de la OpenAI extrage pista audio și timpii.
2. **Corelare Semiotică**:
   - Mișcarea degetului pe trackpad corelată cu rotirea camionului = *Canvas Scrubbing / ScrollTrigger*.
   - Particulele deformate fluid pe fundal negru = *GLSL Perlin/Simplex noise vertex shaders*.
3. **Validare prin Tool de Căutare Web**:
   - Căutare automată țintită (ex: `"terminal-industries.com tech stack"`).
4. **Diagramă Flux**:
   ```
   [ MP4 Video ] ──► ffmpeg/Whisper ──► Transcriere text
                 ──► OpenCV Frames   ──► Vision Model (OCR + UI) ──► Orchestrator Agentic ──► Web Search ──► Raport Tehnic
   ```

---

## 3. Procedura Specifică din Site-ul cu Camionul (Terminal Industries)

### Întrebare Utilizator:
> *„ce procedura s-a folosit in site-ul cu camionul?”*

### Explicație Pas cu Pas:
1. **Offline 3D Pre-render**: Modelarea camionului și a curții în Blender/Cinema 4D și randarea la 60 fps ca secvență cinematică.
2. **All-Intra Encodare (I-Frames)**: Re-encodare cu `gop_size = 1` pentru ca fiecare cadru să fie independent, permițând seek instantaneu la derulare.
3. **HTML5 `<canvas>` Rendering**: Preluarea cadrului curent din elementul video și desenarea scalată (`object-fit: cover`).
4. **GSAP ScrollTrigger Master Timeline**: Legarea proprietății `video.currentTime` la scroll-ul secțiunii ancorate (`position: sticky`).
5. **Layering UI**: Cardurile de date descriptive sunt elemente HTML/React suprapuse cu `z-index`, animate pe opacitate și translație la procente fixe de scroll.

---

## 4. Ghid Detaliat de Implementare & Scripturi

### Întrebări & Soluții Abordate:

#### A. Conversie Video All-Intra (FFmpeg)
```bash
ffmpeg -i input.mp4 -vf "scale=1920:1080" -c:v libx264 -preset slow -crf 18 -g 1 -keyint_min 1 -an output_scrub.mp4
```

#### B. Export Secvență de Imagini WebP (FFmpeg)
```bash
ffmpeg -i input_truck.mp4 -vf "fps=30,scale=1920:1080" -c:v libwebp -quality 80 -compression_level 6 public/frames/frame_%04d.webp
```

#### C. Sincronizare Lenis cu GSAP ScrollTrigger
```typescript
// SmoothScrollProvider.tsx
import Lenis from "lenis";
import gsap from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";

gsap.registerPlugin(ScrollTrigger);
const lenis = new Lenis({ duration: 1.2, smoothWheel: true });
lenis.on("scroll", ScrollTrigger.update);
gsap.ticker.add((time) => lenis.raf(time * 1000));
gsap.ticker.lagSmoothing(0);
```

#### D. Shader GLSL pentru Efectul de Distorsie (Noise Shader)
- **Vertex Shader**: Deformează poziția vertexurilor pe baza funcției 3D Simplex Noise multiplicată cu parametrul `uDistortion` și `uTime`.
- **Fragment Shader**: Mixează două culori de gradient și adaugă iluminare Fresnel pe margini.

#### E. Optimizarea Memoriei pentru 300+ Imagini (LRU Cache & ImageBitmap)
- Folosirea unei ferestre glisante (ex. cadrul curent $\pm 15$ cadre).
- Conversia Blob-urilor descărcate în `createImageBitmap()` pentru decodare pe fir separat de execuție.

#### F. Fallback Compatibil pentru `requestIdleCallback`
- Detectare compatibilitate în Safari cu fallback bazat pe `setTimeout`.

#### G. Split-Type cu GSAP ScrollTrigger
- Descompunerea titlurilor în litere (`types: "chars,words"`) și animarea lor cu efect de cascadă (stagger: 0.02) și rotație 3D.

#### H. Tratarea Erorilor de Rețea (Retry Logic cu Backoff Exponențial)
- Funcție automată de reîncercare de până la 3 ori pentru fiecare cadru descărcat.

#### I. Componentă React Preloader Progresiv
- Bară minimalistă de progres cu afișarea procentului de descărcare a cadrelor și tranziție de fade out la finalizare.

---

## 5. Artefacte Livrate în Depozit

1. `docs/creative-web/terminal_industries_scroll_blueprint.md` - Ghidul tehnic detaliat cu toate fragmentele de cod.
2. `docs/creative-web/demo-standalone.html` - Demonstrație autonomă (HTML + Tailwind + GSAP CDN) gata de testare locală.
3. `docs/creative-web/full_conversation_transcript.md` - Transcriptul complet al tuturor discuțiilor și întrebărilor detaliate.
