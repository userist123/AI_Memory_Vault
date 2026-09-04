import logging
from pypdf import PdfReader

logger = logging.getLogger(__name__)


def extract_text_from_pdf(file_path: str) -> str:
    """
    Attempts native text extraction from a PDF.
    Returns extracted text or empty string if extraction fails.
    """
    try:
        reader = PdfReader(file_path)
        text_chunks: list[str] = []

        for page_number, page in enumerate(reader.pages):
            try:
                text = page.extract_text()
                if text:
                    text_chunks.append(text)
            except Exception as e:
                logger.warning(
                    "Failed to extract text from page",
                    extra={"page": page_number, "error": str(e)},
                )

        full_text = "\n".join(text_chunks).strip()

        if full_text:
            logger.info("Native PDF text extraction succeeded")
        else:
            logger.info("Native PDF text extraction returned empty result")

        return full_text

    except Exception as e:
        logger.error(
            "Native PDF extraction failed",
            extra={"error": str(e)},
        )
        return ""
