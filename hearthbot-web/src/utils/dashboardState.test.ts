import { describe, expect, it } from 'vitest'
import { getDeviceBucket, isCompletionSuspected } from './dashboardState'

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
})
