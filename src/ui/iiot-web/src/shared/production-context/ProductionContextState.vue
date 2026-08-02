<template>
  <CardSurface
    :data-testid="`${testIdPrefix}-${state}`"
    :data-context-state="state"
    class="production-context-state"
  >
    <LoadingState v-if="state === 'loading'" variant="card" :rows="5" />
    <EmptyState v-else :title="copy.title" :description="copy.description">
      <template #icon>
        <AlertTriangle v-if="state === 'error'" :size="52" :stroke-width="1.6" />
        <Factory v-else-if="state === 'no-authorized-devices' || state === 'no-process-devices'" :size="52" :stroke-width="1.6" />
        <ListFilter v-else :size="52" :stroke-width="1.6" />
      </template>
      <template v-if="state === 'error'" #action>
        <UiButton type="primary" @click="$emit('retry')">{{ t('productionContext.retry') }}</UiButton>
      </template>
    </EmptyState>
  </CardSurface>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AlertTriangle, Factory, ListFilter } from 'lucide-vue-next';
import CardSurface from '../../components/layout/CardSurface.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import LoadingState from '../../components/states/LoadingState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import type { ProductionContextState } from './types';

const props = defineProps<{
  state: Exclude<ProductionContextState, 'ready'>;
  error: string;
  testIdPrefix: string;
}>();

defineEmits<{ retry: [] }>();
const { t } = useI18n();
const copy = computed(() => {
  if (props.state === 'error') {
    return { title: t('productionContext.errorTitle'), description: props.error };
  }
  return {
    title: t(`productionContext.state.${props.state}.title`),
    description: t(`productionContext.state.${props.state}.description`),
  };
});
</script>

<style scoped>
.production-context-state {
  min-height: 280px;
}
</style>
