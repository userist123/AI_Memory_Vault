/**
 * @alpha
 * Per-request options for ArtifactFetcher.
 */
export interface IArtifactFetchRequestOptions {
  timeoutMs?: number;
  authToken?: string;
  authType?: 'bearer' | 'basic';
  followRedirects?: boolean;
  headers?: Record<string, string>;
}

/**
 * @alpha
 * Abstraction for HTTP client to fetch artifacts.
 */
export interface IModuleArtifactHttpClient {
  fetch(url: string, options?: IArtifactFetchRequestOptions): Promise<Buffer>;
}

/**
 * @alpha
 * Configuration options for ArtifactFetcher.
 */
export interface IArtifactFetcherOptions {
  /**
   * Default timeout for all fetch requests.
   * @default 30s
   */
  defaultTimeoutMs?: number;
  /**
   * Maximum number of retries for failed fetch requests.
   * @default 2
   */
  maxRetries?: number;
  /**
   * Delay between retry attempts.
   * @default 1s
   */
  retryDelayMs?: number;
}

/**
 * HTTPS artifact fetching with timeout, retry, and auth support.
 */
export class ArtifactFetcher implements IModuleArtifactHttpClient {
  private readonly _defaultTimeoutMs: number;
  private readonly _maxRetries: number;
  private readonly _retryDelayMs: number;

  constructor(options?: IArtifactFetcherOptions) {
    this._defaultTimeoutMs = options?.defaultTimeoutMs ?? 30_000;
    this._maxRetries = options?.maxRetries ?? 2;
    this._retryDelayMs = options?.retryDelayMs ?? 1_000;
  }

  async fetch(
    url: string,
    options?: IArtifactFetchRequestOptions,
  ): Promise<Buffer> {
    const timeout = options?.timeoutMs ?? this._defaultTimeoutMs;
    let lastError: Error | null = null;

    for (let attempt = 0; attempt <= this._maxRetries; attempt++) {
      try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), timeout);

        const headers = this._buildHeaders(options);
        const response = await fetch(url, {
          headers,
          signal: controller.signal,
          redirect: options?.followRedirects !== false ? 'follow' : 'manual',
        });

        clearTimeout(timeoutId);

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return Buffer.from(await response.arrayBuffer());
      } catch (error) {
        lastError = error instanceof Error ? error : new Error(String(error));

        if (attempt < this._maxRetries) {
          await this._delay(this._retryDelayMs * Math.pow(2, attempt));
        }
      }
    }

    throw lastError ?? new Error('Fetch failed');
  }

  private _buildHeaders(
    options?: IArtifactFetchRequestOptions,
  ): Record<string, string> {
    const headers: Record<string, string> = options?.headers ?? {};

    if (!headers['Authorization'] && options?.authToken) {
      const scheme = options.authType === 'basic' ? 'Basic' : 'Bearer';
      headers['Authorization'] = `${scheme} ${options.authToken}`;
    }

    return headers;
  }

  private _delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
