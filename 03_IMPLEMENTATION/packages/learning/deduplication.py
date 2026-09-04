import uuid
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .semantic import SemanticProvider
from .tool_router import ToolRouter
from .version import parse_technology_version, TechnologyIdentity, VersionRange

def extract_tech_and_version(note: Dict[str, Any]):
    version_str = note.get('version_range') or ""
    applies_to = note.get('applies_to') or ""
    
    # Try parsing version_range first
    tech, vr = parse_technology_version(version_str)
    if tech.name != "unknown" and not vr.unknown:
        return tech, vr
        
    # If not fully resolved, try combining applies_to and version_str
    combined = f"{applies_to} {version_str}".strip()
    tech, vr = parse_technology_version(combined)
    return tech, vr

class Deduplicator:
    """
    BRAIN-14: Memory Deduplication.
    Scans for duplicate memories and flags them for review.
    Never automatically deletes human-verified memories.
    All write operations go through ToolRouter.
    """
    def __init__(self, memory_controller: MemoryController, semantic_provider: SemanticProvider, tool_router: ToolRouter):
        self.controller = memory_controller
        self.semantic_provider = semantic_provider
        self.router = tool_router
        self.similarity_threshold = 0.85
        
    def scan_for_duplicates(self, principal: Principal, query: str = "") -> List[str]:
        """
        Retrieves a set of nodes and checks for semantic duplicates.
        Returns a list of IDs flagged as duplicates.
        """
        pack = self.controller.search(principal, query, page_size=20)
        candidates = pack.get("results", [])
        
        flagged_ids = []
        
        for i in range(len(candidates)):
            for j in range(i + 1, len(candidates)):
                node_a = candidates[i]
                node_b = candidates[j]
                
                if node_a.get("type") != node_b.get("type"):
                    continue
                
                # Different source tiers (source_type) MUST remain separate.
                source_a = node_a.get("provenance", {}).get("source_type")
                source_b = node_b.get("provenance", {}).get("source_type")
                if not source_a or not source_b or source_a != source_b:
                    continue
                
                # Extract technology/product identity and version range
                tech_a, vr_a = extract_tech_and_version(node_a)
                tech_b, vr_b = extract_tech_and_version(node_b)
                
                # Unknown versions/technologies must never cause destructive overlap (do not deduplicate)
                if tech_a.name == "unknown" or tech_b.name == "unknown":
                    continue
                if vr_a.unknown or vr_b.unknown:
                    continue
                
                # Different technology versions / products must remain separate
                if tech_a.name != tech_b.name or vr_a != vr_b:
                    continue
                    
                sim = self.semantic_provider.compute_similarity(
                    node_a.get("content", ""),
                    node_b.get("content", "")
                )
                
                if sim >= self.similarity_threshold:
                    note_id = str(uuid.uuid4())
                    content = (
                        f"Potential duplicate detected between {node_a.get('id')} and {node_b.get('id')}.\n"
                        f"Similarity score: {sim:.2f}\n"
                        "Please review and archive one if appropriate."
                    )
                    
                    note = {
                        "id": note_id,
                        "type": "hypothesis",
                        "lifecycle": Lifecycle.REVIEW.value,
                        "category": "deduplication",
                        "confidence": "high",
                        "verification": "unverified",
                        "provenance": {"source_type": "inference", "source_ref": "deduplicator"},
                        "content": content,
                        "relations": [
                            {"target_id": node_a.get("id"), "type": "related_to"},
                            {"target_id": node_b.get("id"), "type": "related_to"}
                        ]
                    }
                    
                    # Propose through ToolRouter
                    self.router.execute(principal, "propose", {"note_data": note})
                    flagged_ids.append(note_id)
                    
        return flagged_ids

