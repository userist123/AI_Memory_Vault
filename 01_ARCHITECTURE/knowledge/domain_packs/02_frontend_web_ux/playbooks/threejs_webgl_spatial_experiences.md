---
id: "31826f4b-4ca1-4e3f-975a-20fa13f50ed5"
type: knowledge
lifecycle: REVIEW
category: threejs_3d
tags:
  - threejs_3d
  - frontend-web-ux
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: Grafică 3D, Three.js și Shadere WebGL

Ghid de decizie și reguli operaționale pentru `threejs_3d`.

## Reguli de Decizie
1. Gestionează ciclul de viață al resurselor: apelează .dispose() pe geometrii, materiale și texturi la unmount.
2. Limitează rata de eșantionare a rezoluției (DPR) la maxim 2.0 pentru a preveni căderile dramatice de FPS pe mobile.
3. Optimizează shaderele GLSL eliminând ramificațiile costisitoare în fragment shaders.

## Capcane de Evitat
- Nu aloca geometrii, materiale sau vectori noi în interiorul buclei requestAnimationFrame.
- Nu încărca texturi necomprimate de dimensiuni mari (> 2048x2048) pe conexiuni mobile.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
