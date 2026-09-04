<template>
  <v-card>
    <v-card-title>{{ manifest.displayName ?? manifest.id }}</v-card-title>
    <v-card-subtitle>v{{ manifest.version }}</v-card-subtitle>
    <v-card-text>
      <div :id="`plugin-${manifest.id}`" />
    </v-card-text>
  </v-card>
</template>

<script setup lang="ts">
import type { IAdminUIPluginManifest } from '@prosto/platform-admin-contracts';

defineProps<{ manifest: IAdminUIPluginManifest }>();
</script>
