<template>
  <UiModal v-model:show="show" preset="card" :title="dialog.title" style="width: 460px;" :mask-closable="false">
    <p class="confirm-desc">{{ dialog.desc }}</p>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton :type="dialog.tone" :loading="submitting" @click="dialog.onConfirm()">
          {{ dialog.confirmText }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { EdgeHostConfirmDialog } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  dialog: EdgeHostConfirmDialog;
  submitting: boolean;
}>();
</script>
