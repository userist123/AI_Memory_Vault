<template>
  <v-container class="auth-page">
    <v-card class="auth-page__card" elevation="12">
      <v-card-title class="text-h5">
        {{ $t('authentication.login.title') }}
      </v-card-title>

      <v-card-text>
        <p>{{ $t('authentication.login.message') }}</p>

        <v-alert v-if="failed" type="error" variant="tonal" class="mb-4">
          {{ $t('authentication.login.failed') }}
        </v-alert>

        <form @submit.prevent="submit">
          <v-text-field
            v-model="username"
            autocomplete="username"
            :disabled="submitting"
            :label="$t('authentication.login.username')"
            :rules="[required]"
          />

          <v-text-field
            v-model="password"
            autocomplete="current-password"
            :disabled="submitting"
            :label="$t('authentication.login.password')"
            :rules="[required]"
            type="password"
          />

          <v-btn
            block
            color="primary"
            :disabled="!isValid || submitting"
            :loading="submitting"
            type="submit"
          >
            {{ $t('authentication.login.submit') }}
          </v-btn>
        </form>
      </v-card-text>
    </v-card>
  </v-container>
</template>

<script setup lang="ts">
import { computed, inject, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import { ADMIN_AUTHENTICATION_CONTEXT } from '@/features/authentication';

const authentication = inject(ADMIN_AUTHENTICATION_CONTEXT);

if (!authentication) {
  throw new Error('Admin authentication context is not available.');
}

const router = useRouter();
const route = useRoute();
const { t } = useI18n();
const username = ref('');
const password = ref('');
const submitting = ref(false);
const failed = ref(false);
const isValid = computed(
  () => username.value.trim().length > 0 && password.value.length >= 8,
);
const required = (value: string): boolean | string =>
  value.trim().length > 0 || t('authentication.validation.required');

function destination(): string {
  return typeof route.query.redirect === 'string' ? route.query.redirect : '/';
}

async function submit(): Promise<void> {
  if (!isValid.value || submitting.value) {
    return;
  }

  submitting.value = true;
  failed.value = false;

  try {
    const result = await authentication!.authClient.login(
      username.value.trim(),
      password.value,
    );

    password.value = '';

    if (result.state === 'password-change-required') {
      await router.replace({
        name: 'change-password',
        query: { redirect: destination() },
      });

      return;
    }

    await authentication!.completeAuthentication(destination());
  } catch {
    password.value = '';
    failed.value = true;
  } finally {
    submitting.value = false;
  }
}
</script>

<style scoped>
.auth-page {
  align-items: center;
  display: flex;
  justify-content: center;
  min-height: 100vh;
}

.auth-page__card {
  max-width: 440px;
  width: 100%;
}
</style>
