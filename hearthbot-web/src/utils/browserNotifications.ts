import type { Device } from '../types'

type NotificationPermissionState = NotificationPermission | 'unsupported'

function getAudioContextCtor(): (new () => AudioContext) | null {
  if (typeof window === 'undefined') return null
  return (window.AudioContext ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext ?? null) as (new () => AudioContext) | null
}

export function completionNoticeKey(device: Pick<Device, 'deviceId' | 'orderNumber' | 'completedAt'>): string {
  return [device.deviceId, device.orderNumber, device.completedAt ?? ''].join('|')
}

export function shouldNotifyCompletion(device: Device, seenKeys: Set<string>): boolean {
  if (!device.isCompleted || !device.orderNumber || !device.completedAt) return false
  return !seenKeys.has(completionNoticeKey(device))
}

export async function requestBrowserNotificationPermission(): Promise<NotificationPermissionState> {
  if (typeof Notification === 'undefined') return 'unsupported'
  if (Notification.permission === 'granted') return 'granted'
  if (Notification.permission === 'denied') return 'denied'
  return Notification.requestPermission()
}

export function playCompletionTone(): boolean {
  const AudioContextCtor = getAudioContextCtor()
  if (!AudioContextCtor) return false

  const audioContext = new AudioContextCtor()
  const oscillator = audioContext.createOscillator()
  const gain = audioContext.createGain()

  oscillator.type = 'triangle'
  oscillator.frequency.setValueAtTime(880, audioContext.currentTime)
  gain.gain.setValueAtTime(0.001, audioContext.currentTime)
  gain.gain.exponentialRampToValueAtTime(0.08, audioContext.currentTime + 0.02)
  gain.gain.exponentialRampToValueAtTime(0.001, audioContext.currentTime + 0.28)

  oscillator.connect(gain)
  gain.connect(audioContext.destination)
  oscillator.start()
  oscillator.stop(audioContext.currentTime + 0.3)
  oscillator.onended = () => {
    void audioContext.close()
  }

  return true
}

export async function notifyCompletion(device: Device): Promise<boolean> {
  const permission = await requestBrowserNotificationPermission()
  if (permission !== 'granted' || typeof Notification === 'undefined') {
    playCompletionTone()
    return false
  }

  new Notification(`订单完成 · ${device.displayName}`, {
    body: `订单号 ${device.orderNumber} · 账号 ${device.currentAccount} · 段位 ${device.completedRank || device.currentRank}`,
    tag: completionNoticeKey(device)
  })

  playCompletionTone()
  return true
}
