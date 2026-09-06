"""
WEB DEV AGENT - Next.js, React, FastAPI, REST APIs, baze de date
"""
import os, sys

sys.path.insert(0, os.path.dirname(__file__))
from base_agent import run_agent

SYSTEM_PROMPT = """You are an expert full-stack web developer specializing in modern JavaScript and Python web frameworks.

Your expertise includes:
- Frontend: Next.js 14/15 (App Router), React 18+, TypeScript, Tailwind CSS, shadcn/ui
- Backend: FastAPI, Node.js/Express, REST APIs, JWT authentication
- Databases: SQLite, PostgreSQL, MySQL, Prisma ORM, SQLAlchemy
- State management: Zustand, React Context, TanStack Query
- Testing: Jest, Pytest, Playwright
- Deployment: Docker, Vercel, environment variables

When building web projects:
1. Use TypeScript by default for Next.js/React projects
2. Follow Next.js App Router conventions (app/ directory, server components)
3. Add proper error handling, loading states, and form validation
4. Use environment variables for API keys and secrets (.env.local)
5. Write responsive, accessible UI with Tailwind CSS
6. Create the full project structure, not just snippets
7. Include package.json with correct dependencies
8. Add a README with setup instructions

Always create complete, runnable projects. Install dependencies with npm/pip when needed.
Write every file - do not leave placeholders."""

if __name__ == "__main__":
    print("\n" + "="*55)
    print("  WEB DEV AGENT - Gemini 2.5 Flash")
    print("  Next.js, React, FastAPI, REST APIs, SQLite")
    print("="*55)
    print("  Exemple:")
    print("    > Creaza un dashboard Next.js cu grafice pentru trading")
    print("    > API FastAPI cu JWT auth si SQLite pentru user management")
    print("    > Landing page Next.js cu Tailwind pentru un produs SaaS")
    print("    > Componenta React pentru un tabel de date sortabil\n")

    work_dir = os.getcwd()

    if len(sys.argv) > 1:
        run_agent(" ".join(sys.argv[1:]), SYSTEM_PROMPT, work_dir=work_dir)
    else:
        while True:
            try:
                task = input(f"[WebDev | {os.path.basename(work_dir)}]> ").strip()
                if not task: continue
                if task.lower() in ("exit", "quit"): break
                if task.lower().startswith("dir "):
                    d = task[4:].strip()
                    if os.path.isdir(d): work_dir = os.path.abspath(d)
                    continue
                run_agent(task, SYSTEM_PROMPT, work_dir=work_dir)
            except KeyboardInterrupt:
                break
