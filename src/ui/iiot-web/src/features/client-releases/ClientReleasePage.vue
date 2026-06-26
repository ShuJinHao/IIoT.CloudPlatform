<template>
  <NiondDataPage class="release-page" :title="pageTitle" :subtitle="pageSubtitle">
    <template #actions>
      <template v-if="isPublishRoute">
        <UiButton size="small" secondary @click="goInstallerCenter"><Boxes :size="15" />首装生成</UiButton>
        <UiButton v-if="canManageReleases" type="primary" size="small" @click="openHostModal"><Plus :size="15" />登记宿主</UiButton>
        <UiButton v-if="canManageReleases" type="info" size="small" secondary @click="openPluginModal"><PackagePlus :size="15" />登记插件</UiButton>
      </template>
      <UiButton v-else-if="canManageReleases" type="info" size="small" secondary @click="goPublishManager"><Settings2 :size="15" />发布管理</UiButton>
    </template>
    <template #toolbar>
      <ClientReleaseToolbar v-model:active-view="activeView" :is-publish-route="isPublishRoute" :can-generate-installer="canGenerateInstaller" @refresh="refresh" />
    </template>

    <ReleaseCatalogSection v-if="isPublishRoute || activeView === 'catalog'" :columns="releaseCatalogColumns" :rows="releaseCatalogRows" :loading="loadingCatalog" />
    <ReleaseInventorySection v-else-if="activeView === 'inventory'" :columns="inventoryColumns" :inventory="inventory" :loading="loadingInventory" />
    <NiondTableCard v-else>
      <EdgeBindingDownloadPanel :plugin-components="catalog?.plugins ?? []" :channel="channelDisplay" :target-runtime="targetRuntime || 'win-x64'" :host-version="selectedHostPackageVersion" />
    </NiondTableCard>

    <ReleaseHistoryModal v-model:show="showHistoryModal" :title="historyModalTitle" :selected-row="selectedReleaseRow" :versions="selectedHistoryVersions" :columns="historyColumns" />
    <ReleaseDetailModal v-model:show="showReleaseDetailModal" :detail="selectedReleaseDetail" />
    <ReleaseHostModal v-model:show="showHostModal" :form="hostForm" :submitting="submitting" @submit="submitHostRelease" />
    <ReleasePluginModal v-model:show="showPluginModal" :form="pluginForm" :submitting="submitting" @submit="submitPluginRelease" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Boxes, PackagePlus, Plus, Settings2 } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import UiButton from '../../components/ui/UiButton.vue';
import ClientReleaseToolbar from './ClientReleaseToolbar.vue';
import EdgeBindingDownloadPanel from './EdgeBindingDownloadPanel.vue';
import ReleaseCatalogSection from './ReleaseCatalogSection.vue';
import ReleaseDetailModal from './ReleaseDetailModal.vue';
import ReleaseHistoryModal from './ReleaseHistoryModal.vue';
import ReleaseHostModal from './ReleaseHostModal.vue';
import ReleaseInventorySection from './ReleaseInventorySection.vue';
import ReleasePluginModal from './ReleasePluginModal.vue';
import { useClientReleases } from './useClientReleases';
import './client-release-page.css';

const state = useClientReleases();
const {
  targetRuntime, catalog, inventory, loadingCatalog, loadingInventory, submitting, showHostModal, showPluginModal,
  showHistoryModal, showReleaseDetailModal, selectedReleaseRow, selectedReleaseDetail, canGenerateInstaller,
  canManageReleases, activeView, isPublishRoute, pageTitle, pageSubtitle, channelDisplay, selectedHostPackageVersion,
  hostForm, pluginForm, releaseCatalogRows, selectedHistoryVersions, historyModalTitle, releaseCatalogColumns,
  historyColumns, inventoryColumns, refresh, goPublishManager, goInstallerCenter, openHostModal, openPluginModal,
  submitHostRelease, submitPluginRelease,
} = state;

onMounted(refresh);
</script>
