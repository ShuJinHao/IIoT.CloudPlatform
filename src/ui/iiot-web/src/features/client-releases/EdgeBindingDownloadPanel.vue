<template>
  <div class="binding-panel">
    <div class="binding-intro">
      <h3>客户端首装包生成</h3>
      <p>只选择已在 Cloud 完成一对一绑定的设备插件。插件版本、Host、通道和架构由服务端自动计算。</p>
      <p class="binding-warn">生成安装包不会替换现场旧凭据；每个设备真实启动并 ready 后才独立激活。</p>
    </div>

    <div class="binding-field">
      <label>云端地址（必填）</label>
      <UiInput v-model:value="baseUrl" size="small" placeholder="例如 http://cloud-host:81" />
    </div>

    <div class="binding-field">
      <label>设备插件</label>
      <UiInput v-model:value="keyword" size="small" placeholder="搜索设备名称、ClientCode 或工序" clearable />
    </div>
    <div class="plugin-list">
      <p v-if="!devices.length" class="binding-empty">暂无可选设备。请先发布独立插件系列并在设备管理中完成绑定。
      </p>
      <button
        v-for="device in filteredDevices"
        :key="device.id"
        type="button"
        class="plugin-item"
        :class="{ 'is-checked': selectedIds.includes(device.id) }"
        @click="toggleDevice(device.id)"
      >
        <div class="plugin-head">
          <UiCheckbox :checked="selectedIds.includes(device.id)" @update:checked="toggleDevice(device.id)" />
          <span class="plugin-name">{{ device.deviceName }}</span>
        </div>
        <code>{{ device.code }}</code>
        <span>{{ device.processName }}（{{ device.processCode }}）</span>
      </button>
    </div>

    <div v-if="plan" class="binding-plan">
      <h4>已确认安装计划（只读）</h4>
      <dl>
        <dt>Host</dt><dd>{{ plan.hostVersion }}</dd>
        <dt>通道 / 架构</dt><dd>{{ plan.channel }} / {{ plan.targetRuntime }}</dd>
        <dt>目标框架</dt><dd>{{ plan.targetFramework || '未声明' }}</dd>
        <template v-for="item in plan.devices" :key="item.deviceId">
          <dt>{{ item.deviceName }}</dt>
          <dd>{{ item.clientCode }} · {{ item.moduleId }}@{{ item.pluginVersion }}</dd>
        </template>
        <dt>计划指纹</dt><dd><code>{{ plan.planFingerprint }}</code></dd>
      </dl>
    </div>

    <div class="binding-actions">
      <span v-if="validationHint" class="binding-hint">{{ validationHint }}</span>
      <UiButton :disabled="!canPlan || planning || generating" @click="loadPlan">
        {{ planning ? '计算中...' : '生成只读计划' }}
      </UiButton>
      <UiButton type="primary" :disabled="!plan || generating" @click="generate">
        <Download :size="15" />
        {{ generating ? '生成中...' : '确认并下载' }}
      </UiButton>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { Download } from 'lucide-vue-next';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiInput from '../../components/ui/UiInput.vue';
import { getScopedDeviceSelectApi, type ScopedDeviceSelectDto } from '../devices/api';
import {
  generateEdgeInstallerPackageApi,
  getEdgeInstallerPlanApi,
  type EdgeInstallerPlanDto,
} from './api';
import { notifySuccess, requestConfirmation } from '../../utils/feedback';

const baseUrl = ref(typeof window === 'undefined' ? '' : window.location.origin);
const devices = ref<ScopedDeviceSelectDto[]>([]);
const selectedIds = ref<string[]>([]);
const keyword = ref('');
const plan = ref<EdgeInstallerPlanDto | null>(null);
const planning = ref(false);
const generating = ref(false);

const filteredDevices = computed(() => {
  const value = keyword.value.trim().toLowerCase();
  if (!value) return devices.value;
  return devices.value.filter((device) =>
    [device.deviceName, device.code, device.processName, device.processCode]
      .some((item) => item.toLowerCase().includes(value)));
});
const validationHint = computed(() => {
  if (!baseUrl.value.trim()) return '请填写云端地址';
  if (!selectedIds.value.length) return '请至少选择一个设备插件';
  return '';
});
const canPlan = computed(() => !validationHint.value);

function toggleDevice(deviceId: string) {
  selectedIds.value = selectedIds.value.includes(deviceId)
    ? selectedIds.value.filter((id) => id !== deviceId)
    : [...selectedIds.value, deviceId];
}

async function loadPlan() {
  if (!canPlan.value || planning.value) return;
  planning.value = true;
  try {
    plan.value = await getEdgeInstallerPlanApi(selectedIds.value);
  } finally {
    planning.value = false;
  }
}

function downloadBlob(filename: string, blob: Blob) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

async function generate() {
  const currentPlan = plan.value;
  if (!currentPlan || generating.value) return;
  const confirmed = await requestConfirmation({
    type: 'warning',
    title: '确认生成首装包',
    message: '服务端将重新计算计划，与当前指纹不一致时会阻断。',
    details: [
      `设备：${currentPlan.devices.map((item) => `${item.deviceName}（${item.clientCode}）`).join('、')}`,
      `Host：${currentPlan.hostVersion} · ${currentPlan.channel} · ${currentPlan.targetRuntime}`,
      '文件安装是整组原子事务，设备凭据在真实 ready 后分别激活。',
    ],
    confirmText: '确认生成',
    cancelText: '取消',
  });
  if (!confirmed) return;

  generating.value = true;
  try {
    const installer = await generateEdgeInstallerPackageApi({
      deviceIds: currentPlan.devices.map((item) => item.deviceId),
      planFingerprint: currentPlan.planFingerprint,
      baseUrl: baseUrl.value.trim(),
    });
    downloadBlob(installer.fileName, installer.blob);
    notifySuccess(`首装包已生成，记录号 ${installer.generationId}，浏览器正在下载。`, {
      title: '生成完成',
    });
  } finally {
    generating.value = false;
  }
}

watch(selectedIds, () => { plan.value = null; }, { deep: true });
onMounted(async () => {
  devices.value = await getScopedDeviceSelectApi().catch(() => []);
});
</script>
