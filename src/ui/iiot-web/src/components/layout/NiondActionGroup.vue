<template>
  <div class="niond-action-group">
    <slot />
  </div>
</template>

<style scoped>
.niond-action-group {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
}

.niond-action-group :deep(.n-button) {
  border-radius: 999px;
  font-weight: 800;
}
</style>
