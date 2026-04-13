# 构筑模式机制驱动动作就绪改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让构筑模式在保留拟人化操作的前提下，改为按炉石内部机制和最小稳定校验决定何时继续下一步动作，而不是继续依赖固定动画等待。

**Architecture:** 先复用战旗现有的 `ActionReadyDiagnostics + ActionReadyEvaluator` 模式，在 `BotMain` 建立构筑共享诊断协议与纯规则评估器，再让 `HearthstonePayload` 负责采集运行时对象状态并产出构筑动作级 ready 结果，最后把 `BotService` 的构筑主循环和 `ActionExecutor` 的动作内部尾等待统一切到“机制确认即继续，必要时回退到 WAIT_READY”。拟人化鼠标轨迹、悬停、微停顿保留，只替换“为了等动画而停”的部分。

**Tech Stack:** C# / .NET 8 / .NET Framework 4.7.2 payload / xUnit / Unity 反射 / 现有 `PipeServer` 协议 / 现有 `ReadyWaitDiagnostics`、`PlayTargetConfirmation`、`ChoiceSnapshot` 机制

---

## Scope Check

这份 spec 只覆盖一个子系统：`构筑执行层 readiness`。它跨 `BotMain`、`HearthstonePayload`、测试项目三处，但都服务于同一个目标：把“何时可以继续下一步动作”从固定等待改成动作级机制驱动，因此不需要再拆成多份 plan。

## File Structure

**Create**
- `BotMain/ConstructedActionReadyDiagnostics.cs`
  责任：定义构筑动作级 ready 的共享响应模型、格式化与解析逻辑，供 `BotService`、`HearthstonePayload`、`BotCore.Tests` 共用。
- `BotMain/ConstructedActionReadyEvaluator.cs`
  责任：定义构筑动作级 probe 模型、对象快照结构和纯规则 `Evaluate(...)`，不接触 Unity 反射，只做命令级 readiness 判定。
- `BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs`
  责任：锁定构筑动作级 pipe 响应格式与解析稳定性。
- `BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs`
  责任：锁定 `PLAY / ATTACK / HERO_POWER / USE_LOCATION / OPTION / TRADE / END_TURN` 的纯规则 readiness 分支。

**Modify**
- `BotCore.Tests/BotCore.Tests.csproj`
  责任：把共享构筑 diagnostics/evaluator 文件 link 进测试项目。
- `HearthstonePayload/HearthstonePayload.csproj`
  责任：把共享构筑 diagnostics/evaluator 文件 link 进 payload 项目。
- `HearthstonePayload/Entry.cs`
  责任：新增 `WAIT_CONSTRUCTED_ACTION_READY` 与 `WAIT_CONSTRUCTED_ACTION_READY_DETAIL` pipe 命令入口。
- `BotMain/BotService.cs`
  责任：新增构筑动作级 ready 轮询、诊断读取和 fallback 到 `WAIT_READY` 的逻辑，并接入构筑主循环的动作前后等待。
- `HearthstonePayload/ActionExecutor.cs`
  责任：基于运行时对象与现有机制信号构建 `ConstructedActionReadyProbe`，并替换 `PLAY / ATTACK / HERO_POWER / USE_LOCATION / OPTION / TRADE / END_TURN` 中用于等动画的固定尾等待。
- `BotCore.Tests/ReadyWaitDiagnosticsTests.cs`
  责任：只在必要时补充“现有全局 `WAIT_READY` 仍可作为构筑 fallback 使用”的稳定性断言。

**Manual Verification Targets**
- 普通随从无目标出牌
- 指向性法术
- 战吼选目标随从
- Discover
- Choose One
- 泰坦子技能
- 地标无目标 / 有目标
- 指向性英雄技能
- 交易
- 连续攻击
- 结束回合前 lingering animation

## Implementation Notes

- 使用 `@superpowers:test-driven-development` 先做共享层和纯规则层，再做运行时采样层。
- 本计划按当前工作区输出，没有额外创建 worktree；执行时如需要隔离，先切到 `@superpowers:using-git-worktrees`。
- 优先复用现有机制，不重复造轮子：
  - `ReadyWaitDiagnostics`
  - `PlayTargetConfirmation`
  - `TryBuildChoiceSnapshot`
  - `IsPlayTargetConfirmationPending`
  - `GameObjectFinder`
  - `GetHandZoneBlockingReason`
- 所有提交信息用中文，遵守仓库要求。
- 最终收尾前必须使用 `@superpowers:verification-before-completion`。

### Task 1: 建立构筑动作级共享诊断协议

**Files:**
- Create: `BotMain/ConstructedActionReadyDiagnostics.cs`
- Create: `BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `HearthstonePayload/HearthstonePayload.csproj`

- [ ] **Step 1: 写共享协议的失败测试**

```csharp
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class ConstructedActionReadyDiagnosticsTests
    {
        [Fact]
        public void FormatBusyResponse_UsesStablePayload()
        {
            var response = ConstructedActionReadyDiagnostics.FormatBusyResponse(
                "pending_target_confirmation",
                new[] { "pending_target_confirmation", "target_not_stable" },
                commandKind: "PLAY",
                sourceEntityId: 42,
                targetEntityId: 77);

            Assert.Equal(
                "BUSY:reason=pending_target_confirmation;flags=pending_target_confirmation,target_not_stable;command=PLAY;source=42;target=77",
                response);
        }

        [Fact]
        public void TryParseResponse_ParsesReadyPayload()
        {
            Assert.True(
                ConstructedActionReadyDiagnostics.TryParseResponse(
                    "READY:reason=ready;command=ATTACK;source=101;target=202",
                    out var state));

            Assert.True(state.IsReady);
            Assert.Equal("ATTACK", state.CommandKind);
            Assert.Equal(101, state.SourceEntityId);
            Assert.Equal(202, state.TargetEntityId);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认先失败**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyDiagnosticsTests -v minimal`  
Expected: FAIL，提示 `ConstructedActionReadyDiagnostics` 或相关方法不存在。

- [ ] **Step 3: 写最小共享协议实现**

```csharp
namespace BotMain
{
    internal sealed class ConstructedActionReadyState
    {
        public bool IsReady { get; set; }
        public string PrimaryReason { get; set; } = string.Empty;
        public IReadOnlyList<string> Flags { get; set; } = Array.Empty<string>();
        public string CommandKind { get; set; } = string.Empty;
        public int SourceEntityId { get; set; }
        public int TargetEntityId { get; set; }
    }

    internal static class ConstructedActionReadyDiagnostics
    {
        internal static string FormatReadyResponse(string commandKind, int sourceEntityId = 0, int targetEntityId = 0) { /* ... */ }
        internal static string FormatBusyResponse(string primaryReason, IEnumerable<string> flags, string commandKind, int sourceEntityId = 0, int targetEntityId = 0) { /* ... */ }
        internal static string FormatResponse(ConstructedActionReadyState state) { /* ... */ }
        internal static bool TryParseResponse(string response, out ConstructedActionReadyState state) { /* ... */ }
    }
}
```

- [ ] **Step 4: 把共享文件 link 到测试项目和 payload 项目**

在两个 `.csproj` 中加入：

```xml
<Compile Include="..\BotMain\ConstructedActionReadyDiagnostics.cs" Link="ConstructedActionReadyDiagnostics.cs" />
```

- [ ] **Step 5: 运行测试，确认通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyDiagnosticsTests -v minimal`  
Expected: PASS

- [ ] **Step 6: 提交**

```bash
git add BotMain/ConstructedActionReadyDiagnostics.cs BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs BotCore.Tests/BotCore.Tests.csproj HearthstonePayload/HearthstonePayload.csproj
git commit -m "构筑：新增动作级就绪诊断协议"
```

### Task 2: 建立构筑动作级纯规则评估器

**Files:**
- Create: `BotMain/ConstructedActionReadyEvaluator.cs`
- Create: `BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `HearthstonePayload/HearthstonePayload.csproj`

- [ ] **Step 1: 写纯规则失败测试**

```csharp
using BotMain;
using Xunit;

namespace BotCore.Tests
{
    public class ConstructedActionReadyEvaluatorTests
    {
        [Fact]
        public void Evaluate_PlayWaitsWhenSourceNotStable()
        {
            var probe = ConstructedActionReadyProbe.ForPlay(
                source: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = false
                },
                target: default,
                requiresTarget: false);

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.False(result.IsReady);
            Assert.Equal("source_not_stable", result.PrimaryReason);
        }

        [Fact]
        public void Evaluate_AttackReadyWhenSourceAndTargetAreStable()
        {
            var probe = ConstructedActionReadyProbe.ForAttack(
                source: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = true,
                    IsActionReadyKnown = true,
                    IsActionReady = true
                },
                target: new ConstructedObjectReadySnapshot
                {
                    Exists = true,
                    HasScreenPosition = true,
                    PositionStableKnown = true,
                    IsPositionStable = true
                });

            var result = ConstructedActionReadyEvaluator.Evaluate(probe);

            Assert.True(result.IsReady);
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认先失败**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyEvaluatorTests -v minimal`  
Expected: FAIL，提示 `ConstructedActionReadyEvaluator` / `ConstructedActionReadyProbe` / `ConstructedObjectReadySnapshot` 不存在。

- [ ] **Step 3: 写最小评估器实现**

```csharp
namespace BotMain
{
    internal enum ConstructedActionReadyKind
    {
        Unknown = 0,
        Play = 1,
        Attack = 2,
        HeroPower = 3,
        UseLocation = 4,
        Option = 5,
        Trade = 6,
        EndTurn = 7
    }

    internal struct ConstructedObjectReadySnapshot
    {
        public bool Exists { get; set; }
        public bool HasScreenPosition { get; set; }
        public bool PositionStableKnown { get; set; }
        public bool IsPositionStable { get; set; }
        public bool IsActionReadyKnown { get; set; }
        public bool IsActionReady { get; set; }
        public bool IsInteractiveKnown { get; set; }
        public bool IsInteractive { get; set; }
    }

    internal struct ConstructedActionReadyProbe
    {
        public ConstructedActionReadyKind Kind { get; set; }
        public string CommandKind { get; set; }
        public ConstructedObjectReadySnapshot Source { get; set; }
        public ConstructedObjectReadySnapshot Target { get; set; }
        public bool RequiresTarget { get; set; }
        public bool InputDenied { get; set; }
        public bool ResponsePacketBlocked { get; set; }
        public bool BlockingPowerProcessor { get; set; }
        public bool PowerProcessorRunning { get; set; }
        public bool HasActiveServerChange { get; set; }
        public bool ChoiceReady { get; set; }
        public bool PendingTargetConfirmation { get; set; }
        public bool EndTurnButtonReady { get; set; }
    }
}
```

- [ ] **Step 4: 把共享文件 link 到测试项目和 payload 项目**

在两个 `.csproj` 中加入：

```xml
<Compile Include="..\BotMain\ConstructedActionReadyEvaluator.cs" Link="ConstructedActionReadyEvaluator.cs" />
```

- [ ] **Step 5: 至少覆盖这些判定分支**

至少实现并测试：
- `input_denied`
- `response_packet_blocked`
- `blocking_power_processor`
- `power_processor_running`
- `zone_active_server_change`
- `source_missing`
- `source_pos_not_found`
- `source_not_stable`
- `source_not_interactive`
- `source_action_not_ready`
- `target_missing`
- `target_pos_not_found`
- `target_not_stable`
- `choice_not_ready`
- `pending_target_confirmation`
- `end_turn_button_disabled`

- [ ] **Step 6: 运行测试，确认通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyEvaluatorTests -v minimal`  
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add BotMain/ConstructedActionReadyEvaluator.cs BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs BotCore.Tests/BotCore.Tests.csproj HearthstonePayload/HearthstonePayload.csproj
git commit -m "构筑：新增动作级就绪规则评估器"
```

### Task 3: 给 payload 与 BotService 增加构筑动作级 ready 协议面

**Files:**
- Modify: `HearthstonePayload/Entry.cs`
- Modify: `BotMain/BotService.cs`
- Test: `BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs`

- [ ] **Step 1: 扩充诊断协议测试，锁定新命令语义**

在 `BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs` 中新增：

```csharp
[Fact]
public void TryParseResponse_ParsesBusyPayloadWithFlags()
{
    Assert.True(
        ConstructedActionReadyDiagnostics.TryParseResponse(
            "BUSY:reason=choice_not_ready;flags=choice_not_ready;command=OPTION;source=88",
            out var state));

    Assert.False(state.IsReady);
    Assert.Equal("OPTION", state.CommandKind);
    Assert.Equal("choice_not_ready", state.PrimaryReason);
    Assert.Equal(88, state.SourceEntityId);
}
```

- [ ] **Step 2: 运行测试，确认先失败或缺口明确**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyDiagnosticsTests -v minimal`  
Expected: FAIL 或提示当前 payload 语义尚未覆盖 `OPTION` 等 command context。

- [ ] **Step 3: 在 Entry.cs 增加新的 pipe 命令**

加入类似分支：

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

- [ ] **Step 4: 在 BotService.cs 增加诊断读取与轮询壳子**

新增类似方法：

```csharp
private bool TryGetConstructedActionReadyDiagnostic(PipeServer pipe, string action, int commandTimeoutMs, out ConstructedActionReadyState diagnosticState)
{
    diagnosticState = null;
    var response = pipe.SendAndReceive("WAIT_CONSTRUCTED_ACTION_READY_DETAIL:" + action, Math.Max(100, commandTimeoutMs));
    return ConstructedActionReadyDiagnostics.TryParseResponse(response, out diagnosticState);
}

private bool WaitForConstructedActionReady(PipeServer pipe, string action, int maxPolls, int pollIntervalMs, int commandTimeoutMs, out ConstructedActionReadyState diagnosticState)
{
    /* ... */
}
```

- [ ] **Step 5: 增加动作筛选器**

加入类似：

```csharp
private static bool ShouldUseConstructedActionReadyWait(string action)
{
    return action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("OPTION|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: 再跑诊断协议测试，确认通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyDiagnosticsTests -v minimal`  
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add HearthstonePayload/Entry.cs BotMain/BotService.cs BotCore.Tests/ConstructedActionReadyDiagnosticsTests.cs
git commit -m "构筑：开放动作级就绪查询协议"
```

### Task 4: 在 ActionExecutor 中实现构筑运行时 probe 组装

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Test: `BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs`

- [ ] **Step 1: 先补一组 evaluator 失败测试，锁定关键机制分支**

在 `BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs` 中补至少三个用例：

```csharp
[Fact]
public void Evaluate_OptionWaitsWhenChoiceNotReady()
{
    var probe = ConstructedActionReadyProbe.ForOption(choiceReady: false, sourceEntityId: 55);
    var result = ConstructedActionReadyEvaluator.Evaluate(probe);
    Assert.False(result.IsReady);
    Assert.Equal("choice_not_ready", result.PrimaryReason);
}

[Fact]
public void Evaluate_PlayWaitsWhenPendingTargetConfirmationStillActive()
{
    var probe = ConstructedActionReadyProbe.ForPlay(
        source: new ConstructedObjectReadySnapshot
        {
            Exists = true,
            HasScreenPosition = true,
            PositionStableKnown = true,
            IsPositionStable = true,
            IsInteractiveKnown = true,
            IsInteractive = true
        },
        target: default,
        requiresTarget: false);
    probe.PendingTargetConfirmation = true;

    var result = ConstructedActionReadyEvaluator.Evaluate(probe);
    Assert.False(result.IsReady);
    Assert.Equal("pending_target_confirmation", result.PrimaryReason);
}

[Fact]
public void Evaluate_EndTurnWaitsWhenButtonNotReady()
{
    var probe = ConstructedActionReadyProbe.ForEndTurn(endTurnButtonReady: false);
    var result = ConstructedActionReadyEvaluator.Evaluate(probe);
    Assert.False(result.IsReady);
    Assert.Equal("end_turn_button_disabled", result.PrimaryReason);
}
```

- [ ] **Step 2: 运行测试，确认先失败**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyEvaluatorTests -v minimal`  
Expected: FAIL，原因是 evaluator 和 probe 工厂还没覆盖 `OPTION / pending target / END_TURN`。

- [ ] **Step 3: 在 ActionExecutor 中增加公共入口**

新增类似：

```csharp
public static bool IsConstructedActionReady(string rawCommand)
{
    return EvaluateConstructedActionReadyState(rawCommand).IsReady;
}

public static string DescribeConstructedActionReady(string rawCommand)
{
    return ConstructedActionReadyDiagnostics.FormatResponse(EvaluateConstructedActionReadyState(rawCommand));
}
```

- [ ] **Step 4: 组装运行时 probe**

新增或抽出 helper：

```csharp
private static ConstructedActionReadyState EvaluateConstructedActionReadyState(string rawCommand) { /* ... */ }
private static ConstructedObjectReadySnapshot BuildHandCardReadySnapshot(object gameState, int entityId) { /* ... */ }
private static ConstructedObjectReadySnapshot BuildBoardEntityReadySnapshot(object gameState, int entityId) { /* ... */ }
private static ConstructedObjectReadySnapshot BuildTargetReadySnapshot(object gameState, int entityId) { /* ... */ }
private static ConstructedObjectReadySnapshot BuildHeroPowerReadySnapshot(object gameState, int sourceHeroPowerEntityId) { /* ... */ }
private static bool TryReadScreenPositionStability(int entityId, out bool stable) { /* ... */ }
```

必须优先复用现有能力：
- `GetHandZoneBlockingReason`
- `TryBuildChoiceSnapshot`
- `IsPlayTargetConfirmationPending`
- `TryResolveRuntimeCardObject`
- `TryGetFriendlyChoiceCardObject`
- `GameObjectFinder.GetEntityScreenPos / GetHeroPowerScreenPos / GetHeroScreenPos`

- [ ] **Step 5: 明确 probe 组装规则**

按动作类型至少这样组装：
- `PLAY`: 源手牌快照 + 可选目标快照 + `PendingTargetConfirmation`
- `ATTACK`: 源场上快照 + 目标快照 + 攻击源 action-ready
- `HERO_POWER`: 技能源快照 + 可选目标快照
- `USE_LOCATION`: 地标源快照 + 可选目标快照
- `OPTION`: `ChoiceReady`
- `TRADE`: 手牌源快照
- `END_TURN`: end turn button ready + 没有 pending target/choice

- [ ] **Step 6: 再跑 evaluator 测试，确认通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ConstructedActionReadyEvaluatorTests -v minimal`  
Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs BotCore.Tests/ConstructedActionReadyEvaluatorTests.cs
git commit -m "构筑：实现动作级就绪探测组装"
```

### Task 5: 把 BotService 的构筑主循环切到动作级 ready，并保留 WAIT_READY fallback

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotCore.Tests/ReadyWaitDiagnosticsTests.cs`

- [ ] **Step 1: 先补 fallback 相关测试**

在 `BotCore.Tests/ReadyWaitDiagnosticsTests.cs` 中新增一条聚焦 fallback 的断言：

```csharp
[Fact]
public void ShouldBypassActionPostReadyBusyReason_DoesNotTreatPendingTargetConfirmationAsReady()
{
    Assert.False(ReadyWaitDiagnostics.ShouldBypassActionPostReadyBusyReason("pending_target_confirmation"));
}
```

- [ ] **Step 2: 运行测试，确认当前行为**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter ReadyWaitDiagnosticsTests -v minimal`  
Expected: 若已通过则保留；若失败则先补齐 `ReadyWaitDiagnostics` 的原因白名单。

- [ ] **Step 3: 切换构筑动作前等待**

把构筑主循环中的：

```csharp
var preReadyOk = WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs, waitScope: "ActionPreReady", action: action);
```

改成：

```csharp
bool preReadyOk;
if (ShouldUseConstructedActionReadyWait(action))
{
    preReadyOk = WaitForConstructedActionReady(pipe, action, 15, 20, readyTimeoutMs, out var constructedReadyState)
        || WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs, waitScope: "ActionPreReadyFallback", action: action);
}
else
{
    preReadyOk = WaitForGameReady(pipe, preReadyRetries, preReadyIntervalMs, readyTimeoutMs, waitScope: "ActionPreReady", action: action);
}
```

- [ ] **Step 4: 切换构筑动作后等待**

把 post-ready 改成同样优先走动作级 ready，再回退 `WAIT_READY`，但要保持这些特例：
- 下一步是 `OPTION|` 时仍直接跳到 option 链路
- 连续攻击仍保留快轮询逻辑
- choice probe 仍先于 post-ready

- [ ] **Step 5: 保留旧恢复链路**

确保这些逻辑不被删掉：
- `CANCEL`
- `TryProbePendingChoiceAfterAction`
- `soft failure`
- `requestResimulation`

- [ ] **Step 6: 跑回归测试**

Run:

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "ConstructedActionReadyDiagnosticsTests|ConstructedActionReadyEvaluatorTests|ReadyWaitDiagnosticsTests" -v minimal
```

Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/ReadyWaitDiagnosticsTests.cs
git commit -m "构筑：主循环接入动作级就绪等待"
```

### Task 6: 替换动作内部“等动画”尾等待为机制确认

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`

- [ ] **Step 1: 先写最小回归测试计划注释**

在本任务开始前，把要替换的路径记在工作注释里，至少包括：

```text
MousePlayCard
ExecuteOption / choice submit confirmation
MouseAttack
MouseHeroPower
MouseUseLocation
MouseTradeCard
MouseEndTurn
```

Expected: 有一个明确的替换清单，避免边改边扩散。

- [ ] **Step 2: 替换 PLAY 尾等待**

把依赖固定 `yield return` 的尾等待改成：

```csharp
if (sourceLeftHand || enteredTargetMode || observedBusyThenReleased)
    continueImmediately = true;
```

并保留：
- 鼠标按下/松开的必要极短等待
- 目标短稳小窗口

- [ ] **Step 3: 替换 OPTION / Discover / Choose One / 泰坦子技能尾等待**

把提交后的固定等待改成：

```csharp
if (choiceChanged || choiceUiClosed || !pendingSourceStillWaiting)
    confirmed = true;
```

- [ ] **Step 4: 替换 ATTACK / HERO_POWER / USE_LOCATION / TRADE / END_TURN 尾等待**

至少做到：
- `ATTACK`: 目标短稳后立即点；攻击已消耗或棋盘已变就放行
- `HERO_POWER`: 技能已消耗或目标模式退出就放行
- `USE_LOCATION`: 地标已消耗或目标模式退出就放行
- `TRADE`: 源牌离手就放行
- `END_TURN`: 按钮已吃到点击且无 pending choice/target 就放行

- [ ] **Step 5: 加日志**

至少为这些原因补日志：
- `source_not_stable`
- `target_not_stable`
- `choice_not_ready`
- `pending_target_confirmation`
- `end_turn_button_disabled`

- [ ] **Step 6: 本地构建 payload**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal`  
Expected: BUILD SUCCEEDED

- [ ] **Step 7: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "构筑：用机制确认替换动作尾等待"
```

### Task 7: 完整验证与手测

**Files:**
- Optional Modify: `docs/superpowers/specs/2026-04-13-constructed-mechanism-driven-action-readiness-design.md`
- Optional Modify: `docs/superpowers/plans/2026-04-13-constructed-mechanism-driven-action-readiness.md`

- [ ] **Step 1: 跑完整目标测试集**

Run:

```bash
dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "ConstructedActionReadyDiagnosticsTests|ConstructedActionReadyEvaluatorTests|ReadyWaitDiagnosticsTests" -v minimal
```

Expected: PASS

- [ ] **Step 2: 构建关键项目**

Run:

```bash
dotnet build BotMain/BotMain.csproj -v minimal
dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal
```

Expected: BUILD SUCCEEDED

- [ ] **Step 3: 手动验证构筑关键链路**

手测清单：
1. 普通随从无目标出牌，牌一离手就继续。
2. 指向性法术进入目标模式后立即继续选目标。
3. 战吼选目标随从落地后，不再额外停整段动画。
4. Discover / Choose One / 泰坦子技能刚 ready 就点。
5. 地标无目标 / 有目标链路不再整段空等。
6. 指向性英雄技能不抢在目标轻微漂移时空点。
7. 交易后不再为动画额外停住。
8. 连续攻击明显提速但不增加空挥。
9. 结束回合前 lingering animation 存在时，只要按钮已可交互且无 pending 选择，就能结束回合。

- [ ] **Step 4: 检查日志质量**

至少确认日志能区分：
- `global_blocked:*`
- `source_not_stable`
- `target_not_stable`
- `choice_not_ready`
- `pending_target_confirmation`
- `not_ready_timeout`

- [ ] **Step 5: 若实现与 spec 有偏差，回写文档**

只记录真实偏差，例如：
- 某类动作仍需保留极短固定等待
- 某个 readiness 信号在运行时不可稳定读取，已降级

- [ ] **Step 6: 最终提交**

```bash
git add .
git commit -m "构筑：按机制驱动提速动作衔接"
```

- [ ] **Step 7: 推送**

```bash
git push origin HEAD
```
