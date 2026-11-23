"""Id/hash validators use re.fullmatch: Python's `$` also matches before a trailing newline, so `re.match(r"^x$")`
accepted "x\\n". Each validator must accept a valid value and reject the same value + "\\n"."""
from __future__ import annotations

from collections.abc import Callable
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest
from tests.winpaths import abs_win_path
from aas_build import baselines, capture, runner, service
from apps.device_host import protocol
from studio_core import ids, plans, run_grants

SHA = "a" * 64
IMAGE = "aas-web-build@sha256:" + SHA
BUILD_ID = "b0123456789abcdef"
PKG = "dev.aiappstudio.p0123456789abcdef"
DOCKER = abs_win_path("Program Files", "Docker", "docker.exe")


def _raises_with(fn: Callable[[], Any], exc: type[BaseException], text: str) -> bool:
    """True = the validator accepted the value (no error mentioning ``text``)."""
    try:
        fn()
    except exc as e:
        return text not in str(e)
    except Exception:  # noqa: BLE001 - a later, unrelated step failed: the validator itself passed
        return True
    return True


def _web_build_id(v: str) -> bool:
    return _raises_with(lambda: runner.build_web_argv(docker=DOCKER, build_id=v, ctx=Path(abs_win_path("x", "c")),
                                                      out=Path(abs_win_path("x", "o")), image=IMAGE),
                        runner.BuildError, "build id")


def _web_image(v: str) -> bool:
    return _raises_with(lambda: runner.build_web_argv(docker=DOCKER, build_id=BUILD_ID, ctx=Path(abs_win_path("x", "c")),
                                                      out=Path(abs_win_path("x", "o")), image=v),
                        runner.BuildError, "pinned")


def _android_build_id(v: str) -> bool:
    return _raises_with(lambda: runner.build_android_argv(docker=DOCKER, build_id=v, dist=Path("d"), keystore=Path("k"),
                                                          out=Path("o"), image=IMAGE, app_id=PKG),
                        runner.BuildError, "build id")


def _android_image(v: str) -> bool:
    return _raises_with(lambda: runner.build_android_argv(docker=DOCKER, build_id=BUILD_ID, dist=Path("d"),
                                                          keystore=Path("k"), out=Path("o"), image=v, app_id=PKG),
                        runner.BuildError, "pinned")


def _android_app_id(v: str) -> bool:
    return _raises_with(lambda: runner.build_android_argv(docker=DOCKER, build_id=BUILD_ID, dist=Path("d"),
                                                          keystore=Path("k"), out=Path("o"), image=IMAGE, app_id=v),
                        runner.BuildError, "application id")


def _genkey_build_id(v: str) -> bool:
    return _raises_with(lambda: runner.build_genkey_argv(docker=DOCKER, build_id=v, keys_out=Path("k"), image=IMAGE),
                        runner.BuildError, "invalid docker path")


def _genkey_image(v: str) -> bool:
    return _raises_with(lambda: runner.build_genkey_argv(docker=DOCKER, build_id=BUILD_ID, keys_out=Path("k"), image=v),
                        runner.BuildError, "invalid docker path")


def _desktop_build_id(v: str) -> bool:
    st = SimpleNamespace(data_dir=Path(abs_win_path("x", "data")))
    return _raises_with(lambda: service.desktop_dir(st, "prj_" + "0" * 16, v), Exception, "desktop build id")


def _build_web_pid(v: str) -> bool:
    return _raises_with(lambda: service._build_web(None, v, run_id=None, run_build=None, docker=None, image=None,  # noqa: SLF001
                                                   free_bytes=None), service.BuildFailed, "invalid project id")


def _reaches(mod: Any, attr: str, call: Callable[[], Any]) -> bool:
    """True = the validator passed and the next step (``mod.attr``) was reached."""
    called: list[int] = []
    orig = getattr(mod, attr)

    def spy(*a: Any, **k: Any) -> Any:
        called.append(1)
        return orig(*a, **k)

    setattr(mod, attr, spy)
    try:
        call()
    except Exception:  # noqa: BLE001, S110 - only whether the spy was reached matters
        pass
    finally:
        setattr(mod, attr, orig)
    return bool(called)


def _site_root_id16(tmp: Path) -> Callable[[str], bool]:
    resolve = service.site_root_for(SimpleNamespace(data_dir=tmp))  # type: ignore[arg-type]
    return lambda v: _reaches(service, "preview_base", lambda: resolve(v))


def _baseline_sha(tmp: Path) -> Callable[[str], bool]:
    st = SimpleNamespace(data_dir=tmp)
    return lambda v: _reaches(capture, "_contained", lambda: baselines.baseline_file(st, "prj_" + "1" * 16, v))


def _capture_sha(tmp: Path) -> Callable[[str], bool]:
    st = SimpleNamespace(data_dir=tmp)
    return lambda v: _reaches(capture, "_contained", lambda: capture.capture_file(st, v))


def _checklist_flow_id(v: str) -> bool:
    return _raises_with(lambda: baselines.validate_checklist({"flows": [{"id": v, "title": "t", "statements": []}]}),
                        Exception, "flow id")


def _protocol(fn: Callable[[Any], Any]) -> Callable[[str], bool]:
    return lambda v: _raises_with(lambda: fn(v), protocol.ProtocolError, "")  # any ProtocolError = rejected


def _run_grant(v: str) -> bool:
    class Boom(Exception):
        pass

    class Studio:
        def session(self) -> Any:
            raise Boom

    try:
        run_grants.issue(Studio(), v, "prj_" + "0" * 16)
    except ValueError:
        return False
    except Boom:
        return True
    return True


def _plan_hash(v: str) -> bool:
    a = SimpleNamespace(params={"contentSha256": v}, project_id="", plan_id="")
    called = []
    orig = plans._recomputed  # noqa: SLF001
    plans._recomputed = lambda _a: (called.append(1), v)[1]  # type: ignore[assignment]  # noqa: SLF001
    try:
        plans._intact(a)  # type: ignore[arg-type]  # noqa: SLF001
    finally:
        plans._recomputed = orig  # type: ignore[assignment]  # noqa: SLF001
    return bool(called)


CASES: list[tuple[str, Any, str]] = [
    ("runner.web.build_id", _web_build_id, BUILD_ID),
    ("runner.web.image", _web_image, IMAGE),
    ("runner.android.build_id", _android_build_id, BUILD_ID),
    ("runner.android.image", _android_image, IMAGE),
    ("runner.android.app_id", _android_app_id, PKG),
    ("runner.genkey.build_id", _genkey_build_id, BUILD_ID),
    ("runner.genkey.image", _genkey_image, "sha256:" + SHA),
    ("service.desktop_dir", _desktop_build_id, "d0123456789abcdef"),
    ("service.build_web.pid", _build_web_pid, "prj_0123456789abcdef"),
    ("service.site_root.id16", "site_root", "0123456789abcdef"),
    ("ids.is_valid_id", ids.is_valid_id, "prj_0123456789abcdef"),
    ("ids.is_valid_slug", ids.is_valid_slug, "my-slug"),
    ("baselines.baseline_file", "baseline", SHA),
    ("baselines.flow_id", _checklist_flow_id, "login-flow"),
    ("capture.capture_file", "capture", SHA),
    ("protocol.pkg", _protocol(protocol._check_pkg), PKG),  # noqa: SLF001
    ("protocol.sha", _protocol(protocol._check_sha), SHA),  # noqa: SLF001
    ("protocol.text", _protocol(protocol._check_text), "hello"),  # noqa: SLF001
    ("protocol.b64", _protocol(protocol._b64), "aGk="),  # noqa: SLF001
    ("run_grants.issue", _run_grant, "run_0123456789abcdef"),
    ("plans.intact", _plan_hash, SHA),
]


@pytest.mark.parametrize(("name", "check", "valid"), CASES, ids=[c[0] for c in CASES])
def test_validator_rejects_trailing_newline(name: str, check: Any, valid: str, tmp_path: Path) -> None:
    if check == "site_root":
        check = _site_root_id16(tmp_path)
    elif check == "baseline":
        check = _baseline_sha(tmp_path)
    elif check == "capture":
        check = _capture_sha(tmp_path)
    assert check(valid), f"{name}: the valid value must be accepted"
    assert not check(valid + "\n"), f"{name}: a trailing newline must be rejected"
