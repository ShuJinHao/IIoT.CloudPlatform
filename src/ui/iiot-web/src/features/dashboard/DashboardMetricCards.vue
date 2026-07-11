<template>
  <div class="grid grid-cols-3 gap-6 max-[1060px]:grid-cols-2 max-[820px]:grid-cols-1">
    <article
      v-for="card in cards"
      :key="card.label"
      class="min-h-[132px] rounded-[var(--radius-lg)] p-5 text-[var(--text-0)]"
      :data-testid="`dashboard-card-${card.id}`"
      :style="{ background: card.background }"
    >
      <component :is="card.icon" class="mb-8" :size="17" :stroke-width="2.4" />
      <div :data-testid="`dashboard-card-${card.id}-value`" class="text-[27px] font-[var(--fw-strong)] leading-none tracking-[0] tabular-nums">{{ card.value }}</div>
      <div class="mt-2 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-1)]">{{ card.label }}</div>
      <div class="mt-1 text-[var(--fs-xs)] font-[var(--fw-semibold)] text-[var(--text-2)]">{{ card.helper }}</div>
    </article>
  </div>
</template>

<script setup lang="ts">
import type { DashboardCard } from './types';

defineProps<{
  cards: DashboardCard[];
}>();
</script>
