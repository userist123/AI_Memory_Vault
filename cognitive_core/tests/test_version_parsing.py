import pytest
from cognitive_core.version import parse_technology_version, is_compatible, TechnologyIdentity, VersionRange, Version

@pytest.mark.parametrize(
    "input_str,expected_tech,expected_range",
    [
        ("Python 3.11", "Python", VersionRange(exact=Version(3, 11))),
        ("Python 3.12", "Python", VersionRange(exact=Version(3, 12))),
        ("Python 3.13", "Python", VersionRange(exact=Version(3, 13))),
        ("PowerShell 5.1", "PowerShell", VersionRange(exact=Version(5, 1))),
        ("PowerShell 7.x", "PowerShell", VersionRange(prefix=7)),
        ("Windows Server 2012", "Windows Server", VersionRange(exact=Version(2012))),
        ("Windows Server 2012 R2", "Windows Server", VersionRange(exact=Version(2012, 2))),
        ("Windows Server 2016", "Windows Server", VersionRange(exact=Version(2016))),
        ("Windows Server 2019", "Windows Server", VersionRange(exact=Version(2019))),
        ("Windows Server 2022", "Windows Server", VersionRange(exact=Version(2022))),
        (".NET Framework 4.8", ".NET Framework", VersionRange(exact=Version(4, 8))),
        (".NET 8", ".NET", VersionRange(exact=Version(8))),
        (".NET 9", ".NET", VersionRange(exact=Version(9))),
        ("unknown tech", "unknown", VersionRange(unknown=True)),
    ]
)
def test_parse_technology_version(input_str, expected_tech, expected_range):
    tech, vr = parse_technology_version(input_str)
    assert isinstance(tech, TechnologyIdentity)
    assert tech.name == expected_tech
    assert vr == expected_range

def test_version_compatibility():
    # Exact matches
    req = VersionRange(exact=Version(7, 1))
    cand = VersionRange(exact=Version(7, 1))
    assert is_compatible(req, cand)
    # Prefix matches exact candidate
    req_prefix = VersionRange(prefix=7)
    cand_exact = VersionRange(exact=Version(7, 4))
    assert is_compatible(req_prefix, cand_exact)
    # Exact request matches prefix candidate (major equal)
    req_exact = VersionRange(exact=Version(7, 2))
    cand_prefix = VersionRange(prefix=7)
    assert is_compatible(req_exact, cand_prefix)
    # Different major should be false
    req = VersionRange(prefix=5)
    cand = VersionRange(exact=Version(7, 0))
    assert not is_compatible(req, cand)
    # Unknown request matches anything
    req = VersionRange(unknown=True)
    cand = VersionRange(exact=Version(3, 11))
    assert is_compatible(req, cand)
