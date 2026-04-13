import { describe, expect, it } from 'vitest'
import { getRemainingRetentionDays } from './completedHistory'

describe('completedHistory', () => {
  it('rounds up the remaining retention window for active snapshots', () => {
    expect(getRemainingRetentionDays('2026-04-20T12:00:00Z', new Date('2026-04-13T13:00:00Z'))).toBe(7)
  })

  it('returns zero for expired snapshots', () => {
    expect(getRemainingRetentionDays('2026-04-13T12:00:00Z', new Date('2026-04-13T13:00:00Z'))).toBe(0)
  })
})
