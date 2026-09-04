import os
from dotenv import load_dotenv
import requests
from datetime import datetime, timedelta, UTC

from fastapi import APIRouter, HTTPException, status, Request, Query
from fastapi.responses import RedirectResponse

from sqlalchemy import select, delete
from sqlalchemy.orm import sessionmaker
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession

from app.database import User, Session as DBSession

load_dotenv()

# Database engine creation
def create_dbengine():
    if not os.getenv("DATABASE_URL"):
        raise ValueError("DATABASE_URL is not set in the environment variables.")

    DATABASE_URL = os.getenv("DATABASE_URL")
    
    engine = create_async_engine(DATABASE_URL, echo=True)
    return engine

# Database setup
engine = create_dbengine()
async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

auth_router = APIRouter()

def get_frontend_url() -> str:
    return os.getenv("FRONTEND_URL", "http://localhost:8501")

@auth_router.get("/github/login")
async def github_login(req: Request):
    CLIENT_ID = os.getenv("GITHUB_APP_CLIENT_ID")
    CLIENT_SECRET = os.getenv("GITHUB_APP_CLIENT_SECRET")
    authorize_url = f"https://github.com/login/oauth/authorize?client_id={CLIENT_ID}"

    session_id = req.session.get("session_id")
    user_id = req.session.get("user_id")

    # Any missing cookie/session data falls back to OAuth authorize.
    if not session_id or not user_id:
        req.session.clear()
        return RedirectResponse(url=authorize_url)

    try:
        async with async_session() as db_session:
            stmt = select(DBSession).where(DBSession.session_id == session_id)
            result = await db_session.execute(stmt)
            db_session_obj = result.scalar_one_or_none()

            # Invalid or expired session: force re-auth.
            if not db_session_obj or db_session_obj.expires_at <= datetime.now(UTC):
                req.session.clear()
                return RedirectResponse(url=authorize_url)

            stmt = select(User).where(User.id == db_session_obj.user_id)
            result = await db_session.execute(stmt)
            db_user = result.scalar_one_or_none()

            # Missing user for a session should always force re-auth.
            if not db_user:
                req.session.clear()
                return RedirectResponse(url=authorize_url)

            token_expired = not db_user.token_expires_at or db_user.token_expires_at <= datetime.now(UTC)

            if token_expired:
                # No refresh token available means we cannot recover this session.
                if not db_user.github_refresh_token or not CLIENT_ID or not CLIENT_SECRET:
                    req.session.clear()
                    return RedirectResponse(url=authorize_url)

                token_url = "https://github.com/login/oauth/access_token"
                headers = {"Accept": "application/json"}
                data = {
                    "client_id": CLIENT_ID,
                    "client_secret": CLIENT_SECRET,
                    "grant_type": "refresh_token",
                    "refresh_token": db_user.github_refresh_token,
                }

                refresh_response = requests.post(token_url, headers=headers, data=data)
                token_data = refresh_response.json()
                new_access_token = token_data.get("access_token")
                new_refresh_token = token_data.get("refresh_token")

                # If refresh fails, fallback should still be OAuth authorize.
                if not new_access_token:
                    req.session.clear()
                    return RedirectResponse(url=authorize_url)

                db_user.github_access_token = new_access_token
                if new_refresh_token:
                    db_user.github_refresh_token = new_refresh_token

                expires_in = token_data.get("expires_in")
                try:
                    expires_in = int(expires_in) if expires_in is not None else 8 * 60 * 60
                except (TypeError, ValueError):
                    expires_in = 8 * 60 * 60

                db_user.token_expires_at = datetime.now(UTC) + timedelta(seconds=expires_in)
                await db_session.commit()
                await db_session.refresh(db_user)
                req.session["user_id"] = db_user.id
                req.session["session_id"] = db_session_obj.session_id

            return RedirectResponse(url=f"{get_frontend_url()}?user_id={db_user.id}&session_id={db_session_obj.session_id}")
    except Exception:
        req.session.clear()
        return RedirectResponse(url=authorize_url)


@auth_router.get("/github/callback")
async def github_callback(req: Request, code: str = Query(None), error: str = Query(None), error_description: str = Query(None)):
    # Handle GitHub OAuth callback
    if error:
        raise HTTPException(
            status_code=400, 
            detail=f"GitHub authentication failed: {error_description}"
        )
    if not code:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Missing code parameter")

    # Getting access token from GitHub
    CLIENT_ID = os.getenv("GITHUB_APP_CLIENT_ID")
    CLIENT_SECRET = os.getenv("GITHUB_APP_CLIENT_SECRET")

    token_url = "https://github.com/login/oauth/access_token"
    headers = {"Accept": "application/json"}
    data = {
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "code": code
    }
    token_response = requests.post(token_url, headers=headers, data=data)
    token_data = token_response.json()
    access_token = token_data.get("access_token")
    refresh_token = token_data.get("refresh_token")

    if not access_token:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Failed to obtain access token")

    # Calculate token expiration (GitHub tokens typically expire in 8 hours)
    token_expires_at = datetime.now(UTC) + timedelta(hours=8)

    app_name = os.getenv("GITHUB_APP_NAME")
    if not app_name:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Error: GITHUB_APP_NAME is not set in your .env file.",
        )

    # Check whether the authenticated GitHub user has installed the app yet.
    installations_url = "https://api.github.com/user/installations"
    installations_headers = {
        "Authorization": f"Bearer {access_token}",
        "Accept": "application/vnd.github.v3+json",
    }
    installations_response = requests.get(installations_url, headers=installations_headers).json()

    if not installations_response.get("installations"):
        install_url = f"https://github.com/apps/{app_name}/installations/new"
        return RedirectResponse(url=install_url)

    # Get user data from GitHub
    user_url = "https://api.github.com/user"
    headers = {"Authorization": f"token {access_token}"}
    user_response = requests.get(user_url, headers=headers)
    user_data = user_response.json()
    
    if not user_data:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Failed to fetch user data")

    # Store user data in database
    async with async_session() as db_session:
        # Prune expired sessions so stale rows do not accumulate over time.
        await db_session.execute(
            delete(DBSession).where(DBSession.expires_at <= datetime.now(UTC))
        )

        # Check if user already exists
        stmt = select(User).where(User.github_id == user_data.get("id"))
        result = await db_session.execute(stmt)
        db_user = result.scalar_one_or_none()

        if db_user is None:
            # Create new user
            db_user = User(
                github_id=user_data.get("id"),
                username=user_data.get("login"),
                avatar_url=user_data.get("avatar_url"),
                github_access_token=access_token,
                github_refresh_token=refresh_token,
                token_expires_at=token_expires_at
            )
            db_session.add(db_user)
            await db_session.commit()
            await db_session.refresh(db_user)
        else:
            # Update existing user's tokens
            db_user.github_access_token = access_token
            db_user.github_refresh_token = refresh_token
            db_user.token_expires_at = token_expires_at
            await db_session.commit()

        # Create a new session for this login
        db_session_obj = DBSession(user_id=db_user.id)
        db_session.add(db_session_obj)
        await db_session.commit()
        await db_session.refresh(db_session_obj)

        # Store session ID in user's browser cookie
        req.session["session_id"] = db_session_obj.session_id
        req.session["user_id"] = db_user.id

    return RedirectResponse(url=f"{get_frontend_url()}?user_id={db_user.id}&session_id={db_session_obj.session_id}")


@auth_router.get("/github/logout")
async def github_logout(req: Request):
    # Get session ID from cookie
    session_id = req.session.get("session_id")

    if session_id:
        # Remove session from database
        async with async_session() as db_session:
            stmt = select(DBSession).where(DBSession.session_id == session_id)
            result = await db_session.execute(stmt)
            db_session_obj = result.scalar_one_or_none()

            if db_session_obj:
                await db_session.delete(db_session_obj)
                await db_session.commit()

    # Clear session cookie
    req.session.clear()
    return RedirectResponse(url=get_frontend_url())