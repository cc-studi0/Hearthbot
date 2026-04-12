export interface RankOption {
  label: string
  value: number
}

const TIER_NAMES = ['青铜', '白银', '黄金', '白金', '钻石'] as const

export function buildTargetRankOptions(withDisabled = false): RankOption[] {
  const options: RankOption[] = []

  if (withDisabled) {
    options.push({ label: '关闭', value: 0 })
  }

  options.push({ label: '传说', value: 51 })

  for (let tierIndex = TIER_NAMES.length - 1; tierIndex >= 0; tierIndex -= 1) {
    for (let rankNumber = 1; rankNumber <= 10; rankNumber += 1) {
      options.push({
        label: `${TIER_NAMES[tierIndex]}${rankNumber}`,
        value: tierIndex * 10 + (11 - rankNumber)
      })
    }
  }

  return options
}
