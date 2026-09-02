import {
  createEventToken,
  createServiceToken,
  type EventTokenType,
  type IEventBus,
  type IPlatformModuleLogger,
  type IServiceRegistry,
  type ServiceTokenType,
} from '@/index.js';

type AssertType<TValue extends true> = TValue;
type IsEqualType<TLeft, TRight> =
  (<TValue>() => TValue extends TLeft ? 1 : 2) extends <
    TValue,
  >() => TValue extends TRight ? 1 : 2
    ? true
    : false;

interface IHealthService {
  ping: () => string;
}

interface IHealthEventPayload {
  status: 'ok' | 'failed';
}

declare const logger: IPlatformModuleLogger;
declare const eventBus: IEventBus;
declare const serviceRegistry: IServiceRegistry;

const healthEventToken =
  createEventToken<IHealthEventPayload>('health.updated');
const healthServiceToken = createServiceToken<IHealthService>('health.service');

type _EventTokenTypeAssertionType = AssertType<
  IsEqualType<typeof healthEventToken, EventTokenType<IHealthEventPayload>>
>;

type _ServiceTokenTypeAssertionType = AssertType<
  IsEqualType<typeof healthServiceToken, ServiceTokenType<IHealthService>>
>;

eventBus.subscribe(healthEventToken, ({ payload }) => {
  if (payload.status === 'ok') {
    logger.info('Healthy');
  } else {
    logger.warn('Unhealthy');
  }
});

serviceRegistry.register(healthServiceToken, { ping: () => 'ok' });

const healthService = serviceRegistry.resolve(healthServiceToken);

if (!healthService) {
  throw new Error('Health service not found.');
}

healthService.ping();

serviceRegistry.register(healthServiceToken, {
  // @ts-expect-error Intentional type-level guard: invalid service shape for token.
  ping: (code: number) => String(code),
});
