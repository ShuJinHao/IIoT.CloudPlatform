<template>
  <UiDrawer v-model:show="show" :width="380" placement="right">
    <UiDrawerContent title="设备详情" closable>
      <div v-if="device" class="detail-stack">
        <div class="detail-status-banner is-active">
          <span class="detail-status-banner__dot"></span>
          设备已启用
        </div>
        <div class="detail-row">
          <span class="detail-row__label">设备名称</span>
          <span class="detail-row__value">{{ device.deviceName }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">设备 Code</span>
          <span class="detail-row__value detail-row__value--mono detail-row__value--brand">{{ device.code }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">设备 ID</span>
          <span class="detail-row__value detail-row__value--mono detail-row__value--small">{{ device.id }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">所属工序</span>
          <span class="detail-row__value">{{ processNameMap[device.processId] || device.processId }}</span>
        </div>
      </div>
    </UiDrawerContent>
  </UiDrawer>
</template>

<script setup lang="ts">
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import type { DeviceListItemDto } from './api';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  device: DeviceListItemDto | null;
  processNameMap: Record<string, string>;
}>();
</script>
