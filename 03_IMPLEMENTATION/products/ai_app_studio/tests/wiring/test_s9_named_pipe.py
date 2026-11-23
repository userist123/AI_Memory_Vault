"""S9a B8: the mirror pipe is a native Windows named pipe whose DACL grants access to the current logon SID only, created
with PIPE_REJECT_REMOTE_CLIENTS and FILE_FLAG_FIRST_PIPE_INSTANCE; the HMAC handshake (both ways) and its timeout stay.
A second Windows account on the same PC cannot be simulated here: that check is manual (docs/slices/S9.md)."""
from __future__ import annotations

import ctypes
import secrets
import sys
import time
from ctypes import wintypes
from multiprocessing.connection import Client, Listener

import pytest

LOCAL = r"\\.\pipe" + "\\"
REMOTE = r"\\localhost\pipe" + "\\"
pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Windows named pipes")

if sys.platform == "win32":
    import _winapi

    from aas_device import video_relay as vr

    _adv = ctypes.WinDLL("advapi32", use_last_error=True)
    _k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _adv.GetSecurityInfo.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p),
                                     ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p),
                                     ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
    _adv.ConvertSecurityDescriptorToStringSecurityDescriptorW.argtypes = [
        ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(wintypes.LPWSTR), ctypes.c_void_p]
    _k32.LocalFree.argtypes = [ctypes.c_void_p]
    _k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
                                 wintypes.DWORD, wintypes.HANDLE]
    _k32.CreateFileW.restype = wintypes.HANDLE


def dacl_of(handle: int) -> str:
    sd = ctypes.c_void_p()
    dacl = ctypes.c_void_p()
    rc = _adv.GetSecurityInfo(handle, 6, 4, None, None, ctypes.byref(dacl), None, ctypes.byref(sd))  # KERNEL_OBJECT, DACL
    assert rc == 0, rc
    s = wintypes.LPWSTR()
    assert _adv.ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, 4, ctypes.byref(s), None)
    out = s.value or ""
    _k32.LocalFree(s)
    _k32.LocalFree(sd)
    return out


def open_client(path: str) -> int:
    """CreateFile on a pipe path; returns the Windows error (0 = opened, and the handle is closed again)."""
    h = _k32.CreateFileW(path, 0xC0000000, 0, None, 3, 0, None)  # GENERIC_READ|WRITE, OPEN_EXISTING
    if h in (None, wintypes.HANDLE(-1).value):
        return ctypes.get_last_error()
    _k32.CloseHandle(h)
    return 0


def test_logon_sid_is_a_logon_session_sid() -> None:
    sid = vr.logon_sid()
    assert sid.startswith("S-1-5-5-")


def test_pipe_dacl_grants_the_logon_sid_only() -> None:
    lst = vr.NativePipeListener(r"\\.\pipe\aas-test-" + secrets.token_hex(8))
    try:
        sddl = dacl_of(lst.handle)
        assert sddl.startswith("D:P"), sddl  # protected: nothing inherited
        aces = sddl[3:].strip("()").split(")(")
        assert len(aces) == 1, sddl
        assert aces[0].startswith("A;;") and aces[0].endswith(";;;" + vr.logon_sid()), sddl
    finally:
        lst.close()


def test_default_multiprocessing_pipe_has_a_wider_dacl_positive_control() -> None:
    addr = r"\\.\pipe\aas-test-" + secrets.token_hex(8)
    lst = Listener(addr, family="AF_PIPE")
    try:
        h = _winapi.CreateFile(addr, _winapi.GENERIC_READ | _winapi.GENERIC_WRITE, 0, _winapi.NULL,
                               _winapi.OPEN_EXISTING, 0, _winapi.NULL)
        try:
            sddl = dacl_of(h)
        finally:
            _winapi.CloseHandle(h)
        assert len(sddl[2:].strip("P()").split(")(")) > 1, sddl  # the default DACL: SYSTEM, Administrators, owner, ...
    finally:
        lst.close()


def test_a_second_listener_on_the_same_name_is_refused() -> None:
    addr = r"\\.\pipe\aas-test-" + secrets.token_hex(8)
    lst = vr.NativePipeListener(addr)
    try:
        with pytest.raises(OSError):
            vr.NativePipeListener(addr)  # FILE_FLAG_FIRST_PIPE_INSTANCE: no squatting / second server
    finally:
        lst.close()


def test_remote_clients_are_rejected_when_smb_loopback_works() -> None:
    """``\\\\localhost\\pipe\\<name>`` goes through SMB = a remote client. Positive control first: a pipe without
    PIPE_REJECT_REMOTE_CLIENTS and with an everyone DACL must be reachable that way, else the test is skipped."""
    name = "aas-test-" + secrets.token_hex(8)
    open_ = vr.NativePipeListener(LOCAL + name, reject_remote=False, sddl="D:P(A;;GA;;;WD)")
    try:
        if open_client(REMOTE + name) != 0:
            pytest.skip("SMB loopback to \\\\localhost\\pipe is not available on this PC")
    finally:
        open_.close()
    name2 = "aas-test-" + secrets.token_hex(8)
    shut = vr.NativePipeListener(LOCAL + name2, sddl="D:P(A;;GA;;;WD)")  # reject_remote=True (default)
    try:
        assert open_client(LOCAL + name2) == 0  # local still fine
        assert open_client(REMOTE + name2) != 0
    finally:
        shut.close()


def test_hub_pipe_keeps_the_hmac_handshake_and_rejects_a_wrong_key() -> None:
    hub = vr.VideoHub()
    env = hub.open_run("run_0123456789abcdef", "prj_0123456789abcdef")
    addr, key = env[vr.ENV_PIPE], bytes.fromhex(env[vr.ENV_KEY])
    try:
        rp = hub._runs["run_0123456789abcdef"]  # noqa: SLF001
        assert isinstance(rp.listener, vr.NativePipeListener)
        with pytest.raises(Exception):  # noqa: B017 - AuthenticationError / EOFError / OSError
            Client(addr, family="AF_PIPE", authkey=b"x" * 32)
        c = Client(addr, family="AF_PIPE", authkey=key)
        c.send_bytes((1).to_bytes(8, "big") + b"\x00\x00\x00\x01\x09\xf0")
        end = time.monotonic() + 5
        while time.monotonic() < end and hub.stats("run_0123456789abcdef").get("received", 0) < 1:
            time.sleep(0.05)
        assert hub.stats("run_0123456789abcdef")["received"] == 1
        assert hub.stats("run_0123456789abcdef")["authFailures"] >= 1
        c.close()
    finally:
        t0 = time.monotonic()
        hub.close_run("run_0123456789abcdef")
        assert time.monotonic() - t0 < 3  # the accept loop stops without the old wake-up client trick


def _sddl_sid(lst: object) -> str:
    sddl = dacl_of(lst.handle)  # type: ignore[attr-defined]
    assert sddl.startswith("D:P"), sddl
    aces = sddl[3:].strip("()").split(")(")
    assert len(aces) == 1 and aces[0].startswith("A;;"), sddl
    return aces[0].rsplit(";", 1)[1]


def test_user_sid_is_the_token_user_sid() -> None:
    sid = vr.user_sid()
    assert sid.startswith("S-1-5-") and not sid.startswith("S-1-5-5-")


@pytest.mark.parametrize("failure", ["not_found", "no_group"])
def test_pipe_dacl_falls_back_to_the_token_user_sid_without_a_logon_sid(monkeypatch: pytest.MonkeyPatch,
                                                                         failure: str) -> None:
    """GitHub Windows runners (service logons) may have no TokenLogonSid: ERROR_NOT_FOUND (1168) or an empty
    TOKEN_GROUPS. The DACL then grants the TokenUser SID only; remote rejection and first-instance stay."""
    real = vr._token_info  # noqa: SLF001

    def fake(cls: int) -> object:
        if cls == vr._TOKEN_LOGON_SID:  # noqa: SLF001
            if failure == "not_found":
                raise ctypes.WinError(1168)
            return ctypes.create_string_buffer(64)  # GroupCount = 0
        return real(cls)

    monkeypatch.setattr(vr, "_token_info", fake)
    with pytest.raises(OSError):
        vr.logon_sid()
    assert vr.pipe_sid() == vr.user_sid()
    addr = LOCAL + "aas-test-" + secrets.token_hex(8)
    lst = vr.NativePipeListener(addr)
    try:
        assert _sddl_sid(lst) == vr.user_sid()
        assert lst._mode & vr._PIPE_REJECT_REMOTE_CLIENTS  # noqa: SLF001
        with pytest.raises(OSError):
            vr.NativePipeListener(addr)  # FILE_FLAG_FIRST_PIPE_INSTANCE still set
        assert open_client(addr) == 0  # this user can still connect
    finally:
        lst.close()


def test_other_token_errors_are_not_masked_by_the_fallback(monkeypatch: pytest.MonkeyPatch) -> None:
    def fake(cls: int) -> object:
        raise ctypes.WinError(5)  # ERROR_ACCESS_DENIED: not a missing logon SID

    monkeypatch.setattr(vr, "_token_info", fake)
    with pytest.raises(OSError):
        vr.pipe_sid()


def test_default_pipe_dacl_uses_the_logon_sid_when_present() -> None:
    assert vr.pipe_sid() == vr.logon_sid()
