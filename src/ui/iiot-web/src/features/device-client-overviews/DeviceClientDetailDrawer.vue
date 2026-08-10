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
          <UiTag
            v-if="!plcLoading && !plcError && plcProjection"
            size="small"
            :bordered="false"
            :type="plcFreshnessTone"
            data-testid="plc-freshness-tag"
          >
            {{ plcFreshnessLabel }}
          </UiTag>
        </div>
        <div v-if="plcLoading" class="detail-state">加载中…</div>
        <div v-else-if="plcError" class="detail-state detail-state--error" role="alert">
          <EmptyState title="PLC 状态加载失败" :description="plcError" />
          <UiButton size="small" secondary @click="$emit('retryPlc')">重试</UiButton>
        </div>
        <template v-else>
          <dl v-if="plcProjection" class="plc-projection-summary" data-testid="plc-projection-summary">
            <div>
              <dt>快照最后接收时间</dt>
              <dd>{{ formatDateTime(plcProjection.plcSnapshotReceivedAtUtc) }}</dd>
            </div>
            <div>
              <dt>PLC 最后观测时间</dt>
              <dd>{{ formatDateTime(plcProjection.lastPlcSeenAtUtc) }}</dd>
            </div>
            <div>
              <dt>判定原因</dt>
              <dd>{{ plcIssueText }}</dd>
            </div>
          </dl>
          <UiDataTable
            :columns="plcColumns"
            :data="plcStates"
            :row-key="plcRowKey"
          >
            <template #empty>
              <EmptyState
                v-if="normalizedPlcFreshness === 'Unavailable'"
                title="PLC 权威快照不可用"
                description="Cloud 未获得可作为当前事实的 PLC 快照，不将空列表解释为 0 台 PLC。"
              />
              <EmptyState
                v-else-if="normalizedPlcFreshness === 'Stale'"
                title="PLC 状态已过期"
                description="Cloud 仅保留过期的历史快照，不将其空列表解释为当前 0 台 PLC。"
              />
              <EmptyState
                v-else
                title="当前权威快照中无 PLC"
                description="Cloud 已确认当前权威快照中没有 PLC 行。"
              />
            </template>
          </UiDataTable>
        </template>
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
  EdgeHostPlcProjectionDto,
  EdgeHostPlcRuntimeStateDto,
} from './api';
import {
  createPlcRuntimeStateColumns,
  plcFreshnessTagTone,
  plcFreshnessText as resolvePlcFreshnessText,
  softwareStatusText,
} from './columns';
import { formatDateTime } from './types';

const props = defineProps<{
  show: boolean;
  device: DeviceClientOverviewItemDto | null;
  canViewPlc: boolean;
  canViewRelease: boolean;
  plcProjection: EdgeHostPlcProjectionDto | null;
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
const plcStates = computed(() => props.plcProjection?.plcStates ?? []);
const normalizedPlcFreshness = computed(() => {
  const freshness = props.plcProjection?.plcFreshness;
  return freshness === 'Current' || freshness === 'Stale' ? freshness : 'Unavailable';
});
const plcFreshnessTone = computed(() => plcFreshnessTagTone(normalizedPlcFreshness.value));
const plcFreshnessLabel = computed(() => {
  const base = resolvePlcFreshnessText(normalizedPlcFreshness.value);
  return normalizedPlcFreshness.value === 'Current'
    ? `${base} · ${plcStates.value.length} 台`
    : base;
});
const plcIssueText = computed(() => {
  switch (props.plcProjection?.plcIssue) {
    case 'PlcSnapshotStale':
      return 'Cloud 已判定 PLC 权威快照超过统一新鲜度窗口。';
    case 'PlcSnapshotUnavailable':
      return '缺少可用的 PLC 权威快照。';
    case 'PlcSnapshotClockSkew':
      return 'PLC 快照接收时间晚于 Cloud 当前时间，已记录时钟偏差。';
    default:
      return props.plcProjection?.plcIssue || (
        normalizedPlcFreshness.value === 'Current'
          ? '当前权威快照有效。'
          : '权威 PLC 快照不可用。'
      );
  }
});

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
