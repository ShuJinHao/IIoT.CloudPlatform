import { describe, expect, it } from 'vitest';

// 旧「上位机 PLC 状态」列表与「设备客户端状态」inventory 视图已随统一主视图退役：
// 旧 API、旧路由与旧 feature 目录不得再有前端消费者（后端旧接口本轮保留，由 C2 后端清理）。
// 用 import.meta.glob 读取源码文本，避免引入 node 类型。
// 数组 glob 模式下 eager raw 的 key 会带上 ?raw query，统一按去掉 query 后的路径匹配。
const rawSources = import.meta.glob(['../../*.ts', '../../**/*.ts', '../../**/*.vue'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;
const sources = new Map(
  Object.entries(rawSources).map(([key, value]) => [key.split('?')[0]!, value]),
);

const featureSource = (path: string) => {
  // glob key 可能是 '../../x'（src 根）、'../client-releases/x'（吞掉 features 段）
  // 或 './api.ts'（本目录文件），统一按去掉 query 与目录前缀后的相对路径匹配。
  const key = [...sources.keys()].find((name) => {
    const normalized = name.replace(/^(\.\.?\/)+/, '').replace(/^features\//, '');
    const target = path.replace(/^features\//, '');
    return normalized === target;
  });
  if (!key) throw new Error(`source not found: ${path}`);
  return sources.get(key)!;
};

describe('旧 device-inventory 与旧 edge-host 列表消费者归零', () => {
  it('client-releases api.ts 不再消费 device-inventory 或 inventory DTO', () => {
    const api = featureSource('features/client-releases/api.ts');
    expect(api).not.toContain('device-inventory');
    expect(api).not.toContain('DeviceClientVersionInventory');
    expect(api).not.toContain('getDeviceClientVersionInventoryApi');
  });

  it('client-releases useClientReleases 不再加载 inventory', () => {
    const composable = featureSource('features/client-releases/useClientReleases.ts');
    expect(composable).not.toContain('inventory');
    expect(composable).not.toContain('getDeviceClientVersionInventoryApi');
  });

  it('路由与布局不再引用旧 edge-hosts 页面', () => {
    const router = featureSource('router/index.ts');
    expect(router).not.toContain('edge-hosts');
    expect(router).not.toContain('edgeHostRoutes');
    const layout = featureSource('layout/MainLayout.vue');
    expect(layout).not.toContain('edge-hosts');
    expect(layout).not.toContain("name: 'EdgeHosts'");
  });

  it('旧 edge-hosts feature 目录已物理删除', () => {
    const leftovers = [...sources.keys()].filter((name) => name.includes('features/edge-hosts/'));
    expect(leftovers).toEqual([]);
  });

  it('PLC 专属接口只在统一主视图 feature 消费，旧 edge-host 列表接口无人调用', () => {
    // 全部已加载源码中不得再出现旧 edge-host 列表 API、旧路由或旧列表 DTO。
    const all = [...sources.values()].join('\n');
    expect(all).not.toContain('getEdgeHostPagedListApi');
    expect(all).not.toContain('getEdgeHostDetailApi');
    expect(all).not.toContain('EdgeHostListItemDto');
    expect(all).not.toContain("path: 'edge-hosts'");
    // 新 feature 只保留 PLC 专属详情接口，不重建旧 edge-hosts 列表基地址。
    const hits = [...sources.entries()].filter(([key, text]) =>
      !key.includes('legacy-consumers.test') && text.includes("'/human/edge-hosts'"));
    expect(hits.map(([key]) => key)).toEqual([]);
  });
});
