# 设备总览看板 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有设备表格总览重构为订单看板视图（未标记→进行中→已完成），以接单工作流为核心展示实时段位进度和对局状态。

**Architecture:** 后端扩展 Device 模型新增4个字段（TargetRank/StartRank/StartedAt/CurrentOpponent），心跳链路增加2个参数。前端将 Dashboard.vue 的表格替换为三列看板组件树，复用现有 SignalR 实时更新机制。新增每日归档定时任务清理已完成订单。

**Tech Stack:** C# / ASP.NET Core / EF Core SQLite（后端）、Vue 3 / TypeScript / Naive UI（前端）、SignalR（实时通信）

---

## 文件结构

### 后端修改
| 文件 | 操作 | 职责 |
|------|------|------|
| `HearthBot.Cloud/Models/Device.cs` | 修改 | 新增 TargetRank, StartRank, StartedAt, CurrentOpponent 字段 |
| `HearthBot.Cloud/Data/CloudDbContext.cs` | 修改 | 无需改动（EF Core 自动迁移，SQLite 无需 migration） |
| `HearthBot.Cloud/Services/DeviceManager.cs` | 修改 | UpdateHeartbeat 增加 targetRank/currentOpponent 参数，记录 startRank/startedAt |
| `HearthBot.Cloud/Hubs/BotHub.cs` | 修改 | Heartbeat 方法增加 targetRank/currentOpponent 参数 |
| `HearthBot.Cloud/Services/DeviceWatchdog.cs` | 修改 | 增加每日归档逻辑 |
| `HearthBot.Cloud/Controllers/DeviceController.cs` | 修改 | GetStats 增加 completedCount 字段 |
| `BotMain/Cloud/CloudAgent.cs` | 修改 | HeartbeatData 新增 TargetRank/CurrentOpponent，SendHeartbeatAsync 传递新字段 |
| `BotMain/Cloud/DeviceStatusCollector.cs` | 修改 | Collect 填充 TargetRank/CurrentOpponent |

### 前端新增
| 文件 | 操作 | 职责 |
|------|------|------|
| `hearthbot-web/src/components/StatsBar.vue` | 新建 | 顶部四统计卡片 |
| `hearthbot-web/src/components/KanbanBoard.vue` | 新建 | 三列看板容器 + 分列逻辑 |
| `hearthbot-web/src/components/OrderCard.vue` | 新建 | 订单卡片（折叠态） |
| `hearthbot-web/src/components/OrderDetail.vue` | 新建 | 卡片展开详情面板 |
| `hearthbot-web/src/components/RankProgress.vue` | 新建 | 段位进度条组件 |

### 前端修改
| 文件 | 操作 | 职责 |
|------|------|------|
| `hearthbot-web/src/views/Dashboard.vue` | 重写 | 从表格切换为看板布局 |
| `hearthbot-web/src/api/index.ts` | 修改 | 增加按设备获取对局记录的API |

---

## Task 1: 后端 Device 模型扩展

**Files:**
- Modify: `HearthBot.Cloud/Models/Device.cs`

- [ ] **Step 1: 新增4个字段到 Device 模型**

在 `Device.cs` 的 `OrderNumber` 属性后面添加：

```csharp
public string TargetRank { get; set; } = string.Empty;
public string StartRank { get; set; } = string.Empty;
public DateTime? StartedAt { get; set; }
public string CurrentOpponent { get; set; } = string.Empty;
```

- [ ] **Step 2: 确认 SQLite 自动迁移**

项目使用 `db.Database.EnsureCreated()` 或类似机制。SQLite 不强制 schema migration，新增字段会在下次写入时自动处理。但如果使用了 EF Core Migration，需要执行：

```bash
cd HearthBot.Cloud
dotnet ef migrations add AddKanbanFields
dotnet ef database update
```

检查 `Program.cs` 中是否使用 `EnsureCreated` 来确定是否需要 migration。

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Models/Device.cs
git commit -m "feat(cloud): Device 模型新增看板字段 (TargetRank/StartRank/StartedAt/CurrentOpponent)"
```

---

## Task 2: BotMain 心跳链路扩展

**Files:**
- Modify: `BotMain/Cloud/CloudAgent.cs`
- Modify: `BotMain/Cloud/DeviceStatusCollector.cs`

- [ ] **Step 1: HeartbeatData 新增字段**

在 `CloudAgent.cs` 的 `HeartbeatData` 结构体中，`SessionLosses` 后面添加：

```csharp
public string TargetRank;
public string CurrentOpponent;
```

- [ ] **Step 2: SendHeartbeatAsync 传递新字段**

将 `SendHeartbeatAsync` 方法中的 `InvokeAsync` 调用改为：

```csharp
await _hub.InvokeAsync("Heartbeat",
    _config.DeviceId, s.Status, s.CurrentAccount, s.CurrentRank,
    s.CurrentDeck, s.CurrentProfile, s.GameMode, s.SessionWins, s.SessionLosses,
    s.TargetRank, s.CurrentOpponent);
```

- [ ] **Step 3: DeviceStatusCollector 填充新字段**

在 `DeviceStatusCollector.cs` 的 `Collect` 方法中，return 语句添加两个新字段：

```csharp
return new HeartbeatData
{
    Status = status,
    CurrentAccount = account?.DisplayName ?? _bot.PlayerName ?? "",
    CurrentRank = account?.CurrentRankText ?? _bot.CurrentRankText ?? "",
    CurrentDeck = account?.DeckName ?? _bot.SelectedDeckName ?? "",
    CurrentProfile = account?.ProfileName ?? _bot.SelectedProfileName ?? "",
    GameMode = (account?.ModeIndex ?? _bot.ModeIndex) == 1 ? "Wild" : "Standard",
    SessionWins = account?.Wins ?? stats.Wins,
    SessionLosses = account?.Losses ?? stats.Losses,
    TargetRank = account?.TargetRankText ?? "",
    CurrentOpponent = _bot.CurrentEnemyClassName ?? ""
};
```

注意：`_bot.CurrentEnemyClassName` 只在对局中有值，非对局时为空字符串。`account?.TargetRankText` 来自 `AccountEntry.TargetRankText`，格式为中文（如"钻石5"）。

- [ ] **Step 4: Commit**

```bash
git add BotMain/Cloud/CloudAgent.cs BotMain/Cloud/DeviceStatusCollector.cs
git commit -m "feat(bot): 心跳新增目标段位和当前对手职业字段"
```

---

## Task 3: 后端 Hub 和 DeviceManager 接收新字段

**Files:**
- Modify: `HearthBot.Cloud/Hubs/BotHub.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`

- [ ] **Step 1: BotHub.Heartbeat 增加参数**

将 `BotHub.cs` 中的 `Heartbeat` 方法签名和调用改为：

```csharp
public async Task Heartbeat(string deviceId, string status,
    string currentAccount, string currentRank, string currentDeck,
    string currentProfile, string gameMode, int sessionWins, int sessionLosses,
    string targetRank = "", string currentOpponent = "")
{
    var device = await _devices.UpdateHeartbeat(deviceId, status,
        currentAccount, currentRank, currentDeck,
        currentProfile, gameMode, sessionWins, sessionLosses,
        targetRank, currentOpponent);

    if (device != null)
        await _dashboard.Clients.All.SendAsync("DeviceUpdated", device);
}
```

注意：`targetRank` 和 `currentOpponent` 使用默认值 `""`，保证旧版本 BotMain 客户端兼容。

- [ ] **Step 2: DeviceManager.UpdateHeartbeat 增加参数和 startRank/startedAt 逻辑**

将 `DeviceManager.cs` 中的 `UpdateHeartbeat` 方法替换为：

```csharp
public async Task<Device?> UpdateHeartbeat(string deviceId, string status,
    string currentAccount, string currentRank, string currentDeck,
    string currentProfile, string gameMode, int sessionWins, int sessionLosses,
    string targetRank = "", string currentOpponent = "")
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

    var device = await db.Devices.FindAsync(deviceId);
    if (device == null) return null;

    device.Status = status;
    device.CurrentAccount = currentAccount;
    device.CurrentRank = currentRank;
    device.CurrentDeck = currentDeck;
    device.CurrentProfile = currentProfile;
    device.GameMode = gameMode;
    device.SessionWins = sessionWins;
    device.SessionLosses = sessionLosses;
    device.LastHeartbeat = DateTime.UtcNow;
    device.TargetRank = targetRank;
    device.CurrentOpponent = currentOpponent;

    // 首次心跳时记录起始段位和开始时间
    if (string.IsNullOrEmpty(device.StartRank) && !string.IsNullOrEmpty(currentRank))
    {
        device.StartRank = currentRank;
        device.StartedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return device;
}
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Hubs/BotHub.cs HearthBot.Cloud/Services/DeviceManager.cs
git commit -m "feat(cloud): Hub 和 DeviceManager 接收目标段位和当前对手"
```

---

## Task 4: 后端每日归档和统计扩展

**Files:**
- Modify: `HearthBot.Cloud/Services/DeviceWatchdog.cs`
- Modify: `HearthBot.Cloud/Controllers/DeviceController.cs`

- [ ] **Step 1: DeviceWatchdog 增加每日归档逻辑**

在 `DeviceWatchdog.cs` 中添加归档方法和调度。在 `ExecuteAsync` 的 while 循环体中，`CheckDevices()` 后面添加归档调用：

```csharp
private DateTime _lastArchiveCheck = DateTime.MinValue;

// 在 ExecuteAsync 的 try 块中，CheckDevices() 后面添加：
if (DateTime.UtcNow.Date > _lastArchiveCheck.Date)
{
    await ArchiveCompletedOrders();
    _lastArchiveCheck = DateTime.UtcNow;
}
```

将 `_lastArchiveCheck` 字段添加在 `_alreadyAlerted` 下面。

归档方法：

```csharp
private async Task ArchiveCompletedOrders()
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

    var today = DateTime.UtcNow.Date;
    var devices = await db.Devices
        .Where(d => d.StartedAt != null && d.StartedAt.Value.Date < today && !string.IsNullOrEmpty(d.OrderNumber))
        .ToListAsync();

    foreach (var device in devices)
    {
        // 检查是否已达标（TargetRank 非空且当前段位 >= 目标段位）
        // 段位比较：数值越大越高（用 starLevel 逻辑）
        device.OrderNumber = string.Empty;
        device.StartRank = string.Empty;
        device.StartedAt = null;
        device.TargetRank = string.Empty;
    }

    if (devices.Count > 0)
    {
        await db.SaveChangesAsync();
        _logger.LogInformation("Archived {Count} completed orders from previous days", devices.Count);
    }
}
```

- [ ] **Step 2: DeviceController.GetStats 增加 completedCount**

在 `DeviceController.cs` 的 `GetStats` 方法返回值中增加已完成计数：

```csharp
return Ok(new
{
    OnlineCount = devices.Count(d => d.Status != "Offline"),
    TotalCount = devices.Count,
    TodayGames = todayGames.Count,
    TodayWins = todayGames.Count(g => g.Result == "Win"),
    TodayLosses = todayGames.Count(g => g.Result is "Loss" or "Concede"),
    AbnormalCount = devices.Count(d =>
        d.Status != "Offline" &&
        d.LastHeartbeat < DateTime.UtcNow.AddSeconds(-90)),
    CompletedCount = devices.Count(d =>
        !string.IsNullOrEmpty(d.OrderNumber) &&
        d.Status == "Offline" &&
        d.StartedAt?.Date == today)
});
```

注意：已完成的判定在前端用 rankMapping 比较更精确，这里仅提供一个粗略计数。

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Services/DeviceWatchdog.cs HearthBot.Cloud/Controllers/DeviceController.cs
git commit -m "feat(cloud): 每日归档已完成订单，统计增加 completedCount"
```

---

## Task 5: 前端 API 层和类型扩展

**Files:**
- Modify: `hearthbot-web/src/api/index.ts`
- Modify: `hearthbot-web/src/views/Dashboard.vue`（仅 Device 接口定义）

- [ ] **Step 1: 扩展 Device 接口**

在 `Dashboard.vue` 的 `Device` 接口中添加新字段（在 `orderNumber` 后面）：

```typescript
interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  lastHeartbeat: string
  availableDecksJson: string
  availableProfilesJson: string
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
}
```

后续 Task 会将这个接口提取到单独的类型文件，目前先就地修改。

- [ ] **Step 2: API 增加按设备获取对局记录**

在 `api/index.ts` 的 `gameRecordApi` 中添加：

```typescript
getByDeviceId: (deviceId: string, page = 1, pageSize = 5) =>
  api.get('/gamerecord', { params: { deviceId, days: 0, page, pageSize } }),
```

注意：`days: 0` 表示不限天数，获取该设备所有记录。

- [ ] **Step 3: Commit**

```bash
git add hearthbot-web/src/api/index.ts hearthbot-web/src/views/Dashboard.vue
git commit -m "feat(web): Device 接口新增看板字段，API 增加按设备获取记录"
```

---

## Task 6: 前端 RankProgress 组件

**Files:**
- Create: `hearthbot-web/src/components/RankProgress.vue`

- [ ] **Step 1: 创建段位进度条组件**

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { rankToNumber } from '../utils/rankMapping'

const props = defineProps<{
  startRank: string
  currentRank: string
  targetRank: string
}>()

const progress = computed(() => {
  const start = rankToNumber(props.startRank)
  const current = rankToNumber(props.currentRank)
  const target = rankToNumber(props.targetRank)
  if (start == null || current == null || target == null || target <= start) return 0
  return Math.min(100, Math.max(0, ((current - start) / (target - start)) * 100))
})
</script>

<template>
  <div class="rank-progress">
    <div class="rank-labels">
      <span class="rank-start">{{ startRank }}</span>
      <span class="rank-current">{{ currentRank }}</span>
      <span class="rank-target">{{ targetRank }}</span>
    </div>
    <div class="rank-bar">
      <div class="rank-bar-fill" :style="{ width: progress + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.rank-progress { margin: 6px 0; }
.rank-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-bottom: 3px;
}
.rank-start, .rank-target { color: #888; }
.rank-current { color: #fff; font-weight: 600; }
.rank-bar {
  background: #1a1a2e;
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}
.rank-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #ffa726, #66bb6a);
  border-radius: 4px;
  transition: width 0.5s ease;
}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/components/RankProgress.vue
git commit -m "feat(web): 新增 RankProgress 段位进度条组件"
```

---

## Task 7: 前端 OrderCard 组件

**Files:**
- Create: `hearthbot-web/src/components/OrderCard.vue`

- [ ] **Step 1: 创建订单卡片组件**

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { NTag, NInput, NButton, NSpace } from 'naive-ui'
import RankProgress from './RankProgress.vue'

interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
}

const props = defineProps<{
  device: Device
  column: 'unmarked' | 'active' | 'completed'
}>()

const emit = defineEmits<{
  click: [device: Device]
  saveOrder: [deviceId: string, orderNumber: string]
}>()

const orderInput = defineModel<string>('orderInput', { default: '' })

const statusMap: Record<string, { type: 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  InGame: { type: 'success', label: '对局中' },
  Idle: { type: 'warning', label: '空闲' },
  Online: { type: 'success', label: '在线' },
  Offline: { type: 'error', label: '离线' },
}

const statusInfo = computed(() => statusMap[props.device.status] || { type: 'default' as const, label: props.device.status })

const winRate = computed(() => {
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? ((props.device.sessionWins / total) * 100).toFixed(1) + '%' : '-'
})

const borderColor = computed(() => {
  if (props.column === 'completed') return '#ab47bc'
  if (props.device.status === 'InGame') return '#66bb6a'
  if (props.device.status === 'Idle' || props.device.status === 'Online') return '#42a5f5'
  return '#555'
})
</script>

<template>
  <div
    class="order-card"
    :style="{ borderLeftColor: borderColor, opacity: column === 'completed' ? 0.85 : 1 }"
    @click="column !== 'unmarked' && emit('click', device)"
  >
    <!-- 头部：订单号 + 状态 -->
    <div class="card-header">
      <span class="order-number" v-if="column !== 'unmarked'">#{{ device.orderNumber }}</span>
      <span class="need-order" v-else style="color:#ffa726;font-size:11px;">需要填写订单号</span>
      <NTag :type="statusInfo.type" size="tiny">{{ statusInfo.label }}</NTag>
    </div>

    <!-- 账号 + 设备 -->
    <div class="card-info">
      账号: {{ device.currentAccount }} · 设备: <span class="device-name">{{ device.displayName }}</span>
    </div>

    <!-- 未标记列：订单号输入框 -->
    <div v-if="column === 'unmarked'" class="order-input">
      <NSpace>
        <NInput
          v-model:value="orderInput"
          placeholder="输入订单号..."
          size="tiny"
          style="width:140px"
          @click.stop
        />
        <NButton
          type="warning"
          size="tiny"
          @click.stop="emit('saveOrder', device.deviceId, orderInput)"
        >确认</NButton>
      </NSpace>
    </div>

    <!-- 进行中列：段位进度 + 对局信息 -->
    <template v-if="column === 'active'">
      <RankProgress
        v-if="device.startRank && device.targetRank"
        :start-rank="device.startRank"
        :current-rank="device.currentRank"
        :target-rank="device.targetRank"
      />
      <div v-if="device.status === 'InGame' && device.currentOpponent" class="game-status">
        <div class="game-matchup">
          <span>{{ device.currentDeck }} vs {{ device.currentOpponent }}</span>
        </div>
        <div class="game-stats">
          {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }})
        </div>
      </div>
      <div v-else class="game-stats">
        {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }}) · 等待下一局...
      </div>
      <div class="card-expand-hint">点击展开详情 ▸</div>
    </template>

    <!-- 已完成列：最终战绩 -->
    <template v-if="column === 'completed'">
      <div class="completed-info">
        {{ device.startRank }} → {{ device.currentRank }} · {{ device.sessionWins }}胜{{ device.sessionLosses }}负({{ winRate }})
      </div>
      <div v-if="device.startedAt" class="completed-time">
        设备: {{ device.displayName }}
      </div>
    </template>
  </div>
</template>

<style scoped>
.order-card {
  background: #252545;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 8px;
  border-left: 3px solid #555;
  cursor: pointer;
  transition: background 0.2s;
}
.order-card:hover { background: #2a2a50; }
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.order-number { font-weight: 600; }
.card-info {
  font-size: 12px;
  color: #aaa;
  margin-bottom: 6px;
}
.device-name { color: #4fc3f7; }
.order-input { margin-top: 8px; }
.game-status {
  background: #1a1a2e;
  border-radius: 6px;
  padding: 8px;
  font-size: 11px;
  margin-top: 6px;
}
.game-matchup { margin-bottom: 2px; }
.game-stats { font-size: 11px; color: #888; margin-top: 4px; }
.card-expand-hint {
  font-size: 10px;
  color: #555;
  text-align: right;
  margin-top: 6px;
}
.completed-info {
  font-size: 11px;
  color: #888;
  margin-top: 4px;
}
.completed-time {
  font-size: 11px;
  color: #666;
}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/components/OrderCard.vue
git commit -m "feat(web): 新增 OrderCard 订单卡片组件"
```

---

## Task 8: 前端 OrderDetail 展开详情组件

**Files:**
- Create: `hearthbot-web/src/components/OrderDetail.vue`

- [ ] **Step 1: 创建展开详情面板**

```vue
<script setup lang="ts">
import { ref, watch, h } from 'vue'
import { NButton, NSelect, NInput, NSpace, NDataTable, NTag } from 'naive-ui'
import RankProgress from './RankProgress.vue'
import { gameRecordApi, deviceApi, commandApi } from '../api'

interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
  availableDecksJson: string
}

const props = defineProps<{ device: Device }>()
const emit = defineEmits<{ close: [] }>()

const editingOrder = ref(props.device.orderNumber)
const selectedDeck = ref(props.device.currentDeck)
const records = ref<any[]>([])
const recordsTotal = ref(0)
const recordsPage = ref(1)
const loadingRecords = ref(false)

function getAvailableDecks(): string[] {
  try { return JSON.parse(props.device.availableDecksJson || '[]') } catch { return [] }
}

const winRate = (() => {
  const total = props.device.sessionWins + props.device.sessionLosses
  return total > 0 ? ((props.device.sessionWins / total) * 100).toFixed(1) + '%' : '-'
})()

async function loadRecords(page = 1) {
  loadingRecords.value = true
  try {
    const res = await gameRecordApi.getAll({
      deviceId: props.device.deviceId,
      accountName: props.device.currentAccount,
      days: 0,
      page,
      pageSize: 5
    })
    records.value = res.data.records
    recordsTotal.value = res.data.total
    recordsPage.value = page
  } finally {
    loadingRecords.value = false
  }
}

async function saveOrderNumber() {
  await deviceApi.setOrderNumber(props.device.deviceId, editingOrder.value)
}

async function changeDeck() {
  await commandApi.send(props.device.deviceId, 'ChangeDeck', { DeckName: selectedDeck.value })
}

async function stopBot() {
  await commandApi.send(props.device.deviceId, 'Stop', {})
}

watch(() => props.device.deviceId, () => loadRecords(), { immediate: true })

const recordColumns = [
  {
    title: '结果', key: 'result', width: 50,
    render: (row: any) => {
      const color = row.result === 'Win' ? '#66bb6a' : '#ef5350'
      return h('span', { style: { color } }, row.result === 'Win' ? '胜' : '负')
    }
  },
  { title: '我方', key: 'myClass', width: 60 },
  { title: '对手', key: 'opponentClass', width: 60 },
  {
    title: '段位变化', key: 'rankChange', width: 100,
    render: (row: any) => `${row.rankBefore || '-'} → ${row.rankAfter || '-'}`
  },
  {
    title: '时长', key: 'duration', width: 60,
    render: (row: any) => {
      const m = Math.floor(row.durationSeconds / 60)
      const s = row.durationSeconds % 60
      return `${m}:${String(s).padStart(2, '0')}`
    }
  },
  {
    title: '时间', key: 'playedAt', width: 60,
    render: (row: any) => {
      const d = new Date(row.playedAt)
      return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
    }
  }
]
</script>

<template>
  <div class="order-detail">
    <!-- 头部 -->
    <div class="detail-header">
      <div>
        <span class="detail-order">#{{ device.orderNumber || '未标记' }}</span>
        <NTag :type="device.status === 'InGame' ? 'success' : device.status === 'Idle' ? 'warning' : 'error'" size="small" style="margin-left:8px">
          {{ device.status === 'InGame' ? '对局中' : device.status === 'Idle' ? '空闲' : device.status }}
        </NTag>
      </div>
      <NButton text size="small" @click="emit('close')">✕ 关闭</NButton>
    </div>

    <div class="detail-body">
      <!-- 左侧：信息 + 操作 -->
      <div class="detail-left">
        <div class="detail-section">
          <div class="section-title">基本信息</div>
          <div class="info-grid">
            <span class="info-label">账号</span><span>{{ device.currentAccount }}</span>
            <span class="info-label">设备</span><span class="device-name">{{ device.displayName }}</span>
            <span class="info-label">模式</span><span>{{ device.gameMode === 'Wild' ? '狂野' : '标准' }}</span>
            <span class="info-label">卡组</span><span>{{ device.currentDeck }}</span>
            <span class="info-label">策略</span><span>{{ device.currentProfile }}</span>
            <span class="info-label">订单号</span>
            <NSpace size="small">
              <NInput v-model:value="editingOrder" size="tiny" style="width:100px" />
              <NButton type="primary" size="tiny" @click="saveOrderNumber">保存</NButton>
            </NSpace>
          </div>
        </div>

        <div class="detail-section">
          <div class="section-title">操作</div>
          <NSpace>
            <NButton type="error" size="small" @click="stopBot">停止 Bot</NButton>
            <NSelect
              v-model:value="selectedDeck"
              :options="getAvailableDecks().map(d => ({ label: d, value: d }))"
              size="small"
              style="width:140px"
            />
            <NButton type="primary" size="small" @click="changeDeck">切换卡组</NButton>
          </NSpace>
        </div>
      </div>

      <!-- 右侧：段位进度 -->
      <div class="detail-right">
        <div class="detail-section">
          <div class="section-title">段位进度</div>
          <RankProgress
            v-if="device.startRank && device.targetRank"
            :start-rank="device.startRank"
            :current-rank="device.currentRank"
            :target-rank="device.targetRank"
          />
          <div class="stats-grid">
            <div class="stat-item">
              <div class="stat-label">胜</div>
              <div class="stat-value">{{ device.sessionWins }}</div>
            </div>
            <div class="stat-item">
              <div class="stat-label">负</div>
              <div class="stat-value">{{ device.sessionLosses }}</div>
            </div>
            <div class="stat-item">
              <div class="stat-label">胜率</div>
              <div class="stat-value" style="color:#66bb6a">{{ winRate }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 底部：对局记录 -->
    <div class="detail-section">
      <div class="section-title">最近对局</div>
      <NDataTable
        :columns="recordColumns"
        :data="records"
        :loading="loadingRecords"
        size="small"
        :bordered="false"
        :pagination="false"
      />
      <div v-if="recordsTotal > 5" class="load-more" @click="loadRecords(recordsPage + 1)">
        加载更多 ▾
      </div>
    </div>
  </div>
</template>

<style scoped>
.order-detail {
  background: #1e1e38;
  border-radius: 10px;
  padding: 16px;
  margin-bottom: 12px;
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.detail-order { font-size: 18px; font-weight: 700; }
.detail-body { display: flex; gap: 16px; margin-bottom: 16px; }
.detail-left { flex: 1; }
.detail-right { flex: 1; }
.detail-section {
  background: #252545;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 10px;
}
.section-title { font-size: 11px; color: #888; margin-bottom: 8px; }
.info-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 12px;
  font-size: 12px;
}
.info-label { color: #888; }
.device-name { color: #4fc3f7; }
.stats-grid {
  display: flex;
  justify-content: space-around;
  margin-top: 12px;
  text-align: center;
}
.stat-item .stat-label { font-size: 11px; color: #888; }
.stat-item .stat-value { font-size: 20px; font-weight: 700; }
.load-more {
  text-align: center;
  color: #555;
  font-size: 10px;
  margin-top: 8px;
  cursor: pointer;
}
.load-more:hover { color: #888; }
</style>
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/components/OrderDetail.vue
git commit -m "feat(web): 新增 OrderDetail 订单展开详情组件"
```

---

## Task 9: 前端 StatsBar 组件

**Files:**
- Create: `hearthbot-web/src/components/StatsBar.vue`

- [ ] **Step 1: 创建统计栏组件**

```vue
<script setup lang="ts">
import { NCard, NStatistic, NGrid, NGi, NSkeleton } from 'naive-ui'
import { computed } from 'vue'

interface Stats {
  onlineCount: number
  totalCount: number
  todayGames: number
  todayWins: number
  todayLosses: number
  abnormalCount: number
  completedCount: number
}

const props = defineProps<{
  stats: Stats
  loading: boolean
}>()

const todayWinRate = computed(() => {
  const total = props.stats.todayWins + props.stats.todayLosses
  return total > 0 ? ((props.stats.todayWins / total) * 100).toFixed(1) + '%' : '-'
})
</script>

<template>
  <NGrid :cols="4" :x-gap="12" style="margin-bottom:24px" v-if="loading">
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
      <NCard><NStatistic label="今日对局" :value="stats.todayGames" /></NCard>
    </NGi>
    <NGi>
      <NCard><NStatistic label="今日胜率" :value="todayWinRate" /></NCard>
    </NGi>
    <NGi>
      <NCard><NStatistic label="今日完成" :value="stats.completedCount" /></NCard>
    </NGi>
  </NGrid>
</template>
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/components/StatsBar.vue
git commit -m "feat(web): 新增 StatsBar 统计栏组件"
```

---

## Task 10: 前端 KanbanBoard 组件

**Files:**
- Create: `hearthbot-web/src/components/KanbanBoard.vue`

- [ ] **Step 1: 创建看板容器组件**

```vue
<script setup lang="ts">
import { computed, ref } from 'vue'
import { rankToNumber } from '../utils/rankMapping'
import OrderCard from './OrderCard.vue'
import OrderDetail from './OrderDetail.vue'
import { deviceApi } from '../api'

interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
  availableDecksJson: string
  availableProfilesJson: string
  lastHeartbeat: string
}

const props = defineProps<{ devices: Device[] }>()
const emit = defineEmits<{ refresh: [] }>()

const expandedDeviceId = ref<string | null>(null)
const orderInputs = ref<Record<string, string>>({})

// 只展示非离线设备（或今日有 startedAt 的已完成设备）
const onlineDevices = computed(() =>
  props.devices.filter(d => d.status !== 'Offline' || isCompletedToday(d))
)

function isCompletedToday(d: Device): boolean {
  if (!d.startedAt || !d.orderNumber) return false
  const started = new Date(d.startedAt)
  const today = new Date()
  return started.toDateString() === today.toDateString()
}

function isCompleted(d: Device): boolean {
  if (!d.orderNumber || !d.targetRank || !d.currentRank) return false
  const current = rankToNumber(d.currentRank)
  const target = rankToNumber(d.targetRank)
  if (current == null || target == null) return false
  return current >= target
}

const unmarked = computed(() =>
  onlineDevices.value.filter(d => !d.orderNumber && d.status !== 'Offline')
)

const active = computed(() =>
  onlineDevices.value.filter(d => d.orderNumber && !isCompleted(d) && d.status !== 'Offline')
)

const completed = computed(() =>
  onlineDevices.value.filter(d => d.orderNumber && (isCompleted(d) || (d.status === 'Offline' && isCompletedToday(d))))
)

function toggleDetail(device: Device) {
  expandedDeviceId.value = expandedDeviceId.value === device.deviceId ? null : device.deviceId
}

async function saveOrder(deviceId: string, orderNumber: string) {
  if (!orderNumber.trim()) return
  await deviceApi.setOrderNumber(deviceId, orderNumber.trim())
  orderInputs.value[deviceId] = ''
  emit('refresh')
}
</script>

<template>
  <div class="kanban-board">
    <!-- 未标记列 -->
    <div class="kanban-column">
      <div class="column-header" style="color:#ffa726">
        未标记 <span class="column-count">{{ unmarked.length }}</span>
      </div>
      <OrderCard
        v-for="d in unmarked"
        :key="d.deviceId"
        :device="d"
        column="unmarked"
        v-model:order-input="orderInputs[d.deviceId]"
        @save-order="saveOrder"
      />
      <div v-if="!unmarked.length" class="column-empty">暂无未标记设备</div>
    </div>

    <!-- 进行中列 -->
    <div class="kanban-column kanban-column-active">
      <div class="column-header" style="color:#4fc3f7">
        进行中 <span class="column-count">{{ active.length }}</span>
      </div>
      <template v-for="d in active" :key="d.deviceId">
        <OrderDetail
          v-if="expandedDeviceId === d.deviceId"
          :device="d"
          @close="expandedDeviceId = null"
        />
        <OrderCard
          v-else
          :device="d"
          column="active"
          @click="toggleDetail"
        />
      </template>
      <div v-if="!active.length" class="column-empty">暂无进行中订单</div>
    </div>

    <!-- 今日完成列 -->
    <div class="kanban-column">
      <div class="column-header" style="color:#ab47bc">
        今日完成 <span class="column-count">{{ completed.length }}</span>
      </div>
      <OrderCard
        v-for="d in completed"
        :key="d.deviceId"
        :device="d"
        column="completed"
      />
      <div v-if="!completed.length" class="column-empty">暂无完成订单</div>
      <div v-if="completed.length" class="archive-hint">隔天自动归档</div>
    </div>
  </div>
</template>

<style scoped>
.kanban-board {
  display: flex;
  gap: 12px;
  min-height: 400px;
}
.kanban-column {
  flex: 1;
  background: #1e1e38;
  border-radius: 10px;
  padding: 12px;
}
.kanban-column-active { flex: 1.3; }
.column-header {
  font-weight: 600;
  margin-bottom: 12px;
}
.column-count {
  background: #3a3a5a;
  border-radius: 10px;
  padding: 1px 8px;
  font-size: 11px;
}
.column-empty {
  color: #555;
  font-size: 12px;
  text-align: center;
  padding: 24px 0;
}
.archive-hint {
  font-size: 10px;
  color: #555;
  text-align: center;
  margin-top: 8px;
}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add hearthbot-web/src/components/KanbanBoard.vue
git commit -m "feat(web): 新增 KanbanBoard 三列看板容器组件"
```

---

## Task 11: 重写 Dashboard.vue

**Files:**
- Modify: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: 将 Dashboard.vue 替换为看板布局**

完全重写 Dashboard.vue，使用新组件：

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { NLayout, NLayoutHeader, NLayoutContent, NSpace, NButton, NMenu } from 'naive-ui'
import { useRouter } from 'vue-router'
import { deviceApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import { useAuth } from '../composables/useAuth'
import StatsBar from '../components/StatsBar.vue'
import KanbanBoard from '../components/KanbanBoard.vue'

interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  lastHeartbeat: string
  availableDecksJson: string
  availableProfilesJson: string
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
}

interface Stats {
  onlineCount: number
  totalCount: number
  todayGames: number
  todayWins: number
  todayLosses: number
  abnormalCount: number
  completedCount: number
}

const router = useRouter()
const { logout } = useAuth()
const devices = ref<Device[]>([])
const stats = ref<Stats>({ onlineCount: 0, totalCount: 0, todayGames: 0, todayWins: 0, todayLosses: 0, abnormalCount: 0, completedCount: 0 })
const firstLoad = ref(true)
const isLoading = ref(false)
let pollTimer: ReturnType<typeof setInterval> | null = null

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

onMounted(async () => {
  await loadData()

  const { connect } = useSignalR()
  const hub = connect()

  hub.on('DeviceUpdated', (device: Device) => {
    const idx = devices.value.findIndex(d => d.deviceId === device.deviceId)
    if (idx >= 0) devices.value[idx] = device
    else devices.value.push(device)
  })

  hub.on('DeviceOnline', (deviceId: string, displayName: string) => {
    const idx = devices.value.findIndex(d => d.deviceId === deviceId)
    if (idx < 0) {
      devices.value.push({ deviceId, displayName, status: 'Idle' } as Device)
    }
  })

  hub.on('DeviceOffline', (deviceId: string) => {
    const idx = devices.value.findIndex(d => d.deviceId === deviceId)
    if (idx >= 0) devices.value[idx] = { ...devices.value[idx], status: 'Offline' }
  })

  hub.on('NewGameRecord', () => loadData())

  pollTimer = setInterval(() => loadData(), 60000)
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
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
      <StatsBar :stats="stats" :loading="firstLoad" />
      <KanbanBoard :devices="devices" @refresh="loadData" />
    </NLayoutContent>
  </NLayout>
</template>
```

- [ ] **Step 2: 验证开发服务器编译通过**

```bash
cd hearthbot-web
npm run dev
```

检查无 TypeScript 编译错误和控制台报错。

- [ ] **Step 3: Commit**

```bash
git add hearthbot-web/src/views/Dashboard.vue
git commit -m "feat(web): Dashboard 从表格重构为订单看板视图"
```

---

## Task 12: 端到端验证

- [ ] **Step 1: 后端编译验证**

```bash
cd HearthBot.Cloud
dotnet build
```

确认无编译错误。

- [ ] **Step 2: 前端编译验证**

```bash
cd hearthbot-web
npm run build
```

确认无编译错误。

- [ ] **Step 3: 功能验证清单**

手动验证以下场景：
1. 设备上线 → 出现在"未标记"列
2. 填写订单号 → 卡片移到"进行中"列
3. 心跳更新 → 段位进度条和对局状态实时刷新
4. 点击卡片 → 展开详情面板，显示对局记录
5. 详情中切换卡组 → 命令发送成功
6. 达到目标段位 → 卡片移到"今日完成"列

- [ ] **Step 4: 最终 Commit 和推送**

```bash
git push origin main
```
