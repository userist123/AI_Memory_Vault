"""Owner brand-kit routes (W5): ``GET/PUT /projects/<id>/brand``. The owner picks the launcher glyph, colours, caption and
version of the Android app; the next android build uses them. Same data the MCP ``brand_kit`` tool edits; offline.
Every route sits behind the owner bearer token (BoundaryMiddleware); bodies are strict (extra fields refused)."""
from __future__ import annotations

import base64
import re
from typing import Annotated, Any

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict, Field

from aas_build import brand, service
from studio_core.errors import ValidationFailed
from studio_core.policy import Actor

API = "/api/v1"
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
Hex = Annotated[str, Field(pattern=r"^#[0-9a-fA-F]{6}$")]


class BrandReq(BaseModel):
    model_config = ConfigDict(extra="forbid")
    glyph: Annotated[str, Field(pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$", max_length=30)]
    background: Hex
    foreground: Hex | None = None
    caption: Annotated[str, Field(max_length=60)] = ""
    versionMajor: Annotated[int, Field(ge=0, le=999)] = 1  # noqa: N815
    versionMinor: Annotated[int, Field(ge=0, le=999)] = 0  # noqa: N815


def _pid(pid: str) -> str:
    if not _PID.match(pid):
        raise ValidationFailed("invalid project id")
    return pid


def register(app: FastAPI, studio: Any, owner: Actor) -> None:
    store = brand.BrandStore(studio.settings.data_dir)

    def view(pid: str) -> dict[str, Any]:
        proj = studio.get_project(owner, pid, with_components=False)
        cfg = service._brand_config(studio, pid, store)  # noqa: SLF001 - saved kit, else tokens, else defaults
        code = store.next_version_code(pid)
        glyphs = [{"name": n, "svg": (brand.GLYPH_DIR / f"{n}.svg").read_text(encoding="utf-8")} for n in brand.glyph_names()]
        try:
            preview = base64.b64encode(brand.preview_sheet(cfg, str(proj.get("name", "")))).decode("ascii")
        except brand.BrandError:
            preview = ""
        return {"kit": cfg.to_json(), "colours": dict(zip(("background", "foreground"), cfg.colours(), strict=True)),
                "appLabel": brand.app_label(str(proj.get("name", ""))), "nextVersionCode": code,
                "nextVersionName": brand.semver(cfg, code), "glyphs": glyphs, "previewPng": preview}

    @app.get(f"{API}/projects/{{pid}}/brand")
    def brand_get(pid: str) -> dict[str, Any]:
        return view(_pid(pid))

    @app.put(f"{API}/projects/{{pid}}/brand")
    def brand_put(pid: str, b: BrandReq) -> dict[str, Any]:
        _pid(pid)
        studio.get_project(owner, pid, with_components=False)
        try:
            store.save_config(pid, brand.BrandConfig(glyph=b.glyph, background=b.background, foreground=b.foreground,
                                                     caption=b.caption, version_major=b.versionMajor,
                                                     version_minor=b.versionMinor))
        except brand.BrandError as e:
            raise ValidationFailed(str(e)) from e
        return view(pid)
