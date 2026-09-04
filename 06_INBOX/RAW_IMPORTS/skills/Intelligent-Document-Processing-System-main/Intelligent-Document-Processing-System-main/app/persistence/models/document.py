from sqlalchemy import String, DateTime, Text, JSON
from sqlalchemy.orm import Mapped, mapped_column
from datetime import datetime

from app.persistence.base import Base
from app.schemas.document import DocumentStatus


class Document(Base):
    __tablename__ = "documents"

    id: Mapped[str] = mapped_column(
        String,
        primary_key=True,
        index=True,
    )

    status: Mapped[str] = mapped_column(
        String,
        nullable=False,
        default=DocumentStatus.PENDING.value,
    )

    raw_text: Mapped[str | None] = mapped_column(
        Text,
        nullable=True,
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime,
        default=datetime.utcnow,
        nullable=False,
    )

    updated_at: Mapped[datetime] = mapped_column(
        DateTime,
        default=datetime.utcnow,
        onupdate=datetime.utcnow,
        nullable=False,
    )

    entities: Mapped[str | None]= mapped_column(
        JSON,
        nullable=True
    )