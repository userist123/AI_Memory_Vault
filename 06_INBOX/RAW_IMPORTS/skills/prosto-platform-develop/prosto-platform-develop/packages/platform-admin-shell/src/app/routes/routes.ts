import type { RouteRecordRaw } from 'vue-router';
import DashboardPage from '@/pages/dashboard/ui/dashboard-page.vue';
import DiagnosticsPage from '@/pages/diagnostics/ui/diagnostics-page.vue';
import LoginPage from '@/pages/login/ui/login-page.vue';
import ChangePasswordPage from '@/pages/change-password/ui/change-password-page.vue';

export const routes: readonly RouteRecordRaw[] = [
  {
    path: '/',
    name: 'dashboard',
    component: DashboardPage,
  },
  {
    path: '/diagnostics',
    name: 'diagnostics',
    component: DiagnosticsPage,
  },
  {
    path: '/login',
    name: 'login',
    component: LoginPage,
    meta: { public: true },
  },
  {
    path: '/change-password',
    name: 'change-password',
    component: ChangePasswordPage,
    meta: { public: true },
  },
];
