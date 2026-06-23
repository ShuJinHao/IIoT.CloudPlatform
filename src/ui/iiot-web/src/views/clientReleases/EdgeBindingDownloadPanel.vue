<template>
  <div class="binding-panel">
    <div class="binding-intro">
      <h3>客户端首装包生成</h3>
      <p>
        勾选现场机器首次部署或重装需要的工序插件，给每个插件指定云端已注册设备，
        云端会生成写入设备身份的一次性安装包。
      </p>
      <p class="binding-warn">
        注意：生成会为所选设备写入新的启动密钥（旧的随之失效）。后续客户端更新由现场 Launcher 自动检测和执行，不从网页更新。
      </p>
    </div>

    <div class="binding-field">
      <label>云端地址（必填）</label>
      <UiInput
        v-model:value="baseUrl"
        size="small"
        placeholder="例如 http://10.98.90.154:81"
      />
    </div>

    <div v-if="plugins.length" class="plugin-list">
      <div
        v-for="plugin in plugins"
        :key="plugin.moduleId"
        class="plugin-item"
        :class="{ 'is-checked': isChecked(plugin.moduleId) }"
      >
        <div class="plugin-head">
          <UiCheckbox
            :checked="isChecked(plugin.moduleId)"
            @update:checked="toggle(plugin.moduleId)"
          />
          <span class="plugin-name" @click="toggle(plugin.moduleId)">{{ plugin.displayName }}</span>
        </div>
        <button
          v-if="isChecked(plugin.moduleId)"
          type="button"
          class="device-pick"
          :class="{ 'is-empty': !choiceOf(plugin.moduleId).deviceId }"
          @click="openPicker(plugin.moduleId)"
        >
          <template v-if="choiceOf(plugin.moduleId).deviceId">
            <strong>{{ choiceOf(plugin.moduleId).deviceName }}</strong>
            <code>{{ choiceOf(plugin.moduleId).clientCode }}</code>
          </template>
          <span v-else>选择设备</span>
        </button>
      </div>
    </div>
    <p v-else class="binding-empty">暂无可用插件，请先在发布区登记插件版本。</p>

    <div class="binding-actions">
      <span v-if="validationHint" class="binding-hint">{{ validationHint }}</span>
      <UiButton type="primary" :disabled="!canGenerate || generating" @click="generate">
        <Download :size="15" />
        {{ generating ? '生成中…' : '生成首装包' }}
      </UiButton>
    </div>

    <UiModal v-model:show="showPicker" preset="card" title="选择设备" style="width: 640px;">
      <div class="picker">
        <UiInput v-model:value="pickerKeyword" size="small" placeholder="搜索设备名称或 Code" clearable />
        <div class="picker-list">
          <p v-if="!devices.length" class="picker-empty">
            暂无可选设备，请先在「设备管理」注册设备。
          </p>
          <p v-else-if="!filteredDevices.length" class="picker-empty">没有匹配的设备。</p>
          <button
            v-for="device in filteredDevices"
            :key="device.id"
            type="button"
            class="picker-item"
            :class="{ 'is-used': isDeviceUsed(device.id) }"
            :disabled="isDeviceUsed(device.id)"
            @click="selectDevice(device)"
          >
            <strong>{{ device.deviceName }}</strong>
            <code>{{ device.code }}</code>
            <span v-if="isDeviceUsed(device.id)" class="picker-used-tag">已被其它插件占用</span>
          </button>
        </div>
      </div>
      <template #footer>
        <UiButton @click="showPicker = false">关闭</UiButton>
      </template>
    </UiModal>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { Download } from 'lucide-vue-next';
import UiButton from '../../components/ui/UiButton.vue';
import UiCheckbox from '../../components/ui/UiCheckbox.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiModal from '../../components/ui/UiModal.vue';
import { getScopedDeviceSelectApi, type DeviceSelectDto } from '../../api/device';
import { generateEdgeInstallerPackageApi, type ClientPluginReleaseComponentDto } from '../../api/clientRelease';
import { notifySuccess, requestConfirmation } from '../../utils/feedback';

const props = defineProps<{
  pluginComponents: ClientPluginReleaseComponentDto[];
  channel?: string;
  targetRuntime?: string;
  hostVersion?: string | null;
}>();

interface BindingChoice {
  deviceId: string;
  deviceName: string;
  clientCode: string;
}

// key = moduleId（勾选即为启用），value = 绑定的设备
const selections = reactive<Record<string, BindingChoice>>({});
const resolveDefaultBaseUrl = () => {
  if (typeof window === 'undefined') {
    return '';
  }
  return window.location.origin;
};

const baseUrl = ref(resolveDefaultBaseUrl());
const devices = ref<DeviceSelectDto[]>([]);
const showPicker = ref(false);
const pickerModuleId = ref('');
const pickerKeyword = ref('');
const generating = ref(false);

// 可选插件清单：按 moduleId 去重
const plugins = computed(() => {
  const result: Array<{ moduleId: string; displayName: string }> = [];
  for (const plugin of props.pluginComponents) {
    if (!plugin.versions.some((version) => version.status.toLowerCase() === 'published')) {
      continue;
    }
    result.push({
      moduleId: plugin.moduleId,
      displayName: plugin.displayName || plugin.moduleId,
    });
  }
  return result;
});

const validPluginIds = computed(() => new Set(plugins.value.map((plugin) => plugin.moduleId)));
const activeSelections = computed(() =>
  plugins.value
    .map((plugin) => [plugin.moduleId, selections[plugin.moduleId]] as const)
    .filter((entry): entry is readonly [string, BindingChoice] => Boolean(entry[1])),
);

const isChecked = (moduleId: string) => moduleId in selections;

// 模板只读：勾选时返回真实（响应式）选择对象，未勾选时返回安全默认值
const choiceOf = (moduleId: string): BindingChoice =>
  selections[moduleId] ?? { deviceId: '', deviceName: '', clientCode: '' };

const toggle = (moduleId: string) => {
  if (moduleId in selections) {
    delete selections[moduleId];
  } else {
    selections[moduleId] = { deviceId: '', deviceName: '', clientCode: '' };
  }
};

const isDeviceUsed = (deviceId: string) => {
  for (const [moduleId, choice] of Object.entries(selections)) {
    if (moduleId !== pickerModuleId.value && choice.deviceId === deviceId) {
      return true;
    }
  }
  return false;
};

const filteredDevices = computed(() => {
  const keyword = pickerKeyword.value.trim().toLowerCase();
  if (!keyword) return devices.value;
  return devices.value.filter(
    (device) =>
      device.deviceName.toLowerCase().includes(keyword) ||
      device.code.toLowerCase().includes(keyword),
  );
});

const validationHint = computed(() => {
  if (!props.hostVersion) return '暂无已发布宿主版本';
  if (!baseUrl.value.trim()) return '请填写云端地址';
  const entries = activeSelections.value.map(([, choice]) => choice);
  if (entries.length === 0) return '请至少勾选一个插件';
  if (entries.some((choice) => !choice.deviceId)) return '有插件未指定设备';
  return '';
});

const canGenerate = computed(() => activeSelections.value.length > 0 && !validationHint.value);

watch(validPluginIds, (ids) => {
  for (const moduleId of Object.keys(selections)) {
    if (!ids.has(moduleId)) {
      delete selections[moduleId];
    }
  }
  if (pickerModuleId.value && !ids.has(pickerModuleId.value)) {
    pickerModuleId.value = '';
    showPicker.value = false;
  }
}, { immediate: true });

const openPicker = (moduleId: string) => {
  pickerModuleId.value = moduleId;
  pickerKeyword.value = '';
  showPicker.value = true;
};

const selectDevice = (device: DeviceSelectDto) => {
  const choice = selections[pickerModuleId.value];
  if (!choice) return;
  choice.deviceId = device.id;
  choice.deviceName = device.deviceName;
  choice.clientCode = device.code;
  showPicker.value = false;
};

const downloadBlob = (filename: string, blob: Blob) => {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
};

const generate = async () => {
  if (!canGenerate.value || generating.value) return;

  // 生成会为所选设备轮换启动密钥，旧密钥立即失效。强制二次确认，避免误操作打挂在线设备。
  const currentSelections = activeSelections.value;
  const deviceList = currentSelections
    .map(([, choice]) => choice)
    .map((choice) => `${choice.deviceName}（${choice.clientCode}）`)
    .filter(Boolean);
  const confirmed = await requestConfirmation({
    type: 'warning',
    title: '确认生成首装包',
    message: '生成后会为所选设备写入新的启动密钥，旧密钥立即失效。',
    details: [
      `设备：${deviceList.join('、')}`,
      '该文件仅用于首次部署或重装绑定，不作为客户端更新入口。',
      '请把最新生成的安装包部署到对应设备，否则旧客户端会无法继续 bootstrap 云端身份。',
    ],
    confirmText: '生成首装包',
    cancelText: '取消',
  });
  if (!confirmed) return;

  generating.value = true;
  try {
    const installer = await generateEdgeInstallerPackageApi({
      channel: props.channel || 'stable',
      targetRuntime: props.targetRuntime || 'win-x64',
      hostVersion: props.hostVersion || null,
      selections: currentSelections.map(([moduleId, choice]) => ({
        moduleId,
        deviceId: choice.deviceId,
      })),
      baseUrl: baseUrl.value.trim(),
    });
    downloadBlob(installer.fileName, installer.blob);
    notifySuccess('首装包已生成，浏览器正在下载。', {
      title: '生成完成',
    });
  } catch {
    // 业务错误已由 http 层统一弹窗提示，这里只需复位状态
  } finally {
    generating.value = false;
  }
};

onMounted(async () => {
  try {
    devices.value = await getScopedDeviceSelectApi();
  } catch {
    // 设备列表加载失败已由 http 层统一提示
  }
});
</script>

<style scoped>
.binding-panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: 22px 24px;
}

.binding-intro h3 {
  margin: 0 0 var(--space-1);
  font-size: var(--fs-xl);
  font-weight: var(--fw-semibold);
  color: var(--text-0);
}

.binding-intro p {
  margin: 0;
  font-size: var(--fs-sm);
  color: var(--text-2);
}

.binding-warn {
  margin-top: var(--space-1) !important;
  color: var(--text-1);
  font-weight: var(--fw-medium);
}

.binding-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  max-width: 480px;
}

.binding-field label {
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--text-1);
}

.plugin-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.plugin-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: 12px 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--card);
  transition: border-color 0.15s ease;
}

.plugin-item.is-checked {
  border-color: var(--brand);
}

.plugin-head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.plugin-name {
  font-size: var(--fs-base);
  font-weight: var(--fw-medium);
  color: var(--text-0);
  cursor: pointer;
}

.device-pick {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 240px;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg-3);
  color: var(--text-0);
  font-family: var(--font-sans);
  font-size: var(--fs-sm);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s ease;
}

.device-pick:hover {
  border-color: var(--brand);
}

.device-pick.is-empty {
  color: var(--text-2);
}

.device-pick code,
.picker-item code {
  padding: 2px 6px;
  border-radius: var(--radius-md);
  background: var(--bg-2);
  color: var(--brand);
  font-size: var(--fs-xs);
}

.binding-empty {
  margin: 0;
  padding: 24px;
  text-align: center;
  font-size: var(--fs-sm);
  color: var(--text-2);
  border: 1px dashed var(--border);
  border-radius: var(--radius-md);
}

.binding-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: var(--space-3);
}

.binding-hint {
  font-size: var(--fs-sm);
  color: var(--text-2);
}

.picker {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.picker-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  max-height: 360px;
  overflow: auto;
}

.picker-item {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--card);
  color: var(--text-0);
  font-family: var(--font-sans);
  font-size: var(--fs-sm);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s ease;
}

.picker-item:hover:not(:disabled) {
  border-color: var(--brand);
}

.picker-item.is-used {
  cursor: not-allowed;
  opacity: 0.5;
}

.picker-used-tag {
  margin-left: auto;
  font-size: var(--fs-xs);
  color: var(--text-2);
}

.picker-empty {
  margin: 0;
  font-size: var(--fs-sm);
  color: var(--text-2);
}
</style>
