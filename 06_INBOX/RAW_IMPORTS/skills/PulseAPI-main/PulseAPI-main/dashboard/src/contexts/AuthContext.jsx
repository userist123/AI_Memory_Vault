import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { authApi } from '../api/api';
import { useQueryClient } from '@tanstack/react-query';

const AuthContext = createContext(undefined);

export function AuthProvider({ children }) {
    const [user, setUser] = useState(null);
    const [isLoading, setIsLoading] = useState(true);
    const queryClient = useQueryClient();

    const fetchProfile = useCallback(async () => {
        try {
            const response = await authApi.getProfile();
            // Assuming response data contains the user object
            setUser(response.data || response);
        } catch (error) {
            setUser(null);
        } finally {
            setIsLoading(false);
        }
    }, []);

    useEffect(() => {
        fetchProfile();
    }, [fetchProfile]);

    const login = (userData) => {
        setUser(userData);
    };

    const logout = useCallback(async () => {
        try {
            await authApi.logout();
        } catch (e) {}
        localStorage.removeItem('authToken');
        queryClient.clear();
        setUser(null);
    }, [queryClient]);

    useEffect(() => {
        const handle401 = () => {
            localStorage.removeItem('authToken');
            queryClient.clear();
            setUser(null);
        };
        window.addEventListener('auth:unauthorized', handle401);
        return () => window.removeEventListener('auth:unauthorized', handle401);
    }, [queryClient]);

    return (
        <AuthContext.Provider value={{ user, isLoading, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
}

export function useAuth() {
    const context = useContext(AuthContext);
    if (context === undefined) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
}
