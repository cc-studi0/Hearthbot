const TIER_BASE: Record<string, number> = {
  Bronze: 0,
  Silver: 10,
  Gold: 20,
  Platinum: 30,
  Diamond: 40
}

export function rankToNumber(rank: string): number | null {
  if (!rank) return null
  const trimmed = rank.trim()
  if (trimmed.toLowerCase().startsWith('legend')) return 51

  const match = trimmed.match(/^(\w+)\s+(\d+)$/)
  if (!match) return null

  const tier = match[1]
  const star = parseInt(match[2], 10)
  const base = TIER_BASE[tier]
  if (base === undefined || star < 1 || star > 10) return null

  return base + (11 - star)
}

export function numberToRank(n: number): string {
  if (n >= 51) return 'Legend'
  for (const [tier, base] of Object.entries(TIER_BASE).sort((a, b) => b[1] - a[1])) {
    if (n > base) return `${tier} ${11 - (n - base)}`
  }
  return ''
}
