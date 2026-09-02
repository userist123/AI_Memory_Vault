import express from "express";
import clientDependencies from "../Dependencies/dependencies.js"
import authenticate from "../../../shared/middlewares/authenticate.js"

// Create a new router instance
const router = express.Router();

// Destructure the clientController from the dependencies
const { clientController } = clientDependencies.controller


// Onboard a new client
router.post("/clients/register", (req, res, next) => clientController.createClient(req, res, next))


//Login Client
router.post("/clients/login", (req, res, next) => clientController.loginClient(req, res, next))


// ─── /me routes — clientId from cookie token (no URL param needed) ───
// Get logged-in client's own users
router.get("/clients/me/users", authenticate, (req, res, next) => clientController.getMyUsers(req, res, next))

// Get logged-in client's own API keys
router.get("/clients/me/api/keys", authenticate, (req, res, next) => clientController.getMyApiKeys(req, res, next))


// ─── :clientId routes — for super admin or explicit access ───
// Create a user for a client
router.post("/clients/:clientId/users", authenticate, (req, res, next) => clientController.createClientUser(req, res, next))

// Get all users for a client (admin + viewer)
router.get("/clients/:clientId/users", authenticate, (req, res, next) => clientController.getClientUsers(req, res, next))

// Create API key for a client
router.post("/clients/:clientId/api/keys", authenticate, (req, res, next) => clientController.createApiKey(req, res, next))

// Get all API keys for a client
router.get("/clients/:clientId/api/keys", authenticate, (req, res, next) => clientController.getClientApiKeys(req, res, next))

// Get all clients
router.get("/admin/clients", authenticate, (req, res, next) => clientController.getAllClients(req, res, next))

// Get client by ID
router.get("/admin/clients/:clientId", authenticate, (req, res, next) => clientController.getClientById(req, res, next))

export default router;