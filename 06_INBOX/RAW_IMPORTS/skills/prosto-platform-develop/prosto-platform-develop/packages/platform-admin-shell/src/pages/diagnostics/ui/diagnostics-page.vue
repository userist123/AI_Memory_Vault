<template>
  <v-container>
    <h1>{{ $t('diagnostics.title') }}</h1>

    <DiagnosticsPanel />
  </v-container>
</template>

<script setup lang="ts">
import DiagnosticsPanel from '@/widgets/diagnostics-panel/ui/diagnostics-panel.vue';
</script>
