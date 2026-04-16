import { describe, expect, it } from 'vitest'
import { getDeviceBucket, getDisplayStatus, isAbnormalDevice, isCompletionSuspected, pickAutoDashboardTab } from './dashboardState'

describe('dashboardState', () => {
  it('puts a device without an order into pending', () => {
    expect(getDeviceBucket({
      orderNumber: '',
      status: 'Running',
      isCompleted: false
    } as any)).toBe('pending')
  })

  it('keeps formally completed devices in completed even when offline', () => {
    expect(getDeviceBucket({
      orderNumber: 'A-2',
      status: 'Offline',
      isCompleted: true
    } as any)).toBe('completed')
  })

  it('marks target reached without formal completion as suspected', () => {
    expect(isCompletionSuspected({
      orderNumber: 'A-1',
      isCompleted: false,
      currentRank: '传说',
      targetRank: '钻石5'
    } as any)).toBe(true)
  })

  it('prefers backend bucket when present', () => {
    expect(getDeviceBucket({
      bucket: 'active',
      orderNumber: '',
      status: 'Offline',
      isCompleted: false
    } as any)).toBe('active')
  })

  it('treats switching as normal when backend says active', () => {
    expect(isAbnormalDevice({
      bucket: 'active',
      displayStatus: 'Switching',
      status: 'Switching',
      isCompleted: false
    } as any)).toBe(false)
  })

  it('falls back to legacy timeout logic when backend fields are missing', () => {
    expect(getDeviceBucket({
      orderNumber: 'A-1',
      status: 'Offline',
      isCompleted: false
    } as any)).toBe('abnormal')
  })

  it('prefers backend display status over raw status', () => {
    expect(getDisplayStatus({
      displayStatus: 'Offline',
      status: 'Running'
    } as any)).toBe('Offline')
  })

  it('switches away from active when only pending devices exist', () => {
    expect(pickAutoDashboardTab('active', {
      active: 0,
      pending: 4,
      abnormal: 0,
      completed: 0
    })).toBe('pending')
  })

  it('keeps the current tab when it still has devices', () => {
    expect(pickAutoDashboardTab('abnormal', {
      active: 2,
      pending: 1,
      abnormal: 1,
      completed: 0
    })).toBe('abnormal')
  })
})
