# overmartinez.com

Personal portfolio and case study showcase — built with Next.js, TypeScript, and Tailwind CSS.

**Live:** [overmartinez.com](https://www.overmartinez.com)

## About

This is my professional portfolio where I showcase selected projects and case studies from 10+ years of experience building web applications. The site is designed as a conversion tool — not just a gallery — with detailed case studies that explain the problems I solved, the technical decisions I made, and the outcomes delivered.

## Tech Stack

- **Framework:** Next.js 15 (App Router)
- **Language:** TypeScript
- **Styling:** Tailwind CSS
- **Deployment:** Vercel
- **Linting:** ESLint + Prettier

## Project Structure

```
src/
├── app/
│   ├── page.tsx              # Home (hero, work, about, contact)
│   ├── work/
│   │   └── [slug]/
│   │       └── page.tsx      # Case study template
│   └── layout.tsx
├── components/
│   ├── ui/                   # Reusable UI primitives
│   └── sections/             # Page-level sections
├── data/
│   └── projects.ts           # Case study content
└── lib/
    └── utils.ts
```

## Running Locally

```bash
git clone https://github.com/oversio/portfolio.git
cd portfolio
npm install
npm run dev
```

Open [http://localhost:3000](http://localhost:3000).

## License

Content and case studies are © Over Martinez. Code structure is available as reference.
