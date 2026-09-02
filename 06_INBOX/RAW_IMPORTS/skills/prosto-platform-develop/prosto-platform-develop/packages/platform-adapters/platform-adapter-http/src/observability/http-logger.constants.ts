/**
 * @alpha
 * Stable event names emitted by the HTTP transport logger.
 */
export const HTTP_LOG_EVENT = Object.freeze({
  serverStarted: 'http.server.started',
  serverStopped: 'http.server.stopped',
  requestCompleted: 'http.request.completed',
  requestSlow: 'http.request.slow',
} as const);
