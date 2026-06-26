<template>
  <CardSurface class="device-log-page__filter-card">
    <div class="filter-stack">
      <div class="filter-field">
        <span class="filter-field__label">查询粒度</span>
        <UiRadioGroup :value="currentMode" size="small" @update:value="$emit('switchMode', $event)">
          <UiRadioButton v-for="mode in queryModes" :key="mode.key" :value="mode.key">
            {{ mode.label }}
          </UiRadioButton>
        </UiRadioGroup>
      </div>

      <div class="filter-row">
        <template v-if="currentMode === 'level'">
          <div class="filter-field">
            <span class="filter-field__label">日志级别</span>
            <UiSelect
              v-model:value="filters.level"
              :options="levelOptions"
              placeholder="全部级别"
              clearable
              size="small"
              style="width: 200px;"
            />
          </div>
        </template>

        <template v-if="currentMode === 'keyword'">
          <div class="filter-field filter-field--wide">
            <span class="filter-field__label">关键字</span>
            <UiInput
              v-model:value="filters.keyword"
              placeholder="搜索日志内容"
              size="small"
              style="width: 320px;"
              @keyup.enter="$emit('search')"
            />
          </div>
        </template>

        <template v-if="currentMode === 'date'">
          <div class="filter-field">
            <span class="filter-field__label">日期</span>
            <UiDatePicker
              v-model:formatted-value="filters.date"
              value-format="yyyy-MM-dd"
              type="date"
              size="small"
              style="width: 180px;"
            />
          </div>
        </template>

        <template v-if="currentMode === 'time-range'">
          <div class="filter-field">
            <span class="filter-field__label">开始时间</span>
            <UiDatePicker
              v-model:formatted-value="filters.startTime"
              value-format="yyyy-MM-dd'T'HH:mm"
              type="datetime"
              size="small"
              style="width: 220px;"
            />
          </div>
          <div class="filter-field">
            <span class="filter-field__label">结束时间</span>
            <UiDatePicker
              v-model:formatted-value="filters.endTime"
              value-format="yyyy-MM-dd'T'HH:mm"
              type="datetime"
              size="small"
              style="width: 220px;"
            />
          </div>
        </template>

        <template v-if="currentMode === 'date-keyword'">
          <div class="filter-field">
            <span class="filter-field__label">日期</span>
            <UiDatePicker
              v-model:formatted-value="filters.date"
              value-format="yyyy-MM-dd"
              type="date"
              size="small"
              style="width: 180px;"
            />
          </div>
          <div class="filter-field filter-field--wide">
            <span class="filter-field__label">关键字</span>
            <UiInput
              v-model:value="filters.keyword"
              placeholder="搜索日志内容"
              size="small"
              style="width: 280px;"
              @keyup.enter="$emit('search')"
            />
          </div>
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
import {
  levelOptions,
  queryModes,
  type DeviceLogFilters,
  type DeviceLogQueryMode,
} from './types';

defineProps<{
  currentMode: DeviceLogQueryMode;
  filters: DeviceLogFilters;
}>();

defineEmits<{
  switchMode: [mode: DeviceLogQueryMode];
  search: [];
}>();
</script>
