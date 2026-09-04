import redis
import os
import logging

logger = logging.getLogger(__name__)

REDIS_URL = os.getenv("REDIS_URL", "redis://redis:6379/0")

def get_redis_client():
    try:
        client = redis.from_url(REDIS_URL, decode_responses=True)
        client.ping()
        return client
    except Exception as e:
        logger.error("Redis unavailable", extra={"error": str(e)})
        return None
