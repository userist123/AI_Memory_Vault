import jwt from "jsonwebtoken";
import config from "../config/index.js";
import logger from "../config/logger.js";
import ResponseFormatter from "../utils/responseFormatter.js";


const authenticate = async (req, res, next) => {
    try {
        let token = null;

        if (req.cookies && req.cookies.authToken) {
            token = req.cookies.authToken;
        }

        if (!token && req.headers.authorization) {
            const authHeader = req.headers.authorization;
            if (authHeader.startsWith("Bearer ")) {
                token = authHeader.split(" ")[1];
            } else {
                token = authHeader;
            }
        }

        if (!token) {
            return res.status(401).json(ResponseFormatter.error("Authentication Token Required", 401));
        }

        const decoded = jwt.verify(token, config.jwt.secret);

        const { userId, email, username, role, clientId } = decoded;

        req.user = { userId, email, username, role, clientId };
        next();
    } catch (error) {
        logger.error("Authentication Failed", {
            error: error.message,
            path: req.path,
        });

        if (error.name === "TokenExpiredError") {
            return res.status(401).json(ResponseFormatter.error("Authentication Token Expired", 401));
        }

        res.status(401).json(ResponseFormatter.error("Invalid Authentication Token", 401));
    }
}

export default authenticate;