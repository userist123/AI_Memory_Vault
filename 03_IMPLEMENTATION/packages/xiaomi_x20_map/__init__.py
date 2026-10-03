"""Read-only contract types for Xiaomi X20+ map work.

This module deliberately contains no device write/update primitive.
"""

from dataclasses import dataclass
from typing import Mapping, Optional


@dataclass(frozen=True)
class RawMapArtifact:
    payload: bytes
    sha256: str
    source: str
    acquisition_id: str
    metadata: Mapping[str, object]


@dataclass(frozen=True)
class NormalizedMap:
    schema_version: str
    source_sha256: str
    width: int
    height: int
    resolution: float
    origin_x: float
    origin_y: float
    geometry: Mapping[str, object]


@dataclass(frozen=True)
class ValidationReport:
    valid: bool
    errors: tuple[str, ...]
    warnings: tuple[str, ...]


READ_ONLY_PROPERTIES = frozenset({
    "map-data",
    "frame-info",
    "map-extend-data",
    "object-name",
    "map-req",
})


def is_read_only_property(name: str) -> bool:
    return name in READ_ONLY_PROPERTIES
