import pytesseract
from pdf2image import convert_from_path
import logging

logger = logging.getLogger(__name__)


def extract_text_with_ocr(pdf_path: str) -> str:
    logger.info("Starting OCR extraction")

    images = convert_from_path(pdf_path)
    logger.info(
        "PDF converted to images",
        extra={"pages": len(images)},
    )

    text_chunks = []

    for idx, image in enumerate(images):
        page_text = pytesseract.image_to_string(image)
        text_chunks.append(page_text)

        logger.debug(
            "OCR page processed",
            extra={"page": idx + 1, "chars": len(page_text)},
        )

    return "\n".join(text_chunks)
