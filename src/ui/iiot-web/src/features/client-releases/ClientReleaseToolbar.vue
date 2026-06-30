<template>
  <NiondToolbar>
    <div class="release-toolbar">
      <div v-if="!isPublishRoute" class="release-tabs">
        <button v-if="canGenerateInstaller" class="release-tab" :class="{ 'is-active': activeView === 'binding' }" type="button" @click="$emit('update:activeView', 'binding')">
          <Boxes :size="16" />
          首装生成
        </button>
        <button class="release-tab" :class="{ 'is-active': activeView === 'catalog' }" type="button" @click="$emit('update:activeView', 'catalog')">
          <CloudDownload :size="16" />
          版本 catalog
        </button>
        <button class="release-tab" :class="{ 'is-active': activeView === 'inventory' }" type="button" @click="$emit('update:activeView', 'inventory')">
          <MonitorCheck :size="16" />
          设备客户端状态
        </button>
      </div>
      <div v-else class="release-mode-label">
        <Settings2 :size="16" />
        客户端发布管理
      </div>
      <div class="release-filters">
        <UiButton size="small" secondary @click="$emit('refresh')">
          <RefreshCw :size="15" />
          刷新
        </UiButton>
      </div>
    </div>
  </NiondToolbar>
</template>

<script setup lang="ts">
import { Boxes, CloudDownload, MonitorCheck, RefreshCw, Settings2 } from 'lucide-vue-next';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { ViewMode } from './types';

defineEmits<{
  'update:activeView': [value: ViewMode];
  refresh: [];
}>();

defineProps<{
  isPublishRoute: boolean;
  canGenerateInstaller: boolean;
  activeView: ViewMode;
}>();
</script>
