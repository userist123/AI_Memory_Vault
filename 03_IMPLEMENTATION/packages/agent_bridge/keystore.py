from __future__ import annotations

import ctypes
import os
from ctypes import wintypes
from pathlib import Path


class KeyStoreError(RuntimeError):
    pass


if os.name == "nt":
    class _Blob(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]

    _crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _crypt32.CryptProtectData.argtypes = [
        ctypes.POINTER(_Blob), wintypes.LPCWSTR, ctypes.POINTER(_Blob),
        wintypes.LPCWSTR, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(_Blob)
    ]
    _crypt32.CryptProtectData.restype = wintypes.BOOL
    _crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(_Blob), ctypes.POINTER(wintypes.LPWSTR), ctypes.POINTER(_Blob),
        ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(_Blob)
    ]
    _crypt32.CryptUnprotectData.restype = wintypes.BOOL
    _kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    _kernel32.LocalFree.restype = ctypes.c_void_p


def _blob(data: bytes):
    raw = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
    return _Blob(len(data), raw)


def _bytes_from_blob(blob) -> bytes:
    return ctypes.string_at(blob.pbData, blob.cbData)


def dpapi_protect(data: bytes, description: str = "AI Memory Vault bridge key") -> bytes:
    if os.name != "nt":
        raise KeyStoreError("DPAPI key storage requires Windows")
    source = _blob(data)
    out = _Blob()
    if not _crypt32.CryptProtectData(
        ctypes.byref(source), description, None, None, None, 0, ctypes.byref(out)
    ):
        raise KeyStoreError(f"CryptProtectData failed: {ctypes.get_last_error()}")
    try:
        return _bytes_from_blob(out)
    finally:
        _kernel32.LocalFree(out.pbData)


def dpapi_unprotect(data: bytes) -> bytes:
    if os.name != "nt":
        raise KeyStoreError("DPAPI key storage requires Windows")
    source = _blob(data)
    out = _Blob()
    description = wintypes.LPWSTR()
    if not _crypt32.CryptUnprotectData(
        ctypes.byref(source), ctypes.byref(description), None, None, None, 0, ctypes.byref(out)
    ):
        raise KeyStoreError(f"CryptUnprotectData failed: {ctypes.get_last_error()}")
    try:
        return _bytes_from_blob(out)
    finally:
        if description:
            _kernel32.LocalFree(description)
        _kernel32.LocalFree(out.pbData)


class DpapiKeyStore:
    """Windows-current-user protected binary key store; never writes plaintext keys."""

    def __init__(self, path: str | Path):
        self.path = Path(path)

    def save(self, key: bytes) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        protected = dpapi_protect(key)
        temp = self.path.with_suffix(self.path.suffix + ".tmp")
        temp.write_bytes(protected)
        os.replace(temp, self.path)

    def load(self) -> bytes:
        if not self.path.is_file():
            raise KeyStoreError(f"key not found: {self.path}")
        return dpapi_unprotect(self.path.read_bytes())
