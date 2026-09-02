import axios from 'axios';

const API_BASE_URL = (import.meta?.env?.VITE_API_BASE_URL ?? 'http://20.235.241.206:5000/api').replace(/\/$/, '');

const api = axios.create({
    baseURL: API_BASE_URL,
    headers: {
        'Content-Type': 'application/json',
    },
    withCredentials: true,
});
api.interceptors.request.use(
    (config) => {
        const token = localStorage.getItem('authToken');
        if (token) {
            config.headers.Authorization = `Bearer ${token}`;
        }
        return config;
    },
    (error) => Promise.reject(error)
);

api.interceptors.response.use(
    (response) => response,
    (error) => {
        const isAuthRoute = error.config?.url?.includes('/auth/');
        if (error.response?.status === 401 && !isAuthRoute) {
            localStorage.removeItem('authToken');
            window.dispatchEvent(new Event('auth:unauthorized'));
        }
        return Promise.reject(error);
    }
);

export const authApi = {
    login: async (credentials) => {
        // Since we have separate logins for client vs admin, we have two login methods:
        // authApi.login (for internal users/super admin) and clientApi.clientLogin (for clients)
        const response = await api.post('/auth/login', credentials);
        return response.data;
    },
    register: async (userData) => {
        const response = await api.post('/auth/register', userData);
        return response.data;
    },
    getProfile: async (options) => {
        const response = await api.get('/auth/profile', { signal: options?.signal });
        return response.data;
    },
    logout: async () => {
        const response = await api.get('/auth/logout');
        return response.data;
    },
    onboardSuperAdmin: async (adminData) => {
        const response = await api.post('/auth/onboard-super-admin', adminData);
        return response.data;
    }
};

export const analyticsApi = {
    getDashboard: async (params) => {
        const response = await api.get('/analytics/dashboard', { params });
        const payload = response.data || {};
        payload.data = payload.data || {};
        payload.data.stats = payload.data.stats ?? {
            totalHits: 0, avgLatency: 0, errorRate: 0, errorHits: 0, successHits: 0, uniqueServices: 0, uniqueEndpoints: 0,
        };
        payload.data.topEndpoints = payload.data.topEndpoints ?? [];
        payload.data.recentActivity = payload.data.recentActitivy ?? payload.data.recentActivity ?? [];
        return payload;
    },
    getStats: async (params) => {
        const response = await api.get('/analytics/stats', { params });
        return response.data;
    },
    getTopEndpoints: async (params) => {
        const response = await api.get('/analytics/top-endpoints', { params });
        return response.data;
    },
    getTimeSeries: async (params) => {
        const response = await api.get('/analytics/time-series', { params });
        return response.data;
    },
};

export const clientApi = {
    // Client specific self-routes
    clientLogin: async (credentials) => {
        const response = await api.post('/clients/login', credentials);
        return response.data;
    },
    clientRegister: async (clientData) => {
        const response = await api.post('/clients/register', clientData);
        return response.data;
    },
    getMyUsers: async () => {
        const response = await api.get('/clients/me/users');
        return response.data;
    },
    getMyApiKeys: async () => {
        const response = await api.get('/clients/me/api/keys');
        return response.data;
    },

    // Super Admin & Specific Client Org routes
    createClient: async (clientData) => {
        // Technically backend doesn't have POST /admin/clients anymore.
        // It has POST /clients/register for self-registration. Let's use that for creating clients.
        const response = await api.post('/clients/register', clientData);
        return response.data;
    },
    getClients: async (params) => {
        const response = await api.get('/admin/clients', { params });
        return response.data;
    },
    getClientById: async (clientId) => {
        const response = await api.get(`/admin/clients/${clientId}`);
        return response.data;
    },
    createClientUser: async (clientId, userData) => {
        const response = await api.post(`/clients/${clientId}/users`, userData);
        return response.data;
    },
    getClientUsers: async (clientId) => {
        const response = await api.get(`/clients/${clientId}/users`);
        return response.data;
    },
    createApiKey: async (clientId, keyData) => {
        const response = await api.post(`/clients/${clientId}/api/keys`, keyData);
        return response.data;
    },
    getClientApiKeys: async (clientId) => {
        const response = await api.get(`/clients/${clientId}/api/keys`);
        return response.data;
    },
};

export default api;
