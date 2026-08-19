import sys, os
sys.path.insert(0, os.path.abspath("."))
from cognitive_core.orchestrator import MultiAgentDispatcher

print("="*60)
print("[TEST] Apel Distribuit catre Nodul Kaggle GPU...")
print("="*60)

dispatcher = MultiAgentDispatcher()
role = "coder"
active_url = dispatcher._get_active_node_url(role)
model_name = dispatcher.models.get(role)

print(f"[*] Endpoint Tinta: {active_url}")
print(f"[*] Model Tinta:    {model_name}")
print("\nTrimitem promptul de test catre GPU...")

system_prompt = "You are an expert high-performance quantitative systems engineer."
user_prompt = "Write a clean, optimized Python function to compute the Exponential Moving Average (EMA) of a NumPy array with alpha parameter. Include docstring and type hints."

try:
    response = dispatcher.dispatch(
        agent_role=role,
        system_prompt=system_prompt,
        user_input=user_prompt
    )
    print("\n" + "="*60)
    print("[SUCCESS] RASPUNS PRIMIT DE LA GPU KAGGLE (qwen2.5-coder:32b):")
    print("="*60)
    print(response)
    print("="*60)
except Exception as e:
    print(f"\n[ERROR] Eroare la executie: {e}")
