from typing import List, Dict, Any

class Synapse:
    """
    Represents a directed relationship between two Memory Objects.
    """
    def __init__(self, source_id: str, target_id: str, relation_type: str, confidence: str = "unknown"):
        self.source_id = source_id
        self.target_id = target_id
        self.relation_type = relation_type
        self.confidence = confidence
        
    def __repr__(self) -> str:
        return f"Synapse(source={self.source_id}, target={self.target_id}, type={self.relation_type})"

class SynapticGraph:
    """
    Ephemeral graph layer extracting synapses from Memory Objects.
    Does not create a secondary persistent model.
    """
    @staticmethod
    def extract_synapses(memory_object: Dict[str, Any]) -> List[Synapse]:
        """
        Derives graph edges from existing Memory Object 'relations'.
        """
        source_id = memory_object.get("id")
        if not source_id:
            return []
            
        synapses = []
        relations = memory_object.get("relations", [])
        
        # Guard against None if relations is null in YAML
        if not relations:
            return synapses
            
        for rel in relations:
            if isinstance(rel, dict):
                target_id = rel.get("target_id")
                # Fallback to target if target_id missing but target is a uuid-like or string
                if not target_id:
                    target_str = rel.get("target", "")
                    # Extract possible UUID from wikilink if present
                    import re
                    match = re.search(r'([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})', target_str)
                    if match:
                        target_id = match.group(1)
                
                rel_type = rel.get("type", "related_to")
                
                if target_id:
                    synapses.append(Synapse(source_id, target_id, rel_type))
        return synapses
