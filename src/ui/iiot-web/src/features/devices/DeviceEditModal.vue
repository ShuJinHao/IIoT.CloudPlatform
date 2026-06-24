<template>
  <UiModal v-model:show="show" preset="card" title="编辑设备名称" style="width: 480px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">设备名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.deviceName" />
      </div>
      <div class="hint-card hint-card--subtle">
        <div class="hint-card__title">设备 Code 不可修改</div>
        <div class="hint-card__desc">如误创建设备，仅可在无依赖数据时删除后重新创建。</div>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">保存修改</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import type { DeviceEditForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: DeviceEditForm;
  submitting: boolean;
}>();
</script>
