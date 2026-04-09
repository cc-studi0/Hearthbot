import { ref, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'

export function useSignalR() {
  const connection = ref<HubConnection | null>(null)
  const connected = ref(false)

  function connect() {
    const token = localStorage.getItem('token') || ''
    const hub = new HubConnectionBuilder()
      .withUrl('/hub/dashboard', { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    hub.onreconnected(() => { connected.value = true })
    hub.onreconnecting(() => { connected.value = false })
    hub.onclose(() => { connected.value = false })

    hub.start()
      .then(() => { connected.value = true })
      .catch(err => { console.error('[SignalR] 连接失败:', err) })
    connection.value = hub
    return hub
  }

  onUnmounted(() => {
    connection.value?.stop()
  })

  return { connection, connected, connect }
}
