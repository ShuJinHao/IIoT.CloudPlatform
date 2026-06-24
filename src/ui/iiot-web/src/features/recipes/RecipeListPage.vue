<template>
  <NiondDataPage class="recipe-page" page-key="recipes" title="配方管理" subtitle="管理生产设备配方和版本历史，支持按设备查看、升级和删除">
    <template #actions>
      <UiButton type="primary" v-permission="'Recipe.Create'" @click="openCreateModal">
        <template #icon><Plus :size="14" /></template>
        新建配方
      </UiButton>
    </template>
    <template #toolbar>
      <NiondToolbar>
        <div class="filter-row">
          <UiInput v-model:value="keyword" placeholder="搜索配方名称..." clearable size="small" style="max-width: 320px;" @input="onSearchInput" @keyup.enter="fetchList" @clear="onClearKeyword">
            <template #prefix><Search :size="14" /></template>
          </UiInput>
          <UiTag round :bordered="false" size="small">共 {{ metaData.totalCount }} 条</UiTag>
        </div>
      </NiondToolbar>
    </template>

    <NiondTableCard class="recipe-page__table-card">
      <UiDataTable class="recipe-page__table" :columns="columns" :data="recipes" :loading="loading" :bordered="false" :single-line="false" :row-key="rowKey" size="small">
        <template #empty><EmptyState title="未找到配方" description="没有任何配方或不满足当前搜索条件。" /></template>
      </UiDataTable>
      <div v-if="metaData.totalPages > 1" class="pagination-wrap">
        <UiPagination :page="currentPage" :page-count="metaData.totalPages" :item-count="metaData.totalCount" :page-size="10" show-quick-jumper @update:page="onPageChange" />
      </div>
    </NiondTableCard>

    <RecipeCreateModal v-model:show="showCreateModal" v-model:params="createParams" :form="createForm" :process-options="processOptions" :device-options="deviceOptions" :submitting="submitting" @submit="submitCreate" />
    <RecipeUpgradeModal v-model:show="showUpgradeModal" v-model:params="upgradeParams" :target="upgradeTarget" :form="upgradeForm" :submitting="submitting" @submit="submitUpgrade" />
    <RecipeDetailDrawer v-model:show="showDetailPanel" :detail="detailData" :detail-params="detailParams" :loading="detailLoading" :process-name-map="processNameMap" :device-name-map="deviceNameMap" :pretty-json="prettyJson" />
    <RecipeDeleteConfirm v-model:show="confirmDialog.show" :dialog="confirmDialog" :submitting="submitting" />
  </NiondDataPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { Plus, Search } from 'lucide-vue-next';
import NiondDataPage from '../../components/layout/NiondDataPage.vue';
import NiondTableCard from '../../components/layout/NiondTableCard.vue';
import NiondToolbar from '../../components/layout/NiondToolbar.vue';
import EmptyState from '../../components/states/EmptyState.vue';
import UiButton from '../../components/ui/UiButton.vue';
import UiDataTable from '../../components/ui/UiDataTable.vue';
import UiInput from '../../components/ui/UiInput.vue';
import UiPagination from '../../components/ui/UiPagination.vue';
import UiTag from '../../components/ui/UiTag.vue';
import { createRecipeColumns } from './columns';
import RecipeCreateModal from './RecipeCreateModal.vue';
import RecipeDeleteConfirm from './RecipeDeleteConfirm.vue';
import RecipeDetailDrawer from './RecipeDetailDrawer.vue';
import RecipeUpgradeModal from './RecipeUpgradeModal.vue';
import type { RecipeListItemDto } from './types';
import { useRecipes } from './useRecipes';
import './recipe-page.css';

const recipeState = useRecipes();
const columns = createRecipeColumns({
  processNameMap: () => recipeState.processNameMap.value,
  deviceNameMap: () => recipeState.deviceNameMap.value,
  canUpdateRecipe: () => recipeState.canUpdateRecipe.value,
  onDetail: recipeState.openDetailPanel,
  onUpgrade: recipeState.openUpgradeModal,
  onDelete: recipeState.handleDelete,
});
const rowKey = (row: RecipeListItemDto) => row.id;

const {
  recipes, loading, keyword, currentPage, metaData, submitting, processNameMap, deviceNameMap, processOptions,
  deviceOptions, showCreateModal, createForm, createParams, showUpgradeModal, upgradeTarget, upgradeForm,
  upgradeParams, showDetailPanel, detailLoading, detailData, detailParams, confirmDialog, initialize, fetchList,
  onSearchInput, onClearKeyword, onPageChange, openCreateModal, submitCreate, submitUpgrade, prettyJson,
} = recipeState;

onMounted(initialize);
</script>
