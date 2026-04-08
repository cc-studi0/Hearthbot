# 云控前端优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除云控面板白屏等待，并将对局记录页从账号筛选+表格改为设备列视图

**Architecture:** 后端新增 `by-device` 聚合接口一次返回所有设备最近20场记录；前端 GameRecords 页完全重写为设备列横向布局；Dashboard 和 GameRecords 均添加骨架屏消除白屏

**Tech Stack:** ASP.NET Core 8 / EF Core / SQLite / Vue 3 Composition API / naive-ui / SignalR

---

### Task 1: 后端新增 by-device 接口

**Files:**
- Modify: `HearthBot.Cloud/Controllers/GameRecordController.cs:63-141` (在文件末尾现有方法后添加)

- [ ] **Step 1: 在 GameRecordController 添加 by-device 端点**

在 `GetStats` 方法之后、类的闭合大括号之前，添加：

```csharp
[HttpGet("by-device")]
public async Task<IActionResult> GetByDevice()
{
    var devices = await _db.Devices
        .OrderBy(d => d.DisplayName)
        .ToListAsync();

    var result = new List<object>();

    foreach (var device in devices)
    {
        var records = await _db.GameRecords
            .Where(g => g.DeviceId == device.DeviceId)
            .OrderByDescending(g => g.PlayedAt)
            .Take(20)
            .Select(g => new
            {
                g.Result,
                g.OpponentClass,
                g.DeckName,
                g.DurationSeconds,
                g.RankBefore,
                g.RankAfter,
                g.PlayedAt
            })
            .ToListAsync();

        var wins = records.Count(r => r.Result == "Win");
        var losses = records.Count(r => r.Result == "Loss");
        var concedes = records.Count(r => r.Result == "Concede");
        var total = records.Count;
        var winRate = total > 0 ? Math.Round(wins * 100.0 / total, 1) : 0;

        result.Add(new
        {
            device.DeviceId,
            device.DisplayName,
            device.CurrentAccount,
            device.CurrentRank,
            TotalGames = total,
            Wins = wins,
            Losses = losses,
            Concedes = concedes,
            WinRate = winRate,
            Records = records
        });
    }

    return Ok(result);
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/GameRecordController.cs
git commit -m "feat(cloud): 新增 by-device 接口，返回所有设备最近20场记录"
```

---

### Task 2: 前端 API 层添加 byDevice 调用

**Files:**
- Modify: `hearthbot-web/src/api/index.ts:35-39` (gameRecordApi 对象)

- [ ] **Step 1: 在 gameRecordApi 中添加 byDevice 方法**

在 `gameRecordApi` 对象中，在 `getStats` 之后添加一行：

```typescript
export const gameRecordApi = {
  getAll: (params: Record<string, any>) => api.get('/gamerecord', { params }),
  getAccounts: (params?: Record<string, any>) => api.get<string[]>('/gamerecord/accounts', { params }),
  getStats: (params: Record<string, any>) => api.get('/gamerecord/stats', { params }),
  byDevice: () => api.get('/gamerecord/by-device')
}
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/api/index.ts
git commit -m "feat(web): API 层添加 byDevice 调用"
```

---

### Task 3: Dashboard 添加骨架屏

**Files:**
- Modify: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: 添加 loading 状态和 NSkeleton 导入**

在 `<script setup>` 顶部的 naive-ui 导入中添加 `NSkeleton`：

```typescript
import {
  NLayout, NLayoutHeader, NLayoutContent, NSpace, NCard, NStatistic,
  NDataTable, NTag, NButton, NModal, NSelect, NGrid, NGi, NMenu, NInput,
  NSkeleton
} from 'naive-ui'
```

在现有 ref 声明区域（`isLoading` 附近）添加：

```typescript
const firstLoad = ref(true)
```

在 `loadData` 函数的 finally 块中，在 `isLoading.value = false` 之后添加：

```typescript
async function loadData() {
  if (isLoading.value) return
  isLoading.value = true
  try {
    const [devRes, statRes] = await Promise.all([deviceApi.getAll(), deviceApi.getStats()])
    devices.value = devRes.data
    stats.value = statRes.data
  } finally {
    isLoading.value = false
    firstLoad.value = false
  }
}
```

- [ ] **Step 2: 在模板中添加骨架屏**

在 `<template>` 中，将统计卡片区域和设备表格区域包裹在 `v-if/v-else` 中：

将当前的统计卡片 NGrid（`<NGrid :cols="4" ...>`）替换为：

```html
<!-- 统计卡片骨架屏 -->
<NGrid :cols="4" :x-gap="12" style="margin-bottom:24px" v-if="firstLoad">
  <NGi v-for="i in 4" :key="i">
    <NCard><NSkeleton text :repeat="2" /></NCard>
  </NGi>
</NGrid>
<NGrid :cols="4" :x-gap="12" style="margin-bottom:24px" v-else>
  <NGi>
    <NCard>
      <NStatistic label="在线设备" :value="stats.onlineCount">
        <template #suffix>/ {{ stats.totalCount }}</template>
      </NStatistic>
    </NCard>
  </NGi>
  <NGi>
    <NCard><NStatistic label="今日胜率" :value="todayWinRate" /></NCard>
  </NGi>
  <NGi>
    <NCard><NStatistic label="今日对局" :value="stats.todayGames" /></NCard>
  </NGi>
  <NGi>
    <NCard><NStatistic label="异常设备" :value="stats.abnormalCount" /></NCard>
  </NGi>
</NGrid>
```

将当前的设备表格 NCard（`<NCard title="设备实时状态">`）替换为：

```html
<!-- 设备表格骨架屏 -->
<NCard title="设备实时状态" v-if="firstLoad">
  <NSkeleton text :repeat="8" />
</NCard>
<NCard title="设备实时状态" v-else>
  <NDataTable :columns="columns" :data="devices" :row-key="(r: Device) => r.deviceId" />
</NCard>
```

- [ ] **Step 3: 验证前端编译**

Run: `cd hearthbot-web && npx vite build`
Expected: 编译成功，无错误

- [ ] **Step 4: Commit**

```bash
git add hearthbot-web/src/views/Dashboard.vue
git commit -m "feat(web): Dashboard 添加骨架屏消除白屏等待"
```

---

### Task 4: GameRecords 页完全重写

**Files:**
- Modify: `hearthbot-web/src/views/GameRecords.vue` (完全重写)

- [ ] **Step 1: 重写 GameRecords.vue**

用以下内容完全替换文件：

```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NSpace,
  NButton, NMenu, NSkeleton
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi } from '../api'
import { useAuth } from '../composables/useAuth'
import { useSignalR } from '../composables/useSignalR'

interface DeviceRecord {
  result: string
  opponentClass: string
  deckName: string
  durationSeconds: number
  rankBefore: string
  rankAfter: string
  playedAt: string
}

interface DeviceColumn {
  deviceId: string
  displayName: string
  currentAccount: string
  currentRank: string
  totalGames: number
  wins: number
  losses: number
  concedes: number
  winRate: number
  records: DeviceRecord[]
}

const router = useRouter()
const { logout } = useAuth()
const deviceColumns = ref<DeviceColumn[]>([])
const loading = ref(true)

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#63e2b7'
  if (rate >= 45) return '#f2c97d'
  return '#e06262'
}

function resultLabel(r: string): { text: string; color: string } {
  if (r === 'Win') return { text: 'W', color: '#63e2b7' }
  if (r === 'Loss') return { text: 'L', color: '#e06262' }
  return { text: 'C', color: '#f2c97d' }
}

function rankChanged(rec: DeviceRecord): string | null {
  if (!rec.rankBefore || !rec.rankAfter || rec.rankBefore === rec.rankAfter) return null
  return `${rec.rankBefore}→${rec.rankAfter}`
}

async function loadData() {
  const { data } = await gameRecordApi.byDevice()
  deviceColumns.value = data
  loading.value = false
}

onMounted(async () => {
  await loadData()

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => loadData())
})

const menuOptions = [
  { label: '总览', key: '/' },
  { label: '对局记录', key: '/records' }
]
</script>

<template>
  <NLayout style="min-height:100vh">
    <NLayoutHeader bordered style="padding:0 24px;display:flex;align-items:center;justify-content:space-between;height:56px">
      <NSpace align="center">
        <strong style="font-size:16px;color:#63e2b7">HearthBot 云控</strong>
        <NMenu mode="horizontal" :options="menuOptions" :value="router.currentRoute.value.path"
          @update:value="(k: string) => router.push(k)" />
      </NSpace>
      <NButton text @click="logout">退出</NButton>
    </NLayoutHeader>

    <NLayoutContent style="padding:24px">
      <NCard title="设备战绩">
        <!-- 骨架屏 -->
        <div v-if="loading" style="display:flex;gap:16px;">
          <div v-for="i in 4" :key="i" style="flex:1;">
            <NSkeleton text :repeat="12" />
          </div>
        </div>

        <!-- 设备列视图 -->
        <div v-else-if="deviceColumns.length > 0" style="display:flex;overflow-x:auto;">
          <div
            v-for="(dev, idx) in deviceColumns"
            :key="dev.deviceId"
            :style="{
              flex: '1',
              minWidth: '180px',
              padding: '0 12px',
              borderRight: idx < deviceColumns.length - 1 ? '1px solid #333' : 'none'
            }"
          >
            <!-- 设备头部 -->
            <div style="margin-bottom:12px;">
              <div style="font-weight:bold;color:#63e2b7;font-size:14px;">{{ dev.displayName }}</div>
              <div style="color:#999;font-size:12px;">{{ dev.currentAccount }} · {{ dev.currentRank }}</div>
              <div :style="{ fontWeight: 'bold', fontSize: '13px', color: winRateColor(dev.winRate) }">
                {{ dev.wins }}W {{ dev.losses }}L ({{ dev.winRate }}%)
              </div>
            </div>

            <!-- 对局列表 -->
            <div style="display:flex;flex-direction:column;gap:3px;font-size:12px;">
              <div
                v-for="(rec, ri) in dev.records"
                :key="ri"
                style="display:flex;align-items:center;gap:4px;line-height:1.6;"
              >
                <span :style="{ color: resultLabel(rec.result).color, fontWeight: 'bold', width: '16px' }">
                  {{ resultLabel(rec.result).text }}
                </span>
                <span style="color:#ccc;">{{ rec.opponentClass }}</span>
                <span style="color:#888;font-size:11px;">{{ rec.deckName }}</span>
                <span style="color:#666;font-size:11px;">{{ formatDuration(rec.durationSeconds) }}</span>
                <span v-if="rankChanged(rec)" style="color:#7ec8e3;font-size:11px;">{{ rankChanged(rec) }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 无数据 -->
        <div v-else style="color:#999;text-align:center;padding:60px;">
          暂无设备数据
        </div>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
```

- [ ] **Step 2: 验证前端编译**

Run: `cd hearthbot-web && npx vite build`
Expected: 编译成功，无错误

- [ ] **Step 3: Commit**

```bash
git add hearthbot-web/src/views/GameRecords.vue
git commit -m "feat(web): 对局记录页改版为设备列视图"
```

---

### Task 5: 构建前端并部署验证

**Files:**
- 构建输出到: `HearthBot.Cloud/wwwroot/`

- [ ] **Step 1: 构建前端**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功，输出到 `../HearthBot.Cloud/wwwroot/`

- [ ] **Step 2: Commit 构建产物**

```bash
git add HearthBot.Cloud/wwwroot/
git commit -m "build: 重新构建前端资源"
```

- [ ] **Step 3: 部署到服务器验证**

Run: `powershell -File deploy_cloud.ps1`
Expected: 部署成功，服务启动

- [ ] **Step 4: 在浏览器中验证**

1. 打开 `http://70.39.201.9:5000`
2. 验证 Dashboard：页面加载时应显示骨架屏，数据到达后替换为真实内容
3. 验证对局记录页：应显示设备列视图，每台设备一列，竖线分隔，最近20场记录
