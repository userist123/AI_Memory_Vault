import logging

from app.pipeline.text_extractor import extract_text_from_pdf
from app.pipeline.ocr_processor import extract_text_with_ocr
from app.pipeline.ner_processor import run_ner

logger = logging.getLogger(__name__)


def should_run_ocr(text: str) -> tuple[bool, str]:
    if len(text) < 300:
        return True, "text_too_short"

    alpha_ratio = sum(c.isalpha() for c in text) / max(len(text), 1)
    if alpha_ratio < 0.6:
        return True, f"low_alpha_ratio:{alpha_ratio:.2f}"

    valid_lines = [line for line in text.splitlines() if len(line.strip()) > 20]
    if len(valid_lines) < 3:
        return True, "insufficient_meaningful_lines"

    return False, "native_text_trusted"


def extract_and_infer(file_path: str) -> tuple[str, list[dict]]:
    text = extract_text_from_pdf(file_path)

    run_ocr, reason = should_run_ocr(text)

    if run_ocr:
        logger.info(
            "Falling back to OCR",
            extra={"decision": reason},
        )
        text = extract_text_with_ocr(file_path)
    else:
        logger.info(
            "Using native PDF text",
            extra={"decision": reason},
        )

    logger.info("Running NER inference")
    entities = run_ner(text)

    return text, entities
