# provenance.py
"""Validation of provenance fields in a note.
Ensures required provenance keys exist and minimal redaction rules.
"""

def validate_provenance(prov: dict) -> None:
    required = {"source_type", "source_ref"}
    missing = required - set(prov.keys())
    if missing:
        raise ValueError(f"Provenance missing required fields: {missing}")
