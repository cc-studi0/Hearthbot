# 移除传统对战动画等待逻辑 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除传统对战（构筑/竞技场/休闲）中所有动画等待逻辑，获取推荐后零延迟连续执行命令，空推荐直接END_TURN。

**Architecture:** 移除 `WaitForGameReady` 在传统对战路径的所有调用点，删除整个 `ConstructedActionReady` 子系统（Evaluator、Diagnostics、类型定义、Pipe命令处理、BotService helper方法），修改盒子推荐轮询参数为200ms/10s。

**Tech Stack:** C# / .NET 472 (HearthstonePayload) + .NET 8 (BotCore.Tests)

---

### Task 1: 修改盒子推荐轮询参数

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:79-85`

- [ ] **Step 1: 修改构造函数默认参数**

将 `actionWaitTimeoutMs` 从 `2600` 改为 `10000`，将 `actionPollIntervalMs` 从 `180` 改为 `200`：

```csharp
internal HsBoxGameRecommendationProvider(
    IHsBoxRecommendationBridge bridge,
    IHsBoxBattlegroundsBridge bgBridge = null,
    int actionWaitTimeoutMs = 10000,
    int actionPollIntervalMs = 200)
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "refactor: 盒子推荐轮询参数改为200ms间隔/10s超时"
```

---

### Task 2: 删除构筑主循环中的 WaitForGameReady 和 ConstructedActionReady 调用

**Files:**
- Modify: `BotMain/BotService.cs:2672-2696` (TurnStart)
- Modify: `BotMain/BotService.cs:2941-3007` (Pre-ready)
- Modify: `BotMain/BotService.cs:3300-3331` (Post-ready)
- Modify: `BotMain/BotService.cs:3377-3378` (Resimulation后)

- [ ] **Step 1: 删除构筑 TurnStart 就绪等待（行2672-2696）**

将整个 `if (_skipNextTurnStartReadyWait ...) ... else { WaitForGameReady ... }` 块替换为直接继续：

```csharp
                if (!_skipNextTurnStartReadyWait)
                    TryPromotePendingHsBoxActionConfirmation();

                _skipNextTurnStartReadyWait = false;
                gameReadyWaitStreak = 0;
```

删除 `skippedTurnStartReadyWait` 变量及其后续使用（行2698-2702的 `RefreshPlanningBoardAfterReady` 调用中的 `preferFastRecovery` 参数改为 `false`）。

- [ ] **Step 2: 删除构筑 Pre-ready 整个代码块（行2941-3007）**

删除 `preReadyRetries`、`preReadyIntervalMs`、`postReadyRetries`、`postReadyIntervalMs` 常量定义。删除 `readyTimeoutMs` 常量。删除 `isOption` 分支中的整个 pre-ready 检查逻辑（行2953-3007），包括 `ShouldUseConstructedActionReadyWait`、`WaitForConstructedActionReady`、`WaitForGameReady` 调用和超时失败处理。

将 `preReadyStatus` 设为 `"skipped"`：

```csharp
                            preReadyStatus = "skipped";
```

- [ ] **Step 3: 删除构筑 Post-ready 整个代码块（行3300-3331）**

删除行3300-3331的 `postReadySw`、`postReadyOk`、`WaitForConstructedActionReady`、`WaitForGameReady` 调用。将 `postReadyStatus` 设为 `"skipped"`：

```csharp
                                postReadyStatus = "skipped";
                                if (choiceWatchArmed)
                                    ClearChoiceStateWatch("action_settled_no_choice");
```

- [ ] **Step 4: 删除 Resimulation 后的 WaitForGameReady（行3378）**

删除行3378的 `WaitForGameReady(pipe, 30);`。

- [ ] **Step 5: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded（可能有 unused variable 警告，后续步骤清理）

- [ ] **Step 6: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 删除构筑主循环中所有 WaitForGameReady 和 ConstructedActionReady 调用"
```

---

### Task 3: 删除竞技场循环中的 WaitForGameReady 和 ConstructedActionReady 调用

**Files:**
- Modify: `BotMain/BotService.cs:4038-4049` (Arena TurnStart)
- Modify: `BotMain/BotService.cs:4153-4181` (Arena Pre-ready)
- Modify: `BotMain/BotService.cs:4331-4345` (Arena Post-ready)

- [ ] **Step 1: 删除竞技场 TurnStart 就绪等待（行4038-4049）**

将 `_skipNextTurnStartReadyWait` 分支和 `WaitForGameReady` else 分支替换为：

```csharp
                _skipNextTurnStartReadyWait = false;
```

同时将后续 `RefreshPlanningBoardAfterReady` 的 `preferFastRecovery` 参数改为 `false`。

- [ ] **Step 2: 删除竞技场 Pre-ready 整个代码块（行4153-4181）**

删除 `if (!isOption)` 块中的 `ShouldUseConstructedActionReadyWait`、`WaitForConstructedActionReady`、`WaitForGameReady` 调用和超时失败处理。只保留 `isOption` 跳过逻辑不变。

- [ ] **Step 3: 删除竞技场 Post-ready 代码块（行4331-4345）**

删除 `if (ai < actions.Count - 1 && !nextIsOption && !skipArenaPostActionReadyWait)` 块中的 `WaitForConstructedActionReady` 和 `WaitForGameReady` 调用。

- [ ] **Step 4: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 删除竞技场循环中所有 WaitForGameReady 和 ConstructedActionReady 调用"
```

---

### Task 4: 删除选择处理和投降逻辑中的 WaitForGameReady

**Files:**
- Modify: `BotMain/BotService.cs:7528-7529` (Choice处理后)
- Modify: `BotMain/BotService.cs:8787` (ConcedeWhenLethal)

- [ ] **Step 1: 删除选择处理后的等待（行7528-7529）**

删除行7528的 `Thread.Sleep(150);` 和行7529的 `WaitForGameReady(pipe, maxRetries: 10, intervalMs: 100);`。

- [ ] **Step 2: 删除投降逻辑中的等待（行8787）**

删除行8787-8791的 `WaitForGameReady` 调用和超时返回逻辑：

```csharp
                if (!WaitForGameReady(pipe, 15, 200))
                {
                    Log("[ConcedeWhenLethal] reason=blocked:unsupported-state detail=wait-ready-timeout");
                    return false;
                }
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 删除选择处理和投降逻辑中的 WaitForGameReady"
```

---

### Task 5: 添加空推荐直接 END_TURN 逻辑

**Files:**
- Modify: `BotMain/BotService.cs:2861-2868` (构筑空推荐处理)
- Modify: `BotMain/BotService.cs` (竞技场空推荐处理，对应区域)

- [ ] **Step 1: 构筑主循环中空推荐直接 END_TURN**

在行2861 `if (actions != null && actions.Count > 0)` 之前，插入空推荐检查：

```csharp
                if (actions == null || actions.Count == 0)
                {
                    Log("[Action] 空推荐，直接结束回合");
                    try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                    continue;
                }
```

- [ ] **Step 2: 竞技场循环中空推荐直接 END_TURN**

在竞技场的动作列表获取后（约行4130附近），插入相同的空推荐检查：

```csharp
                if (actions == null || actions.Count == 0)
                {
                    Log("[Arena.Action] 空推荐，直接结束回合");
                    try { SendActionCommand(pipe, "END_TURN", 5000); } catch { }
                    continue;
                }
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 空推荐时直接发送 END_TURN 结束回合"
```

---

### Task 6: 删除 BotService 中的 ConstructedActionReady 辅助方法

**Files:**
- Modify: `BotMain/BotService.cs:4655-4663` (TryGetConstructedActionReadyDiagnostic)
- Modify: `BotMain/BotService.cs:4709-4751` (WaitForConstructedActionReady)
- Modify: `BotMain/BotService.cs:4769-4781` (ShouldUseConstructedActionReadyWait)

- [ ] **Step 1: 删除三个方法**

删除以下方法（经过前面的步骤已无调用方）：
1. `TryGetConstructedActionReadyDiagnostic`（行4655-4663）
2. `WaitForConstructedActionReady`（行4709-4751）
3. `ShouldUseConstructedActionReadyWait`（行4769-4781）

- [ ] **Step 2: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 删除 BotService 中 ConstructedActionReady 辅助方法"
```

---

### Task 7: 删除 ActionExecutor 中的 ConstructedActionReady 相关方法

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:8083-8090` (IsConstructedActionReady, DescribeConstructedActionReady)
- Modify: `HearthstonePayload/ActionExecutor.cs:8179-8317` (EvaluateConstructedActionReadyState 及辅助方法)
- Modify: `HearthstonePayload/ActionExecutor.cs:8319-8416` (PopulateConstructedHardBlockFlags, BuildConstructed* 方法)
- Modify: `HearthstonePayload/ActionExecutor.cs:8280-8301` (MapConstructedActionReadyKind)

- [ ] **Step 1: 删除公共入口方法**

删除 `IsConstructedActionReady`（行8083-8085）和 `DescribeConstructedActionReady`（行8088-8090）。

- [ ] **Step 2: 删除内部实现方法**

删除以下方法（全部在 `ActionExecutor.cs` 中）：
- `EvaluateConstructedActionReadyState`（行8179-8277）
- `MapConstructedActionReadyKind`（行8280-8301）
- `CreateConstructedBusyReadyState`（行8304-8317）
- `PopulateConstructedHardBlockFlags`（行8319-8361）
- `BuildConstructedHandCardReadySnapshot`（行8363-8384）
- `BuildConstructedAttackSourceReadySnapshot`（行8386-8416）
- `TryBuildConstructedAttackRuntimeState`（行8418-8470附近）
- `BuildConstructedBoardEntityReadySnapshot`、`BuildConstructedCardReadySnapshot`、`BuildConstructedTargetReadySnapshot`、`BuildConstructedHeroPowerReadySnapshot` 等所有以 `BuildConstructed` 或 `Constructed` 为前缀且仅被上述已删方法调用的辅助方法
- `GetConstructedActionCommandKind`（被 `MapConstructedActionReadyKind` 和 `EvaluateConstructedActionReadyState` 调用）
- `IsConstructedEndTurnButtonReady`（行8273调用）

删除后 grep 验证无残留：`grep -r "Constructed" HearthstonePayload/ActionExecutor.cs` 应只返回零结果或与构筑无关的内容。

- [ ] **Step 3: 验证编译**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "refactor: 删除 ActionExecutor 中 ConstructedActionReady 相关方法"
```

---

### Task 8: 删除 Entry.cs 中的 Pipe 命令处理

**Files:**
- Modify: `HearthstonePayload/Entry.cs:779-788`

- [ ] **Step 1: 删除 WAIT_CONSTRUCTED_ACTION_READY 命令处理**

删除行779-788的两个 `else if` 分支：

```csharp
            else if (cmd.StartsWith("WAIT_CONSTRUCTED_ACTION_READY:", StringComparison.Ordinal))
            {
                var action = cmd.Substring("WAIT_CONSTRUCTED_ACTION_READY:".Length);
                _pipe.Write(ActionExecutor.IsConstructedActionReady(action) ? "READY" : "BUSY");
            }
            else if (cmd.StartsWith("WAIT_CONSTRUCTED_ACTION_READY_DETAIL:", StringComparison.Ordinal))
            {
                var action = cmd.Substring("WAIT_CONSTRUCTED_ACTION_READY_DETAIL:".Length);
                _pipe.Write(ActionExecutor.DescribeConstructedActionReady(action));
            }
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add HearthstonePayload/Entry.cs
git commit -m "refactor: 删除 Entry.cs 中 WAIT_CONSTRUCTED_ACTION_READY Pipe 命令处理"
```

---

### Task 9: 删除 ConstructedActionReady 文件和项目引用

**Files:**
- Delete: `BotMain/ConstructedActionReadyEvaluator.cs`
- Delete: `BotMain/ConstructedActionReadyDiagnostics.cs`
- Delete: `BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs`
- Delete: `BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs`
- Modify: `HearthstonePayload/HearthstonePayload.csproj:13-14`
- Modify: `BotCore.Tests/BotCore.Tests.csproj:37-38`

- [ ] **Step 1: 删除源文件**

```bash
rm BotMain/ConstructedActionReadyEvaluator.cs
rm BotMain/ConstructedActionReadyDiagnostics.cs
```

- [ ] **Step 2: 删除测试文件**

```bash
rm BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs
rm BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs
```

- [ ] **Step 3: 从 HearthstonePayload.csproj 移除 Compile Include 引用**

删除 `HearthstonePayload/HearthstonePayload.csproj` 中的行13-14：

```xml
    <Compile Include="..\BotMain\ConstructedActionReadyDiagnostics.cs" Link="ConstructedActionReadyDiagnostics.cs" />
    <Compile Include="..\BotMain\ConstructedActionReadyEvaluator.cs" Link="ConstructedActionReadyEvaluator.cs" />
```

- [ ] **Step 4: 从 BotCore.Tests.csproj 移除 Compile Include 引用**

删除 `BotCore.Tests/BotCore.Tests.csproj` 中的行37-38：

```xml
    <Compile Include="..\BotMain\ConstructedActionReadyDiagnostics.cs" Link="ConstructedActionReadyDiagnostics.cs" />
    <Compile Include="..\BotMain\ConstructedActionReadyEvaluator.cs" Link="ConstructedActionReadyEvaluator.cs" />
```

- [ ] **Step 5: 验证全项目编译**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj && dotnet build BotCore.Tests/BotCore.Tests.csproj`
Expected: Both builds succeeded

- [ ] **Step 6: 运行测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj`
Expected: All tests pass（ConstructedActionReady 相关测试已随文件删除）

- [ ] **Step 7: Commit**

```bash
git add -A BotMain/ConstructedActionReadyEvaluator.cs BotMain/ConstructedActionReadyDiagnostics.cs BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs HearthstonePayload/HearthstonePayload.csproj BotCore.Tests/BotCore.Tests.csproj
git commit -m "refactor: 删除 ConstructedActionReady 子系统文件和项目引用"
```

---

### Task 10: 清理未使用的变量和常量

**Files:**
- Modify: `BotMain/BotService.cs` (多处)

- [ ] **Step 1: 清理构筑主循环中的残留变量**

删除或调整因前面步骤产生的未使用变量：
- `preReadyRetries`、`preReadyIntervalMs`、`postReadyRetries`、`postReadyIntervalMs`、`readyTimeoutMs` 常量（已在 Task 2 中删除）
- `constructedPreReadyState`、`constructedPreReadyMaxPolls`、`constructedPreReadyIntervalMs` 等变量
- `isFaceAttack` 如果仅被 pre-ready 使用则删除（检查其他用途）
- `skippedTurnStartReadyWait`、`skippedArenaTurnStartReadyWait` 变量
- `skipPostActionReadyWait`、`skipArenaPostActionReadyWait` 变量如果仅被 post-ready 使用

- [ ] **Step 2: 验证全项目编译无警告**

Run: `dotnet build BotMain/BotMain.csproj -warnaserror`
Expected: Build succeeded with no warnings（或仅有已知的非相关警告）

- [ ] **Step 3: 运行测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "refactor: 清理移除动画等待后的未使用变量和常量"
```
