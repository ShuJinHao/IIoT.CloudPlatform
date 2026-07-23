<template>
  <NiondTableCard>
    <div class="table-heading">
      <div>
        <h2>删除恢复</h2>
        <p>待清理、清理失败或审计待确认的永久删除操作；可在此重试</p>
      </div>
    </div>
    <div v-if="loading" class="deletions-loading">加载中…</div>
    <EmptyState v-else-if="items.length === 0" title="无待恢复删除操作" description="所有永久删除操作均已完成。" />
    <ul v-else class="deletion-list">
      <li v-for="item in items" :key="item.deletionId" class="deletion-item">
        <div class="deletion-item__main">
          <div class="deletion-item__title">
            <strong>{{ item.componentKey }}</strong>
            <UiTag :type="item.componentKind === 'Host' ? 'default' : 'info'" size="small" :bordered="false">
              {{ item.componentKind === 'Host' ? '宿主' : '工序插件' }}
            </UiTag>
            <UiTag :type="deletionStatusTone(item.status)" size="small" :bordered="false">
              {{ deletionStatusText(item.status) }}
            </UiTag>
          </div>
          <div class="deletion-item__meta">
            <span>操作 ID：<code class="deletion-id">{{ item.deletionId }}</code></span>
            <span>频道：{{ item.channel }} / {{ item.targetRuntime }}</span>
            <span>重试 {{ item.retryCount }} 次</span>
          </div>
          <div v-if="item.failureCode" class="deletion-item__failure">
            失败码：<code>{{ item.failureCode }}</code>
            <template v-if="item.reason">；原因：{{ item.reason }}</template>
          </div>
        </div>
        <UiButton
          size="small"
          type="primary"
          :loading="retryingId === item.deletionId"
          @click="$emit('retry', item)"
        >
          重试
        </UiButton>
      </li>
    </ul>
  </NiondTableCard>
</template>

<script setup lang="ts">
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { ClientReleaseComponentDeletionDto } from './api';
import type { TagTone } from './types';

defineProps<{
  items: ClientReleaseComponentDeletionDto[];
  loading: boolean;
  retryingId: string | null;
}>();

defineEmits<{
  retry: [item: ClientReleaseComponentDeletionDto];
}>();

function deletionStatusText(status: string): string {
  return {
    Requested: '待清理',
    Failed: '清理失败',
    CleanupCompleted: '审计待确认',
  }[status] || status;
}

function deletionStatusTone(status: string): TagTone {
  if (status === 'Failed') return 'error';
  if (status === 'CleanupCompleted') return 'warning';
  return 'info';
}
</script>
