import { LRUCache } from "lru-cache";

const apiKeyCache = new LRUCache({
    max: 10000,
    ttl: 1000 * 60 * 10
})

export default apiKeyCache;