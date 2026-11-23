"""AI App Studio device host control loop (python3 -I -S, stdlib only).

Runs as pid 1 of a private user+net+mount+pid namespace started by aas-serve/aas-ns. stdin/stdout carry JSON
lines (docs/DESIGN.md 2.3); stderr is a capped log. stdin EOF or {"op":"shutdown"} stops the emulator and exits;
when this process exits every process of the namespace is killed by the kernel.
Output is written by one thread from two queues: control lines first, video chunks bounded (oldest dropped).
"""
import base64
import hashlib
import json
import os
import re
import signal
import subprocess
import sys
import threading
import time
from collections import deque

AVDS = {"aas-phone": 5600, "aas-tablet": 5602}
MAX_LINE = 192 * 1024
RAW_CHUNK = 48 * 1024
VIDEO_Q_MAX = 32
BOOT_TIMEOUT = 240
APK_MAX = 200 * 1024 * 1024
VIDEO_SIZES = {"540x1170", "720x1560", "800x1280", "540x864"}
PKG_RE = re.compile(r"^dev\.aiappstudio\.p[0-9a-f]{16}$")
TEXT_RE = re.compile(r"^[ -~]{0,200}$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")


def _int(v, lo, hi):
    if type(v) is not int or not lo <= v <= hi:
        raise ValueError("bad int")
    return v


class Out:
    def __init__(self, stream):
        self.stream = stream
        self.cond = threading.Condition()
        self.ctl = deque()
        self.vid = deque()
        self.dropped = 0
        self.closed = False
        threading.Thread(target=self._run, daemon=True).start()

    def control(self, obj):
        line = (json.dumps(obj, separators=(",", ":")) + "\n").encode()
        if len(line) > MAX_LINE:
            raise ValueError("line too long")
        with self.cond:
            self.ctl.append(line)
            self.cond.notify()

    def video(self, obj):
        line = (json.dumps(obj, separators=(",", ":")) + "\n").encode()
        with self.cond:
            if len(self.vid) >= VIDEO_Q_MAX:
                self.vid.popleft()
                self.dropped += 1
            self.vid.append(line)
            self.cond.notify()

    def flush_wait(self, timeout=5.0):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            with self.cond:
                if not self.ctl:
                    return
            time.sleep(0.02)

    def _run(self):
        while True:
            with self.cond:
                while not self.ctl and not self.vid:
                    self.cond.wait()
                line = self.ctl.popleft() if self.ctl else self.vid.popleft()
            try:
                self.stream.write(line)
                self.stream.flush()
            except (BrokenPipeError, OSError):
                os._exit(3)


def log(msg):
    try:
        sys.stderr.write("aas_devhost: " + str(msg)[:400] + "\n")
        sys.stderr.flush()
    except OSError:
        pass


class Host:
    def __init__(self, avd):
        self.avd = avd
        self.port = AVDS[avd]
        self.serial = "emulator-%d" % self.port
        sdk = os.environ["ANDROID_SDK_ROOT"]
        self.state = os.environ["AAS_STATE"]
        self.adb_bin = os.path.join(sdk, "platform-tools", "adb")
        self.emu_bin = os.path.join(sdk, "emulator", "emulator")
        self.out = Out(sys.stdout.buffer)
        self.emu = None
        self.rec = None
        self.rec_lock = threading.Lock()
        self.video_on = False
        self.video_seq = 0
        self.install = None
        self.stopping = False

    # ---------- adb helpers (argv lists, never a host shell) ----------
    def adb(self, *args, timeout=30, data=False):
        argv = [self.adb_bin, "-P", "5641", "-s", self.serial, *args]
        r = subprocess.run(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                           timeout=timeout)
        return r if data else (r.returncode, r.stdout.decode("utf-8", "replace").strip())

    def shell(self, *words, timeout=30):
        # adb joins words into one device shell command line: every word is quoted here.
        return self.adb("shell", " ".join(_q(w) for w in words), timeout=timeout)

    # ---------- lifecycle ----------
    def _clear_locks(self):
        """SIGKILL shutdown leaves the emulator's AVD lock files behind; the flock in aas-serve already
        guarantees one session, so stale locks in the studio AVD dir are removed before each start."""
        d = os.path.join(os.environ["ANDROID_AVD_HOME"], self.avd + ".avd")
        for n in os.listdir(d):
            p = os.path.join(d, n)
            if n.endswith(".lock") and os.path.isfile(p) and not os.path.islink(p):
                os.unlink(p)

    def start(self):
        self._clear_locks()
        subprocess.run([self.adb_bin, "-P", "5641", "start-server"], stdin=subprocess.DEVNULL,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30, check=True)
        argv = [self.emu_bin, "-avd", self.avd, "-port", str(self.port), "-gpu", "host", "-no-audio",
                "-no-boot-anim", "-accel", "on", "-wipe-data", "-no-snapshot-save", "-no-snapshot-load",
                "-network-user-mode-options", "restrict=on", "-wifi-user-mode-options", "restrict=on"]
        logf = open(os.path.join(self.state, "run", "emulator-%s.log" % self.avd), "wb")
        self.emu = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=logf, stderr=subprocess.STDOUT)
        logf.close()
        t0 = time.monotonic()
        while True:
            if self.emu.poll() is not None:
                self.out.control({"ch": "e", "kind": "exit", "code": 30, "reason": "emulator-exited"})
                return False
            if time.monotonic() - t0 > BOOT_TIMEOUT:
                self.out.control({"ch": "e", "kind": "exit", "code": 31, "reason": "boot-timeout"})
                return False
            try:
                rc, val = self.adb("shell", "getprop sys.boot_completed", timeout=10)
            except subprocess.TimeoutExpired:
                rc, val = 1, ""
            if rc == 0 and val == "1":
                break
            time.sleep(2)
        boot_ms = int((time.monotonic() - t0) * 1000)
        for words in (("settings", "put", "system", "show_touches", "1"),
                      ("settings", "put", "system", "pointer_location", "1"),
                      ("input", "keyevent", "KEYCODE_WAKEUP"), ("wm", "dismiss-keyguard")):
            try:
                self.shell(*words, timeout=20)
            except subprocess.TimeoutExpired:
                pass
        rc, w = self.adb("shell", "wm size", timeout=10)
        m = re.search(r"(\d+)x(\d+)", w)
        size = [int(m.group(1)), int(m.group(2))] if m else [0, 0]
        self.out.control({"ch": "e", "kind": "booted", "avd": self.avd, "ms": boot_ms, "size": size})
        threading.Thread(target=self._modal_watch, daemon=True).start()
        threading.Thread(target=self._emu_watch, daemon=True).start()
        return True

    def _emu_watch(self):
        self.emu.wait()
        if not self.stopping:
            self.stopping = True
            self.out.control({"ch": "e", "kind": "exit", "code": 33, "reason": "emulator-exited"})
            self.out.flush_wait()
            os._exit(33)  # pid 1 exits -> the kernel kills the rest of the namespace

    def _modal_watch(self):
        for _ in range(10):
            try:
                r = subprocess.run(["xwininfo", "-root", "-tree"], stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=10)
                if b"Nested Virtualization" in r.stdout:
                    self.out.control({"ch": "e", "kind": "modal", "title": "Nested Virtualization"})
                    return
            except (OSError, subprocess.TimeoutExpired):
                return
            time.sleep(3)

    def stop(self):
        if self.stopping:
            return
        self.stopping = True
        self._video_stop()
        # No `adb emu kill`: qemu segfaults (core dump) on its graceful-exit path. The guest state is discarded
        # anyway (-wipe-data, -no-snapshot-save), so every process of this pid namespace gets SIGKILL
        # (SIGKILL never dumps core). We are pid 1 here, so kill(-1) reaches all of them but not us.
        if self.install:
            self._install_abort()
        if os.getpid() == 1:
            try:
                os.kill(-1, signal.SIGKILL)
            except ProcessLookupError:
                pass
        elif self.emu is not None and self.emu.poll() is None:
            self.emu.kill()
        if self.emu is not None:
            try:
                self.emu.wait(timeout=15)
            except subprocess.TimeoutExpired:
                pass
        _reap()

    # ---------- video ----------
    def _video_start(self, size):
        with self.rec_lock:
            if self.video_on:
                return
            self.video_on = True
        threading.Thread(target=self._video_loop, args=(size,), daemon=True).start()

    def _video_loop(self, size):
        while self.video_on and not self.stopping:
            argv = [self.adb_bin, "-P", "5641", "-s", self.serial, "exec-out", "screenrecord",
                    "--output-format=h264", "--size", size, "--bit-rate", "4000000", "--time-limit", "180", "-"]
            p = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
            with self.rec_lock:
                self.rec = p
            fd = p.stdout.fileno()
            while True:
                buf = os.read(fd, RAW_CHUNK)
                if not buf:
                    break
                self.video_seq += 1
                self.out.video({"ch": "v", "seq": self.video_seq, "b64": base64.b64encode(buf).decode()})
            p.wait()
            with self.rec_lock:
                self.rec = None

    def _video_stop(self):
        with self.rec_lock:
            self.video_on = False
            p = self.rec
        if p is not None and p.poll() is None:
            p.terminate()
            try:
                p.wait(timeout=5)
            except subprocess.TimeoutExpired:
                p.kill()
        try:
            self.shell("pkill", "-INT", "screenrecord", timeout=10)
        except subprocess.TimeoutExpired:
            pass

    # ---------- install ----------
    def _install_abort(self):
        inst, self.install = self.install, None
        if inst:
            inst["f"].close()
            try:
                os.unlink(inst["path"])
            except OSError:
                pass

    # ---------- dispatch ----------
    def handle(self, req):
        op = req["op"]
        if op == "tap":
            x, y = _int(req["x"], 0, 4096), _int(req["y"], 0, 4096)
            return self._ok_rc(self.shell("input", "tap", str(x), str(y)))
        if op == "swipe":
            a = [str(_int(req[k], 0, 4096)) for k in ("x1", "y1", "x2", "y2")]
            ms = _int(req["ms"], 1, 5000)
            return self._ok_rc(self.shell("input", "swipe", *a, str(ms)))
        if op == "text":
            t = req["t"]
            if not isinstance(t, str) or not TEXT_RE.match(t):
                raise ValueError("bad text")
            return self._ok_rc(self.shell("input", "text", t.replace(" ", "%s")))
        if op == "launch":
            pkg = req["pkg"]
            if not isinstance(pkg, str) or not PKG_RE.match(pkg):
                raise ValueError("bad pkg")
            return self._ok_rc(self.shell("monkey", "-p", pkg, "-c", "android.intent.category.LAUNCHER", "1"))
        if op == "screenshot":
            r = self.adb("exec-out", "screencap", "-p", timeout=30, data=True)
            if r.returncode != 0 or not r.stdout.startswith(b"\x89PNG"):
                return {"ok": False, "err": "screencap"}
            png = r.stdout
            n = (len(png) + RAW_CHUNK - 1) // RAW_CHUNK
            for i in range(n):
                part = png[i * RAW_CHUNK:(i + 1) * RAW_CHUNK]
                self.out.control({"ch": "s", "id": req["id"], "seq": i, "last": i == n - 1,
                                  "b64": base64.b64encode(part).decode()})
            return {"ok": True, "size": len(png), "sha256": hashlib.sha256(png).hexdigest(), "chunks": n}
        if op == "video_start":
            if req["size"] not in VIDEO_SIZES:
                raise ValueError("bad size")
            self._video_start(req["size"])
            return {"ok": True}
        if op == "video_stop":
            self._video_stop()
            return {"ok": True, "dropped": self.out.dropped}
        if op == "install":
            sha, size = req["sha256"], _int(req["size"], 1, APK_MAX)
            if not isinstance(sha, str) or not SHA_RE.match(sha):
                raise ValueError("bad sha")
            self._install_abort()
            path = os.path.join(self.state, "run", sha + ".apk")
            fdn = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            self.install = {"sha": sha, "size": size, "got": 0, "h": hashlib.sha256(), "f": os.fdopen(fdn, "wb"),
                            "path": path}
            return {"ok": True, "phase": "begin"}
        if op == "chunk":
            inst = self.install
            if not inst:
                raise ValueError("no install")
            buf = base64.b64decode(req["b64"], validate=True)
            if inst["got"] + len(buf) > inst["size"]:
                self._install_abort()
                raise ValueError("oversize")
            inst["f"].write(buf)
            inst["h"].update(buf)
            inst["got"] += len(buf)
            if inst["got"] < inst["size"]:
                return {"ok": True, "phase": "chunk", "got": inst["got"]}
            inst["f"].close()
            if inst["h"].hexdigest() != inst["sha"]:
                self._install_abort()
                return {"ok": False, "err": "sha256"}
            self.install = None
            try:
                rc, txt = self.adb("install", "-r", "-d", inst["path"], timeout=180)
            finally:
                os.unlink(inst["path"])
            return {"ok": rc == 0 and "Success" in txt, "phase": "done", "msg": txt[-200:]}
        raise ValueError("bad op")

    @staticmethod
    def _ok_rc(res):
        rc, txt = res
        return {"ok": rc == 0} if rc == 0 else {"ok": False, "err": txt[-200:]}


FIELDS = {
    "tap": {"x", "y"}, "swipe": {"x1", "y1", "x2", "y2", "ms"}, "text": {"t"}, "launch": {"pkg"},
    "screenshot": set(), "video_start": {"size"}, "video_stop": set(), "shutdown": set(),
    "install": {"sha256", "size"}, "chunk": {"b64"},
}


def _reap():
    """pid 1 duty: collect every exited child of the namespace."""
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            return
        if pid == 0:
            time.sleep(0.2)
            try:
                if os.waitpid(-1, os.WNOHANG)[0] == 0:
                    return
            except ChildProcessError:
                return


def _q(word):
    return "'" + word.replace("'", "'\\''") + "'"


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in AVDS:
        sys.exit(64)
    host = Host(sys.argv[1])
    try:
        if not host.start():
            host.stop()
            host.out.flush_wait()
            sys.exit(30)
    except Exception as e:  # noqa: BLE001
        log("start failed: %r" % (e,))
        host.stop()
        host.out.control({"ch": "e", "kind": "exit", "code": 32, "reason": "start-failed"})
        host.out.flush_wait()
        sys.exit(32)
    stdin = sys.stdin.buffer
    code = 0
    while True:
        if host.emu.poll() is not None:
            host.out.control({"ch": "e", "kind": "exit", "code": 33, "reason": "emulator-exited"})
            code = 33
            break
        line = stdin.readline(MAX_LINE + 1)
        if not line:
            break  # EOF: the Windows side went away
        if len(line) > MAX_LINE or not line.endswith(b"\n"):
            log("oversize request, stopping")
            code = 34
            break
        rid = None
        try:
            req = json.loads(line)
            if not isinstance(req, dict):
                raise ValueError("not an object")
            rid = req.get("id")
            if type(rid) is not int or not 0 < rid < 2 ** 31:
                raise ValueError("bad id")
            op = req.get("op")
            if op not in FIELDS or set(req) != FIELDS[op] | {"id", "op"}:
                raise ValueError("bad request")
            if op == "shutdown":
                host.out.control({"ch": "c", "id": rid, "ok": True})
                break
            t_req = time.monotonic()
            res = host.handle(req)
            res = dict(res, ms=int((time.monotonic() - t_req) * 1000))
        except subprocess.TimeoutExpired:
            res = {"ok": False, "err": "timeout"}
        except (ValueError, KeyError, TypeError) as e:
            res = {"ok": False, "err": "invalid: %s" % (str(e)[:80],)}
        if type(rid) is int:
            res = dict(res, ch="c", id=rid)
            host.out.control(res)
    host.stop()
    host.out.control({"ch": "e", "kind": "exit", "code": code, "reason": "stopped"})
    host.out.flush_wait()
    sys.exit(code)


if __name__ == "__main__":
    main()
