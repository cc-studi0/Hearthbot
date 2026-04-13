import axios from 'axios'

const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  r => r,
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)

export const authApi = {
  login: (username: string, password: string) =>
    api.post<{ token: string }>('/auth/login', { username, password })
}

export const deviceApi = {
  getAll: () => api.get('/device'),
  getStats: () => api.get('/device/stats'),
  get: (id: string) => api.get(`/device/${id}`),
  setOrderNumber: (id: string, orderNumber: string) =>
    api.put(`/device/${id}/order-number`, { orderNumber }),
  hide: (id: string, currentAccount: string, orderNumber: string) =>
    api.post(`/device/${id}/hide`, { currentAccount, orderNumber }),
  markCompleted: (id: string) =>
    api.post(`/device/${id}/complete`)
}

export const gameRecordApi = {
  getAll: (params: Record<string, any>) => api.get('/gamerecord', { params }),
  getAccounts: (params?: Record<string, any>) => api.get<string[]>('/gamerecord/accounts', { params }),
  getStats: (params: Record<string, any>) => api.get('/gamerecord/stats', { params }),
  byDevice: () => api.get('/gamerecord/by-device'),
  getByDeviceId: (deviceId: string, page = 1, pageSize = 5) =>
    api.get('/gamerecord', { params: { deviceId, days: 0, page, pageSize } })
}

export const commandApi = {
  send: (deviceId: string, commandType: string, payload: Record<string, any>) =>
    api.post('/command', { deviceId, commandType, payload: JSON.stringify(payload) }),
  start: (deviceId: string) =>
    api.post('/command', { deviceId, commandType: 'Start', payload: '{}' }),
  stop: (deviceId: string) =>
    api.post('/command', { deviceId, commandType: 'Stop', payload: '{}' }),
  changeDeck: (deviceId: string, deckName: string) =>
    api.post('/command', { deviceId, commandType: 'ChangeDeck', payload: JSON.stringify({ DeckName: deckName }) }),
  changeProfile: (deviceId: string, profileName: string) =>
    api.post('/command', { deviceId, commandType: 'ChangeProfile', payload: JSON.stringify({ ProfileName: profileName }) }),
  changeTarget: (deviceId: string, targetRankStarLevel: number) =>
    api.post('/command', { deviceId, commandType: 'ChangeTarget', payload: JSON.stringify({ TargetRankStarLevel: targetRankStarLevel }) })
}

export const completedOrderApi = {
  getAll: () => api.get('/completedorder'),
  hide: (id: number) => api.post(`/completedorder/${id}/hide`)
}
