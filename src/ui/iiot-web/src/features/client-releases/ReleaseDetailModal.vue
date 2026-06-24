<template>
  <UiModal v-model:show="show" preset="card" title="更新内容详情" style="width: 720px;">
    <div v-if="detail" class="release-detail-modal">
      <div class="release-detail-summary">
        <div class="release-detail-heading">
          <strong>{{ detail.componentName }}</strong>
          <code>{{ detail.componentCode }}</code>
        </div>
        <UiTag :type="detail.kind === 'host' ? 'default' : 'info'" size="small" :bordered="false">{{ detail.kindLabel }}</UiTag>
      </div>
      <div class="release-detail-meta">
        <div><span>版本</span><strong>{{ detail.version }}</strong></div>
        <div>
          <span>状态</span>
          <UiTag :type="detail.statusTone" size="small" :bordered="false">{{ detail.statusText }}</UiTag>
        </div>
        <div><span>发布时间</span><strong>{{ detail.publishedAt }}</strong></div>
        <div><span>大小</span><strong>{{ detail.packageSize }}</strong></div>
      </div>
      <section class="release-detail-notes">
        <h3>完整更新内容</h3>
        <p>{{ detail.releaseNotes }}</p>
      </section>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">关闭</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { ReleaseDetail } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{ detail: ReleaseDetail | null }>();
</script>
