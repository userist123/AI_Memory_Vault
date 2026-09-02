from typing import Optional

from sqlalchemy import select, update

from app.persistence.database import get_session
from app.persistence.models.document import Document
from app.schemas.document import DocumentStatus


class DocumentRepository:
    def create(self, document_id: str) -> None:
        with get_session() as session:
            document = Document(
                id=document_id,
                status=DocumentStatus.PENDING.value,
            )
            session.add(document)

    def update_status(self, document_id: str, status: DocumentStatus) -> bool:
        with get_session() as session:
            stmt = (
                update(Document)
                .where(Document.id == document_id)
                .values(status=status.value)
            )
            result = session.execute(stmt)
            return result.rowcount > 0

    def get_status(self, document_id: str) -> Optional[DocumentStatus]:
        with get_session() as session:
            stmt = select(Document.status).where(Document.id == document_id)
            result = session.execute(stmt).scalar_one_or_none()

            if result is None:
                return None

            return DocumentStatus(result)

    def exists(self, document_id: str) -> bool:
        with get_session() as session:
            stmt = select(Document.id).where(Document.id == document_id)
            return session.execute(stmt).first() is not None
            
    def get_raw_text(self, document_id: str) -> str | None:
        with get_session() as session:
            stmt = select(Document.raw_text).where(Document.id == document_id)
            return session.execute(stmt).scalar_one_or_none()
    
    def store_result(self,document_id: str,raw_text: str,entities: list[dict],) -> bool:
        with get_session() as session:
            stmt = (update(Document).where(Document.id == document_id).values(raw_text=raw_text,entities=entities,))
            result = session.execute(stmt)
            return result.rowcount > 0

    
    def get_entities(self, document_id: str):
        with get_session() as session:
            stmt = select(Document.entities).where(Document.id == document_id)
            return session.execute(stmt).scalar_one_or_none()
