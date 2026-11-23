"""Offline web build runner (DESIGN 3.4; probes P1b/P1c, P2c, P7a, P8b, P8c, P8d).

This is the ONLY module of the package that may start a process (P1c: AST test in tests/build/test_runner.py).
- argv is a fixed list, never a shell string; the only variable parts are validated (build id, digest-pinned image,
  absolute directories without separators that could smuggle -v fields).
- `--network none` is always present; no -p/--publish and no other --network token can be produced.
- The output directory is read only after `docker run` returned AND the container no longer exists, with a reader
  that FAILS the build on any symlink, junction (reparse point), hardlink (nlink > 1) or non-regular file.
"""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import stat
import subprocess
import threading
import uuid
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path

ENTRYPOINT = "/usr/local/bin/aas-build-web"
CONTAINER_PREFIX = "aas-b-"
BUILD_TIMEOUT_S = 600
LOG_CAP_BYTES = 1024 * 1024
FSIZE_ULIMIT = 64 * 1024 * 1024  # per-file write cap inside the container (RLIMIT_FSIZE)
ALLOWED_EXT = frozenset({"html", "js", "css", "map", "json", "png", "jpg", "jpeg", "webp", "svg", "ico", "woff2", "txt"})
CONTROL_NAMES = frozenset({".claude", ".mcp.json", "claude.md", "agents.md"})

_BUILD_ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{7,39}$")
_IMAGE_RE = re.compile(r"^(?:[a-z0-9][a-z0-9._/-]{0,60}@sha256:[0-9a-f]{64}|sha256:[0-9a-f]{64})$")
_SEGMENT_RE = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9._@+-]{0,100}$")
_REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


class BuildError(Exception):
    """The build failed closed; nothing was copied to the destination."""


@dataclass(frozen=True)
class BuildLimits:
    max_files: int = 1000
    max_total_bytes: int = 200 * 1024 * 1024
    max_file_bytes: int = 50 * 1024 * 1024


@dataclass
class BuildResult:
    dest: Path
    files: list[str] = field(default_factory=list)
    total_bytes: int = 0
    dist_sha256: str = ""
    log_tail: str = ""


# ---- argv -----------------------------------------------------------------------------------------------------------
def _check_dir(p: Path, what: str) -> str:
    s = str(p)
    if not p.is_absolute():
        raise BuildError(f"{what} must be an absolute path")
    if any(c in s for c in (",", '"', "'", "\n", "\r", "\0", "=")) or ":" in s[2:]:
        raise BuildError(f"{what} contains a character that could change the mount spec")
    return s


def build_web_argv(*, docker: str, build_id: str, ctx: Path, out: Path, image: str, cpus: str = "2", memory: str = "2g") -> list[str]:
    if not os.path.isabs(docker):
        raise BuildError("docker executable must be an absolute path")
    if not _BUILD_ID_RE.match(build_id):
        raise BuildError("invalid build id")
    if not _IMAGE_RE.match(image):
        raise BuildError("image must be pinned by sha256 digest or image id")
    if not re.fullmatch(r"[0-9]{1,2}(\.[0-9])?", cpus) or not re.fullmatch(r"[0-9]{1,2}[mg]", memory):
        raise BuildError("invalid resource limit")
    return [
        docker, "run", "--rm", "--name", CONTAINER_PREFIX + build_id,
        "--network", "none",
        "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=1g",  # noqa: S108 - container path; vite runs from the image's /opt/aas/kit/node_modules
        "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
        "--user", "1000:1000", "--cpus", cpus, "--memory", memory,
        "--pids-limit", "512", "--ulimit", f"fsize={FSIZE_ULIMIT}:{FSIZE_ULIMIT}",
        "-e", "HOME=/tmp/home", "-e", "CI=1",
        "-v", f"{_check_dir(ctx, 'ctx')}:/src:ro",
        "-v", f"{_check_dir(out, 'out')}:/out:rw",
        image, ENTRYPOINT,
    ]


# ---- docker helpers (the only subprocess users) -----------------------------------------------------------------------
def _env() -> dict[str, str]:
    keep = ("PATH", "SYSTEMROOT", "SystemRoot", "USERPROFILE", "APPDATA", "HOME", "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG", "TEMP", "TMP")
    return {k: v for k, v in os.environ.items() if k in keep}


def docker_container_exists(docker: str, name: str) -> bool:
    """True = exists. False ONLY when the daemon answered "No such container/object"; any other failure raises (fail closed)."""
    if not os.path.isabs(docker) or not re.fullmatch(CONTAINER_PREFIX + r"[a-z0-9-]{8,40}", name):
        raise BuildError("invalid docker path or container name")
    r = subprocess.run([docker, "container", "inspect", name], capture_output=True, timeout=60, check=False, shell=False, env=_env(), stdin=subprocess.DEVNULL)  # noqa: S603
    if r.returncode == 0:
        return True
    err = (r.stderr or b"").lower()
    if b"no such container" in err or b"no such object" in err:  # daemon wording differs by version
        return False
    raise BuildError("cannot determine whether the build container is gone (daemon error); failing closed")


def _docker_kill(docker: str, name: str) -> None:
    subprocess.run([docker, "kill", name], capture_output=True, timeout=60, check=False, shell=False, env=_env(), stdin=subprocess.DEVNULL)  # noqa: S603


def resolve_image(docker: str, tag: str = "aas-web-build:dev") -> str:
    """Image id (`sha256:...`) of a locally built tag: the form build_web_argv accepts. Fails closed."""
    if not re.fullmatch(r"[a-z0-9][a-z0-9._/-]{0,60}(:[A-Za-z0-9._-]{1,40})?", tag):
        raise BuildError("invalid image tag")
    r = subprocess.run([docker, "image", "inspect", tag, "--format", "{{.Id}}"], capture_output=True, timeout=60,  # noqa: S603
                       check=False, shell=False, env=_env(), stdin=subprocess.DEVNULL)
    out = r.stdout.decode("utf-8", "replace").strip()
    if r.returncode != 0 or not _IMAGE_RE.match(out):
        raise BuildError(f"build image {tag} is not available locally (docker build -f images/web-build/Dockerfile ...)")
    return out


def _tree_stats(root: Path) -> tuple[int, int]:
    """(bytes, non-directory entries) under root without following links (used by the /out watchdog, N1)."""
    total = files = 0
    stack = [str(root)]
    while stack:
        try:
            with os.scandir(stack.pop()) as it:
                for e in it:
                    try:
                        if e.is_dir(follow_symlinks=False):
                            stack.append(e.path)
                        else:
                            files += 1
                            total += e.stat(follow_symlinks=False).st_size
                    except OSError:
                        continue
        except OSError:
            continue
    return total, files


def _tree_size(root: Path) -> int:
    """Bytes under root without following links."""
    return _tree_stats(root)[0]


@dataclass
class _RunOutcome:
    returncode: int
    tail: str
    over_cap: bool = False
    timed_out: bool = False
    over_files: bool = False


def _run_capped(argv: list[str], *, timeout: float, out: Path, max_out_bytes: int, kill: Callable[[], None],
                log_cap: int = LOG_CAP_BYTES, poll: float = 0.2, max_files: int | None = None) -> _RunOutcome:
    """Run argv (no shell); keep only the last `log_cap` bytes of stdout/stderr; kill the container if /out outgrows its cap."""
    proc = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=False, env=_env())  # noqa: S603
    bufs = [bytearray(), bytearray()]

    def pump(stream, buf: bytearray) -> None:  # type: ignore[no-untyped-def]
        read = getattr(stream, "read1", stream.read)
        while True:
            chunk = read(65536)
            if not chunk:
                return
            buf += chunk
            if len(buf) > log_cap:
                del buf[: len(buf) - log_cap]

    threads = [threading.Thread(target=pump, args=(proc.stdout, bufs[0]), daemon=True),
               threading.Thread(target=pump, args=(proc.stderr, bufs[1]), daemon=True)]
    over = threading.Event()
    over_files = threading.Event()
    done = threading.Event()

    def watchdog() -> None:
        while not done.wait(poll):
            size, nfiles = _tree_stats(out)
            if size > max_out_bytes:
                over.set()
                kill()
                return
            if max_files is not None and nfiles > max_files:  # N1: a flood of tiny files is under the byte cap
                over_files.set()
                kill()
                return

    wd = threading.Thread(target=watchdog, daemon=True)
    for t in (*threads, wd):
        t.start()
    timed_out = False
    try:
        proc.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        kill()
        proc.kill()
        proc.wait(timeout=30)
    finally:
        done.set()
    for t in threads:
        t.join(timeout=10)
    wd.join(timeout=5)
    # keep the end of BOTH streams (stdout first, stderr last so error text stays at the very end)
    tail = bytes(bufs[0]).decode("utf-8", "replace")[-1999:] + "\n" + bytes(bufs[1]).decode("utf-8", "replace")[-2000:]
    return _RunOutcome(proc.returncode if proc.returncode is not None else -1, tail, over.is_set(), timed_out,
                       over_files.is_set())


def run_web_build(*, docker: str, build_id: str, ctx: Path, out: Path, image: str, dest: Path,
                  container_exists: Callable[[str], bool] | None = None, limits: BuildLimits | None = None,
                  timeout: int = BUILD_TIMEOUT_S, poll: float = 0.2) -> BuildResult:
    """Run the offline build and collect output. On ANY failure the out dir is deleted and nothing is left in dest."""
    limits = limits or BuildLimits()
    argv = build_web_argv(docker=docker, build_id=build_id, ctx=ctx, out=out, image=image)
    name = CONTAINER_PREFIX + build_id
    try:
        o = _run_capped(argv, timeout=timeout, out=out, max_out_bytes=limits.max_total_bytes, kill=lambda: _docker_kill(docker, name), poll=poll,
                        max_files=limits.max_files)
        if o.over_cap:
            raise BuildError(f"output size over {limits.max_total_bytes} bytes during the build; container killed")
        if o.over_files:
            raise BuildError(f"output has more than {limits.max_files} files during the build; container killed")
        if o.timed_out:
            raise BuildError(f"build timed out after {timeout}s")
        if o.returncode != 0:
            raise BuildError(f"build container exit code {o.returncode}: {o.tail[-600:]}")
        exists = container_exists or (lambda n: docker_container_exists(docker, n))
        res = collect_output(out, dest, container_exists=exists, container_name=name, limits=limits)
    except BaseException:
        shutil.rmtree(out, ignore_errors=True)
        raise
    res.log_tail = o.tail
    return res


# ---- output reader ----------------------------------------------------------------------------------------------------
def _is_link_like(st: os.stat_result) -> bool:
    return stat.S_ISLNK(st.st_mode) or bool(getattr(st, "st_file_attributes", 0) & _REPARSE)


def _scan(root: Path, limits: BuildLimits) -> list[tuple[str, os.stat_result]]:
    """Validate the whole tree without copying; return (relative posix path, lstat) of accepted regular files."""
    top = os.lstat(root)
    if _is_link_like(top) or not stat.S_ISDIR(top.st_mode):
        raise BuildError("output root is not a plain directory")
    found: list[tuple[str, os.stat_result]] = []
    total = 0
    stack: list[tuple[Path, str]] = [(root, "")]
    while stack:
        d, rel = stack.pop()
        with os.scandir(d) as it:
            entries = sorted(it, key=lambda e: e.name)
        for e in entries:
            name = e.name
            r = f"{rel}/{name}" if rel else name
            if not _SEGMENT_RE.match(name) or name.endswith(".") or name.casefold() in CONTROL_NAMES:
                raise BuildError(f"output name not allowed: {r!r}")
            st = os.lstat(e.path)
            if _is_link_like(st):
                raise BuildError(f"link or reparse point in output refused: {r!r}")
            if stat.S_ISDIR(st.st_mode):
                stack.append((Path(e.path), r))
                continue
            if not stat.S_ISREG(st.st_mode):
                raise BuildError(f"non-regular file in output refused: {r!r}")
            if st.st_nlink > 1:
                raise BuildError(f"hardlinked file in output refused: {r!r}")
            ext = name.rsplit(".", 1)[-1].casefold() if "." in name else ""
            if ext not in ALLOWED_EXT:
                raise BuildError(f"file extension not allowed in output: {r!r}")
            if st.st_size > limits.max_file_bytes:
                raise BuildError(f"file size over {limits.max_file_bytes} bytes: {r!r}")
            total += st.st_size
            if total > limits.max_total_bytes:
                raise BuildError(f"output size over {limits.max_total_bytes} bytes")
            found.append((r, st))
            if len(found) > limits.max_files:
                raise BuildError(f"output has more than {limits.max_files} files")
    return found


def _read_regular(path: Path, st_before: os.stat_result, max_bytes: int) -> bytes:
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(path, flags)
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode) or st.st_nlink > 1 or (st.st_ino, st.st_dev) != (st_before.st_ino, st_before.st_dev):
            raise BuildError("file changed while being read")
        with os.fdopen(fd, "rb", closefd=False) as f:
            data = f.read(max_bytes + 1)
    finally:
        os.close(fd)
    if len(data) > max_bytes:
        raise BuildError(f"file size over {max_bytes} bytes")
    return data


def collect_output(out: Path, dest: Path, *, container_exists: Callable[[str], bool], container_name: str,
                   limits: BuildLimits | None = None) -> BuildResult:
    """Validate `<out>/dist`, copy accepted files to `dest` (atomic rename), return hashes. Fails closed."""
    limits = limits or BuildLimits()
    if container_exists(container_name):
        raise BuildError("build container still exists; refusing to read output")
    if dest.exists() and (not dest.is_dir() or any(dest.iterdir())):
        raise BuildError("dest must be absent or an empty directory")
    out = Path(out)
    names = {e.name for e in os.scandir(out)}
    if names != {"dist"}:
        raise BuildError(f"output must contain exactly 'dist', found {sorted(names)}")
    dist = out / "dist"
    files = _scan(dist, limits)
    if "index.html" not in {r for r, _ in files}:
        raise BuildError("output has no index.html")
    stage = dest.parent / f".{dest.name}.partial-{uuid.uuid4().hex[:8]}"
    h = hashlib.sha256()
    total = 0
    try:
        stage.mkdir(parents=True)
        for rel, st in sorted(files):
            data = _read_regular(dist / rel, st, limits.max_file_bytes)
            total += len(data)
            if total > limits.max_total_bytes:
                raise BuildError(f"output size over {limits.max_total_bytes} bytes")
            target = stage / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            h.update(rel.encode() + b"\0" + data + b"\0")
        if dest.exists():
            dest.rmdir()
        os.replace(stage, dest)
    except BaseException:
        shutil.rmtree(stage, ignore_errors=True)
        raise
    return BuildResult(dest=dest, files=sorted(r for r, _ in files), total_bytes=total, dist_sha256=h.hexdigest())


# ---- Android (DESIGN 3.5; PB7a/PB7b) ----------------------------------------------------------------------------------
ANDROID_TAG = "aas-android-build:dev"
ANDROID_TIMEOUT_S = 1500
ANDROID_FSIZE_ULIMIT = 512 * 1024 * 1024  # Gradle writes dex/jar transforms well above the web build's 64 MiB cap
APK_MAX_BYTES = 100 * 1024 * 1024
KEYSTORE_NAME = "aas-debug.keystore"
SIGNER_NAME = "aas-debug.cert.sha256"
_APP_ID_RE = re.compile(r"^dev\.aiappstudio\.p[0-9a-f]{16}$")  # = aas_devhost.PKG_RE (the device host allow-list)
_SIGNER_LINE = re.compile(r"^AAS-SIGNER-SHA256=([0-9a-f]{64})\s*$", re.M)


@dataclass
class AndroidResult:
    apk: Path
    sha256: str
    size: int
    signer_sha256: str
    log_tail: str = ""


def app_id_for(project_id: str) -> str:
    """Studio package id of a project: dev.aiappstudio.p<id16> (the only prefix the device host installs)."""
    if not re.fullmatch(r"prj_[0-9a-f]{16}", project_id or ""):
        raise BuildError("invalid project id")
    return "dev.aiappstudio.p" + project_id[4:]


def _hardening(cpus: str, memory: str, fsize: int, pids: int) -> list[str]:
    if not re.fullmatch(r"[0-9]{1,2}(\.[0-9])?", cpus) or not re.fullmatch(r"[0-9]{1,4}[mg]", memory):
        raise BuildError("invalid resource limit")
    return ["--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--user", "1000:1000", "--cpus", cpus, "--memory", memory, "--pids-limit", str(pids),
            "--ulimit", f"fsize={fsize}:{fsize}"]


def _studio_keystore(p: Path) -> None:
    if p.name != KEYSTORE_NAME or any(part.casefold() == ".android" for part in p.parts):
        raise BuildError("only the studio keystore may be used (never the owner's Android debug key)")


def build_android_argv(*, docker: str, build_id: str, dist: Path, keystore: Path, out: Path, image: str, app_id: str,
                       cpus: str = "4", memory: str = "4g") -> list[str]:
    """Fixed argv of the offline APK build. /tmp is noexec: Gradle runs from the image (/opt), aapt2 comes from the
    pinned build-tools (android.aapt2FromMavenOverride) and native integration is off (org.gradle.native=false)."""
    if not os.path.isabs(docker):
        raise BuildError("docker executable must be an absolute path")
    if not _BUILD_ID_RE.match(build_id):
        raise BuildError("invalid build id")
    if not _IMAGE_RE.match(image):
        raise BuildError("image must be pinned by sha256 digest or image id")
    if not isinstance(app_id, str) or not _APP_ID_RE.match(app_id):
        raise BuildError("application id is not a studio package id")
    _studio_keystore(keystore)
    return [
        docker, "run", "--rm", "--name", CONTAINER_PREFIX + build_id,
        *_hardening(cpus, memory, ANDROID_FSIZE_ULIMIT, 1024),
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=3g",  # noqa: S108 - container path
        "-e", f"AAS_APP_ID={app_id}",
        "-v", f"{_check_dir(dist, 'dist')}:/dist:ro",
        "-v", f"{_check_dir(keystore, 'keystore')}:/keys/{KEYSTORE_NAME}:ro",
        "-v", f"{_check_dir(out, 'out')}:/out:rw",
        image, "build",
    ]


def build_genkey_argv(*, docker: str, build_id: str, keys_out: Path, image: str) -> list[str]:
    if not os.path.isabs(docker) or not _BUILD_ID_RE.match(build_id) or not _IMAGE_RE.match(image):
        raise BuildError("invalid docker path, build id or image")
    return [docker, "run", "--rm", "--name", CONTAINER_PREFIX + build_id, *_hardening("1", "512m", 1024 * 1024, 128),
            "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=64m",  # noqa: S108 - container path
            "-v", f"{_check_dir(keys_out, 'keys')}:/keys-out:rw", image, "genkey"]


def ensure_keystore(*, docker: str, image: str, keys_dir: Path) -> tuple[Path, str]:
    """The studio debug keystore (generated once per install inside the build image, never the owner's
    Android debug key) and its certificate sha256. Returns (keystore path, signer sha256).

    S7 review L4: the pair is committed with a pending marker, so a crash can never leave a keystore without its record:
    record -> ``<signer>.pending`` (atomic), keystore (atomic), pending -> record (atomic). A pending record whose
    keystore arrived is finished on the next start; a pending record without a keystore is dropped and regenerated."""
    keystore, signer_file = keys_dir / KEYSTORE_NAME, keys_dir / SIGNER_NAME
    pending = keys_dir / (SIGNER_NAME + ".pending")
    _studio_keystore(keystore)
    if pending.is_file() and not pending.is_symlink():
        if keystore.is_file() and not keystore.is_symlink() and not signer_file.exists():
            if not re.fullmatch(r"[0-9a-f]{64}", pending.read_text(encoding="ascii", errors="replace").strip()):
                raise BuildError("the studio keystore certificate record is damaged")
            os.replace(pending, signer_file)  # the crash happened between the keystore and the final record
        elif not keystore.exists() and not keystore.is_symlink():
            pending.unlink()  # the crash happened before the keystore was placed: nothing was signed with it
    if keystore.is_file() and not keystore.is_symlink() and signer_file.is_file():
        sha = signer_file.read_text(encoding="ascii", errors="replace").strip()
        if re.fullmatch(r"[0-9a-f]{64}", sha):
            return keystore, sha
        raise BuildError("the studio keystore certificate record is damaged")
    if keystore.exists() or keystore.is_symlink():
        raise BuildError("a studio keystore exists without its certificate record; refusing to replace it")
    keys_dir.mkdir(parents=True, exist_ok=True)
    stage = keys_dir / f".gen-{uuid.uuid4().hex[:8]}"
    stage.mkdir()
    try:
        bid = "k" + uuid.uuid4().hex[:15]
        r = subprocess.run(build_genkey_argv(docker=docker, build_id=bid, keys_out=stage, image=image),  # noqa: S603
                           capture_output=True, timeout=180, check=False, shell=False, env=_env(),
                           stdin=subprocess.DEVNULL)
        m = _SIGNER_LINE.search(r.stdout.decode("utf-8", "replace"))
        made = stage / KEYSTORE_NAME
        if r.returncode != 0 or not m or not made.is_file() or made.is_symlink():
            raise BuildError("studio keystore generation failed: " + r.stderr.decode("utf-8", "replace")[-300:])
        staged = stage / SIGNER_NAME
        staged.write_text(m.group(1) + "\n", encoding="ascii")
        os.replace(staged, pending)  # record first, marked pending
        os.replace(made, keystore)
        os.replace(pending, signer_file)
        return keystore, m.group(1)
    finally:
        shutil.rmtree(stage, ignore_errors=True)


def collect_apk(out: Path, dest: Path, *, container_exists: Callable[[str], bool], container_name: str,
                max_bytes: int = APK_MAX_BYTES) -> tuple[str, int]:
    """`<out>` must hold exactly one regular file app-debug.apk (no link, nlink 1, ZIP magic, <= cap). Copied
    atomically to `dest`; returns (sha256, size)."""
    if container_exists(container_name):
        raise BuildError("build container still exists; refusing to read output")
    names = {e.name for e in os.scandir(out)}
    if names != {"app-debug.apk"}:
        raise BuildError(f"output must contain exactly app-debug.apk, found {sorted(names)[:10]}")
    p = Path(out) / "app-debug.apk"
    st = os.lstat(p)
    if _is_link_like(st) or not stat.S_ISREG(st.st_mode) or st.st_nlink > 1:
        raise BuildError("app-debug.apk is not a plain regular file")
    if st.st_size > max_bytes:
        raise BuildError(f"APK over {max_bytes} bytes")
    data = _read_regular(p, st, max_bytes)
    if not data.startswith(b"PK\x03\x04"):
        raise BuildError("app-debug.apk is not a ZIP/APK file")
    sha = hashlib.sha256(data).hexdigest()
    tmp = dest.parent / f".{dest.name}.partial-{uuid.uuid4().hex[:8]}"
    try:
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp.write_bytes(data)
        os.replace(tmp, dest)
    except BaseException:
        tmp.unlink(missing_ok=True)
        raise
    return sha, len(data)


def run_android_build(*, docker: str, build_id: str, dist: Path, keystore: Path, expected_signer: str, out: Path,
                      image: str, app_id: str, dest: Path, container_exists: Callable[[str], bool] | None = None,
                      timeout: int = ANDROID_TIMEOUT_S, poll: float = 0.5) -> AndroidResult:
    """Offline APK build; on ANY failure `out` is deleted and `dest` is left untouched."""
    argv = build_android_argv(docker=docker, build_id=build_id, dist=dist, keystore=keystore, out=out, image=image,
                              app_id=app_id)
    name = CONTAINER_PREFIX + build_id
    try:
        o = _run_capped(argv, timeout=timeout, out=out, max_out_bytes=APK_MAX_BYTES,
                        kill=lambda: _docker_kill(docker, name), poll=poll, max_files=4)
        if o.over_cap or o.over_files:
            raise BuildError("APK output over its cap during the build; container killed")
        if o.timed_out:
            raise BuildError(f"Android build timed out after {timeout}s")
        if o.returncode != 0:
            raise BuildError(f"Android build container exit code {o.returncode}: {o.tail[-900:]}")
        signers = set(_SIGNER_LINE.findall(o.tail))
        if signers != {expected_signer}:  # PB7b: the in-image apksigner check must report the studio key alone
            raise BuildError("the APK is not signed by the studio keystore")
        exists = container_exists or (lambda n: docker_container_exists(docker, n))
        sha, size = collect_apk(out, dest, container_exists=exists, container_name=name)
    except BaseException:
        shutil.rmtree(out, ignore_errors=True)
        raise
    return AndroidResult(apk=dest, sha256=sha, size=size, signer_sha256=expected_signer, log_tail=o.tail)
