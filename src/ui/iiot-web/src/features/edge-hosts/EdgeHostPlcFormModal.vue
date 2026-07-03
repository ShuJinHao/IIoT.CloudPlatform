<template>
  <UiModal v-model:show="show" preset="card" :title="title" style="width: 720px;" :mask-closable="false">
    <div class="form-grid">
      <div class="form-field">
        <label class="form-label">PLC 编码 <span v-if="mode === 'create'" class="required">*</span></label>
        <UiInput v-model:value="form.plcCode" :disabled="mode === 'edit'" placeholder="如：PLC-CUT-01" />
      </div>
      <div class="form-field">
        <label class="form-label">PLC 名称 <span class="required">*</span></label>
        <UiInput v-model:value="form.plcName" placeholder="如：模切 PLC 01" />
      </div>
      <div class="form-field">
        <label class="form-label">关联工序</label>
        <UiSelect v-model:value="form.processId" :options="processOptions" clearable placeholder="可暂不关联" />
      </div>
      <div class="form-field">
        <label class="form-label">业务设备</label>
        <UiSelect v-model:value="form.businessDeviceId" :options="deviceOptions" clearable placeholder="可暂不关联" />
      </div>
      <div class="form-field">
        <label class="form-label">工位</label>
        <UiInput v-model:value="form.stationCode" placeholder="如：S01" />
      </div>
      <div class="form-field">
        <label class="form-label">协议</label>
        <UiInput v-model:value="form.protocol" placeholder="如：ModbusTcp" />
      </div>
      <div class="form-field form-field--wide">
        <label class="form-label">地址</label>
        <UiInput v-model:value="form.address" placeholder="如：192.168.1.10:502" />
      </div>
      <div class="form-field">
        <label class="form-label">排序</label>
        <UiNumberInput v-model:value="form.displayOrder" step="1" />
      </div>
      <div v-if="mode === 'create'" class="form-field form-field--switch">
        <label class="form-label">创建后启用</label>
        <UiSwitch v-model:value="form.enabled" />
      </div>
      <div class="form-field form-field--wide">
        <label class="form-label">备注</label>
        <UiInput v-model:value="form.remark" type="textarea" placeholder="可填写 PLC 用途、柜位或维护说明" />
      </div>
    </div>
    <div class="hint-card hint-card--subtle">
      <div class="hint-card__title">关联关系可以后续补齐</div>
      <div class="hint-card__desc">工序和业务设备可为空；两者同时填写时，云端会校验设备是否属于该工序。</div>
    </div>
    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton type="primary" :loading="submitting" @click="$emit('submit')">
          {{ mode === 'create' ? '确认新增' : '保存修改' }}
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
import UiNumberInput from '../../components/ui/UiNumberInput.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import UiSwitch from '../../components/ui/UiSwitch.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { PlcBindingFormData, PlcBindingFormMode } from './types';

defineEmits<{ submit: [] }>();
const show = defineModel<boolean>('show', { required: true });
const props = defineProps<{
  mode: PlcBindingFormMode;
  form: PlcBindingFormData;
  processOptions: UiSelectOption[];
  deviceOptions: UiSelectOption[];
  submitting: boolean;
}>();

const title = computed(() => (props.mode === 'create' ? '新增 PLC 绑定' : '编辑 PLC 绑定'));
</script>
