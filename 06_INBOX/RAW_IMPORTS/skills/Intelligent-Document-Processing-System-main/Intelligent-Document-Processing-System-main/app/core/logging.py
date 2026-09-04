import logging
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parents[2]
LOG_DIR = BASE_DIR / "logs"
LOG_DIR.mkdir(exist_ok=True)

LOG_FILE = LOG_DIR / "app.log"

LOG_FORMAT = (
    "[%(asctime)s] %(levelname)s %(name)s "
    "document_id=%(document_id)s %(message)s"
)

class DefaultContextFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        if not hasattr(record, "document_id"):
            record.document_id = "-"
        return True

def setup_logging():
    handlers = [
        logging.FileHandler(LOG_FILE),
        logging.StreamHandler(),
    ]

    logging.basicConfig(
        level=logging.INFO,
        format=LOG_FORMAT,
        handlers=handlers,
    )

    for handler in logging.getLogger().handlers:
        handler.addFilter(DefaultContextFilter())
