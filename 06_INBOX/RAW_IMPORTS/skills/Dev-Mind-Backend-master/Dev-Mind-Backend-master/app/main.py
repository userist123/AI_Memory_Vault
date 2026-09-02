import os
from dotenv import load_dotenv

from fastapi import FastAPI
from starlette.middleware.sessions import SessionMiddleware
from fastapi.concurrency import asynccontextmanager
from fastapi.middleware.cors import CORSMiddleware

from sqlalchemy.orm import sessionmaker
from sqlalchemy.ext.asyncio import create_async_engine, AsyncSession

from app.database import Base
from app.routers.auth import auth_router
from app.routers.agent import agent_router

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

# Lifespan event to create tables on startup
@asynccontextmanager
async def lifespan(app: FastAPI):
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    await engine.dispose()

    print("🔼Engine Startup complete. Database tables created.")
    yield
    print("🔽Engine Shutdown complete. Database connection closed.")


origins = [
    os.getenv("FRONTEND_URL","http://localhost:8501"),
    os.getenv("AGENT_GATEWAY_URL","http://localhost:7000"),
]

# FastAPI application setup
app = FastAPI(title="Devmind Backend", lifespan=lifespan)

app.include_router(auth_router, prefix="/auth", tags=["Authentication"])
app.include_router(agent_router, prefix="/agent", tags=["Agent"])

# Middlewares
app.add_middleware(SessionMiddleware, secret_key=os.getenv("ENCRYPTION_KEY"), max_age=14 * 24 * 60 * 60, https_only=True, same_site="lax")
app.add_middleware(CORSMiddleware,
    allow_origins=origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/health")
async def root():
    return {"Health Status": "Server is running and healthy!"}
