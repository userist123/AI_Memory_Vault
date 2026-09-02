import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { clientApi } from '../api/api';
import { useAuth } from '../contexts/AuthContext';
import { Users, UserPlus, Loader2, ShieldCheck, Eye, X, Mail, Lock, User, ChevronDown } from 'lucide-react';
import { cn } from '../lib/utils';

// ─── Invite User Modal ────────────────────────────────────────────────────────
function InviteUserModal({ clientId, onClose }) {
    const queryClient = useQueryClient();
    const [form, setForm] = useState({ username: '', email: '', password: '', role: 'CLIENT_VIEWER' });
    const [error, setError] = useState('');

    const set = (field) => (e) => setForm((f) => ({ ...f, [field]: e.target.value }));

    const mutation = useMutation({
        mutationFn: (data) => clientApi.createClientUser(clientId, data),
        onSuccess: (res) => {
            if (res?.success === false) {
                setError(res.message || 'Failed to invite user');
                return;
            }
            queryClient.invalidateQueries({ queryKey: ['users'] });
            onClose();
        },
        onError: (err) => {
            setError(err.response?.data?.message || 'Failed to connect to server');
        },
    });

    const handleSubmit = (e) => {
        e.preventDefault();
        setError('');
        mutation.mutate(form);
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
            {/* Backdrop */}
            <div
                className="absolute inset-0 bg-background/80 backdrop-blur-sm"
                onClick={onClose}
            />

            {/* Modal */}
            <div className="relative w-full max-w-md rounded-2xl bg-card border border-border shadow-2xl p-6 text-foreground">
                {/* Header */}
                <div className="flex items-center justify-between mb-6">
                    <div className="flex items-center gap-3">
                        <div className="w-9 h-9 rounded-xl bg-primary/15 flex items-center justify-center ring-1 ring-primary/25">
                            <UserPlus className="w-4 h-4 text-primary" />
                        </div>
                        <div>
                            <h2 className="text-base font-semibold">Invite User</h2>
                            <p className="text-xs text-muted-foreground">Add a new member to your organization</p>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        className="w-7 h-7 flex items-center justify-center rounded-lg text-muted-foreground hover:text-foreground hover:bg-muted transition-colors cursor-pointer"
                    >
                        <X className="w-4 h-4" />
                    </button>
                </div>

                {error && (
                    <div className="mb-4 p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-sm">
                        {error}
                    </div>
                )}

                <form onSubmit={handleSubmit} className="space-y-4">
                    {/* Username */}
                    <div className="space-y-1.5">
                        <label htmlFor="inv-username" className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                            Username
                        </label>
                        <div className="relative">
                            <User className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none" />
                            <input
                                id="inv-username"
                                type="text"
                                value={form.username}
                                onChange={set('username')}
                                required
                                disabled={mutation.isPending}
                                placeholder="e.g. Katari_6"
                                className="w-full pl-10 pr-4 py-2.5 bg-background border border-border rounded-lg text-sm text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/60 transition-all disabled:opacity-50"
                            />
                        </div>
                    </div>

                    {/* Email */}
                    <div className="space-y-1.5">
                        <label htmlFor="inv-email" className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                            Email
                        </label>
                        <div className="relative">
                            <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none" />
                            <input
                                id="inv-email"
                                type="email"
                                value={form.email}
                                onChange={set('email')}
                                required
                                disabled={mutation.isPending}
                                placeholder="e.g. katari6@gmail.com"
                                className="w-full pl-10 pr-4 py-2.5 bg-background border border-border rounded-lg text-sm text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/60 transition-all disabled:opacity-50"
                            />
                        </div>
                    </div>

                    {/* Password */}
                    <div className="space-y-1.5">
                        <label htmlFor="inv-password" className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                            Password
                        </label>
                        <div className="relative">
                            <Lock className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none" />
                            <input
                                id="inv-password"
                                type="password"
                                value={form.password}
                                onChange={set('password')}
                                required
                                disabled={mutation.isPending}
                                placeholder="Minimum 6 characters"
                                className="w-full pl-10 pr-4 py-2.5 bg-background border border-border rounded-lg text-sm text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/60 transition-all disabled:opacity-50"
                            />
                        </div>
                    </div>

                    {/* Role */}
                    <div className="space-y-1.5">
                        <label htmlFor="inv-role" className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                            Role
                        </label>
                        <div className="relative">
                            <ChevronDown className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none" />
                            <select
                                id="inv-role"
                                value={form.role}
                                onChange={set('role')}
                                disabled={mutation.isPending}
                                className="w-full appearance-none px-4 py-2.5 bg-background border border-border rounded-lg text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/60 transition-all disabled:opacity-50 cursor-pointer"
                            >
                                <option value="CLIENT_ADMIN">Client Admin</option>
                                <option value="CLIENT_VIEWER">Client Viewer</option>
                            </select>
                        </div>
                        <p className="text-xs text-muted-foreground/60">
                            {form.role === 'CLIENT_ADMIN'
                                ? 'Full access: manage users, API keys, and settings.'
                                : 'Read-only access to analytics and monitoring.'}
                        </p>
                    </div>

                    {/* Actions */}
                    <div className="flex gap-3 pt-2">
                        <button
                            type="button"
                            onClick={onClose}
                            disabled={mutation.isPending}
                            className="flex-1 py-2.5 rounded-lg text-sm font-medium text-muted-foreground bg-muted hover:bg-muted/80 transition-colors disabled:opacity-50 cursor-pointer"
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            disabled={mutation.isPending}
                            className="flex-1 py-2.5 rounded-lg text-sm font-medium text-primary-foreground bg-primary hover:bg-primary/90 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2 cursor-pointer"
                        >
                            {mutation.isPending ? (
                                <><Loader2 className="w-4 h-4 animate-spin" /> Inviting…</>
                            ) : (
                                <><UserPlus className="w-4 h-4" /> Invite User</>
                            )}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}

const ROLE_CONFIG = {
    client_admin: { label: 'Admin', color: 'bg-primary/10 text-primary ring-primary/20' },
    client_viewer: { label: 'Viewer', color: 'bg-muted text-muted-foreground ring-border/50' },
    super_admin: { label: 'Super Admin', color: 'bg-sky-500/10 text-sky-400 ring-sky-500/20' },
};

function UserRow({ user }) {
    const roleConf = ROLE_CONFIG[user.role] || { label: user.role, color: 'bg-muted text-muted-foreground ring-border/50' };
    const initials = (user.username || user.email || 'U').slice(0, 2).toUpperCase();

    return (
        <tr className="border-b border-border/50 hover:bg-muted/30 transition-colors">
            <td className="px-4 py-3">
                <div className="flex items-center gap-3">
                    <div className="w-8 h-8 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center text-xs font-semibold text-primary">
                        {initials}
                    </div>
                    <div>
                        <p className="text-sm font-medium text-foreground">{user.username || '—'}</p>
                        <p className="text-xs text-muted-foreground">{user.email || '—'}</p>
                    </div>
                </div>
            </td>
            <td className="px-4 py-3">
                <span className={cn(
                    'inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ring-1',
                    roleConf.color
                )}>
                    {roleConf.label}
                </span>
            </td>
            <td className="px-4 py-3">
                <span className={cn(
                    'inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium ring-1',
                    user.isActive
                        ? 'bg-green-500/10 text-green-400 ring-green-500/20'
                        : 'bg-muted text-muted-foreground ring-border/50'
                )}>
                    <span className={cn('w-1.5 h-1.5 rounded-full', user.isActive ? 'bg-green-400' : 'bg-muted-foreground')} />
                    {user.isActive ? 'Active' : 'Inactive'}
                </span>
            </td>
            <td className="px-4 py-3 text-xs text-muted-foreground">
                {user.createdAt ? new Date(user.createdAt).toLocaleDateString() : '—'}
            </td>
        </tr>
    );
}

function EmptyState() {
    return (
        <div className="flex flex-col items-center justify-center py-16 text-center">
            <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center mb-4 ring-1 ring-primary/20">
                <Users className="w-6 h-6" />
            </div>
            <h3 className="text-sm font-medium text-foreground mb-1">No users yet</h3>
            <p className="text-xs text-muted-foreground max-w-xs">Invite team members to collaborate on your API management.</p>
        </div>
    );
}

export function UsersPage() {
    const { user } = useAuth();
    const isAdmin = user?.role === 'client_admin' || user?.role === 'super_admin';
    const [inviteOpen, setInviteOpen] = useState(false);

    const { data, isPending, error } = useQuery({
        queryKey: ['users', user?.clientId],
        queryFn: () => clientApi.getMyUsers(),
        enabled: !!user?.clientId,
    });

    const users = data?.data || [];
    const admins = users.filter(u => u.role === 'client_admin');
    const viewers = users.filter(u => u.role === 'client_viewer');

    return (
        <div className="p-6 max-w-5xl mx-auto space-y-6">
            {/* Header */}
            <div className="flex items-start justify-between">
                <div>
                    <h1 className="text-xl font-semibold text-foreground">Team Members</h1>
                    <p className="text-sm text-muted-foreground mt-1">Manage users in your organization</p>
                </div>
                {isAdmin && (
                    <button
                        onClick={() => setInviteOpen(true)}
                        className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary/90 text-primary-foreground text-sm font-medium rounded-lg transition-colors cursor-pointer"
                    >
                        <UserPlus className="w-4 h-4" />
                        Invite User
                    </button>
                )}

                {inviteOpen && (
                    <InviteUserModal
                        clientId={user?.clientId}
                        onClose={() => setInviteOpen(false)}
                    />
                )}
            </div>

            {/* Stats */}
            <div className="grid grid-cols-3 gap-4">
                {[
                    { label: 'Total Members', value: users.length, icon: Users },
                    { label: 'Admins', value: admins.length, icon: ShieldCheck },
                    { label: 'Viewers', value: viewers.length, icon: Eye },
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

            {/* Table */}
            <div className="bg-card/50 border border-border rounded-xl overflow-hidden">
                <div className="px-4 py-3 border-b border-border flex items-center justify-between">
                    <div className="flex items-center gap-2 text-sm font-medium text-foreground">
                        <Users className="w-4 h-4 text-primary" />
                        Users
                    </div>
                    <span className="text-xs text-muted-foreground">{users.length} total</span>
                </div>

                {isPending ? (
                    <div className="flex items-center justify-center py-16">
                        <Loader2 className="w-6 h-6 text-primary animate-spin" />
                    </div>
                ) : error ? (
                    <div className="flex items-center justify-center py-16 text-sm text-destructive">
                        Failed to load users
                    </div>
                ) : users.length === 0 ? (
                    <EmptyState />
                ) : (
                    <div className="overflow-x-auto">
                        <table className="w-full text-sm">
                            <thead>
                                <tr className="border-b border-border text-left">
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Member</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Role</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Status</th>
                                    <th className="px-4 py-2.5 text-xs font-medium text-muted-foreground uppercase tracking-wider">Joined</th>
                                </tr>
                            </thead>
                            <tbody>
                                {users.map((u) => <UserRow key={u._id} user={u} />)}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
}
