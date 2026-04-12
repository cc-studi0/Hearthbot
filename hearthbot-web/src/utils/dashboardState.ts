import type { DashboardBucket, DashboardCounts, Device } from '../types'
import { rankToNumber } from './rankMapping'

const HEARTBEAT_TIMEOUT_MS = 90_000

function parseHeartbeat(value: string): number | null {
  if (!value) return null
  const ms = Date.parse(value)
  return Number.isNaN(ms) ? null : ms
}

export function isHeartbeatStale(device: Device, now = Date.now()): boolean {
  if (!device.lastHeartbeat || device.status === 'Offline' || device.isCompleted) return false
  const lastHeartbeat = parseHeartbeat(device.lastHeartbeat)
  if (lastHeartbeat === null) return false
  return now - lastHeartbeat > HEARTBEAT_TIMEOUT_MS
}

export function isAbnormalDevice(device: Device, now = Date.now()): boolean {
  if (device.isCompleted) return false
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
