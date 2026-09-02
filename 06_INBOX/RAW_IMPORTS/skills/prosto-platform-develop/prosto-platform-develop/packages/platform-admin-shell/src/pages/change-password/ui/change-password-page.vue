<template>
  <v-container class="auth-page">
    <v-card class="auth-page__card" elevation="12">
      <v-card-title class="text-h5">
        {{ $t('authentication.changePassword.title') }}
      </v-card-title>

      <v-card-text>
        <p>{{ $t('authentication.changePassword.message') }}</p>

        <v-alert v-if="failed" type="error" variant="tonal" class="mb-4">
          {{ $t('authentication.changePassword.failed') }}
        </v-alert>

        <form @submit.prevent="submit">
          <v-text-field
            v-model="currentPassword"
            autocomplete="current-password"
            :disabled="submitting"
            :label="$t('authentication.changePassword.currentPassword')"
            :rules="[required]"
            type="password"
          />

          <v-text-field
            v-model="newPassword"
            autocomplete="new-password"
            :disabled="submitting"
            :label="$t('authentication.changePassword.newPassword')"
            :rules="[minimumLength]"
            type="password"
          />

          <v-btn
            block
            color="primary"
            :disabled="!isValid || submitting"
            :loading="submitting"
            type="submit"
          >
            {{ $t('authentication.changePassword.submit') }}
          </v-btn>
        </form>
      </v-card-text>
    </v-card>
  </v-container>
</template>

<script setup lang="ts">
import { computed, inject, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import { ADMIN_AUTHENTICATION_CONTEXT } from '@/features/authentication';

const authentication = inject(ADMIN_AUTHENTICATION_CONTEXT);

if (!authentication) {
  throw new Error('Admin authentication context is not available.');
}

const route = useRoute();
const { t } = useI18n();
const currentPassword = ref('');
const newPassword = ref('');
const submitting = ref(false);
const failed = ref(false);
const isValid = computed(
  () => currentPassword.value.length >= 8 && newPassword.value.length >= 8,
);
const required = (value: string): boolean | string =>
  value.length > 0 || t('authentication.validation.required');
const minimumLength = (value: string): boolean | string =>
  value.length >= 8 || t('authentication.validation.minimumPasswordLength');

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
    await authentication!.authClient.changePassword(
      currentPassword.value,
      newPassword.value,
    );

    currentPassword.value = '';
    newPassword.value = '';

    await authentication!.completeAuthentication(destination());
  } catch {
    currentPassword.value = '';
    newPassword.value = '';
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
