<?php

// src/Serializer/ProductNormalizer.php

namespace App\Serializer;

use App\Entity\Product;
use Symfony\Component\DependencyInjection\Attribute\Autowire;
use Symfony\Component\HttpFoundation\RequestStack;
use Symfony\Component\Routing\Generator\UrlGeneratorInterface;
use Symfony\Component\Serializer\Normalizer\NormalizerInterface;

class ProductNormalizer implements NormalizerInterface
{
    /**
     * @param NormalizerInterface $normalizer Injected native ObjectNormalizer to read entity primitive properties.
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
        //force type safety and prevent normalization of unsupported objects
        if (!$object instanceof Product) {
            throw new \InvalidArgumentException('The object must be an instance of Product.');
        }

        $context[self::class . '_ALREADY_CALLED'] = true;

        // 1. Delegate core properties normalization to the native platform ObjectNormalizer
        $normalizedData = $this->normalizer->normalize($object, $format, $context);

        $request = $this->requestStack->getCurrentRequest();
        if (!$request || !is_array($normalizedData)) {
            /** @var array<string, mixed>|string|int|float|bool|\ArrayObject<string, mixed>|null $normalizedData */
            return $normalizedData;
        }

        // 2. Define standard item links (Self always maps back to its precise singular resource URI)
        $normalizedData['_links'] = [
            'self' => [
                'href' => $this->router->generate('app_product_detail', ['id' => (int) $object->getId()], UrlGeneratorInterface::ABSOLUTE_URL)
            ],
            'products' => [
                'href' => $this->router->generate('app_product_list', [], UrlGeneratorInterface::ABSOLUTE_URL)
            ]
        ];

        // 3. Inject B2B Client contextual pointer if the relation is mapped and defined on the entity object
        if ($object->getClient()) {
            $normalizedData['_links']['client'] = [
                'href' => $this->router->generate('app_client_profile', [], UrlGeneratorInterface::ABSOLUTE_URL)
            ];
        }

        /** @var array<string, mixed> $normalizedData */
        return $normalizedData;
    }

    public function supportsNormalization(mixed $data, ?string $format = null, array $context = []): bool
    {
        return $data instanceof Product && !isset($context[self::class . '_ALREADY_CALLED']);
    }

    public function getSupportedTypes(?string $format): array
    {
        return [
            Product::class => true,
        ];
    }
}
