<template>
  <section class="rounded-[var(--radius-xl)] bg-white p-6 shadow-[var(--shadow-sm)]">
    <div class="mb-5 flex items-center justify-between">
      <h3 class="text-[var(--fs-2xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.recentAlerts') }}</h3>
      <span class="rounded-[var(--radius-sm)] bg-[var(--bg-2)] px-3 py-2 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-1)]">{{ t('common.latest') }}</span>
    </div>
    <LoadingState
      v-if="sourceState.status === 'loading'"
      data-testid="dashboard-alerts-loading"
      variant="card"
      :rows="5"
    />
    <EmptyState
      v-else-if="sourceState.status === 'error'"
      data-testid="dashboard-alerts-error"
      :title="t('dashboard.alertsErrorTitle')"
      :description="sourceState.error"
    >
      <template #action>
        <UiButton type="primary" @click="emit('retry')">{{ t('dashboard.retry') }}</UiButton>
      </template>
    </EmptyState>
    <div v-else class="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--border)]">
      <div class="grid grid-cols-[120px_minmax(0,1fr)_140px_92px] bg-[var(--bg-2)] px-4 py-3 text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--muted-foreground)]">
        <span>{{ t('dashboard.time') }}</span>
        <span>{{ t('dashboard.message') }}</span>
        <span>{{ t('dashboard.device') }}</span>
        <span>{{ t('dashboard.status') }}</span>
      </div>
      <div v-if="events.length" class="divide-y divide-[var(--border)]">
        <div
          v-for="event in events.slice(0, 5)"
          :key="`${event.time}-${event.message}`"
          class="grid grid-cols-[120px_minmax(0,1fr)_140px_92px] items-center px-4 py-4 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--text-0)]"
        >
          <span class="font-mono text-[var(--fs-sm)] text-[var(--muted-foreground)]">{{ event.time }}</span>
          <span class="truncate">{{ event.message }}</span>
          <span class="truncate text-[var(--muted-foreground)]">{{ event.deviceCode }}</span>
          <span class="w-fit rounded-full px-3 py-1 text-[var(--fs-xs)] font-[var(--fw-strong)]" :class="event.severity === 'error' ? 'bg-[rgba(239,68,68,0.12)] text-[var(--error)]' : 'bg-[rgba(224,138,0,0.14)] text-[var(--warn)]'">
            {{ event.label }}
          </span>
        </div>
      </div>
      <div v-else class="px-4 py-10 text-center text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">
        {{ t('dashboard.noAlerts') }}
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { DashboardEvent, DashboardSourceState } from './types';

defineProps<{
  events: DashboardEvent[];
  sourceState: DashboardSourceState;
}>();
const emit = defineEmits<{ retry: [] }>();

const { t } = useI18n();
</script>
