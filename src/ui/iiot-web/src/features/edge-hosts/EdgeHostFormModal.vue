<template>
  <UiModal v-model:show="show" preset="card" :title="title" style="width: 560px;" :mask-closable="false">
    <div class="form-stack">
      <div class="form-field">
        <label class="form-label">绑定设备 <span v-if="mode === 'create'" class="required">*</span></label>
        <UiSelect
          v-model:value="form.deviceId"
          :options="deviceOptions"
          :disabled="mode === 'edit'"
          placeholder="请选择云端设备"
        />
      </div>
      <div class="form-field">
        <label class="form-label">ClientCode</label>
        <UiInput :value="clientCodePreview || '选择设备后自动带出'" disabled />
      </div>
      <div class="form-field">
        <label class="form-label">上位机名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.hostName" placeholder="如：模切上位机 A" />
      </div>
      <div class="form-field">
        <label class="form-label">备注</label>
        <UiInput v-model:value="form.remark" type="textarea" placeholder="可填写产线、机台位置或维护说明" />
      </div>
      <div class="hint-card">
        <div class="hint-card__title">身份链不可手工改写</div>
        <div class="hint-card__desc">上位机必须绑定已有设备，ClientCode 来自设备档案；编辑时只能维护名称和备注。</div>
      </div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">
          {{ mode === 'create' ? '确认创建' : '保存修改' }}
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { EdgeHostFormData, EdgeHostFormMode } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
const props = defineProps<{
  mode: EdgeHostFormMode;
  form: EdgeHostFormData;
  deviceOptions: UiSelectOption[];
  clientCodePreview: string;
  submitting: boolean;
}>();

const title = computed(() => (props.mode === 'create' ? '新增上位机配置' : '编辑上位机配置'));
</script>
