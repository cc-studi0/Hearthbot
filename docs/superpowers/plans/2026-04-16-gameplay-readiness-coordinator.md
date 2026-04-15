# 统一交互就绪协调器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重建一套统一的交互就绪协调层，让构筑动作链、留牌、发现/抉择、竞技场选牌都在真正可操作时执行，并在解锁后立刻放行。

**Architecture:** 新增 `InteractionReadinessCoordinator` 作为统一策略与轮询入口，区分 `GameplayGate` 和 `SceneGate` 两类判定。`BotService` 只负责组装上下文和发送 Pipe/状态查询，不再自己散落维护 `WAIT_READY + Sleep + bypass reason` 逻辑；四类交互逐步接入同一协调器，并删除旧的 post-ready bypass 决策。

**Tech Stack:** C# / .NET 8 / WPF (`BotMain`) / xUnit (`BotCore.Tests`) / HearthstonePayload Pipe 协议

---

### Task 1: 建立统一协调器契约与纯策略测试

**Files:**
- Create: `BotMain/InteractionReadinessCoordinator.cs`
- Create: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`

- [ ] **Step 1: 写失败测试，锁定 scope、策略和基础放行规则**

```csharp
public class InteractionReadinessCoordinatorTests
{
    [Fact]
    public void GameplayScope_DoesNotTreatBusyAsReady()
    {
        var request = new InteractionReadinessRequest(InteractionReadinessScope.MulliganCommit);
        var observation = InteractionReadinessObservation.Busy("input_denied");

        var result = InteractionReadinessCoordinator.Evaluate(request, observation);

        Assert.False(result.IsReady);
        Assert.Equal("input_denied", result.Reason);
    }

    [Fact]
    public void ArenaDraftPick_RequiresDraftSceneAndMatchingStatus()
    {
        var request = new InteractionReadinessRequest(
            InteractionReadinessScope.ArenaDraftPick,
            expectedArenaStatus: "HERO_PICK");
        var observation = InteractionReadinessObservation.ArenaDraft(
            scene: "DRAFT",
            arenaStatus: "CARD_PICK",
            optionCount: 3,
            overlayBlocked: false);

        var result = InteractionReadinessCoordinator.Evaluate(request, observation);

        Assert.False(result.IsReady);
        Assert.Equal("arena_status_mismatch", result.Reason);
    }
}
```

- [ ] **Step 2: 运行测试，确认当前缺少协调器类型**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: FAIL，提示 `InteractionReadinessCoordinator` / `InteractionReadinessRequest` / `InteractionReadinessObservation` 未定义

- [ ] **Step 3: 实现最小协调器契约与纯策略**

在 `BotMain/InteractionReadinessCoordinator.cs` 中先实现最小集合：

```csharp
internal enum InteractionReadinessScope
{
    ConstructedActionPre,
    ConstructedActionPost,
    MulliganCommit,
    ChoiceCommit,
    ArenaDraftPick
}

internal sealed record InteractionReadinessRequest(
    InteractionReadinessScope Scope,
    string ExpectedArenaStatus = null);

internal sealed record InteractionReadinessResult(
    bool IsReady,
    string Reason,
    string Detail);

internal sealed class InteractionReadinessObservation
{
    public bool IsReady { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Scene { get; init; } = string.Empty;
    public string ArenaStatus { get; init; } = string.Empty;
    public int OptionCount { get; init; }
    public bool OverlayBlocked { get; init; }

    public static InteractionReadinessObservation Ready() => new() { IsReady = true, Reason = "ready" };
    public static InteractionReadinessObservation Busy(string reason) => new() { IsReady = false, Reason = reason ?? "unknown" };
    public static InteractionReadinessObservation ArenaDraft(string scene, string arenaStatus, int optionCount, bool overlayBlocked)
        => new() { Scene = scene ?? string.Empty, ArenaStatus = arenaStatus ?? string.Empty, OptionCount = optionCount, OverlayBlocked = overlayBlocked };
}

internal static class InteractionReadinessCoordinator
{
    internal static InteractionReadinessResult Evaluate(
        InteractionReadinessRequest request,
        InteractionReadinessObservation observation)
    {
        // 先只实现 gameplay 与 arena draft 的最小规则，够测试通过即可
    }
}
```

- [ ] **Step 4: 把新 BotMain 文件链接进测试项目**

在 `BotCore.Tests/BotCore.Tests.csproj` 中追加：

```xml
<Compile Include="..\BotMain\InteractionReadinessCoordinator.cs" Link="InteractionReadinessCoordinator.cs" />
```

- [ ] **Step 5: 重新运行测试，确认纯策略通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS，2/2 通过

- [ ] **Step 6: 提交**

```bash
git add BotMain/InteractionReadinessCoordinator.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs BotCore.Tests/BotCore.Tests.csproj
git commit -m "feat: 新增统一交互就绪协调器基础契约和策略测试"
```

### Task 2: 给协调器补轮询引擎和每个 scope 的默认等待配置

**Files:**
- Modify: `BotMain/InteractionReadinessCoordinator.cs`
- Modify: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`

- [ ] **Step 1: 写失败测试，锁定短轮询和超时语义**

```csharp
[Fact]
public void PollUntilReady_ReturnsReadyImmediately_WhenProbeTurnsReadyOnThirdPoll()
{
    var observations = new Queue<InteractionReadinessObservation>(new[]
    {
        InteractionReadinessObservation.Busy("power_processor_running"),
        InteractionReadinessObservation.Busy("post_animation_grace"),
        InteractionReadinessObservation.Ready()
    });

    var outcome = InteractionReadinessCoordinator.PollUntilReady(
        new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre),
        () => observations.Dequeue(),
        _ => false);

    Assert.True(outcome.IsReady);
    Assert.Equal(3, outcome.Polls);
}

[Fact]
public void GetDefaultSettings_UsesShortBudgetForConstructedPost()
{
    var settings = InteractionReadinessCoordinator.GetDefaultSettings(InteractionReadinessScope.ConstructedActionPost);

    Assert.Equal(60, settings.PollIntervalMs);
    Assert.Equal(1200, settings.TimeoutMs);
}
```

- [ ] **Step 2: 运行测试，确认轮询 API 尚不存在**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: FAIL，提示 `PollUntilReady` / `GetDefaultSettings` 未定义

- [ ] **Step 3: 在协调器中实现默认配置与可注入轮询循环**

```csharp
internal sealed record InteractionReadinessSettings(int PollIntervalMs, int TimeoutMs);

internal static InteractionReadinessSettings GetDefaultSettings(InteractionReadinessScope scope)
{
    return scope switch
    {
        InteractionReadinessScope.ConstructedActionPre => new(60, 1800),
        InteractionReadinessScope.ConstructedActionPost => new(60, 1200),
        InteractionReadinessScope.MulliganCommit => new(120, 5000),
        InteractionReadinessScope.ChoiceCommit => new(80, 3000),
        InteractionReadinessScope.ArenaDraftPick => new(150, 5000),
        _ => new(100, 3000)
    };
}

internal static InteractionReadinessPollOutcome PollUntilReady(
    InteractionReadinessRequest request,
    Func<InteractionReadinessObservation> observe,
    Func<int, bool> sleep)
{
    // 使用 timeout / pollInterval 计算最大轮询数
    // 每轮调用 Evaluate
    // ready 即返回
    // 超时返回 timed_out
}
```

- [ ] **Step 4: 运行测试，确认轮询行为通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS，新增轮询相关测试通过

- [ ] **Step 5: 提交**

```bash
git add BotMain/InteractionReadinessCoordinator.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs
git commit -m "feat: 为交互就绪协调器补充轮询引擎和默认等待配置"
```

### Task 3: 在 BotService 中接入统一的 GameplayGate helper，替代散装 WAIT_READY 决策

**Files:**
- Modify: `BotMain/BotService.cs:4347-4620`
- Modify: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`

- [ ] **Step 1: 写失败测试，锁定 GameplayGate 不再走旧 bypass 逻辑**

```csharp
[Fact]
public void GameplayGate_KeepsWaiting_ForInputDenied()
{
    var request = new InteractionReadinessRequest(InteractionReadinessScope.ConstructedActionPre);
    var observation = InteractionReadinessObservation.Busy("input_denied");

    var result = InteractionReadinessCoordinator.Evaluate(request, observation);

    Assert.False(result.IsReady);
}
```

- [ ] **Step 2: 运行测试，确保旧的 bypass 假设已经被新测试覆盖**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS；如果失败，说明协调器仍把 `input_denied` / `power_processor_running` 当作可放行

- [ ] **Step 3: 在 BotService 新增统一 helper，包装 Pipe 查询**

在 `BotMain/BotService.cs` 的 readiness helper 区域新增：

```csharp
private InteractionReadinessObservation ObserveGameplayReadiness(PipeServer pipe)
{
    if (pipe == null || !pipe.IsConnected)
        return InteractionReadinessObservation.Busy("pipe_disconnected");

    var response = pipe.SendAndReceive("WAIT_READY_DETAIL", 1200);
    if (!ReadyWaitDiagnostics.TryParseResponse(response, out var state) || state == null)
        return InteractionReadinessObservation.Busy("ready_detail_unparsed");

    return state.IsReady
        ? InteractionReadinessObservation.Ready()
        : InteractionReadinessObservation.Busy(state.PrimaryReason);
}

private InteractionReadinessPollOutcome WaitForGameplayInteraction(
    PipeServer pipe,
    InteractionReadinessScope scope)
{
    return InteractionReadinessCoordinator.PollUntilReady(
        new InteractionReadinessRequest(scope),
        () => ObserveGameplayReadiness(pipe),
        SleepOrCancelled);
}
```

- [ ] **Step 4: 把旧 `ShouldBypassReadyWait()` 调用从新路径中剥离**

先不要删除旧方法，只确保新 helper 不再依赖：

- `ShouldBypassReadyWait()`
- `ShouldBypassActionPostReadyBusyReason()`
- `ShouldBypassTurnStartBusyReason()`

- [ ] **Step 5: 编译主项目**

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs
git commit -m "refactor: 在 BotService 中接入统一 GameplayGate 等待入口"
```

### Task 4: 用协调器重新接管构筑动作链的 pre/post wait

**Files:**
- Modify: `BotMain/BotService.cs:2923-3330`
- Test: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`
- Test: `BotCore.Tests/BotServiceActionConfirmationTests.cs`

- [ ] **Step 1: 写失败测试，锁定构筑 pre/post wait 的 scope 和预算**

```csharp
[Theory]
[InlineData(InteractionReadinessScope.ConstructedActionPre, 1800)]
[InlineData(InteractionReadinessScope.ConstructedActionPost, 1200)]
public void ConstructedScopes_UseExpectedTimeouts(InteractionReadinessScope scope, int timeoutMs)
{
    var settings = InteractionReadinessCoordinator.GetDefaultSettings(scope);
    Assert.Equal(timeoutMs, settings.TimeoutMs);
}
```

- [ ] **Step 2: 运行测试，确认配置与构筑 scope 对齐**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS

- [ ] **Step 3: 在构筑动作发送前接入 `ConstructedActionPre`**

把 [BotService.cs:2923-3005] 的构筑动作发送前区域改为：

```csharp
var preReadyOutcome = isOption
    ? InteractionReadinessPollOutcome.Ready("option_chain")
    : WaitForGameplayInteraction(pipe, InteractionReadinessScope.ConstructedActionPre);

preReadyStatus = preReadyOutcome.IsReady ? "ready" : "timeout";
preReadyMs = preReadyOutcome.ElapsedMs;

if (!preReadyOutcome.IsReady)
{
    actionOutcome = "WAIT_READY_TIMEOUT";
    actionFailed = true;
    actionFailedThisAction = true;
    break;
}
```

- [ ] **Step 4: 在构筑动作发送后接入 `ConstructedActionPost`**

把 [BotService.cs:3274-3330] 的动作后区域改为：

```csharp
if (!isFaceAttack && !nextIsOption && ai < actions.Count - 1)
{
    var postReadyOutcome = WaitForGameplayInteraction(pipe, InteractionReadinessScope.ConstructedActionPost);
    postReadyStatus = postReadyOutcome.IsReady ? "ready" : "timeout";
    postReadyMs = postReadyOutcome.ElapsedMs;
}
else
{
    postReadyStatus = "skipped";
}
```

- [ ] **Step 5: 运行相关测试并编译**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests|FullyQualifiedName~BotServiceActionConfirmationTests"`

Expected: PASS

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs BotCore.Tests/BotServiceActionConfirmationTests.cs
git commit -m "feat: 用统一协调器重建构筑动作链前后等待"
```

### Task 5: 修复留牌提交，不再把 BUSY 误当作可继续状态

**Files:**
- Modify: `BotMain/BotService.cs:10127-10217`
- Test: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`
- Test: `BotCore.Tests/MulliganProtocolTests.cs`

- [ ] **Step 1: 写失败测试，锁定 `MulliganCommit` 必须等待 ready**

```csharp
[Fact]
public void MulliganCommit_StaysBusy_WhenGameplayObservationIsBusy()
{
    var request = new InteractionReadinessRequest(InteractionReadinessScope.MulliganCommit);
    var observation = InteractionReadinessObservation.Busy("post_animation_grace");

    var result = InteractionReadinessCoordinator.Evaluate(request, observation);

    Assert.False(result.IsReady);
}
```

- [ ] **Step 2: 运行测试，确认留牌不会把 `BUSY` 判成 ready**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests|FullyQualifiedName~MulliganProtocolTests"`

Expected: PASS

- [ ] **Step 3: 重写 `TryApplyMulligan()` 的 ready 判定**

把 [BotService.cs:10133-10148] 从“收到 `READY` 或 `BUSY` 就继续”改成：

```csharp
var readiness = WaitForGameplayInteraction(pipe, InteractionReadinessScope.MulliganCommit);
if (!readiness.IsReady)
{
    result = $"gameplay_not_ready:{readiness.Reason}";
    return false;
}
```

后面的 `GET_MULLIGAN_STATE` 逻辑保留，但把失败结果细化成：

- `mulligan_state_timeout`
- `mulligan_state_unavailable`
- `mulligan_choices_empty`

- [ ] **Step 4: 运行留牌相关测试和编译**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests|FullyQualifiedName~MulliganProtocolTests"`

Expected: PASS

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs BotCore.Tests/MulliganProtocolTests.cs
git commit -m "fix: 修正留牌提交等待，禁止在 busy 状态下继续执行"
```

### Task 6: 修复发现/抉择提交流程，要求 choice ready 与 gameplay ready 同时满足

**Files:**
- Modify: `BotMain/BotService.cs:6816-7160`
- Test: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`
- Test: `BotCore.Tests/ChoiceExecutionPolicyTests.cs`

- [ ] **Step 1: 写失败测试，锁定 `ChoiceCommit` 不能只看 snapshot ready**

```csharp
[Fact]
public void ChoiceCommit_RequiresGameplayGateEvenWhenChoiceSnapshotIsReady()
{
    var request = new InteractionReadinessRequest(InteractionReadinessScope.ChoiceCommit);
    var observation = InteractionReadinessObservation.Busy("input_denied");

    var result = InteractionReadinessCoordinator.Evaluate(request, observation);

    Assert.False(result.IsReady);
}
```

- [ ] **Step 2: 运行测试，确认 choice 不会因为 UI 先出来就直接提交**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests|FullyQualifiedName~ChoiceExecutionPolicyTests"`

Expected: PASS

- [ ] **Step 3: 在 `TryHandleChoice()` 中加入 gameplay gate**

在 [BotService.cs:7052-7125] 的提交前插入：

```csharp
var readiness = WaitForGameplayInteraction(pipe, InteractionReadinessScope.ChoiceCommit);
if (!readiness.IsReady)
{
    Log($"[Choice] gameplay_wait snapshotId={currentState.SnapshotId} reason={readiness.Reason}");
    return false;
}
```

保持现有：

- `ChoiceStateSnapshot.IsReady`
- `APPLY_CHOICE`
- snapshot 变化确认

这三段逻辑不动。

- [ ] **Step 4: 运行选择相关测试与编译**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests|FullyQualifiedName~ChoiceExecutionPolicyTests"`

Expected: PASS

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs BotCore.Tests/ChoiceExecutionPolicyTests.cs
git commit -m "fix: 为发现与抉择提交流程补充统一 gameplay ready 门禁"
```

### Task 7: 为竞技场英雄选/卡牌选引入 SceneGate

**Files:**
- Modify: `BotMain/BotService.cs:3626-3687`
- Modify: `BotMain/BotService.cs:3522-3565`
- Test: `BotCore.Tests/InteractionReadinessCoordinatorTests.cs`

- [ ] **Step 1: 写失败测试，锁定 SceneGate 需要 DRAFT + 正确状态 + 非空选项**

```csharp
[Theory]
[InlineData("HUB", "HERO_PICK", 3, false, false)]
[InlineData("DRAFT", "REWARDS", 3, false, false)]
[InlineData("DRAFT", "HERO_PICK", 0, false, false)]
[InlineData("DRAFT", "HERO_PICK", 3, true, false)]
[InlineData("DRAFT", "HERO_PICK", 3, false, true)]
public void ArenaDraftPick_ReadinessMatrix(
    string scene,
    string status,
    int optionCount,
    bool overlayBlocked,
    bool expectedReady)
{
    var request = new InteractionReadinessRequest(InteractionReadinessScope.ArenaDraftPick, expectedArenaStatus: "HERO_PICK");
    var observation = InteractionReadinessObservation.ArenaDraft(scene, status, optionCount, overlayBlocked);

    var result = InteractionReadinessCoordinator.Evaluate(request, observation);

    Assert.Equal(expectedReady, result.IsReady);
}
```

- [ ] **Step 2: 运行测试，确认 SceneGate 规则覆盖到了竞技场矩阵**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS

- [ ] **Step 3: 在 BotService 中新增竞技场观察 helper**

在 `BotMain/BotService.cs` 增加：

```csharp
private InteractionReadinessObservation ObserveArenaDraftPick(
    PipeServer pipe,
    string expectedArenaStatus,
    bool heroPick)
{
    if (!TryGetSceneValue(pipe, 1500, out var scene, "Arena.Readiness"))
        return InteractionReadinessObservation.ArenaDraft("UNKNOWN", "scene_timeout", 0, overlayBlocked: false);

    var overlayBlocked = TryGetBlockingDialog(pipe, 300, out _, out _, out _, "Arena.ReadinessOverlay");
    TrySendAndReceiveExpected(pipe, "ARENA_GET_STATUS", 1500, _ => true, out var status, "Arena.ReadinessStatus");

    var choiceCommand = heroPick ? "ARENA_GET_HERO_CHOICES" : "ARENA_GET_DRAFT_CHOICES";
    TrySendAndReceiveExpected(pipe, choiceCommand, 1500, _ => true, out var choices, "Arena.ReadinessChoices");
    var optionCount = ParseArenaChoiceCount(choices);

    return InteractionReadinessObservation.ArenaDraft(scene, status, optionCount, overlayBlocked);
}
```

- [ ] **Step 4: 在 `ArenaPickHero()` / `ArenaPickCard()` 前统一等待**

在 [BotService.cs:3675-3687] 前半段改成：

```csharp
var readiness = InteractionReadinessCoordinator.PollUntilReady(
    new InteractionReadinessRequest(
        InteractionReadinessScope.ArenaDraftPick,
        expectedArenaStatus: heroPick ? "HERO_PICK" : "CARD_PICK"),
    () => ObserveArenaDraftPick(pipe, heroPick ? "HERO_PICK" : "CARD_PICK", heroPick),
    SleepOrCancelled);

if (!readiness.IsReady)
{
    Log($"[Arena] pick_wait_timeout scope={(heroPick ? "Hero" : "Card")} reason={readiness.Reason}");
    return;
}
```

- [ ] **Step 5: 运行竞技场相关测试和编译**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~InteractionReadinessCoordinatorTests"`

Expected: PASS

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/InteractionReadinessCoordinatorTests.cs
git commit -m "feat: 为竞技场英雄选和卡牌选接入 SceneGate 门禁"
```

### Task 8: 清理旧 bypass 逻辑并做全量验证

**Files:**
- Modify: `BotMain/ReadyWaitDiagnostics.cs:20-45,188-198`
- Modify: `BotMain/BotService.cs:4591-4616`
- Modify: `BotCore.Tests/ReadyWaitDiagnosticsTests.cs`
- Modify: `docs/superpowers/specs/2026-04-16-gameplay-readiness-coordinator-design.md`（仅当实现与设计有偏差时更新）

- [ ] **Step 1: 写失败测试，标记旧 bypass API 已不再是新路径依赖**

先把 `ReadyWaitDiagnosticsTests` 中这两组测试改成仅保留格式化与解析断言，移除：

- `ShouldBypassActionPostReadyBusyReason_MatchesExpectedReasons`
- `ShouldBypassTurnStartBusyReason_MatchesExpectedReasons`

这样在删除旧 API 前，测试会因找不到成员或断言不再成立而失败。

- [ ] **Step 2: 运行测试，确认旧 bypass 断言确实失效**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~ReadyWaitDiagnosticsTests"`

Expected: FAIL，提示被删除或断言不再适配

- [ ] **Step 3: 删除旧 bypass helper 与 BotService 中的死代码**

删除：

- `ReadyWaitDiagnostics.ShouldBypassActionPostReadyBusyReason`
- `ReadyWaitDiagnostics.ShouldBypassTurnStartBusyReason`
- `BotService.ShouldBypassReadyWait`
- `BotService.IsTurnStartReadyWaitScope`
- `BotService.IsPostActionReadyWaitScope`

保留：

- `ReadyWaitDiagnostics.FormatResponse`
- `ReadyWaitDiagnostics.TryParseResponse`
- draw blocking 相关解析能力

- [ ] **Step 4: 运行完整构建与测试**

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj`

Expected: All tests pass

- [ ] **Step 5: 做人工回归清单**

手工验证至少覆盖：

1. 构筑对局内连续出牌/攻击时，动作不会在动画锁输入阶段提前发送
2. 留牌不会在发牌动画未结束时提交
3. Discover / Choose One 出现后要等到 gameplay ready 才提交
4. 竞技场英雄选和卡牌选只会在 `DRAFT + 正确状态 + 非空选项` 时提交

- [ ] **Step 6: 提交**

```bash
git add BotMain/ReadyWaitDiagnostics.cs BotMain/BotService.cs BotCore.Tests/ReadyWaitDiagnosticsTests.cs docs/superpowers/specs/2026-04-16-gameplay-readiness-coordinator-design.md
git commit -m "refactor: 清理旧的就绪绕过逻辑并完成统一门禁验证"
```
