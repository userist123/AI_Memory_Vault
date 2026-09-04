<?php

// src/Controller/ClientController.php

namespace App\Controller;

use App\Entity\Client;
use Nelmio\ApiDocBundle\Attribute\Model;
use OpenApi\Attributes as OA;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Response;
use Symfony\Component\Routing\Attribute\Route;
use Symfony\Component\Serializer\SerializerInterface;

#[OA\Tag(name: 'Client Context Profiles')]
final class ClientController extends AbstractController
{
    /**
     * Retrieve details and automated HATEOAS navigation controls for the authenticated Client session.
     */
    #[Route('/profile', name: 'app_client_profile', methods: ['GET'])]
    #[OA\Get(
        path: '/profile',
        summary: 'Fetch connected organization workspace profile matrix data',
        description: 'Extracts identification keys metadata variables belonging to the session token bearer workspace.'
    )]
    #[OA\Response(
        response: 200,
        description: 'Success - Identity profiles matrix returned smoothly mapping structural components properties',
        content: new OA\JsonContent(ref: new Model(type: Client::class, groups: ['client:read']))
    )]
    #[OA\Response(
        response: 401,
        description: 'Unauthorized - Active authentication pipeline token is null, invalid or context matching parameters failed'
    )]
    public function getProfile(SerializerInterface $serializer): JsonResponse
    {
        // Extract token bearer identification profile directly out of active system context pools
        /** @var Client|null $currentClient */
        $currentClient = $this->getUser();

        // Enforce rigid fallback validation safeguards to catch anomaly session drop situations
        if (!$currentClient) {
            return new JsonResponse(
                ['message' => 'JWT Token validation failed or user context missing.'],
                Response::HTTP_UNAUTHORIZED
            );
        }

        // Convert the authenticated B2B Client model profile into JSON output bounded by context rules
        $jsonClient = $serializer->serialize($currentClient, 'json', ['groups' => ['client:read']]);

        // Package structural strings back directly inside standard pre-formatted system json channels
        return new JsonResponse($jsonClient, Response::HTTP_OK, [], true);
    }
}
