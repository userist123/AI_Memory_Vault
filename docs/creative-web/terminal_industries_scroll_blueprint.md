# Creative Web Engineering: Scrollytelling, Canvas Scrubbing & GLSL Shaders

Ghid tehnic complet și blueprint arhitectural inspirat de **Terminal Industries** (`terminal-industries.com`) și site-uri interactive Awwwards de nivel înalt.

---

## 1. Stack Tehnologic

| Componentă | Tehnologie | Rol |
| :--- | :--- | :--- |
| **Framework** | Next.js 14+ (App Router) / React | Arhitectură frontend, SSR și optimizare bundle |
| **Animation Engine** | GSAP (ScrollTrigger) | Sincronizare pe axa de timp legată de scroll |
| **Smooth Scroll** | Lenis Scroll | Fizică inerțială, eliminare trepidații |
| **Graphics** | HTML5 Canvas 2D / Three.js / WebGL | Randare frame-by-frame sau modele 3D interactive |
| **Styling** | Tailwind CSS + Glassmorphism | Straturi UI, efecte de backdrop blur |
| **Video Encoding** | FFmpeg | Conversie All-Intra (I-Frames) sau secvențe WebP |

---

## 2. Pipeline FFmpeg pentru Scrubbing Instantaneu

### Metoda A: Video All-Intra (Keyframe per frame)
```bash
ffmpeg -i input.mp4 -r 30 -vf "scale=1920:1080" -c:v libx264 -preset slow -crf 20 -g 1 -keyint_min 1 -an optimized_truck.mp4
```

### Metoda B: Secvență de Imagini WebP (Zero Buffer Lag)
```bash
ffmpeg -i input.mp4 -vf "fps=30,scale=1920:1080" -c:v libwebp -quality 80 -compression_level 6 public/frames/frame_%04d.webp
```

---

## 3. Integrare Lenis + GSAP ScrollTrigger

Pentru a evita conflictele de frame loop:

```typescript
// SmoothScrollProvider.tsx
"use client";

import { useEffect } from "react";
import Lenis from "lenis";
import "lenis/dist/lenis.css";
import gsap from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";

export default function SmoothScrollProvider({ children }: { children: React.ReactNode }) {
  useEffect(() => {
    gsap.registerPlugin(ScrollTrigger);

    const lenis = new Lenis({
      duration: 1.2,
      easing: (t) => Math.min(1, 1.001 - Math.pow(2, -10 * t)),
      smoothWheel: true,
    });

    lenis.on("scroll", ScrollTrigger.update);

    const updateTicker = (time: number) => {
      lenis.raf(time * 1000);
    };

    gsap.ticker.add(updateTicker);
    gsap.ticker.lagSmoothing(0);

    return () => {
      gsap.ticker.remove(updateTicker);
      lenis.destroy();
    };
  }, []);

  return <>{children}</>;
}
```

---

## 4. Componentă React cu Secvențe de Imagini & Preloader

```tsx
// TerminalExperience.tsx
"use client";

import { useEffect, useRef, useState } from "react";
import gsap from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";

gsap.registerPlugin(ScrollTrigger);

const TOTAL_FRAMES = 120;
const getFramePath = (index: number) =>
  `/frames/frame_${index.toString().padStart(4, "0")}.webp`;

export default function TerminalExperience() {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [progress, setProgress] = useState(0);
  const [loaded, setLoaded] = useState(false);
  const imagesRef = useRef<HTMLImageElement[]>([]);

  useEffect(() => {
    let count = 0;
    const images: HTMLImageElement[] = [];

    for (let i = 1; i <= TOTAL_FRAMES; i++) {
      const img = new Image();
      img.src = getFramePath(i);
      img.onload = () => {
        count++;
        setProgress(Math.round((count / TOTAL_FRAMES) * 100));
        if (count === TOTAL_FRAMES) {
          imagesRef.current = images;
          setLoaded(true);
        }
      };
      images.push(img);
    }
  }, []);

  useEffect(() => {
    if (!loaded || !canvasRef.current || !containerRef.current) return;

    const canvas = canvasRef.current;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const render = (idx: number) => {
      const img = imagesRef.current[Math.floor(idx)];
      if (!img) return;

      const ratio = Math.max(canvas.width / img.width, canvas.height / img.height);
      const shiftX = (canvas.width - img.width * ratio) / 2;
      const shiftY = (canvas.height - img.height * ratio) / 2;

      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0, img.width, img.height, shiftX, shiftY, img.width * ratio, img.height * ratio);
    };

    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
    render(0);

    const frameObj = { frame: 0 };
    gsap.to(frameObj, {
      frame: TOTAL_FRAMES - 1,
      ease: "none",
      scrollTrigger: {
        trigger: containerRef.current,
        start: "top top",
        end: "bottom bottom",
        scrub: 0.2,
      },
      onUpdate: () => render(frameObj.frame),
    });

    return () => {
      ScrollTrigger.getAll().forEach((t) => t.kill());
    };
  }, [loaded]);

  return (
    <div ref={containerRef} className="relative h-[450vh] bg-black">
      {!loaded && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black text-white">
          <p className="font-mono text-sm">PRELOADING: {progress}%</p>
        </div>
      )}
      <div className="sticky top-0 h-screen w-full flex items-center justify-center overflow-hidden">
        <canvas ref={canvasRef} className="w-full h-full object-cover" />
      </div>
    </div>
  );
}
```
