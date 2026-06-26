<template>
  <UiModal v-model:show="show" preset="card" title="升级配方版本" style="width: 720px;" :mask-closable="false">
    <div class="form-stack">
      <div class="modal-header-sub">{{ target?.recipeName }} · 当前版本 {{ target?.version }}</div>
      <div class="form-grid form-grid--2">
        <div class="form-field">
          <label class="form-label">新版本号 <span class="required">*</span></label>
          <UiInput v-model:value="form.newVersion" placeholder="如：V2.0" class="mono-input" />
          <p class="form-hint">版本号不能与已有版本重复</p>
        </div>
        <div class="form-field">
          <label class="form-label">配方类型</label>
          <div class="readonly-badge">
            <UiTag :type="isDeviceBoundRecipe(target?.deviceId) ? 'warning' : 'info'" :bordered="false" size="small">
              {{ isDeviceBoundRecipe(target?.deviceId) ? '设备配方' : '未绑定设备' }}
            </UiTag>
          </div>
        </div>
      </div>
      <RecipeParamsEditor v-model:params="params" />
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" :disabled="params.length === 0" @click="$emit('submit')">保存并升版</UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiTag from '../../components/ui/UiTag.vue';
import RecipeParamsEditor from './RecipeParamsEditor.vue';
import { isDeviceBoundRecipe, type RecipeListItemDto, type RecipeParameter, type RecipeUpgradeForm } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
const params = defineModel<RecipeParameter[]>('params', { required: true });
defineProps<{
  target: RecipeListItemDto | null;
  form: RecipeUpgradeForm;
  submitting: boolean;
}>();
</script>
