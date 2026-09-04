import { Navigate, Outlet } from 'react-router-dom';

/**
 * RoleGuard protects routes that require specific roles.
 * @param {Object} props
 * @param {Array<string>} props.allowedRoles - Array of roles allowed to access the route.
 * @param {Object} props.user - The currently logged-in user object.
 * @param {string} props.redirectPath - Where to redirect if unauthorized (default: '/')
 */
export function RoleGuard({ allowedRoles, user, redirectPath = '/' }) {
    if (!user) {
        return <Navigate to="/login" replace />;
    }

    if (!allowedRoles.includes(user.role)) {
        return <Navigate to={redirectPath} replace />;
    }

    return <Outlet />;
}
