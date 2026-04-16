import { describe, expect, it } from 'vitest'
import { buildCompletedItems, buildDashboardCounts, getDeviceBucket, getDisplayStatus, isAbnormalDevice, isCompletionSuspected, pickAutoDashboardTab } from './dashboardState'

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

  it('counts live completed devices even when snapshots are missing', () => {
    const counts = buildDashboardCounts([
      {
        deviceId: 'pc-01',
        displayName: '一号机',
        orderNumber: 'A-1',
        status: 'Running',
        isCompleted: true,
        completedAt: '2026-04-16T12:00:00Z',
        completedRank: '传说',
        currentRank: '传说',
        currentAccount: '账号A',
        currentDeck: '卡组A',
        currentProfile: '策略A',
        gameMode: 'Standard',
        sessionWins: 12,
        sessionLosses: 3,
        startRank: '钻石5',
        targetRank: '传说',
        lastHeartbeat: '2026-04-16T12:01:00Z',
        availableDecksJson: '[]',
        availableProfilesJson: '[]',
        orderAccountName: '账号A',
        startedAt: '2026-04-16T09:00:00Z',
        currentOpponent: ''
      } as any
    ], [], Date.parse('2026-04-16T12:02:00Z'))

    expect(counts.completed).toBe(1)
  })

  it('deduplicates completed snapshots against the matching live device', () => {
    const items = buildCompletedItems([
      {
        deviceId: 'pc-01',
        displayName: '一号机',
        orderNumber: 'A-1',
        status: 'Running',
        isCompleted: true,
        completedAt: '2026-04-16T12:00:00Z',
        completedRank: '传说',
        currentRank: '传说',
        currentAccount: '账号A',
        currentDeck: '卡组A',
        currentProfile: '策略A',
        gameMode: 'Standard',
        sessionWins: 12,
        sessionLosses: 3,
        startRank: '钻石5',
        targetRank: '传说',
        lastHeartbeat: '2026-04-16T12:01:00Z',
        availableDecksJson: '[]',
        availableProfilesJson: '[]',
        orderAccountName: '账号A',
        startedAt: '2026-04-16T09:00:00Z',
        currentOpponent: ''
      } as any
    ], [
      {
        id: 7,
        deviceId: 'pc-01',
        displayName: '一号机',
        orderNumber: 'A-1',
        accountName: '账号A',
        startRank: '钻石5',
        targetRank: '传说',
        completedRank: '传说',
        deckName: '卡组A',
        profileName: '策略A',
        gameMode: 'Standard',
        wins: 12,
        losses: 3,
        completedAt: '2026-04-16T12:00:00Z',
        expiresAt: '2026-04-23T12:00:00Z',
        deletedAt: null
      }
    ])

    expect(items).toHaveLength(1)
    expect(items[0].id).toBe(7)
  })
})
