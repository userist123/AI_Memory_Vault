from sqlalchemy import Column, Integer, String, DateTime, ForeignKey
from sqlalchemy.orm import declarative_base, relationship
from datetime import datetime, timedelta, UTC
import uuid

Base = declarative_base()

class User(Base):
    """
    Stores the core identity of the user and their GitHub credentials.
    If a user logs in from their phone and their laptop, they share this ONE record.
    """
    __tablename__ = "users"

    id = Column(Integer, primary_key=True, index=True, default=lambda: str(uuid.uuid4()))

    github_id = Column(Integer, unique=True, index=True, nullable=False)
    username = Column(String, unique=True, nullable=False)
    avatar_url = Column(String, nullable=True)

    github_access_token = Column(String, nullable=False)
    github_refresh_token = Column(String, nullable=True)

    token_expires_at = Column(DateTime(timezone=True), nullable=True)

    sessions = relationship("Session", back_populates="user", cascade="all, delete-orphan")

    threads = relationship("Thread", back_populates="user", cascade="all, delete-orphan")

    def __repr__(self):
        return f"<User(id={self.id}, username='{self.username}')>"

class Session(Base):
    """
    Stores the login instance for a specific browser/device.
    The 'session_id' is the ONLY thing you put in the user's browser cookie.
    """
    __tablename__ = "sessions"
    
    session_id = Column(String, primary_key=True, default=lambda: str(uuid.uuid4()))

    user_id = Column(Integer, ForeignKey("users.id"), nullable=False)

    expires_at = Column(DateTime(timezone=True), nullable=False, default=lambda: datetime.now(UTC) + timedelta(days=14))
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(UTC))

    user = relationship("User", back_populates="sessions")

    def __repr__(self):
        return f"<Session(session_id='{self.session_id}', user_id={self.user_id})>"

class Thread(Base):
    """
    Represents a conversation thread for a user.
    Each thread can have multiple messages.
    """
    __tablename__ = "threads"
    id = Column(String, primary_key=True, index=True)
    user_id = Column(Integer, ForeignKey("users.id"), nullable=False)
    title = Column(String, nullable=True)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(UTC))

    user = relationship("User", back_populates="threads")

    def __repr__(self):
        return f"<Thread(id={self.id}, user_id={self.user_id}, title='{self.title}')>"