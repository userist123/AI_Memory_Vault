import ResponseFormatter from "../utils/responseFormatter.js";

const authorize = (allowedRoles = []) => (req, res, next) => {
    try{
        if(!req.user || !req.user.role){
            return res.status(403).json(ResponseFormatter.error("Access Denied: No Role Assigned", 403));
        }
        //skip
        if(allowedRoles.length === 0){
            return next();
        };

        if(!allowedRoles.includes(req.user.role)){
            return res.status(403).json(ResponseFormatter.error("Access Denied: Insufficient Permissions", 403));
        }

        next();
    }catch(error){
        return res.status(403).json(ResponseFormatter.error("Access Denied", 403));
    }
}

export default authorize;