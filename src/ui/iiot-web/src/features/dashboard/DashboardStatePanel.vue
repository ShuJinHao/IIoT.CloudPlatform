<template>
  <section
    class="min-h-[420px] rounded-[var(--radius-xl)] bg-white p-8 shadow-[var(--shadow-sm)]"
    :data-testid="`dashboard-${state}`"
    :aria-live="state === 'loading' ? 'polite' : 'assertive'"
  >
    <div v-if="state === 'loading'" class="mx-auto max-w-3xl py-10">
      <div class="mb-8 text-center">
        <h3 class="text-[var(--fs-2xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.loadingTitle') }}</h3>
        <p class="mt-2 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">{{ t('dashboard.loadingDesc') }}</p>
      </div>
      <LoadingState variant="card" :rows="7" />
    </div>

    <EmptyState
      v-else
      :title="state === 'error' ? t('dashboard.errorTitle') : t('dashboard.emptyTitle')"
      :description="state === 'error' ? t('dashboard.errorDesc') : t('dashboard.emptyDesc')"
    >
      <template #icon>
        <AlertTriangle v-if="state === 'error'" :size="56" :stroke-width="1.6" />
        <Database v-else :size="56" :stroke-width="1.6" />
      </template>
      <template v-if="state === 'error'" #action>
        <UiButton type="primary" @click="emit('retry')">{{ t('dashboard.retry') }}</UiButton>
      </template>
    </EmptyState>
  </section>
</template>

<script setup lang="ts">
import { AlertTriangle, Database } from 'lucide-vue-next';
import { useI18n } from 'vue-i18n';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { DashboardNonReadyState } from './types';

defineProps<{ state: DashboardNonReadyState }>();
const emit = defineEmits<{ retry: [] }>();
const { t } = useI18n();
</script>
