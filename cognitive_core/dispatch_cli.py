import sys
import os
import argparse
import json

# Asiguram ca radacina proiectului este in PYTHONPATH
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from cognitive_core.orchestrator import MultiAgentDispatcher

def main():
    parser = argparse.ArgumentParser(description="Dispecer Distribuit de Calcul pentru Claude Code & Agenti Locali")
    parser.add_argument("--role", choices=["coder", "critic", "memory", "router"], default="coder", help="Rolul agentului")
    parser.add_argument("--node", choices=["auto", "kaggle", "colab", "local"], default="auto", help="Nodul de calcul tinta")
    parser.add_argument("--prompt", required=True, help="Promptul de sarcina pentru LLM")
    parser.add_argument("--system", default="", help="System prompt optional")

    args = parser.parse_args()

    dispatcher = MultiAgentDispatcher()

    # Daca utilizatorul a cerut un nod explicit (kaggle/colab/local), suprascriem prioritatea
    if args.node != "auto":
        nodes = dispatcher.config.get("nodes", {})
        if args.node in nodes:
            # Setam temporar prioritatea maxima pentru nodul ales
            for k in nodes:
                nodes[k]["enabled"] = (k == args.node)

    system_prompt = args.system or f"You are an expert {args.role} specialized in quantitative engineering, systems programming, and high-performance algorithms."
    
    active_url, model_name = dispatcher._get_active_node_and_model(args.role)
    print(f"[*] Dispatching task to [{args.node.upper()}] Node: {active_url} (Model: {model_name})", file=sys.stderr)

    try:
        response = dispatcher.dispatch(
            agent_role=args.role,
            system_prompt=system_prompt,
            user_input=args.prompt
        )
        print(response)
    except Exception as e:
        print(f"[ERROR] Failed to execute task on remote node: {e}", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
