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
  get: (id: string) => api.get(`/device/${id}`)
}

export const gameRecordApi = {
  getAll: (params: Record<string, any>) => api.get('/gamerecord', { params })
}

export const commandApi = {
  send: (deviceId: string, commandType: string, payload: Record<string, any>) =>
    api.post('/command', { deviceId, commandType, payload: JSON.stringify(payload) })
}
