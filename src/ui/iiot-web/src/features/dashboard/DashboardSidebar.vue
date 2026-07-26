<template>
  <aside class="space-y-6">
    <section
      class="rounded-[22px] bg-[var(--accent)] p-6 text-white shadow-[var(--shadow-sm)]"
      :data-source-status="capacityState.status"
    >
      <div class="mb-5 text-[17px] font-[var(--fw-strong)]">{{ t('dashboard.productionCenter') }}</div>
      <div data-testid="dashboard-production-display" class="mb-1 text-[30px] font-[var(--fw-strong)] tabular-nums">{{ productionDisplay }}</div>
      <div
        class="mb-7 line-clamp-3 text-[var(--fs-sm)] font-[var(--fw-semibold)]"
        :class="capacityState.status === 'error' ? 'text-[#7f1d1d]' : 'text-white/80'"
        :title="productionHelper"
      >{{ productionHelper }}</div>
      <router-link class="inline-flex h-10 items-center rounded-[var(--radius-sm)] bg-[var(--primary)] px-4 text-[var(--fs-sm)] font-[var(--fw-strong)] text-[var(--text-0)]" to="/capacity">
        {{ t('dashboard.viewCapacity') }}
      </router-link>
    </section>

    <section class="rounded-[22px] bg-white p-5 shadow-[var(--shadow-sm)]">
      <h3 class="text-[var(--fs-xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.deviceStatus') }}</h3>
      <p class="mt-2 text-[var(--fs-xs)] font-[var(--fw-semibold)] leading-5 text-[var(--muted-foreground)]">{{ t('dashboard.deviceStatusDefinition') }}</p>
      <LoadingState
        v-if="statusState.status === 'loading'"
        data-testid="dashboard-status-loading"
        class="mt-4"
        :rows="4"
      />
      <div
        v-else-if="statusState.status === 'error'"
        data-testid="dashboard-status-error"
        class="mt-4 rounded-[var(--radius-md)] bg-[rgba(239,68,68,0.08)] p-4"
      >
        <AlertTriangle class="mb-3 text-[var(--error)]" :size="22" />
        <p class="text-[var(--fs-sm)] font-[var(--fw-semibold)] leading-5 text-[var(--error)]">{{ statusState.error }}</p>
        <UiButton class="mt-4" size="small" type="error" secondary @click="emit('retry')">{{ t('dashboard.retry') }}</UiButton>
      </div>
      <div v-else class="mt-4 space-y-3">
        <p class="text-[var(--fs-sm)] font-[var(--fw-bold)] text-[var(--text-1)]">{{ statusSummary }}</p>
        <div v-for="row in statusRows" :key="row.label" class="flex items-center justify-between rounded-[var(--radius-md)] bg-[var(--bg-2)] px-3 py-3">
          <span class="flex items-center gap-3 text-[var(--fs-base)] font-[var(--fw-bold)] text-[var(--text-0)]">
            <i class="size-2.5 rounded-full" :style="{ background: row.color }"></i>
            {{ row.label }}
          </span>
          <span class="font-mono text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ row.value }}</span>
        </div>
      </div>
    </section>

  </aside>
</template>

<script setup lang="ts">
import { AlertTriangle } from 'lucide-vue-next';
import { useI18n } from 'vue-i18n';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { DashboardSourceState, DashboardStatusRow } from './types';

defineProps<{
  productionDisplay: string;
  productionHelper: string;
  capacityState: DashboardSourceState;
  statusRows: DashboardStatusRow[];
  statusState: DashboardSourceState;
  statusSummary: string;
}>();
const emit = defineEmits<{ retry: [] }>();

const { t } = useI18n();
</script>
