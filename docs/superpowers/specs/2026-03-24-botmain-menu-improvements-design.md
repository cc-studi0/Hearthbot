# BotMain 菜单改进设计

**日期**: 2026-03-24
**状态**: 已批准

## 概述

对 BotMain 的三项改进：修正 Finish 语义、增加 Inject 按钮状态、提升 Stop 响应速度。

---

## 改进一：Finish — 打完当前对局后停止

### 问题

`_finishAfterGame` 在 `BotService.cs:2645`（动作循环内部）检查，导致点击 Finish 后当前回合中途停止，而非打完整场对局。而对局结束后 `AutoQueue` 前没有检查该标志。

### 方案

#### 标准模式

1. **删除** `BotService.cs:2645-2650` 动作循环中的 `_finishAfterGame` 检查。
2. **新增** 在主循环中 `HandleGameResult()` 之后（约第7497行 `CheckRunLimits()` 之后）、`AutoQueue(pipe)` 调用（第7524行）之前检查：
   ```csharp
   if (_finishAfterGame)
   {
       Log("Game finished, stopping as requested.");
       _running = false;
       return;
   }
   ```
   此检查放在 `wasInGame` 块的末尾、`AutoQueue` 之前，这样所有对局结束路径只需修改一处。

#### 战旗模式

3. 在战旗 `BG.AutoQueue` 循环中（第1727行 `if (!_running) break;` 之后）加入：
   ```csharp
   if (_finishAfterGame)
   {
       Log("[BG] Game finished, stopping as requested.");
       _running = false;
       break;
   }
   ```

4. `FinishAfterGame()` 方法本身不变。

### 涉及文件

- `BotMain/BotService.cs`

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
4. **通知机制**：`Prepare()` 完成时已调用 `StatusChanged()`，而 `OnStatusChanged` 回调中已有 `Notify(nameof(MainButtonText))`（MainViewModel.cs:60），因此无需新增事件，`_prepared` 变化时按钮文本会自动刷新。
5. **与 `_prepareTimer` 的关系**：手动点击 Inject 调用 `Prepare()`，`Prepare()` 内部有 `_prepareStateSync` 锁保护（第534行），与定时器触发不会冲突。

### 涉及文件

- `BotMain/BotService.cs`
- `BotMain/MainViewModel.cs`

---

## 改进三：Stop 立即中断

### 问题

`Stop()` 只设置 `_running = false`，但后台线程卡在 `Thread.Sleep`、`WaitForGameReady` 轮询等阻塞点，响应缓慢。

### 方案

1. 引入 `SleepOrCancelled(int ms)` 辅助方法：
   ```csharp
   private bool SleepOrCancelled(int ms)
   {
       return _cts.Token.WaitHandle.WaitOne(ms);
   }
   ```
   返回 true 表示已取消，false 表示正常超时。

2. **替换范围**：替换所有在 `_running` 循环中的 `Thread.Sleep`，包括以下方法：
   - `MainLoop` 及其内联逻辑（标准模式和战旗模式）
   - `WaitForGameReady`
   - `AutoQueue`
   - 对局结果等待相关方法

   **保留不替换**的：`Prepare()` 阶段的 Sleep、一次性初始化中的短延迟。

3. `WaitForGameReady` 循环中：
   - 每次迭代前检查 `_cts.IsCancellationRequested`，若已取消返回 false
   - 用 `SleepOrCancelled` 替代 `Thread.Sleep`

4. **退出路径模式**：
   - 主循环层：`if (SleepOrCancelled(ms)) break;`（跳出 while 循环）
   - 方法内部：`if (SleepOrCancelled(ms)) return;`（直接返回）
   - 嵌套循环：`if (SleepOrCancelled(ms)) { _running = false; break; }`

5. `Stop()` 方法不变——`_cts.Cancel()` 触发所有 WaitHandle 立即返回。

### 涉及文件

- `BotMain/BotService.cs`

---

## 实施优先级

1. Finish（改动最小，风险最低）
2. Inject 按钮（涉及 UI 绑定）
3. Stop 立即中断（改动面最广，需仔细处理每个 Sleep 点的退出路径）

## 验证计划

- **Finish**：对局中途点击 Finish → 确认对局打完才停止（不是回合结束就停），验证标准模式和战旗模式
- **Inject**：启动程序未连接游戏 → 按钮显示 Inject；连接后 → 变为 Start；游戏重启后 → 自动变回 Inject
- **Stop**：对局中点击 Stop → 1-2秒内响应停止
