import base64
import json
import time
import pytest
from cryptography.hazmat.primitives import serialization
from agent_bridge.bridge import SecureBridge, BridgeRequestError
from agent_bridge.crypto import Ed25519Signer, X25519Envelope, packet_bytes, packet_sha256
from agent_bridge.policy import BridgePolicy, CapabilityToken
from agent_bridge.replay import ReplayGuard
from routing.models import WorkPacket

def packet():
    return WorkPacket("task-1","route-1","router","visual_architect","antigravity","visual-v1","command","secret goal",("evidence",),(),(),60,{"profile_ref":"visual-v1"})

def bridge():
    keys=X25519Envelope.generate(); signer=Ed25519Signer.generate(); bridge_signer=Ed25519Signer.generate()
    return SecureBridge("bridge-1",keys,signer.public_key,
        BridgePolicy(("antigravity",),("visual_architect",)),ReplayGuard(),
        {"antigravity":lambda p: {"status":"completed","response":"ok"}},bridge_signer),keys,signer,bridge_signer

def request(keys,signer,p,nonce="n1",agent="visual_architect",token_packet=None):
    response_key=base64.urlsafe_b64encode(keys.public_key.public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)).decode()
    token=CapabilityToken.issue(signer.private_key,bridge_id="bridge-1",task_id=p.task_id,
        runtime="antigravity",agent=agent,permissions=("execute",),expires_at=time.time()+60,nonce=nonce,response_public_key=response_key,
        packet_sha256=packet_sha256(token_packet or p))
    aad=f"bridge-1:{p.task_id}".encode()
    body=packet_bytes(p)
    return {"version":1,"bridge_id":"bridge-1","task_id":p.task_id,"runtime":"antigravity",
        "agent":agent,"nonce":nonce,"capability_token":token,"aad":aad.decode(),
        "payload":keys.encrypt(body,aad=aad),"response_public_key":response_key}

def test_secure_bridge_executes_encrypted_packet():
    b,k,s,_=bridge(); req=request(k,s,packet())
    assert packet().goal.encode() not in json.dumps(req).encode()
    out=b.handle(req)
    assert out["status"]=="completed" and out["task_id"]=="task-1"

def test_secure_bridge_rejects_replay():
    b,k,s,_=bridge(); req=request(k,s,packet(),nonce="n2"); b.handle(req)
    with pytest.raises(BridgeRequestError): b.handle(req)

def test_secure_bridge_rejects_scope():
    b,k,s,_=bridge(); req=request(k,s,packet(),agent="engineering_reviewer")
    with pytest.raises(BridgeRequestError): b.handle(req)

def test_secure_bridge_rejects_arbitrary_commands():
    b,_,_,_=bridge()
    with pytest.raises(BridgeRequestError): b.handle({"command":"powershell -enc SECRET"})

def test_secure_bridge_rejects_replaced_response_public_key():
    b,keys,signer,_=bridge(); p=packet()
    req=request(keys,signer,p,nonce="key-nonce")
    req["response_public_key"]="ATTACKER-KEY"
    with pytest.raises(BridgeRequestError): b.handle(req)

def test_secure_bridge_client_rejects_altered_response_signature():
    from agent_bridge.client import SecureBridgeClient, SecureBridgeTransportError
    b,keys,signer,bridge_signer=bridge(); p=packet()
    caller=X25519Envelope.generate()
    response_key=base64.urlsafe_b64encode(caller.public_key.public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)).decode()
    token=CapabilityToken.issue(signer.private_key,bridge_id="bridge-1",task_id=p.task_id,
        runtime="antigravity",agent="visual_architect",permissions=("execute",),expires_at=time.time()+60,
        nonce="client-sig",response_public_key=response_key,packet_sha256=packet_sha256(p))
    req=request(keys,signer,p,nonce="client-sig"); req["response_public_key"]=response_key; req["capability_token"]=token
    def send(_):
        out=b.handle(req); out["signature"]="invalid"; return out
    client=SecureBridgeClient("bridge-1",keys.public_key,caller,send,bridge_signer.public_key)
    with pytest.raises(SecureBridgeTransportError): client.dispatch(p,token,"client-sig")


# ── regressions for the PR #211 review ──────────────────────────────────────────────────
def test_token_is_bound_to_one_packet_a_substituted_packet_is_refused():
    b,keys,signer,_=bridge()
    original=packet()
    forged=WorkPacket("task-1","route-1","router","visual_architect","antigravity","visual-v1","command",
                      "exfiltrate everything",("evidence",),(),(),60,{"profile_ref":"visual-v1"})
    # attacker keeps the captured token (issued for `original`) and re-encrypts `forged`
    req=request(keys,signer,forged,nonce="sub-1",token_packet=original)
    with pytest.raises(BridgeRequestError, match="does not match capability token"):
        b.handle(req)


def test_a_token_without_packet_binding_cannot_be_issued():
    from agent_bridge.policy import PolicyError
    signer=Ed25519Signer.generate()
    with pytest.raises(PolicyError):
        CapabilityToken.issue(signer.private_key,bridge_id="bridge-1",task_id="t",runtime="antigravity",
            agent="visual_architect",permissions=("execute",),expires_at=time.time()+60,nonce="n",
            response_public_key="k",packet_sha256="")


@pytest.mark.parametrize("task_id",["../../x","a/b","..","x"*65,"C:\\x",""])  # hygiene: intentional-absolute-path
def test_unsafe_task_ids_are_refused_everywhere(task_id):
    with pytest.raises(ValueError):
        WorkPacket(task_id,"route-1","router","visual_architect","antigravity","v","command","g")
    b,keys,signer,_=bridge()
    req=request(keys,signer,packet(),nonce="tid")
    req["task_id"]=task_id
    with pytest.raises(BridgeRequestError):
        b.handle(req)


def test_replay_guard_refuses_new_claims_when_full_instead_of_evicting(tmp_path):
    for guard in (ReplayGuard(max_entries=2), ReplayGuard(max_entries=2, path=tmp_path/"r.sqlite")):
        assert guard.claim("t","victim",now=1000)
        assert guard.claim("t","flood-1",now=1001)
        assert guard.claim("t","flood-2",now=1002) is False      # full: refuse, do not evict
        assert guard.claim("t","victim",now=1003) is False       # the victim nonce is still remembered
        assert guard.claim("t","later",now=1000+guard.ttl_seconds+5)  # expiry frees room


def test_client_rejects_a_validly_signed_response_for_another_task():
    from agent_bridge.client import SecureBridgeClient, SecureBridgeTransportError
    b,keys,signer,bridge_signer=bridge()
    caller=X25519Envelope.generate()
    p=packet()
    other=WorkPacket("task-2","route-1","router","visual_architect","antigravity","visual-v1","command","other",
                     ("evidence",),(),(),60,{"profile_ref":"visual-v1"})
    response_key=base64.urlsafe_b64encode(caller.public_key.public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)).decode()
    client=SecureBridgeClient("bridge-1",keys.public_key,caller,lambda r: None,bridge_signer.public_key)
    def issue(pk, nonce):
        return CapabilityToken.issue(signer.private_key,bridge_id="bridge-1",task_id=pk.task_id,runtime="antigravity",
            agent="visual_architect",permissions=("execute",),expires_at=time.time()+60,nonce=nonce,
            response_public_key=response_key,packet_sha256=packet_sha256(pk))
    other_response=b.handle(client.build_request(other,issue(other,"o1"),"o1"))
    client.send=lambda r: other_response
    with pytest.raises(SecureBridgeTransportError):
        client.dispatch(p,issue(p,"p1"),"p1")


def test_bridge_config_is_fail_closed(tmp_path):
    import copy
    from pathlib import Path
    from agent_bridge.config import BridgeConfigError, load_bridge_config
    repo=Path(__file__).resolve().parents[1]
    good=json.loads((repo/"04_CONFIG"/"agent_bridge.json").read_text(encoding="utf-8"))
    cfg=load_bridge_config(repo/"04_CONFIG"/"agent_bridge.json")
    assert cfg.bridge_id=="local-windows-bridge" and cfg.allowed_runtimes==("antigravity",)
    keys=X25519Envelope.generate(); signer=Ed25519Signer.generate()
    built=cfg.build_bridge(recipient=keys,verifier=signer.public_key,signer=Ed25519Signer.generate(),
                           replay_guard=ReplayGuard(),executors={"antigravity":lambda p:{"status":"completed"}})
    assert built.max_packet_bytes==good["bridge"]["max_packet_bytes"]
    with pytest.raises(BridgeConfigError):
        cfg.build_bridge(recipient=keys,verifier=signer.public_key,signer=signer,replay_guard=ReplayGuard(),
                         executors={"codex":lambda p:{}})
    mutations=[("bridge","require_capability_token",False),("bridge","require_replay_protection",False),
               ("bridge","require_authenticated_packet",None),("antigravity","dangerously_skip_permissions",True),
               ("bridge","pipe_name","Global\\pipe"),("security","maximum_ttl_seconds",3600),(None,"schema_version",2)]
    for section,key,value in mutations:
        bad=copy.deepcopy(good)
        target=bad if section is None else bad[section]
        target[key]=value
        path=tmp_path/"bad.json"; path.write_text(json.dumps(bad),encoding="utf-8")
        with pytest.raises(BridgeConfigError):
            load_bridge_config(path)
    with pytest.raises(BridgeConfigError):
        load_bridge_config(tmp_path/"missing.json")
