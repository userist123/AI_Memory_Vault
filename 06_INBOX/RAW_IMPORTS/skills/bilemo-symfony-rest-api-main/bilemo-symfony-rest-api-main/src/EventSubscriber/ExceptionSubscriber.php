<?php

// src/EventSubscriber/ExceptionSubscriber.php

namespace App\EventSubscriber;

use Symfony\Component\EventDispatcher\EventSubscriberInterface;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Response;
use Symfony\Component\HttpKernel\Event\ExceptionEvent;
use Symfony\Component\HttpKernel\Exception\HttpExceptionInterface;
use Symfony\Component\HttpKernel\KernelInterface;

final class ExceptionSubscriber implements EventSubscriberInterface
{
    private string $environment;

    public function __construct(KernelInterface $kernel)
    {
        // Accessing environment to dynamically allow debugging logs if needed
        $this->environment = $kernel->getEnvironment();
    }

    public static function getSubscribedEvents(): array
    {
        // Listen to the core kernel.exception footprint with high priority
        return [
            'kernel.exception' => 'onKernelException',
        ];
    }

    public function onKernelException(ExceptionEvent $event): void
    {
        // Extract the original throwable exception object from the event lifecycle
        $exception = $event->getThrowable();

        // 1. Determine the appropriate HTTP status code fallback hierarchy
        if ($exception instanceof HttpExceptionInterface) {
            $statusCode = $exception->getStatusCode();
        } else {
            // Default generic internal server error fallback mapping
            $statusCode = Response::HTTP_INTERNAL_SERVER_ERROR;
        }

        // 2. Format a predictable, structured error payload message
        // In production, we mask raw internal exceptions messages to prevent sensitive data leaks
        if ($statusCode === Response::HTTP_INTERNAL_SERVER_ERROR && $this->environment === 'prod') {
            $message = 'An unexpected internal server error occurred.';
        } else {
            $message = $exception->getMessage();
        }

        // 3. Build the clean, compliant JSON error envelope payload contract
        $payload = [
            'status' => $statusCode,
            'message' => $message,
        ];

        // 4. Create and configure the concrete JsonResponse object
        $response = new JsonResponse($payload, $statusCode);
        $response->headers->set('Content-Type', 'application/json');

        // 5. Intercept standard HTML exception views by feeding our custom response into the event
        $event->setResponse($response);
    }
}
