"""S7 review L4: the studio keystore and its signer record are committed together (crash recovery on the next start)."""
from __future__ import annotations

import os
import subprocess
from pathlib import Path
from typing import Any

import pytest
from aas_build import runner

IMG = "sha256:" + "a" * 64
SIGNER = "c" * 64
DOCKER = str(Path("C:/docker.exe"))


class FakeGenkey:
    def __init__(self) -> None:
        self.calls = 0

    def __call__(self, argv: list[str], **kw: Any) -> subprocess.CompletedProcess[bytes]:
        self.calls += 1
        stage = next(a for a in argv if a.endswith(":/keys-out:rw")).rsplit(":/keys-out:rw", 1)[0]
        (Path(stage) / runner.KEYSTORE_NAME).write_bytes(b"ks")
        return subprocess.CompletedProcess(argv, 0, f"AAS-SIGNER-SHA256={SIGNER}\n".encode(), b"")


def _crash_on(monkeypatch: pytest.MonkeyPatch, name: str) -> None:
    real = os.replace

    def replace(src: Any, dst: Any) -> None:
        if Path(dst).name == name:
            raise OSError("simulated crash")
        real(src, dst)
    monkeypatch.setattr(runner.os, "replace", replace)
    real_write = Path.write_text

    def write_text(self: Path, *a: Any, **kw: Any) -> int:
        if self.name == name and not self.parent.name.startswith(".gen-"):  # the final record, not the staged copy
            raise OSError("simulated crash")
        return real_write(self, *a, **kw)
    monkeypatch.setattr(Path, "write_text", write_text)


@pytest.mark.parametrize("crash_at", [runner.SIGNER_NAME, runner.KEYSTORE_NAME])
def test_crash_between_keystore_and_record_recovers_on_the_next_start(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch, crash_at: str) -> None:
    keys = tmp_path / "keys"
    gen = FakeGenkey()
    monkeypatch.setattr(runner.subprocess, "run", gen)
    with monkeypatch.context() as m:
        _crash_on(m, crash_at)
        with pytest.raises(OSError, match="simulated"):
            runner.ensure_keystore(docker=DOCKER, image=IMG, keys_dir=keys)
    # next start: no "keystore exists without its certificate record" dead end; a usable pair comes out
    ks, sha = runner.ensure_keystore(docker=DOCKER, image=IMG, keys_dir=keys)
    assert sha == SIGNER and ks.read_bytes() == b"ks"
    assert (keys / runner.SIGNER_NAME).read_text().strip() == SIGNER
    assert not list(keys.glob("*.pending")) and not list(keys.glob(".gen-*"))
    calls = gen.calls
    assert runner.ensure_keystore(docker=DOCKER, image=IMG, keys_dir=keys) == (ks, SIGNER)
    assert gen.calls == calls  # and it is generated once, not again
    if crash_at == runner.SIGNER_NAME:
        assert calls == 1  # the keystore that was already made is kept (the pending record belongs to it)
