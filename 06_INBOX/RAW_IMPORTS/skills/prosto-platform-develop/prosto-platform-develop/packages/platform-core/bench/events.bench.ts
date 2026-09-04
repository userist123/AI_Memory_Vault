import { bench, describe } from 'vitest';
import { performance } from 'node:perf_hooks';
import { DEFAULT_REGRESSION_BUDGET } from './regression-budget.config.js';

/**
 * Simple in-memory event bus implementation for benchmarking.
 */
type EventHandlerType = (payload: unknown) => void;

class SimpleEventBus {
  private readonly _handlers = new Map<string, EventHandlerType[]>();

  subscribe(event: string, handler: EventHandlerType): void {
    const handlers = this._handlers.get(event) ?? [];
    handlers.push(handler);
    this._handlers.set(event, handlers);
  }

  emit(event: string, payload: unknown): void {
    const handlers = this._handlers.get(event);

    if (handlers) {
      for (const handler of handlers) {
        handler(payload);
      }
    }
  }
}

/**
 * Simulate event dispatch with multiple handlers.
 */
function simulateEventDispatch(): number {
  const bus = new SimpleEventBus();

  // Register multiple handlers
  const results: unknown[] = [];

  for (let i = 0; i < 5; i++) {
    bus.subscribe('test:event', (payload) => {
      results.push(payload);
    });
  }

  const start = performance.now();

  // Emit multiple events
  for (let i = 0; i < 10; i++) {
    bus.emit('test:event', { index: i, timestamp: Date.now() });
  }

  const end = performance.now();

  return end - start;
}

/**
 * Benchmark: Event dispatch P95.
 * Measures the 95th percentile of event dispatch times.
 */
describe('event dispatch performance', () => {
  bench(
    'event dispatch with handlers',
    () => {
      simulateEventDispatch();
    },
    {
      iterations: DEFAULT_REGRESSION_BUDGET.measuredIterations,
      warmupIterations: DEFAULT_REGRESSION_BUDGET.warmupIterations,
      time: DEFAULT_REGRESSION_BUDGET.time,
    },
  );
});
