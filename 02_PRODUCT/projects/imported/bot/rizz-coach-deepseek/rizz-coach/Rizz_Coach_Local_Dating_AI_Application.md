---
title: Rizz Coach Local Dating AI Application
type: application
status: active
category: product
---

# Rizz Coach — MVP Local

Coach AI de conversații dating. Rulează local pe PC și mobil (same network).

## Stack
- Backend: Node.js + Express
- Frontend: Vanilla HTML/JS (funcționează pe orice browser/mobil)
- AI: OpenAI API (GPT-4o-mini — ieftin și rapid)

## Setup

```bash
npm install
cp .env.example .env
# Adaugă OPENAI_API_KEY în .env
npm start
```

Accesează:
- PC: http://localhost:3000
- Mobil (aceeași rețea): http://IP_PC_TAU:3000

## Utilizare
1. Scrii mesajul primit sau trimis
2. Alegi contextul (mesaj primit / mesaj trimis)
3. Primești 2-3 variante de răspuns + recomandare
