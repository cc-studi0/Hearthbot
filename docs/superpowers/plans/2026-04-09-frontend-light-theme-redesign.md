# HearthBot 前端浅色主题全面改版 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 HearthBot 前端从深色主题全面改版为经典管理后台浅色主题（白底、蓝色强调、左侧深蓝侧边栏）

**Architecture:** 将导航从 Dashboard/GameRecords 中提取到 App.vue 作为全局侧边栏布局，Login 页面独立不带侧边栏。所有组件的 scoped CSS 从深色系替换为浅色系。不改动任何业务逻辑和功能。

**Tech Stack:** Vue 3, TypeScript, Naive UI, scoped CSS

---

### Task 1: App.vue — 侧边栏 + 顶部栏全局布局

**Files:**
- Modify: `hearthbot-web/src/App.vue`
- Modify: `hearthbot-web/src/main.ts` (无改动，仅确认路由结构兼容)

- [ ] **Step 1: 重写 App.vue 为侧边栏布局**

将 `src/App.vue` 从简单的 `<router-view />` 改为带侧边栏的全局布局。Login 页面不显示侧边栏。

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuth } from './composables/useAuth'

const router = useRouter()
const route = useRoute()
const { logout } = useAuth()

const isLoginPage = computed(() => route.path === '/login')

const navItems = [
  { key: '/', label: '总览', icon: '📊' },
  { key: '/records', label: '对局记录', icon: '📋' },
]

function navigate(path: string) {
  router.push(path)
}
</script>

<template>
  <!-- Login 页面：无侧边栏 -->
  <router-view v-if="isLoginPage" />

  <!-- 其他页面：侧边栏 + 顶部栏 + 内容区 -->
  <div v-else class="app-layout">
    <aside class="sidebar">
      <div class="sidebar-logo">HearthBot</div>
      <nav class="sidebar-nav">
        <div
          v-for="item in navItems"
          :key="item.key"
          class="nav-item"
          :class="{ active: route.path === item.key }"
          @click="navigate(item.key)"
        >
          <span class="nav-icon">{{ item.icon }}</span>
          <span class="nav-label">{{ item.label }}</span>
        </div>
      </nav>
      <div class="sidebar-footer">
        <div class="nav-item" @click="logout">
          <span class="nav-icon">🚪</span>
          <span class="nav-label">退出登录</span>
        </div>
      </div>
    </aside>

    <div class="main-area">
      <header class="topbar">
        <h1 class="page-title">{{ navItems.find(n => n.key === route.path)?.label || '总览' }}</h1>
        <span class="user-name">管理员</span>
      </header>
      <main class="content">
        <router-view />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  min-height: 100vh;
}

.sidebar {
  width: 200px;
  background: #1e293b;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  z-index: 100;
}

.sidebar-logo {
  padding: 20px 16px;
  font-size: 18px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 1px;
}

.sidebar-nav {
  flex: 1;
  padding: 8px 0;
}

.sidebar-footer {
  padding: 8px 0;
  border-top: 1px solid #334155;
}

.nav-item {
  display: flex;
  align-items: center;
  padding: 10px 16px;
  cursor: pointer;
  color: #94a3b8;
  font-size: 14px;
  transition: all 0.2s ease;
  border-left: 3px solid transparent;
}

.nav-item:hover {
  background: #334155;
  color: #fff;
}

.nav-item.active {
  background: #334155;
  color: #fff;
  border-left-color: #3b82f6;
}

.nav-icon {
  margin-right: 10px;
  font-size: 16px;
}

.nav-label {
  font-size: 14px;
}

.main-area {
  flex: 1;
  margin-left: 200px;
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.topbar {
  height: 56px;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  flex-shrink: 0;
}

.page-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0;
}

.user-name {
  font-size: 14px;
  color: #64748b;
}

.content {
  flex: 1;
  background: #f5f7fa;
  padding: 24px;
}
</style>
```

- [ ] **Step 2: 验证路由结构**

确认 `src/main.ts` 中路由定义不需要改动，路由守卫仍然生效。当前路由：
- `/login` → Login.vue（无侧边栏）
- `/` → Dashboard.vue（有侧边栏）
- `/records` → GameRecords.vue（有侧边栏）

无需修改 main.ts。

- [ ] **Step 3: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功无报错

- [ ] **Step 4: 提交**

```bash
git add hearthbot-web/src/App.vue
git commit -m "refactor(web): App.vue 添加侧边栏+顶部栏全局布局"
```

---

### Task 2: Login.vue — 浅色登录页

**Files:**
- Modify: `hearthbot-web/src/views/Login.vue`

- [ ] **Step 1: 重写 Login.vue 样式和模板**

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { useAuth } from '../composables/useAuth'

const username = ref('')
const password = ref('')
const { login, loading, error } = useAuth()

function onSubmit() {
  login(username.value, password.value)
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <h1 class="login-title">HearthBot</h1>
      <p class="login-subtitle">云控管理平台</p>

      <div v-if="error" class="login-error">{{ error }}</div>

      <form @submit.prevent="onSubmit">
        <div class="form-group">
          <label class="form-label">用户名</label>
          <input
            v-model="username"
            class="form-input"
            placeholder="admin"
            autocomplete="username"
          />
        </div>
        <div class="form-group">
          <label class="form-label">密码</label>
          <input
            v-model="password"
            type="password"
            class="form-input"
            placeholder="密码"
            autocomplete="current-password"
            @keyup.enter="onSubmit"
          />
        </div>
        <button
          type="submit"
          class="login-btn"
          :disabled="loading"
        >
          {{ loading ? '登录中...' : '登录' }}
        </button>
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #e0e7ff 0%, #f5f7fa 100%);
}

.login-card {
  width: 400px;
  background: #ffffff;
  border-radius: 12px;
  padding: 40px;
  box-shadow: 0 4px 24px rgba(0, 0, 0, 0.08);
}

.login-title {
  font-size: 24px;
  font-weight: 700;
  color: #3b82f6;
  text-align: center;
  margin: 0 0 4px 0;
}

.login-subtitle {
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
  margin: 0 0 32px 0;
}

.login-error {
  background: #fef2f2;
  color: #ef4444;
  border: 1px solid #fecaca;
  border-radius: 8px;
  padding: 10px 14px;
  font-size: 13px;
  margin-bottom: 16px;
}

.form-group {
  margin-bottom: 20px;
}

.form-label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  color: #1e293b;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  font-size: 14px;
  color: #1e293b;
  background: #ffffff;
  outline: none;
  transition: border-color 0.2s ease;
  box-sizing: border-box;
}

.form-input:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.form-input::placeholder {
  color: #94a3b8;
}

.login-btn {
  width: 100%;
  padding: 10px 0;
  background: #3b82f6;
  color: #ffffff;
  border: none;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.2s ease;
  margin-top: 8px;
}

.login-btn:hover {
  background: #2563eb;
}

.login-btn:disabled {
  background: #93c5fd;
  cursor: not-allowed;
}
</style>
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/views/Login.vue
git commit -m "style(web): Login 页面改为浅色主题"
```

---

### Task 3: Dashboard.vue — 移除导航，简化为纯内容

**Files:**
- Modify: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: 重写 Dashboard.vue**

导航已移至 App.vue，Dashboard 只保留内容部分。移除 NLayout/NLayoutHeader/NLayoutContent/NMenu/NSpace/NButton 等导航相关引用。

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { deviceApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import StatsBar from '../components/StatsBar.vue'
import KanbanBoard from '../components/KanbanBoard.vue'
import type { Device, Stats } from '../types'

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
</script>

<template>
  <div>
    <StatsBar :stats="stats" :loading="firstLoad" />
    <KanbanBoard :devices="devices" @refresh="loadData" />
  </div>
</template>
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/views/Dashboard.vue
git commit -m "refactor(web): Dashboard 移除导航，由 App.vue 统一管理"
```

---

### Task 4: GameRecords.vue — 移除导航 + 浅色主题

**Files:**
- Modify: `hearthbot-web/src/views/GameRecords.vue`

- [ ] **Step 1: 重写 GameRecords.vue**

移除导航，所有颜色替换为浅色系。

```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NSkeleton } from 'naive-ui'
import { gameRecordApi } from '../api'
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

const deviceColumns = ref<DeviceColumn[]>([])
const loading = ref(true)

function formatDuration(s: number) {
  return `${Math.floor(s / 60)}:${(s % 60).toString().padStart(2, '0')}`
}

function winRateColor(rate: number): string {
  if (rate >= 55) return '#22c55e'
  if (rate >= 45) return '#f59e0b'
  return '#ef4444'
}

function resultLabel(r: string): { text: string; color: string; bg: string } {
  if (r === 'Win') return { text: 'W', color: '#22c55e', bg: '#f0fdf4' }
  if (r === 'Loss') return { text: 'L', color: '#ef4444', bg: '#fef2f2' }
  return { text: 'C', color: '#f59e0b', bg: '#fffbeb' }
}

function rankDisplay(rec: DeviceRecord): string | null {
  if (!rec.rankAfter) return null
  if (rec.rankBefore && rec.rankBefore !== rec.rankAfter)
    return `${rec.rankBefore}→${rec.rankAfter}`
  return rec.rankAfter
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
</script>

<template>
  <div class="records-page">
    <div class="records-card">
      <h2 class="records-title">设备战绩</h2>

      <!-- 骨架屏 -->
      <div v-if="loading" class="skeleton-row">
        <div v-for="i in 4" :key="i" class="skeleton-col">
          <NSkeleton text :repeat="12" />
        </div>
      </div>

      <!-- 设备列视图 -->
      <div v-else-if="deviceColumns.length > 0" class="device-columns">
        <div
          v-for="(dev, idx) in deviceColumns"
          :key="dev.deviceId"
          class="device-column"
          :style="{ borderRight: idx < deviceColumns.length - 1 ? '1px solid #e2e8f0' : 'none' }"
        >
          <!-- 设备头部 -->
          <div class="device-header">
            <div class="device-name">{{ dev.displayName }}</div>
            <div class="device-meta">{{ dev.currentAccount }} · {{ dev.currentRank }}</div>
            <div class="device-winrate" :style="{ color: winRateColor(dev.winRate) }">
              {{ dev.wins }}W {{ dev.losses }}L ({{ dev.winRate }}%)
            </div>
          </div>

          <!-- 对局列表 -->
          <div class="record-list">
            <div
              v-for="(rec, ri) in dev.records"
              :key="ri"
              class="record-row"
            >
              <span
                class="record-result"
                :style="{ color: resultLabel(rec.result).color, background: resultLabel(rec.result).bg }"
              >
                {{ resultLabel(rec.result).text }}
              </span>
              <span class="record-opponent">{{ rec.opponentClass }}</span>
              <span class="record-deck">{{ rec.deckName }}</span>
              <span class="record-duration">{{ formatDuration(rec.durationSeconds) }}</span>
              <span v-if="rankDisplay(rec)" class="record-rank">{{ rankDisplay(rec) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 无数据 -->
      <div v-else class="no-data">暂无设备数据</div>
    </div>
  </div>
</template>

<style scoped>
.records-page {
  /* 内容区已由 App.vue 提供 padding */
}

.records-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
}

.records-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
  margin: 0 0 16px 0;
}

.skeleton-row {
  display: flex;
  gap: 16px;
}

.skeleton-col {
  flex: 1;
}

.device-columns {
  display: flex;
  overflow-x: auto;
}

.device-column {
  flex: 1;
  min-width: 180px;
  padding: 0 12px;
}

.device-header {
  margin-bottom: 12px;
}

.device-name {
  font-weight: 600;
  color: #3b82f6;
  font-size: 14px;
}

.device-meta {
  color: #64748b;
  font-size: 12px;
}

.device-winrate {
  font-weight: 600;
  font-size: 13px;
}

.record-list {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12px;
}

.record-row {
  display: flex;
  align-items: center;
  gap: 6px;
  line-height: 1.6;
  padding: 2px 0;
}

.record-row:hover {
  background: #f8fafc;
  border-radius: 4px;
}

.record-result {
  font-weight: 600;
  width: 20px;
  height: 20px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  font-size: 11px;
  flex-shrink: 0;
}

.record-opponent {
  color: #1e293b;
}

.record-deck {
  color: #64748b;
  font-size: 11px;
}

.record-duration {
  color: #94a3b8;
  font-size: 11px;
}

.record-rank {
  color: #3b82f6;
  font-size: 11px;
}

.no-data {
  color: #94a3b8;
  text-align: center;
  padding: 60px;
}
</style>
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/views/GameRecords.vue
git commit -m "style(web): GameRecords 移除导航并改为浅色主题"
```

---

### Task 5: StatsBar.vue — 浅色卡片 + 彩色竖条

**Files:**
- Modify: `hearthbot-web/src/components/StatsBar.vue`

- [ ] **Step 1: 重写 StatsBar.vue**

移除 Naive UI 组件，使用纯 HTML + CSS 实现带彩色竖条的统计卡片。

```vue
<script setup lang="ts">
import { computed } from 'vue'
import type { Stats } from '../types'

const props = defineProps<{
  stats: Stats
  loading: boolean
}>()

const todayWinRate = computed(() => {
  const total = props.stats.todayWins + props.stats.todayLosses
  return total > 0 ? ((props.stats.todayWins / total) * 100).toFixed(1) + '%' : '-'
})

const cards = computed(() => [
  { label: '在线设备', value: `${props.stats.onlineCount} / ${props.stats.totalCount}`, color: '#3b82f6' },
  { label: '今日对局', value: props.stats.todayGames, color: '#22c55e' },
  { label: '今日胜率', value: todayWinRate.value, color: '#f59e0b' },
  { label: '今日完成', value: props.stats.completedCount, color: '#8b5cf6' },
])
</script>

<template>
  <div class="stats-bar">
    <template v-if="loading">
      <div v-for="i in 4" :key="i" class="stat-card loading-card">
        <div class="loading-line" />
        <div class="loading-line short" />
      </div>
    </template>
    <template v-else>
      <div
        v-for="card in cards"
        :key="card.label"
        class="stat-card"
        :style="{ borderLeftColor: card.color }"
      >
        <div class="stat-value">{{ card.value }}</div>
        <div class="stat-label">{{ card.label }}</div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.stats-bar {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 16px 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
  border-left: 4px solid #e2e8f0;
}

.stat-value {
  font-size: 24px;
  font-weight: 700;
  color: #1e293b;
  line-height: 1.2;
}

.stat-label {
  font-size: 13px;
  color: #64748b;
  margin-top: 4px;
}

.loading-card {
  padding: 20px;
}

.loading-line {
  height: 24px;
  background: #e2e8f0;
  border-radius: 4px;
  margin-bottom: 8px;
  animation: pulse 1.5s infinite;
}

.loading-line.short {
  width: 60%;
  height: 14px;
  margin-bottom: 0;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
</style>
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/components/StatsBar.vue
git commit -m "style(web): StatsBar 改为浅色卡片+彩色竖条装饰"
```

---

### Task 6: KanbanBoard.vue — 浅色看板

**Files:**
- Modify: `hearthbot-web/src/components/KanbanBoard.vue`

- [ ] **Step 1: 替换 KanbanBoard.vue 的样式**

只改 `<style scoped>` 部分和列标题颜色，模板结构和逻辑不变。

将 `<style scoped>` 替换为：

```css
.kanban-board {
  display: flex;
  gap: 16px;
  min-height: 400px;
}
.kanban-column {
  flex: 1;
  background: #f5f7fa;
  border-radius: 8px;
  padding: 12px;
}
.kanban-column-active { flex: 1.3; }
.column-header {
  font-weight: 600;
  font-size: 14px;
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.column-count {
  background: #e2e8f0;
  color: #64748b;
  border-radius: 10px;
  padding: 1px 8px;
  font-size: 11px;
}
.column-empty {
  color: #94a3b8;
  font-size: 12px;
  text-align: center;
  padding: 24px 0;
}
.archive-hint {
  font-size: 10px;
  color: #94a3b8;
  text-align: center;
  margin-top: 8px;
}
```

同时修改模板中三个列标题的 `style` 属性：
- 未标记列：`style="color:#f59e0b"` 保持橙色
- 进行中列：`style="color:#3b82f6"` 改为蓝色
- 今日完成列：`style="color:#8b5cf6"` 保持紫色

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/components/KanbanBoard.vue
git commit -m "style(web): KanbanBoard 改为浅色看板样式"
```

---

### Task 7: OrderCard.vue — 白色卡片 + 浅色徽章

**Files:**
- Modify: `hearthbot-web/src/components/OrderCard.vue`

- [ ] **Step 1: 替换 OrderCard.vue 的样式和边框颜色逻辑**

修改 script 中的 `borderColor` computed：

```typescript
const borderColor = computed(() => {
  if (props.column === 'completed') return '#8b5cf6'
  if (props.device.status === 'InGame') return '#22c55e'
  if (props.device.status === 'Idle' || props.device.status === 'Online') return '#3b82f6'
  return '#e2e8f0'
})
```

替换 `<style scoped>`：

```css
.order-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 8px;
  border-left: 3px solid #e2e8f0;
  border: 1px solid #e2e8f0;
  border-left: 3px solid #e2e8f0;
  cursor: pointer;
  transition: all 0.2s ease;
}
.order-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.order-number {
  font-weight: 600;
  color: #3b82f6;
}
.need-order {
  color: #f59e0b;
  font-size: 11px;
}
.card-info {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
}
.device-name {
  color: #3b82f6;
  font-weight: 500;
}
.order-input { margin-top: 8px; }
.game-status {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px;
  font-size: 11px;
  margin-top: 6px;
  color: #1e293b;
}
.game-matchup {
  margin-bottom: 2px;
  color: #1e293b;
}
.game-stats {
  font-size: 11px;
  color: #64748b;
  margin-top: 4px;
}
.card-expand-hint {
  font-size: 10px;
  color: #94a3b8;
  text-align: right;
  margin-top: 6px;
}
.completed-info {
  font-size: 11px;
  color: #64748b;
  margin-top: 4px;
}
.completed-time {
  font-size: 11px;
  color: #94a3b8;
}
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/components/OrderCard.vue
git commit -m "style(web): OrderCard 改为白色卡片+浅色徽章"
```

---

### Task 8: OrderDetail.vue — 浅色详情面板

**Files:**
- Modify: `hearthbot-web/src/components/OrderDetail.vue`

- [ ] **Step 1: 替换 OrderDetail.vue 中的颜色引用**

修改 script 中 `recordColumns` 的结果列颜色：
- 胜：`color: '#22c55e'`（原 `#66bb6a`）
- 负：`color: '#ef4444'`（原 `#ef5350`）

替换 `<style scoped>`：

```css
.order-detail {
  background: #ffffff;
  border-radius: 8px;
  border: 1px solid #e2e8f0;
  padding: 16px;
  margin-bottom: 12px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.detail-order {
  font-size: 18px;
  font-weight: 700;
  color: #1e293b;
}
.detail-body {
  display: flex;
  gap: 16px;
  margin-bottom: 16px;
}
.detail-left { flex: 1; }
.detail-right { flex: 1; }
.detail-section {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 10px;
}
.section-title {
  font-size: 11px;
  color: #64748b;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 8px;
}
.info-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 12px;
  font-size: 12px;
  color: #1e293b;
}
.info-label {
  color: #64748b;
}
.device-name {
  color: #3b82f6;
}
.stats-grid {
  display: flex;
  justify-content: space-around;
  margin-top: 12px;
  text-align: center;
}
.stat-item .stat-label {
  font-size: 11px;
  color: #64748b;
}
.stat-item .stat-value {
  font-size: 20px;
  font-weight: 700;
  color: #1e293b;
}
.load-more {
  text-align: center;
  color: #94a3b8;
  font-size: 10px;
  margin-top: 8px;
  cursor: pointer;
}
.load-more:hover {
  color: #3b82f6;
}
```

同时修改模板中胜率的内联样式：
- `style="color:#66bb6a"` → `style="color:#22c55e"`

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/components/OrderDetail.vue
git commit -m "style(web): OrderDetail 改为浅色详情面板"
```

---

### Task 9: RankProgress.vue — 蓝色进度条

**Files:**
- Modify: `hearthbot-web/src/components/RankProgress.vue`

- [ ] **Step 1: 替换 RankProgress.vue 的样式**

```css
.rank-progress { margin: 6px 0; }
.rank-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-bottom: 3px;
}
.rank-start, .rank-target { color: #64748b; }
.rank-current { color: #1e293b; font-weight: 600; }
.rank-bar {
  background: #e2e8f0;
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}
.rank-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #3b82f6, #2563eb);
  border-radius: 4px;
  transition: width 0.5s ease;
}
```

- [ ] **Step 2: 构建验证**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add hearthbot-web/src/components/RankProgress.vue
git commit -m "style(web): RankProgress 改为蓝色渐变进度条"
```

---

### Task 10: 全量构建 + 部署产物更新

**Files:**
- Build output: `HearthBot.Cloud/wwwroot/`

- [ ] **Step 1: 全量构建**

Run: `cd hearthbot-web && npx vite build`
Expected: 构建成功，输出到 `../HearthBot.Cloud/wwwroot/`

- [ ] **Step 2: 提交构建产物**

```bash
git add HearthBot.Cloud/wwwroot/
git commit -m "build(web): 构建浅色主题前端产物"
```

- [ ] **Step 3: 推送到远程**

```bash
git push
```
