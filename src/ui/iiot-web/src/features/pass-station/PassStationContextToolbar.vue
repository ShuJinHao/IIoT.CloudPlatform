<template>
  <NiondToolbar class="passstation-page__context-card">
    <div class="context-row">
      <div class="filter-field">
        <span class="filter-field__label">当前工序</span>
        <UiSelect v-model:value="currentProcessId" :options="processOptions" placeholder="请选择工序" clearable size="small" style="width: 280px;" />
      </div>
      <div v-if="currentProcess" class="context-hint">
        工序编码：<code class="context-hint__code">{{ currentProcess.processCode }}</code>
      </div>
    </div>
  </NiondToolbar>
</template>

<script setup lang="ts">
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { ProcessSelectDto } from '../processes/api';
import type { UiSelectOption } from '../../components/ui/types';

const currentProcessId = defineModel<string | null>('currentProcessId', { required: true });
defineProps<{
  processOptions: UiSelectOption[];
  currentProcess: ProcessSelectDto | null;
}>();
</script>
