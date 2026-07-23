<template>
  <NiondDataPage class="release-page" :title="pageTitle" :subtitle="pageSubtitle">
    <template #actions>
      <template v-if="isPublishRoute">
        <UiButton size="small" secondary @click="goInstallerCenter"><Boxes :size="15" />首装生成</UiButton>
      </template>
      <UiButton v-else-if="canManageReleases" type="info" size="small" secondary @click="goPublishManager"><Settings2 :size="15" />发布管理</UiButton>
    </template>
    <template #toolbar>
      <ClientReleaseToolbar v-model:active-view="activeView" :is-publish-route="isPublishRoute" :can-generate-installer="canGenerateInstaller" @refresh="refresh" />
    </template>

    <template v-if="isPublishRoute || activeView === 'catalog'">
      <ReleaseCatalogSection :columns="releaseCatalogColumns" :rows="releaseCatalogRows" :loading="loadingCatalog" />
      <ReleaseDeletionsSection
        v-if="isPublishRoute && canHardDelete"
        :items="deletions"
        :loading="loadingDeletions"
        :retrying-id="retryingDeletionId"
        @retry="retryDeletion"
      />
      <ReleaseHistorySection
        :items="historyItems"
        :total="historyTotal"
        :page="historyPage"
        :page-size="historyPageSize"
        :loading="loadingHistory"
        :error="historyError"
        @update:page="gotoHistoryPage"
        @retry="fetchHistory"
      />
    </template>
    <ReleaseInventorySection v-else-if="activeView === 'inventory'" :columns="inventoryColumns" :inventory="inventory" :loading="loadingInventory" />
    <NiondTableCard v-else>
      <EdgeBindingDownloadPanel :plugin-components="catalog?.plugins ?? []" :channel="channelDisplay" :target-runtime="targetRuntime || 'win-x64'" :host-version="selectedHostPackageVersion" />
    </NiondTableCard>

    <ReleaseHistoryModal v-model:show="showHistoryModal" :title="historyModalTitle" :selected-row="selectedReleaseRow" :versions="selectedOtherVersions" :columns="historyColumns" />
    <ReleaseDetailModal v-model:show="showReleaseDetailModal" :detail="selectedReleaseDetail" />
    <ReleaseHardDeleteModal
      v-model:show="showHardDeleteModal"
      v-model:confirm-text="hardDeleteConfirmText"
      v-model:reason="hardDeleteReason"
      :target="hardDeleteTarget"
      :submitting="submitting"
      :problem="hardDeleteProblem"
      @cancel="closeHardDeleteModal"
      @submit="submitHardDelete"
    />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Boxes, Settings2 } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import UiButton from '../../components/ui/UiButton.vue';
import ClientReleaseToolbar from './ClientReleaseToolbar.vue';
import EdgeBindingDownloadPanel from './EdgeBindingDownloadPanel.vue';
import ReleaseCatalogSection from './ReleaseCatalogSection.vue';
import ReleaseDeletionsSection from './ReleaseDeletionsSection.vue';
import ReleaseDetailModal from './ReleaseDetailModal.vue';
import ReleaseHardDeleteModal from './ReleaseHardDeleteModal.vue';
import ReleaseHistoryModal from './ReleaseHistoryModal.vue';
import ReleaseHistorySection from './ReleaseHistorySection.vue';
import ReleaseInventorySection from './ReleaseInventorySection.vue';
import { useClientReleases } from './useClientReleases';
import './client-release-page.css';

const state = useClientReleases();
const {
  targetRuntime, catalog, inventory, loadingCatalog, loadingInventory, submitting,
  showHistoryModal, showReleaseDetailModal, selectedReleaseRow, selectedReleaseDetail,
  historyItems, historyTotal, historyPage, historyPageSize, loadingHistory, historyError,
  showHardDeleteModal, hardDeleteTarget, hardDeleteConfirmText, hardDeleteReason, hardDeleteProblem,
  deletions, loadingDeletions, retryingDeletionId,
  canGenerateInstaller, canManageReleases, canHardDelete, activeView, isPublishRoute,
  pageTitle, pageSubtitle, channelDisplay, selectedHostPackageVersion,
  releaseCatalogRows, selectedOtherVersions, historyModalTitle, releaseCatalogColumns,
  historyColumns, inventoryColumns,
  refresh, fetchHistory, gotoHistoryPage, goPublishManager, goInstallerCenter,
  closeHardDeleteModal, submitHardDelete, retryDeletion,
} = state;

onMounted(refresh);
</script>
