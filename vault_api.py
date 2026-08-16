import uuid
from datetime import datetime, timezone
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any

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
        results = controller.search(query=query, filter_dict={"lifecycle": "ACTIVE"}, principal=Principal.AI_AGENT)
        return {"status": "success", "results": results}
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))