<template>
  <div class="grid grid-cols-4 gap-5 max-[1500px]:grid-cols-2 max-[820px]:grid-cols-1">
    <article
      v-for="card in cards"
      :key="card.id"
      class="min-h-[142px] rounded-[var(--radius-lg)] p-5 text-[#111827]"
      :class="card.status === 'error' ? 'ring-2 ring-[rgba(239,68,68,0.32)]' : ''"
      :data-testid="`dashboard-card-${card.id}`"
      :data-source-status="card.status"
      :aria-busy="card.status === 'loading'"
      :style="{ background: card.background }"
    >
      <div class="mb-8 flex items-center justify-between">
        <component :is="card.icon" :size="18" :stroke-width="2.4" />
        <span
          class="size-2.5 rounded-full"
          :class="card.status === 'ready' ? 'bg-[rgba(17,24,39,0.42)]' : card.status === 'error' ? 'bg-[var(--error)]' : 'animate-pulse bg-[rgba(17,24,39,0.22)]'"
        ></span>
      </div>
      <div :data-testid="`dashboard-card-${card.id}-value`" class="text-[27px] font-[var(--fw-strong)] leading-none tracking-[0] tabular-nums">{{ card.value }}</div>
      <div class="mt-2 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[rgba(17,24,39,0.82)]">{{ card.label }}</div>
      <div
        class="mt-1 line-clamp-2 text-[var(--fs-xs)] font-[var(--fw-semibold)]"
        :class="card.status === 'error' ? 'text-[#7f1d1d]' : 'text-[rgba(17,24,39,0.68)]'"
        :title="card.helper"
      >{{ card.helper }}</div>
    </article>
  </div>
</template>

<script setup lang="ts">
import type { DashboardCard } from './types';

defineProps<{
  cards: DashboardCard[];
}>();
</script>
