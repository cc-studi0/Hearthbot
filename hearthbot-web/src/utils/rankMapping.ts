const TIER_BASE: Record<string, number> = {
  青铜: 0,
  白银: 10,
  黄金: 20,
  白金: 30,
  钻石: 40,
  Bronze: 0,
  Silver: 10,
  Gold: 20,
  Platinum: 30,
  Diamond: 40
}

const ORDERED_TIERS = ['钻石', '白金', '黄金', '白银', '青铜'] as const

function normalizeEnglishTier(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase()
}

export function rankToNumber(rank: string): number | null {
  if (!rank) return null

  const trimmed = rank.trim()
  if (!trimmed) return null

  if (/^(legend|传说)(\s+\d+名)?$/i.test(trimmed)) return 51

  const normalized = trimmed.replace(/\s+\d+星$/u, '')
  const chineseMatch = normalized.match(/^(青铜|白银|黄金|白金|钻石)\s?(\d+)$/u)
  if (chineseMatch) {
    const tier = chineseMatch[1]
    const star = parseInt(chineseMatch[2], 10)
    const base = TIER_BASE[tier]
    return star >= 1 && star <= 10 ? base + (11 - star) : null
  }

  const englishMatch = normalized.match(/^([A-Za-z]+)\s+(\d+)$/)
  if (!englishMatch) return null

  const tier = normalizeEnglishTier(englishMatch[1])
  const star = parseInt(englishMatch[2], 10)
  const base = TIER_BASE[tier]
  if (base === undefined || star < 1 || star > 10) return null

  return base + (11 - star)
}

export function numberToRank(n: number): string {
  if (n >= 51) return '传说'

  for (const tier of ORDERED_TIERS) {
    const base = TIER_BASE[tier]
    if (n > base) return `${tier}${11 - (n - base)}`
  }

  return ''
}
