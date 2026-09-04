"""Blueprint entrypoint for the Jarvis assistant."""

from __future__ import annotations

import asyncio

from jarvis.runtime import JarvisRuntime, create_runtime


async def main() -> None:
    runtime = create_runtime()
    await runtime.run_forever()


if __name__ == "__main__":
    asyncio.run(main())
