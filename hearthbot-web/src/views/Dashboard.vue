<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { NLayout, NLayoutHeader, NLayoutContent, NSpace, NButton, NMenu } from 'naive-ui'
import { useRouter } from 'vue-router'
import { deviceApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import { useAuth } from '../composables/useAuth'
import StatsBar from '../components/StatsBar.vue'
import KanbanBoard from '../components/KanbanBoard.vue'
import type { Device, Stats } from '../types'

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
