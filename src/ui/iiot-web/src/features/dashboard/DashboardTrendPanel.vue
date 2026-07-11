<template>
  <section class="rounded-[var(--radius-xl)] bg-white p-6 shadow-[var(--shadow-sm)]">
    <div class="mb-8 flex items-start justify-between gap-4">
      <div>
        <h3 class="text-[var(--fs-2xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.productionTrend') }}</h3>
        <p class="mt-1 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">{{ t('dashboard.trendSubtitle') }}</p>
      </div>
    </div>
    <EmptyState
      v-if="!trendBars.length"
      data-testid="dashboard-trend-empty"
      :title="t('dashboard.trendEmptyTitle')"
      :description="t('dashboard.trendEmptyDesc')"
    />
    <div v-else class="dashboard-bars" :aria-label="t('dashboard.productionTrend')">
      <div v-for="(bar, index) in trendBars" :key="`${bar.label}-${index}`" class="dashboard-bars__group">
        <span class="dashboard-bars__bar" :style="{ height: bar.height, background: bar.color }" :title="`${bar.label}: ${bar.value}`"></span>
        <small>{{ bar.label }}</small>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import EmptyState from '../../components/states/EmptyState.vue';
import type { DashboardTrendBar } from './types';

defineProps<{
  trendBars: DashboardTrendBar[];
}>();

const { t } = useI18n();
</script>
