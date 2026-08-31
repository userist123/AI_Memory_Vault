"""Run Jarvis runtime, continuous voice pipeline and the cognitive HUD."""

from __future__ import annotations

import argparse
import asyncio
import os

import uvicorn

from jarvis.hud.server import app as hud_app
from jarvis.hud.state_publisher import publish_state
from jarvis.runtime import create_runtime


async def serve_hud(host: str, port: int) -> None:
    config = uvicorn.Config(hud_app, host=host, port=port, log_level="info")
    await uvicorn.Server(config).serve()


async def main() -> None:
    parser = argparse.ArgumentParser(description="Run Jarvis Cognitive Brain")
    parser.add_argument("--hud-host", default=os.getenv("JARVIS_HUD_HOST", "127.0.0.1"))
    parser.add_argument("--hud-port", type=int, default=int(os.getenv("JARVIS_HUD_PORT", "8001")))
    parser.add_argument("--no-audio", action="store_true")
    args = parser.parse_args()

    runtime = create_runtime()
    runtime.executive.register_state_callback(publish_state)
    hud_task = asyncio.create_task(serve_hud(args.hud_host, args.hud_port))
    audio_task = None if args.no_audio else asyncio.create_task(runtime.run_forever())
    try:
        tasks = [hud_task]
        if audio_task:
            tasks.append(audio_task)
        await asyncio.gather(*tasks)
    finally:
        runtime.stop()
        hud_task.cancel()
        if audio_task:
            audio_task.cancel()
        await asyncio.gather(hud_task, *( [audio_task] if audio_task else [] ), return_exceptions=True)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
