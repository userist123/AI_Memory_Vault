from xiaomi_x20_map import (
    READ_ONLY_PROPERTIES,
    RawMapArtifact,
    NormalizedMap,
    ValidationReport,
    is_read_only_property,
)


def test_known_map_properties_are_read_only():
    assert "map-data" in READ_ONLY_PROPERTIES
    assert "frame-info" in READ_ONLY_PROPERTIES
    assert "map-extend-data" in READ_ONLY_PROPERTIES
    assert "object-name" in READ_ONLY_PROPERTIES
    assert "map-req" in READ_ONLY_PROPERTIES


def test_mutation_property_is_not_read_only():
    assert not is_read_only_property("update-map")
    assert not is_read_only_property("set-clean-area")


def test_contract_types_are_immutable():
    artifact = RawMapArtifact(
        payload=b"map",
        sha256="abc",
        source="xiaomi",
        acquisition_id="a1",
        metadata={},
    )
    normalized = NormalizedMap(
        schema_version="0.1.0",
        source_sha256="abc",
        width=10,
        height=10,
        resolution=0.05,
        origin_x=0.0,
        origin_y=0.0,
        geometry={},
    )
    report = ValidationReport(valid=True, errors=(), warnings=())

    for value, field, replacement in (
        (artifact, "sha256", "def"),
        (normalized, "width", 20),
        (report, "valid", False),
    ):
        try:
            setattr(value, field, replacement)
        except Exception:
            pass
        else:
            raise AssertionError("contract type must be immutable")
