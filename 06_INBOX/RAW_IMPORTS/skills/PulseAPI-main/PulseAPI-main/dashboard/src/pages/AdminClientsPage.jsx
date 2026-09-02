import { useQuery } from '@tanstack/react-query';
import { clientApi } from '../api/api';
import { useAuth } from '../contexts/AuthContext';
import { Users, Building2, KeyRound, ChevronRight, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';
import { cn } from '../lib/utils';

function ClientCard({ client }) {
    return (
        <div className="bg-card border border-border rounded-xl p-4 hover:border-primary/50 transition-colors group">
            <div className="flex items-start justify-between mb-3">
                <div className="flex items-center gap-3">
                    <div className="w-9 h-9 rounded-lg bg-primary/10 flex items-center justify-center ring-1 ring-primary/20">
                        <Building2 className="w-4 h-4 text-primary" />
                    </div>
                    <div>
                        <p className="text-sm font-medium text-foreground">{client.name}</p>
                        <p className="text-xs text-muted-foreground">{client.slug}</p>
                    </div>
                </div>
                <span className={cn(
                    'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ring-1',
                    client.isActive
                        ? 'bg-green-500/10 text-green-400 ring-green-500/20'
                        : 'bg-muted text-muted-foreground ring-border/50'
                )}>
                    <span className={cn('w-1.5 h-1.5 rounded-full', client.isActive ? 'bg-green-400' : 'bg-muted-foreground')} />
                    {client.isActive ? 'Active' : 'Inactive'}
                </span>
            </div>
            <p className="text-xs text-muted-foreground mb-4 line-clamp-2">{client.description || 'No description provided.'}</p>
            <div className="flex items-center justify-between text-xs text-muted-foreground">
                <span>{new Date(client.createdAt).toLocaleDateString()}</span>
                <Link
                    to={`/admin/clients/${client._id}`}
                    className="flex items-center gap-1 text-primary hover:text-primary/80 transition-colors opacity-0 group-hover:opacity-100"
                >
                    View <ChevronRight className="w-3 h-3" />
                </Link>
            </div>
        </div>
    );
}

export function AdminClientsPage() {
    const { data, isPending, error } = useQuery({
        queryKey: ['admin', 'clients'],
        queryFn: () => clientApi.getClients(),
    });

    const clients = data?.data || [];

    return (
        <div className="p-6 max-w-6xl mx-auto space-y-6">
            <div className="flex items-start justify-between">
                <div>
                    <h1 className="text-xl font-semibold text-foreground">All Clients</h1>
                    <p className="text-sm text-muted-foreground mt-1">Manage all registered client organizations</p>
                </div>
            </div>

            {/* Stats */}
            <div className="grid grid-cols-3 gap-4">
                {[
                    { label: 'Total Clients', value: clients.length, icon: Building2 },
                    { label: 'Active', value: clients.filter(c => c.isActive).length, icon: Users },
                    { label: 'Total Keys', value: clients.reduce((sum, c) => sum + (c.keysCount || 0), 0), icon: KeyRound },
                ].map((s) => {
                    const Icon = s.icon;
                    return (
                        <div key={s.label} className="bg-card/50 border border-border rounded-xl px-4 py-3 flex items-center gap-3">
                            <div className="w-8 h-8 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
                                <Icon className="w-4 h-4 text-primary" />
                            </div>
                            <div>
                                <p className="text-xs text-muted-foreground">{s.label}</p>
                                <p className="text-xl font-semibold text-foreground">{s.value}</p>
                            </div>
                        </div>
                    );
                })}
            </div>

            {/* Grid */}
            {isPending ? (
                <div className="flex items-center justify-center py-16">
                    <Loader2 className="w-6 h-6 text-primary animate-spin" />
                </div>
            ) : error ? (
                <div className="flex items-center justify-center py-16 text-sm text-destructive">
                    Failed to load clients
                </div>
            ) : clients.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-16 text-center">
                    <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center mb-4 ring-1 ring-primary/20">
                        <Building2 className="w-6 h-6" />
                    </div>
                    <h3 className="text-sm font-medium text-foreground mb-1">No clients yet</h3>
                    <p className="text-xs text-muted-foreground">Clients will appear here once they register.</p>
                </div>
            ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {clients.map((c) => <ClientCard key={c._id} client={c} />)}
                </div>
            )}
        </div>
    );
}
