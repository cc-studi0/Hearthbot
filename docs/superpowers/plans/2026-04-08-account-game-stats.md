# 账号级对局统计 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在云控对局记录页面增加按账号维度的统计汇总——基础胜负、职业对阵、段位曲线、每日趋势。

**Architecture:** 后端在 `GameRecordController` 新增两个 GET 端点（`/accounts`、`/stats`），用 LINQ 聚合查询直接返回 JSON。前端在 `GameRecords.vue` 顶部加账号选择器，选中后展示统计卡片和 Chart.js 图表，下方对局表格联动过滤。

**Tech Stack:** ASP.NET Core / EF Core (后端), Vue 3 / Naive UI / Chart.js + vue-chartjs (前端)

---

### Task 1: 后端 — 账号列表端点

**Files:**
- Modify: `HearthBot.Cloud/Controllers/GameRecordController.cs`

- [ ] **Step 1: 在 GameRecordController 末尾新增 GetAccounts 端点**

在 `GetAll` 方法之后添加：

```csharp
[HttpGet("accounts")]
public async Task<IActionResult> GetAccounts([FromQuery] string? deviceId)
{
    var query = _db.GameRecords.AsQueryable();
    if (!string.IsNullOrEmpty(deviceId))
        query = query.Where(g => g.DeviceId == deviceId);

    var accounts = await query
        .Where(g => g.AccountName != "")
        .Select(g => g.AccountName)
        .Distinct()
        .OrderBy(a => a)
        .ToListAsync();

    return Ok(accounts);
}
```

- [ ] **Step 2: 提交**

```bash
git add HearthBot.Cloud/Controllers/GameRecordController.cs
git commit -m "feat(cloud): 新增账号列表 API GET /api/gamerecord/accounts"
```

---

### Task 2: 后端 — 统计聚合端点

**Files:**
- Modify: `HearthBot.Cloud/Controllers/GameRecordController.cs`

- [ ] **Step 1: 在 GetAccounts 方法之后新增 GetStats 端点**

```csharp
[HttpGet("stats")]
public async Task<IActionResult> GetStats(
    [FromQuery] string accountName,
    [FromQuery] int days = 7,
    [FromQuery] string? deviceId = null)
{
    if (string.IsNullOrEmpty(accountName))
        return BadRequest("accountName is required");

    var query = _db.GameRecords
        .Where(g => g.AccountName == accountName);

    if (!string.IsNullOrEmpty(deviceId))
        query = query.Where(g => g.DeviceId == deviceId);
    if (days > 0)
        query = query.Where(g => g.PlayedAt >= DateTime.UtcNow.AddDays(-days));

    var all = await query.ToListAsync();

    var wins = all.Count(g => g.Result == "Win");
    var losses = all.Count(g => g.Result == "Loss");
    var concedes = all.Count(g => g.Result == "Concede");
    var totalGames = all.Count;
    var winRate = totalGames > 0 ? Math.Round(wins * 100.0 / totalGames, 1) : 0;

    // 职业对阵
    var matchups = all
        .GroupBy(g => g.OpponentClass)
        .Select(grp => new
        {
            OpponentClass = grp.Key,
            Games = grp.Count(),
            Wins = grp.Count(g => g.Result == "Win"),
            WinRate = grp.Count() > 0
                ? Math.Round(grp.Count(g => g.Result == "Win") * 100.0 / grp.Count(), 1)
                : 0
        })
        .OrderByDescending(m => m.Games)
        .ToList();

    // 段位历史：每天取最后一局的 RankAfter
    var rankHistory = all
        .Where(g => g.RankAfter != "")
        .GroupBy(g => g.PlayedAt.Date)
        .OrderBy(grp => grp.Key)
        .Select(grp =>
        {
            var last = grp.OrderByDescending(g => g.PlayedAt).First();
            return new { Date = grp.Key.ToString("yyyy-MM-dd"), Rank = last.RankAfter };
        })
        .ToList();

    // 每日趋势
    var dailyTrend = all
        .GroupBy(g => g.PlayedAt.Date)
        .OrderBy(grp => grp.Key)
        .Select(grp => new
        {
            Date = grp.Key.ToString("yyyy-MM-dd"),
            Games = grp.Count(),
            Wins = grp.Count(g => g.Result == "Win"),
            WinRate = grp.Count() > 0
                ? Math.Round(grp.Count(g => g.Result == "Win") * 100.0 / grp.Count(), 1)
                : 0
        })
        .ToList();

    return Ok(new
    {
        AccountName = accountName,
        TotalGames = totalGames,
        Wins = wins,
        Losses = losses,
        Concedes = concedes,
        WinRate = winRate,
        Matchups = matchups,
        RankHistory = rankHistory,
        DailyTrend = dailyTrend
    });
}
```

- [ ] **Step 2: 提交**

```bash
git add HearthBot.Cloud/Controllers/GameRecordController.cs
git commit -m "feat(cloud): 新增账号统计 API GET /api/gamerecord/stats"
```

---

### Task 3: 前端 — 安装 Chart.js 依赖

**Files:**
- Modify: `hearthbot-web/package.json`

- [ ] **Step 1: 安装依赖**

```bash
cd hearthbot-web
npm install chart.js vue-chartjs
```

- [ ] **Step 2: 提交**

```bash
git add hearthbot-web/package.json hearthbot-web/package-lock.json
git commit -m "chore(web): 安装 chart.js + vue-chartjs 依赖"
```

---

### Task 4: 前端 — API 接口新增

**Files:**
- Modify: `hearthbot-web/src/api/index.ts`

- [ ] **Step 1: 在 gameRecordApi 对象中新增两个方法**

在 `gameRecordApi` 中现有的 `getAll` 后面添加：

```typescript
export const gameRecordApi = {
  getAll: (params: Record<string, any>) => api.get('/gamerecord', { params }),
  getAccounts: (params?: Record<string, any>) => api.get<string[]>('/gamerecord/accounts', { params }),
  getStats: (params: Record<string, any>) => api.get('/gamerecord/stats', { params })
}
```

- [ ] **Step 2: 提交**

```bash
git add hearthbot-web/src/api/index.ts
git commit -m "feat(web): 新增 getAccounts / getStats API 接口"
```

---

### Task 5: 前端 — 段位映射工具函数

**Files:**
- Create: `hearthbot-web/src/utils/rankMapping.ts`

- [ ] **Step 1: 创建段位映射工具文件**

```typescript
const TIER_BASE: Record<string, number> = {
  Bronze: 0,
  Silver: 10,
  Gold: 20,
  Platinum: 30,
  Diamond: 40
}

/**
 * 将段位文本（如 "Diamond 5"）转为数值（如 46）。
 * Legend 统一返回 51。无法识别返回 null。
 */
export function rankToNumber(rank: string): number | null {
  if (!rank) return null
  const trimmed = rank.trim()
  if (trimmed.toLowerCase().startsWith('legend')) return 51

  const match = trimmed.match(/^(\w+)\s+(\d+)$/)
  if (!match) return null

  const tier = match[1]
  const star = parseInt(match[2], 10)
  const base = TIER_BASE[tier]
  if (base === undefined || star < 1 || star > 10) return null

  return base + (11 - star)
}

/**
 * 数值转回段位文本用于 Y 轴标签。
 */
export function numberToRank(n: number): string {
  if (n >= 51) return 'Legend'
  for (const [tier, base] of Object.entries(TIER_BASE).sort((a, b) => b[1] - a[1])) {
    if (n > base) return `${tier} ${11 - (n - base)}`
  }
  return ''
}
```

- [ ] **Step 2: 提交**

```bash
git add hearthbot-web/src/utils/rankMapping.ts
git commit -m "feat(web): 段位文本与数值互转工具函数"
```

---

### Task 6: 前端 — GameRecords.vue 统计区改造

**Files:**
- Modify: `hearthbot-web/src/views/GameRecords.vue`

这是最大的一个任务，完整替换 GameRecords.vue 内容。

- [ ] **Step 1: 替换 `<script setup>` 部分**

完整的 `<script setup>` 块：

```typescript
<script setup lang="ts">
import { ref, onMounted, h, watch } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NCard, NDataTable, NSpace,
  NSelect, NTag, NButton, NMenu, NPagination, NGrid, NGi, NStatistic
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { gameRecordApi, deviceApi } from '../api'
import { useAuth } from '../composables/useAuth'
import { useSignalR } from '../composables/useSignalR'
import { Bar, Line } from 'vue-chartjs'
import {
  Chart as ChartJS, CategoryScale, LinearScale, BarElement,
  PointElement, LineElement, Title, Tooltip, Legend
} from 'chart.js'
import { rankToNumber, numberToRank } from '../utils/rankMapping'

ChartJS.register(CategoryScale, LinearScale, BarElement, PointElement, LineElement, Title, Tooltip, Legend)

interface GameRecord {
  id: number
  deviceId: string
  accountName: string
  result: string
  myClass: string
  opponentClass: string
  deckName: string
  durationSeconds: number
  rankBefore: string
  rankAfter: string
  playedAt: string
}

interface AccountStats {
  accountName: string
  totalGames: number
  wins: number
  losses: number
  concedes: number
  winRate: number
  matchups: { opponentClass: string; games: number; wins: number; winRate: number }[]
  rankHistory: { date: string; rank: string }[]
  dailyTrend: { date: string; games: number; wins: number; winRate: number }[]
}

const router = useRouter()
const { logout } = useAuth()
const records = ref<GameRecord[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const filterDevice = ref<string | null>(null)
const filterResult = ref<string | null>(null)
const filterDays = ref(7)
const filterAccount = ref<string | null>(null)
const deviceOptions = ref<{ label: string; value: string }[]>([])
const accountOptions = ref<{ label: string; value: string }[]>([])
const stats = ref<AccountStats | null>(null)

const resultOptions = [
  { label: '全部结果', value: '' },
  { label: '胜利', value: 'Win' },
  { label: '失败', value: 'Loss' },
  { label: '投降', value: 'Concede' }
]

const daysOptions = [
  { label: '今天', value: 1 },
  { label: '最近3天', value: 3 },
  { label: '最近7天', value: 7 },
  { label: '全部', value: 0 }
]

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function formatTime(iso: string) {
  const d = new Date(iso)
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}

const columns = [
  { title: '时间', key: 'playedAt', width: 60, render: (r: GameRecord) => formatTime(r.playedAt) },
  { title: '设备', key: 'deviceId', width: 80 },
  { title: '账号', key: 'accountName', width: 100 },
  {
    title: '结果', key: 'result', width: 60,
    render: (r: GameRecord) => {
      const map: Record<string, 'success' | 'warning' | 'error' | 'default'> = { Win: 'success', Loss: 'error', Concede: 'warning' }
      const label: Record<string, string> = { Win: '胜利', Loss: '失败', Concede: '投降' }
      return h(NTag, { type: map[r.result] || 'default', size: 'small' }, () => label[r.result] || r.result)
    }
  },
  { title: '我方', key: 'myClass', width: 80 },
  { title: '对手', key: 'opponentClass', width: 80 },
  { title: '卡组', key: 'deckName', width: 100 },
  { title: '用时', key: 'duration', width: 60, render: (r: GameRecord) => formatDuration(r.durationSeconds) },
  { title: '段位变化', key: 'rankChange', width: 140, render: (r: GameRecord) => `${r.rankBefore} → ${r.rankAfter}` }
]

async function loadRecords() {
  const params: Record<string, any> = { page: page.value, pageSize: pageSize.value, days: filterDays.value }
  if (filterDevice.value) params.deviceId = filterDevice.value
  if (filterResult.value) params.result = filterResult.value
  if (filterAccount.value) params.accountName = filterAccount.value

  const { data } = await gameRecordApi.getAll(params)
  records.value = data.records
  total.value = data.total
}

async function loadDevices() {
  const { data } = await deviceApi.getAll()
  deviceOptions.value = [
    { label: '全部设备', value: '' },
    ...data.map((d: any) => ({ label: d.displayName, value: d.deviceId }))
  ]
}

async function loadAccounts() {
  const params: Record<string, any> = {}
  if (filterDevice.value) params.deviceId = filterDevice.value
  const { data } = await gameRecordApi.getAccounts(params)
  accountOptions.value = [
    { label: '全部账号', value: '' },
    ...data.map((a: string) => ({ label: a, value: a }))
  ]
}

async function loadStats() {
  if (!filterAccount.value) {
    stats.value = null
    return
  }
  const params: Record<string, any> = { accountName: filterAccount.value, days: filterDays.value }
  if (filterDevice.value) params.deviceId = filterDevice.value
  const { data } = await gameRecordApi.getStats(params)
  stats.value = data
}

async function onFilterChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadStats()])
}

async function onAccountChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadStats()])
}

async function onDeviceChange() {
  page.value = 1
  await Promise.all([loadRecords(), loadAccounts(), loadStats()])
}

// 图表数据计算
function matchupChartData() {
  if (!stats.value) return null
  const m = stats.value.matchups
  return {
    labels: m.map(x => x.opponentClass),
    datasets: [{
      label: '胜率%',
      data: m.map(x => x.winRate),
      backgroundColor: m.map(x => x.winRate >= 50 ? 'rgba(99,226,183,0.7)' : 'rgba(224,98,98,0.7)')
    }]
  }
}

function dailyTrendChartData() {
  if (!stats.value) return null
  const t = stats.value.dailyTrend
  return {
    labels: t.map(x => x.date.slice(5)), // MM-DD
    datasets: [{
      label: '胜率%',
      data: t.map(x => x.winRate),
      borderColor: '#63e2b7',
      backgroundColor: 'rgba(99,226,183,0.2)',
      fill: true,
      tension: 0.3
    }]
  }
}

function rankChartData() {
  if (!stats.value) return null
  const r = stats.value.rankHistory
  const values = r.map(x => rankToNumber(x.rank)).filter((v): v is number => v !== null)
  const labels = r.map(x => x.date.slice(5))
  if (values.length === 0) return null
  return {
    labels,
    datasets: [{
      label: '段位',
      data: values,
      borderColor: '#7ec8e3',
      backgroundColor: 'rgba(126,200,227,0.2)',
      fill: true,
      tension: 0.3
    }]
  }
}

const barOptions = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { display: false } },
  scales: { x: { min: 0, max: 100 } }
}

const lineOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { display: false } },
  scales: { y: { min: 0, max: 100 } }
}

function rankChartOptions() {
  return {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      y: {
        reverse: false,
        ticks: {
          callback: (v: number) => numberToRank(v)
        }
      }
    }
  }
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#63e2b7'
  if (rate >= 45) return '#f2c97d'
  return '#e06262'
}

onMounted(async () => {
  await Promise.all([loadRecords(), loadDevices(), loadAccounts()])

  const { connect } = useSignalR()
  const hub = connect()
  hub.on('NewGameRecord', () => {
    loadRecords()
    loadStats()
    loadAccounts()
  })
})

const menuOptions = [
  { label: '总览', key: '/' },
  { label: '对局记录', key: '/records' }
]
</script>
```

- [ ] **Step 2: 替换 `<template>` 部分**

```html
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
      <NCard title="对局记录">
        <!-- 筛选栏 -->
        <NSpace style="margin-bottom:16px">
          <NSelect v-model:value="filterAccount" :options="accountOptions" style="width:160px"
            placeholder="全部账号" clearable @update:value="onAccountChange" />
          <NSelect v-model:value="filterDevice" :options="deviceOptions" style="width:140px"
            placeholder="全部设备" clearable @update:value="onDeviceChange" />
          <NSelect v-model:value="filterResult" :options="resultOptions" style="width:120px"
            placeholder="全部结果" clearable @update:value="onFilterChange" />
          <NSelect v-model:value="filterDays" :options="daysOptions" style="width:120px"
            @update:value="onFilterChange" />
        </NSpace>

        <!-- 统计卡片 -->
        <div v-if="stats" style="margin-bottom:20px">
          <NGrid :cols="4" :x-gap="12">
            <NGi>
              <NCard size="small">
                <NStatistic label="总场次" :value="stats.totalGames">
                  <template #prefix><span style="color:#7ec8e3">⬢</span></template>
                </NStatistic>
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="胜场" :value="stats.wins">
                  <template #prefix><span style="color:#63e2b7">▲</span></template>
                </NStatistic>
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="负场" :value="stats.losses + stats.concedes">
                  <template #prefix><span style="color:#e06262">▼</span></template>
                </NStatistic>
              </NCard>
            </NGi>
            <NGi>
              <NCard size="small">
                <NStatistic label="胜率">
                  <template #default>
                    <span :style="{ color: winRateColor(stats.winRate), fontWeight: 'bold', fontSize: '24px' }">
                      {{ stats.winRate }}%
                    </span>
                  </template>
                </NStatistic>
              </NCard>
            </NGi>
          </NGrid>
        </div>

        <!-- 图表区 -->
        <div v-if="stats" style="margin-bottom:20px">
          <NGrid :cols="2" :x-gap="12">
            <NGi>
              <NCard title="职业对阵胜率" size="small">
                <div style="height:250px" v-if="matchupChartData()">
                  <Bar :data="matchupChartData()!" :options="barOptions" />
                </div>
                <div v-else style="color:#999;text-align:center;padding:40px">暂无数据</div>
              </NCard>
            </NGi>
            <NGi>
              <NCard title="每日胜率趋势" size="small">
                <div style="height:250px" v-if="dailyTrendChartData()">
                  <Line :data="dailyTrendChartData()!" :options="lineOptions" />
                </div>
                <div v-else style="color:#999;text-align:center;padding:40px">暂无数据</div>
              </NCard>
            </NGi>
          </NGrid>
        </div>

        <!-- 段位变化 -->
        <div v-if="stats && rankChartData()" style="margin-bottom:20px">
          <NCard title="段位变化" size="small">
            <div style="height:200px">
              <Line :data="rankChartData()!" :options="rankChartOptions()" />
            </div>
          </NCard>
        </div>

        <!-- 对局记录表 -->
        <NDataTable :columns="columns" :data="records" :row-key="(r: GameRecord) => r.id" />

        <NSpace justify="center" style="margin-top:16px">
          <NPagination v-model:page="page" :page-count="Math.ceil(total / pageSize)"
            @update:page="loadRecords" />
        </NSpace>
      </NCard>
    </NLayoutContent>
  </NLayout>
</template>
```

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/views/GameRecords.vue
git commit -m "feat(web): 对局记录页面增加账号统计卡片和图表"
```

---

### Task 7: 数据库索引优化

**Files:**
- Modify: `HearthBot.Cloud/Data/CloudDbContext.cs`

- [ ] **Step 1: 为 AccountName 添加索引**

在 `OnModelCreating` 方法中 `GameRecord` 的配置块里，在 `e.HasIndex(g => g.PlayedAt);` 之后添加：

```csharp
e.HasIndex(g => g.AccountName);
```

- [ ] **Step 2: 提交**

```bash
git add HearthBot.Cloud/Data/CloudDbContext.cs
git commit -m "perf(cloud): 为 GameRecord.AccountName 添加数据库索引"
```

---

### Task 8: 验证

- [ ] **Step 1: 构建后端确认无编译错误**

```bash
cd HearthBot.Cloud
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: 构建前端确认无编译错误**

```bash
cd hearthbot-web
npm run build
```

Expected: Build succeeded, 无 TypeScript 错误。

- [ ] **Step 3: 最终提交（如有修复）**

如果构建发现问题，修复后统一提交。
