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
        <div><span>Host API</span><strong>{{ detail.hostApiVersion }}</strong></div>
        <div><span>目标框架</span><strong>{{ detail.targetFramework }}</strong></div>
        <div><span>兼容窗口</span><strong>{{ detail.compatibilityWindow }}</strong></div>
        <div><span>发布人</span><strong>{{ detail.publisher }}</strong></div>
      </div>
      <section class="release-detail-facts">
        <div><span>SHA-256</span><code>{{ detail.sha256 }}</code></div>
        <div><span>签名</span><code>{{ detail.signature }}</code></div>
        <div><span>下载地址</span><code>{{ detail.downloadUrl }}</code></div>
      </section>
      <section class="release-detail-notes">
        <h3>完整更新内容</h3>
        <p>{{ detail.releaseNotes }}</p>
      </section>
      <section class="release-detail-notes">
        <h3>依赖</h3>
        <pre>{{ detail.dependencies }}</pre>
      </section>
      <section v-if="detail.artifacts.length > 0" class="release-detail-artifacts">
        <h3>Artifact 清单</h3>
        <ul>
          <li v-for="artifact in detail.artifacts" :key="`${artifact.artifactKind}:${artifact.relativePath}`">
            <div>
              <code>{{ artifact.relativePath }}</code>
              <UiTag :type="artifact.filesPresent ? 'success' : 'error'" size="small" :bordered="false">
                {{ artifact.filesPresent ? '文件存在' : '文件缺失' }}
              </UiTag>
            </div>
            <small>{{ artifact.artifactKind }} · {{ artifact.size }} · {{ artifact.sha256 }}</small>
          </li>
        </ul>
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
