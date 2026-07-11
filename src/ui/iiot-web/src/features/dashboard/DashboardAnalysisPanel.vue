<template>
  <section class="rounded-[var(--radius-xl)] bg-white p-6 shadow-[var(--shadow-sm)]">
    <div class="mb-7 flex items-center justify-between">
      <div>
        <h3 class="text-[var(--fs-2xl)] font-[var(--fw-strong)] text-[var(--text-0)]">{{ t('dashboard.analysis') }}</h3>
        <p class="mt-1 text-[var(--fs-base)] font-[var(--fw-semibold)] text-[var(--muted-foreground)]">{{ t('dashboard.analysisSub') }}</p>
      </div>
    </div>
    <div class="space-y-3">
      <router-link
        v-for="item in links"
        :key="item.label"
        :to="item.to"
        class="flex h-14 items-center justify-between rounded-[var(--radius-md)] bg-[var(--bg-2)] px-4 text-[var(--fs-base)] font-[var(--fw-strong)] text-[var(--text-0)] transition-colors hover:bg-[var(--bg-3)]"
      >
        <span class="flex items-center gap-3">
          <component :is="item.icon" :size="16" />
          {{ item.label }}
        </span>
        <ChevronRight :size="16" />
      </router-link>
    </div>
  </section>
</template>

<script setup lang="ts">
import { ChevronRight } from 'lucide-vue-next';
import { useI18n } from 'vue-i18n';
import type { AnalysisLink } from './types';

defineProps<{
  links: AnalysisLink[];
}>();

const { t } = useI18n();
</script>
