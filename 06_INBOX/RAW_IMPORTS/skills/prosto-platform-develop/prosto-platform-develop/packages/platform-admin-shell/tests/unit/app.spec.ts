import { createPinia, setActivePinia } from 'pinia';
import { shallowMount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '@/app/app.vue';

describe('admin shell app authentication UI', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('should expose only login retry controls after callback failure', () => {
    const wrapper = shallowMount(App, {
      props: {
        authenticationFailed: true,
        logout: vi.fn(),
      },
      global: {
        mocks: {
          $t: (key: string) => key,
        },
      },
    });

    expect(wrapper.get('[data-testid="auth-retry"]').attributes('href')).toBe(
      '/auth/login',
    );
    expect(wrapper.find('[data-testid="logout"]').exists()).toBe(false);
    expect(wrapper.find('router-view-stub').exists()).toBe(false);
    expect(wrapper.find('degraded-mode-banner-stub').exists()).toBe(false);
  });

  it('should invoke logout from the regular app bar', async () => {
    const logout = vi.fn().mockResolvedValue(undefined);
    const wrapper = shallowMount(App, {
      props: {
        authenticationFailed: false,
        logout,
      },
      global: {
        mocks: {
          $t: (key: string) => key,
        },
      },
    });

    await wrapper.get('[data-testid="logout"]').trigger('click');

    expect(logout).toHaveBeenCalledOnce();
    expect(wrapper.find('[data-testid="auth-retry"]').exists()).toBe(false);
  });
});
