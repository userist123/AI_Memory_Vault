import type {
  EventHandlerType,
  EventTokenType,
  IEventBus,
  IEventEnvelope,
  IEventMetadata,
} from '@prosto/platform-sdk';

export class InMemoryEventBus implements IEventBus {
  private readonly _handlersByToken = new Map<
    EventTokenType<unknown>,
    Set<EventHandlerType<unknown>>
  >();

  async publish<TPayload>(
    token: EventTokenType<TPayload>,
    payload: TPayload,
    metadata?: Partial<IEventMetadata>,
  ): Promise<void> {
    const handlers = this._handlersByToken.get(token);

    if (!handlers || handlers.size === 0) {
      return;
    }

    const envelope: IEventEnvelope<TPayload> = {
      payload,
      metadata: {
        timestamp: metadata?.timestamp ?? new Date().toISOString(),
        correlationId: metadata?.correlationId,
        producerModuleId: metadata?.producerModuleId,
        schemaVersion: metadata?.schemaVersion,
      },
    };

    for (const handler of handlers) {
      await handler(envelope);
    }
  }

  subscribe<TPayload>(
    token: EventTokenType<TPayload>,
    handler: EventHandlerType<TPayload>,
  ): void {
    const handlers = this._handlersByToken.get(token) ?? new Set();
    handlers.add(handler as EventHandlerType<unknown>);
    this._handlersByToken.set(token, handlers);
  }

  unsubscribe<TPayload>(
    token: EventTokenType<TPayload>,
    handler: EventHandlerType<TPayload>,
  ): void {
    const handlers = this._handlersByToken.get(token);

    if (!handlers) {
      return;
    }

    handlers.delete(handler as EventHandlerType<unknown>);

    if (!handlers.size) {
      this._handlersByToken.delete(token);
    }
  }

  dispose(): void {
    this._handlersByToken.clear();
  }
}
