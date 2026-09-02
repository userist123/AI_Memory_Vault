<template>
  <v-app>
    <v-main v-if="authenticationFailed" class="auth-failure">
      <v-card class="auth-failure__card" elevation="12">
        <v-card-title class="text-h4">{{
          $t('authentication.failedTitle')
        }}</v-card-title>

        <v-card-text>{{ $t('authentication.failedMessage') }}</v-card-text>

        <v-card-actions>
          <v-btn
            data-testid="auth-retry"
            color="primary"
            href="/auth/login"
            variant="flat"
          >
            {{ $t('authentication.tryAgain') }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-main>

    <template v-else-if="isPublicRoute">
      <router-view />
    </template>

    <template v-else>
      <v-app-bar theme="dark" color="primary" density="comfortable">
        <v-app-bar-title>{{ $t('app.title') }}</v-app-bar-title>

        <v-spacer />

        <div class="d-flex align-center mx-5">
          <v-btn :to="{ name: 'diagnostics' }" density="comfortable" icon>
            <v-badge
              :model-value="!!rejectedCount"
              :content="rejectedCount"
              color="error"
            >
              <v-icon icon="mdi-bug" />
            </v-badge>
          </v-btn>

          <v-btn
            data-testid="logout"
            :loading="isLoggingOut"
            class="ml-3"
            prepend-icon="mdi-logout"
            variant="text"
            @click="handleLogout"
          >
            {{ $t('authentication.logout') }}
          </v-btn>
        </div>
      </v-app-bar>

      <v-main>
        <DegradedModeBanner />

        <router-view />
      </v-main>
    </template>
  </v-app>
</template>

<script setup lang="ts">
import { storeToRefs } from 'pinia';
import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';
import { useDiagnosticsStore } from '@/entities/diagnostics';
import { DegradedModeBanner } from '@/widgets/degraded-mode-banner';

const props = defineProps<{
  authenticationFailed: boolean;
  logout: () => Promise<void>;
}>();

const diagnosticsStore = useDiagnosticsStore();
const { rejectedCount } = storeToRefs(diagnosticsStore);
const route = useRoute();
const isPublicRoute = computed(() => route?.meta.public === true);

const isLoggingOut = ref(false);

async function handleLogout(): Promise<void> {
  if (isLoggingOut.value) {
    return;
  }

  isLoggingOut.value = true;

  try {
    await props.logout();
  } finally {
    isLoggingOut.value = false;
  }
}
</script>

<style scoped>
.auth-failure {
  align-items: center;
  background:
    radial-gradient(
      circle at 20% 15%,
      rgb(var(--v-theme-primary), 0.18),
      transparent 42%
    ),
    rgb(var(--v-theme-surface));
  display: flex;
  justify-content: center;
  min-height: 100vh;
  padding: 24px;
}

.auth-failure__card {
  border-top: 4px solid rgb(var(--v-theme-primary));
  max-width: 520px;
  padding: 24px;
  width: 100%;
}
</style>
