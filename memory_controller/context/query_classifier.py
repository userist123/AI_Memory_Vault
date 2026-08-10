from enum import Enum
from typing import List, Dict, Any

class Intent(Enum):
    READ = "read"
    SEARCH = "search"
    PROPOSE = "propose"
    UPDATE = "update"
    REVIEW = "review"
    PROMOTE = "promote"
    ARCHIVE = "archive"

class QueryClassifier:
    """Classify a raw query string into intent and target memory types.

    This is a rule‑based lightweight classifier; can be extended with
    LLM‑based intent detection later.
    """

    def __init__(self, intent_map: Dict[str, Intent] = None):
        # Simple keyword mapping; defaults cover main operations.
        self.intent_map = intent_map or {
            "read": Intent.READ,
            "search": Intent.SEARCH,
            "propose": Intent.PROPOSE,
            "update": Intent.UPDATE,
            "review": Intent.REVIEW,
            "promote": Intent.PROMOTE,
            "archive": Intent.ARCHIVE,
        }

    def classify(self, query: str) -> Dict[str, Any]:
        """Return a dict with intent, target_types, lifecycle_filters, confidence.

        - intent: inferred Intent enum (default READ)
        - target_types: list of memory types (knowledge, project, …)
        - lifecycle_filters: optional list of lifecycle stages to limit
        - confidence: soft estimate (0‑1) based on keyword match count
        """
        lowered = query.lower()
        # Determine intent by first matching keyword.
        intent = Intent.READ
        for kw, val in self.intent_map.items():
            if kw in lowered:
                intent = val
                break
        # Very naive extraction of target types – look for known nouns.
        target_types = []
        for t in ["knowledge", "project", "procedure", "decision", "error", "lesson", "experience", "resource", "hypothesis"]:
            if t in lowered:
                target_types.append(t)
        # Lifecycle filters – e.g., "active", "verified".
        lifecycle_filters = []
        for stage in ["raw", "classified", "normalized", "review", "verified", "active", "superseded", "archived"]:
            if stage in lowered:
                lifecycle_filters.append(stage.upper())
        confidence = 0.9 if intent != Intent.READ else 0.5
        return {
            "intent": intent,
            "target_types": target_types,
            "lifecycle_filters": lifecycle_filters,
            "confidence": confidence,
        }
