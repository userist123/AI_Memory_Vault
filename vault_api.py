import uuid
from datetime import datetime, timezone
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any, Optional

from memory_controller.controller import MemoryController
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.authorizer import Principal

app = FastAPI(title="AI Memory Vault - Exo-Cortex", version="1.0")

# Inițializăm baza de date SQLite în modul WAL
storage = SQLiteStorageEngine("vault_memory.sqlite3", wal_mode=True)
controller = MemoryController(storage)

class ProposeRequest(BaseModel):
    category: str
    content: str
    tags: List[str] = []
    relations: List[Dict[str, Any]] = []

@app.post("/memory/propose")
def propose_memory(req: ProposeRequest):
    note_id = str(uuid.uuid4())
    current_date = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    
    # P0-P15: Forțăm granițele de încredere
    note_data = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",          # Restricționat strict
        "category": req.category,
        "tags": req.tags,
        "created": current_date,
        "updated": current_date,
        "provenance": {
            "source_type": "ai",        # Proveniență strictă controlată de API
            "source_ref": "ollama_exo_agent"
        },
        "confidence": "medium",
        "verification": "unverified",   # Fără auto-verificare
        "relations": req.relations,
        "content": req.content
    }
    
    try:
        res_id = controller.propose(Principal.AI_AGENT, note_data)
        return {"status": "success", "note_id": res_id, "message": "Propunere salvata pentru review."}
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Eroare de securitate: {str(e)}")

@app.get("/memory/search")
def search_memory(query: str):
    try:
        results = controller.search(principal=Principal.AI_AGENT, query=query)
        return {"status": "success", "results": results}
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

class FinancialNoteRequest(BaseModel):
    title: str = ""
    symbol: Optional[str] = None
    category: str = "financial"
    tags: List[str] = []
    indicators: Dict[str, Any] = {}
    signals: List[Dict[str, Any]] = []
    risk_metrics: Dict[str, Any] = {}
    narrative: str = ""
    raw_content: str = ""
    content: Optional[str] = None
    relations: List[Dict[str, Any]] = []

@app.post("/financial_note")
def ingest_financial_note_endpoint(req: FinancialNoteRequest):
    note_id = str(uuid.uuid4())
    current_date = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    rendered_content = (
        req.raw_content
        or req.narrative
        or req.content
        or f"# {req.title}\nSymbol: {req.symbol or ''}\n\n{req.narrative}"
    )
    note_data = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": req.category,
        "tags": req.tags or ["finance", (req.symbol or "").lower()],
        "created": current_date,
        "updated": current_date,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_note_api",
            "source_date": current_date,
            "provenance_status": "complete"
        },
        "confidence": "high",
        "verification": "partially_verified",
        "relations": req.relations,
        "content": rendered_content
    }
    try:
        res_id = controller.propose(Principal.HUMAN, note_data)
        return {"status": "success", "note_id": res_id, "message": "Financial note ingested."}
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

@app.get("/search")
@app.get("/api/v1/search")
def unified_search_endpoint(
    q: Optional[str] = None,
    query: Optional[str] = None,
    symbol: Optional[str] = None,
    category: Optional[str] = None,
    limit: int = 10
):
    search_q = q or query or ""
    try:
        pack = controller.search_financial(
            principal=Principal.AI_AGENT,
            query=search_q,
            symbol=symbol,
            category=category,
            limit=limit,
            page_size=limit
        )
        return {
            "status": "success",
            "results": pack.get("results", []),
            "total_matched": pack.get("total_matched", len(pack.get("results", [])))
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

class FinancialSearchRequest(BaseModel):
    query: str = ""
    symbol: Optional[str] = None
    symbols: Optional[List[str]] = None
    asset_symbol: Optional[str] = None
    category: Optional[str] = None
    asset_classes: Optional[List[str]] = None
    min_confidence: Optional[str] = None
    confidence_min: Optional[str] = None
    verification_state: Optional[str] = None
    verification_states: Optional[List[str]] = None
    date_from: Optional[str] = None
    date_to: Optional[str] = None
    types: Optional[List[str]] = None
    lifecycles: Optional[List[str]] = None
    limit: int = 10
    page_size: Optional[int] = None
    page_token: Optional[str] = None
    disclosure_level: Optional[str] = None

@app.get("/memory/financial/search")
def get_financial_search(
    query: str = "",
    symbol: Optional[str] = None,
    category: Optional[str] = None,
    min_confidence: Optional[str] = None,
    verification_state: Optional[str] = None,
    date_from: Optional[str] = None,
    date_to: Optional[str] = None,
    limit: int = 10,
    page_size: Optional[int] = None,
    page_token: Optional[str] = None,
    disclosure_level: Optional[str] = None,
):
    try:
        eff_limit = page_size if page_size is not None else limit
        pack = controller.search_financial(
            principal=Principal.AI_AGENT,
            query=query,
            symbol=symbol,
            category=category,
            min_confidence=min_confidence,
            verification_state=verification_state,
            date_from=date_from,
            date_to=date_to,
            page_size=eff_limit,
            limit=eff_limit,
            page_token=page_token,
            disclosure_level=disclosure_level,
        )
        return {
            "status": "success",
            "context_pack": pack,
            "results": pack.get("results", []),
            "next_page_token": pack.get("next_page_token"),
            "total_matched": pack.get("total_matched", len(pack.get("results", []))),
            "metadata": pack.get("metadata", {}),
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Financial search error: {str(e)}")

@app.post("/memory/financial/search")
def post_financial_search(req: FinancialSearchRequest):
    try:
        eff_limit = req.page_size if req.page_size is not None else req.limit
        pack = controller.search_financial(
            principal=Principal.AI_AGENT,
            query=req.query,
            symbol=req.symbol,
            symbols=req.symbols,
            asset_symbol=req.asset_symbol,
            category=req.category,
            asset_classes=req.asset_classes,
            min_confidence=req.min_confidence,
            confidence_min=req.confidence_min,
            verification_state=req.verification_state,
            verification_states=req.verification_states,
            date_from=req.date_from,
            date_to=req.date_to,
            types=req.types,
            lifecycles=req.lifecycles,
            page_size=eff_limit,
            limit=eff_limit,
            page_token=req.page_token,
            disclosure_level=req.disclosure_level,
        )
        return {
            "status": "success",
            "context_pack": pack,
            "results": pack.get("results", []),
            "next_page_token": pack.get("next_page_token"),
            "total_matched": pack.get("total_matched", len(pack.get("results", []))),
            "metadata": pack.get("metadata", {}),
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Financial search error: {str(e)}")

class DispatchRequest(BaseModel):
    role: str = "coder"
    node: str = "auto"
    prompt: str
    system_prompt: str = ""

@app.post("/agent/dispatch")
def dispatch_task(req: DispatchRequest):
    """Deleaga sarcini catre GPU-urile externe (Colab/Kaggle) sau local prin MultiAgentDispatcher."""
    from cognitive_core.orchestrator import MultiAgentDispatcher
    dispatcher = MultiAgentDispatcher()
    
    # Suprascriere nod daca este specificat
    if req.node != "auto":
        nodes = dispatcher.config.get("nodes", {})
        if req.node in nodes:
            for k in nodes:
                nodes[k]["enabled"] = (k == req.node)

    system = req.system_prompt or f"You are an expert {req.role} specialized in high-performance quantitative systems engineering."
    active_url, model_name = dispatcher._get_active_node_and_model(req.role)
    
    try:
        response = dispatcher.dispatch(
            agent_role=req.role,
            system_prompt=system,
            user_input=req.prompt
        )
        return {
            "status": "success",
            "node_url": active_url,
            "model": model_name,
            "response": response
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Eroare la dispatch distribuit: {str(e)}")

@app.get("/compute/status")
def get_compute_status():
    """Returneaza statusul si configuratia curenta a nodurilor GPU (Local, Colab, Kaggle)."""
    from cognitive_core.orchestrator import MultiAgentDispatcher
    dispatcher = MultiAgentDispatcher()
    return {
        "status": "success",
        "nodes": dispatcher.config.get("nodes", {}),
        "default_models": dispatcher.config.get("default_models", {})
    }