export function getRemainingRetentionDays(expiresAt: string, now = new Date()): number {
  const expiresAtMs = new Date(expiresAt).getTime()
  if (Number.isNaN(expiresAtMs)) return 0

  const diff = expiresAtMs - now.getTime()
  if (diff <= 0) return 0

  return Math.ceil(diff / 86_400_000)
}
