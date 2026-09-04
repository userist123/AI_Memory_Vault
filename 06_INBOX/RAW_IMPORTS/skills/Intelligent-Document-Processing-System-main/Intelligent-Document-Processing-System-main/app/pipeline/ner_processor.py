import logging
from transformers import pipeline

logger = logging.getLogger(__name__)

logger.info("Loading NER model...")

ner_pipeline = pipeline(
    task="ner",
    model="dslim/bert-base-NER",
    aggregation_strategy="none",
)

logger.info("NER model loaded")


def _merge_entities(raw_entities, min_score=0.85):
    merged = []
    current = None

    for ent in raw_entities:
        label = ent["entity"].replace("B-", "").replace("I-", "")
        word = ent["word"].lstrip("#")
        score = ent.get("score", 1.0)

        if score < min_score:
            continue

        if current and current["label"] == label:
            current["text"] += " " + word
            current["score"] = min(current["score"], score)
        else:
            if current:
                merged.append(current)
            current = {
                "text": word,
                "label": label,
                "score": score,
            }

    if current:
        merged.append(current)

    return [
        {"text": e["text"], "label": e["label"]}
        for e in merged
        if len(e["text"].strip()) >= 2
    ]


def run_ner(text: str) -> list[dict]:
    if not text.strip():
        return []

    raw = ner_pipeline(text)
    return _merge_entities(raw)
