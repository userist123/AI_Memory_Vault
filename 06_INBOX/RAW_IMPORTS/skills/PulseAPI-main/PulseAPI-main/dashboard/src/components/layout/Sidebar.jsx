import { NavLink } from 'react-router-dom';
import { cn } from '../../lib/utils';
import { useAuth } from '../../contexts/AuthContext';
import {
    LayoutDashboard,
    Settings,
    Zap,
    Users,
    KeyRound,
    ShieldCheck,
    ChevronRight,
} from 'lucide-react';

// Nav items per role
const NAV_ITEMS = {
    super_admin: [
        { title: 'Overview', href: '/', icon: LayoutDashboard, end: true },
        { title: 'All Clients', href: '/admin/clients', icon: ShieldCheck },
    ],
    client_admin: [
        { title: 'Overview', href: '/', icon: LayoutDashboard, end: true },
        { title: 'API Keys', href: '/api-keys', icon: KeyRound },
        { title: 'Users', href: '/users', icon: Users },
    ],
    client_viewer: [
        { title: 'Overview', href: '/', icon: LayoutDashboard, end: true },
    ],
};

export function Sidebar({ isOpen, onClose }) {
    const { user } = useAuth();
    const role = user?.role || 'client_viewer';
    const navItems = NAV_ITEMS[role] || NAV_ITEMS.client_viewer;

    const roleBadge = {
        super_admin: { label: 'Super Admin', color: 'bg-purple-50 dark:bg-purple-500/10 text-purple-700 dark:text-purple-400 ring-1 ring-purple-600/10 dark:ring-purple-500/20' },
        client_admin: { label: 'Admin', color: 'bg-indigo-50 dark:bg-indigo-500/10 text-indigo-700 dark:text-indigo-400 ring-1 ring-indigo-600/10 dark:ring-indigo-500/20' },
        client_viewer: { label: 'Viewer', color: 'bg-slate-100 dark:bg-slate-500/10 text-slate-700 dark:text-slate-400 ring-1 ring-slate-650/10 dark:ring-slate-500/20' },
    }[role] || { label: role, color: 'bg-slate-100 dark:bg-slate-500/10 text-slate-700 dark:text-slate-400 ring-1 ring-slate-650/10 dark:ring-slate-500/20' };

    return (
        <>
            {/* Mobile overlay */}
            {isOpen && (
                <div
                    className="fixed inset-0 z-20 bg-slate-950/70 backdrop-blur-sm lg:hidden"
                    onClick={onClose}
                />
            )}

            {/* Sidebar */}
            <aside
                className={cn(
                    'fixed top-0 left-0 z-30 h-full w-64 flex flex-col',
                    'bg-card border-r border-border/45',
                    'transition-transform duration-300 ease-in-out',
                    'lg:translate-x-0 lg:static lg:z-auto',
                    isOpen ? 'translate-x-0' : '-translate-x-full'
                )}
            >
                {/* Logo */}
                <div className="flex items-center gap-3 px-5 py-5 border-b border-border/45">
                    <div className="flex items-center justify-center w-8 h-8 rounded-lg bg-indigo-600/10 text-indigo-400 ring-1 ring-indigo-500/20">
                        <Zap className="w-4 h-4" />
                    </div>
                    <div>
                        <p className="text-sm font-semibold text-foreground tracking-tight">PulseAPI</p>
                        <p className="text-[10px] text-muted-foreground">API Management Platform</p>
                    </div>
                </div>

                {/* User badge */}
                <div className="px-4 py-3 border-b border-border/40">
                    <div className="flex items-center gap-2.5 px-2 py-1.5 rounded-lg bg-muted/40">
                        <div className="flex-shrink-0 w-7 h-7 rounded-full bg-indigo-600 flex items-center justify-center text-xs font-semibold text-white">
                            {(user?.username || user?.name || 'U')[0].toUpperCase()}
                        </div>
                        <div className="flex-1 min-w-0">
                            <p className="text-xs font-medium text-foreground truncate">
                                {user?.username || user?.name || 'User'}
                            </p>
                            <span className={cn(
                                'inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium ring-1',
                                roleBadge.color
                            )}>
                                {roleBadge.label}
                            </span>
                        </div>
                    </div>
                </div>

                {/* Navigation */}
                <nav className="flex-1 px-3 py-4 space-y-0.5 overflow-y-auto">
                    <p className="text-[10px] font-medium text-muted-foreground/60 uppercase tracking-wider px-2 mb-2">
                        Navigation
                    </p>
                    {navItems.map((item) => {
                        const Icon = item.icon;
                        return (
                            <NavLink
                                key={item.href}
                                to={item.href}
                                end={item.end}
                                onClick={onClose}
                                className={({ isActive }) => cn(
                                    'group flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-all duration-150',
                                    isActive
                                        ? 'bg-indigo-600/10 text-indigo-600 dark:text-indigo-400 font-medium'
                                        : 'text-muted-foreground hover:text-foreground hover:bg-muted/50'
                                )}
                            >
                                {({ isActive }) => (
                                    <>
                                        <Icon className={cn('w-4 h-4 flex-shrink-0', isActive ? 'text-indigo-600 dark:text-indigo-400' : 'text-muted-foreground/75 group-hover:text-foreground')} />
                                        <span className="flex-1">{item.title}</span>
                                        {isActive && <ChevronRight className="w-3 h-3 text-indigo-600/60 dark:text-indigo-400/60" />}
                                    </>
                                )}
                            </NavLink>
                        );
                    })}
                </nav>

                {/* Bottom nav */}
                <div className="px-3 py-3 border-t border-border/45">
                    <NavLink
                        to="/settings"
                        onClick={onClose}
                        className={({ isActive }) => cn(
                            'group flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-all duration-150',
                            isActive
                                ? 'bg-indigo-600/10 text-indigo-600 dark:text-indigo-400 font-medium'
                                : 'text-muted-foreground hover:text-foreground hover:bg-muted/50'
                        )}
                    >
                        <Settings className="w-4 h-4 text-muted-foreground/75 group-hover:text-foreground" />
                        <span>Settings</span>
                    </NavLink>
                </div>
            </aside>
        </>
    );
}
