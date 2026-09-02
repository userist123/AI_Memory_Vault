from enum import Enum
from typing import List

from pydantic import BaseModel

class DocumentStatus(str, Enum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"

class UploadResponse(BaseModel):
    document_id: str
    status: DocumentStatus

class StatusResponse(BaseModel):
    document_id: str
    status: DocumentStatus

class Entity(BaseModel):
    text: str
    label: str

class ResultResponse(BaseModel):
    document_id: str
    entities: list[Entity]
