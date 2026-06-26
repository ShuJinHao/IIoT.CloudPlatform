<template>
  <UiModal v-model:show="show" preset="card" title="配置设备管辖权" style="width: 580px;" :mask-closable="false">
    <div class="form-stack">
      <LoadingState v-if="loading" :rows="4" />
      <div v-else>
        <div class="access-header">
          <span class="access-header__title">可访问设备</span>
          <span class="access-header__hint">当前已分配 {{ form.DeviceIds.length }} 台</span>
        </div>
        <div v-if="devices.length > 0" class="access-list">
          <UiCheckbox
            v-for="device in devices"
            :key="device.id"
            :checked="form.DeviceIds.includes(device.id)"
            @update:checked="(checked: boolean) => $emit('toggle-device', device.id, checked)"
          >
            {{ device.deviceName }}
          </UiCheckbox>
        </div>
        <EmptyState v-else title="暂无设备数据" />
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">关闭</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">保存管辖权</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { DeviceSelectDto } from '../devices/api';
import type { EmployeeAccessForm } from './types';

defineEmits<{
  submit: [];
  'toggle-device': [deviceId: string, checked: boolean];
}>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: EmployeeAccessForm;
  devices: DeviceSelectDto[];
  loading: boolean;
  submitting: boolean;
}>();
</script>
