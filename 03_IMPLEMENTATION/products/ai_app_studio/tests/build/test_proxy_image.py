"""N5 + install-stage topology: allow-list CONNECT proxy on an --internal network (needs docker and image aas-proxy:dev).

  docker build -f images/proxy/Dockerfile -t aas-proxy:dev images/proxy
"""
from __future__ import annotations

import shutil
import subprocess
import uuid
from collections.abc import Iterator

import pytest

DOCKER = shutil.which("docker") or ""


def _have_image() -> bool:
    if not DOCKER:
        return False
    return subprocess.run([DOCKER, "image", "inspect", "aas-proxy:dev"], capture_output=True, timeout=60).returncode == 0  # noqa: S603


pytestmark = pytest.mark.skipif(not _have_image(), reason="docker or aas-proxy:dev image not available")

CLIENT = """
const net=require('net');
const [, host, targetHost, targetPort]=process.argv;
const s=net.connect(3128, host, ()=>s.write(`CONNECT ${targetHost}:${targetPort} HTTP/1.1\\r\\nHost: ${targetHost}:${targetPort}\\r\\n\\r\\n`));
s.setTimeout(8000, ()=>{console.log('timeout'); process.exit(0);});
s.once('data', d=>{console.log(String(d).split('\\r\\n')[0]); process.exit(0);});
s.on('error', e=>{console.log('error '+e.code); process.exit(0);});
"""


def run(*args: str, timeout: int = 120) -> subprocess.CompletedProcess[str]:
    return subprocess.run([DOCKER, *args], capture_output=True, text=True, timeout=timeout)  # noqa: S603


@pytest.fixture()
def topology() -> Iterator[str]:
    tag = uuid.uuid4().hex[:8]
    net, proxy = f"aas-egress-t{tag}", f"aas-proxy-t{tag}"
    assert run("network", "create", "--internal", net).returncode == 0
    try:
        r = run("run", "-d", "--name", proxy, "--network", net, "--read-only", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--memory", "128m", "--pids-limit", "64", "aas-proxy:dev")
        assert r.returncode == 0, r.stderr
        assert run("network", "connect", "bridge", proxy).returncode == 0  # proxy side has internet; clients do not
        yield f"{net}|{proxy}"
    finally:
        run("rm", "-f", proxy)
        run("network", "rm", net)


def connect(net: str, proxy: str, host: str, port: str) -> str:
    r = run("run", "--rm", "--network", net, "--read-only", "--cap-drop", "ALL", "--entrypoint", "node", "aas-proxy:dev",
            "-e", CLIENT, "--", proxy, host, port)
    return (r.stdout.strip() or r.stderr.strip()).splitlines()[-1] if (r.stdout.strip() or r.stderr.strip()) else ""


@pytest.mark.parametrize("host,port", [
    ("example.com", "443"), ("registry.npmjs.org", "22"), ("registry.npmjs.org", "80"), ("1.1.1.1", "443"),
    ("host.docker.internal", "443"), ("127.0.0.1", "443"), ("registry.npmjs.org.evil.com", "443"), ("REGISTRY.npmjs.org.", "443"),
])
def test_n5_refused_targets(topology: str, host: str, port: str) -> None:
    net, proxy = topology.split("|")
    assert " 403 " in connect(net, proxy, host, port), (host, port)


def test_n5_allowed_target_and_internal_network_has_no_other_way_out(topology: str) -> None:
    net, proxy = topology.split("|")
    out = connect(net, proxy, "registry.npmjs.org", "443")
    if "200" not in out and "502" in out:
        pytest.skip("proxy host has no internet")
    assert " 200 " in out, out  # [+] the allowed target works through the proxy
    # the same internal network has no gateway: a client cannot reach the internet directly
    r = run("run", "--rm", "--network", net, "--cap-drop", "ALL", "--entrypoint", "sh", "aas-proxy:dev", "-c",
            "wget -q -T 4 -O /dev/null https://registry.npmjs.org/ ; echo direct=$?")
    assert "direct=0" not in r.stdout
