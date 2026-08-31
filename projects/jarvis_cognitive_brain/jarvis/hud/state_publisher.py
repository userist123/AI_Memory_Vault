'''HUD state publisher module.

Provides an async function to broadcast executive state to all connected HUD clients.
'''

from .ws_manager import manager

async def publish_state(state: dict) -> None:
    '''Broadcast executive state to all HUD WebSocket clients.'''
    await manager.broadcast(state)
