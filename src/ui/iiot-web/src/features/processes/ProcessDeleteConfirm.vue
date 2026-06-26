<template>
  <UiModal
    :show="dialog.show"
    preset="card"
    :title="dialog.title"
    style="width: 420px;"
    :mask-closable="false"
    @update:show="dialog.show = $event"
  >
    <p class="confirm-desc">{{ dialog.desc }}</p>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="dialog.show = false">取消</UiButton>
        <UiButton type="error" :loading="submitting" @click="dialog.onConfirm()">
          {{ dialog.confirmText }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { ProcessConfirmDialog } from './types';

defineProps<{
  dialog: ProcessConfirmDialog;
  submitting: boolean;
}>();
</script>
