"""Windows ACL reads for the desktop build root (S9c): owner SID and ACCESS_ALLOWED aces via advapi32/kernel32 (ctypes).

This is the only module besides runner.py allowed to import ctypes (P1c, tests/build/test_runner.py): the AST test
asserts it references no process-creation API (CreateProcess*, ShellExecute*, WinExec, system, popen, spawn*), no
dynamic lookup that could reach one, and loads no DLL other than advapi32/kernel32. It starts no process.
"""
from __future__ import annotations

from pathlib import Path

Ace = tuple[str, int, int]  # (sid, mask, ace flags) of an ACCESS_ALLOWED ACE


def _win():  # type: ignore[no-untyped-def]
    import ctypes
    from ctypes import wintypes
    adv = ctypes.WinDLL("advapi32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    return ctypes, wintypes, adv, k32


def _sid_str(ct, wt, adv, k32, psid) -> str:  # type: ignore[no-untyped-def]
    adv.ConvertSidToStringSidW.argtypes = [ct.c_void_p, ct.POINTER(wt.LPWSTR)]
    out = wt.LPWSTR()
    if not adv.ConvertSidToStringSidW(psid, ct.byref(out)):
        raise OSError("ConvertSidToStringSid failed")
    try:
        return str(out.value)
    finally:
        k32.LocalFree.argtypes = [ct.c_void_p]
        k32.LocalFree(ct.cast(out, ct.c_void_p))


def current_sid() -> str:
    """SID of the process token's user (Windows)."""
    ct, wt, adv, k32 = _win()
    k32.GetCurrentProcess.restype = ct.c_void_p
    adv.OpenProcessToken.argtypes = [ct.c_void_p, wt.DWORD, ct.POINTER(ct.c_void_p)]
    tok = ct.c_void_p()
    if not adv.OpenProcessToken(k32.GetCurrentProcess(), 0x8, ct.byref(tok)):  # TOKEN_QUERY
        raise OSError("OpenProcessToken failed")
    try:
        adv.GetTokenInformation.argtypes = [ct.c_void_p, ct.c_int, ct.c_void_p, wt.DWORD, ct.POINTER(wt.DWORD)]
        n = wt.DWORD()
        adv.GetTokenInformation(tok, 1, None, 0, ct.byref(n))  # TokenUser
        buf = ct.create_string_buffer(n.value)
        if not adv.GetTokenInformation(tok, 1, buf, n, ct.byref(n)):
            raise OSError("GetTokenInformation failed")
        psid = ct.cast(buf, ct.POINTER(ct.c_void_p))[0]
        return _sid_str(ct, wt, adv, k32, psid)
    finally:
        k32.CloseHandle.argtypes = [ct.c_void_p]
        k32.CloseHandle(tok)


def read_acl(path: Path) -> tuple[str, list[Ace]]:
    """(owner SID, ACCESS_ALLOWED aces) of a file system object (Windows, ctypes). A NULL DACL = Everyone full."""
    ct, wt, adv, k32 = _win()
    adv.GetNamedSecurityInfoW.argtypes = [wt.LPCWSTR, ct.c_int, wt.DWORD, ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p),
                                          ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p), ct.POINTER(ct.c_void_p)]
    adv.GetNamedSecurityInfoW.restype = wt.DWORD
    owner, dacl, sd = ct.c_void_p(), ct.c_void_p(), ct.c_void_p()
    rc = adv.GetNamedSecurityInfoW(str(path), 1, 0x1 | 0x4, ct.byref(owner), None, ct.byref(dacl), None, ct.byref(sd))
    if rc != 0:
        raise OSError(f"GetNamedSecurityInfo failed ({rc}) for {path}")
    try:
        osid = _sid_str(ct, wt, adv, k32, owner)
        if not dacl.value:
            return osid, [("S-1-1-0", 0x1F01FF, 0)]

        class AclSize(ct.Structure):
            _fields_ = [("AceCount", wt.DWORD), ("AclBytesInUse", wt.DWORD), ("AclBytesFree", wt.DWORD)]
        info = AclSize()
        adv.GetAclInformation.argtypes = [ct.c_void_p, ct.c_void_p, wt.DWORD, ct.c_int]
        if not adv.GetAclInformation(dacl, ct.byref(info), ct.sizeof(info), 2):
            raise OSError("GetAclInformation failed")
        aces: list[Ace] = []
        adv.GetAce.argtypes = [ct.c_void_p, wt.DWORD, ct.POINTER(ct.c_void_p)]
        for i in range(info.AceCount):
            pace = ct.c_void_p()
            if not adv.GetAce(dacl, i, ct.byref(pace)):
                raise OSError("GetAce failed")
            hdr = ct.string_at(pace.value, 8)
            if hdr[0] != 0:  # only ACCESS_ALLOWED_ACE_TYPE grants rights
                continue
            mask = int.from_bytes(hdr[4:8], "little")
            aces.append((_sid_str(ct, wt, adv, k32, pace.value + 8), mask, hdr[1]))
        return osid, aces
    finally:
        k32.LocalFree.argtypes = [ct.c_void_p]
        k32.LocalFree(sd)
