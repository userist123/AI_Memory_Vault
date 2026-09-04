/**
 * BaseRepository class to define the interface for user repository implementations
 * This class provides method signatures for creating users, finding users by ID, username, or email, and retrieving all users. It serves as a base class for specific database implementations of the user repository. Each method throws a "Method not implemented" error, indicating that subclasses must provide their own implementations for these methods.
 */
export default class BaseRepository {
    /**
     * Constructor for BaseRepository
     * @param {*} model - The Mongoose model for the user
     */
    constructor(model) {
        this.model = model;
    }
    async create(data) {
        throw new Error("Method not implemented");
    }
    async findById(id) {
        throw new Error("Method not implemented");
    }
    async findByUsername(username) {
        throw new Error("Method not implemented");
    }
    async findByEmail(email) {
        throw new Error("Method not implemented");
    }
    async findAll() {
        throw new Error("Method not implemented");
    }
}