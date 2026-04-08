<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, h } from 'vue'
import {
  NLayout, NLayoutHeader, NLayoutContent, NSpace, NCard, NStatistic,
  NDataTable, NTag, NButton, NModal, NSelect, NGrid, NGi, NMenu, NInput,
  NSkeleton
} from 'naive-ui'
import { useRouter } from 'vue-router'
import { deviceApi, commandApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import { useAuth } from '../composables/useAuth'

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
}

interface Stats {
  onlineCount: number
  totalCount: number
  todayGames: number
  todayWins: number
  todayLosses: number
  abnormalCount: number
}

const router = useRouter()
const { logout } = useAuth()
const devices = ref<Device[]>([])
const stats = ref<Stats>({ onlineCount: 0, totalCount: 0, todayGames: 0, todayWins: 0, todayLosses: 0, abnormalCount: 0 })
const showManage = ref(false)
const selectedDevice = ref<Device | null>(null)
const selectedDeck = ref('')
const editingOrderNumber = ref('')
const isLoading = ref(false)
const firstLoad = ref(true)
let pollTimer: ReturnType<typeof setInterval> | null = null

const todayWinRate = computed(() => {
  const total = stats.value.todayWins + stats.value.todayLosses
  return total > 0 ? ((stats.value.todayWins / total) * 100).toFixed(1) + '%' : '-'
})

const columns = [
  { title: '设备名', key: 'displayName', width: 100 },
  {
    title: '状态', key: 'status', width: 80,
    render: (row: Device) => {
      const map: Record<string, 'success' | 'warning' | 'error' | 'default'> = { Online: 'success', InGame: 'success', Idle: 'warning', Offline: 'error' }
      const label: Record<string, string> = { Online: '在线', InGame: '对局中', Idle: '空闲', Offline: '离线' }
      return h(NTag, { type: map[row.status] || 'default', size: 'small' }, () => label[row.status] || row.status)
    }
  },
  { title: '订单号', key: 'orderNumber', width: 120 },
  { title: '昵称', key: 'currentAccount', width: 100 },
  { title: '段位', key: 'currentRank', width: 100 },
  {
    title: '胜/负', key: 'stats', width: 80,
    render: (row: Device) => `${row.sessionWins} / ${row.sessionLosses}`
  },
  {
    title: '胜率', key: 'winRate', width: 70,
    render: (row: Device) => {
      const total = row.sessionWins + row.sessionLosses
      return total > 0 ? ((row.sessionWins / total) * 100).toFixed(1) + '%' : '-'
    }
  },
  { title: '卡组', key: 'currentDeck', width: 100 },
  { title: '策略', key: 'currentProfile', width: 100 },
  {
    title: '操作', key: 'actions', width: 80,
    render: (row: Device) =>
      row.status !== 'Offline'
        ? h(NButton, { size: 'small', onClick: () => openManage(row) }, () => '管理')
        : h(NTag, { size: 'small' }, () => '离线')
  }
]

function openManage(device: Device) {
  selectedDevice.value = device
  selectedDeck.value = device.currentDeck
  editingOrderNumber.value = device.orderNumber || ''
  showManage.value = true
}

async function sendCommand(type: string, payload: Record<string, any> = {}) {
  if (!selectedDevice.value) return
  await commandApi.send(selectedDevice.value.deviceId, type, payload)
}

async function changeDeck() {
  await sendCommand('ChangeDeck', { DeckName: selectedDeck.value })
}

async function saveOrderNumber() {
  if (!selectedDevice.value) return
  const res = await deviceApi.setOrderNumber(selectedDevice.value.deviceId, editingOrderNumber.value)
  const idx = devices.value.findIndex(d => d.deviceId === selectedDevice.value!.deviceId)
  if (idx >= 0) devices.value[idx] = res.data
  selectedDevice.value = res.data
}

function getAvailableDecks(device: Device) {
  try { return JSON.parse(device.availableDecksJson || '[]') as string[] } catch { return [] }
}

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
    if (selectedDevice.value?.deviceId === device.deviceId)
      selectedDevice.value = device
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

  // 每 60 秒全量同步兜底，确保漏消息时也能自愈
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

      <NCard title="设备实时状态" v-if="firstLoad">
        <NSkeleton text :repeat="8" />
      </NCard>
      <NCard title="设备实时状态" v-else>
        <NDataTable :columns="columns" :data="devices" :row-key="(r: Device) => r.deviceId" />
      </NCard>
    </NLayoutContent>
  </NLayout>

  <NModal v-model:show="showManage" preset="card" :title="`设备管理 — ${selectedDevice?.displayName}`"
    style="width:600px">
    <template v-if="selectedDevice">
      <NGrid :cols="2" :x-gap="16">
        <NGi>
          <h4>当前状态</h4>
          <p>状态: {{ selectedDevice.status }}</p>
          <p>昵称: {{ selectedDevice.currentAccount }}</p>
          <p>段位: {{ selectedDevice.currentRank }}</p>
          <p>卡组: {{ selectedDevice.currentDeck }}</p>
          <p>策略: {{ selectedDevice.currentProfile }}</p>
          <p>模式: {{ selectedDevice.gameMode }}</p>
          <p>胜/负: {{ selectedDevice.sessionWins }} / {{ selectedDevice.sessionLosses }}</p>
        </NGi>
        <NGi>
          <h4>远程操作</h4>
          <div style="margin-bottom:12px">
            <label>订单号</label>
            <NSpace>
              <NInput v-model:value="editingOrderNumber" placeholder="输入订单号" style="width:200px" />
              <NButton type="primary" size="small" @click="saveOrderNumber">保存</NButton>
            </NSpace>
          </div>
          <div style="margin-bottom:12px">
            <label>切换卡组</label>
            <NSpace>
              <NSelect v-model:value="selectedDeck" :options="getAvailableDecks(selectedDevice).map((d: string) => ({label:d,value:d}))"
                style="width:200px" />
              <NButton type="primary" size="small" @click="changeDeck">应用</NButton>
            </NSpace>
          </div>
          <NSpace style="margin-top:16px">
            <NButton type="success" @click="sendCommand('Start')">开始</NButton>
            <NButton type="error" @click="sendCommand('Stop')">停止</NButton>
          </NSpace>
        </NGi>
      </NGrid>
    </template>
  </NModal>
</template>
