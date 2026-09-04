import ResponseFormatter from "../../../shared/utils/responseFormatter.js";
import config, { getCookieOptions } from "../../../shared/config/index.js";

/**
 * ClientController class to handle client related requests
 */
export class ClientController {
    /**
     * Constructor for ClientController
     * @param {Object} clientService 
     * @param {Object} authService 
     */
    constructor(clientService, authService) {
        // Validate dependencies
        if (!clientService) {
            throw new Error('ClientService is required');
        };

        if (!authService) {
            throw new Error('authService is required');
        };

        // Assign dependencies to instance variables
        this.clientService = clientService;
        this.authService = authService;
    };


    /**
     * Create a new client, only accessible by super admins
     * @param {Request} req - Express request object
     * @param {Response} res - Express response object
     * @param {Function} next - Express next function for error handling
     * @returns {Promise<Response>} - JSON response with created client data or error message
     */
    async createClient(req, res, next) {
        try {
            const { client, token } = await this.clientService.createClient(req.body);

            res.cookie("authToken", token, getCookieOptions());

            return res.status(201).json(ResponseFormatter.success({ client, token }, "Client created successfully", 201))
        } catch (error) {
            next(error)
        }
    }

    async loginClient(req, res, next) {
        try {
            const { client, token } = await this.clientService.loginClient(req.body);

            res.cookie("authToken", token, getCookieOptions());

            return res.status(200).json(ResponseFormatter.success({ client, token }, "Client logged in successfully", 200))
        } catch (error) {
            next(error)
        }
    }

    /**
     * Create a new client user for a specific client
     * @param {Request} req - Express request object
     * @param {Response} res - Express response object
     * @param {Function} next - Express next function for error handling
     * @returns {Promise<Response>} - JSON response with created client user data or error message
     */
    async createClientUser(req, res, next) {
        try {
            const { clientId } = req.params;
            const user = await this.clientService.createClientUser(clientId, req.body, req.user)
            return res.status(201).json(ResponseFormatter.success(user, "Client user created successfully", 201))
        } catch (error) {
            next(error)
        }
    }


    /**
     * Create a new API key for a specific client
     * @param {Request} req - Express request object
     * @param {Response} res - Express response object
     * @param {Function} next - Express next function for error handling
     * @returns {Promise<Response>} - JSON response with created API key data or error message
     */
    async createApiKey(req, res, next) {
        try {
            const { clientId } = req.params;
            const apiKey = await this.clientService.createApiKey(clientId, req.body, req.user)
            return res.status(201).json(ResponseFormatter.success(apiKey, "API key created successfully", 201))
        } catch (error) {
            next(error)
        }
    };

    /**
     * Get all API keys for a specific client
     * @param {Request} req - Express request object
     * @param {Response} res - Express response object
     * @param {Function} next - Express next function for error handling
     * @returns {Promise<Response>} - JSON response with fetched API keys data or error message
 */
    async getClientApiKeys(req, res, next) {
        try {
            const { clientId } = req.params;
            const apiKey = await this.clientService.getClientApiKeys(clientId, req.user)
            return res.status(200).json(ResponseFormatter.success(apiKey, "API key fetched successfully", 200))
        } catch (error) {
            next(error)
        }
    }

    async getAllClients(req, res, next) {
        try {
            const clients = await this.clientService.getAllClients(req.user)
            return res.status(200).json(ResponseFormatter.success(clients, "Clients fetched successfully", 200))
        } catch (error) {
            next(error)
        }
    }

    async getClientById(req, res, next) {
        try {
            const { clientId } = req.params
            const client = await this.clientService.getClientById(clientId, req.user)
            return res.status(200).json(ResponseFormatter.success(client, "Client fetched successfully", 200))
        } catch (error) {
            next(error)
        }
    }

    /**
     * Get all users (admin/viewer) for a specific client org
     */
    async getClientUsers(req, res, next) {
        try {
            const { clientId } = req.params;
            const users = await this.clientService.getClientUsers(clientId, req.user);
            return res.status(200).json(ResponseFormatter.success(users, "Client users fetched successfully", 200));
        } catch (error) {
            next(error);
        }
    }

    /**
     * Get logged-in client's own users via /me route
     * clientId is taken from the cookie token — no URL param needed
     */
    async getMyUsers(req, res, next) {
        try {
            const clientId = req.user.clientId;
            const users = await this.clientService.getClientUsers(clientId, req.user);
            return res.status(200).json(ResponseFormatter.success(users, "Users fetched successfully", 200));
        } catch (error) {
            next(error);
        }
    }

    /**
     * Get logged-in client's own API keys via /me route
     * clientId is taken from the cookie token — no URL param needed
     */
    async getMyApiKeys(req, res, next) {
        try {
            const clientId = req.user.clientId;
            const apiKeys = await this.clientService.getClientApiKeys(clientId, req.user);
            return res.status(200).json(ResponseFormatter.success(apiKeys, "API keys fetched successfully", 200));
        } catch (error) {
            next(error);
        }
    }

}