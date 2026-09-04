import re
from enum import Enum
from typing import Any, Dict


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

    This is a rule-based lightweight classifier; can be extended with
    LLM-based intent detection later.
    """

    def __init__(self, intent_map: Dict[str, Intent] | None = None):
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
        """Return intent, target types, lifecycle filters, and confidence.

        Lifecycle stages are matched as whole words so terms such as
        ``unverified`` do not accidentally imply the ``VERIFIED`` stage.
        """
        lowered = query.lower()

        intent = Intent.READ
        for kw, val in self.intent_map.items():
            if re.search(rf"\b{re.escape(kw)}\b", lowered):
                intent = val
                break

        target_types = []
        for target_type in [
            "knowledge",
            "project",
            "procedure",
            "decision",
            "error",
            "lesson",
            "experience",
            "resource",
            "hypothesis",
        ]:
            if re.search(rf"\b{re.escape(target_type)}\b", lowered):
                target_types.append(target_type)

        lifecycle_filters = []
        for stage in [
            "raw",
            "classified",
            "normalized",
            "review",
            "verified",
            "active",
            "superseded",
            "archived",
        ]:
            if re.search(rf"\b{re.escape(stage)}\b", lowered):
                lifecycle_filters.append(stage.upper())

        confidence = 0.9 if intent != Intent.READ else 0.5
        return {
            "intent": intent,
            "target_types": target_types,
            "lifecycle_filters": lifecycle_filters,
            "confidence": confidence,
        }
