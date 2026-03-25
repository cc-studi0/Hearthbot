# 游戏闪退自动重启设计文档

**日期**: 2026-03-25
**状态**: 已批准

---

## 背景

当前脚本仅在匹配超时时调用 `RestartHearthstone()` 触发重启，并通过 `_restartPending` 标志控制重连逻辑。游戏意外闪退时，pipe 断开但 `_restartPending == false`，代码走进 `else { Log("Payload disconnected.") }` 分支，线程退出，脚本 stop。

目标：脚本运行期间（`_running == true`），游戏意外闪退后自动检测、重启游戏并恢复主循环。

---

## 需求

- **始终启用**：无需配置开关，只要脚本在运行就生效。
- **进程消失才重启**：检测到炉石进程不存在时才触发启动新进程（而非仅凭 pipe 断开）。
- **无限重连**：不限重试次数，每 15 秒重试一次，直到连接成功或用户手动 Stop。
- **覆盖所有模式**：标准模式和战旗模式（BG AutoQueue）均支持。

---

## 设计

### 新增方法：`TryReconnectLoop(string reason)`

统一封装所有重连逻辑，替代标准模式中现有的固定次数重连代码。

**伪代码**：
```
TryReconnectLoop(reason):
  if !_running: return false
  if GetProcessesByName("Hearthstone").Length == 0:
    log "[Restart] {reason}: 炉石进程已消失，尝试重新启动..."
    TryLaunchHearthstone(ResolveHearthstoneLaunchPath())
  else:
    log "[Restart] {reason}: 炉石进程仍在，等待重新连接..."
  StatusChanged("Reconnecting")
  attempt = 0
  while _running:
    attempt++
    log "[Restart] 重连尝试 {attempt}..."
    if EnsurePreparedAndConnected():
      log "[Restart] 重连成功"
      StatusChanged("Running")
      return true
    if _running:
      log "[Restart] 连接未就绪，15 秒后重试..."
      SleepOrCancelled(15000)
  return false
```

**关键行为**：
- 进程不存在 → 调用 `TryLaunchHearthstone()`（复用现有逻辑，含路径解析和错误处理）
- 进程已存在（匹配超时重启已提前拉起）→ 跳过启动，直接重连
- 成功返回 `true`；用户 Stop 后返回 `false`

---

### 标准模式断开处理（`MainLoop` 末尾，约 2746 行）

**变更前**：
```csharp
if (_restartPending && _running)
{
    _restartPending = false;
    // 固定 6 次 × 5 秒重连
    ...
    goto MainLoopReconnect;
}
else
{
    Log("Payload disconnected.");
}
```

**变更后**：
```csharp
var reason = _restartPending ? "匹配超时重启" : "游戏闪退";
_restartPending = false;
if (_running && TryReconnectLoop(reason))
    goto MainLoopReconnect;
```

`_restartPending` 保留，仅用于区分日志原因（匹配超时 vs 闪退），不再控制是否执行重连。

---

### 战旗模式补充处理（`MainLoop` BG AutoQueue while 循环内）

`BattlegroundsLoop()` 返回后，新增 pipe 状态检查：

**变更前**（`BattlegroundsLoop()` 调用后）：
```csharp
BattlegroundsLoop();
if (!_running) break;
// 直接进入等待大厅逻辑
```

**变更后**：
```csharp
BattlegroundsLoop();
if (!_running) break;

if (_pipe == null || !_pipe.IsConnected)
{
    _prepared = false;
    _decksLoaded = false;
    _restartPending = false;
    if (!TryReconnectLoop("[BG] 游戏闪退"))
        break;
    continue; // 重连成功，跳过等待大厅，直接进入下一局
}
```

---

## 影响分析

| 场景 | 变更前 | 变更后 |
|------|--------|--------|
| 匹配超时重启 | `_restartPending` 触发，最多重连 30 秒 | 统一走 `TryReconnectLoop`，无限重连（15s/次）|
| 游戏闪退（标准模式） | 直接 stop | 检测进程消失 → 启动新进程 → 无限重连 |
| 游戏闪退（战旗模式） | 无效循环直到 Stop | 检测 pipe 断开 → `TryReconnectLoop` → 无限重连 |
| 用户手动 Stop | `_running = false`，正常退出 | 同前，`TryReconnectLoop` 检测到 `_running=false` 立即返回 false |

---

## 涉及文件

- `Hearthbot/BotMain/BotService.cs`：所有变更集中在此文件
  - 新增 `TryReconnectLoop` 方法
  - 修改 `MainLoop()` 标准模式断开处理（约 2746–2782 行）
  - 修改 `MainLoop()` BG AutoQueue 循环（约 1728–1730 行）
