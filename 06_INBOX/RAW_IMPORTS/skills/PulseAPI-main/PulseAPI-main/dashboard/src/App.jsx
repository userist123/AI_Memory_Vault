import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Login from './components/Login';
import Register from './components/Register';
import LandingPage from './pages/LandingPage';
import { DashboardLayout } from './components/layout';
import { ThemeProvider } from './contexts/ThemeContext';
import { ToastProvider } from './contexts/ToastContext';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import ErrorBoundary from './components/ErrorBoundary';
import { RoleGuard } from './guards/RoleGuard';

const OverviewPage = lazy(() => import('./pages/OverviewPage').then(m => ({ default: m.OverviewPage })));
const SettingsPage = lazy(() => import('./pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const ApiKeysPage = lazy(() => import('./pages/ApiKeysPage').then(m => ({ default: m.ApiKeysPage })));
const UsersPage = lazy(() => import('./pages/UsersPage').then(m => ({ default: m.UsersPage })));
const AdminClientsPage = lazy(() => import('./pages/AdminClientsPage').then(m => ({ default: m.AdminClientsPage })));
const ClientMonitoringPage = lazy(() => import('./pages/ClientMonitoringPage').then(m => ({ default: m.ClientMonitoringPage })));

const pageFallback = (
    <div className="flex items-center justify-center h-[60vh] text-muted-foreground">Loading…</div>
);

function AppRoutes() {
    const { user, isLoading, logout } = useAuth();

    if (isLoading) {
        return (
            <div className="flex items-center justify-center h-screen bg-background text-foreground">
                Checking authentication…
            </div>
        );
    }

    if (!user) {
        return (
            <Routes>
                <Route path="/" element={<LandingPage />} />
                <Route path="/login" element={<Login />} />
                <Route path="/register" element={<Register />} />
                <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
        );
    }

    return (
        <DashboardLayout onLogout={logout}>
            <Suspense fallback={pageFallback}>
                <Routes>
                    {/* Overview: all roles */}
                    <Route path="/" element={<OverviewPage />} />

                    {/* Client Admin + Super Admin: API Keys & Users */}
                    <Route element={<RoleGuard allowedRoles={['super_admin', 'client_admin']} user={user} />}>
                        <Route path="/api-keys" element={<ApiKeysPage />} />
                        <Route path="/users" element={<UsersPage />} />
                        <Route path="/settings" element={<SettingsPage />} />
                    </Route>

                    {/* Super Admin only: All Clients */}
                    <Route element={<RoleGuard allowedRoles={['super_admin']} user={user} />}>
                        <Route path="/admin/clients" element={<AdminClientsPage />} />
                        <Route path="/admin/clients/:clientId" element={<ClientMonitoringPage />} />
                    </Route>

                    {/* Catch all */}
                    <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
            </Suspense>
        </DashboardLayout>
    );
}

function App() {
    return (
        <ErrorBoundary>
            <ThemeProvider>
                <ToastProvider>
                    <BrowserRouter>
                        <AuthProvider>
                            <AppRoutes />
                        </AuthProvider>
                    </BrowserRouter>
                </ToastProvider>
            </ThemeProvider>
        </ErrorBoundary>
    );
}

export default App;
