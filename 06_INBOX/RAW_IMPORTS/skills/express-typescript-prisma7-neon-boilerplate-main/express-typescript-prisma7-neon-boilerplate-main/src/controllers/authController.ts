import type { Request, Response } from 'express';
import type { AuthRequest } from '../middleware/authMiddleware.js';
import { PrismaClient } from '@prisma/client';
import { PrismaPg } from '@prisma/adapter-pg';
import pg from 'pg';
import bcrypt from 'bcrypt';
import jwt from 'jsonwebtoken';

// 🔌 Dynamic Prisma Client Initialization helper
let prisma: PrismaClient;

const getPrisma = () => {
  if (!prisma) {
    const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL });
    const adapter = new PrismaPg(pool);
    prisma = new PrismaClient({ adapter });
  }
  return prisma;
};

// 📝 SECURE USER REGISTRATION
export const register = async (req: Request, res: Response): Promise<void> => {
  try {
    const db = getPrisma();
    const { email, password, name } = req.body;

    if (!email || !password) {
      res.status(400).json({ error: "Email and password are required" });
      return;
    }

    // Check if user already exists
    const existingUser = await db.user.findUnique({ where: { email } });
    if (existingUser) {
      res.status(409).json({ error: "User with this email already exists" });
      return;
    }

    // 🔒 Hash the password before saving (10 salt rounds)
    const hashedPassword = await bcrypt.hash(password, 10);

    // Save user with the hashed password
    const newUser = await db.user.create({
      data: {
        email,
        password: hashedPassword, // 👈 Storing secure hash
        name,
      },
    });

    res.status(201).json({
      message: "User registered securely successfully!",
      user: { id: newUser.id, email: newUser.email, name: newUser.name },
    });
  } catch (error: any) {
    console.error(error);
    res.status(500).json({ error: 'Internal server error.' });
  }
};

// 🔑 SECURE USER LOGIN WITH JWT ISSUANCE
export const login = async (req: Request, res: Response): Promise<void> => {
  try {
    if (!process.env.JWT_SECRET) {
      throw new Error('JWT_SECRET environment variable is not configured.');
    }

    const db = getPrisma();
    const { email, password } = req.body;

    if (!email || !password) {
      res.status(400).json({ error: "Email and password are required" });
      return;
    }

    // Find user in Neon database
    const user = await db.user.findUnique({ where: { email } });
    if (!user) {
      res.status(401).json({ error: "Invalid email or password" });
      return;
    }

    // 🔒 Compare the incoming plain text password against the stored database hash
    const isPasswordValid = await bcrypt.compare(password, user.password);
    if (!isPasswordValid) {
      res.status(401).json({ error: "Invalid email or password" });
      return;
    }

    // 🎟️ Create a secure JSON Web Token valid for 24 hours
    const token = jwt.sign(
      { userId: user.id, email: user.email },
      process.env.JWT_SECRET!,
      { expiresIn: '24h' }
    );

    res.status(200).json({
      message: "Login successful!",
      token, // 👈 Frontend stores this to stay authenticated!
      user: { id: user.id, email: user.email, name: user.name },
    });
  } catch (error: any) {
    console.error(error);
    res.status(500).json({ error: 'Internal server error.' });
  }
};

export const getProfile = (req: AuthRequest, res: Response): void => {
  if (!req.user) {
    res.status(401).json({ message: 'Unauthorized.' });
    return;
  }

  res.status(200).json({
    user: {
      id: req.user.userId,
      email: req.user.email,
    },
  });
};