import { describe, expect, it } from 'vitest'
import { rankToNumber } from './rankMapping'

describe('rankToNumber', () => {
  it('parses Chinese ranks from RankHelper', () => {
    expect(rankToNumber('钻石5')).toBe(46)
    expect(rankToNumber('传说')).toBe(51)
  })

  it('parses RankHelper legend labels with index suffix', () => {
    expect(rankToNumber('传说 123名')).toBe(51)
  })
})
