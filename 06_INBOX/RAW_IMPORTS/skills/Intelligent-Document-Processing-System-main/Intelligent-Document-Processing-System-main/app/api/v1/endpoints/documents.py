from fastapi import APIRouter, UploadFile, File, HTTPException
from uuid import uuid4
import logging

from app.schemas.document import (
    UploadResponse,
    StatusResponse,
    ResultResponse,
)
from app.services.document_service import document_service

logger = logging.getLogger(__name__)

router = APIRouter()


@router.post("/upload", response_model=UploadResponse)
def upload_document(file: UploadFile = File(...)):
    if file.content_type != "application/pdf":
        logger.warning(
            "Rejected file upload: unsupported content type",
            extra={"content_type": file.content_type},
        )
        raise HTTPException(
            status_code=400,
            detail="Only PDF files are supported",
        )

    document_id = str(uuid4())
    file_path = f"/data/documents/{document_id}.pdf"
    with open(file_path, "wb") as f:
        f.write(file.file.read())
    document_service.create(document_id)

    logger.info(
        "Upload received",
        extra={"document_id": document_id, "content_type": file.content_type},
    )

    return UploadResponse(
        document_id=document_id,
        status="PENDING",
    )


@router.get("/{document_id}/status", response_model=StatusResponse)
def get_document_status(document_id: str):
    status = document_service.get_status(document_id)

    if status is None:
        raise HTTPException(status_code=404, detail="Document not found")

    return StatusResponse(
        document_id=document_id,
        status=status,
    )


@router.get("/{document_id}/result", response_model=ResultResponse)
def get_document_result(document_id: str):
    entities = document_service.get_result(document_id)

    if entities is None:
        raise HTTPException(status_code=404, detail="Result not available")

    return ResultResponse(
        document_id=document_id,
        entities=entities,
    )
