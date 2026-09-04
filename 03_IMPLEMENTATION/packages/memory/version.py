# cognitive_core/version.py
"""Version abstraction utilities for Technology‑aware memory handling.

Provides:
* ``TechnologyIdentity`` – name of the technology/product (e.g. "Python").
* ``Version`` – major/minor/patch representation.
* ``VersionRange`` – exact version, open‑ended range (e.g. "7.x"), or unknown.
* ``parse_technology_version`` – parse a free‑form string into (TechnologyIdentity, VersionRange).
* ``is_compatible`` – determine if a candidate version range satisfies a request.

Only the Python standard library is used (``re`` and ``dataclasses``).
"""

import re
from dataclasses import dataclass
from typing import Optional, Tuple


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class TechnologyIdentity:
    """Canonical name of a technology/product.

    The ``name`` is normalized to title case (e.g. "Python", "PowerShell").
    """
    name: str

    def __str__(self) -> str:
        return self.name


@dataclass(frozen=True)
class Version:
    """Semantic version representation.

    ``major`` is required; ``minor`` and ``patch`` may be ``None``.
    """
    major: int
    minor: Optional[int] = None
    patch: Optional[int] = None

    def __str__(self) -> str:
        parts = [str(self.major)]
        if self.minor is not None:
            parts.append(str(self.minor))
        if self.patch is not None:
            parts.append(str(self.patch))
        return ".".join(parts)

    def matches(self, other: "Version") -> bool:
        """Exact match – all defined components must be equal.
        ``None`` components are treated as wildcards.
        """
        if self.major != other.major:
            return False
        if self.minor is not None and other.minor is not None and self.minor != other.minor:
            return False
        if self.patch is not None and other.patch is not None and self.patch != other.patch:
            return False
        return True


@dataclass(frozen=True)
class VersionRange:
    """Represents a version specification.

    * ``exact`` – a concrete ``Version`` instance.
    * ``prefix`` – a string like "7.x" meaning any version whose major equals 7.
    * ``unknown`` – used when parsing fails.
    """
    exact: Optional[Version] = None
    prefix: Optional[int] = None  # major version when using "X.x" notation
    unknown: bool = False

    def __str__(self) -> str:
        if self.unknown:
            return "unknown"
        if self.exact:
            return str(self.exact)
        if self.prefix is not None:
            return f"{self.prefix}.x"
        return ""

    def matches(self, candidate: "VersionRange") -> bool:
        """Compatibility check between a *request* and a *candidate*.

        The request may be more specific than the candidate. Compatibility rules:
        * If the request is unknown – it matches anything.
        * If the request is an exact version, the candidate must have the same exact version.
        * If the request is a prefix (e.g. ``7.x``), the candidate must have the same major.
        * If the request is exact and the candidate is a prefix, the major must match.
        """
        if self.unknown:
            return True
        if self.exact:
            if candidate.exact:
                return self.exact.matches(candidate.exact)
            if candidate.prefix is not None:
                return self.exact.major == candidate.prefix
            return False
        if self.prefix is not None:
            if candidate.exact:
                return candidate.exact.major == self.prefix
            if candidate.prefix is not None:
                return candidate.prefix == self.prefix
            return False
        return False


# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------

# Regex patterns for the supported technologies.
_TECH_PATTERNS = [
    (r"python\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?(?:\.(?P<patch>\d+))?", "Python"),
    (r"powershell\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", "PowerShell"),
    (r"windows\s*server\s*(?P<major>\d{4})(?:\s*R2)?", "Windows Server"),
    (r"\.net\s*framework\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", ".NET Framework"),
    (r"\.net\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", ".NET"),
]

# Helper to build a Version (or prefix) from regex groups.
def _build_version(groups: dict) -> VersionRange:
    major = groups.get("major")
    minor = groups.get("minor")
    patch = groups.get("patch")
    if major is None:
        return VersionRange(unknown=True)
    try:
        major_i = int(major)
    except ValueError:
        return VersionRange(unknown=True)
    # If minor is missing, treat this as an exact version with only major (e.g., Windows Server 2012, .NET 8)
    if minor is None:
        return VersionRange(exact=Version(major_i))
    minor_i = int(minor)
    patch_i = int(patch) if patch is not None else None
    return VersionRange(exact=Version(major_i, minor_i, patch_i))

def parse_technology_version(text: str) -> Tuple[TechnologyIdentity, VersionRange]:
    """Parse a free‑form description of a technology and its version.

    Returns a ``(TechnologyIdentity, VersionRange)`` tuple. If parsing fails, the
    ``TechnologyIdentity`` name is ``"unknown"`` and ``VersionRange`` is marked as
    unknown.
    """
    lowered = text.lower().strip()
    for pattern, tech_name in _TECH_PATTERNS:
        match = re.search(pattern, lowered)
        if match:
            groups = match.groupdict()
            # Special handling for Windows Server R2 which denotes a separate version.
            if tech_name == "Windows Server" and "r2" in lowered:
                # Treat 2012 R2 as version 2012.2 (minor 2) for compatibility.
                groups["minor"] = "2"
            # Special handling for PowerShell prefix notation (e.g., "7.x").
            if tech_name == "PowerShell" and ".x" in lowered:
                return TechnologyIdentity(tech_name), VersionRange(prefix=int(groups["major"]))
            vr = _build_version(groups)
            return TechnologyIdentity(tech_name), vr
    # No pattern matched – unknown technology/version.
    return TechnologyIdentity("unknown"), VersionRange(unknown=True)

def is_compatible(request: VersionRange, candidate: VersionRange) -> bool:
    """Public helper – delegates to ``VersionRange.matches``.
    """
    return request.matches(candidate)

# End of module
