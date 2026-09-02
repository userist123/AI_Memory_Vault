import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { clientApi } from '../api/api';
import { useAuth } from '../contexts/AuthContext';
import { KeyRound, Copy, CheckCircle2, PlusCircle, Eye, EyeOff, Trash2, Loader2, ShieldCheck, X } from 'lucide-react';
import { useState } from 'react';
import { cn } from '../lib/utils';

function CopyButton({ text }) {
    const [copied, setCopied] = useState(false);
    const handleCopy = () => {
        navigator.clipboard.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
    };
    return (
        <button
            onClick={handleCopy}
            className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition-all cursor-pointer"
            title="Copy key"
        >
            {copied ? <CheckCircle2 className="w-3.5 h-3.5 text-green-400" /> : <Copy className="w-3.5 h-3.5" />}
        </button>
    );
}

function ApiKeyRow({ apiKey }) {
    const [visible, setVisible] = useState(false);
    const maskedKey = apiKey.key || apiKey.keyValue ? `${(apiKey.key || apiKey.keyValue).slice(0, 10)}${'•'.repeat(20)}${(apiKey.key || apiKey.keyValue).slice(-4)}` : '••••••••••••••••••••••••••••••';
    const displayKey = visible ? (apiKey.key || apiKey.keyValue) : maskedKey;

    return (
        <tr className="border-b border-border/50 hover:bg-muted/30 transition-colors">
            <td className="px-4 py-3">
                <div className="flex items-center gap-2">
                    <div className="w-7 h-7 rounded-lg bg-primary/10 flex items-center justify-center">
                        <KeyRound className="w-3.5 h-3.5 text-primary" />
                    </div>
                    <div>
                        <p className="text-sm font-medium text-foreground">{apiKey.name || 'Unnamed Key'}</p>
                        <p className="text-xs text-muted-foreground">{apiKey.description || '—'}</p>
                    </div>
                </div>
            </td>
            <td className="px-4 py-3">
                <div className="flex items-center gap-1.5">
                    <code className="text-xs font-mono text-muted-foreground bg-muted px-2 py-1 rounded max-w-[220px] truncate">
                        {displayKey}
                    </code>
                    <button onClick={() => setVisible(!visible)} className="p-1 text-muted-foreground hover:text-foreground transition-colors cursor-pointer">
                        {visible ? <EyeOff className="w-3 h-3" /> : <Eye className="w-3 h-3" />}
                    </button>
                    {(apiKey.key || apiKey.keyValue) && <CopyButton text={apiKey.key || apiKey.keyValue} />}
                </div>
            </td>
            <td className="px-4 py-3">
                <span className={cn(
                    'inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium ring-1',
                    apiKey.isActive
                        ? 'bg-green-500/10 text-green-400 ring-green-500/20'
                        : 'bg-muted text-muted-foreground ring-border/50'
                )}>
                    <span className={cn('w-1.5 h-1.5 rounded-full', apiKey.isActive ? 'bg-green-400' : 'bg-muted-foreground')} />
                    {apiKey.isActive ? 'Active' : 'Inactive'}
                </span>
            </td>
            <td className="px-4 py-3 text-xs text-muted-foreground">
                {apiKey.createdAt ? new Date(apiKey.createdAt).toLocaleDateString() : '—'}
            </td>
        </tr>
    );
}

function EmptyState({ onCreateClick, showCreateBtn }) {
    return (
        <div className="flex flex-col items-center justify-center py-16 text-center">
            <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center mb-4 ring-1 ring-primary/20">
                <KeyRound className="w-6 h-6" />
            </div>
            <h3 className="text-sm font-medium text-foreground mb-1">No API keys</h3>
            <p className="text-xs text-muted-foreground max-w-xs mb-4">Create your first API key to start integrating with your services.</p>
            {showCreateBtn && (
                <button
                    onClick={onCreateClick}
                    className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary/90 text-primary-foreground text-sm font-medium rounded-lg transition-colors shadow-md cursor-pointer"
                >
                    <PlusCircle className="w-4 h-4" />
                    New Key
                </button>
            )}
        </div>
    );
}

export function ApiKeysPage() {
    const { user } = useAuth();
    const queryClient = useQueryClient();
    const isAdminOrSuperAdmin = user?.role === 'super_admin' || user?.role === 'client_admin';

    const [showModal, setShowModal] = useState(false);
    const [name, setName] = useState('');
    const [description, setDescription] = useState('');
    const [errorMsg, setErrorMsg] = useState('');

    const { data, isPending, error } = useQuery({
        queryKey: ['apiKeys', user?.clientId],
        queryFn: () => clientApi.getMyApiKeys(),
        enabled: !!user?.clientId,
    });

    const createMutation = useMutation({
        mutationFn: ({ clientId, name, description }) => clientApi.createApiKey(clientId, { name, description }),
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ['apiKeys', user?.clientId] });
            setShowModal(false);
            setName('');
            setDescription('');
            setErrorMsg('');
        },
        onError: (err) => {
            setErrorMsg(err.response?.data?.message || 'Failed to create API key');
        }
    });

    const keys = data?.data || [];

    const handleCreateKey = (e) => {
        e.preventDefault();
        setErrorMsg('');
        if (!user?.clientId) {
            setErrorMsg('No client organization associated with your user.');
            return;
        }
        createMutation.mutate({
            clientId: user.clientId,
            name,
            description
        });
    };

    return (
        <div className="p-6 max-w-5xl mx-auto space-y-6">
            {/* Header */}
            <div className="flex items-start justify-between">
                <div>
                    <h1 className="text-xl font-semibold text-foreground">API Keys</h1>
                    <p className="text-sm text-muted-foreground mt-1">Manage your API keys for service integration</p>
                </div>
                {isAdminOrSuperAdmin && (
                    <button
                        onClick={() => setShowModal(true)}
                        className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary/90 text-primary-foreground text-sm font-medium rounded-lg transition-colors shadow-md cursor-pointer"
                    >
                        <PlusCircle className="w-4 h-4" />
                        New Key
                    </button>
                )}
            </div>

            {/* Stats bar */}
            <div className="grid grid-cols-3 gap-4">
                {[
                    { label: 'Total Keys', value: keys.length },
                    { label: 'Active', value: keys.filter(k => k.isActive).length },
                    { label: 'Inactive', value: keys.filter(k => !k.isActive).length },
                ].map((s) => (
                    <div key={s.label} className="bg-card/50 border border-border rounded-xl px-4 py-3">
                        <p className="text-xs text-muted-foreground">{s.label}</p>
                        <p className="text-2xl font-semibold text-foreground mt-0.5">{s.value}</p>
                    </div>
                ))}
            </div>

            {/* Table */}
            <div className="bg-card/50 border border-border rounded-xl overflow-hidden">
                <div className="px-4 py-3 border-b border-border flex items-center justify-between">
                    <div className="flex items-center gap-2 text-sm font-medium text-foreground">
                        <ShieldCheck className="w-4 h-4 text-primary" />
                        API Keys
                    </div>
                    <span className="text-xs text-muted-foreground">{keys.length} total</span>
                </div>

                {isPending ? (
                    <div className="flex items-center justify-center py-16">
                        <Loader2 className="w-6 h-6 text-primary animate-spin" />
                    </div>
                ) : error ? (
                    <div className="flex items-center justify-center py-16 text-sm text-destructive">
                        Failed to load API keys
                    </div>
                ) : keys.length === 0 ? (
                    <EmptyState onCreateClick={() => setShowModal(true)} showCreateBtn={isAdminOrSuperAdmin} />
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-sm">
                            <thead>
                                <tr className="border-b border-border text-left">
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Name</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Key</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Status</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Created</th>
                                </tr>
                            </thead>
                            <tbody>
                                {keys.map((k) => <ApiKeyRow key={k._id || k.id} apiKey={k} />)}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Modal Dialog for New Key creation */}
            {showModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm p-4">
                    <div className="relative w-full max-w-md bg-card border border-border rounded-2xl p-6 shadow-2xl space-y-6">
                        <div className="flex items-center justify-between">
                            <h3 className="text-lg font-semibold text-foreground flex items-center gap-2">
                                <KeyRound className="w-5 h-5 text-primary" />
                                Generate New API Key
                            </h3>
                            <button
                                onClick={() => { setShowModal(false); setErrorMsg(''); }}
                                className="p-1 rounded-lg text-muted-foreground hover:text-foreground hover:bg-muted transition-colors cursor-pointer"
                            >
                                <X className="w-4 h-4" />
                            </button>
                        </div>

                        {errorMsg && (
                            <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-sm">
                                {errorMsg}
                            </div>
                        )}

                        <form onSubmit={handleCreateKey} className="space-y-4">
                            <div className="space-y-1.5">
                                <label htmlFor="key-name" className="text-sm font-medium text-foreground">
                                    Key Name *
                                </label>
                                <input
                                    type="text"
                                    id="key-name"
                                    value={name}
                                    onChange={(e) => setName(e.target.value)}
                                    required
                                    placeholder="e.g. Production Frontend"
                                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:ring-2 focus:ring-primary/50 focus:border-primary outline-none text-foreground placeholder:text-muted-foreground/50 transition-all text-sm"
                                />
                            </div>

                            <div className="space-y-1.5">
                                <label htmlFor="key-description" className="text-sm font-medium text-foreground">
                                    Description <span className="text-muted-foreground">(Optional)</span>
                                </label>
                                <textarea
                                    id="key-description"
                                    value={description}
                                    onChange={(e) => setDescription(e.target.value)}
                                    rows={3}
                                    placeholder="What will this key be used for?"
                                    className="w-full px-3 py-2 bg-background border border-border rounded-lg focus:ring-2 focus:ring-primary/50 focus:border-primary outline-none text-foreground placeholder:text-muted-foreground/50 transition-all text-sm resize-none"
                                />
                            </div>

                            <div className="flex gap-3 justify-end pt-2">
                                <button
                                    type="button"
                                    onClick={() => { setShowModal(false); setErrorMsg(''); }}
                                    disabled={createMutation.isPending}
                                    className="px-4 py-2 bg-muted hover:bg-muted/80 text-muted-foreground hover:text-foreground text-sm font-medium rounded-lg transition-colors cursor-pointer"
                                >
                                    Cancel
                                </button>
                                <button
                                    type="submit"
                                    disabled={createMutation.isPending}
                                    className="px-4 py-2 bg-primary hover:bg-primary/90 text-primary-foreground text-sm font-medium rounded-lg transition-colors flex items-center gap-2 disabled:opacity-50 cursor-pointer"
                                >
                                    {createMutation.isPending ? (
                                        <>
                                            <Loader2 className="w-4 h-4 animate-spin" />
                                            Generating...
                                        </>
                                    ) : (
                                        'Generate Key'
                                    )}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
