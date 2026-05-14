<template>
  <section class="niond-data-page">
    <header class="niond-data-page__header">
      <div class="niond-data-page__title-block">
        <p class="niond-data-page__eyebrow">{{ displayEyebrow }}</p>
        <h1 class="niond-data-page__title">{{ displayTitle }}</h1>
        <p v-if="displaySubtitle" class="niond-data-page__subtitle">{{ displaySubtitle }}</p>
      </div>
      <div v-if="$slots.actions" class="niond-data-page__actions">
        <slot name="actions" />
      </div>
    </header>

    <slot name="toolbar" />
    <slot />
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

const { t, te } = useI18n();

const props = withDefaults(
  defineProps<{
    title: string;
    subtitle?: string;
    eyebrow?: string;
    pageKey?: string;
  }>(),
  {
    subtitle: '',
    eyebrow: '',
    pageKey: '',
  },
);

const displayEyebrow = computed(() => props.eyebrow || t('pages.workspace'));

const displayTitle = computed(() => {
  const key = props.pageKey ? `pages.${props.pageKey}.title` : '';
  return key && te(key) ? t(key) : props.title;
});

const displaySubtitle = computed(() => {
  const key = props.pageKey ? `pages.${props.pageKey}.subtitle` : '';
  return key && te(key) ? t(key) : props.subtitle;
});
</script>

<style scoped>
.niond-data-page {
  display: grid;
  gap: 24px;
}

.niond-data-page__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
}

.niond-data-page__title-block {
  min-width: 0;
  flex: 1 1 auto;
}

.niond-data-page__eyebrow {
  margin: 0 0 6px;
  color: var(--muted-foreground);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

.niond-data-page__title {
  margin: 0;
  color: var(--text-0);
  font-size: 32px;
  font-weight: 900;
  line-height: 1.16;
  letter-spacing: 0;
}

.niond-data-page__subtitle {
  max-width: 760px;
  margin: 8px 0 0;
  color: var(--text-1);
  font-size: 14px;
  font-weight: 600;
  line-height: 1.55;
}

.niond-data-page__actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}
</style>
