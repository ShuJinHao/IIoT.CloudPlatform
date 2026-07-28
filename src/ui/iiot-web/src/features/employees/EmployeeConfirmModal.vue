<template>
  <UiModal
    :show="show"
    preset="card"
    :title="dialog.title"
    style="width: 480px;"
    :mask-closable="false"
    @update:show="onShowUpdate"
  >
    <p class="confirm-desc">{{ dialog.desc }}</p>
    <template #footer>
      <div class="modal-actions">
        <UiButton :disabled="submitting" @click="onShowUpdate(false)">取消</UiButton>
        <UiButton
          :type="dialog.confirmType"
          :loading="submitting"
          @click="dialog.onConfirm()"
        >
          {{ dialog.confirmText }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { EmployeeConfirmDialogState } from './types';

const show = defineModel<boolean>('show', { required: true });
const props = defineProps<{
  dialog: EmployeeConfirmDialogState;
  submitting: boolean;
}>();

function onShowUpdate(value: boolean) {
  if (!value && props.submitting) return;
  show.value = value;
}
</script>
