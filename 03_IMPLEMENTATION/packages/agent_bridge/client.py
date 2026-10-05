from __future__ import annotations

import base64
import json
import cryptography.hazmat.primitives.serialization as serialization
from typing import Callable, Mapping
from routing.models import WorkPacket
from .crypto import Ed25519Signer, X25519Envelope, canonical_json

class SecureBridgeTransportError(RuntimeError):
    pass

class SecureBridgeClient:
    """Builds encrypted bridge packets; transport is injected to keep IPC policy-neutral."""
    def __init__(self, bridge_id: str, bridge_public_key, caller_keys: X25519Envelope,
                 send: Callable[[Mapping], Mapping], bridge_signing_public_key):
        self.bridge_id=bridge_id
        self.bridge_public_key=bridge_public_key
        self.caller_keys=caller_keys
        self.send=send
        self.bridge_signing_public_key=bridge_signing_public_key

    def build_request(self, packet: WorkPacket, capability_token: str, nonce: str) -> dict:
        if not packet.task_id or not packet.target_runtime or not packet.target_agent:
            raise SecureBridgeTransportError("packet identity is incomplete")
        aad=f"{self.bridge_id}:{packet.task_id}".encode()
        body=json.dumps(packet.__dict__,ensure_ascii=False,default=str,separators=(",",":")).encode()
        return {"version":1,"bridge_id":self.bridge_id,"task_id":packet.task_id,
                "runtime":packet.target_runtime,"agent":packet.target_agent,"nonce":nonce,
                "capability_token":capability_token,"aad":aad.decode(),
                "payload":X25519Envelope.encrypt_for_public_key(self.bridge_public_key,body,aad=aad),
                "response_public_key":base64.urlsafe_b64encode(
                    self.caller_keys.public_key.public_bytes(serialization.Encoding.Raw,serialization.PublicFormat.Raw)
                ).decode()}

    def dispatch(self, packet: WorkPacket, capability_token: str, nonce: str) -> dict:
        response=self.send(self.build_request(packet,capability_token,nonce))
        try:
            aad=f"{self.bridge_id}:{packet.task_id}".encode()
            signed={k:response[k] for k in ("version","bridge_id","task_id","status","payload")}
            Ed25519Signer.verify(self.bridge_signing_public_key,canonical_json(signed),response["signature"])
            return json.loads(self.caller_keys.decrypt(response["payload"],aad=aad).decode("utf-8"))
        except Exception as exc:
            raise SecureBridgeTransportError("invalid encrypted bridge response") from exc
