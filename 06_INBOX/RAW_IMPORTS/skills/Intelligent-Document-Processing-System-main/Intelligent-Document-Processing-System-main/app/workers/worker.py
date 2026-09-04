import time
import logging
import numpy

from app.messaging.queue import dequeue
from app.persistence.repositories.document_repository import DocumentRepository
from app.schemas.document import DocumentStatus
from app.pipeline.pipeline import extract_and_infer

logger = logging.getLogger(__name__)
repo = DocumentRepository()


def run():
    logger.info("Worker started", extra={"document_id": "-"})

    while True:
        document_id = dequeue()

        if not document_id:
            time.sleep(2)  # prevent busy looping
            continue

        logger.info("Processing job", extra={"document_id": document_id})

        updated = repo.update_status(document_id, DocumentStatus.PROCESSING)
        if not updated:
            logger.warning(
                "Document not found while processing",
                extra={"document_id": document_id},
            )
            continue

        try:
            file_path = f"/data/documents/{document_id}.pdf"

            raw_text, entities = extract_and_infer(file_path)

            repo.store_result(
                document_id=document_id,
                raw_text=raw_text,
                entities=entities,
            )

            repo.update_status(document_id, DocumentStatus.COMPLETED)

            logger.info("Job completed", extra={"document_id": document_id})

        except Exception:
            logger.exception("Worker failed", extra={"document_id": document_id})
            repo.update_status(document_id, DocumentStatus.FAILED)

if __name__ == "__main__":
    run()
