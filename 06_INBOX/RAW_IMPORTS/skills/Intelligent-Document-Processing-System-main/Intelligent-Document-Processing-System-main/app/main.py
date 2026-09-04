from fastapi import FastAPI
import logging

from app.core.logging import setup_logging
from app.api.v1.router import api_router
from app.persistence.database import engine
from app.persistence.base import Base


def create_app() -> FastAPI:
    app = FastAPI(
        title="Intelligent Document Processing",
        version="0.1.0",
    )

    app.include_router(api_router, prefix="/api/v1")

    @app.on_event("startup")
    def on_startup():
        Base.metadata.create_all(bind=engine)
        logger = logging.getLogger(__name__)
        logger.info("Server startup complete")

    return app


setup_logging()
app = create_app()
