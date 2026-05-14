<template>
  <section class="niond-toolbar">
    <slot />
  </section>
</template>

<style scoped>
.niond-toolbar {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  flex-wrap: wrap;
  min-height: 68px;
  padding: 18px 22px;
  border: 1px solid var(--border);
  border-radius: 24px;
  background: var(--bg-1);
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 12px 32px rgba(15, 23, 42, 0.05);
}

.niond-toolbar :deep(.filter-row),
.niond-toolbar :deep(.capacity-page__filter-row),
.niond-toolbar :deep(.detail-page__filter-row),
.niond-toolbar :deep(.device-row),
.niond-toolbar :deep(.context-row) {
  width: 100%;
}

.niond-toolbar :deep(.filter-stack) {
  width: 100%;
}

.niond-toolbar :deep(.device-row__error),
.niond-toolbar :deep(.capacity-page__filter-error) {
  width: 100%;
}
</style>
