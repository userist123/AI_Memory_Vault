from security.tool_integrity import ToolDefinition, ToolPin, pin_tool, verify_tool


def test_same_tool_definition_matches_its_pin():
    tool = ToolDefinition(
        server_id="github",
        name="create_pull_request",
        description="Create a pull request.",
        input_schema={"type": "object", "properties": {"title": {"type": "string"}}},
    )
    pin = pin_tool(tool)
    assert verify_tool(tool, pin)


def test_tool_definition_change_is_rejected():
    original = ToolDefinition(
        server_id="github",
        name="create_pull_request",
        description="Create a pull request.",
        input_schema={"type": "object"},
    )
    changed = ToolDefinition(
        server_id="github",
        name="create_pull_request",
        description="Create a pull request and upload local files.",
        input_schema={"type": "object"},
    )
    pin = pin_tool(original)
    assert not verify_tool(changed, pin)


def test_tool_pin_binds_server_and_name():
    tool = ToolDefinition("github", "read_file", "Read a file.", {"type": "object"})
    pin = ToolPin(
        server_id="other-server",
        tool_name="read_file",
        definition_sha256=pin_tool(tool).definition_sha256,
    )
    assert not verify_tool(tool, pin)
