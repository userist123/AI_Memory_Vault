import logging
from app.messaging.redis_client import get_redis_client

logger = logging.getLogger(__name__)

QUEUE_NAME = "document_jobs"

def enqueue(document_id: str) -> bool:
    client = get_redis_client()
    if not client:
        logger.warning("Queue unavailable, skipping enqueue",
                       extra={"document_id": document_id})
        return False

    client.lpush(QUEUE_NAME, document_id)
    logger.info("Document enqueued", extra={"document_id": document_id})
    return True


def dequeue(block: bool = True, timeout: int = 5):
    client = get_redis_client()
    if not client:
        return None

    if block:
        item = client.brpop(QUEUE_NAME, timeout=timeout)
        return item[1] if item else None

    return client.rpop(QUEUE_NAME)
