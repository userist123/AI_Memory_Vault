# ⚡ Express + TypeScript + Prisma 7 + Neon Serverless Postgres — Free Boilerplate

> Jump from `git clone` to a live, database-connected API in under 60 seconds.
> **Stop wasting days on boilerplate.** This kit gives you the cleanest,
> most modern backend foundation for free.

[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript)](https://www.typescriptlang.org/)
[![Express](https://img.shields.io/badge/Express-5-000000?logo=express)](https://expressjs.com/)
[![Prisma](https://img.shields.io/badge/Prisma-7-2D3748?logo=prisma)](https://www.prisma.io/)
[![Neon](https://img.shields.io/badge/Neon-Serverless%20Postgres-00E5A0?logo=neon)](https://neon.tech)

---

## 🚀 Want the full production engine that saves you another 20+ hours?

Stop piecing together auth, security, and testing yourself.  
**The Premium Production Kit** adds everything you need to go from localhost
to launch instantly:

👉 **[Get the Premium Kit on Gumroad for $39](https://nalrna.gumroad.com/l/mqwlo)**

*(No account setup required. Instant .zip delivery. Free updates for 12 months included.)*

---

## 📊 What's Inside — Free vs. Premium

| Feature | Free (this repo) | Premium ($39) |
|--------|------------------|---------------|
| Express 5 + TypeScript (ESM) setup | ✅ | ✅ |
| Prisma 7 schema & migration | ✅ | ✅ |
| Neon serverless Postgres connection | ✅ | ✅ |
| Environment variable configuration | ✅ | ✅ |
| Public health-check endpoint (`GET /api/health`) | ✅ | ✅ |
| Modular folder structure (routes, controllers, middleware) | ✅ | ✅ |
| User registration (bcrypt hashing) | ❌ | ✅ |
| User login (JWT access token) | ❌ | ✅ |
| Protected route middleware (`authMiddleware`) | ❌ | ✅ |
| Profile endpoint (`GET /api/auth/me`) | ❌ | ✅ |
| Input validation (Zod) | ❌ | ✅ |
| Rate limiting & security headers (helmet) | ❌ | ✅ |
| Structured error handling | ❌ | ✅ |
| Integration tests (Jest + Supertest) | ❌ | ✅ |
| Docker & Docker Compose | ❌ | ✅ |
| GitHub Actions CI pipeline | ❌ | ✅ |
| Deployment guides (Railway, Render, Fly.io) | ❌ | ✅ |
| Email support & free updates for 12 months | ❌ | ✅ |

---

## 🏁 Quick Start (Free Version)

```bash
# 1. Clone the repo
git clone https://github.com/nalrnalar-star/express-typescript-prisma7-neon-boilerplate.git

# 2. Navigate into the project
cd express-typescript-prisma7-neon-boilerplate

# 3. Install dependencies
npm install

# 4. Configure environment
cp .env.example .env
# Fill in your DATABASE_URL from Neon (free tier works perfectly)
# Generate a strong JWT_SECRET: node -e "console.log(require('crypto').randomBytes(64).toString('hex'))"

# 5. Push the Prisma schema to your database
npx prisma db push

# 6. Start the development server
npm run dev