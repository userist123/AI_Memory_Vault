import json
import time
import pytest
from agent_bridge.bridge import SecureBridge, BridgeRequestError
from agent_bridge.crypto import Ed25519Signer, X25519Envelope
from agent_bridge.policy import BridgePolicy, CapabilityToken
from agent_bridge.replay import ReplayGuard
from routing.models import WorkPacket

def packet():
    return WorkPacket("task-1","route-1","router","visual_architect","antigravity","visual-v1","command","secret goal",("evidence",),(),(),60,{"profile_ref":"visual-v1"})

def bridge():
    keys=X25519Envelope.generate(); signer=Ed25519Signer.generate()
    return SecureBridge("bridge-1",keys,signer.public_key,
        BridgePolicy(("antigravity",),("visual_architect",)),ReplayGuard(),
        {"antigravity":lambda p: {"status":"completed","response":"ok"}}),keys,signer

def request(keys,signer,p,nonce="n1",agent="visual_architect"):
    token=CapabilityToken.issue(signer.private_key,bridge_id="bridge-1",task_id=p.task_id,
        runtime="antigravity",agent=agent,permissions=("execute",),expires_at=time.time()+60,nonce=nonce)
    aad=f"bridge-1:{p.task_id}".encode()
    body=json.dumps(p.__dict__,default=str).encode()
    return {"version":1,"bridge_id":"bridge-1","task_id":p.task_id,"runtime":"antigravity",
        "agent":agent,"nonce":nonce,"capability_token":token,"aad":aad.decode(),
        "payload":keys.encrypt(body,aad=aad)}

def test_secure_bridge_executes_encrypted_packet():
    b,k,s=bridge(); req=request(k,s,packet())
    assert packet().goal.encode() not in json.dumps(req).encode()
    out=b.handle(req)
    assert out["status"]=="completed" and out["task_id"]=="task-1"

def test_secure_bridge_rejects_replay():
    b,k,s=bridge(); req=request(k,s,packet(),nonce="n2"); b.handle(req)
    with pytest.raises(BridgeRequestError): b.handle(req)

def test_secure_bridge_rejects_scope():
    b,k,s=bridge(); req=request(k,s,packet(),agent="engineering_reviewer")
    with pytest.raises(BridgeRequestError): b.handle(req)

def test_secure_bridge_rejects_arbitrary_commands():
    b,_,_=bridge()
    with pytest.raises(BridgeRequestError): b.handle({"command":"powershell -enc SECRET"})
