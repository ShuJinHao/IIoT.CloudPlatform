<template>
  <nav class="ui-pagination" aria-label="分页">
    <span v-if="itemCount !== undefined" class="ui-pagination__summary">
      共 {{ itemCount }} 条
    </span>
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
    <label v-if="showQuickJumper" class="ui-pagination__jumper">
      跳至
      <input
        v-model="jumpPage"
        type="number"
        min="1"
        :max="pageCount"
        @keyup.enter="goJumpPage"
      />
      页
    </label>
  </nav>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';

const props = withDefaults(defineProps<{
  page: number;
  pageCount: number;
  itemCount?: number;
  pageSize?: number;
  showQuickJumper?: boolean;
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

const jumpPage = ref(String(props.page));

watch(() => props.page, (value) => {
  jumpPage.value = String(value);
});

function go(value: number) {
  const next = Math.min(Math.max(1, value), props.pageCount);
  if (next !== props.page) emit('update:page', next);
}

function goJumpPage() {
  const parsed = Number(jumpPage.value);
  if (!Number.isFinite(parsed)) {
    jumpPage.value = String(props.page);
    return;
  }
  go(Math.trunc(parsed));
}
</script>

<style scoped>
.ui-pagination {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.ui-pagination__summary,
.ui-pagination__jumper {
  font-size: 13px;
  font-weight: 700;
  color: #596273;
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

.ui-pagination__jumper {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.ui-pagination__jumper input {
  width: 58px;
  height: 32px;
  border: 1px solid var(--input);
  border-radius: 10px;
  background: #f4f7f8;
  color: #111827;
  text-align: center;
  font-size: 13px;
  font-weight: 800;
  outline: none;
}

.ui-pagination__jumper input:focus {
  border-color: #111827;
  background: #fff;
  box-shadow: 0 0 0 2px rgba(17, 24, 39, 0.08);
}
</style>
