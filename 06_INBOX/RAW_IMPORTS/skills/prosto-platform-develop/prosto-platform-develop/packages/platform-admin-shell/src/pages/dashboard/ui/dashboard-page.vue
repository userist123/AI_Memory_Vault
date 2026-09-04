<template>
  <v-container>
    <h1>{{ $t('dashboard.title') }}</h1>

    <v-row>
      <v-col
        v-for="plugin in readyPlugins"
        :key="plugin.manifest.id"
        cols="12"
        md="6"
      >
        <PluginContainer :manifest="plugin.manifest" />
      </v-col>

      <v-col v-if="!readyPlugins.length">
        <v-card>
          <v-card-text>{{ $t('dashboard.noPlugins') }}</v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { usePlugins } from '@/entities/plugin/vue/use-plugins.js';
import PluginContainer from '@/widgets/plugin-container/ui/plugin-container.vue';

const { readyPlugins } = usePlugins();
</script>
