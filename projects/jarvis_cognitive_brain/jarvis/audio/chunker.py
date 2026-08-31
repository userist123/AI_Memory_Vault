"""
Streaming Sentence & Clause Chunker for Sub-300ms TTFB Speech Synthesis.
Processes token streams, performs text normalization, and emits synthesizable clauses.
"""

from typing import List, AsyncIterator, Optional
import re
from jarvis.llm.base import CancellationToken, CancellationError


class TextNormalizer:
    """
    Normalizes technical abbreviations, currency, temperatures, sample rates, and acronyms for natural TTS pronunciation.
    """

    @staticmethod
    def normalize(text: str) -> str:
        if not text:
            return ""

        res = text
        # Technical frequencies & sample rates
        res = re.sub(r"\b24\s*kHz\b", "twenty four kilohertz", res, flags=re.IGNORECASE)
        res = re.sub(r"\b16\s*kHz\b", "sixteen kilohertz", res, flags=re.IGNORECASE)
        res = re.sub(r"\b8\s*kHz\b", "eight kilohertz", res, flags=re.IGNORECASE)
        res = re.sub(r"\b48\s*kHz\b", "forty eight kilohertz", res, flags=re.IGNORECASE)

        # Percentages
        res = re.sub(r"(\d+)%", r"\1 percent", res)

        # Temperature expressions
        res = re.sub(r"(\d+)\s*(?:°C|deg C|degrees C)\b", r"\1 degrees Celsius", res)
        res = re.sub(r"(\d+)\s*°F\b", r"\1 degrees Fahrenheit", res)

        # Technical acronyms (spaced out for phoneme clarity)
        res = re.sub(r"\bIoT\b", "I o T", res)
        res = re.sub(r"\bSTT\b", "S T T", res)
        res = re.sub(r"\bTTS\b", "T T S", res)
        res = re.sub(r"\bVAD\b", "V A D", res)
        res = re.sub(r"\bOODA\b", "O O D A", res)
        res = re.sub(r"\bAPI\b", "A P I", res)
        res = re.sub(r"\bDAC\b", "D A C", res)
        res = re.sub(r"\bTTFB\b", "T T F B", res)
        res = re.sub(r"\bWAL\b", "W A L", res)
        res = re.sub(r"\bCTE\b", "C T E", res)

        # Whitespace normalization
        return re.sub(r"\s+", " ", res).strip()


class SentenceChunker:
    """
    Streaming text chunker that accumulates LLM token deltas and emits ready synthesizable chunks.
    Splits on sentence terminals immediately, and on clause punctuation when sufficient words have accumulated.
    """

    def __init__(
        self,
        clause_split: bool = True,
        min_clause_words: int = 4,
        max_buffer_words: int = 20,
    ):
        self.clause_split = clause_split
        self.min_clause_words = min_clause_words
        self.max_buffer_words = max_buffer_words
        self.buffer: str = ""

    def feed_token(self, token: str) -> List[str]:
        """
        Feed a single LLM token delta and return any completed synthesizable text chunks.
        """
        self.buffer += token
        ready_chunks: List[str] = []

        while True:
            cleaned_buf = re.sub(r"\s+", " ", self.buffer)
            if not cleaned_buf.strip():
                break

            # 1. Full sentence boundaries (. ! ? \n\n)
            match = re.search(r"^(.*?[.!?])(?:\s+|$)(.*)$", cleaned_buf, re.DOTALL)
            if match and match.group(1).strip():
                chunk_str = match.group(1).strip()
                # Check for abbreviation protection (e.g., "Mr.", "Dr.", "e.g.", "1.5")
                if not re.search(r"\b(?:Mr|Mrs|Ms|Dr|Prof|e\.g|i\.e|\d+\.\d+)\.$", chunk_str, re.IGNORECASE):
                    norm = TextNormalizer.normalize(chunk_str)
                    if norm:
                        ready_chunks.append(norm)
                    self.buffer = match.group(2)
                    continue

            # 2. Clause boundaries (, ; : \n) if minimum word threshold is met
            words = cleaned_buf.split()
            if self.clause_split and len(words) >= self.min_clause_words:
                clause_match = re.search(r"^(.*?[,;:\n])\s+(.*)$", cleaned_buf, re.DOTALL)
                if clause_match and clause_match.group(1).strip():
                    norm = TextNormalizer.normalize(clause_match.group(1).strip())
                    if norm:
                        ready_chunks.append(norm)
                    self.buffer = clause_match.group(2)
                    continue

            # 3. Fallback runaway boundary (no punctuation after max_buffer_words)
            if len(words) >= self.max_buffer_words:
                split_idx = cleaned_buf.rfind(" ")
                if split_idx != -1:
                    norm = TextNormalizer.normalize(cleaned_buf[:split_idx].strip())
                    if norm:
                        ready_chunks.append(norm)
                    self.buffer = cleaned_buf[split_idx + 1 :]
                    continue

            break

        return ready_chunks

    def flush(self) -> List[str]:
        """Flush remaining text in buffer when stream ends."""
        rem = re.sub(r"\s+", " ", self.buffer).strip()
        self.buffer = ""
        if rem:
            norm = TextNormalizer.normalize(rem)
            return [norm] if norm else []
        return []

    async def stream_chunks(
        self,
        token_stream: AsyncIterator[str],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> AsyncIterator[str]:
        """
        Async generator yielding normalized text chunks from an async token stream.
        """
        async for token in token_stream:
            if cancellation_token:
                cancellation_token.raise_if_cancelled()
            for chunk in self.feed_token(token):
                if cancellation_token:
                    cancellation_token.raise_if_cancelled()
                yield chunk

        if cancellation_token:
            cancellation_token.raise_if_cancelled()
        for chunk in self.flush():
            yield chunk


# Compatibility aliases for test suites
SimulatedSentenceChunker = SentenceChunker
SimulatedTextNormalizer = TextNormalizer
