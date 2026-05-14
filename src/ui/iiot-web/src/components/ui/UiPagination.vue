<template>
  <nav class="ui-pagination" aria-label="分页">
    <button type="button" :disabled="page <= 1" @click="go(page - 1)">上一页</button>
    <button
      v-for="item in visiblePages"
      :key="item"
      type="button"
      :class="{ active: item === page }"
      @click="go(item)"
    >
      {{ item }}
    </button>
    <button type="button" :disabled="page >= pageCount" @click="go(page + 1)">下一页</button>
  </nav>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = withDefaults(defineProps<{
  page: number;
  pageCount: number;
}>(), {
  page: 1,
  pageCount: 1,
});

const emit = defineEmits<{
  'update:page': [value: number];
}>();

const visiblePages = computed(() => {
  const total = Math.max(1, props.pageCount);
  const start = Math.max(1, Math.min(props.page - 2, total - 4));
  const end = Math.min(total, start + 4);
  return Array.from({ length: end - start + 1 }, (_, index) => start + index);
});

function go(value: number) {
  const next = Math.min(Math.max(1, value), props.pageCount);
  if (next !== props.page) emit('update:page', next);
}
</script>

<style scoped>
.ui-pagination {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.ui-pagination button {
  min-width: 36px;
  height: 36px;
  border: 0;
  border-radius: 12px;
  background: #f4f7f8;
  color: #596273;
  font-size: 13px;
  font-weight: 800;
  transition: all 0.16s ease;
}

.ui-pagination button:hover:not(:disabled),
.ui-pagination button.active {
  background: #111827;
  color: #fff;
}

.ui-pagination button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
</style>
