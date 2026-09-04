export class ServiceAlreadyRegisteredError extends Error {
  constructor(token: string) {
    super(`Service with token ${token} already registered.`);
    this.name = 'ServiceAlreadyRegisteredError';
  }
}

export class ServiceNotFoundError extends Error {
  constructor(token: string) {
    super(`Service with token ${token} not found.`);
    this.name = 'ServiceNotFoundError';
  }
}
