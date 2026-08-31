"""Run the optional FastMCP JarvisControls server."""

from __future__ import annotations

import argparse

from jarvis.config import get_settings
from jarvis.iot.fastmcp_server import FastMCPIoTServer, create_fastmcp_server, run_fastmcp
from jarvis.iot.ha_client import HomeAssistantClient


def main() -> None:
    parser = argparse.ArgumentParser(description="Run JarvisControls through FastMCP")
    parser.add_argument("--transport", default="stdio", choices=("stdio", "sse", "streamable-http"))
    args = parser.parse_args()
    settings = get_settings()
    client = HomeAssistantClient(
        base_url=settings.home_assistant_url,
        token=settings.home_assistant_token,
    )
    run_fastmcp(FastMCPIoTServer(client), transport=args.transport)


if __name__ == "__main__":
    main()
