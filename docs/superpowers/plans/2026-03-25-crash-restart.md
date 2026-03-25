# 游戏闪退自动重启 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 脚本运行期间游戏意外闪退时，自动检测进程消失、重启游戏并恢复主循环，无需用户手动干预。

**Architecture:** 在 `BotService.cs` 新增 `TryReconnectLoop` 方法统一处理所有重连场景（匹配超时重启 + 游戏闪退），替代现有的固定次数重连代码；战旗模式的等待大厅阶段补充 pipe 断开检测。

**Tech Stack:** C# / .NET 8，`System.Diagnostics.Process`，`BotMain.BotService`

**Spec:** `docs/superpowers/specs/2026-03-25-crash-restart-design.md`

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `BotMain/BotService.cs` | 修改 | 新增 `TryReconnectLoop`；替换标准模式断开处理；修改 BG 模式循环 |

所有变更集中在一个文件，无需新建文件。

---

## Task 1：新增 `TryReconnectLoop` 方法

**Files:**
- Modify: `BotMain/BotService.cs`（在 `RestartHearthstone()` 方法附近，约 8222 行之前）

- [ ] **Step 1: 找到插入位置**

  在 `BotService.cs` 中找到 `RestartHearthstone()` 方法（约 8222 行）：
  ```csharp
  private void RestartHearthstone()
  {
  ```
  新方法插入在它之前。

- [ ] **Step 2: 插入 `TryReconnectLoop` 方法**

  在 `RestartHearthstone()` 方法定义之前，插入以下代码：

  ```csharp
  /// <summary>
  /// 统一重连循环：检测炉石进程是否存活，若已消失则尝试启动新进程，
  /// 然后无限重试连接直到成功或 _running 变为 false。
  /// </summary>
  /// <returns>true 表示重连成功；false 表示 _running 变为 false（用户 Stop）。</returns>
  private bool TryReconnectLoop(string reason)
  {
      if (!_running) return false;

      // 清理旧 pipe，与 RestartHearthstone 行为对齐
      lock (_sync) { try { _pipe?.Dispose(); } catch { } _pipe = null; }

      var hearthstoneAlive = System.Diagnostics.Process.GetProcessesByName("Hearthstone").Length > 0;
      if (!hearthstoneAlive)
      {
          Log($"[Restart] {reason}: 炉石进程已消失，尝试重新启动...");
          var launchPath = ResolveHearthstoneLaunchPath();
          if (TryLaunchHearthstone(launchPath))
              Log($"[Restart] {reason}: 已启动炉石，等待连接...");
          else
              Log($"[Restart] {reason}: 无法自动启动炉石（路径未知），等待手动启动后重连...");
      }
      else
      {
          Log($"[Restart] {reason}: 炉石进程仍在，等待重新连接...");
      }

      StatusChanged("Reconnecting");
      var attempt = 0;
      while (_running)
      {
          attempt++;
          Log($"[Restart] 重连尝试 {attempt}...");
          if (EnsurePreparedAndConnected())
          {
              Log($"[Restart] 重连成功（第 {attempt} 次）。");
              StatusChanged("Running");
              return true;
          }
          if (_running)
          {
              Log("[Restart] 连接未就绪，15 秒后重试...");
              SleepOrCancelled(15000);
          }
      }
      return false;
  }
  ```

- [ ] **Step 3: 验证编译通过**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet build BotMain/BotMain.csproj -c Debug --no-restore 2>&1 | tail -5
  ```
  预期：`Build succeeded.`，无错误。

- [ ] **Step 4: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs
  git commit -m "新增 TryReconnectLoop：统一重连逻辑，支持游戏闪退自动重启"
  ```

---

## Task 2：替换标准模式断开处理

**Files:**
- Modify: `BotMain/BotService.cs`（约 2746–2782 行，`MainLoop` 末尾 pipe 断开检测）

- [ ] **Step 1: 定位旧代码**

  在 `BotService.cs` 中找到以下代码块（约 2746 行，`MainLoop` 方法末尾，`while (_running)` 循环结束后）：

  ```csharp
  if (pipe == null || !pipe.IsConnected)
  {
      _prepared = false;
      _decksLoaded = false;

      if (_restartPending && _running)
      {
          _restartPending = false;
          Log("[Restart] 正在等待炉石重启后重新连接...");
          StatusChanged("Reconnecting");

          // 炉石启动需要较长时间，循环重试连接
          const int maxReconnectAttempts = 6;
          for (var attempt = 1; attempt <= maxReconnectAttempts && _running; attempt++)
          {
              Log($"[Restart] 重连尝试 {attempt}/{maxReconnectAttempts}...");
              if (EnsurePreparedAndConnected())
              {
                  Log("[Restart] 重连成功，恢复主循环。");
                  StatusChanged("Running");
                  goto MainLoopReconnect;
              }

              if (attempt < maxReconnectAttempts && _running)
              {
                  Log($"[Restart] 连接未就绪，等待后重试...");
                  SleepOrCancelled(5000);
              }
          }

          Log("[Restart] 多次重连失败，停止运行。");
      }
      else
      {
          Log("Payload disconnected.");
      }
  }
  ```

- [ ] **Step 2: 替换为新代码**

  将上面整个 `if (pipe == null || !pipe.IsConnected) { ... }` 块替换为：

  ```csharp
  if (pipe == null || !pipe.IsConnected)
  {
      _prepared = false;
      _decksLoaded = false;

      var reason = _restartPending ? "匹配超时重启" : "游戏闪退";
      _restartPending = false;
      if (_running && TryReconnectLoop(reason))
          goto MainLoopReconnect;
  }
  ```

- [ ] **Step 3: 验证编译通过**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet build BotMain/BotMain.csproj -c Debug --no-restore 2>&1 | tail -5
  ```
  预期：`Build succeeded.`

- [ ] **Step 4: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs
  git commit -m "标准模式断开处理：替换固定次数重连为 TryReconnectLoop，支持闪退重启"
  ```

---

## Task 3：战旗模式——对局中闪退处理

**Files:**
- Modify: `BotMain/BotService.cs`（约 1729–1736 行，`MainLoop` BG AutoQueue while 循环内，`BattlegroundsLoop()` 调用之后）

- [ ] **Step 1: 定位旧代码**

  找到以下代码（约 1729 行，`MainLoop` 内 BG AutoQueue 循环体）：

  ```csharp
                  BattlegroundsLoop();
                  if (!_running) break;
                  if (_finishAfterGame)
  ```

- [ ] **Step 2: 在 `if (!_running) break;` 之后插入 pipe 检测**

  将上面代码修改为：

  ```csharp
                  BattlegroundsLoop();
                  if (!_running) break;

                  // 检测对局中游戏闪退
                  if (_pipe == null || !_pipe.IsConnected)
                  {
                      _prepared = false;
                      _decksLoaded = false;
                      _restartPending = false;
                      if (!TryReconnectLoop("[BG] 游戏闪退"))
                          break;
                      continue;
                  }

                  if (_finishAfterGame)
  ```

- [ ] **Step 3: 验证编译通过**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet build BotMain/BotMain.csproj -c Debug --no-restore 2>&1 | tail -5
  ```
  预期：`Build succeeded.`

- [ ] **Step 4: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs
  git commit -m "战旗模式：对局后检测 pipe 断开，支持游戏闪退后自动重连"
  ```

---

## Task 4：战旗模式——等待大厅阶段闪退处理

**Files:**
- Modify: `BotMain/BotService.cs`（约 1742–1778 行，等待大厅 for 循环及 `!lobbyReady` 块）

- [ ] **Step 1: 修改等待大厅 for 循环，加入双重 pipe 检测**

  找到等待大厅循环（约 1742 行）：
  ```csharp
                  for (var waitIdx = 0; waitIdx < 30 && _running; waitIdx++)
                  {
                      if (SleepOrCancelled(1000)) break;
                      if (!TryGetSceneValue(_pipe, 2000, out var scene, "BG.AutoQueue"))
                          continue;
  ```

  替换为：
  ```csharp
                  for (var waitIdx = 0; waitIdx < 30 && _running; waitIdx++)
                  {
                      if (SleepOrCancelled(1000)) break;
                      if (_pipe == null || !_pipe.IsConnected) break; // 提前退出：pipe 断开
                      if (!TryGetSceneValue(_pipe, 2000, out var scene, "BG.AutoQueue"))
                      {
                          if (_pipe == null || !_pipe.IsConnected) break; // TryGetSceneValue 超时后再检查
                          continue;
                      }
  ```

  注意：原来 `continue;` 单独一行，现在改为 `if...break; continue;` 两行，同时原有的后续代码（`if (string.Equals(scene, "GAMEPLAY"...`）保持不变，只需把 `continue;` 改成带花括号的 if 块即可。完整修改后的循环体如下：

  ```csharp
                  for (var waitIdx = 0; waitIdx < 30 && _running; waitIdx++)
                  {
                      if (SleepOrCancelled(1000)) break;
                      if (_pipe == null || !_pipe.IsConnected) break;
                      if (!TryGetSceneValue(_pipe, 2000, out var scene, "BG.AutoQueue"))
                      {
                          if (_pipe == null || !_pipe.IsConnected) break;
                          continue;
                      }

                      if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                      {
                          if (TryGetEndgameState(_pipe, 1500, out var bgEndgameShown, out _, "BG.AutoQueue.Dismiss")
                              && bgEndgameShown)
                          {
                              TrySendStatusCommand(_pipe, "CLICK_DISMISS", 1500, out _, "BG.AutoQueue.Dismiss");
                          }
                          continue;
                      }

                      if (BotProtocol.IsStableLobbyScene(scene))
                      {
                          Log($"[BG.AutoQueue] 已返回大厅: scene={scene}");
                          lobbyReady = true;
                          break;
                      }
                  }
  ```

- [ ] **Step 2: 修改 `!lobbyReady` 块，优先检查 pipe 状态**

  找到 `!lobbyReady` 处理块（约 1767 行）：
  ```csharp
                  if (!lobbyReady)
                  {
                      // 检查是否仍在 GAMEPLAY — 如果是，说明之前的结束检测是误判，重新进入战旗循环
                      if (TryGetSceneValue(_pipe, 2000, out var stuckScene, "BG.AutoQueue.StuckCheck")
                          && string.Equals(stuckScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                      {
                          Log("[BG.AutoQueue] 超时仍在 GAMEPLAY，可能是误判对局结束，重新进入战旗循环");
                          continue;
                      }
                      Log("[BG.AutoQueue] 超时仍未返回大厅，中止");
                      break;
                  }
  ```

  替换为：
  ```csharp
                  if (!lobbyReady)
                  {
                      // 优先检查 pipe 是否因闪退断开（在 stuck check 之前）
                      if (_pipe == null || !_pipe.IsConnected)
                      {
                          _prepared = false;
                          _decksLoaded = false;
                          _restartPending = false;
                          if (!TryReconnectLoop("[BG] 游戏闪退（等待大厅期间）"))
                              break;
                          continue;
                      }
                      // 检查是否仍在 GAMEPLAY — 如果是，说明之前的结束检测是误判，重新进入战旗循环
                      if (TryGetSceneValue(_pipe, 2000, out var stuckScene, "BG.AutoQueue.StuckCheck")
                          && string.Equals(stuckScene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
                      {
                          Log("[BG.AutoQueue] 超时仍在 GAMEPLAY，可能是误判对局结束，重新进入战旗循环");
                          continue;
                      }
                      Log("[BG.AutoQueue] 超时仍未返回大厅，中止");
                      break;
                  }
  ```

- [ ] **Step 3: 验证编译通过**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet build BotMain/BotMain.csproj -c Debug --no-restore 2>&1 | tail -5
  ```
  预期：`Build succeeded.`

- [ ] **Step 4: 运行现有单元测试确认无回归**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --no-build 2>&1 | tail -10
  ```
  预期：所有测试通过，`Passed!`

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs
  git commit -m "战旗模式：等待大厅阶段检测 pipe 断开，支持闪退自动重连"
  ```

---

## 手动验证指南

自动化测试无法覆盖 `TryReconnectLoop`（需要真实 pipe 和进程），通过以下方式手动验证：

### 场景 1：标准/竞技场模式下游戏闪退
1. 启动脚本，进入标准对局
2. 在对局过程中用任务管理器强制终止 `Hearthstone.exe`
3. 预期日志：`[Restart] 游戏闪退: 炉石进程已消失，尝试重新启动...` → `[Restart] 重连尝试 1...` → 游戏启动后 `[Restart] 重连成功`
4. 预期：脚本恢复运行，UI 状态从 `Reconnecting` 变回 `Running`

### 场景 2：战旗模式下游戏闪退（对局中）
1. 启动脚本，进入战旗对局
2. 在对局中强制终止 `Hearthstone.exe`
3. 预期：`BattlegroundsLoop` 退出后检测到 pipe 断开，触发 `TryReconnectLoop`，重连成功后继续 BG AutoQueue

### 场景 3：战旗模式下游戏闪退（等待大厅阶段）
1. 进入战旗对局，等对局结束（出现结算画面）
2. 在结算画面消失、等待返回大厅的 30 秒窗口内强制终止 `Hearthstone.exe`
3. 预期：for 循环检测到 pipe 断开提前退出，`!lobbyReady` 块触发重连

### 场景 4：用户手动 Stop 不触发重启
1. 脚本运行时点击 Stop
2. 预期：`_running = false`，`TryReconnectLoop` 立即返回 `false`，不重启游戏

### 场景 5：匹配超时重启（回归验证）
1. 配置极短超时（如 30s），等待匹配超时
2. 预期：原有行为保持——`RestartHearthstone()` 杀掉进程并启动新进程，`TryReconnectLoop` 检测到进程已存在（新进程），直接等待重连
