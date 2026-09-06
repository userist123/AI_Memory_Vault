/**
 * AES-256-GCM encryption using Web Crypto API.
 * Works on both Cloudflare Workers and Node.js 22+.
 *
 * Format: base64(iv || authTag || ciphertext)
 * IV: 12 bytes (96-bit, recommended for GCM)
 * Tag: 16 bytes (128-bit)
 */

async function getKey(): Promise<CryptoKey> {
  const rawKey = process.env.ENCRYPTION_KEY;
  if (!rawKey) {
    if (process.env.NODE_ENV === 'production') {
      throw new Error('ENCRYPTION_KEY is required in production');
    }
    console.warn('[Crypto] ENCRYPTION_KEY not set, using dev fallback');
    const devKey = 'dev-encryption-key-please-set-ENCRYPTION_KEY-32bytes';
    const keyBytes = new TextEncoder().encode(devKey).slice(0, 32);
    return crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
  }

  // Key must be 32 bytes for AES-256
  // Accept either base64 (recommended) or plain 32-char string
  let keyBytes: Uint8Array;
  try {
    // Try base64 first
    const binary = atob(rawKey);
    keyBytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      keyBytes[i] = binary.charCodeAt(i);
    }
    if (keyBytes.length !== 32) {
      throw new Error('Not 32 bytes');
    }
  } catch {
    // Fallback to raw string
    keyBytes = new TextEncoder().encode(rawKey);
    if (keyBytes.length < 32) {
      throw new Error('ENCRYPTION_KEY must be 32 bytes (use openssl rand -base64 32)');
    }
    keyBytes = keyBytes.slice(0, 32);
  }

  return crypto.subtle.importKey('raw', keyBytes, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

function base64ToArrayBuffer(base64: string): ArrayBuffer {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

/**
 * Encrypt a string. Returns base64 of (iv || ciphertext+tag).
 */
export async function encryptString(plaintext: string): Promise<string> {
  if (!plaintext) return '';
  const key = await getKey();
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv },
    key,
    new TextEncoder().encode(plaintext)
  );

  // Combine iv + ciphertext (tag is already appended by subtle.encrypt)
  const combined = new Uint8Array(iv.length + ciphertext.byteLength);
  combined.set(iv, 0);
  combined.set(new Uint8Array(ciphertext), iv.length);
  return arrayBufferToBase64(combined.buffer);
}

/**
 * Decrypt a base64 string.
 */
export async function decryptString(encrypted: string): Promise<string> {
  if (!encrypted) return '';
  try {
    const key = await getKey();
    const combined = new Uint8Array(base64ToArrayBuffer(encrypted));
    const iv = combined.slice(0, 12);
    const ciphertext = combined.slice(12);

    const plaintext = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv },
      key,
      ciphertext
    );
    return new TextDecoder().decode(plaintext);
  } catch (err) {
    console.error('[Crypto] Decryption failed:', err);
    throw new Error('Decryption failed - key mismatch or corrupted data');
  }
}

/**
 * Mask a string for display (e.g. API key preview).
 * "abcd1234efgh5678" -> "abcd...5678"
 */
export function maskSecret(secret: string): string {
  if (!secret || secret.length < 8) return '••••••••';
  return `${secret.slice(0, 4)}...${secret.slice(-4)}`;
}
