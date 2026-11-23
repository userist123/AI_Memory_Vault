"""S7: offline Android build (DESIGN 3.5). Unit tests of the fixed argv / output reader, and real Docker probes
PB7a (offline strict build from the baked cache succeeds; an added unpinned dependency fails offline) and PB7b
(tampered verification-metadata.xml fails; the APK is signed by the studio key alone), each with a positive control.

Real probes need the image:
  docker build -f images/android-build/Dockerfile --build-context aas=images/android-build -t aas-android-build:dev kit
"""
from __future__ import annotations

import shutil
import subprocess
import uuid
import zipfile
from pathlib import Path

import pytest
from tests.winpaths import abs_win_path
from aas_build import runner
from aas_build.runner import BuildError

DOCKER = shutil.which("docker") or ""
IMG = "sha256:" + "a" * 64


def _image_id() -> str | None:
    if not DOCKER:
        return None
    r = subprocess.run([DOCKER, "image", "inspect", runner.ANDROID_TAG, "--format", "{{.Id}}"],  # noqa: S603
                       capture_output=True, text=True, timeout=60)
    return r.stdout.strip() if r.returncode == 0 and r.stdout.strip().startswith("sha256:") else None


def _argv(tmp_path: Path, **kw: object) -> list[str]:
    args: dict[str, object] = dict(docker=str(Path(DOCKER or abs_win_path("docker.exe")).resolve()), build_id="t" + "0" * 15,
                                   dist=tmp_path / "dist", keystore=tmp_path / "keys" / "aas-debug.keystore",
                                   out=tmp_path / "out", image=IMG, app_id="dev.aiappstudio.p0123456789abcdef")
    args.update(kw)
    return runner.build_android_argv(**args)  # type: ignore[arg-type]


# ---------------------------------------------------------------- argv
def test_android_argv_is_offline_hardened_and_mounts_read_only(tmp_path: Path) -> None:
    a = _argv(tmp_path)
    j = " ".join(a)
    assert a[a.index("--network") + 1] == "none" and a.count("--network") == 1
    for flag in ("--read-only", "--cap-drop", "--security-opt", "--user", "--memory", "--pids-limit"):
        assert flag in a
    assert a[a.index("--memory") + 1] == "4g" and a[a.index("--user") + 1] == "1000:1000"
    assert "/tmp:rw,noexec" in j
    assert f"{tmp_path / 'dist'}:/dist:ro" in a
    assert f"{tmp_path / 'keys' / 'aas-debug.keystore'}:/keys/aas-debug.keystore:ro" in a
    assert f"{tmp_path / 'out'}:/out:rw" in a
    assert "AAS_APP_ID=dev.aiappstudio.p0123456789abcdef" in a
    assert not any(t in ("-p", "--publish", "--privileged") or t.startswith("--network=") for t in a)


@pytest.mark.parametrize("app_id", ["com.evil.app", "dev.aiappstudio.p0123", "dev.aiappstudio.pXYZ456789abcdef0",
                                    "dev.aiappstudio.p0123456789abcdef;x", ""])
def test_android_argv_refuses_non_studio_package(tmp_path: Path, app_id: str) -> None:
    with pytest.raises(BuildError):
        _argv(tmp_path, app_id=app_id)


@pytest.mark.parametrize("ks", ["home/.android/aas-debug.keystore", "keys/debug.keystore"])
def test_android_argv_refuses_any_keystore_but_the_studio_one(tmp_path: Path, ks: str) -> None:
    with pytest.raises(BuildError):
        _argv(tmp_path, keystore=tmp_path / ks)


def test_app_id_for_matches_the_device_host_allow_list() -> None:
    import re
    src = (Path(__file__).resolve().parents[2] / "wsl" / "opt-aas-bin" / "aas_devhost.py").read_text(encoding="utf-8")
    pkg_re = re.compile(re.search(r'PKG_RE = re\.compile\(r"([^"]+)"\)', src).group(1))  # type: ignore[union-attr]
    assert pkg_re.match(runner.app_id_for("prj_0123456789abcdef"))
    with pytest.raises(BuildError):
        runner.app_id_for("prj_../x")


# ---------------------------------------------------------------- output reader
def _apk(path: Path) -> None:
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("AndroidManifest.xml", b"\x03\x00")


def test_collect_apk_accepts_one_apk_and_hashes(tmp_path: Path) -> None:
    out = tmp_path / "out"
    out.mkdir()
    _apk(out / "app-debug.apk")
    sha, size = runner.collect_apk(out, tmp_path / "dest" / "app-debug.apk", container_exists=lambda n: False,
                                   container_name="aas-b-x")
    assert len(sha) == 64 and size == (tmp_path / "dest" / "app-debug.apk").stat().st_size


@pytest.mark.parametrize("case", ["extra", "not_zip", "running", "dir"])
def test_collect_apk_fails_closed(tmp_path: Path, case: str) -> None:
    out = tmp_path / "out"
    out.mkdir()
    if case == "dir":
        (out / "app-debug.apk").mkdir()
    elif case == "not_zip":
        (out / "app-debug.apk").write_bytes(b"MZ not an apk")
    else:
        _apk(out / "app-debug.apk")
    if case == "extra":
        (out / "CLAUDE.md").write_text("x", encoding="utf-8")
    with pytest.raises(BuildError):
        runner.collect_apk(out, tmp_path / "d.apk", container_exists=lambda n: case == "running",
                           container_name="aas-b-x")
    assert not (tmp_path / "d.apk").exists()


def test_collect_apk_refuses_hardlink(tmp_path: Path) -> None:
    out = tmp_path / "out"
    out.mkdir()
    _apk(tmp_path / "elsewhere.apk")
    try:
        (out / "app-debug.apk").hardlink_to(tmp_path / "elsewhere.apk")
    except OSError:
        pytest.skip("hardlinks not supported here")
    with pytest.raises(BuildError):
        runner.collect_apk(out, tmp_path / "d.apk", container_exists=lambda n: False, container_name="aas-b-x")


def test_signer_mismatch_fails_the_build(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:  # PB7b, host side
    out = tmp_path / "out"
    out.mkdir()

    def fake_run(argv, **kw):  # type: ignore[no-untyped-def]
        _apk(out / "app-debug.apk")
        return runner._RunOutcome(0, "BUILD SUCCESSFUL\nAAS-SIGNER-SHA256=" + "b" * 64 + "\n")  # noqa: SLF001

    monkeypatch.setattr(runner, "_run_capped", fake_run)
    kw = dict(docker=str(Path(DOCKER or abs_win_path("docker.exe")).resolve()), build_id="t" + "1" * 15, dist=tmp_path / "dist",
              keystore=tmp_path / "keys" / "aas-debug.keystore", out=out, image=IMG,
              app_id="dev.aiappstudio.p0123456789abcdef", dest=tmp_path / "d.apk", container_exists=lambda n: False)
    with pytest.raises(BuildError, match="not signed by the studio keystore"):
        runner.run_android_build(expected_signer="c" * 64, **kw)  # type: ignore[arg-type]
    assert not (tmp_path / "d.apk").exists() and not out.exists()
    out.mkdir()
    res = runner.run_android_build(expected_signer="b" * 64, **kw)  # type: ignore[arg-type]  # [+]
    assert res.signer_sha256 == "b" * 64 and (tmp_path / "d.apk").is_file()


def test_ensure_keystore_refuses_owner_android_dir(tmp_path: Path) -> None:
    with pytest.raises(BuildError):
        runner.ensure_keystore(docker=abs_win_path("docker.exe"), image=IMG, keys_dir=tmp_path / ".android")


def test_ensure_keystore_never_replaces_an_unrecorded_keystore(tmp_path: Path) -> None:
    keys = tmp_path / "keys"
    keys.mkdir()
    (keys / "aas-debug.keystore").write_bytes(b"x")
    with pytest.raises(BuildError):
        runner.ensure_keystore(docker=abs_win_path("docker.exe"), image=IMG, keys_dir=keys)
    assert (keys / "aas-debug.keystore").read_bytes() == b"x"


# ---------------------------------------------------------------- committed verification metadata (S7 review M2)
ANDROID_IMAGE = Path(__file__).resolve().parents[2] / "images" / "android-build"


def test_committed_verification_metadata_pins_every_artifact_with_sha256() -> None:
    import xml.etree.ElementTree as ET
    ns = "{https://schema.gradle.org/dependency-verification}"
    root = ET.parse(ANDROID_IMAGE / "template" / "gradle" / "verification-metadata.xml").getroot()
    arts = list(root.iter(ns + "artifact"))
    assert len(arts) > 300
    for a in arts:
        digests = [s.get("value", "") for s in a.findall(ns + "sha256")]
        assert digests and all(len(d) == 64 and set(d) <= set("0123456789abcdef") for d in digests), a.get("name")


def test_dockerfile_verifies_against_the_committed_file_and_never_generates_it() -> None:
    df = (ANDROID_IMAGE / "Dockerfile").read_text(encoding="utf-8")
    assert "--write-verification-metadata" not in df.replace("regenerate the file by hand (gradle --write-verification-metadata", "")
    resolve = df[df.index("AS resolve"):df.index("# ---- 6. final")]
    assert "--dependency-verification strict" in resolve and "verification-metadata.xml" in resolve
    assert "COPY --from=aas template/ /out/android/" in df  # the committed file travels with the template


# ---------------------------------------------------------------- real Docker probes
IMAGE_ID = _image_id()
docker_only = pytest.mark.skipif(IMAGE_ID is None, reason="docker or aas-android-build:dev image not available")


@pytest.fixture(scope="module")
def keys(tmp_path_factory: pytest.TempPathFactory) -> tuple[Path, str]:
    if IMAGE_ID is None:
        pytest.skip("no image")
    return runner.ensure_keystore(docker=DOCKER, image=IMAGE_ID, keys_dir=tmp_path_factory.mktemp("k") / "keys")


def _dist(tmp_path: Path) -> Path:
    d = tmp_path / "dist"
    (d / "assets").mkdir(parents=True)
    (d / "index.html").write_text("<!doctype html><title>t</title><script src=assets/a.js></script>", encoding="utf-8")
    (d / "assets" / "a.js").write_text("document.title='aas'", encoding="utf-8")
    return d


def _probe(keys: tuple[Path, str], tmp_path: Path, script: str) -> subprocess.CompletedProcess[str]:
    """The production hardening and mounts, with a shell script instead of the entrypoint (the probe edits the
    template copy in /tmp before running the same offline strict Gradle command)."""
    argv = runner.build_android_argv(docker=DOCKER, build_id="p" + uuid.uuid4().hex[:15], dist=_dist(tmp_path),
                                     keystore=keys[0], out=tmp_path / "out", image=IMAGE_ID or "",
                                     app_id="dev.aiappstudio.p0123456789abcdef")
    (tmp_path / "out").mkdir(exist_ok=True)
    i = argv.index(IMAGE_ID or "")
    argv = [*argv[:i], "--entrypoint", "sh", IMAGE_ID or "", "-c", script]
    return subprocess.run(argv, capture_output=True, text=True, timeout=1500)  # noqa: S603


PREP = ("set -e; mkdir -p /tmp/w $GRADLE_USER_HOME $ANDROID_USER_HOME /tmp/home; cp -R /opt/aas/android-template/. /tmp/w/;"
        " cp -R /dist/. /tmp/w/android/app/src/main/assets/public/; cd /tmp/w/android; ")
GRADLE = ("gradle --offline --no-daemon --console=plain --dependency-verification strict -PaasAppId=$AAS_APP_ID"
          " -PaasKeystore=/keys/aas-debug.keystore assembleDebug")


@docker_only
def test_pb7a_offline_build_succeeds_and_apk_is_studio_signed(keys: tuple[Path, str], tmp_path: Path) -> None:
    out = tmp_path / "out"
    out.mkdir()
    res = runner.run_android_build(docker=DOCKER, build_id="a" + uuid.uuid4().hex[:15], dist=_dist(tmp_path),
                                   keystore=keys[0], expected_signer=keys[1], out=out, image=IMAGE_ID or "",
                                   app_id="dev.aiappstudio.p0123456789abcdef", dest=tmp_path / "app-debug.apk")
    assert res.size > 100_000 and res.signer_sha256 == keys[1]
    with zipfile.ZipFile(res.apk) as z:
        names = set(z.namelist())
    assert "assets/public/index.html" in names and "AndroidManifest.xml" in names
    assert any(n.startswith("META-INF/") for n in names) or res.size > 0  # v2/v3 signature block is outside the zip


@docker_only
def test_pb7a_added_unpinned_dependency_fails_offline(keys: tuple[Path, str], tmp_path: Path) -> None:
    add = ("printf '\\ndependencies { implementation \"com.squareup.okio:okio:3.9.1\" }\\n' >> app/build.gradle; ")
    bad = _probe(keys, tmp_path, PREP + add + GRADLE)
    assert bad.returncode != 0
    assert "okio" in (bad.stdout + bad.stderr)
    assert "No cached version" in (bad.stdout + bad.stderr) or "offline" in (bad.stdout + bad.stderr).lower()


@docker_only
def test_pb7b_tampered_verification_metadata_fails(keys: tuple[Path, str], tmp_path: Path) -> None:
    # flip the first recorded sha256 (keeps the XML valid): the artifact in the cache no longer matches -> fail
    tamper = ("sed -i '0,/<sha256 value=\"[0-9a-f]*\"/s//<sha256 value=\"" + "0" * 64 + "\"/' gradle/verification-metadata.xml;"
              " grep -c '" + "0" * 64 + "' gradle/verification-metadata.xml; ")
    bad = _probe(keys, tmp_path, PREP + tamper + GRADLE)
    assert bad.returncode != 0, bad.stdout[-2000:]
    assert "Dependency verification failed" in (bad.stdout + bad.stderr)
    ok = _probe(keys, tmp_path / "pos", PREP + GRADLE)  # [+] the same command on the untouched copy succeeds
    assert ok.returncode == 0, (ok.stdout + ok.stderr)[-2000:]


@docker_only
def test_pb7b_artifact_missing_from_the_metadata_fails(keys: tuple[Path, str], tmp_path: Path) -> None:
    # S7 review M2: strict verification refuses an artifact that has no entry (the image build relies on exactly this)
    drop = ("python3 -c \"import re,pathlib;p=pathlib.Path('gradle/verification-metadata.xml');s=p.read_text();"
            "p.write_text(re.sub(r'<artifact name=.*?</artifact>', '', s, count=1, flags=re.S))\"; ")
    bad = _probe(keys, tmp_path, PREP + drop + GRADLE)
    assert bad.returncode != 0, bad.stdout[-2000:]
    assert "Dependency verification failed" in (bad.stdout + bad.stderr)


@docker_only
def test_pb7b_apk_signed_by_another_key_is_flagged(keys: tuple[Path, str], tmp_path: Path) -> None:
    other = runner.ensure_keystore(docker=DOCKER, image=IMAGE_ID or "", keys_dir=tmp_path / "other-keys")
    assert other[1] != keys[1]
    out = tmp_path / "out"
    out.mkdir()
    with pytest.raises(BuildError, match="not signed by the studio keystore"):
        runner.run_android_build(docker=DOCKER, build_id="s" + uuid.uuid4().hex[:15], dist=_dist(tmp_path),
                                 keystore=other[0], expected_signer=keys[1], out=out, image=IMAGE_ID or "",
                                 app_id="dev.aiappstudio.p0123456789abcdef", dest=tmp_path / "x.apk")
    assert not (tmp_path / "x.apk").exists()
