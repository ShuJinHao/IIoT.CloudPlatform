<template>
  <UiModal v-model:show="show" preset="card" :title="dialog.title" style="width: 440px;" :mask-closable="false">
    <p class="confirm-desc">{{ dialog.desc }}</p>
    <div v-if="dialog.impact" class="delete-impact">
      <div class="delete-impact__summary">
        <div>
          <span>设备</span>
          <strong>{{ dialog.impact.deviceName }}</strong>
        </div>
        <div>
          <span>ClientCode</span>
          <code>{{ dialog.impact.clientCode }}</code>
        </div>
        <div>
          <span>DeviceId</span>
          <code>{{ dialog.impact.deviceId }}</code>
        </div>
      </div>
      <div class="delete-impact__grid">
        <div v-for="item in deletionImpactRows" :key="item.label" class="delete-impact__item">
          <span>{{ item.label }}</span>
          <strong>{{ item.value }}</strong>
        </div>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton :type="dialog.danger ? 'error' : 'warning'" :disabled="confirmDisabled" :loading="submitting" @click="dialog.onConfirm()">
          {{ dialog.confirmText }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { DeviceConfirmDialogState, DeviceDeletionImpactRow } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  dialog: DeviceConfirmDialogState;
  deletionImpactRows: DeviceDeletionImpactRow[];
  confirmDisabled: boolean;
  submitting: boolean;
}>();
</script>
