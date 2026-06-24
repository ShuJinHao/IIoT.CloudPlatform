<template>
  <CardSurface class="passstation-page__filter-card">
    <div class="filter-stack">
      <div class="filter-field">
        <span class="filter-field__label">查询模式</span>
        <UiRadioGroup :value="currentMode" size="small" @update:value="$emit('switchMode', $event)">
          <UiRadioButton v-for="mode in activeQueryModes" :key="mode.key" :value="mode.key">{{ mode.label }}</UiRadioButton>
        </UiRadioGroup>
      </div>
      <div class="filter-row">
        <template v-if="currentMode === 'barcode-process'">
          <div class="filter-field filter-field--wide">
            <span class="filter-field__label">条码</span>
            <UiInput v-model:value="filters.barcode" placeholder="请输入条码" size="small" style="width: 280px;" @keyup.enter="$emit('search')" />
          </div>
        </template>
        <template v-if="currentMode === 'time-process'">
          <div class="filter-field">
            <span class="filter-field__label">开始时间</span>
            <UiDatePicker v-model:formatted-value="filters.startTime" value-format="yyyy-MM-dd'T'HH:mm" type="datetime" size="small" style="width: 220px;" />
          </div>
          <div class="filter-field">
            <span class="filter-field__label">结束时间</span>
            <UiDatePicker v-model:formatted-value="filters.endTime" value-format="yyyy-MM-dd'T'HH:mm" type="datetime" size="small" style="width: 220px;" />
          </div>
        </template>
        <template v-if="currentMode === 'device-barcode'">
          <div class="filter-field">
            <span class="filter-field__label">设备</span>
            <UiSelect v-model:value="filters.deviceId" :options="deviceOptions" placeholder="请选择设备" clearable size="small" style="width: 220px;" />
          </div>
          <div class="filter-field filter-field--wide">
            <span class="filter-field__label">条码</span>
            <UiInput v-model:value="filters.barcode" placeholder="请输入条码" size="small" style="width: 240px;" @keyup.enter="$emit('search')" />
          </div>
        </template>
        <template v-if="currentMode === 'device-time'">
          <div class="filter-field">
            <span class="filter-field__label">设备</span>
            <UiSelect v-model:value="filters.deviceId" :options="deviceOptions" placeholder="请选择设备" clearable size="small" style="width: 220px;" />
          </div>
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
          <div class="filter-field">
            <span class="filter-field__label">设备</span>
            <UiSelect v-model:value="filters.deviceId" :options="deviceOptions" placeholder="请选择设备" clearable size="small" style="width: 220px;" />
          </div>
          <div class="latest-hint">读取所选设备最新 200 条过站记录</div>
        </template>
        <UiButton type="primary" size="small" @click="$emit('search')">
          <template #icon><Search :size="14" /></template>
          查询
        </UiButton>
      </div>
    </div>
  </CardSurface>
</template>

<script setup lang="ts">
import { Search } from 'lucide-vue-next';
import CardSurface from '../../components/layout/CardSurface.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDatePicker from '../../components/ui/UiDatePicker.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiRadioButton from '../../components/ui/UiRadioButton.vue';
import UiRadioGroup from '../../components/ui/UiRadioGroup.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { PassStationQueryMode } from './api';
import type { PassStationFilters } from './types';

defineEmits<{
  search: [];
  switchMode: [value: PassStationQueryMode];
}>();

defineProps<{
  currentMode: PassStationQueryMode;
  activeQueryModes: Array<{ key: PassStationQueryMode; label: string }>;
  filters: PassStationFilters;
  deviceOptions: UiSelectOption[];
}>();
</script>
