from __future__ import annotations

import base64
import json
from typing import Callable, Mapping

from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PublicKey
from routing.models import WorkPacket
import hashlib
import hmac
import re

from .crypto import X25519Envelope
from .policy import BridgePolicy, CapabilityToken, PolicyError
from .replay import ReplayGuard

class BridgeRequestError(ValueError):
    pass


TASK_ID_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")

class SecureBridge:
    def __init__(self, bridge_id: str, recipient: X25519Envelope, verifier,
                 policy: BridgePolicy, replay_guard: ReplayGuard,
                 executors: Mapping[str, Callable[[WorkPacket], Mapping]],
                 signer,
                 max_token_ttl: float = 120.0,
                 clock_skew: float = 30.0,
                 max_packet_bytes: int = 1024 * 1024,
                 max_result_bytes: int = 4 * 1024 * 1024):
        self.bridge_id=bridge_id
        self.recipient=recipient
        self.verifier=verifier
        self.signer=signer
        self.max_token_ttl=max_token_ttl
        self.clock_skew=clock_skew
        self.policy=policy
        self.replay_guard=replay_guard
        self.executors=dict(executors)
        self.max_packet_bytes=max_packet_bytes
        self.max_result_bytes=max_result_bytes

    @staticmethod
    def _public_key(value: str) -> X25519PublicKey:
        try:
            raw=base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))
            return X25519PublicKey.from_public_bytes(raw)
        except Exception as exc:
            raise BridgeRequestError("invalid response public key") from exc

    def _packet(self, raw: bytes) -> WorkPacket:
        if len(raw)>self.max_packet_bytes:
            raise BridgeRequestError("decrypted packet exceeds limit")
        try:
            data=json.loads(raw.decode("utf-8"))
            if not isinstance(data,dict): raise ValueError
            data["acceptance_criteria"]=tuple(data.get("acceptance_criteria",()))
            data["constraints"]=tuple(data.get("constraints",()))
            data["memory_refs"]=tuple(data.get("memory_refs",()))
            data["metadata"]=dict(data.get("metadata",{}))
            packet=WorkPacket(**data)
        except Exception as exc:
            raise BridgeRequestError("invalid work packet") from exc
        if not packet.task_id or not packet.route_id or not packet.goal:
            raise BridgeRequestError("work packet missing required identity")
        if packet.timeout_seconds<=0:
            raise BridgeRequestError("invalid timeout")
        return packet

    def handle(self, request: Mapping) -> dict:
        required={"version","bridge_id","task_id","runtime","agent","nonce","capability_token","aad","payload","response_public_key"}
        try:
            if not required.issubset(request):
                raise BridgeRequestError("incomplete bridge request")
            if request["version"]!=1 or request["bridge_id"]!=self.bridge_id:
                raise BridgeRequestError("bridge identity mismatch")
            task_id=str(request["task_id"])
            if not TASK_ID_RE.fullmatch(task_id):
                raise BridgeRequestError("invalid task id")
            aad=f"{self.bridge_id}:{task_id}".encode()
            if str(request["aad"]).encode()!=aad:
                raise BridgeRequestError("AAD mismatch")
            token=CapabilityToken.verify(request["capability_token"],self.verifier,max_ttl=self.max_token_ttl,clock_skew=self.clock_skew)
            if token.get("bridge_id")!=self.bridge_id or token.get("task_id")!=task_id:
                raise BridgeRequestError("capability identity mismatch")
            if token.get("nonce")!=request["nonce"]:
                raise BridgeRequestError("capability nonce mismatch")
            if token.get("response_public_key") != request["response_public_key"]:
                raise BridgeRequestError("response public key mismatch")
            self.policy.authorize(token,runtime=str(request["runtime"]),agent=str(request["agent"]))
            if not self.replay_guard.claim(task_id,str(request["nonce"])):
                raise BridgeRequestError("replay detected")
            expected=token.get("packet_sha256")
            if not isinstance(expected,str) or len(expected)!=64:
                raise BridgeRequestError("capability token is not bound to a packet")
            raw=self.recipient.decrypt(request["payload"],aad=aad)
            if not hmac.compare_digest(hashlib.sha256(raw).hexdigest(),expected):
                raise BridgeRequestError("packet does not match capability token")
            packet=self._packet(raw)
            if packet.task_id!=task_id or packet.target_runtime!=request["runtime"] or packet.target_agent!=request["agent"]:
                raise BridgeRequestError("packet scope mismatch")
            executor=self.executors.get(packet.target_runtime)
            if executor is None:
                raise BridgeRequestError("runtime executor unavailable")
            result=dict(executor(packet))
            encoded=json.dumps(result,sort_keys=True,separators=(",",":"),ensure_ascii=False).encode()
            if len(encoded)>self.max_result_bytes:
                raise BridgeRequestError("execution result exceeds limit")
            response={
                "version":1,
                "bridge_id":self.bridge_id,
                "task_id":task_id,
                "status":str(result.get("status","completed")),
                "payload":X25519Envelope.encrypt_for_public_key(self._public_key(str(request["response_public_key"])),encoded,aad=aad),
            }
            response["signature"]=self.signer.sign(json.dumps(response,sort_keys=True,separators=(",",":"),ensure_ascii=False).encode())
            return response
        except PolicyError as exc:
            raise BridgeRequestError(str(exc)) from exc
        except BridgeRequestError:
            raise
        except Exception as exc:
            raise BridgeRequestError("secure bridge request rejected") from exc
