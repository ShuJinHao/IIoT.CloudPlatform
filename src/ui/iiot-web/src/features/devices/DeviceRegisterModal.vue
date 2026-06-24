<template>
  <UiModal v-model:show="show" preset="card" title="新建设备" style="width: 520px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">设备名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.deviceName" placeholder="如：1号注液机" />
      </div>
      <div class="form-field">
        <label class="form-label">所属工序 <span class="required">*</span></label>
        <UiSelect v-model:value="form.processId" :options="processOptions" placeholder="请选择工序" />
      </div>
      <div class="hint-card">
        <div class="hint-card__title">设备 Code 由云端自动生成</div>
        <div class="hint-card__desc">保存后请到「客户端首装生成」为对应工序生成绑定安装包，现场无需手工配置密钥。</div>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">确认创建</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { DeviceRegisterForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
defineProps<{
  form: DeviceRegisterForm;
  processOptions: UiSelectOption[];
  submitting: boolean;
}>();
</script>
