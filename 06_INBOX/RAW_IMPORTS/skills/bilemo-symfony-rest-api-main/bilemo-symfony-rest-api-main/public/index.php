<?php

use App\Kernel;
use Symfony\Component\HttpKernel\HttpCache\HttpCache;
use Symfony\Component\HttpKernel\HttpCache\Store;

require_once dirname(__DIR__).'/vendor/autoload_runtime.php';

return static function (array $context) {
    $kernel = new Kernel((string) $context['APP_ENV'], (bool) $context['APP_DEBUG']);

    // Enable Symfony's built-in Gateway Cache (Reverse Proxy) when running in production environment.
    // This intercepts cached HTTP payloads before booting heavy framework structures or database layers.
    if ('prod' === $context['APP_ENV']) {
        $kernel = new HttpCache(
            $kernel,
            new Store(dirname(__DIR__).'/var/cache/http_cache')
        );
    }

    return $kernel;
};
