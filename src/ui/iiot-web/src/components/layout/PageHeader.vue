<template>
  <header class="page-header">
    <div class="page-header__main">
      <p class="page-header__eyebrow">{{ t('pages.workspace') }}</p>
      <h1 class="page-header__title">{{ displayTitle }}</h1>
      <p v-if="displaySubtitle" class="page-header__subtitle">{{ displaySubtitle }}</p>
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { useI18n } from 'vue-i18n';

const props = defineProps<{
  title: string;
  subtitle?: string;
}>();

const route = useRoute();
const { t, te } = useI18n();

const pageKey = computed(() => {
  const routeName = String(route.name || '');
  const map: Record<string, string> = {
    Employees: 'employees',
    Devices: 'devices',
    MasterDataProcesses: 'processes',
    Recipes: 'recipes',
    PassStation: 'passStation',
    Capacity: 'capacity',
    CapacityDetail: 'capacityDetail',
    DeviceLogs: 'logs',
    Roles: 'roles',
  };
  return map[routeName] || '';
});

const displayTitle = computed(() => {
  const key = pageKey.value ? `pages.${pageKey.value}.title` : '';
  return key && te(key) ? t(key) : props.title;
});

const displaySubtitle = computed(() => {
  const key = pageKey.value ? `pages.${pageKey.value}.subtitle` : '';
  return key && te(key) ? t(key) : props.subtitle;
});
</script>

<style scoped>
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-6);
  margin-bottom: var(--space-8);
  flex-wrap: wrap;
}

.page-header__main {
  min-width: 0;
  flex: 1 1 auto;
}

.page-header__eyebrow {
  margin: 0 0 6px;
  color: var(--muted-foreground);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

.page-header__title {
  margin: 0;
  color: var(--text-0);
  font-size: 32px;
  font-weight: 900;
  line-height: 1.16;
  letter-spacing: 0;
  word-break: break-word;
}

.page-header__subtitle {
  max-width: 760px;
  margin: 8px 0 0;
  color: var(--text-1);
  font-size: 14px;
  font-weight: 600;
  line-height: 1.55;
}

.page-header__actions {
  display: flex;
  align-items: center;
  flex: 0 0 auto;
  flex-wrap: wrap;
  gap: var(--space-3);
  margin-top: 2px;
}
</style>
