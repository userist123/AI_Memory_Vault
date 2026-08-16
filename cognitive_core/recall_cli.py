import os
import sys
import argparse
import re

# Asiguram ca radacina proiectului este in PYTHONPATH
VAULT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, VAULT_ROOT)

def search_markdown_vault(query: str, max_results: int = 5) -> list:
    """Scaneaza fisierele canonice din Vault (00_CORE, 01_KNOWLEDGE, 02_PROJECTS, 03_PROCEDURES, 04_MEMORY)"""
    folders_to_scan = [
        "02_PROJECTS",
        "01_KNOWLEDGE",
        "03_PROCEDURES",
        "04_MEMORY",
        "00_CORE"
    ]
    
    query_terms = [t.lower() for t in query.split() if len(t) > 2]
    if not query_terms:
        query_terms = [query.lower()]

    results = []

    for folder in folders_to_scan:
        folder_path = os.path.join(VAULT_ROOT, folder)
        if not os.path.exists(folder_path):
            continue

        for root, _, files in os.walk(folder_path):
            for file in files:
                if not file.endswith(".md"):
                    continue
                
                filepath = os.path.join(root, file)
                try:
                    with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
                        content = f.read()

                    rel_path = os.path.relpath(filepath, VAULT_ROOT)
                    lower_content = content.lower()
                    lower_name = file.lower()

                    # Calculam relevanta
                    score = 0
                    for term in query_terms:
                        if term in lower_name:
                            score += 10
                        score += lower_content.count(term)

                    if score > 0:
                        results.append({
                            "file": rel_path,
                            "score": score,
                            "content": content[:1500]  # primele 1500 caractere pentru context
                        })
                except Exception:
                    pass

    # Sortam dupa relevanta
    results.sort(key=lambda x: x["score"], reverse=True)
    return results[:max_results]

def main():
    parser = argparse.ArgumentParser(description="Cautare si Extragere Memorie din Vault pentru Claude Code")
    parser.add_argument("--query", required=True, help="Termenul sau intrebarea pentru cautare in memorie")
    parser.add_argument("--max", type=int, default=3, help="Numar maxim de notite returnate")

    args = parser.parse_args()

    matches = search_markdown_vault(args.query, max_results=args.max)

    if not matches:
        print(f"[*] Nu s-au gasit notite relevante in Vault pentru interogarea: '{args.query}'")
        return

    print("="*60)
    print(f"[*] MEMORIE VAULT GASITA PENTRU: '{args.query}' ({len(matches)} rezultate)")
    print("="*60)

    for i, m in enumerate(matches, 1):
        print(f"\n--- [Notita {i}: {m['file']} (Relevanta: {m['score']})] ---")
        print(m['content'])
        print("-" * 50)

if __name__ == "__main__":
    main()
