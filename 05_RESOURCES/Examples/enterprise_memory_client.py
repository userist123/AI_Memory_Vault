"""
Enterprise Memory Client Wrapper Example for AI Memory Vault v6.0.0.

Shows how any enterprise python backend (FastAPI, Flask, Celery, ARQ)
connects to the AI Memory Vault for sub-10ms memory retrieval, ranked search,
and Memory V6 atomic extraction.
"""

import sys
import os

# Auto-add project root to sys.path
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.ranked_search import ranked_search
from cognitive_core.sensor_buffer import SensorBuffer
from cognitive_core.extraction import AtomicMemoryExtractor
from cognitive_core.proposal_queue import MemoryProposalQueue

class EnterpriseMemoryClient:
    """Production SDK wrapper for integrating AI Memory Vault in large-scale projects."""

    def __init__(self, vault_root: str = PROJECT_ROOT):
        self.vault_root = vault_root
        self.storage = FileStorageEngine(self.vault_root)
        self.controller = MemoryController(self.storage)
        self.sensor_buffer = SensorBuffer(max_events_per_session=100, ttl_minutes=120)
        self.extractor = AtomicMemoryExtractor()
        self.proposal_queue = MemoryProposalQueue(
            queue_path=os.path.join(self.vault_root, "06_INBOX", "memory_v6_proposals.jsonl")
        )

    def query_memory(self, query_text: str, top_k: int = 5, principal: Principal = Principal.AI_AGENT):
        """High-performance ranked search combining BM25, Cosine similarity, and ACT-R Spreading Activation."""
        return ranked_search(
            controller=self.controller,
            principal=principal,
            query=query_text,
            top_k=top_k
        )

    def capture_session_telemetry(self, session_id: str, agent_id: str, role: str, content: str):
        """Captures raw session telemetry into the ephemeral sensor buffer."""
        return self.sensor_buffer.append(session_id=session_id, agent_id=agent_id, role=role, content=content)

    def extract_and_enqueue_facts(self, raw_text: str, source_ref: str = "enterprise_app"):
        """Extracts facts deterministically and enqueues into 06_INBOX proposal queue."""
        candidates = self.extractor.extract(raw_text, source_ref=source_ref)
        if candidates:
            return self.proposal_queue.enqueue(candidates)
        return 0


if __name__ == "__main__":
    client = EnterpriseMemoryClient()
    print("[ENTERPRISE CLIENT] Querying AI Memory Vault...")
    results = client.query_memory("cognitive architecture v6.0.0", top_k=3)
    print(f"[ENTERPRISE CLIENT] Found {len(results)} ranked memory notes:")
    for idx, res in enumerate(results, 1):
        note = res.get("note", {})
        print(f"  {idx}. [{note.get('type')}] {note.get('id')} — {note.get('category')} (Score: {res.get('score', 0):.3f})")
