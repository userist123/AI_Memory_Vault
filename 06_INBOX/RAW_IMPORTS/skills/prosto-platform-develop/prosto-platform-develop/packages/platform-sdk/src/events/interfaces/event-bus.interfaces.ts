declare const EVENT_TOKEN_BRAND: unique symbol;

/**
 * @alpha
 * Typed symbol identity for event bus channels.
 */
export type EventTokenType<TPayload> = symbol & {
  readonly [EVENT_TOKEN_BRAND]: TPayload;
};

/**
 * @alpha
 * Event envelope metadata.
 */
export interface IEventMetadata {
  readonly timestamp: string;
  readonly correlationId?: string;
  readonly producerModuleId?: string;
  readonly schemaVersion?: string;
}

/**
 * @alpha
 * Event envelope.
 */
export interface IEventEnvelope<TPayload> {
  readonly payload: TPayload;
  readonly metadata: IEventMetadata;
}

/**
 * @alpha
 * Event handler callback signature.
 */
export type EventHandlerType<TPayload> = (
  envelope: IEventEnvelope<TPayload>,
) => void | Promise<void>;

/**
 * @alpha
 * Typed event bus contract shared by modules and runtime.
 */
export interface IEventBus {
  publish<TPayload>(
    token: EventTokenType<TPayload>,
    payload: TPayload,
    metadata?: Partial<IEventMetadata>,
  ): void | Promise<void>;
  subscribe<TPayload>(
    token: EventTokenType<TPayload>,
    handler: EventHandlerType<TPayload>,
  ): void;
  unsubscribe<TPayload>(
    token: EventTokenType<TPayload>,
    handler: EventHandlerType<TPayload>,
  ): void;
}
