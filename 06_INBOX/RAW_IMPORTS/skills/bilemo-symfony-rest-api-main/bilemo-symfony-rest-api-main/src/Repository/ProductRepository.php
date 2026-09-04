<?php

// src/Repository/ProductRepository.php

namespace App\Repository;

use App\Entity\Product;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<Product>
 */
class ProductRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, Product::class);
    }

    /**
     * Fetches a paginated slice of available products matching controller naming expectations.
     * Uses Doctrine Result Cache to drastically reduce recurring SQL workload.
     *
     * @return array<int, Product>
     */
    public function findPaginatedProducts(int $page, int $limit): array
    {
        /** @var array<int, Product> $result */
        $result = $this->createQueryBuilder('p')
            ->setFirstResult(($page - 1) * $limit) // Offset boundary calculation
            ->setMaxResults($limit) // Strict range limits
            ->getQuery()
            ->enableResultCache(3600) // Caches SQL query results for 1 hour
            ->getResult();

        return $result;
    }

    /**
     * Computes total catalog record capacity for pagination metadata blocks.
     * Uses Doctrine Result Cache to optimize recurring aggregate count statements.
     *
     * @return int
     */
    public function countAllProducts(): int
    {
        return (int) $this->createQueryBuilder('p')
            ->select('COUNT(p.id)')
            ->getQuery()
            ->enableResultCache(3600) // Caches total count framework for 1 hour
            ->getSingleScalarResult();
    }
}
