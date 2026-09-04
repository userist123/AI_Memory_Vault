import { Router } from 'express';
import type { Request, Response } from 'express';
import { PrismaClient } from '@prisma/client';
import { PrismaPg } from '@prisma/adapter-pg';
import pg from 'pg';

let prisma: PrismaClient;

const getPrisma = (): PrismaClient => {
  if (!prisma) {
    const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL });
    const adapter = new PrismaPg(pool);
    prisma = new PrismaClient({ adapter });
  }
  return prisma;
};

const router = Router();

router.get('/health', async (_req: Request, res: Response): Promise<void> => {
  try {
    const db = getPrisma();
    await db.$queryRaw`SELECT 1`;
    res.status(200).json({
      status: 'ok',
      db: 'connected',
      timestamp: new Date().toISOString(),
    });
  } catch {
    res.status(500).json({
      status: 'error',
      db: 'disconnected',
      message: 'Database connection failed.',
    });
  }
});

export default router;
