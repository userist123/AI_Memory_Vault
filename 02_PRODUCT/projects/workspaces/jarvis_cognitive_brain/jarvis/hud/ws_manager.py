import asyncio
import json
from typing import List
from fastapi import WebSocket, WebSocketDisconnect

class WebSocketManager:
    """Manage active WebSocket connections and broadcast JSON messages."""
    def __init__(self) -> None:
        self.active_connections: List[WebSocket] = []
        self.lock = asyncio.Lock()

    async def connect(self, websocket: WebSocket) -> None:
        await websocket.accept()
        async with self.lock:
            self.active_connections.append(websocket)

    async def disconnect(self, websocket: WebSocket) -> None:
        async with self.lock:
            if websocket in self.active_connections:
                self.active_connections.remove(websocket)

    async def broadcast(self, message: dict) -> None:
        """Send a JSON serialisable ``message`` to all connected clients."""
        payload = json.dumps({key: value for key, value in message.items() if value is not None})
        async with self.lock:
            for connection in list(self.active_connections):
                try:
                    await connection.send_text(payload)
                except Exception:
                    await self.disconnect(connection)

# Global manager instance used by HUD server and state publisher
manager = WebSocketManager()

