<template>
  <CardSurface class="passstation-page__filter-card">
    <div class="filter-stack">
      <div class="filter-field">
        <span class="filter-field__label">查询模式</span>
        <UiRadioGroup class="passstation-query-modes" :value="currentMode" size="small" @update:value="$emit('switchMode', $event)">
          <UiRadioButton v-for="mode in activeQueryModes" :key="mode.key" :value="mode.key">{{ mode.label }}</UiRadioButton>
        </UiRadioGroup>
      </div>
      <div class="filter-row">
        <template v-if="currentMode === 'device-barcode'">
          <div class="filter-field filter-field--wide">
            <span class="filter-field__label">弹夹号</span>
            <UiInput v-model:value="filters.barcode" placeholder="请输入弹夹号" size="small" style="width: 280px;" @keyup.enter="$emit('search')" />
          </div>
        </template>
        <template v-if="currentMode === 'device-time'">
          <div class="filter-field">
            <span class="filter-field__label">开始时间</span>
            <UiDatePicker v-model:formatted-value="filters.startTime" value-format="yyyy-MM-dd'T'HH:mm" type="datetime" size="small" style="width: 220px;" />
          </div>
          <div class="filter-field">
            <span class="filter-field__label">结束时间</span>
            <UiDatePicker v-model:formatted-value="filters.endTime" value-format="yyyy-MM-dd'T'HH:mm" type="datetime" size="small" style="width: 220px;" />
          </div>
        </template>
        <template v-if="currentMode === 'device-latest'">
          <div class="latest-hint">读取所选设备最新 200 条过站记录</div>
        </template>
        <UiButton type="primary" size="small" @click="$emit('search')">
          <template #icon><Search :size="14" /></template>
          查询
        </UiButton>
        <UiButton secondary size="small" :loading="exporting" @click="$emit('export')">
          <template #icon><Download :size="14" /></template>
          导出 CSV
        </UiButton>
      </div>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import { Download, Search } from 'lucide-vue-next';
import CardSurface from '../../components/layout/CardSurface.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import type { DevicePassStationQueryMode, PassStationFilters } from './types';

defineEmits<{
  search: [];
  export: [];
  switchMode: [value: DevicePassStationQueryMode];
}>();

defineProps<{
  currentMode: DevicePassStationQueryMode;
  activeQueryModes: Array<{ key: DevicePassStationQueryMode; label: string }>;
  filters: PassStationFilters;
  exporting: boolean;
}>();
</script>
