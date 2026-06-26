<template>
  <UiDrawer v-model:show="show" :width="460" placement="right">
    <UiDrawerContent title="过站详情" closable>
      <LoadingState v-if="loading" :rows="6" />
      <div v-else-if="detail && schema" class="detail-stack">
        <div class="detail-result-banner" :class="(detail.cellResult ?? '').toUpperCase() === 'OK' ? 'is-ok' : 'is-ng'">
          <span class="detail-result-banner__dot"></span>
          结果：{{ formatResultText(detail.cellResult || '-') }}
        </div>
        <div v-for="section in schema.detailSections" :key="section.title" class="detail-section">
          <div class="detail-section__title">{{ section.title }}</div>
          <div v-for="field in section.fields" :key="field.key" class="detail-row">
            <span class="detail-row__label">{{ field.label }}</span>
            <span class="detail-row__value" :class="{ 'detail-row__value--mono': field.className === 'mono-val' || field.className === 'mono-val small', 'detail-row__value--small': field.className === 'small' || field.className === 'mono-val small' }">
              {{ formatDisplayValue(field.render(detail)) }}
            </span>
          </div>
        </div>
      </div>
    </UiDrawerContent>
  </UiDrawer>
</template>

<script setup lang="ts">
import LoadingState from '../../components/states/LoadingState.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiDrawerContent from '../../components/ui/UiDrawerContent.vue';
import type { PassStationDetailDto } from './api';
import type { PassStationSchema } from './schema';
import { formatDisplayValue, formatResultText } from './types';

const show = defineModel<boolean>('show', { required: true });
defineProps<{
  loading: boolean;
  detail: PassStationDetailDto | null;
  schema: PassStationSchema | null;
}>();
</script>
