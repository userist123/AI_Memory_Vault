<?php

// src/Serializer/ClientNormalizer.php

namespace App\Serializer;

use App\Entity\Client;
use Symfony\Component\DependencyInjection\Attribute\Autowire;
use Symfony\Component\HttpFoundation\RequestStack;
use Symfony\Component\Routing\Generator\UrlGeneratorInterface;
use Symfony\Component\Serializer\Normalizer\NormalizerInterface;

class ClientNormalizer implements NormalizerInterface
{
    /**
     * @param NormalizerInterface $normalizer Injected native ObjectNormalizer to handle core primitive properties.
     */
    public function __construct(
        #[Autowire(service: 'serializer.normalizer.object')]
        private readonly NormalizerInterface $normalizer,
        private readonly UrlGeneratorInterface $router,
        private readonly RequestStack $requestStack
    ) {
    }

    /**
     * @param mixed $object
     * @param string|null $format
     * @param array<string, mixed> $context
     * @return array<string, mixed>|string|int|float|bool|\ArrayObject<string, mixed>|null
     */
    public function normalize(mixed $object, ?string $format = null, array $context = []): array|string|int|float|bool|\ArrayObject|null
    {
        // ENforce type safety and prevent normalization of unsupported objects
        if (!$object instanceof Client) {
            throw new \InvalidArgumentException('The object must be an instance of Client.');
        }

        // Prevent infinite recursion loops during downstream normalization cascades
        $context[self::class . '_ALREADY_CALLED'] = true;

        // 1. Delegate standard property normalization to the native ObjectNormalizer
        $normalizedData = $this->normalizer->normalize($object, $format, $context);

        // 2. Safeguard execution context against missing HTTP request footprints
        $request = $this->requestStack->getCurrentRequest();
        if (!$request || !is_array($normalizedData)) {
            /** @var array<string, mixed>|string|int|float|bool|\ArrayObject<string, mixed>|null $normalizedData */
            return $normalizedData;
        }

        // 3. Inject root discovery hypermedia controls matching Richardson Maturity Level 3
        $normalizedData['_links'] = [
            'self' => [
                'href' => $this->router->generate('app_client_profile', [], UrlGeneratorInterface::ABSOLUTE_URL)
            ],
            'users' => [
                'href' => $this->router->generate('app_user_list', [], UrlGeneratorInterface::ABSOLUTE_URL)
            ],
            'products' => [
                'href' => $this->router->generate('app_product_list', [], UrlGeneratorInterface::ABSOLUTE_URL)
            ]
        ];

        /** @var array<string, mixed> $normalizedData */
        return $normalizedData;
    }

    /**
     * Validates if the data payload is eligible for Client hypermedia enrichment.
     */
    public function supportsNormalization(mixed $data, ?string $format = null, array $context = []): bool
    {
        return $data instanceof Client && !isset($context[self::class . '_ALREADY_CALLED']);
    }

    /**
     * Declares caching optimization configurations for the Dependency Injection container.
     */
    public function getSupportedTypes(?string $format): array
    {
        return [
            Client::class => true,
        ];
    }
}
