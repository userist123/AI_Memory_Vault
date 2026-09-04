import asyncio
from fastapi.testclient import TestClient
from jarvis.hud.server import app
from jarvis.hud.state_publisher import publish_state

client = TestClient(app)

def test_health_endpoint():
    response = client.get('/health')
    assert response.status_code == 200
    assert response.json() == {'status': 'ok'}

def test_renderer_served():
    response = client.get('/renderer.html')
    assert response.status_code == 200
    assert 'Jarvis HUD' in response.text

def test_websocket_broadcast():
    with client.websocket_connect('/ws') as ws:
        # send a dummy state via the publisher
        asyncio.run(publish_state({'principal': 0.07, 'active_plan_id': None, 'memory_len': 0}))
        data = ws.receive_text()
        state = eval(data)  # simple parsing for test purposes
        assert isinstance(state, dict)
        assert state.get('principal') == 0.07
