import os
from dotenv import load_dotenv
load_dotenv()

import requests
import uuid
from datetime import datetime, UTC
from pydantic import BaseModel, Field

from sqlalchemy import select, desc, delete
from sqlalchemy.orm import sessionmaker, selectinload
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession
from app.database import Thread, User, Session as DBSession

from fastapi import APIRouter, Depends, HTTPException, status, Request, Query

def create_dbengine():
    if not os.getenv("DATABASE_URL"):
        raise ValueError("DATABASE_URL is not set in the environment variables.")

    DATABASE_URL = os.getenv("DATABASE_URL")
    
    engine = create_async_engine(DATABASE_URL, echo=True)
    return engine

async def authenticate(req: Request) -> User | HTTPException:
    user_id = req.cookies.get("user_id")
    session_id = req.cookies.get("session_id")
    
    if not user_id or not session_id:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Not authenticated")

    async with async_session() as db_session:
        stmt = select(DBSession).where(DBSession.session_id == session_id)
        result = await db_session.execute(stmt)
        db_session_obj = result.scalar_one_or_none()

        if not db_session_obj or db_session_obj.expires_at <= datetime.now(UTC):
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Not authenticated")

        stmt = select(User).where(User.id == db_session_obj.user_id)
        result = await db_session.execute(stmt)
        db_user = result.scalar_one_or_none()

        if not db_user:
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Not authenticated")
    
    return db_user

def gatewayurl()->str:
    gateway_url = os.getenv("AGENT_GATEWAY_URL", "http://localhost:8001").rstrip("/")
    if not gateway_url:
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail="AGENT_GATEWAY_URL is not set in the environment variables.")

    return gateway_url
# Database setup
engine = create_dbengine()
async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

agent_router = APIRouter()

class ChatRequest(BaseModel):
    thread_id: str = Field(..., description="The ID of the chat thread")
    message: str = Field(..., min_length=1, max_length=1200, description="The message to send to the agent")
    interrupt: bool = Field(False, description="Whether to interrupt the current conversation")

@agent_router.post("/chat")
async def invoke_agent(req: Request, payload: ChatRequest, user: User = Depends(authenticate)):
    if payload.interrupt:
        target_url = f"{gatewayurl()}/api/v1/chat/resume"
    
        payload = {
            "thread_id": payload.thread_id,
            "input": payload.message
        }
    else:
        target_url = f"{gatewayurl()}/api/v1/chat"

        thread_id = payload.thread_id
        if not thread_id:
            thread_id = uuid.uuid4().hex
            async with async_session() as db_session:
                new_thread = Thread(id = thread_id, user_id=user.id, title=f"{payload.message[:50]}...")
                db_session.add(new_thread)
                await db_session.commit()
                thread_id = new_thread.id

        payload = {
            "thread_id": thread_id,
            "message": payload.message,
            "username": user.username,
            "accessToken": user.github_access_token
        }
        
    
    try:
        response = requests.post(target_url, json=payload, timeout=60)
        response.raise_for_status()
        return response.json()
    except requests.RequestException as e:
        print(f"Error invoking agent: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=f"Gateway Error: An error occurred while communicating with the agent gateway")


@agent_router.get("/history")
async def get_history(user: User = Depends(authenticate)):
    """List of Threads for the user"""
    try:
        async with async_session() as db_session:
            stmt = select(Thread).where(Thread.user_id == user.id).options(selectinload(Thread.user)).order_by(desc(Thread.created_at))
            result = await db_session.execute(stmt)
            threads = result.scalars().all()

            return {
                "status": "success",
                "data": [
                    {"id": t.id, "title": t.title, "created_at": t.created_at}
                    for t in threads
                ]
            }
    except Exception as e:
        print(f"Error fetching history: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail="An error occurred while fetching chat history")


@agent_router.get("/chatHistory")
async def get_chat_history(thread_id: str = Query(...), user: User = Depends(authenticate)):
    """List of previous messages for a specific thread"""
    target_url = f"{gatewayurl()}/api/v1/chat/history"
    params = {"thread_id": thread_id}

    try:
        response = requests.get(target_url, params=params, timeout=60)
        response.raise_for_status()
        return response.json()
    except requests.RequestException as e:
        print(f"Error fetching chat history: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=f"Gateway Error: An error occurred while communicating with the agent gateway")


@agent_router.delete("/deleteHistory")
async def delete_chat(thread_id: str = Query(...), user: User = Depends(authenticate)):
    """List of previous messages for a specific thread"""
    target_url = f"{gatewayurl()}/api/v1/chat/delete"
    params = {"thread_id": thread_id}

    try:
        async with async_session() as db_session:
            stmt = delete(Thread).where(Thread.id == thread_id, Thread.user_id == user.id)
            result = await db_session.execute(stmt)

            if result.rowcount == 0:
                raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Thread not found")

            await db_session.commit()

        response = requests.delete(target_url, params=params, timeout=60)
        response.raise_for_status()
        return response.json()
    except requests.RequestException as e:
        print(f"Error deleting chat history: {str(e)}")
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=f"Gateway Error: An error occurred while communicating with the agent gateway")

