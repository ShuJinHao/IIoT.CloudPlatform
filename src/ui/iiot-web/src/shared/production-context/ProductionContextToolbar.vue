<template>
  <NiondToolbar class="production-context-toolbar">
    <div class="production-context-toolbar__row">
      <label class="production-context-toolbar__field">
        <span class="production-context-toolbar__label">{{ t('productionContext.processLabel') }}</span>
        <UiSelect
          :data-testid="`${testIdPrefix}-process-select`"
          :value="processId"
          :options="processOptions"
          :placeholder="t('productionContext.processPlaceholder')"
          :loading="status === 'loading' || status === 'idle'"
          :disabled="status !== 'ready' || !hasAuthorizedDevices"
          clearable
          @update:value="emitProcess"
        />
      </label>

      <label class="production-context-toolbar__field">
        <span class="production-context-toolbar__label">{{ t('productionContext.deviceLabel') }}</span>
        <UiSelect
          :data-testid="`${testIdPrefix}-device-select`"
          :value="deviceId"
          :options="deviceOptions"
          :placeholder="t('productionContext.devicePlaceholder')"
          :disabled="status !== 'ready' || !processId || deviceOptions.length === 0"
          clearable
          @update:value="emitDevice"
        />
      </label>

      <div v-if="context" class="production-context-toolbar__summary">
        <span>{{ t('productionContext.currentContext') }}</span>
        <strong>{{ context.processName }} · {{ context.deviceName }}</strong>
        <code>{{ context.processCode }} / {{ context.deviceCode }}</code>
      </div>
    </div>
  </NiondToolbar>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { ProductionContext, ProductionContextStatus } from './types';

defineProps<{
  processId: string | null;
  deviceId: string | null;
  processOptions: UiSelectOption[];
  deviceOptions: UiSelectOption[];
  context: ProductionContext | null;
  status: ProductionContextStatus;
  hasAuthorizedDevices: boolean;
  testIdPrefix: string;
}>();

const emit = defineEmits<{
  'update:process-id': [value: string | null];
  'update:device-id': [value: string | null];
}>();
const { t } = useI18n();

function emitProcess(value: string | number | boolean | null) {
  emit('update:process-id', typeof value === 'string' ? value : null);
}

function emitDevice(value: string | number | boolean | null) {
  emit('update:device-id', typeof value === 'string' ? value : null);
}
</script>

<style scoped>
.production-context-toolbar {
  margin-bottom: var(--space-4);
}

.production-context-toolbar__row {
  display: grid;
  width: 100%;
  grid-template-columns: minmax(220px, 300px) minmax(260px, 340px) minmax(260px, 1fr);
  align-items: end;
  gap: 18px;
}

.production-context-toolbar__field,
.production-context-toolbar__summary {
  display: grid;
  min-width: 0;
}

.production-context-toolbar__field {
  gap: 8px;
}

.production-context-toolbar__summary {
  gap: 4px;
  padding: 2px 0 3px;
}

.production-context-toolbar__label,
.production-context-toolbar__summary > span {
  color: var(--muted-foreground);
  font-size: var(--fs-xs);
  font-weight: var(--fw-bold);
}

.production-context-toolbar__summary strong {
  overflow: hidden;
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-strong);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.production-context-toolbar__summary code {
  overflow: hidden;
  color: var(--muted-foreground);
  font-size: var(--fs-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1024px) {
  .production-context-toolbar__row {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .production-context-toolbar__summary {
    grid-column: 1 / -1;
  }
}

@media (max-width: 640px) {
  .production-context-toolbar__row {
    grid-template-columns: 1fr;
  }

  .production-context-toolbar__summary {
    grid-column: auto;
  }
}
</style>
