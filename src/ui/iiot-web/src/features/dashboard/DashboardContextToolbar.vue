<template>
  <NiondToolbar class="dashboard-context-toolbar">
    <div class="context-row">
      <label class="context-field">
        <span class="context-field__label">{{ t('dashboard.processLabel') }}</span>
        <UiSelect
          data-testid="dashboard-process-select"
          :value="processId"
          :options="processOptions"
          :placeholder="t('dashboard.processPlaceholder')"
          :loading="loading"
          :disabled="loading || !hasAuthorizedDevices"
          clearable
          @update:value="emitProcess"
        />
      </label>

      <label class="context-field">
        <span class="context-field__label">{{ t('dashboard.deviceLabel') }}</span>
        <UiSelect
          data-testid="dashboard-device-select"
          :value="deviceId"
          :options="deviceOptions"
          :placeholder="t('dashboard.devicePlaceholder')"
          :disabled="loading || !processId"
          clearable
          @update:value="emitDevice"
        />
      </label>

      <div v-if="selectedProcess && selectedDevice" class="context-summary">
        <span>{{ t('dashboard.currentContext') }}</span>
        <strong>{{ selectedProcess.name }} · {{ selectedDevice.deviceName }}</strong>
        <code>{{ selectedProcess.code }} / {{ selectedDevice.code }}</code>
      </div>
    </div>
  </NiondToolbar>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { ScopedDeviceSelectDto } from '../devices/api';

defineProps<{
  processId: string | null;
  deviceId: string | null;
  processOptions: UiSelectOption[];
  deviceOptions: UiSelectOption[];
  selectedProcess: { id: string; code: string; name: string } | null;
  selectedDevice: ScopedDeviceSelectDto | null;
  loading: boolean;
  hasAuthorizedDevices: boolean;
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
.context-row {
  display: grid;
  grid-template-columns: minmax(220px, 300px) minmax(260px, 340px) minmax(260px, 1fr);
  align-items: end;
  gap: 18px;
}

.context-field {
  display: grid;
  gap: 8px;
  min-width: 0;
}

.context-field__label,
.context-summary > span {
  color: var(--muted-foreground);
  font-size: var(--fs-xs);
  font-weight: var(--fw-bold);
}

.context-summary {
  display: grid;
  gap: 4px;
  min-width: 0;
  padding: 2px 0 3px;
}

.context-summary strong {
  overflow: hidden;
  color: var(--text-0);
  font-size: var(--fs-base);
  font-weight: var(--fw-strong);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.context-summary code {
  color: var(--muted-foreground);
  font-size: var(--fs-xs);
}

@media (max-width: 1024px) {
  .context-row {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .context-summary {
    grid-column: 1 / -1;
  }
}

@media (max-width: 640px) {
  .context-row {
    grid-template-columns: 1fr;
  }

  .context-summary {
    grid-column: auto;
  }
}
</style>
