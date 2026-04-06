# 内存泄漏监控 + 网络异常检测 设计文档

**日期：** 2026-04-06  
**状态：** 待实施

---

## 背景

对比 SmartBot 的 ReloggerImproved，当前 Hearthbot 缺少两项监控能力：

1. **内存泄漏检测** — 炉石长时间运行后内存持续增长，无自动干预
2. **网络异常检测** — 炉石与暴雪服务器断连后无感知，Bot 空转

## 设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 内存异常处理 | 先 GC 再重启 | Payload 已有 GC 能力，避免不必要的重启 |
| 网络检测范围 | Pipe 断开 + 游戏服务器断连 | Payload 可直接访问游戏内部网络状态 |
| 内存阈值策略 | 增量检测 | 监控单位时间内增长量，比固定绝对值更灵敏 |
| 网络状态获取 | Payload 端检测 + Pipe 上报 | Payload 在游戏进程内，可访问 Network API |
| 架构方式 | 独立监控器 + Watchdog 集成 | 职责隔离，不膨胀 Watchdog |

---

## 模块 1：MemoryMonitor

### 职责

定期采集炉石进程内存，检测增量异常，先触发 GC 清理，失败后通知 Watchdog 重启。

### 采集方式

- 主程序侧通过 `Process.WorkingSet64` 采集炉石进程内存占用
- 每 30 秒采集一次，记录滑动窗口（保留最近 20 个采样点，即 10 分钟）

### 增量检测逻辑

- 比较窗口内最早和最新采样点的差值
- 如果 10 分钟内增长 > `GrowthThresholdMB`（默认 500MB），判定为内存泄漏
- 阈值可配置

### 处理流程

```
检测到增量异常
  → 通过 Pipe 发送 "GC" 命令给 Payload
  → Payload 执行 UnloadUnusedAssets() + GC.Collect()
  → 等待 30 秒后重新采样
  → 如果内存回落 > 100MB → 记录日志，重置窗口，继续监控
  → 如果内存未回落 → 通知 Watchdog 触发 EnterRecovering("内存泄漏")
```

### 接口

```csharp
class MemoryMonitor
{
    int SampleIntervalSeconds { get; set; }      // 默认 30
    int WindowSize { get; set; }                  // 默认 20（10分钟）
    int GrowthThresholdMB { get; set; }           // 默认 500
    int GcRecoveryThresholdMB { get; set; }       // 默认 100

    Func<long?> GetHearthstoneMemoryBytes { get; set; }  // 采集回调
    Func<bool> RequestGarbageCollection { get; set; }     // 通过 Pipe 发 GC 命令
    Action<string> OnMemoryAlert { get; set; }            // 通知 Watchdog
    Action<string> Log { get; set; }

    void Start()
    void Stop()
    void Tick()   // 由内部定时器驱动
}
```

---

## 模块 2：NetworkMonitor

### 架构

Payload 端检测 + Pipe 上报 + 主程序侧决策。

### Payload 端

- 在主循环中定期检查炉石内部网络状态
- 通过反射访问 `Network` 类的连接状态（如 `Network.Get().IsConnectedToAurora()`）
- 新增 Pipe 命令 `NETSTATUS`，主程序可主动查询
- 响应格式：`NETSTATUS:connected` 或 `NETSTATUS:disconnected;reason=<reason>`
- reason 可选值：`server_lost`、`reconnecting`、`unknown`
- 如果反射失败（API 变动），回复 `NETSTATUS:unknown`，主程序忽略

### 主程序端

- 每 10 秒通过 Pipe 发送 `NETSTATUS` 查询
- Pipe 本身断开由 Watchdog 已有逻辑处理，NetworkMonitor 不重复检测

### 断连判定

- 收到 `disconnected` 后开始计时
- 连续 `DisconnectTimeoutSeconds`（默认 60 秒）仍为 disconnected → 触发告警
- 给游戏足够的自动重连时间，不过早干预

### 处理流程

```
Pipe 查询 NETSTATUS
  → connected → 重置计时器，继续监控
  → disconnected → 开始/继续计时
      → 未超时 → 等待游戏自动重连
      → 超时 60s → 通知 Watchdog 触发 EnterRecovering("网络断连")
  → Pipe 无响应/超时 → 忽略（交给 Watchdog 的 Pipe 超时检测）
```

### 接口

```csharp
class NetworkMonitor
{
    int PollIntervalSeconds { get; set; }         // 默认 10
    int DisconnectTimeoutSeconds { get; set; }    // 默认 60

    Func<string> QueryNetStatus { get; set; }     // 通过 Pipe 发 NETSTATUS 命令
    Action<string> OnNetworkAlert { get; set; }   // 通知 Watchdog
    Action<string> Log { get; set; }

    void Start()
    void Stop()
    void Tick()   // 由内部定时器驱动
}
```

---

## 模块 3：Watchdog 集成

### Watchdog 改动

新增一个公开方法，供外部监控器触发恢复：

```csharp
public void TriggerRecovery(string reason)
{
    if (_state != WatchdogState.Recovering && _state != WatchdogState.Disabled)
        EnterRecovering(reason);
}
```

不改动 Tick 核心逻辑。

### Payload 端改动

1. **GC 命令处理：** 收到 `GC` → 执行 `Resources.UnloadUnusedAssets()` + `GC.Collect()` → 回复 `GC:done`
2. **NETSTATUS 命令处理：** 收到 `NETSTATUS` → 反射检查 Network 连接状态 → 回复状态字符串

---

## 接线

```
启动顺序：
1. PipeServer 启动
2. Watchdog 启动
3. MemoryMonitor 启动（OnMemoryAlert → Watchdog.TriggerRecovery）
4. NetworkMonitor 启动（OnNetworkAlert → Watchdog.TriggerRecovery）

停止顺序：反向
```

---

## 文件变更清单

### 新增文件

| 文件 | 位置 | 说明 |
|------|------|------|
| `MemoryMonitor.cs` | BotMain/ | 内存增量监控 |
| `NetworkMonitor.cs` | BotMain/ | 网络状态监控 |

### 改动文件

| 文件 | 改动 |
|------|------|
| `HearthstoneWatchdog.cs` | 新增 `TriggerRecovery(string)` 公开方法 |
| `HearthstonePayload/Entry.cs` | 新增 `GC` 和 `NETSTATUS` 命令处理 |
| BotService 或启动入口 | 创建并接线 MemoryMonitor、NetworkMonitor |
