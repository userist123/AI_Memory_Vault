"""
ORCHESTRATOR - Analizeaza task-ul si il trimite la agentul potrivit
"""
import os, sys, subprocess

AGENTS = {
    "1": ("Trading Agent",     "trading_agent.py",   "Trading bots, RSI/MACD/strategii, backtesting, Binance API, crypto/forex"),
    "2": ("Web Dev Agent",     "webdev_agent.py",    "Next.js, React, FastAPI, REST APIs, baze de date, HTML/CSS/JS"),
    "3": ("Infra Agent",       "infra_agent.py",     "PowerShell, Active Directory, retea, securitate, Docker, automatizare sistem"),
    "4": ("General Dev Agent", "general_agent.py",   "Python, scripturi generale, automatizare, debugging, orice altceva"),
}

BANNER = """
╔══════════════════════════════════════════════════════════╗
║           OWNER AI AGENT TEAM - Powered by Gemini       ║
╠══════════════════════════════════════════════════════════╣
║  1. Trading Agent     - bots, strategii, backtesting     ║
║  2. Web Dev Agent     - Next.js, React, FastAPI, APIs    ║
║  3. Infra Agent       - PowerShell, AD, retea, Docker    ║
║  4. General Dev Agent - Python, scripturi, debugging     ║
║  0. Iesire                                               ║
╚══════════════════════════════════════════════════════════╝
"""

def auto_detect_agent(task: str) -> str:
    task_lower = task.lower()
    trading_kw = ["trading", "bot", "rsi", "macd", "sma", "ema", "binance", "crypto", "forex",
                  "backtest", "strategy", "candlestick", "indicator", "ohlcv", "stock", "price",
                  "signal", "trade", "market", "order", "profit", "loss", "portfolio"]
    web_kw = ["next.js", "nextjs", "react", "fastapi", "flask", "django", "api", "endpoint",
              "html", "css", "javascript", "typescript", "component", "frontend", "backend",
              "database", "sql", "sqlite", "postgres", "mysql", "rest", "json", "http"]
    infra_kw = ["powershell", "active directory", "ad", "network", "firewall", "dns", "dhcp",
                "docker", "container", "server", "backup", "log", "monitor", "windows", "linux",
                "security", "certificate", "vpn", "switch", "vlan", "ip", "port", "service"]

    scores = {
        "1": sum(1 for kw in trading_kw if kw in task_lower),
        "2": sum(1 for kw in web_kw if kw in task_lower),
        "3": sum(1 for kw in infra_kw if kw in task_lower),
        "4": 0
    }

    best = max(scores, key=lambda k: scores[k])
    return best if scores[best] > 0 else "4"

def run_agent(agent_num: str, task: str = ""):
    name, script, _ = AGENTS[agent_num]
    script_path = os.path.join(os.path.dirname(__file__), script)

    if not os.path.exists(script_path):
        print(f"  [EROARE] Script nu gasit: {script_path}")
        return

    print(f"\n  >> Pornesc {name}...")

    if task:
        subprocess.run([sys.executable, script_path, task])
    else:
        subprocess.run([sys.executable, script_path])

def main():
    print(BANNER)

    while True:
        try:
            print("\nScrie task-ul direct (auto-detectez agentul) sau alege manual (1-4):")
            user_input = input("> ").strip()

            if not user_input or user_input == "0":
                print("La revedere!")
                break

            # Alegere manuala
            if user_input in AGENTS:
                run_agent(user_input)
                continue

            # Auto-detectie
            detected = auto_detect_agent(user_input)
            name, _, desc = AGENTS[detected]
            print(f"\n  Auto-detect: [{detected}] {name}")
            print(f"  Motiv: {desc[:60]}...")
            confirm = input("  Continui cu acest agent? (Enter=DA / alt nr=schimba): ").strip()

            if confirm in AGENTS:
                run_agent(confirm, user_input)
            else:
                run_agent(detected, user_input)

        except KeyboardInterrupt:
            print("\n\nLa revedere!")
            break

if __name__ == "__main__":
    main()
