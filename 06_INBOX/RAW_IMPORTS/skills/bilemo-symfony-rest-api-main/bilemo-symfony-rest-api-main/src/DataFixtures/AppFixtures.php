<?php

// src/DataFixtures/AppFixtures.php

namespace App\DataFixtures;

use App\Entity\Client;
use App\Entity\Product;
use App\Entity\User;
use Doctrine\Bundle\FixturesBundle\Fixture;
use Doctrine\Persistence\ObjectManager;
use Faker\Factory;

class AppFixtures extends Fixture
{
    /**
     * @var string[]
     */
    private array $phoneBrands = ['Apple', 'Samsung', 'Xiaomi', 'Google', 'OnePlus'];

    public function load(ObjectManager $manager): void
    {
        // Initialize Faker with French locale for realistic local data
        $faker = Factory::create('fr_FR');

        // Array to store created clients references for dependency injections
        $clients = [];

        // Pre-hashing password using bcrypt (will be wired with Security/JWT later)
        $hashedPassword = password_hash('password123', PASSWORD_BCRYPT);

        // ===================================================
        // 1. CLIENT (B2B) AND LINKED USER FIXTURES (EXECUTED FIRST)
        // ===================================================
        for ($c = 1; $c <= 5; $c++) {
            $client = new Client();
            $companyName = (string) $faker->company;

            // Generate a clean slug-like username (e.g., "sfr-telecom")
            // Clean up unwanted characters like commas or dots from company names
            $cleanName = (string) preg_replace('/[^A-Za-z0-9\- ]/', '', $companyName);
            $username = strtolower(str_replace(' ', '-', $cleanName));

            $client->setUsername($username)
                ->setRoles(['ROLE_USER'])
                ->setPassword($hashedPassword)
                ->setCompanyName($companyName);

            $manager->persist($client);
            $clients[] = $client; // Save reference for users and products linking

            // Generate between 10 and 20 end-users for each B2B Client
            $numberOfUsers = $faker->numberBetween(10, 20);
            for ($u = 1; $u <= $numberOfUsers; $u++) {
                $user = new User();
                $user->setFirstName($faker->firstName)
                    ->setLastName($faker->lastName)
                    ->setEmail($faker->unique()->safeEmail) // Ensure email uniqueness
                    ->setCreatedAt(new \DateTimeImmutable())
                    ->setClient($client); // Establish the ManyToOne relationship

                $manager->persist($user);
            }
        }

        // ===================================================
        // 2. PRODUCT FIXTURES (SMARTPHONES BOUND TO CLIENTS)
        // ===================================================
        for ($i = 1; $i <= 20; $i++) {
            $product = new Product();
            $brand = $faker->randomElement($this->phoneBrands);

            // Fetch a random client reference from our freshly created client list
            /** @var Client $randomClient */
            $randomClient = $faker->randomElement($clients);

            /** @var string $modelName */
            $modelName = $faker->words(2, true);

            $product->setBrand((string) $brand)
                ->setModel($modelName)
                ->setDescription((string) $faker->paragraph(3))
                ->setPrice((string) $faker->randomFloat(2, 299, 1299)) // Price between 299€ and 1299€
                ->setStock($faker->numberBetween(5, 150))
                ->setColor((string) $faker->safeColorName)
                ->setStorage((string) $faker->randomElement(['128 Go', '256 Go', '512 Go']))
                ->setCreatedAt(new \DateTimeImmutable())
                ->setClient($randomClient); // Establish the mandatory ManyToOne relationship

            $manager->persist($product);
        }

        // Save everything into Wamp MySQL database
        $manager->flush();
    }
}
