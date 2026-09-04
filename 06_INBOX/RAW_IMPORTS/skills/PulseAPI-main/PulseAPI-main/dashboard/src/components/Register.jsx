import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { clientApi } from '../api/api';
import { Activity, Lock, Mail, Building, Globe, FileText, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';

function Register() {
    const [name, setName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [website, setWebsite] = useState('');
    const [description, setDescription] = useState('');
    const [error, setError] = useState('');

    const registerMutation = useMutation({
        mutationFn: clientApi.clientRegister,
        onSuccess: (data) => {
            if (data.success) {
                const token = data.data?.token || data.token;
                if (token) {
                    localStorage.setItem('authToken', token);
                }
                window.location.href = '/';
            } else {
                setError(data.message || 'Registration failed');
            }
        },
        onError: (err) => {
            setError(err.response?.data?.message || 'Failed to connect to server');
        },
    });

    const handleSubmit = (e) => {
        e.preventDefault();
        setError('');
        registerMutation.mutate({
            name,
            email,
            password,
            website,
            description,
        });
    };

    return (
        <div className="relative min-h-screen flex items-center justify-center overflow-y-auto py-12 bg-slate-950 text-slate-50">
            {/* Animated background elements */}
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-indigo-600/20 rounded-full blur-[120px]" />
                <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-purple-600/20 rounded-full blur-[120px]" />
            </div>

            <div className="relative w-full max-w-lg p-8 mx-4 rounded-2xl bg-slate-900/50 border border-slate-800 backdrop-blur-xl shadow-2xl">
                <div className="text-center mb-8">
                    <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-indigo-600/10 text-indigo-400 mb-4 ring-1 ring-indigo-500/20">
                        <Activity className="w-6 h-6" />
                    </div>
                    <h1 className="text-2xl font-semibold tracking-tight">
                        Client Registration
                    </h1>
                    <p className="text-sm text-slate-400 mt-2">
                        Register your organization to start monitoring APIs
                    </p>
                </div>
                <div className="space-y-6">
                    {error && (
                        <div className="p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm text-center">
                            {error}
                        </div>
                    )}
                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div className="space-y-2">
                            <label htmlFor="name" className="text-sm font-medium text-slate-300">
                                Organization Name *
                            </label>
                            <div className="relative">
                                <Building className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input
                                    type="text"
                                    id="name"
                                    value={name}
                                    onChange={(e) => setName(e.target.value)}
                                    required
                                    disabled={registerMutation.isPending}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                    placeholder="e.g. Acme Corp"
                                />
                            </div>
                        </div>

                        <div className="space-y-2">
                            <label htmlFor="email" className="text-sm font-medium text-slate-300">
                                Business Email *
                            </label>
                            <div className="relative">
                                <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input
                                    type="email"
                                    id="email"
                                    value={email}
                                    onChange={(e) => setEmail(e.target.value)}
                                    required
                                    disabled={registerMutation.isPending}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                    placeholder="admin@company.com"
                                />
                            </div>
                        </div>

                        <div className="space-y-2">
                            <label htmlFor="password" className="text-sm font-medium text-slate-300">
                                Password *
                            </label>
                            <div className="relative">
                                <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input
                                    type="password"
                                    id="password"
                                    value={password}
                                    onChange={(e) => setPassword(e.target.value)}
                                    required
                                    disabled={registerMutation.isPending}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                    placeholder="Minimum 6 characters"
                                />
                            </div>
                        </div>

                        <div className="space-y-2">
                            <label htmlFor="website" className="text-sm font-medium text-slate-300">
                                Website URL <span className="text-slate-500">(Optional)</span>
                            </label>
                            <div className="relative">
                                <Globe className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                                <input
                                    type="url"
                                    id="website"
                                    value={website}
                                    onChange={(e) => setWebsite(e.target.value)}
                                    disabled={registerMutation.isPending}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50"
                                    placeholder="https://company.com"
                                />
                            </div>
                        </div>

                        <div className="space-y-2">
                            <label htmlFor="description" className="text-sm font-medium text-slate-300">
                                Organization Description <span className="text-slate-500">(Optional)</span>
                            </label>
                            <div className="relative">
                                <FileText className="absolute left-3 top-3 w-4 h-4 text-slate-500" />
                                <textarea
                                    id="description"
                                    value={description}
                                    onChange={(e) => setDescription(e.target.value)}
                                    disabled={registerMutation.isPending}
                                    rows={3}
                                    className="w-full pl-10 pr-4 py-2 bg-slate-950/50 border border-slate-800 rounded-lg focus:ring-2 focus:ring-indigo-500/50 focus:border-indigo-500 outline-none transition-all text-slate-100 placeholder:text-slate-600 disabled:opacity-50 resize-none"
                                    placeholder="Describe your API use case..."
                                />
                            </div>
                        </div>

                        <button
                            type="submit"
                            className="w-full py-2.5 mt-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg font-medium transition-colors flex items-center justify-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                            disabled={registerMutation.isPending}
                        >
                            {registerMutation.isPending ? (
                                <>
                                    <Loader2 className="w-4 h-4 animate-spin" />
                                    Registering...
                                </>
                            ) : (
                                'Register Organization'
                            )}
                        </button>
                    </form>

                    <div className="text-center text-sm text-slate-400 mt-4">
                        Already have an account?{' '}
                        <Link to="/login" className="text-indigo-400 hover:text-indigo-300 font-medium transition-colors">
                            Sign In
                        </Link>
                    </div>
                </div>
            </div>
        </div>
    );
}

export default Register;
