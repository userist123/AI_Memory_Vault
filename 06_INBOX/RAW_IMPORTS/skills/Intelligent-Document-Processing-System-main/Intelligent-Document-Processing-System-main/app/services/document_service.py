import logging

from app.schemas.document import DocumentStatus
from app.persistence.repositories.document_repository import DocumentRepository
from app.messaging.queue import enqueue

logger = logging.getLogger(__name__)


class DocumentService:
    def __init__(self):
        self.repo = DocumentRepository()

    def create(self, document_id: str) -> None:
        logger.info(
            "Creating document record",
            extra={"document_id": document_id},
        )

        self.repo.create(document_id)

        enqueued = enqueue(document_id)
        if not enqueued:
            logger.warning(
                "Document not enqueued (queue unavailable)",
                extra={"document_id": document_id},
            )

    def get_status(self, document_id: str):
        return self.repo.get_status(document_id)
    
    def get_raw_text(self, document_id: str) -> str | None:
        with get_session() as session:
            stmt = select(Document.raw_text).where(Document.id == document_id)
            return session.execute(stmt).scalar_one_or_none()

    def get_result(self, document_id: str):
        status = self.repo.get_status(document_id)

        if status != DocumentStatus.COMPLETED:
            return None

        return self.repo.get_entities(document_id)


document_service = DocumentService()
