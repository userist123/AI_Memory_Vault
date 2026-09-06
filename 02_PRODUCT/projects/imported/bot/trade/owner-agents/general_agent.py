"""
GENERAL DEV AGENT - Python, scripturi, automatizare, debugging
"""
import os, sys

sys.path.insert(0, os.path.dirname(__file__))
from base_agent import run_agent

SYSTEM_PROMPT = """You are a versatile senior software engineer who can handle any programming task.

Your expertise includes:
- Python: advanced Python, data processing, automation, web scraping
- Scripting: bash, batch, automation pipelines
- Data: pandas, CSV/Excel processing, JSON/XML manipulation, SQLite
- APIs: consuming REST APIs, building simple HTTP clients, OAuth
- File processing: PDF manipulation, image processing, text parsing
- Web scraping: requests, BeautifulSoup, selenium when needed
- Utilities: cron jobs, schedulers, file watchers, notification systems
- Debugging: analyzing error logs, fixing broken code, optimizing slow scripts

When building tools:
1. Write clean, Pythonic code following PEP 8
2. Add proper CLI arguments using argparse when appropriate
3. Include requirements.txt for dependencies
4. Add meaningful docstrings and comments
5. Handle edge cases and errors gracefully
6. Make scripts reusable and configurable
7. Test with sample data and verify output

If debugging existing code: read it first, understand the issue, fix it, explain what was wrong.
Always write complete, executable code to files."""

if __name__ == "__main__":
    print("\n" + "="*55)
    print("  GENERAL DEV AGENT - Gemini 2.5 Flash")
    print("  Python, Automatizare, Debugging, Scripturi")
    print("="*55)
    print("  Exemple:")
    print("    > Script care parseaza CSV-uri si trimite raport pe email")
    print("    > Web scraper pentru preturi produse cu Excel export")
    print("    > Automatizare descarcari fisiere din FTP cu logging")
    print("    > Debug-uieste acest error: [paste error]\n")

    work_dir = os.getcwd()

    if len(sys.argv) > 1:
        run_agent(" ".join(sys.argv[1:]), SYSTEM_PROMPT, work_dir=work_dir)
    else:
        while True:
            try:
                task = input(f"[General | {os.path.basename(work_dir)}]> ").strip()
                if not task: continue
                if task.lower() in ("exit", "quit"): break
                if task.lower().startswith("dir "):
                    d = task[4:].strip()
                    if os.path.isdir(d): work_dir = os.path.abspath(d)
                    continue
                run_agent(task, SYSTEM_PROMPT, work_dir=work_dir)
            except KeyboardInterrupt:
                break
