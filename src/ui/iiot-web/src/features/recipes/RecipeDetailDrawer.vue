<template>
  <UiDrawer v-model:show="show" :width="420" placement="right">
    <UiDrawerContent title="配方详情" closable>
      <LoadingState v-if="loading" :rows="6" />
      <div v-else-if="detail" class="detail-stack">
        <div class="detail-status-banner" :class="detail.status === 'Active' ? 'is-active' : 'is-archived'">
          <span class="detail-status-banner__dot"></span>
          {{ detail.status === 'Active' ? '配方启用中' : '配方已归档' }}
          <UiTag size="small" :bordered="false" :type="isDeviceBoundRecipe(detail.deviceId) ? 'warning' : 'info'" style="margin-left: auto;">
            {{ isDeviceBoundRecipe(detail.deviceId) ? '设备配方' : '未绑定设备' }}
          </UiTag>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">配方名称</span>
          <span class="detail-row__value">{{ detail.recipeName }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">当前版本</span>
          <span class="detail-row__value">
            <UiTag size="small" :bordered="false" type="info" class="mono-tag">{{ detail.version }}</UiTag>
          </span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">归属工序</span>
          <span class="detail-row__value">{{ processNameMap[detail.processId] || detail.processId }}</span>
        </div>
        <div v-if="isDeviceBoundRecipe(detail.deviceId)" class="detail-row">
          <span class="detail-row__label">归属设备</span>
          <span class="detail-row__value">{{ deviceNameMap[detail.deviceId] || detail.deviceId }}</span>
        </div>
        <div class="detail-row">
          <span class="detail-row__label">工艺参数</span>
          <div v-if="detailParams.length > 0" class="detail-params">
            <div v-for="param in detailParams" :key="param.id" class="detail-params__item">
              <span class="detail-params__name">{{ param.name }}</span>
              <span class="detail-params__range">{{ param.min }} ~ {{ param.max }} {{ param.unit }}</span>
            </div>
          </div>
          <pre v-else class="detail-json">{{ prettyJson(detail.parametersJsonb) }}</pre>
        </div>
      </div>
    </UiDrawerContent>
  </UiDrawer>
</template>

<script setup lang="ts">
import LoadingState from '../../components/states/LoadingState.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import UiTag from '../../components/ui/UiTag.vue';
import { isDeviceBoundRecipe, type RecipeDetailDto, type RecipeParameter } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  detail: RecipeDetailDto | null;
  detailParams: RecipeParameter[];
  loading: boolean;
  processNameMap: Record<string, string>;
  deviceNameMap: Record<string, string>;
  prettyJson: (value: string) => string;
}>();
</script>
