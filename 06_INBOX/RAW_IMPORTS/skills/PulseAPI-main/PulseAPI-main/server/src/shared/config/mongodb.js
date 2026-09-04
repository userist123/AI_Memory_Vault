import config from "./index.js";
import logger from "./logger.js";
import mongoose from "mongoose";

/**
 * MongoDB connection class using mongoose
 */
class MongoConnection {
    constructor() {
        this.connection = null;
    }

    /**
     * Connect to MongoDB using mongoose
     * @returns {Promise<mongoose.Connection>} 
     */
    async connect() {
        try{
            if(this.connection) {
                logger.info("MongoDB already connected");
                return this.connection;
            }
            await mongoose.connect(config.mongo.uri, {
                dbName: config.mongo.dbName
            })
            logger.info(`MongoDB connected to ${config.mongo.uri}`);
            this.connection = mongoose.connection;
            this.connection.on("error", (error) => {
                logger.error("MongoDB connection error:", error);
            });
            this.connection.on("disconnected", () => {
                logger.info("MongoDB disconnected");
            })
            return this.connection;
        } catch(error) {
            logger.error("Error connecting to MongoDB:", error);
            throw error;
        }
    }

    /**
     * Disconnect from mongodb
     * @returns {Promise<void>}
     */
    
    async disconnect() {
        try {
            if(this.connection) {
                await mongoose.disconnect();
                this.connection = null;
                logger.info("MongoDB disconnected");
            }
        }catch(error) {
            logger.error("Error disconnecting from MongoDB:", error);
            throw error;
        }
    }

    /**
     * get the active MongoDB connection
     * @returns {mongoose.Connection}
     */

    getConnection() {
        return this.connection;
    
    }
}

export default new MongoConnection();