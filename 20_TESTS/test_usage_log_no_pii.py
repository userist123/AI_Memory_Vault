"""WP-11: the usage log holds the query hash, never the query text, the user name or the home path."""
import getpass
import json
from pathlib import Path

from interfaces import vault_runtime

SENTINEL = "SENTINEL-QUERY-7f3a9c1e-do-not-log"


def test_usage_log_has_no_query_text_user_name_or_home_path(tmp_path, monkeypatch):
    monkeypatch.setenv(vault_runtime.HOME_ENV, str(tmp_path))
    vault_runtime.log_usage("recall", "test-client", SENTINEL, ["id-1"], 1, 1.5)
    log = vault_runtime.usage_log_path()
    assert log.parent == tmp_path and log.is_file()
    text = log.read_text(encoding="utf-8")
    entry = json.loads(text.splitlines()[0])
    assert entry["query_sha256"] == vault_runtime.query_digest(SENTINEL)

    home = Path.home()
    forbidden = {
        "query text": SENTINEL,
        "user name": getpass.getuser(),
        "home path": str(home),
        "home path (posix)": home.as_posix(),
        "home path (json-escaped)": json.dumps(str(home))[1:-1],
    }
    low = text.lower()
    found = [name for name, value in forbidden.items() if value and value.lower() in low]
    assert found == []
