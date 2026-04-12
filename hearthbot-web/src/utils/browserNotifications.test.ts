import { describe, expect, it } from 'vitest'
import { completionNoticeKey, shouldNotifyCompletion } from './browserNotifications'

describe('browserNotifications', () => {
  it('builds a stable completion dedupe key', () => {
    expect(completionNoticeKey({
      deviceId: 'pc-01',
      orderNumber: 'A-1',
      completedAt: '2026-04-12T12:00:00Z'
    } as any)).toBe('pc-01|A-1|2026-04-12T12:00:00Z')
  })

  it('dedupes the same completed order', () => {
    const device = {
      deviceId: 'pc-01',
      orderNumber: 'A-1',
      completedAt: '2026-04-12T12:00:00Z',
      isCompleted: true
    } as any

    expect(shouldNotifyCompletion(device, new Set())).toBe(true)
    expect(shouldNotifyCompletion(device, new Set(['pc-01|A-1|2026-04-12T12:00:00Z']))).toBe(false)
  })
})
