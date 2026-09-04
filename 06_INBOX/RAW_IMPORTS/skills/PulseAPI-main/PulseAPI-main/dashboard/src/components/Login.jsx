import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { authApi, clientApi } from '../api/api';
import { Activity, Lock, User, Mail, Loader2, Building } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { Link } from 'react-router-dom';

function Login() {
    const [loginType, setLoginType] = useState('user'); // 'user' | 'client'
    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const { login } = useAuth();

    const userLoginMutation = useMutation({
        mutationFn: authApi.login,
        onSuccess: (data) => {
            if (data.success) {
                const token = data.data?.token || data.token;
                if (token) {
                    localStorage.setItem('authToken', token);
                }
                window.location.href = '/';
            } else {
                setError(data.message || 'Login failed');
            }
        },
        onError: (error) => {
            setError(error.response?.data?.message || 'Failed to connect to server');
        },
    });

    const clientLoginMutation = useMutation({
        mutationFn: clientApi.clientLogin,
        onSuccess: (data) => {
            if (data.success) {
                const token = data.data?.token || data.token;
                if (token) {
                    localStorage.setItem('authToken', token);
                }
                window.location.href = '/';
            } else {
                setError(data.message || 'Login failed');
            }
        },
        onError: (error) => {
            setError(error.response?.data?.message || 'Failed to connect to server');
        },
    });

    const handleSubmit = (e) => {
        e.preventDefault();
        setError('');
        if (loginType === 'user') {
            userLoginMutation.mutate({ username, password });
        } else {
            clientLoginMutation.mutate({ email, password });
        }
    };

    const isPending = userLoginMutation.isPending || clientLoginMutation.isPending;

    return (
        <div className="relative min-h-screen flex items-center justify-center overflow-hidden bg-slate-950 text-slate-50">
            {/* Animated background elements */}
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-indigo-600/20 rounded-full blur-[120px]" />
                <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-purple-600/20 rounded-full blur-[120px]" />
            </div>

            <div className="relative w-full max-w-md p-8 rounded-2xl bg-slate-900/50 border border-slate-800 backdrop-blur-xl shadow-2xl">
                <div className="text-center mb-6">
                    <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-indigo-600/10 text-indigo-400 mb-4 ring-1 ring-indigo-500/20">
                        <Activity className="w-6 h-6" />
                    </div>
                    <h1 className="text-2xl font-semibold tracking-tight">
                        API Monitor
                    </h1>
                    <p className="text-sm text-slate-400 mt-2">
                        Sign in to access your dashboard
                    </p>
                </div>

                {/* Tab selectors for User vs Client Login */}
                <div className="flex border-b border-slate-800 mb-6">
                    <button
                        type="button"
                        onClick={() => { setLoginType('user'); setError(''); }}
                        disabled={isPending}
                        className={`flex-1 pb-3 text-sm font-medium border-b-2 transition-all flex items-center justify-center gap-2 ${
                            loginType === 'user'
                                ? 'border-indigo-500 text-indigo-400'
                                : 'border-transparent text-slate-400 hover:text-slate-200'
                        }`}
                    >
                        <User className="w-4 h-4" />
                        User / Admin
                    </button>
                    <button
                        type="button"
                        onClick={() => { setLoginType('client'); setError(''); }}
                        disabled={isPending}
                        className={`flex-1 pb-3 text-sm font-medium border-b-2 transition-all flex items-center justify-center gap-2 ${
                            loginType === 'client'
                                ? 'border-indigo-500 text-indigo-400'
                                : 'border-transparent text-slate-400 hover:text-slate-200'
                        }`}
                    >
                        <Building className="w-4 h-4" />
                        Client Org
                    </button>
                </div>

                <div className="space-y-6">
                    {error && (
                        <div className="p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm text-center">
                            {error}
                        </div>
                    )}
                    <form onSubmit={handleSubmit} className="space-y-4">
                        {loginType === 'user' ? (
                            <div className="space-y-2">
                                <label htmlFor="username" className="text-sm font-medium text-slate-300">
                                    Username
                                </label>
                                <div className="relative">
                                    <User className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                    <input
                                        type="text"
                                        id="username"
                                        value={username}
                                        onChange={(e) => setUsername(e.target.value)}
                                        required
                                        disabled={isPending}
                                        className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                        placeholder="Enter your username"
                                    />
                                </div>
                            </div>
                        ) : (
                            <div className="space-y-2">
                                <label htmlFor="email" className="text-sm font-medium text-slate-300">
                                    Business Email
                                </label>
                                <div className="relative">
                                    <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                    <input
                                        type="email"
                                        id="email"
                                        value={email}
                                        onChange={(e) => setEmail(e.target.value)}
                                        required
                                        disabled={isPending}
                                        className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                        placeholder="Enter client email"
                                    />
                                </div>
                            </div>
                        )}

                        <div className="space-y-2">
                            <label htmlFor="password" className="text-sm font-medium text-slate-300">
                                Password
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input
                                    type="password"
                                    id="password"
                                    value={password}
                                    onChange={(e) => setPassword(e.target.value)}
                                    required
                                    disabled={isPending}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                    placeholder="Enter your password"
                                />
                            </div>
                        </div>

                        <button
                            type="submit"
                            className="w-full py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg font-medium transition-colors flex items-center justify-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                            disabled={isPending}
                        >
                            {isPending ? (
                                <>
                                    <Loader2 className="w-4 h-4 animate-spin" />
                                    Signing in...
                                </>
                            ) : (
                                'Sign In'
                            )}
                        </button>
                    </form>

                    <div className="text-center text-sm text-slate-400 mt-4">
                        Don't have an account?{' '}
                        <Link to="/register" className="text-indigo-400 hover:text-indigo-300 font-medium transition-colors">
                            Register as a Client
                        </Link>
                    </div>
                </div>
            </div>
        </div>
    );
}

export default Login;
