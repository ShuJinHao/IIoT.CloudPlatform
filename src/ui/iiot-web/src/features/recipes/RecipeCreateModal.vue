<template>
  <UiModal v-model:show="show" preset="card" title="新建生产配方" style="width: 720px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-grid form-grid--2">
        <div class="form-field">
          <label class="form-label">配方名称 <span class="required">*</span></label>
          <UiInput v-model:value="form.recipeName" placeholder="如：A型号冬季配方" />
        </div>
        <div class="form-field">
          <label class="form-label">归属工序 <span class="required">*</span></label>
          <UiSelect v-model:value="form.processId" :options="processOptions" placeholder="请选择工序" />
        </div>
      </div>
      <div class="form-field">
        <label class="form-label">归属设备 <span class="required">*</span></label>
        <UiSelect v-model:value="form.deviceId" :options="deviceOptions" placeholder="请选择设备" />
      </div>
      <RecipeParamsEditor v-model:params="params" />
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" :disabled="params.length === 0" @click="$emit('submit')">创建配方</UiButton>
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
import RecipeParamsEditor from './RecipeParamsEditor.vue';
import type { RecipeCreateForm, RecipeParameter } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
const params = defineModel<RecipeParameter[]>('params', { required: true });
defineProps<{
  form: RecipeCreateForm;
  processOptions: UiSelectOption[];
  deviceOptions: UiSelectOption[];
  submitting: boolean;
}>();
</script>
