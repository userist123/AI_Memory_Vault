# ⚡ PulseAPI — Developer-First API Monitoring & Observability Platform

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Node.js](https://img.shields.io/badge/Node.js-v18%2B-green.svg)](https://nodejs.org/)
[![React](https://img.shields.io/badge/React-v18-blue.svg)](https://react.dev/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED.svg)](https://www.docker.com/)
[![TailwindCSS](https://img.shields.io/badge/TailwindCSS-v4-38B2AC.svg)](https://tailwindcss.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-v15-4169E1.svg)](https://www.postgresql.org/)
[![MongoDB](https://img.shields.io/badge/MongoDB-v6-47A248.svg)](https://www.mongodb.com/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-v3-FF6600.svg)](https://www.rabbitmq.com/)

**PulseAPI** is a high-performance, developer-first API monitoring and observability platform. Drop in our lightweight middleware SDK into your backend in seconds to stream real-time endpoint latency, uptime status, error distributions, and historical performance metrics straight to a sleek dashboard.

---

## 🌟 Features

- **⚡ Zero-Overhead Telemetry**: Ingestion requests are buffered asynchronously via RabbitMQ message queues to guarantee zero latency penalty on production APIs.
- **📊 Real-Time Observability**: Interactive ApexCharts tracking throughput (Hits), Average & P95 Latencies, and HTTP Status Code distributions (2xx, 4xx, 5xx).
- **⏱️ Flexible Time-Range Filtering**: Inspect telemetry over **1H**, **4H**, **24H**, **7D**, **30D**, or custom date-time windows.
- **👑 Multi-Tenant Architecture & Role-Based Access (RBAC)**:
  - **Super Admin**: Global cross-tenant metrics, client organization management, system-wide endpoint tracking.
  - **Client Admin**: Team member management, API key rotation, project-level environment configuration.
- **🔑 API Key & Client Security**: Redacted headers, hashed credentials, bcrypt auth, and scoped client tokens.
- **🎨 Warm Blue Design System**: High-contrast, accessibility-tested Warm Blue Dark and Light themes with Tailwind v4 and SCSS modules.

---

## 🏗️ Architecture Overview

```mermaid
flowchart TD
    subgraph Client Application
        App[Your Express / Node.js API] -->|Asynchronous Middleware| SDK[PulseAPI SDK Middleware]
    end

    subgraph Ingestion Pipeline
        SDK -->|POST /api/hit| API[PulseAPI Ingestion Server]
        API -->|Publish Hit Event| RMQ[(RabbitMQ Queue: api_hits)]
        RMQ -->|Consume Event| Worker[Background Consumer Worker]
    end

    subgraph Storage Layer
        Worker -->|Aggregation Metrics| PG[(PostgreSQL: endpoint_metrics)]
        API -->|Auth, Orgs, Keys| Mongo[(MongoDB: Metadata & Users)]
    end

    subgraph Observability Dashboard
        UI[React 18 + Vite Dashboard] -->|REST API & React Query| API
        PG -.->|Time-Series Stats| UI
        Mongo -.->|User & Org Config| UI
    end
```

---

## 🛠️ Tech Stack

### **Frontend (`/dashboard`)**
- **Framework**: React 18, Vite 5
- **Styling**: Tailwind CSS v4, SCSS Modules
- **State & Data Fetching**: TanStack React Query v5
- **Visualization**: React ApexCharts, Lucide React Icons
- **Animations & UX**: Framer Motion

### **Backend (`/server`)**
- **Core**: Node.js, Express 5
- **Databases**: PostgreSQL 15 (Metrics time-series), MongoDB 6 (User accounts, Client orgs, API keys)
- **Message Broker**: RabbitMQ 3 (Asynchronous queue buffering)
- **Authentication**: JWT, BCrypt, Role Guards (`super_admin`, `client_admin`)
- **Logging & Utilities**: Winston Logger, Zod validation

---

## 📁 Repository Structure

```
PulseAPI/
├── dashboard/                 # React 18 frontend dashboard web app
│   ├── src/
│   │   ├── api/              # Axios API client setup
│   │   ├── components/       # UI components (charts, modals, layout)
│   │   ├── contexts/         # Theme, Auth & Toast contexts
│   │   ├── hooks/            # TanStack Query custom hooks
│   │   ├── pages/            # Overview, Clients, ApiKeys, Users pages
│   │   └── styles/           # Warm Blue theme tokens & SCSS modules
│   └── tailwind.config.js
│
├── server/                    # Node.js backend server & ingestion worker
│   ├── src/
│   │   ├── services/         # Analytics, Auth, Client, Ingest services
│   │   └── shared/           # Database connectors, logger, middleware
│   ├── Docker-compose.yml     # Complete local stack orchestration
│   ├── Dockerfile             # API server image definition
│   └── Dockerfile.consumer    # RabbitMQ background worker image definition
│
├── demo/                      # Sample Express API showcasing SDK integration
│   ├── server.js
│   └── monitoring.js          # PulseAPI middleware client
│
└── k6-load-test.js            # Stress testing script for API hit ingestion
```

---

## 🚀 Quick Start & Local Setup

### Prerequisites

Ensure you have the following installed:
- [Node.js](https://nodejs.org/) (v18 or higher)
- [npm](https://www.npmjs.com/) (v9 or higher)
- [Docker & Docker Compose](https://www.docker.com/)

---

### Step 1: Clone the Repository

```bash
git clone https://github.com/Katari-8055/PulseAPI.git
cd PulseAPI
```

---

### Step 2: Spin Up Infrastructure (Docker)

Launch PostgreSQL, MongoDB, RabbitMQ, pgAdmin, and the background services with Docker Compose:

```bash
cd server
docker compose up -d --build
```

> **Services Started:**
> - **Backend API**: `http://localhost:5000`
> - **PostgreSQL**: `localhost:5432`
> - **MongoDB**: `localhost:27017`
> - **RabbitMQ Management**: `http://localhost:15672` (User: `api_user`, Pass: `secure_password`)
> - **pgAdmin**: `http://localhost:8080` (Email: `admin@example.com`, Pass: `admin`)

---

### Step 3: Run the Dashboard (Frontend)

In a new terminal tab:

```bash
cd dashboard
npm install
npm run dev
```

The React dashboard will be live at `http://localhost:5173`.

---

### Step 4: Run the Demo Client Application

To generate real-time metrics and test monitoring ingestion:

```bash
cd demo
npm install
npm run dev
```

The demo API server will run at `http://localhost:3002`. Trigger endpoints (e.g., `GET /api/v1/auth/session`) to see live metrics appear on your dashboard!

---

## 💻 SDK Integration Guide

Instrumenting your existing Node.js / Express backend takes less than 1 minute:

### 1. Include `monitoring.js` middleware in your backend project:

```javascript
const express = require('express');
const monitoring = require('./monitoring'); // SDK middleware

const app = express();

// Apply monitoring middleware before your routes
app.use(monitoring({
    serviceName: 'my-microservice',
    apiKey: process.env.MONITORING_API_KEY,
    endpoint: 'http://localhost:5000/api/hit',
}));

app.get('/api/v1/users', (req, res) => {
    res.json({ message: 'User list' });
});

app.listen(3000, () => console.log('Server running on port 3000'));
```

---

## ⚙️ Environment Variables Reference

### Backend (`/server/.env`)

| Variable | Default Value | Description |
|---|---|---|
| `PORT` | `5000` | HTTP server port |
| `NODE_ENV` | `development` | Runtime environment (`development`/`production`) |
| `MONGO_URI` | `mongodb://localhost:27017/api_monitoring` | MongoDB connection string |
| `PG_HOST` | `localhost` | PostgreSQL database host |
| `PG_PORT` | `5432` | PostgreSQL port |
| `PG_DATABASE` | `api_monitoring` | PostgreSQL database name |
| `PG_USER` | `postgres` | PostgreSQL username |
| `PG_PASSWORD` | `rahul` | PostgreSQL password |
| `RABBITMQ_URL` | `amqp://api_user:secure_password@localhost:5672/api_monitoring` | RabbitMQ connection string |
| `JWT_SECRET` | `your_secret_key` | Secret key for signing JWT tokens |

### Dashboard (`/dashboard/.env`)

| Variable | Default Value | Description |
|---|---|---|
| `VITE_API_BASE_URL` | `/api` | Base API target URL for frontend requests |

---

## 🧪 Load Testing with k6

Stress test ingestion throughput using the included `k6` load test:

```bash
k6 run k6-load-test.js
```

---

## 🤝 Contributing Guidelines

We welcome contributions from the community! To get started:

1. **Fork the Repository** on GitHub.
2. **Create a Feature Branch**:
   ```bash
   git checkout -b feature/amazing-new-feature
   ```
3. **Commit Your Changes**:
   ```bash
   git commit -m "feat: Add amazing new telemetry feature"
   ```
4. **Push to Your Branch**:
   ```bash
   git push origin feature/amazing-new-feature
   ```
5. **Open a Pull Request** against `main`.

### Code Style & Standards
- Write clean, modular ES6+ JavaScript.
- Use SCSS modules or design system tokens for styling.
- Ensure components handle loading, error, and empty states gracefully.

---

## 📜 License

Distributed under the MIT License. See `LICENSE` for more information.

---

<p align="center">
  Handcrafted with ❤️ by <a href="https://github.com/Katari-8055">Rahul Katari</a>
</p>
