import { z } from 'zod';

// Password: min 8 chars, at least one letter and one digit
const passwordRegex = /^(?=.*[A-Za-z])(?=.*\d).{8,}$/;

export const SignupSchema = z.object({
  email: z.string().email('Invalid email'),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters')
    .regex(passwordRegex, 'Password must contain at least one letter and one digit'),
  name: z.string().min(1).max(100).optional(),
  language: z.enum(['ro', 'en']).default('ro'),
});

export const LoginSchema = z.object({
  email: z.string().email('Invalid email'),
  password: z.string().min(1, 'Password required'),
});

export type SignupInput = z.infer<typeof SignupSchema>;
export type LoginInput = z.infer<typeof LoginSchema>;

// Stored in MongoDB (with passwordHash)
export const UserSchema = z.object({
  _id: z.string().optional(),
  email: z.string().email(),
  passwordHash: z.string(),
  name: z.string().nullable().optional(),
  language: z.enum(['ro', 'en']).default('ro'),
  plan: z.enum(['free', 'pro', 'elite']).default('free'),
  createdAt: z.date(),
  updatedAt: z.date(),
  lastLoginAt: z.date().nullable().optional(),
  emailVerified: z.boolean().default(false),
});

export type User = z.infer<typeof UserSchema>;

// Public user data (no passwordHash, safe to send to client)
export const PublicUserSchema = UserSchema.omit({ passwordHash: true });
export type PublicUser = z.infer<typeof PublicUserSchema>;

// JWT payload
export interface JWTPayload {
  sub: string; // user ID
  email: string;
  plan: 'free' | 'pro' | 'elite';
  iat?: number;
  exp?: number;
}

export function toPublicUser(user: User): PublicUser {
  const { passwordHash: _, ...publicData } = user;
  return publicData;
}
