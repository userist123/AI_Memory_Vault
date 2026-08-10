import re
from typing import List, Dict, Any, Tuple
from abc import ABC, abstractmethod

class SemanticProvider(ABC):
    """
    Abstraction for semantic similarity and embedding operations.
    Allows swapping a mock/deterministic provider with a real embedding model later.
    """
    @abstractmethod
    def compute_similarity(self, text_a: str, text_b: str) -> float:
        """Computes similarity score between 0.0 and 1.0"""
        pass

class DeterministicSemanticProvider(SemanticProvider):
    """
    Dependency-free, deterministic mock provider for associative recall testing.
    Uses basic word overlap (Jaccard similarity) instead of embeddings.
    """
    def _tokenize(self, text: str) -> set:
        if not text:
            return set()
        # Simple lowercase alphanumeric tokenization
        words = re.findall(r'\w+', text.lower())
        return set(words)
        
    def compute_similarity(self, text_a: str, text_b: str) -> float:
        set_a = self._tokenize(text_a)
        set_b = self._tokenize(text_b)
        
        if not set_a or not set_b:
            return 0.0
            
        intersection = set_a.intersection(set_b)
        union = set_a.union(set_b)
        return len(intersection) / len(union)
