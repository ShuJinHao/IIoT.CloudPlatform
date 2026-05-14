<template>
  <section class="niond-table-card">
    <slot />
  </section>
</template>

<style scoped>
.niond-table-card {
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--border);
  border-radius: 24px;
  background: var(--bg-1);
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 12px 32px rgba(15, 23, 42, 0.05);
}

.niond-table-card :deep(.n-data-table-th) {
  background: #f4f7f8;
  color: var(--muted-foreground);
  font-size: 12px;
  font-weight: 800;
}

.niond-table-card :deep(.n-data-table-td) {
  font-size: 13px;
  font-weight: 600;
}

.niond-table-card :deep(.pagination-wrap),
.niond-table-card :deep(.capacity-page__pagination) {
  border-top: 1px solid var(--border);
  background: var(--bg-1);
}

.niond-table-card :deep(.hint-empty) {
  padding: 40px 24px;
}
</style>
