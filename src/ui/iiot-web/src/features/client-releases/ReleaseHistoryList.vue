<template>
  <div class="history-component" v-for="component in items" :key="component.componentId">
    <div class="history-component__head">
      <div>
        <strong>{{ component.displayName || component.moduleId }}</strong>
        <code>{{ component.moduleId }}</code>
      </div>
      <div class="history-component__meta">
        <UiTag :type="component.componentKind === 'Host' ? 'default' : 'info'" size="small" :bordered="false">
          {{ component.componentKind === 'Host' ? '宿主' : '工序插件' }}
        </UiTag>
        <span class="history-component__channel">{{ component.channel }} / {{ component.targetRuntime }}</span>
      </div>
    </div>
    <ul class="history-version-list">
      <li v-for="version in component.versions" :key="version.id" class="history-version">
        <span class="history-version__version">{{ version.version }}</span>
        <UiTag :type="statusTone(version.status)" size="small" :bordered="false">{{ statusText(version.status) }}</UiTag>
        <span class="history-version__time">删除于 {{ formatDate(version.deletedAtUtc) }}</span>
        <span v-if="version.deletionReason" class="history-version__reason" :title="version.deletionReason">
          原因：{{ version.deletionReason }}
        </span>
        <span v-if="version.deletionFailure" class="history-version__failure" :title="version.deletionFailure">
          失败：{{ version.deletionFailure }}
        </span>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import UiTag from '../../components/ui/UiTag.vue';
import type { ClientReleaseHistoryComponentDto } from './api';
import { formatDate, statusText, statusTone } from './types';

defineProps<{
  items: ClientReleaseHistoryComponentDto[];
}>();
</script>
