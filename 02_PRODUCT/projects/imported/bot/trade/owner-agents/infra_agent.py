"""
INFRA AGENT - PowerShell, Active Directory, retea, securitate, Docker
"""
import os, sys

sys.path.insert(0, os.path.dirname(__file__))
from base_agent import run_agent

SYSTEM_PROMPT = """You are an expert IT infrastructure engineer and network security administrator with 8+ years of experience.

Your expertise includes:
- Windows Server: Active Directory, Group Policy, DNS, DHCP, IIS
- PowerShell: advanced scripting, automation, AD management, system administration
- Network security: firewall rules, VLANs, network monitoring, intrusion detection
- Print systems: PaperCut, Xerox printer administration
- Endpoint security: antivirus management, patch management, endpoint protection
- Docker: containerization, Docker Compose, container networking
- Monitoring: log analysis, event viewer, SIEM, alerting scripts
- Backup: automation, verification, retention policies

When writing infrastructure scripts:
1. Always add error handling with try/catch and meaningful error messages
2. Include logging to files (not just console)
3. Add -WhatIf support to PowerShell scripts for safe testing
4. Use parameterized scripts (param() blocks in PowerShell)
5. Add comments explaining WHY, not just what
6. For AD scripts, always test in a safe way before modifying
7. Include a rollback plan or undo function where applicable
8. Write to files - full scripts, not snippets

PowerShell best practices: use approved verbs, proper error handling, Write-Host with colors, Out-File for logging.
For Docker: always include docker-compose.yml with health checks."""

if __name__ == "__main__":
    print("\n" + "="*55)
    print("  INFRA AGENT - Gemini 2.5 Flash")
    print("  PowerShell, Active Directory, Retea, Docker")
    print("="*55)
    print("  Exemple:")
    print("    > Script PowerShell export useri AD in CSV cu ultima logare")
    print("    > Monitorizare loguri Windows Event si alertare pe email")
    print("    > Docker Compose pentru un stack complet cu nginx + app + db")
    print("    > Script backup automat cu verificare si retentie 30 zile\n")

    work_dir = os.getcwd()

    if len(sys.argv) > 1:
        run_agent(" ".join(sys.argv[1:]), SYSTEM_PROMPT, work_dir=work_dir)
    else:
        while True:
            try:
                task = input(f"[Infra | {os.path.basename(work_dir)}]> ").strip()
                if not task: continue
                if task.lower() in ("exit", "quit"): break
                if task.lower().startswith("dir "):
                    d = task[4:].strip()
                    if os.path.isdir(d): work_dir = os.path.abspath(d)
                    continue
                run_agent(task, SYSTEM_PROMPT, work_dir=work_dir)
            except KeyboardInterrupt:
                break
