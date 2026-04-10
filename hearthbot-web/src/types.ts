export interface Device {
  deviceId: string
  displayName: string
  status: string
  currentAccount: string
  currentRank: string
  currentDeck: string
  currentProfile: string
  gameMode: string
  sessionWins: number
  sessionLosses: number
  lastHeartbeat: string
  availableDecksJson: string
  availableProfilesJson: string
  orderNumber: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
  isCompleted: boolean
  completedAt: string | null
  completedRank: string
}

export interface Stats {
  onlineCount: number
  totalCount: number
  todayGames: number
  todayWins: number
  todayLosses: number
  abnormalCount: number
  completedCount: number
}
