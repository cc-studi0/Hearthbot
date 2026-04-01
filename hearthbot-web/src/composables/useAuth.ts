import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '../api'

export function useAuth() {
  const router = useRouter()
  const loading = ref(false)
  const error = ref('')

  async function login(username: string, password: string) {
    loading.value = true
    error.value = ''
    try {
      const { data } = await authApi.login(username, password)
      localStorage.setItem('token', data.token)
      router.push('/')
    } catch (e: any) {
      error.value = e.response?.data?.error || '登录失败'
    } finally {
      loading.value = false
    }
  }

  function logout() {
    localStorage.removeItem('token')
    router.push('/login')
  }

  return { login, logout, loading, error }
}
