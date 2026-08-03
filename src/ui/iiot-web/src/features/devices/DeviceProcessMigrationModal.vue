<template>
  <UiModal
    v-model:show="show"
    preset="card"
    title="迁移设备工序"
    style="width: min(620px, calc(100vw - 32px));"
    :mask-closable="false"
  >
    <div class="migration-form">
      <div v-if="dialog.device" class="migration-device">
        <div>
          <span>设备</span>
          <strong>{{ dialog.device.deviceName }}</strong>
        </div>
        <div>
          <span>ClientCode</span>
          <code>{{ dialog.device.code }}</code>
        </div>
      </div>

      <label class="migration-field">
        <span>目标工序</span>
        <UiSelect
          data-testid="device-migration-target-process"
          :value="dialog.targetProcessId"
          :options="targetProcessOptions"
          placeholder="请选择目标工序"
          :disabled="submitting"
          clearable
          @update:value="emitTarget"
        />
      </label>

      <div v-if="dialog.loading" class="migration-message">正在重新核对设备与关联数据…</div>
      <div v-else-if="dialog.error" class="migration-error" role="alert">
        <strong>迁移预检失败</strong>
        <span>{{ dialog.error }}</span>
      </div>

      <template v-else-if="dialog.impact">
        <div class="migration-route">
          <div>
            <span>源工序</span>
            <strong>{{ processLabel(dialog.impact.sourceProcess) }}</strong>
          </div>
          <span aria-hidden="true">→</span>
          <div>
            <span>目标工序</span>
            <strong>{{ processLabel(dialog.impact.targetProcess) }}</strong>
          </div>
        </div>

        <div class="migration-counts">
          <div><span>配方</span><strong>{{ dialog.impact.relatedCounts.recipes }}</strong></div>
          <div><span>产能</span><strong>{{ dialog.impact.relatedCounts.capacities }}</strong></div>
          <div><span>过站</span><strong>{{ dialog.impact.relatedCounts.passStations }}</strong></div>
          <div><span>PLC 运行状态</span><strong>{{ dialog.impact.relatedCounts.edgeHostPlcRuntimeStates }}</strong></div>
        </div>

        <div v-if="dialog.impact.blockers.length" class="migration-error" role="alert">
          <strong>当前禁止迁移</strong>
          <span v-for="blocker in dialog.impact.blockers" :key="blocker.code">
            {{ blocker.message }}<template v-if="blocker.count > 0">（{{ blocker.count }} 条）</template>
          </span>
        </div>

        <label v-else class="migration-field">
          <span>输入确认文本</span>
          <code>{{ dialog.impact.confirmationText }}</code>
          <UiInput
            v-model:value="dialog.confirmInput"
            data-testid="device-migration-confirmation"
            autocomplete="off"
            :placeholder="dialog.impact.confirmationText"
          />
        </label>
      </template>
    </div>

    <template #footer>
      <div class="modal-actions">
        <UiButton @click="show = false">取消</UiButton>
        <UiButton
          type="warning"
          data-testid="device-migration-submit"
          :disabled="confirmDisabled"
          :loading="submitting"
          @click="$emit('submit')"
        >
          确认迁移
        </UiButton>
      </div>
    </template>
  </UiModal>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import UiSelect from '../../components/ui/UiSelect.vue';
import type { UiSelectOption } from '../../components/ui/types';
import type { DeviceProcessMigrationProcessDto } from './api';
import type { DeviceProcessMigrationDialogState } from './types';

const show = defineModel<boolean>('show', { required: true });
const props = defineProps<{
  dialog: DeviceProcessMigrationDialogState;
  processOptions: UiSelectOption[];
  submitting: boolean;
}>();
const emit = defineEmits<{
  'target-change': [value: string | null];
  submit: [];
}>();

const targetProcessOptions = computed(() =>
  props.processOptions.filter(
    option => String(option.value) !== props.dialog.device?.processId,
  ),
);
const confirmDisabled = computed(() =>
  props.submitting
  || props.dialog.loading
  || !props.dialog.impact?.canMigrate
  || props.dialog.confirmInput !== props.dialog.impact.confirmationText,
);

function emitTarget(value: string | number | boolean | null) {
  emit('target-change', typeof value === 'string' ? value : null);
}

function processLabel(process: DeviceProcessMigrationProcessDto) {
  return `${process.processCode} · ${process.processName}`;
}
</script>

<style scoped>
.migration-form,
.migration-field,
.migration-error {
  display: grid;
  gap: 10px;
}

.migration-device,
.migration-route,
.migration-counts {
  display: grid;
  gap: 12px;
}

.migration-device {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: 14px;
  background: var(--muted);
}

.migration-device > div,
.migration-route > div,
.migration-counts > div {
  display: grid;
  gap: 4px;
}

.migration-device span,
.migration-field > span,
.migration-route span,
.migration-counts span {
  color: var(--muted-foreground);
  font-size: 12px;
  font-weight: 700;
}

.migration-route {
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  padding: 14px;
  border-radius: 14px;
  background: rgba(245, 158, 11, 0.08);
}

.migration-counts {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.migration-counts > div {
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 12px;
}

.migration-error {
  padding: 13px 14px;
  border: 1px solid rgba(220, 38, 38, 0.2);
  border-radius: 12px;
  color: #b91c1c;
  background: rgba(220, 38, 38, 0.06);
}

.migration-message {
  color: var(--muted-foreground);
}

@media (max-width: 640px) {
  .migration-device,
  .migration-counts {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
