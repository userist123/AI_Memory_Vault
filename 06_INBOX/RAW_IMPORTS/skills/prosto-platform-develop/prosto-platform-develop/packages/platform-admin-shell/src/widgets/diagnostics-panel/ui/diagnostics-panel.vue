<template>
  <v-card>
    <v-card-title class="d-flex align-center">
      <v-icon icon="mdi-bug" class="mr-2" />

      {{ $t('diagnostics_panel.title') }}

      <v-chip v-if="rejectedCount" color="warning" size="small" class="ml-2">
        {{ rejectedCount }}
      </v-chip>
    </v-card-title>

    <v-card-text v-if="isDegraded">
      <v-alert type="warning" variant="tonal" density="compact">
        {{ $t('diagnostics_panel.degradedMode') }}: {{ degradedMode.message }}
      </v-alert>
    </v-card-text>

    <v-list lines="three">
      <v-list-item
        v-for="entry in rejectedEntries"
        :key="`${entry.pluginId}-${entry.reasonCode}-${entry.timestamp.getTime()}`"
        density="comfortable"
      >
        <template #prepend>
          <v-avatar>
            <v-icon color="error" icon="mdi-alert-circle" />
          </v-avatar>
        </template>

        <v-list-item-title class="font-weight-bold">
          {{ entry.pluginId }}

          <v-chip
            :color="reasonColor(entry.reasonCode)"
            size="x-small"
            class="ml-2"
          >
            {{ entry.reasonCode }}
          </v-chip>
        </v-list-item-title>

        <v-list-item-subtitle v-if="entry.message" class="mt-1">
          {{ entry.message }}
        </v-list-item-subtitle>

        <v-list-item-subtitle
          v-if="entry.remediationHint"
          class="mt-1 text-info"
        >
          <v-icon icon="mdi-information-outline" size="x-small" class="mr-1" />

          {{ entry.remediationHint }}
        </v-list-item-subtitle>

        <template #append>
          <span class="text-caption text-medium-emphasis">
            {{ formatTimestamp(entry.timestamp) }}
          </span>
        </template>
      </v-list-item>

      <v-list-item v-if="!rejectedCount">
        <template #prepend>
          <v-avatar>
            <v-icon color="success" icon="mdi-check-circle" />
          </v-avatar>
        </template>

        <v-list-item-title>
          {{ $t('diagnostics.noRejected') }}
        </v-list-item-title>
      </v-list-item>
    </v-list>

    <v-card-actions v-if="rejectedCount">
      <v-spacer />

      <v-btn variant="text" @click="diagnosticsStore.clear">
        {{ $t('diagnostics_panel.clear') }}
      </v-btn>
    </v-card-actions>
  </v-card>
</template>

<script setup lang="ts">
import type { ShellRejectionReasonCodeType } from '@/features/plugin-runtime';
import { storeToRefs } from 'pinia';
import { useDiagnosticsStore } from '@/entities';

const diagnosticsStore = useDiagnosticsStore();
const { rejectedEntries, rejectedCount, isDegraded, degradedMode } =
  storeToRefs(diagnosticsStore);

function reasonColor(
  reasonCode: ShellRejectionReasonCodeType | string,
): string {
  switch (reasonCode) {
    case 'SHELL_VERSION_MISMATCH':
      return 'orange';

    case 'PERMISSION_DENIED':
      return 'red';

    case 'PLUGIN_LOAD_FAILED':
      return 'red-darken-1';

    case 'EXTENSION_DUPLICATE_ID':
    case 'EXTENSION_DUPLICATE_ROUTE':
    case 'EXTENSION_DUPLICATE_SLOT':
    case 'EXTENSION_DUPLICATE_ACTION':
      return 'purple';

    default:
      return 'grey';
  }
}

function formatTimestamp(date: Date): string {
  return date.toLocaleTimeString();
}
</script>
