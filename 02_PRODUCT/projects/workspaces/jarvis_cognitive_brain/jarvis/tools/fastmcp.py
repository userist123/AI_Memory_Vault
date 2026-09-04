"""Compatibility exports for JarvisControls FastMCP integration."""

from jarvis.iot.fastmcp_server import (
    FastMCPIoTServer,
    JarvisControlsServer,
    JarvisControls,
    create_fastmcp_server,
    run_fastmcp,
)

__all__ = [
    "FastMCPIoTServer",
    "JarvisControlsServer",
    "JarvisControls",
    "create_fastmcp_server",
    "run_fastmcp",
]
