import os
import sys
import argparse
from typing import List, Dict, Any, Optional

# Asiguram ca radacina proiectului este in PYTHONPATH
VAULT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
if VAULT_ROOT not in sys.path:
    sys.path.insert(0, VAULT_ROOT)

MIN_HMAC_SECRET_LENGTH = 32

class MissingHMACSecretError(ValueError):
    """Raised when MEMORY_CONTROLLER_HMAC_SECRET is missing from the environment."""
    pass

class InvalidHMACSecretError(ValueError):
    """Raised when MEMORY_CONTROLLER_HMAC_SECRET is present but invalid or too short."""
    pass

def validate_hmac_secret() -> str:
    """Validates that MEMORY_CONTROLLER_HMAC_SECRET is present in the environment and valid.

    A1: If present and valid (>= 32 chars): returns the secret string.
    A2: If missing: fails closed with MissingHMACSecretError.
    A3: If invalid / too short (< 32 chars): fails closed with InvalidHMACSecretError.
    A4: No hardcoded fallback or invented secret.
    """
    secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
    if not secret:
        raise MissingHMACSecretError(
            "MEMORY_CONTROLLER_HMAC_SECRET environment variable is missing. "
            "Please set MEMORY_CONTROLLER_HMAC_SECRET with at least 32 characters."
        )
    if len(secret.strip()) < MIN_HMAC_SECRET_LENGTH:
        raise InvalidHMACSecretError(
            f"MEMORY_CONTROLLER_HMAC_SECRET is invalid: must be at least {MIN_HMAC_SECRET_LENGTH} characters "
            f"(got {len(secret.strip())})."
        )
    return secret

from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine

_CACHED_CONTROLLER: Optional[MemoryController] = None

def get_memory_controller() -> MemoryController:
    """Initializeaza si returneaza instanta securizata de MemoryController."""
    global _CACHED_CONTROLLER
    if _CACHED_CONTROLLER is not None:
        return _CACHED_CONTROLLER

    db_path = os.path.join(VAULT_ROOT, "vault_memory.sqlite3")
    if os.path.exists(db_path):
        try:
            storage = SQLiteStorageEngine(db_path, wal_mode=True)
            _CACHED_CONTROLLER = MemoryController(storage)
            return _CACHED_CONTROLLER
        except Exception:
            pass

    # Fallback catre FileStorageEngine care scaneaza folderele canonice
    storage = FileStorageEngine(VAULT_ROOT)
    _CACHED_CONTROLLER = MemoryController(storage)
    return _CACHED_CONTROLLER

def search_markdown_vault(
    query: str,
    max_results: int = 5,
    principal: Principal = Principal.AI_AGENT,
    controller: Optional[MemoryController] = None
) -> List[Dict[str, Any]]:
    """Cautare securizata in memoria Vault delegand catre MemoryController.search().
    
    Aplica automat verificările si granițele de încredere P0-P15:
    - Verificarea permisiunilor Principal.AI_AGENT (Least Privilege)
    - Sanitizarea interogarii si limitarea dimensiunii (check_query_size / sanitize_query)
    - Excluderea stricta a notelor nevalidate din stadiul RAW
    - Calculul relevantei si filtrarea prin bugetul de context (ContextBudget)
    - Jurnalizare criptografica tamper-evidenta SHA-256 (audit logging)
    """
    # Enforce strict HMAC secret validation (fails closed if missing or invalid)
    validate_hmac_secret()

    ctrl = controller or get_memory_controller()

    
    # Delegare directa catre pipeline-ul protejat MemoryController.search()
    pack = ctrl.search(principal=principal, query=query, page_size=max_results)

    raw_results = pack.get("results", [])
    formatted_results = []
    
    for item in raw_results:
        source_file = item.get("source_ref") or item.get("id") or "unknown"
        if os.path.isabs(source_file) and source_file.startswith(VAULT_ROOT):
            source_file = os.path.relpath(source_file, VAULT_ROOT)
            
        content = item.get("content", "")
        if not content:
            meta_parts = []
            if item.get("id"):
                meta_parts.append(f"ID: {item['id']}")
            if item.get("type"):
                meta_parts.append(f"Type: {item['type']}")
            if item.get("category"):
                meta_parts.append(f"Category: {item['category']}")
            if item.get("tags"):
                meta_parts.append(f"Tags: {', '.join(item['tags'])}")
            content = "\n".join(meta_parts)
            
        score = item.get("relevance_score") or item.get("score") or 1.0
        
        formatted_results.append({
            "id": item.get("id", "unknown"),
            "file": source_file,
            "score": score,
            "lifecycle": item.get("lifecycle", "ACTIVE"),
            "type": item.get("type", "knowledge"),
            "verification": item.get("verification", "unverified"),
            "content": content[:1500]
        })
        
    return formatted_results[:max_results]

def main():
    parser = argparse.ArgumentParser(description="Cautare Securizata si Extragere Memorie din Vault (P0-P15 Gated)")
    parser.add_argument("--query", required=True, help="Termenul sau intrebarea pentru cautare in memorie")
    parser.add_argument("--max", type=int, default=3, help="Numar maxim de notite returnate")

    args = parser.parse_args()

    try:
        matches = search_markdown_vault(args.query, max_results=args.max)
    except Exception as e:
        print(f"[!] Eroare la interogarea securizata a memoriei: {e}", file=sys.stderr)
        sys.exit(1)

    if not matches:
        print(f"[*] Nu s-au gasit notite relevante in Vault pentru interogarea: '{args.query}'")
        return

    print("="*60)
    print(f"[*] MEMORIE VAULT SECURIZATA PENTRU: '{args.query}' ({len(matches)} rezultate)")
    print("="*60)

    for i, m in enumerate(matches, 1):
        print(f"\n--- [Notita {i}: {m['file']} (Relevanta: {m['score']}, Tip: {m['type']}, Lifecycle: {m['lifecycle']})] ---")
        print(m['content'])
        print("-" * 50)

if __name__ == "__main__":
    main()
