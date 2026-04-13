<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { NButton, NEmpty } from 'naive-ui'
import { completedOrderApi, deviceApi } from '../api'
import { useSignalR } from '../composables/useSignalR'
import type { CompletedOrderSnapshot, DashboardBucket, Device, Stats } from '../types'
import DashboardHeaderStats from '../components/dashboard/DashboardHeaderStats.vue'
import CompletionBanner from '../components/dashboard/CompletionBanner.vue'
import DeviceOverviewTabs from '../components/dashboard/DeviceOverviewTabs.vue'
import DeviceStatusCard from '../components/dashboard/DeviceStatusCard.vue'
import DeviceDetailDrawer from '../components/dashboard/DeviceDetailDrawer.vue'
import CompletedOrderCard from '../components/dashboard/CompletedOrderCard.vue'
import { completionNoticeKey, notifyCompletion, shouldNotifyCompletion } from '../utils/browserNotifications'
import { countDevicesByBucket, getDeviceBucket, isCompletionSuspected, sortDevicesForBucket } from '../utils/dashboardState'

interface CompletionBannerItem {
  key: string
  deviceId: string
  title: string
  detail: string
}

const NOTIFIED_COMPLETION_STORAGE_KEY = 'hearthbot-cloud.notified-completions'

const devices = ref<Device[]>([])
const completedSnapshots = ref<CompletedOrderSnapshot[]>([])
const stats = ref<Stats>({
  onlineCount: 0,
  totalCount: 0,
  todayGames: 0,
  todayWins: 0,
  todayLosses: 0,
  abnormalCount: 0,
  completedCount: 0
})
const firstLoad = ref(true)
const isLoading = ref(false)
const activeTab = ref<DashboardBucket>('active')
const detailDeviceId = ref<string | null>(null)
const detailOpen = ref(false)
const pendingHints = ref<Record<string, string>>({})
const recentCompletions = ref<CompletionBannerItem[]>([])
const nowTick = ref(Date.now())
const hiddenLiveKeys = ref<string[]>([])

let pollTimer: ReturnType<typeof setInterval> | null = null
let heartbeatTimer: ReturnType<typeof setInterval> | null = null

const notifiedCompletionKeys = new Set<string>(readNotifiedCompletionKeys())

function readNotifiedCompletionKeys(): string[] {
  if (typeof window === 'undefined') return []

  try {
    const raw = window.localStorage.getItem(NOTIFIED_COMPLETION_STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? parsed.filter((value): value is string => typeof value === 'string') : []
  } catch {
    return []
  }
}

function persistNotifiedCompletionKeys() {
  if (typeof window === 'undefined') return
  window.localStorage.setItem(NOTIFIED_COMPLETION_STORAGE_KEY, JSON.stringify([...notifiedCompletionKeys]))
}

function createPlaceholderDevice(deviceId: string, displayName: string): Device {
  return {
    deviceId,
    displayName,
    status: 'Idle',
    currentAccount: '',
    currentRank: '',
    currentDeck: '',
    currentProfile: '',
    gameMode: 'Standard',
    sessionWins: 0,
    sessionLosses: 0,
    lastHeartbeat: new Date().toISOString(),
    availableDecksJson: '[]',
    availableProfilesJson: '[]',
    orderNumber: '',
    orderAccountName: '',
    targetRank: '',
    startRank: '',
    startedAt: null,
    currentOpponent: '',
    isCompleted: false,
    completedAt: null,
    completedRank: ''
  }
}

function addCompletionBanner(device: Device) {
  const key = completionNoticeKey(device)
  const bannerItem: CompletionBannerItem = {
    key,
    deviceId: device.deviceId,
    title: `${device.displayName} 已完成 #${device.orderNumber}`,
    detail: `${device.currentAccount || '未知账号'} · ${device.completedRank || device.currentRank || '未知段位'}`
  }

  recentCompletions.value = [
    bannerItem,
    ...recentCompletions.value.filter(item => item.key !== key)
  ].slice(0, 4)
}

async function maybeNotifyCompletion(device: Device, allowNotify: boolean) {
  if (!allowNotify || !shouldNotifyCompletion(device, notifiedCompletionKeys)) return

  const key = completionNoticeKey(device)
  notifiedCompletionKeys.add(key)
  persistNotifiedCompletionKeys()
  addCompletionBanner(device)
  await notifyCompletion(device)
}

function mergeSingleDevice(incoming: Device, allowNotify: boolean) {
  const previous = devices.value.find(device => device.deviceId === incoming.deviceId)
  const hideKey = liveHideKey(incoming)

  if (
    previous &&
    previous.orderNumber &&
    !incoming.orderNumber &&
    previous.currentAccount &&
    incoming.currentAccount &&
    previous.currentAccount !== incoming.currentAccount
  ) {
    pendingHints.value[incoming.deviceId] = `检测到账号从 ${previous.currentAccount} 切换到 ${incoming.currentAccount}，旧订单已清空`
  } else if (incoming.orderNumber) {
    delete pendingHints.value[incoming.deviceId]
  }

  void maybeNotifyCompletion(incoming, allowNotify)

  if (hiddenLiveKeys.value.includes(hideKey)) {
    devices.value = devices.value.filter(device => device.deviceId !== incoming.deviceId)
    return
  }

  const index = devices.value.findIndex(device => device.deviceId === incoming.deviceId)
  if (index >= 0) {
    devices.value[index] = incoming
  } else {
    devices.value.push(incoming)
  }
}

function replaceDevices(nextDevices: Device[], allowNotify: boolean) {
  const previousById = new Map(devices.value.map(device => [device.deviceId, device]))
  const nextHints: Record<string, string> = { ...pendingHints.value }

  for (const device of nextDevices) {
    const previous = previousById.get(device.deviceId)
    if (
      previous &&
      previous.orderNumber &&
      !device.orderNumber &&
      previous.currentAccount &&
      device.currentAccount &&
      previous.currentAccount !== device.currentAccount
    ) {
      nextHints[device.deviceId] = `检测到账号从 ${previous.currentAccount} 切换到 ${device.currentAccount}，旧订单已清空`
    } else if (device.orderNumber) {
      delete nextHints[device.deviceId]
    }

    void maybeNotifyCompletion(device, allowNotify)
  }

  pendingHints.value = nextHints
  devices.value = nextDevices
}

async function loadData(allowNotify = false) {
  if (isLoading.value) return
  isLoading.value = true

  try {
    const [deviceResponse, statsResponse, completedOrderResponse] = await Promise.all([
      deviceApi.getAll(),
      deviceApi.getStats(),
      completedOrderApi.getAll()
    ])

    replaceDevices(deviceResponse.data, allowNotify && !firstLoad.value)
    stats.value = statsResponse.data
    completedSnapshots.value = completedOrderApiResponseToList(completedOrderResponse.data)
  } finally {
    isLoading.value = false
    firstLoad.value = false
  }
}

function liveHideKey(device: Pick<Device, 'deviceId' | 'currentAccount' | 'orderNumber'>): string {
  return [device.deviceId, device.currentAccount ?? '', device.orderNumber ?? ''].join('|')
}

function completedOrderApiResponseToList(data: unknown): CompletedOrderSnapshot[] {
  return Array.isArray(data) ? data as CompletedOrderSnapshot[] : []
}
const counts = computed(() => {
  const liveCounts = countDevicesByBucket(devices.value, nowTick.value)
  return {
    ...liveCounts,
    completed: completedSnapshots.value.length
  }
})

const headerStats = computed<Stats>(() => ({
  ...stats.value,
  completedCount: completedSnapshots.value.length
}))

const filteredDevices = computed(() => {
  const bucketDevices = devices.value.filter(device => getDeviceBucket(device, nowTick.value) === activeTab.value)
  const sorted = sortDevicesForBucket(bucketDevices, activeTab.value)

  if (activeTab.value === 'active') {
    sorted.sort((left, right) => Number(isCompletionSuspected(right)) - Number(isCompletionSuspected(left)))
  }

  return sorted
})

const filteredCompletedSnapshots = computed(() =>
  [...completedSnapshots.value].sort((left, right) => right.completedAt.localeCompare(left.completedAt))
)

const selectedDevice = computed(() =>
  devices.value.find(device => device.deviceId === detailDeviceId.value) ?? null
)

function openDevice(device: Device) {
  detailDeviceId.value = device.deviceId
  detailOpen.value = true
}

function dismissCompletion(key: string) {
  recentCompletions.value = recentCompletions.value.filter(item => item.key !== key)
}

function openCompletionDevice(deviceId: string) {
  const device = devices.value.find(item => item.deviceId === deviceId)
  if (!device) return
  openDevice(device)
}

async function saveOrder(deviceId: string, orderNumber: string) {
  const response = await deviceApi.setOrderNumber(deviceId, orderNumber)
  delete pendingHints.value[deviceId]
  mergeSingleDevice(response.data, false)
}

async function refreshFromDrawer() {
  await loadData(true)
}

async function hideLiveDevice(device: Device) {
  await deviceApi.hide(device.deviceId, device.currentAccount, device.orderNumber)
  hiddenLiveKeys.value = [
    ...hiddenLiveKeys.value.filter(key => !key.startsWith(`${device.deviceId}|`)),
    liveHideKey(device)
  ]
  devices.value = devices.value.filter(item => item.deviceId !== device.deviceId)
  await loadData(false)
}

async function hideCompletedSnapshot(id: number) {
  await completedOrderApi.hide(id)
  completedSnapshots.value = completedSnapshots.value.filter(snapshot => snapshot.id !== id)
  stats.value = {
    ...stats.value,
    completedCount: Math.max(0, stats.value.completedCount - 1)
  }
}

onMounted(async () => {
  await loadData(false)

  const { connect } = useSignalR()
  const hub = connect()

  hub.on('DeviceUpdated', (device: Device) => {
    mergeSingleDevice(device, true)
  })

  hub.on('DeviceOnline', (deviceId: string, displayName: string) => {
    mergeSingleDevice(createPlaceholderDevice(deviceId, displayName), false)
  })

  hub.on('DeviceOffline', (deviceId: string) => {
    const device = devices.value.find(item => item.deviceId === deviceId)
    if (!device) return
    mergeSingleDevice({ ...device, status: 'Offline' }, false)
  })

  hub.on('NewGameRecord', () => {
    void loadData(true)
  })

  pollTimer = setInterval(() => {
    void loadData(true)
  }, 60_000)

  heartbeatTimer = setInterval(() => {
    nowTick.value = Date.now()
  }, 30_000)
})

onUnmounted(() => {
  if (pollTimer) clearInterval(pollTimer)
  if (heartbeatTimer) clearInterval(heartbeatTimer)
})
</script>

<template>
  <div class="dashboard-page">
    <section class="hero-shell">
      <div class="hero-copy">
        <div class="hero-label">Cloud Ops</div>
        <h1>云控巡检台</h1>
        <p>先看完成、异常和待录单，再做少量关键操作。页面按手机高频巡检重新组织。</p>
      </div>
      <NButton tertiary type="primary" @click="loadData(true)">立即刷新</NButton>
    </section>

    <DashboardHeaderStats :stats="headerStats" :loading="firstLoad" />

    <CompletionBanner
      :items="recentCompletions"
      @dismiss="dismissCompletion"
      @open="openCompletionDevice"
    />

    <DeviceOverviewTabs
      :counts="counts"
      :active-tab="activeTab"
      @change="activeTab = $event"
    />

    <section class="list-shell">
      <template v-if="activeTab !== 'completed'">
        <DeviceStatusCard
          v-for="device in filteredDevices"
          :key="device.deviceId"
          :device="device"
          :bucket="getDeviceBucket(device, nowTick)"
          :suspected-completion="isCompletionSuspected(device)"
          :hint-text="pendingHints[device.deviceId]"
          @open="openDevice"
          @save-order="saveOrder"
          @hide="hideLiveDevice"
        />
      </template>

      <template v-else>
        <CompletedOrderCard
          v-for="snapshot in filteredCompletedSnapshots"
          :key="snapshot.id"
          :snapshot="snapshot"
          @hide="hideCompletedSnapshot"
        />
      </template>

      <div v-if="activeTab !== 'completed' && !filteredDevices.length" class="empty-state">
        <NEmpty :description="activeTab === 'active' ? '当前没有进行中的设备' : activeTab === 'pending' ? '当前没有待录单设备' : '当前没有异常设备'" />
      </div>

      <div v-if="activeTab === 'completed' && !filteredCompletedSnapshots.length" class="empty-state">
        <NEmpty description="最近7天没有已完成订单" />
      </div>
    </section>

    <DeviceDetailDrawer
      v-model:show="detailOpen"
      :device="selectedDevice"
      @refresh="refreshFromDrawer"
      @hide="hideLiveDevice"
    />
  </div>
</template>

<style scoped>
.dashboard-page {
  min-height: 100%;
  padding: 18px 14px 28px;
  background:
    radial-gradient(circle at top left, rgba(56, 189, 248, 0.14), transparent 24%),
    radial-gradient(circle at bottom right, rgba(251, 191, 36, 0.14), transparent 24%),
    linear-gradient(180deg, #f8fafc, #eef2f7 46%, #f8fafc);
}

.hero-shell {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}

.hero-label {
  color: #2563eb;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

.hero-copy h1 {
  margin: 8px 0 6px;
  color: #0f172a;
  font-size: 30px;
  line-height: 1.08;
}

.hero-copy p {
  margin: 0;
  color: #475569;
  font-size: 14px;
  line-height: 1.55;
  max-width: 640px;
}

.list-shell {
  display: grid;
  gap: 14px;
}

.empty-state {
  padding: 40px 12px;
  border-radius: 22px;
  background: rgba(255, 255, 255, 0.74);
  border: 1px solid rgba(15, 23, 42, 0.08);
}

@media (min-width: 860px) {
  .dashboard-page {
    padding: 24px 24px 36px;
  }

  .list-shell {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (min-width: 1200px) {
  .list-shell {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}
</style>
