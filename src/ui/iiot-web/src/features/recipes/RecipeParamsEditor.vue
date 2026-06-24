<template>
  <div class="params-editor">
    <div class="params-editor__head">
      <label class="form-label">工艺参数 <span class="required">*</span></label>
      <UiButton size="tiny" type="primary" secondary @click="addParam">+ 添加参数</UiButton>
    </div>
    <div v-if="params.length === 0" class="params-editor__empty">暂无参数，点击"添加参数"开始配置</div>
    <div v-else class="params-editor__table">
      <div class="params-editor__row params-editor__row--head">
        <span>参数名称</span>
        <span>单位</span>
        <span>下限</span>
        <span>上限</span>
        <span></span>
      </div>
      <div v-for="(param, index) in params" :key="param.id" class="params-editor__row">
        <UiInput :value="param.name" placeholder="如：温度" size="small" @update:value="updateParam(index, 'name', $event)" />
        <UiInput :value="param.unit" placeholder="℃" size="small" @update:value="updateParam(index, 'unit', $event)" />
        <UiNumberInput :value="param.min" placeholder="0" size="small" :show-button="false" style="width: 100%;" @update:value="updateParam(index, 'min', $event ?? 0)" />
        <UiNumberInput :value="param.max" placeholder="100" size="small" :show-button="false" style="width: 100%;" @update:value="updateParam(index, 'max', $event ?? 0)" />
        <UiButton size="tiny" quaternary type="error" @click="removeParam(index)">×</UiButton>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiNumberInput from '../../components/ui/UiNumberInput.vue';
import { generateParamId, type RecipeParameter } from './types';

const params = defineModel<RecipeParameter[]>('params', { required: true });

function addParam() {
  params.value = [
    ...params.value,
    { id: generateParamId(), name: '', unit: '', min: 0, max: 0 },
  ];
}

function removeParam(index: number) {
  const next = [...params.value];
  next.splice(index, 1);
  params.value = next;
}

function updateParam(index: number, field: keyof RecipeParameter, value: string | number) {
  const next = [...params.value];
  const current = next[index];
  if (!current) return;

  const updated: RecipeParameter = { ...current };
  if (field === 'name' || field === 'unit' || field === 'id') {
    updated[field] = String(value);
  } else {
    updated[field] = Number(value);
  }
  next[index] = updated;
  params.value = next;
}
</script>
