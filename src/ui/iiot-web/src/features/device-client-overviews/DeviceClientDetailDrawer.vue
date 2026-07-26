<template>
  <UiDrawer :show="show" width="920px" @update:show="emitShow">
    <div class="detail-drawer">
      <header class="detail-drawer__header">
        <div>
          <p class="eyebrow">设备运行与版本详情</p>
          <h2>{{ device?.deviceName || '设备详情' }}</h2>
          <div class="detail-drawer__meta">
            <span>{{ device?.primaryIpAddress || '-' }}</span>
            <UiTag v-if="device" size="small" :type="softwareTagTone" :bordered="false">
              {{ deviceStatusText }}
            </UiTag>
          </div>
        </div>
        <div class="detail-drawer__actions">
          <UiButton size="small" secondary @click="emitShow(false)">关闭</UiButton>
        </div>
      </header>

      <!-- PLC 状态：仅 EdgeHost.Read；无权限时既不请求也不渲染占位 -->
      <section v-if="canViewPlc" class="detail-drawer__section">
        <div class="detail-drawer__section-title">
          <h3>PLC 状态</h3>
          <UiTag v-if="!plcLoading && !plcError" size="small" :bordered="false" type="info">
            来自 Edge 客户端上报 {{ plcStates.length }}
          </UiTag>
        </div>
        <div v-if="plcLoading" class="detail-state">加载中…</div>
        <div v-else-if="plcError" class="detail-state detail-state--error" role="alert">
          <EmptyState title="PLC 状态加载失败" :description="plcError" />
          <UiButton size="small" secondary @click="$emit('retryPlc')">重试</UiButton>
        </div>
        <UiDataTable
          v-else
          :columns="plcColumns"
          :data="plcStates"
          :row-key="plcRowKey"
        >
          <template #empty>
            <EmptyState title="客户端尚未上报 PLC 清单" description="等待 Edge 客户端上报本地 PLC 配置快照和运行状态。" />
          </template>
        </UiDataTable>
      </section>

      <!-- 版本、插件和升级详情：仅 ClientRelease.Read；与 PLC 区块失败互不影响 -->
      <section v-if="canViewRelease" class="detail-drawer__section">
        <div class="detail-drawer__section-title">
          <h3>版本、插件和升级详情</h3>
        </div>
        <div v-if="releaseLoading" class="detail-state">加载中…</div>
        <div v-else-if="releaseError" class="detail-state detail-state--error" role="alert">
          <EmptyState title="版本与升级详情加载失败" :description="releaseError" />
          <UiButton size="small" secondary @click="$emit('retryRelease')">重试</UiButton>
        </div>
        <DeviceClientVersionFacts v-else-if="release" :release="release" />
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
import DeviceClientVersionFacts from './DeviceClientVersionFacts.vue';
import type {
  DeviceClientOverviewItemDto,
  DeviceClientReleaseDetailsDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import { createPlcRuntimeStateColumns, softwareStatusText } from './columns';

const props = defineProps<{
  show: boolean;
  device: DeviceClientOverviewItemDto | null;
  canViewPlc: boolean;
  canViewRelease: boolean;
  plcStates: EdgeHostPlcRuntimeStateDto[];
  plcLoading: boolean;
  plcError: string | null;
  release: DeviceClientReleaseDetailsDto | null;
  releaseLoading: boolean;
  releaseError: string | null;
}>();

const emit = defineEmits<{
  'update:show': [value: boolean];
  close: [];
  retryPlc: [];
  retryRelease: [];
}>();

const plcColumns = createPlcRuntimeStateColumns();

const deviceStatusText = computed(() => softwareStatusText(props.device?.softwareStatus));
const softwareTagTone = computed(() => {
  switch ((props.device?.softwareStatus ?? '').toLowerCase()) {
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

const plcRowKey = (row: EdgeHostPlcRuntimeStateDto) => row.id;

function emitShow(value: boolean) {
  emit('update:show', value);
  if (!value) emit('close');
}
</script>
