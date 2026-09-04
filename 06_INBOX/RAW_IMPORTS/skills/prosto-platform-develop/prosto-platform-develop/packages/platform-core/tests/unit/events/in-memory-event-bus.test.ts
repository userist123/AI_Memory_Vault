import { describe, expect, it } from 'vitest';
import { createEventToken, type IEventEnvelope } from '@prosto/platform-sdk';
import { InMemoryEventBus } from '@/events/index.js';

const TOKEN_A = createEventToken<string>('event-a');

describe('InMemoryEventBus', () => {
  it('publishes to subscribed handlers', async () => {
    const bus = new InMemoryEventBus();
    const received: string[] = [];

    bus.subscribe(TOKEN_A, (envelope) => {
      received.push(envelope.payload);
    });

    await bus.publish(TOKEN_A, 'hello');

    expect(received).toEqual(['hello']);
  });

  it('does not invoke unsubscribed handlers', async () => {
    const bus = new InMemoryEventBus();
    const received: string[] = [];

    const handler = (envelope: { payload: string }): void => {
      received.push(envelope.payload);
    };

    bus.subscribe(TOKEN_A, handler);
    bus.unsubscribe(TOKEN_A, handler);

    await bus.publish(TOKEN_A, 'hello');

    expect(received).toEqual([]);
  });

  it('supports multiple handlers for same token', async () => {
    const bus = new InMemoryEventBus();
    const received: string[] = [];

    bus.subscribe(TOKEN_A, (envelope) => {
      received.push(`h1:${envelope.payload}`);
    });

    bus.subscribe(TOKEN_A, (envelope) => {
      received.push(`h2:${envelope.payload}`);
    });

    await bus.publish(TOKEN_A, 'msg');

    expect(received).toContain('h1:msg');
    expect(received).toContain('h2:msg');
  });

  it('ignores publish when no handlers exist', async () => {
    const bus = new InMemoryEventBus();

    await expect(bus.publish(TOKEN_A, 'orphan')).resolves.toBeUndefined();
  });

  it('clears all handlers', async () => {
    const bus = new InMemoryEventBus();
    const received: string[] = [];

    bus.subscribe(TOKEN_A, (envelope) => {
      received.push(envelope.payload);
    });
    bus.dispose();

    await bus.publish(TOKEN_A, 'hello');

    expect(received).toEqual([]);
  });

  it('includes metadata in envelope', async () => {
    const bus = new InMemoryEventBus();
    let envelope: IEventEnvelope<string> | undefined;

    bus.subscribe(TOKEN_A, (ev) => {
      envelope = ev;
    });

    await bus.publish(TOKEN_A, 'data', { correlationId: 'cid-1' });

    expect(envelope?.payload).toBe('data');
    expect(envelope?.metadata.correlationId).toBe('cid-1');
    expect(typeof envelope?.metadata.timestamp).toBe('string');
  });
});
