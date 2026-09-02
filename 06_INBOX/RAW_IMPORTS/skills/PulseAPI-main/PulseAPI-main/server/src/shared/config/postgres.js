import pg from "pg";
import config from "./index.js";
import logger from "./logger.js";

const { Pool } = pg;

class PostgresConnection {
    constructor() {
        this.pool = null;
    }

    getPool() {
        if (!this.pool) {
            this.pool = new Pool({
                host: config.postgres.host,
                port: config.postgres.port,
                database: config.postgres.database,
                user: config.postgres.user,
                password: config.postgres.password,
                max: 20,
                idleTimeoutMillis: 30000,
                connectionTimeoutMillis: 2000,
            })
            this.pool.on("error", (err) => {
                logger.error("Unexpected error on idle client:", err);
            })
            logger.info(`PostgreSQL pool created for ${config.postgres.host}:${config.postgres.port}/${config.postgres.database}`);
        }
        return this.pool;
    }

    async testConnection() {
        try {
            const pool = this.getPool();
            const client = await pool.connect();
            const result = await client.query("SELECT NOW()");
            client.release();

            logger.info("PostgreSQL connection test successful:", result.rows[0].now);
        } catch (err) {
            logger.error("Error testing PostgreSQL connection:", err);
            throw err;
        }
    }

    async query(text, params) {
        const pool = this.getPool();
        const start = Date.now();
        try {
            const result = await pool.query(text, params);
            const duration = Date.now() - start;
            const queryString = typeof text === 'object' && text !== null ? text.text : text;
            const queryParams = typeof text === 'object' && text !== null ? text.values : params;
            logger.info(`Query executed in ${duration} ms: ${queryString}`, { params: queryParams });
            return result;
        } catch (error) {
            const queryString = typeof text === 'object' && text !== null ? text.text : text;
            logger.error("Error executing query:", { text: queryString, error: error.message });
            throw error;
        }
    }

    async close() {
        if (this.pool) {
            await this.pool.end();
            this.pool = null;
            logger.info("PostgreSQL pool closed");
        }
    }
}

export default new PostgresConnection();