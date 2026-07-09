<template>
  <UiDrawer :show="show" width="980px" @update:show="emitShow">
    <div class="plc-drawer">
      <header class="plc-drawer__header">
        <div>
          <p class="eyebrow">客户端 PLC 状态</p>
          <h2>{{ host?.hostName || '上位机详情' }}</h2>
          <div class="plc-drawer__meta">
            <code>{{ host?.clientCode || '-' }}</code>
            <span>{{ host?.primaryIpAddress || '-' }}</span>
            <UiTag v-if="host" size="small" :type="softwareTagType" :bordered="false">
              {{ softwareStatusText }}
            </UiTag>
          </div>
        </div>
        <div class="plc-drawer__actions">
          <UiButton size="small" secondary @click="$emit('close')">关闭</UiButton>
        </div>
      </header>

      <section class="plc-drawer__summary">
        <div>
          <span>PLC 数量</span>
          <strong>{{ host?.plcCount ?? 0 }}</strong>
        </div>
        <div>
          <span>已连接</span>
          <strong>{{ host?.connectedPlcCount ?? 0 }}</strong>
        </div>
        <div>
          <span>异常</span>
          <strong>{{ host?.faultedPlcCount ?? 0 }}</strong>
        </div>
        <div>
          <span>最后 PLC 上报</span>
          <strong>{{ lastPlcSeenAt }}</strong>
        </div>
        <div>
          <span>最后运行心跳</span>
          <strong>{{ lastRuntimeHeartbeatAt }}</strong>
        </div>
      </section>

      <section class="plc-drawer__section">
        <div class="plc-drawer__section-title">
          <h3>PLC 清单与状态</h3>
          <UiTag size="small" :bordered="false" type="info">来自 Edge 客户端上报 {{ runtimeStates.length }}</UiTag>
        </div>
        <UiDataTable
          class="edge-host-page__table"
          :columns="runtimeColumns"
          :data="runtimeStates"
          :loading="loading || runtimeLoading"
          :row-key="runtimeRowKey"
        >
          <template #empty>
            <EmptyState title="客户端尚未上报 PLC 清单" description="等待 Edge 客户端上报本地 PLC 配置快照和运行状态。" />
          </template>
        </UiDataTable>
      </section>
    </div>
  </UiDrawer>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiDrawer from '../../components/ui/UiDrawer.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { UiDataTableColumn } from '../../components/ui/types';
import type {
  EdgeHostDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import { formatDateTime } from './types';

const props = defineProps<{
  show: boolean;
  host: EdgeHostDto | null;
  runtimeColumns: UiDataTableColumn<EdgeHostPlcRuntimeStateDto>[];
  runtimeStates: EdgeHostPlcRuntimeStateDto[];
  loading: boolean;
  runtimeLoading: boolean;
}>();
const emit = defineEmits<{
  'update:show': [value: boolean];
  close: [];
}>();

const softwareStatusText = computed(() => {
  switch ((props.host?.softwareStatus ?? '').toLowerCase()) {
    case 'running':
      return '运行中';
    case 'starting':
      return '启动中';
    case 'stopped':
      return '已停止';
    case 'runtimeheartbeatstale':
      return '心跳超时';
    case 'missingruntimeheartbeat':
      return '无运行心跳';
    default:
      return props.host?.softwareStatus || '未知';
  }
});
const softwareTagType = computed(() => {
  switch ((props.host?.softwareStatus ?? '').toLowerCase()) {
    case 'running':
      return 'success';
    case 'starting':
      return 'info';
    case 'stopped':
    case 'runtimeheartbeatstale':
      return 'warning';
    default:
      return 'default';
  }
});
const lastPlcSeenAt = computed(() => formatDateTime(props.host?.lastPlcSeenAtUtc));
const lastRuntimeHeartbeatAt = computed(() => formatDateTime(props.host?.lastRuntimeHeartbeatAtUtc));
const runtimeRowKey = (row: EdgeHostPlcRuntimeStateDto) => row.id;

function emitShow(value: boolean) {
  emit('update:show', value);
  if (!value) emit('close');
}
</script>
