export interface Device {
  deviceId: string
  displayName: string
  status: string
  rawStatus?: string
  displayStatus?: string
  bucket?: DashboardBucket
  abnormalReason?: string | null
  heartbeatAgeSeconds?: number
  isHeartbeatStale?: boolean
  isSwitchingTooLong?: boolean
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
  orderAccountName: string
  targetRank: string
  startRank: string
  startedAt: string | null
  currentOpponent: string
  isCompleted: boolean
  completedAt: string | null
  completedRank: string
}

export interface CompletedOrderSnapshot {
  id: number
  deviceId: string
  displayName: string
  orderNumber: string
  accountName: string
  startRank: string
  targetRank: string
  completedRank: string
  deckName: string
  profileName: string
  gameMode: string
  wins: number
  losses: number
  completedAt: string
  expiresAt: string
  deletedAt: string | null
}

export type DashboardBucket = 'active' | 'pending' | 'abnormal' | 'completed'

export interface DashboardCounts {
  active: number
  pending: number
  abnormal: number
  completed: number
}

export interface DashboardDeviceState {
  bucket: DashboardBucket
  suspectedCompletion: boolean
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
