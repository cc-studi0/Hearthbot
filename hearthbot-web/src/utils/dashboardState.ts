import type { CompletedOrderSnapshot, DashboardBucket, DashboardCompletedItem, DashboardCounts, Device } from '../types'
import { rankToNumber } from './rankMapping'

const HEARTBEAT_TIMEOUT_MS = 90_000

function parseHeartbeat(value: string): number | null {
  if (!value) return null
  const ms = Date.parse(value)
  return Number.isNaN(ms) ? null : ms
}

export function isHeartbeatStale(device: Device, now = Date.now()): boolean {
  if (typeof device.isHeartbeatStale === 'boolean') return device.isHeartbeatStale
  if (!device.lastHeartbeat || device.status === 'Offline' || device.isCompleted) return false
  const lastHeartbeat = parseHeartbeat(device.lastHeartbeat)
  if (lastHeartbeat === null) return false
  return now - lastHeartbeat > HEARTBEAT_TIMEOUT_MS
}

export function getDisplayStatus(device: Device): string {
  return device.displayStatus || device.status || 'Unknown'
}

export function isAbnormalDevice(device: Device, now = Date.now()): boolean {
  if (device.isCompleted) return false
  if (device.bucket) return device.bucket === 'abnormal'
  return device.status === 'Offline' || device.status === 'Switching' || isHeartbeatStale(device, now)
}

export function isCompletionSuspected(device: Device): boolean {
  const current = rankToNumber(device.currentRank)
  const target = rankToNumber(device.targetRank)

  return Boolean(device.orderNumber)
    && !device.isCompleted
    && current !== null
    && target !== null
    && current >= target
}

export function getDeviceBucket(device: Device, now = Date.now()): DashboardBucket {
  if (device.bucket) return device.bucket
  if (device.isCompleted) return 'completed'
  if (isAbnormalDevice(device, now)) return 'abnormal'
  if (!device.orderNumber) return 'pending'
  return 'active'
}

export function countDevicesByBucket(devices: Device[], now = Date.now()): DashboardCounts {
  return devices.reduce<DashboardCounts>((acc, device) => {
    acc[getDeviceBucket(device, now)] += 1
    return acc
  }, {
    active: 0,
    pending: 0,
    abnormal: 0,
    completed: 0
  })
}

function getCompletedItemKey(item: Pick<Device, 'deviceId' | 'orderNumber' | 'completedAt'> | Pick<CompletedOrderSnapshot, 'deviceId' | 'orderNumber' | 'completedAt'>): string {
  return [item.deviceId, item.orderNumber ?? '', item.completedAt ?? ''].join('|')
}

function buildLiveCompletedFallbacks(devices: Device[], snapshots: CompletedOrderSnapshot[]): DashboardCompletedItem[] {
  const snapshotKeys = new Set(snapshots.map(snapshot => getCompletedItemKey(snapshot)))
  let fallbackId = -1

  return devices
    .filter(device => device.isCompleted && Boolean(device.completedAt) && Boolean(device.orderNumber))
    .filter(device => !snapshotKeys.has(getCompletedItemKey(device)))
    .map(device => {
      const completedAt = device.completedAt ?? new Date().toISOString()
      const expiresAt = new Date(Date.parse(completedAt) + 7 * 86_400_000).toISOString()

      return {
        id: fallbackId--,
        deviceId: device.deviceId,
        displayName: device.displayName,
        orderNumber: device.orderNumber,
        accountName: device.currentAccount,
        startRank: device.startRank,
        targetRank: device.targetRank,
        completedRank: device.completedRank || device.currentRank,
        deckName: device.currentDeck,
        profileName: device.currentProfile,
        gameMode: device.gameMode,
        wins: device.sessionWins,
        losses: device.sessionLosses,
        completedAt,
        expiresAt,
        deletedAt: null,
        source: 'live'
      } satisfies DashboardCompletedItem
    })
}

export function buildCompletedItems(devices: Device[], snapshots: CompletedOrderSnapshot[]): DashboardCompletedItem[] {
  const snapshotItems = snapshots.map(snapshot => ({
    ...snapshot,
    source: 'snapshot'
  } satisfies DashboardCompletedItem))

  return [...snapshotItems, ...buildLiveCompletedFallbacks(devices, snapshots)]
    .sort((left, right) => right.completedAt.localeCompare(left.completedAt))
}

export function buildDashboardCounts(devices: Device[], snapshots: CompletedOrderSnapshot[], now = Date.now()): DashboardCounts {
  const liveCounts = countDevicesByBucket(devices, now)
  return {
    ...liveCounts,
    completed: buildCompletedItems(devices, snapshots).length
  }
}

const DASHBOARD_TAB_ORDER: DashboardBucket[] = ['active', 'pending', 'abnormal', 'completed']

export function pickAutoDashboardTab(currentTab: DashboardBucket, counts: DashboardCounts): DashboardBucket {
  if (counts[currentTab] > 0) return currentTab

  for (const tab of DASHBOARD_TAB_ORDER) {
    if (counts[tab] > 0) return tab
  }

  return currentTab
}

export function sortDevicesForBucket(devices: Device[], bucket: DashboardBucket): Device[] {
  return [...devices].sort((left, right) => {
    if (bucket === 'completed') {
      return (right.completedAt ?? '').localeCompare(left.completedAt ?? '')
    }

    if (bucket === 'pending') {
      return left.displayName.localeCompare(right.displayName, 'zh-CN')
    }

    if (bucket === 'abnormal') {
      return (right.lastHeartbeat ?? '').localeCompare(left.lastHeartbeat ?? '')
    }

    return left.displayName.localeCompare(right.displayName, 'zh-CN')
  })
}
