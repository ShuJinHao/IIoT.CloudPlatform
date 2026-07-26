<template>
  <div class="version-inventory">
    <div class="version-inventory__context">
      <div>
        <span>发布通道</span>
        <strong>{{ release.channel || '未上报' }}</strong>
      </div>
      <div>
        <span>宿主升级状态</span>
        <UiTag size="small" :bordered="false" :type="releaseStatusTone(release.hostUpdateStatus)">
          {{ releaseStatusText(release.hostUpdateStatus) }}
        </UiTag>
      </div>
    </div>

    <p class="version-inventory__note">
      运行心跳、安装版本上报和正式发布目录是三类独立事实；任一来源缺失时不使用其它来源补值。
    </p>

    <div class="version-fact-grid">
      <article class="version-fact-card" data-testid="runtime-version-fact">
        <header>
          <span>运行实例</span>
          <UiTag size="small" :bordered="false" :type="softwareStatusTone(release.softwareStatus)">
            {{ softwareStatusText(release.softwareStatus) }}
          </UiTag>
        </header>
        <strong class="version-fact-card__version">{{ release.runtimeHostVersion || '未上报' }}</strong>
        <dl>
          <div><dt>Host API</dt><dd>{{ release.runtimeHostApiVersion || '未上报' }}</dd></div>
          <div><dt>最后运行心跳</dt><dd>{{ factDate(release.lastRuntimeHeartbeatAtUtc, '未上报') }}</dd></div>
        </dl>
      </article>

      <article class="version-fact-card" data-testid="reported-version-fact">
        <header>
          <span>安装版本上报</span>
          <UiTag size="small" :bordered="false" :type="releaseStatusTone(release.installStatus)">
            {{ releaseStatusText(release.installStatus) }}
          </UiTag>
        </header>
        <strong class="version-fact-card__version">{{ release.reportedHostVersion || '未上报' }}</strong>
        <dl>
          <div><dt>Host API</dt><dd>{{ release.reportedHostApiVersion || '未上报' }}</dd></div>
          <div><dt>版本上报时间</dt><dd>{{ reportedAt }}</dd></div>
        </dl>
      </article>

      <article class="version-fact-card" data-testid="published-version-fact">
        <header>
          <span>最新正式发布</span>
          <UiTag size="small" :bordered="false" :type="releaseStatusTone(release.hostUpdateStatus)">
            {{ releaseStatusText(release.hostUpdateStatus) }}
          </UiTag>
        </header>
        <strong class="version-fact-card__version">{{ latestPublishedVersion }}</strong>
        <dl>
          <div><dt>Host API</dt><dd>{{ release.latestPublishedHostApiVersion || '未提供' }}</dd></div>
          <div><dt>发布时间</dt><dd>{{ factDate(release.latestPublishedAtUtc, '未提供') }}</dd></div>
          <div>
            <dt>包 SHA-256</dt>
            <dd class="version-fact-card__digest" :title="release.latestPublishedHostPackageSha256 || undefined">
              {{ latestPackageDigest }}
            </dd>
          </div>
        </dl>
      </article>
    </div>

    <div v-if="release.versionIssue || release.hostCompatibilityIssue" class="version-issues">
      <p v-if="release.versionIssue">版本问题：{{ release.versionIssue }}</p>
      <p v-if="release.hostCompatibilityIssue">兼容性问题：{{ release.hostCompatibilityIssue }}</p>
    </div>

    <section class="plugin-inventory">
      <div class="plugin-inventory__header">
        <div>
          <h4>插件版本事实</h4>
          <p>安装、配置启用和运行状态分别展示；当前接口尚未上报插件运行态。</p>
        </div>
        <UiTag size="small" :bordered="false">已安装 {{ release.plugins.length }}</UiTag>
      </div>
      <UiDataTable
        :columns="pluginColumns"
        :data="release.plugins"
        :row-key="pluginRowKey"
      >
        <template #empty>
          <EmptyState title="客户端尚未上报插件清单" description="等待 Edge 客户端上报安装版本快照后展示插件明细。" />
        </template>
      </UiDataTable>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiTag from '../../components/ui/UiTag.vue';
import type { DeviceClientPluginInventoryDto, DeviceClientReleaseDetailsDto } from './api';
import {
  createPluginInventoryColumns,
  releaseStatusText,
  releaseStatusTone,
  softwareStatusText,
  softwareStatusTone,
} from './columns';
import { formatDateTime, formatPackageDigest } from './types';

const props = defineProps<{
  release: DeviceClientReleaseDetailsDto;
}>();

const pluginColumns = createPluginInventoryColumns();
const pluginRowKey = (row: DeviceClientPluginInventoryDto) => row.moduleId;

const reportedAt = computed(() =>
  factDate(props.release.reportedAtUtc ?? props.release.receivedAtUtc, '未上报'));
const latestPublishedVersion = computed(() => {
  if (props.release.latestPublishedHostVersion) return props.release.latestPublishedHostVersion;
  return props.release.hostUpdateStatus.toLowerCase() === 'norelease'
    ? '无正式发布'
    : '未提供';
});
const latestPackageDigest = computed(() => {
  const digest = formatPackageDigest(props.release.latestPublishedHostPackageSha256);
  return digest === '-' ? '未提供' : digest;
});

function factDate(value: string | null | undefined, emptyText: string): string {
  const formatted = formatDateTime(value);
  return formatted === '-' ? emptyText : formatted;
}
</script>
