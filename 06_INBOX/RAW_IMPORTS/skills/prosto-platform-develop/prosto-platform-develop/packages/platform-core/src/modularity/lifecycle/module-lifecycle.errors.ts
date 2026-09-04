export class ShutdownTimeoutError extends Error {
  readonly moduleId: string;

  constructor(moduleId: string, timeoutMs: number) {
    super(`Shutdown timeout for module "${moduleId}" after ${timeoutMs} ms.`);

    this.name = 'ShutdownTimeoutError';
    this.moduleId = moduleId;
  }
}
