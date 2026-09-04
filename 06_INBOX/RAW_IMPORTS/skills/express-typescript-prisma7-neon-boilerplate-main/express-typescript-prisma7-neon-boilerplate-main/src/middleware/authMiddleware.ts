import type { Request, Response, NextFunction } from 'express';
import jwt from 'jsonwebtoken';

export interface AuthRequest extends Request {
  user?: {
    userId: string;
    email: string;
  };
}

interface JwtPayload {
  userId: string;
  email: string;
}

export const authMiddleware = (
  req: AuthRequest,
  res: Response,
  next: NextFunction
): void => {
  try {
    const authHeader = req.headers.authorization;

    if (!authHeader || !authHeader.startsWith('Bearer ')) {
      res.status(401).json({ message: 'Unauthorized: No token provided.' });
      return;
    }

    const token = authHeader.split(' ')[1];

    if (!token) {
      res.status(401).json({ message: 'Unauthorized: Token is empty.' });
      return;
    }

    const jwtSecret = process.env.JWT_SECRET;
    if (!jwtSecret) {
      throw new Error('JWT_SECRET environment variable is not configured.');
    }

    const decoded = jwt.verify(token, jwtSecret) as JwtPayload;

    req.user = {
      userId: decoded.userId,
      email: decoded.email,
    };

    next();
  } catch (error: unknown) {
    if (error instanceof jwt.TokenExpiredError) {
      res.status(401).json({ message: 'Unauthorized: Token expired.' });
      return;
    }

    if (error instanceof jwt.JsonWebTokenError) {
      res.status(401).json({ message: 'Unauthorized: Invalid token.' });
      return;
    }

    res.status(500).json({ message: 'Internal server error during authentication.' });
  }
};
