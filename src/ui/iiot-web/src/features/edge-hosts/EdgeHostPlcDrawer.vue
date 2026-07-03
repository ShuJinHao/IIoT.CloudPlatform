<template>
  <UiDrawer :show="show" width="980px" @update:show="emitShow">
    <div class="plc-drawer">
      <header class="plc-drawer__header">
        <div>
          <p class="eyebrow">PLC 绑定配置</p>
          <h2>{{ host?.hostName || '上位机详情' }}</h2>
          <div class="plc-drawer__meta">
            <code>{{ host?.clientCode || '-' }}</code>
            <span>{{ deviceLabel }}</span>
            <UiTag v-if="host" size="small" :type="host.enabled ? 'success' : 'warning'" :bordered="false">
              {{ host.enabled ? '配置启用' : '配置禁用' }}
            </UiTag>
          </div>
        </div>
        <div class="plc-drawer__actions">
          <UiButton v-if="canManage" type="primary" size="small" @click="$emit('add')">
            <Plus :size="14" />
            新增 PLC
          </UiButton>
          <UiButton size="small" secondary @click="$emit('close')">关闭</UiButton>
        </div>
      </header>

      <section class="plc-drawer__summary">
        <div>
          <span>PLC 配置</span>
          <strong>{{ host?.plcBindings.length ?? 0 }}</strong>
        </div>
        <div>
          <span>已启用配置</span>
          <strong>{{ enabledCount }}</strong>
        </div>
        <div>
          <span>最后更新</span>
          <strong>{{ lastUpdated }}</strong>
        </div>
        <div>
          <span>状态投影</span>
          <strong>{{ runtimeStates.length }}</strong>
        </div>
        <div>
          <span>产能可读</span>
          <strong>{{ readableCapacityCount }}</strong>
        </div>
      </section>

      <section class="plc-drawer__section">
        <div class="plc-drawer__section-title">
          <h3>PLC 绑定配置</h3>
          <UiTag size="small" :bordered="false">期望绑定 {{ host?.plcBindings.length ?? 0 }}</UiTag>
        </div>
        <UiDataTable
          class="edge-host-page__table"
          :columns="columns"
          :data="host?.plcBindings ?? []"
          :loading="loading"
          :row-key="rowKey"
        >
          <template #empty>
            <EmptyState title="未配置 PLC" description="当前上位机还没有 PLC 绑定配置。" />
          </template>
        </UiDataTable>
      </section>

      <section class="plc-drawer__section">
        <div class="plc-drawer__section-title">
          <h3>PLC 状态投影</h3>
          <UiTag size="small" :bordered="false" type="info">来自 Edge 上报 {{ runtimeStates.length }}</UiTag>
        </div>
        <UiDataTable
          class="edge-host-page__table"
          :columns="runtimeColumns"
          :data="runtimeStates"
          :loading="runtimeLoading"
          :row-key="runtimeRowKey"
        >
          <template #empty>
            <EmptyState title="未收到 PLC 状态" description="等待 Edge 上报真实 PLC 运行状态。" />
          </template>
        </UiDataTable>
      </section>

      <section class="plc-drawer__section">
        <div class="plc-drawer__section-title">
          <h3>PLC 产能汇总</h3>
          <UiTag size="small" :bordered="false" type="info">{{ capacityDate }}</UiTag>
        </div>
        <UiDataTable
          class="edge-host-page__table"
          :columns="capacityColumns"
          :data="capacitySummaries"
          :loading="capacityLoading"
          :row-key="capacityRowKey"
        >
          <template #empty>
            <EmptyState title="未读取到 PLC 产能" description="当前没有可展示的 PLC 产能汇总。" />
          </template>
        </UiDataTable>
      </section>
    </div>
  </UiDrawer>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { Plus } from 'lucide-vue-next';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type {
  EdgeHostDto,
  EdgeHostPlcBindingDto,
  EdgeHostPlcCapacitySummaryDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import { formatDateTime } from './types';

const props = defineProps<{
  show: boolean;
  host: EdgeHostDto | null;
  columns: UiDataTableColumn<EdgeHostPlcBindingDto>[];
  runtimeColumns: UiDataTableColumn<EdgeHostPlcRuntimeStateDto>[];
  capacityColumns: UiDataTableColumn<EdgeHostPlcCapacitySummaryDto>[];
  runtimeStates: EdgeHostPlcRuntimeStateDto[];
  capacitySummaries: EdgeHostPlcCapacitySummaryDto[];
  loading: boolean;
  runtimeLoading: boolean;
  capacityLoading: boolean;
  capacityDate: string;
  canManage: boolean;
  deviceLabel: string;
}>();
const emit = defineEmits<{
  'update:show': [value: boolean];
  add: [];
  close: [];
}>();

const enabledCount = computed(() =>
  props.host?.plcBindings.filter((binding) => binding.enabled).length ?? 0,
);
const readableCapacityCount = computed(() =>
  props.capacitySummaries.filter((item) => item.canReadCapacity).length,
);
const lastUpdated = computed(() => formatDateTime(props.host?.updatedAtUtc));
const rowKey = (row: EdgeHostPlcBindingDto) => row.id;
const runtimeRowKey = (row: EdgeHostPlcRuntimeStateDto) => row.id;
const capacityRowKey = (row: EdgeHostPlcCapacitySummaryDto) => row.plcBindingId;

function emitShow(value: boolean) {
  emit('update:show', value);
  if (!value) emit('close');
}
</script>
