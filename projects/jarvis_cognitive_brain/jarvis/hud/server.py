import asyncio
from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Request
from fastapi.staticfiles import StaticFiles
from fastapi.responses import HTMLResponse

from .ws_manager import manager

app = FastAPI()

# Mount static assets (css, js, html)
from pathlib import Path
static_dir = Path(__file__).parent / 'static'
app.mount('/static', StaticFiles(directory=str(static_dir)), name='static')

@app.get('/renderer.html', response_class=HTMLResponse)
async def get_renderer():
    renderer_path = Path(__file__).parent / 'static' / 'renderer.html'
    with open(renderer_path, 'r', encoding='utf-8') as f:
        return HTMLResponse(content=f.read())

@app.get('/health')
async def health():
    return {'status': 'ok'}

@app.websocket('/ws')
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            await asyncio.sleep(0)
    except WebSocketDisconnect:
        await manager.disconnect(websocket)
    except Exception:
        await manager.disconnect(websocket)

@app.get('/', response_class=HTMLResponse)
async def root():
    return await get_renderer()
