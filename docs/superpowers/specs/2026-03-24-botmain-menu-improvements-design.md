# BotMain 菜单改进设计

**日期**: 2026-03-24
**状态**: 已批准

## 概述

对 BotMain 的三项改进：修正 Finish 语义、增加 Inject 按钮状态、提升 Stop 响应速度。

---

## 改进一：Finish — 打完当前对局后停止

### 问题

`_finishAfterGame` 在 `BotService.cs:2645`（动作循环内部）检查，导致点击 Finish 后当前回合中途停止，而非打完整场对局。

### 方案

1. **删除** `BotService.cs:2645-2650` 动作循环中的 `_finishAfterGame` 检查。
2. **新增** 在 `HandleGameResult()` 之后、`AutoQueue()` 之前检查 `_finishAfterGame`：
   - 若为 true，设置 `_running = false` 并 `return`，不再排队。
3. `FinishAfterGame()` 方法本身不变。

### 涉及文件

- `Hearthbot/BotMain/BotService.cs`

---

## 改进二：Inject 按钮 — 未连接游戏时显示

### 问题

`MainButtonText` 只有 "Start"/"Stop" 两态，游戏重启后用户不知道需要重新连接。

### 方案

1. `BotService.cs` 暴露 `IsPrepared` 属性（`public bool IsPrepared => _prepared;`）。
2. `MainViewModel.cs` 的 `MainButtonText` 改为三态：
   - `State == Idle && !IsPrepared` → "Inject"
   - `State == Idle && IsPrepared` → "Start"
   - 其他 → "Stop"
3. `OnMainButton()` 增加 Inject 分支：若 `!IsPrepared`，调用 `Prepare()` 并 return。
4. `_prepared` 变化时触发事件，ViewModel 刷新 `MainButtonText`。

### 涉及文件

- `Hearthbot/BotMain/BotService.cs`
- `Hearthbot/BotMain/MainViewModel.cs`

---

## 改进三：Stop 立即中断

### 问题

`Stop()` 只设置 `_running = false`，但后台线程卡在 `Thread.Sleep`、`WaitForGameReady` 轮询等阻塞点，响应缓慢。

### 方案

1. 引入 `SleepOrCancelled(int ms)` 辅助方法，使用 `_cts.Token.WaitHandle.WaitOne(ms)` 替代 `Thread.Sleep`。返回 true 表示已取消。
2. 替换主循环路径上的关键 `Thread.Sleep` 调用（约20-30处），取消时立即退出。
3. `WaitForGameReady` 循环中：
   - 每次迭代前检查 `_cts.IsCancellationRequested`
   - 用 `SleepOrCancelled` 替代 `Thread.Sleep`
4. 主循环关键节点增加 `if (!_running) break;` 检查。
5. `Stop()` 方法不变——`_cts.Cancel()` 触发所有 WaitHandle 立即返回。

### 涉及文件

- `Hearthbot/BotMain/BotService.cs`

---

## 实施优先级

1. Finish（改动最小，风险最低）
2. Inject 按钮（涉及 UI 绑定，需测试刷新）
3. Stop 立即中断（改动面最广，需仔细处理每个 Sleep 点的退出路径）
