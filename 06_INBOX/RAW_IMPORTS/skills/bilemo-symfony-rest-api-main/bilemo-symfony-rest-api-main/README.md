# BileMo B2B API v1.0.0

[![Symfony](https://img.shields.io/badge/Symfony-7.0%2B-000000?style=for-the-badge&logo=symfony)](https://symfony.com)
[![PHP](https://img.shields.io/badge/PHP-8.2%2B-777BB4?style=for-the-badge&logo=php)](https://php.net)
[![JWT](https://img.shields.io/badge/JWT-Authentication-000000?style=for-the-badge&logo=json-web-tokens)](https://jwt.io)
[![OpenAPI](https://img.shields.io/badge/OpenAPI-v3.0-6BA539?style=for-the-badge&logo=openapi-initiative)](https://www.openapis.org/)

**BileMo** est une API REST d'ingénierie logicielle avancée conçue exclusivement pour le marché B2B. Elle permet à des plateformes tierces (agences de photographie, boutiques en ligne, distributeurs partenaires) d'accéder à un catalogue premium de téléphones mobiles et de gérer leur propre pool d'utilisateurs finaux de manière totalement isolée et sécurisée.

Ce projet s'inscrit dans une architecture logicielle robuste respectant les standards de l'industrie : conformité REST, hypermédias HATEOAS, sécurité multi-tenant, pagination stricte, mécanismes anti-DOS et mise en cache HTTP avancée.

---

## 🚀 Fonctionnalités Clés

- **Authentification Sécurisée JWT** : Échange d'identifiants B2B contre un jeton cryptographique signé via `LexikJWTAuthenticationBundle`.
- **Isolation Multi-Tenant (Cloisonnement)** : Un client connecté ne peut **strictement jamais** accéder, modifier ou supprimer les utilisateurs d'un autre client (implémentation via les _Voters_ Symfony).
- **Catalogue de Produits Global** : Consultation publique (mais authentifiée) des spécifications techniques des mobiles.
- **Gestion d'Utilisateurs Scopée** : CRUD complet (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`) sur le registre des consommateurs rattachés.
- **Pagination Dynamique & HATEOAS** : Intégration systématique des métadonnées de pagination et des liens hypermédias d'auto-découverte (`_links`).
- **HTTP Caching & Performance** : Optimisation de la bande passante par validation de cache via empreintes de conformité **ETag** (Renvoie un statut `304 Not Modified`).
- **Protection Anti-DOS** : Plafonnement strict et automatisé des requêtes de collection (`limit` bridée à 50 maximum).
- **Documentation Interactive OpenAPI v3** : Génération automatisée du contrat d'interface exposé via un tableau de bord **Swagger UI** ordonné et nettoyé.

---

## 🛠️ Stack Technique

- **Framework** : Symfony 7.x _(API pure, sans Twig applicatif ni Symfony Flex standard template)_
- **Langage** : PHP 8.2+ _(Utilisation exclusive des attributs natifs)_
- **Base de données** : MySQL / PostgreSQL _(via Doctrine ORM)_
- **Sécurité** : LexikJWTAuthenticationBundle _(Jetons RSA asymétriques)_
- **Documentation** : NelmioApiDocBundle + Swagger UI _(Conformité OpenAPI 3.0)_

---

## 📋 Prérequis & Installation

    1. Cloner le dépôt et installer les dépendances

    ```bash
    git clone [https://github.com/Mike031289/bilemo-symfony-rest-api.git](https://github.com/Mike031289/bilemo-symfony-rest-api.git)
    cd bilemo-symfony-rest-api
    composer install
    ```

    2. Configuration de l'environnement
    Configurez vos variables d'accès à la base de données dans votre fichier .env.local :
    Extrait de code
    NB: Penser à créer et modifier cette ligne de config avec le bon nom de votre nouvelle db avant de lancer la commande de création de la db pour l'étape 4.
    DATABASE_URL="mysql://db_user:db_password@127.0.0.1:3306/bilemo_api_db?serverVersion=8.0.32&charset=utf8mb4"

    3. Génération des clés de sécurité JWT
    L'API utilise une signature asymétrique pour sécuriser ses tokens. Générez la paire de clés privée/publique :
    ```bash
        - php bin/console lexik:jwt:generate-keypair
    ```

    4. Initialisation de la base de données & Fixtures
    Créez la base de données, appliquez le schéma et chargez le jeu de données de test (Produits et structures Clients B2B) :
    ```bash

        - php bin/console doctrine:database:create
        - php bin/console doctrine:migrations:migrate --no-interaction
        - php bin/console doctrine:fixtures:load --no-interaction
    ```

    5. Lancement du serveur local
    ```bash
        symfony server:start
        L'API est maintenant accessible sur https://127.0.0.1:8000.
    ```.

## 🔒 Schéma de Sécurité & Droits d'accès :
L'ensemble des ressources métiers (/products, /users, /profile) nécessite l'envoi d'un jeton JWT valide dans les en-têtes de la requête :
    . HTTP
    . Authorization: Bearer <votre_token_jwt>
    . Matrice d'Accessibilité des Routes

## Méthode	Endpoint	Description	Niveau d'accès	Cache HTTP :
    - POST /login_check
        Authentification (Génération Token)	Anonyme	❌
    - GET /bilemob2bapi/doc
        Interface Interactive Swagger UI	Anonyme	❌
    - GET /profile
        Profil de l'organisation connectée	Client Authentifié	❌
    - GET /products
        Liste paginée du catalogue mobile	Client Authentifié	✔️ (ETag)
    - GET /products/{id}
        Détails d'un modèle de smartphone	Client Authentifié	✔️ (ETag)
    - GET /users
        Liste des utilisateurs du Client	Client Connecté	✔️ (ETag)
    - POST /users	Création et liaison d'un utilisateur	Client Connecté	❌
    - GET /users/{id}
        Consulter la fiche d'un de ses utilisateurs	Client Propriétaire	✔️ (ETag)
    - PUT / PATCH	/users/{id}
        Mutation / Hydration des attributs	Client Propriétaire	❌
    - DELETE /users/{id}
        Purge de l'utilisateur de l'index SQL	Client Propriétaire	❌

## 📖 Utilisation de la Documentation (Swagger UI)
Une attention majeure a été portée à l'ergonomie contractuelle de l'interface graphique pour le confort d'intégration de vos équipes de développement.
    1.	Ouvrez votre navigateur sur https://127.0.0.1:8000/bilemob2bapi/doc.
    2.	Authentification : Déroulez le premier tiroir nommé Authentication, cliquez sur Try it out, et soumettez les identifiants d'un client B2B (fournis via les Fixtures).
    3.	Copiez le token généré dans la réponse JSON.
    4.	Cliquez sur le bouton général Authorize en haut à droite, collez le token sous la forme Bearer <votre_token> et validez.
    5.	L'intégralité des routes verrouillées est désormais interactive et testable en direct.

## Modèles de données (Schemas OpenAPI)
Les schémas en bas de documentation ont été nettoyés de tout doublon structurel et sont explicitement nommés selon les contextes de sérialisation :
    •	Client : Données de structure de l'organisation partenaire.
    •	ProductListItem : Vue allégée optimisée pour les performances de listage.
    •	ProductDetail : Vue exhaustive incluant l'intégralité des variables techniques du produit.
    •	UserListItem : Identité condensée pour les listes d'utilisateurs.
    •	UserDetail : Profil complet du consommateur final (utilisé pour les détails, les créations et les modifications).

## Qualité du Code & Standards
Le code source applique les paradigmes de programmation les plus stricts pour garantir la maintenabilité :
    •	Conventional Commits : Historique Git sémantique et limpide (feat(), docs(), security(), config()).
    •	Semantic Status Codes : Utilisation stricte des codes HTTP appropriés (201 Created, 204 No Content, 304 Not Modified, 400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found).
    •	Architecture Multi-Tenant Logique : Découplage de la sécurité globale (Pare-feu security.yaml) de la sécurité métier algorithmique fine (Voters polymorphes).

## Architecture & Diagrams
    ```mermaid
    ```
    1. Diagramme de Class (classDiagram):
        direction LR

            class Client {
                +int id
                +string username
                +array roles
                +string password
                +string companyName
                +getUsers() Collection
            }

            class User {
                +int id
                +string firstName
                +string lastName
                +string email
                +DateTime createdAt
                +Client client
                +getClient() Client
            }

            class Product {
                +int id
                +string brand
                +string model
                +string description
                +float price
                +int stock
                +string color
                +string storage
                +DateTime createdAt
            }

            Client "1" -- "0..*" User : manages
            Client "0..*" -- "0..*" Product : consults

    2. Schéma de la Base de Données (Modèle Relationnel / EER): Ce diagramme représente les tables physiques SQL, leurs types de colonnes natifs et les contraintes de clés étrangères (`FK`).

        CLIENT {
            int id PK
            varchar username UK
            json roles
            varchar password
            varchar company_name
        }
        USER {
            int id PK
            varchar first_name
            varchar last_name
            varchar email
            datetime created_at
            int client_id FK
        }
        PRODUCT {
            int id PK
            varchar brand
            varchar model
            longtext description
            decimal price
            int stock
            varchar color
            varchar storage
            datetime created_at
            int client_id FK
        }

        CLIENT ||--o{ USER : "has many (1:N)"
        CLIENT ||--o{ PRODUCT : "references (1:N)"

    3. Diagramme de Séquence (sequenceDiagram: Flux d'une Requête API Multi-Tenant):Ce diagramme illustre le cycle de vie d'une requête HTTP sécurisée par JWT, passant par ton Voter Symfony pour valider le cloisonnement des données.

        autonumber
        actor App as Client B2B (API Consumer)
        participant FW as JWT Firewall (Lexik)
        participant CTRL as UsersController
        participant VTR as UserVoter (Security)
        participant DB as MySQL Database

            App->>FW: GET /api/users/42 (with Bearer Token)

            alt Token Invalid or Missing
                FW-->>App: 4101 Unauthorized
            else Token Valid
                FW->>CTRL: Forward Request + Client Entity
                CTRL->>VTR: denyAccessUnlessGranted('can_view', requestedUser)

                alt User does NOT belong to this Client
                    VTR-->>CTRL: Access Denied
                    CTRL-->>App: 403 Forbidden
                else Access Granted
                    VTR-->>CTRL: Access Allowed
                    CTRL->>DB: Query User details
                    DB-->>CTRL: Return User Data
                    CTRL-->>App: 200 OK (JSON Payload + HATEOAS Links)
                end
            end

    4. Diagramme de Cas d'Utilisation / Use Case (Périmètre Fonctionnel): Ce schéma montre les actions et droits d'accès des différents acteurs sur ce API REST.

        subgraph subGraph0["BileMo API System Boundary"]
            UC1("Authentification /login_check")
            UC2("Consulter la documentation /bilemob2bapi/doc")
            UC3("Consulter le catalogue de Smartphones")
            UC4@{ label: "Gérer son pool d'utilisateurs finaux CRUD" }
        end
            Anon["Acteur : Client Anonyme"] --> UC1 & UC2
            Client["Acteur : Client B2B Connecté"] --> UC3 & UC4
            UC4@{ shape: stadium }


## 📄 Licence
Ce projet est sous licence propriétaire exclusive pour l'agence BileMo. Toute reproduction ou distribution sans accord préalable est interdite.
```
