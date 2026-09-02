# Dev Mind Backend

A FastAPI-based backend service that powers the Dev Mind AI agent platform. It handles user authentication via GitHub OAuth, manages user sessions, and provides API endpoints for interacting with the AI agent.

## Overview

Dev Mind Backend serves as the authentication layer and API gateway for the Dev Mind platform. It integrates with GitHub for user authentication and communicates with an agent gateway service to process user messages and manage conversation threads.

## Features

### 🔐 GitHub Authentication
- Secure OAuth 2.0 authentication with GitHub
- Automatic token refresh for extended sessions
- Multi-device session management
- Cryptographically secure session tokens

### 💬 Agent Integration
- Chat API for sending messages to the AI agent
- Thread-based conversation management
- Chat history retrieval
- Conversation interruption capability

### 🗄️ Database Management
- PostgreSQL database with SQLAlchemy ORM
- Alembic database migrations
- User, Session, and Thread data models

### 🔒 Security
- Session-based authentication with secure cookies
- CORS middleware for controlled access
- Token expiration handling

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | FastAPI |
| Database | PostgreSQL |
| ORM | SQLAlchemy (async) |
| Migrations | Alembic |
| Authentication | GitHub OAuth 2.0 |
| Deployment | Docker, Render |

## Project Structure

```
dev-mind-backend/
├── app/
│   ├── main.py           # FastAPI application entry point
│   ├── database.py       # SQLAlchemy models (User, Session, Thread)
│   └── routers/
│       ├── auth.py       # Authentication endpoints
│       └── agent.py      # Agent API endpoints
├── alembic/
│   └── versions/         # Database migration files
├── .github/
│   └── workflows/       # GitHub Actions workflows
├── Dockerfile            # Docker container configuration
├── docker-compose.yml   # Docker Compose setup
├── pyproject.toml       # Project dependencies
└── requirements.txt     # Python dependencies
```

## API Endpoints

### Authentication (`/auth`)
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/auth/github/login` | GET | Initiate GitHub OAuth login |
| `/auth/github/callback` | GET | Handle OAuth callback |
| `/auth/github/logout` | GET | Logout and clear session |

### Agent (`/agent`)
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/agent/chat` | POST | Send a message to the agent |
| `/agent/history` | GET | Get all conversation threads |
| `/agent/chatHistory` | GET | Get messages for a specific thread |
| `/agent/deleteHistory` | DELETE | Delete a conversation thread |

### Health Check
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Server health status |

## Environment Variables

| Variable | Description |
|----------|-------------|
| `DATABASE_URL` | PostgreSQL database connection string |
| `ENCRYPTION_KEY` | Secret key for session encryption |
| `GITHUB_APP_CLIENT_ID` | GitHub OAuth app client ID |
| `GITHUB_APP_CLIENT_SECRET` | GitHub OAuth app client secret |
| `GITHUB_APP_NAME` | GitHub app name |
| `AGENT_GATEWAY_URL` | URL of the agent gateway service |
| `FRONTEND_URL` | Frontend application URL |
| `PORT` | Server port (default: 8000) |

## Getting Started

### Prerequisites
- Python 3.12+
- PostgreSQL database
- GitHub OAuth application

## Database Models

### User
Represents a user authenticated via GitHub OAuth.

- `id`: Primary key
- `github_id`: GitHub user ID (unique)
- `username`: GitHub username (unique)
- `avatar_url`: Profile picture URL
- `github_access_token`: OAuth access token
- `github_refresh_token`: OAuth refresh token
- `token_expires_at`: Token expiration timestamp

### Session
Represents a login session for a specific device/browser.

- `session_id`: UUID-based session identifier (primary key)
- `user_id`: Foreign key to User
- `expires_at`: Session expiration timestamp
- `created_at`: Session creation timestamp

### Thread
Represents a conversation thread.

- `id`: Thread identifier (primary key)
- `user_id`: Foreign key to User
- `title`: Thread title
- `created_at`: Creation timestamp

## Connected Projects

This backend is designed to work with two other projects in the Dev Mind platform:

- **[Frontend Application](https://github.com/Thunderer9506/Dev-Mind-Frontend)** — The user interface for interacting with the Dev Mind AI agent
- **[Agent Gateway](https://github.com/Thunderer9506/Dev-Mind)** — The service that processes AI agent requests and manages conversations
