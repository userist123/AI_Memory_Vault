<?php

// src/Repository/UserRepository.php

namespace App\Repository;

use App\Entity\User;
use App\Entity\Client;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<User>
 */
class UserRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, User::class);
    }

    /**
     * Fetches a paginated list of users belonging to a specific B2B client.
     * Uses Doctrine Result Cache to optimize database performance.
     *
     * @return array<int, User>
     */
    public function findPaginatedUsersByClient(Client $client, int $page, int $limit): array
    {
        /** @var array<int, User> $result */
        $result = $this->createQueryBuilder('u')
            ->andWhere('u.client = :client')
            ->setParameter('client', $client)
            ->setFirstResult(($page - 1) * $limit)
            ->setMaxResults($limit)
            ->getQuery()
            ->enableResultCache(3600) // Caches SQL results for 1 hour (3600 seconds)
            ->getResult();

        return $result;
    }

    /**
     * Counts the total number of users belonging to a specific B2B client.
     * Uses Doctrine Result Cache to optimize aggregate counts.
     *
     * @return int
     */
    public function countByClient(Client $client): int
    {
        return (int) $this->createQueryBuilder('u')
            ->select('count(u.id)')
            ->andWhere('u.client = :client')
            ->setParameter('client', $client)
            ->getQuery()
            ->enableResultCache(3600) // Caches total count for 1 hour
            ->getSingleScalarResult();
    }
}
