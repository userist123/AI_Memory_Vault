<template>
  <v-alert
    v-if="visible"
    type="warning"
    variant="elevated"
    density="compact"
    class="rounded-0"
    closable
    @click:close="dismissed = true"
  >
    <template #title>
      {{ $t('degraded_banner.title') }}
    </template>

    <template #text>
      <span>{{ degradedMode.message }}</span>

      <span class="ml-2 text-caption">
        ({{ formatTimestamp(degradedMode.timestamp) }})
      </span>
    </template>

    <template #append>
      <v-btn :to="{ name: 'diagnostics' }" variant="plain" size="small" slim>
        {{ $t('degraded_banner.viewDiagnostics') }}
      </v-btn>
    </template>
  </v-alert>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useDiagnosticsStore } from '@/entities';
import { storeToRefs } from 'pinia';

const diagnosticsStore = useDiagnosticsStore();
const { isDegraded, degradedMode } = storeToRefs(diagnosticsStore);

const dismissed = ref(false);

const visible = computed(() => isDegraded.value && !dismissed.value);

function formatTimestamp(date: Date): string {
  return date.toLocaleTimeString();
}
</script>
