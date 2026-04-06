# 构筑模式局面指纹去重 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用局面指纹替代盒子时间戳作为构筑模式跟随推荐的主要去重依据，消除"盒子未刷新导致脚本卡死"的问题。

**Architecture:** 在 `ActionRecommendationRequest` 中新增 `BoardFingerprint` 和 `LastConsumedBoardFingerprint` 字段。`RecommendActions` 优先用局面指纹判断新鲜度，盒子时间戳降级为辅助判断。移除 `minimumUpdatedAtMs` 机制，所有 `RefreshHsBoxActionMinimumUpdatedAtNow()` 调用改为 `ResetHsBoxActionRecommendationTracking()`。

**Tech Stack:** C# / .NET, xUnit

---

## 文件结构

| 文件 | 改动类型 | 职责 |
|------|---------|------|
| `BotMain/GameRecommendationProvider.cs` | 修改 | `ActionRecommendationRequest` 加字段、`ConstructedRecommendationConsumptionTracker` 加指纹方法 |
| `BotMain/HsBoxRecommendationProvider.cs` | 修改 | `RecommendActions` 新鲜度判断改用局面指纹优先 |
| `BotMain/BotService.cs` | 修改 | 生成指纹、传入请求、消费记录扩展、移除 minimumUpdatedAtMs |
| `BotCore.Tests/HsBoxRecommendationProviderTests.cs` | 修改 | 新增局面指纹去重测试 |

---

### Task 1: ActionRecommendationRequest 加字段、Tracker 加指纹方法

**Files:**
- Modify: `BotMain/GameRecommendationProvider.cs:124-169` (ActionRecommendationRequest)
- Modify: `BotMain/GameRecommendationProvider.cs:327-378` (ConstructedRecommendationConsumptionTracker)

- [ ] **Step 1: 给 ActionRecommendationRequest 加 BoardFingerprint 和 LastConsumedBoardFingerprint 字段**

在 `GameRecommendationProvider.cs` 中修改 `ActionRecommendationRequest`：

```csharp
internal sealed class ActionRecommendationRequest
{
    public ActionRecommendationRequest(
        string seed,
        Board planningBoard,
        Profile selectedProfile,
        IReadOnlyList<ApiCard.Cards> deckCards,
        long minimumUpdatedAtMs = 0,
        string deckName = null,
        string deckSignature = null,
        IReadOnlyList<ApiCard.Cards> remainingDeckCards = null,
        IReadOnlyList<EntityContextSnapshot> friendlyEntities = null,
        MatchContextSnapshot matchContext = null,
        long lastConsumedUpdatedAtMs = 0,
        string lastConsumedPayloadSignature = null,
        string lastConsumedActionCommand = null,
        string boardFingerprint = null,
        string lastConsumedBoardFingerprint = null)
    {
        Seed = SeedCompatibility.GetCompatibleSeed(seed, out _);
        PlanningBoard = planningBoard;
        SelectedProfile = selectedProfile;
        DeckCards = deckCards;
        MinimumUpdatedAtMs = minimumUpdatedAtMs;
        DeckName = deckName ?? string.Empty;
        DeckSignature = deckSignature ?? string.Empty;
        RemainingDeckCards = remainingDeckCards ?? deckCards ?? Array.Empty<ApiCard.Cards>();
        FriendlyEntities = friendlyEntities ?? Array.Empty<EntityContextSnapshot>();
        MatchContext = matchContext ?? new MatchContextSnapshot();
        LastConsumedUpdatedAtMs = lastConsumedUpdatedAtMs;
        LastConsumedPayloadSignature = lastConsumedPayloadSignature ?? string.Empty;
        LastConsumedActionCommand = lastConsumedActionCommand ?? string.Empty;
        BoardFingerprint = boardFingerprint ?? string.Empty;
        LastConsumedBoardFingerprint = lastConsumedBoardFingerprint ?? string.Empty;
    }

    public string Seed { get; }
    public Board PlanningBoard { get; }
    public Profile SelectedProfile { get; }
    public IReadOnlyList<ApiCard.Cards> DeckCards { get; }
    public long MinimumUpdatedAtMs { get; }
    public string DeckName { get; }
    public string DeckSignature { get; }
    public IReadOnlyList<ApiCard.Cards> RemainingDeckCards { get; }
    public IReadOnlyList<EntityContextSnapshot> FriendlyEntities { get; }
    public MatchContextSnapshot MatchContext { get; }
    public long LastConsumedUpdatedAtMs { get; }
    public string LastConsumedPayloadSignature { get; }
    public string LastConsumedActionCommand { get; }
    public string BoardFingerprint { get; }
    public string LastConsumedBoardFingerprint { get; }
}
```

- [ ] **Step 2: 给 ConstructedRecommendationConsumptionTracker 加 IsBoardChanged 方法**

在 `GameRecommendationProvider.cs` 的 `ConstructedRecommendationConsumptionTracker` 类中追加：

```csharp
public static bool IsBoardChanged(string currentFingerprint, string lastConsumedFingerprint)
{
    if (string.IsNullOrWhiteSpace(currentFingerprint) || string.IsNullOrWhiteSpace(lastConsumedFingerprint))
        return false; // 任一为空时不能判定局面变化，降级到时间戳判断
    return !string.Equals(currentFingerprint, lastConsumedFingerprint, StringComparison.Ordinal);
}
```

- [ ] **Step 3: 编译确认无错误**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add BotMain/GameRecommendationProvider.cs
git commit -m "feat: ActionRecommendationRequest 加 BoardFingerprint 字段"
```

---

### Task 2: HsBoxRecommendationProvider 新鲜度判断改用局面指纹优先

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:86-256` (RecommendActions)
- Modify: `BotMain/HsBoxRecommendationProvider.cs:388-439` (TryEvaluateActionPayloadFreshness)

- [ ] **Step 1: 修改 TryEvaluateActionPayloadFreshness 加入局面指纹优先判断**

在 `HsBoxRecommendationProvider.cs` 中，修改 `TryEvaluateActionPayloadFreshness` 方法签名和逻辑：

```csharp
private static bool TryEvaluateActionPayloadFreshness(
    HsBoxRecommendationState state,
    long minimumUpdatedAtMs,
    long lastConsumedUpdatedAtMs,
    string lastConsumedPayloadSignature,
    out string reason,
    string boardFingerprint = null,
    string lastConsumedBoardFingerprint = null)
{
    if (state == null)
    {
        reason = "state_null";
        return false;
    }

    if (state.UpdatedAtMs <= 0)
    {
        reason = "updated_at_missing";
        return false;
    }

    // 局面指纹优先：局面变了则任何推荐都是新的
    if (ConstructedRecommendationConsumptionTracker.IsBoardChanged(boardFingerprint, lastConsumedBoardFingerprint))
    {
        reason = "board_changed";
        return true;
    }

    if (lastConsumedUpdatedAtMs > 0)
    {
        if (state.UpdatedAtMs > lastConsumedUpdatedAtMs)
        {
            reason = "updated_after_last_consumed";
            return true;
        }

        if (state.UpdatedAtMs == lastConsumedUpdatedAtMs
            && !string.IsNullOrWhiteSpace(lastConsumedPayloadSignature)
            && !string.Equals(state.PayloadSignature, lastConsumedPayloadSignature, StringComparison.Ordinal))
        {
            reason = "same_updated_at_new_signature";
            return true;
        }

        reason = "consumed_same_or_older_payload";
        return false;
    }

    // 无已消费记录，直接放行
    reason = "no_freshness_filter";
    return true;
}
```

注意：移除了 `minimumUpdatedAtMs > 0 && state.UpdatedAtMs < minimumUpdatedAtMs` 的 `below_minimum` 分支。`minimumUpdatedAtMs` 参数保留但不再使用，后续 Task 4 会从调用方移除。

- [ ] **Step 2: 更新 IsActionPayloadFreshEnough 传递新参数**

```csharp
private static bool IsActionPayloadFreshEnough(
    HsBoxRecommendationState state,
    long minimumUpdatedAtMs,
    long lastConsumedUpdatedAtMs,
    string lastConsumedPayloadSignature = null,
    string boardFingerprint = null,
    string lastConsumedBoardFingerprint = null)
{
    return TryEvaluateActionPayloadFreshness(
        state,
        minimumUpdatedAtMs,
        lastConsumedUpdatedAtMs,
        lastConsumedPayloadSignature,
        out _,
        boardFingerprint,
        lastConsumedBoardFingerprint);
}
```

- [ ] **Step 3: 在 RecommendActions 轮询循环中传入局面指纹**

在 `RecommendActions` 方法中（约第120行），修改 `IsActionPayloadFreshEnough` 的调用：

```csharp
var fresh = IsActionPayloadFreshEnough(
    currentState,
    minimumUpdatedAtMs,
    lastConsumedUpdatedAtMs,
    lastConsumedPayloadSignature,
    request?.BoardFingerprint,
    request?.LastConsumedBoardFingerprint);
```

- [ ] **Step 4: 在诊断输出中增加指纹信息**

在 `RecommendActions` 的诊断输出部分（约第206行 `TryEvaluateActionPayloadFreshness` 调用）传入指纹参数，并在 `diagParts` 中增加：

```csharp
var freshResult = TryEvaluateActionPayloadFreshness(
    diagState,
    minimumUpdatedAtMs,
    lastConsumedUpdatedAtMs,
    lastConsumedPayloadSignature,
    out var freshnessReason,
    request?.BoardFingerprint,
    request?.LastConsumedBoardFingerprint);
// ... 现有 diagParts ...
diagParts.Add($"boardFp={request?.BoardFingerprint ?? "null"}");
diagParts.Add($"lastBoardFp={request?.LastConsumedBoardFingerprint ?? "null"}");
diagParts.Add($"boardChanged={ConstructedRecommendationConsumptionTracker.IsBoardChanged(request?.BoardFingerprint, request?.LastConsumedBoardFingerprint)}");
```

- [ ] **Step 5: 编译确认无错误**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: RecommendActions 新鲜度判断改用局面指纹优先"
```

---

### Task 3: 新增局面指纹去重测试

**Files:**
- Modify: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: 写测试——局面变化时即使盒子时间戳没变也放行**

```csharp
[Fact]
public void RecommendActions_AcceptsStalePayload_WhenBoardFingerprintChanged()
{
    var state = CreateState(100, raw: "play-card", actionName: "play_card", cardId: "CS2_022", cardName: "暗影步", zonePosition: 1);
    var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 50, actionPollIntervalMs: 5);

    var result = provider.RecommendActions(new ActionRecommendationRequest(
        "seed", null, null, null,
        lastConsumedUpdatedAtMs: 100,
        lastConsumedPayloadSignature: state.PayloadSignature,
        lastConsumedActionCommand: "PLAY|1",
        boardFingerprint: "fp_after_play",
        lastConsumedBoardFingerprint: "fp_before_play"));

    Assert.False(result.ShouldRetryWithoutAction, "局面已变，应放行而非重试");
    Assert.NotEmpty(result.Actions);
}
```

- [ ] **Step 2: 写测试——同局面同时间戳同动作被拦截**

```csharp
[Fact]
public void RecommendActions_RejectsPayload_WhenSameBoardAndSamePayloadAndSameAction()
{
    var state = CreateState(100, raw: "play-card", actionName: "play_card", cardId: "CS2_022", cardName: "暗影步", zonePosition: 1);
    var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 50, actionPollIntervalMs: 5);

    var result = provider.RecommendActions(new ActionRecommendationRequest(
        "seed", null, null, null,
        lastConsumedUpdatedAtMs: 100,
        lastConsumedPayloadSignature: state.PayloadSignature,
        lastConsumedActionCommand: "PLAY|1",
        boardFingerprint: "fp_same",
        lastConsumedBoardFingerprint: "fp_same"));

    Assert.True(result.ShouldRetryWithoutAction, "同局面同推荐应被拦截");
}
```

- [ ] **Step 3: 写测试——指纹为空时降级到时间戳判断**

```csharp
[Fact]
public void RecommendActions_FallsBackToTimestamp_WhenFingerprintEmpty()
{
    var state = CreateState(200, raw: "play-card", actionName: "play_card", cardId: "CS2_022", cardName: "暗影步", zonePosition: 1);
    var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 50, actionPollIntervalMs: 5);

    // 指纹为空，但时间戳比上次消费大 → 应放行
    var result = provider.RecommendActions(new ActionRecommendationRequest(
        "seed", null, null, null,
        lastConsumedUpdatedAtMs: 100,
        lastConsumedPayloadSignature: "old_sig",
        lastConsumedActionCommand: "PLAY|1",
        boardFingerprint: "",
        lastConsumedBoardFingerprint: ""));

    Assert.False(result.ShouldRetryWithoutAction, "时间戳更新时应放行");
    Assert.NotEmpty(result.Actions);
}
```

- [ ] **Step 4: 运行测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "ClassName~HsBoxRecommendationProviderTests" -v n`
Expected: 全部通过，包含新增的 3 个测试

- [ ] **Step 5: Commit**

```bash
git add BotCore.Tests/HsBoxRecommendationProviderTests.cs
git commit -m "test: 局面指纹去重测试"
```

---

### Task 4: BotService 生成局面指纹、扩展消费记录

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 新增 BuildBoardFingerprint 方法**

在 `BotService.cs` 中（`ResetHsBoxActionRecommendationTracking` 方法附近，约第1305行之后），新增：

```csharp
private static string BuildBoardFingerprint(Board board)
{
    if (board == null) return string.Empty;
    var sb = new StringBuilder(256);
    sb.Append(board.TurnCount).Append('|');
    sb.Append(board.Mana).Append('|');
    if (board.Hand != null)
        foreach (var c in board.Hand.Where(c => c != null).OrderBy(c => c.Id))
            sb.Append(c.Id).Append(',');
    sb.Append('|');
    if (board.MinionFriend != null)
        foreach (var m in board.MinionFriend.Where(m => m != null).OrderBy(m => m.Id))
            sb.Append(m.Id).Append(':').Append(m.Health).Append(',');
    sb.Append('|');
    if (board.MinionEnemy != null)
        foreach (var m in board.MinionEnemy.Where(m => m != null).OrderBy(m => m.Id))
            sb.Append(m.Id).Append(':').Append(m.Health).Append(',');
    sb.Append('|');
    sb.Append(board.FriendHero?.Health ?? 0).Append('|');
    sb.Append(board.EnemyHero?.Health ?? 0);

    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
```

- [ ] **Step 2: 新增 _lastConsumedBoardFingerprint 字段**

在现有字段声明区域（约第151-153行）后新增：

```csharp
private string _lastConsumedBoardFingerprint = string.Empty;
```

- [ ] **Step 3: 修改 ResetHsBoxActionRecommendationTracking 清除指纹**

```csharp
private void ResetHsBoxActionRecommendationTracking()
{
    _lastConsumedHsBoxActionUpdatedAtMs = 0;
    _lastConsumedHsBoxActionPayloadSignature = string.Empty;
    _lastConsumedHsBoxActionCommand = string.Empty;
    _lastConsumedBoardFingerprint = string.Empty;
}
```

注意：同时从此方法中移除 `_hsBoxActionMinimumUpdatedAtMs = 0;`（该字段将在 Task 5 中整体移除）。

- [ ] **Step 4: 修改 RememberConsumedHsBoxActionRecommendation 记录指纹**

方法签名增加 `boardFingerprint` 参数：

```csharp
private void RememberConsumedHsBoxActionRecommendation(ActionRecommendationResult recommendation, string executedAction, string boardFingerprint = null)
{
    if (recommendation == null)
        return;

    if (recommendation.SourceUpdatedAtMs <= 0
        && string.IsNullOrWhiteSpace(recommendation.SourcePayloadSignature))
    {
        return;
    }

    _lastConsumedHsBoxActionUpdatedAtMs = recommendation.SourceUpdatedAtMs;
    _lastConsumedHsBoxActionPayloadSignature = recommendation.SourcePayloadSignature ?? string.Empty;
    _lastConsumedHsBoxActionCommand = string.IsNullOrWhiteSpace(executedAction)
        ? ConstructedRecommendationConsumptionTracker.SummarizeFirstAction(recommendation.Actions)
        : executedAction.Trim();
    if (!string.IsNullOrWhiteSpace(boardFingerprint))
        _lastConsumedBoardFingerprint = boardFingerprint;
}
```

- [ ] **Step 5: 在 MainLoop 构造 ActionRecommendationRequest 时传入指纹**

修改约第2564行的构造调用。在 `planningBoard` 可用时生成指纹：

```csharp
var currentBoardFingerprint = BuildBoardFingerprint(planningBoard);
var actionRequest = new ActionRecommendationRequest(
    seed,
    planningBoard,
    _selectedProfile,
    deckCards,
    GetHsBoxActionMinimumUpdatedAtMs(),
    _currentDeckContext?.DeckName,
    _currentDeckContext?.DeckSignature,
    deckCards,
    friendlyEntities,
    BuildMatchContext(planningBoard),
    _lastConsumedHsBoxActionUpdatedAtMs,
    _lastConsumedHsBoxActionPayloadSignature,
    _lastConsumedHsBoxActionCommand,
    currentBoardFingerprint,
    _lastConsumedBoardFingerprint);
```

- [ ] **Step 6: 在 MainLoop RememberConsumed 调用时传入指纹**

在约第2813行：

```csharp
RememberConsumedHsBoxActionRecommendation(recommendation, action, currentBoardFingerprint);
```

注意：`currentBoardFingerprint` 变量需要在 action 循环外可见。把变量声明提到动作循环之前（在 `var actionRequest = ...` 之后即可，因为已在那里定义）。

- [ ] **Step 7: 在 Arena 循环中同样传入指纹**

修改约第3623行 Arena 的 `ActionRecommendationRequest` 构造：

```csharp
var currentBoardFingerprint = BuildBoardFingerprint(planningBoard);
var actionRequest = new ActionRecommendationRequest(
    seed,
    planningBoard,
    _selectedProfile,
    null,
    GetHsBoxActionMinimumUpdatedAtMs(),
    null, null, null, null,
    BuildMatchContext(planningBoard),
    _lastConsumedHsBoxActionUpdatedAtMs,
    _lastConsumedHsBoxActionPayloadSignature,
    _lastConsumedHsBoxActionCommand,
    currentBoardFingerprint,
    _lastConsumedBoardFingerprint);
```

在 Arena 的 `RememberConsumed` 调用（约第3723行）同样传入指纹：

```csharp
RememberConsumedHsBoxActionRecommendation(recommendation, action, currentBoardFingerprint);
```

- [ ] **Step 8: 编译确认无错误**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "feat: BotService 生成局面指纹并传入推荐请求"
```

---

### Task 5: 移除 minimumUpdatedAtMs 机制，RefreshHsBoxActionMinimumUpdatedAtNow → ResetTracking

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotMain/GameRecommendationProvider.cs`
- Modify: `BotMain/HsBoxRecommendationProvider.cs`

- [ ] **Step 1: BotService 中移除 _hsBoxActionMinimumUpdatedAtMs 字段及相关方法**

删除以下内容：
- 字段 `_hsBoxActionMinimumUpdatedAtMs`（第154行）
- 方法 `RefreshHsBoxActionMinimumUpdatedAtNow()`（第1297-1302行）
- 方法 `GetHsBoxActionMinimumUpdatedAtMs()`（第1304行）

- [ ] **Step 2: BotService 中所有 RefreshHsBoxActionMinimumUpdatedAtNow() 调用改为 ResetHsBoxActionRecommendationTracking()**

逐个替换以下位置（共 9 处）：

| 行号 | 上下文 | 改动 |
|------|--------|------|
| 2447 | `TryHandlePendingChoiceBeforePlanning` 后 | `RefreshHsBoxActionMinimumUpdatedAtNow()` → `ResetHsBoxActionRecommendationTracking()` |
| 2794 | `choice_after_play_fail` | 同上 |
| 2892 | `choice_after_action` resimulation | 同上 |
| 2981 | action fail streak >= 3 | 同上（此处已有 `ResetHsBoxActionRecommendationTracking()`，移除紧跟的 `RefreshHsBoxActionMinimumUpdatedAtNow()`） |
| 3585 | Arena `TryHandlePendingChoiceBeforePlanning` 后 | 同上 |
| 3734 | Arena `choice_after_action` | 同上 |
| 3757 | Arena action fail streak | 同上（同2981，移除多余的 Refresh 调用） |

对于第2607行（`RequireFreshSourcePayload` 分支）和第3655行（Arena 的同一逻辑）：这两处连同 `staleFreshSourceRetryCount` 整个分支都要移除（见 Step 3）。

- [ ] **Step 3: 移除 staleFreshSourceRetryCount 及 RequireFreshSourcePayload 处理逻辑**

在 MainLoop（约第2603-2618行）：

将：
```csharp
if (recommendation?.ShouldRetryWithoutAction == true)
{
    if (recommendation.RequireFreshSourcePayload)
    {
        RefreshHsBoxActionMinimumUpdatedAtNow();
        staleFreshSourceRetryCount++;
        if (staleFreshSourceRetryCount >= 3)
        {
            Log("[Action] stale hsbox recommendation after repeated fresh-source retries, clearing consumed state.");
            ResetHsBoxActionRecommendationTracking();
            staleFreshSourceRetryCount = 0;
        }
    }
    Thread.Sleep(120);
    continue;
}
staleFreshSourceRetryCount = 0;
```

改为：
```csharp
if (recommendation?.ShouldRetryWithoutAction == true)
{
    Thread.Sleep(120);
    continue;
}
```

在 Arena 循环中（约第3651-3667行）做同样的简化。

移除 `staleFreshSourceRetryCount` 变量声明（MainLoop 第2027行、Arena 第3449行）以及 `ApplyPlanningBoard` 中的参数和重置（第5743行、第5769行）。

`ApplyPlanningBoard` 方法签名从：
```csharp
private void ApplyPlanningBoard(
    Board planningBoard,
    ref int lastTurnNumber,
    ref DateTime currentTurnStartedUtc,
    ref int resimulationCount,
    ref int actionFailStreak,
    ref int staleFreshSourceRetryCount,
    Dictionary<int, int> playActionFailStreakByEntity)
```
改为：
```csharp
private void ApplyPlanningBoard(
    Board planningBoard,
    ref int lastTurnNumber,
    ref DateTime currentTurnStartedUtc,
    ref int resimulationCount,
    ref int actionFailStreak,
    Dictionary<int, int> playActionFailStreakByEntity)
```

更新所有调用 `ApplyPlanningBoard` 的位置，移除 `ref staleFreshSourceRetryCount` 参数。

- [ ] **Step 4: 在 MainLoop 和 Arena 的 ActionRecommendationRequest 构造中移除 minimumUpdatedAtMs**

将 `GetHsBoxActionMinimumUpdatedAtMs()` 参数改为 `0`：

MainLoop（约第2569行）：
```csharp
var actionRequest = new ActionRecommendationRequest(
    seed,
    planningBoard,
    _selectedProfile,
    deckCards,
    0, // minimumUpdatedAtMs 不再使用
    ...
```

Arena（约第3628行）同上。

- [ ] **Step 5: resimulation 路径加 ResetTracking**

在约第2948行：
```csharp
if (requestResimulation)
{
    resimulationCount++;
    if (resimulationCount <= 5)
    {
        ResetHsBoxActionRecommendationTracking();
        Log($"[AI] resimulation requested ({resimulationCount}/5): {resimulationReason}");
        if (SleepOrCancelled(800)) break;
        WaitForGameReady(pipe, 30);
        continue;
    }
    Log($"[AI] resimulation limit reached ({resimulationCount}), skipping further resimulation this turn.");
}
```

- [ ] **Step 6: HsBoxRecommendationProvider 中清理 minimumUpdatedAtMs 参数**

在 `TryEvaluateActionPayloadFreshness` 和 `IsActionPayloadFreshEnough` 中移除 `minimumUpdatedAtMs` 参数（已不再使用）。

在 `RecommendActions` 中移除 `var minimumUpdatedAtMs = request?.MinimumUpdatedAtMs ?? 0;`（第90行）及相关传参。

- [ ] **Step 7: GameRecommendationProvider 中清理 MinimumUpdatedAtMs 属性**

从 `ActionRecommendationRequest` 中移除：
- 构造函数参数 `long minimumUpdatedAtMs = 0`
- 属性 `public long MinimumUpdatedAtMs { get; }`
- 构造函数赋值 `MinimumUpdatedAtMs = minimumUpdatedAtMs;`

- [ ] **Step 8: ActionRecommendationResult 中移除 RequireFreshSourcePayload**

从 `ActionRecommendationResult` 中移除：
- 构造函数参数 `bool requireFreshSourcePayload = false`
- 属性 `public bool RequireFreshSourcePayload { get; }`
- 构造函数赋值 `RequireFreshSourcePayload = requireFreshSourcePayload;`

在 `HsBoxRecommendationProvider.RecommendActions` 返回值（约第255行）中移除 `requireFreshSourcePayload: releasedDueToRepeatedFirstAction`。
同时移除 `releasedDueToRepeatedFirstAction` 变量及其相关逻辑（第103行声明、第151行赋值、第152行日志、第153行 break）。

**注意**：保留 `samePayloadRepeatedActionCount` 计数 — 但当它达到 `ReleaseThreshold` 时，不再设置 `requireFreshSourcePayload`，而是直接在 2600ms 轮询超时后返回 `shouldRetryWithoutAction: true`（这是现有的超时兜底行为，不需要额外代码）。

简化：将第143-154行的 `IsSameFirstAction` 分支整体删除。保留第156-173行的 `same_payload_reuse`（不同动作）分支不变。

- [ ] **Step 9: 编译确认无错误**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 10: 运行全量测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj -v n`
Expected: 全部通过

- [ ] **Step 11: Commit**

```bash
git add BotMain/BotService.cs BotMain/GameRecommendationProvider.cs BotMain/HsBoxRecommendationProvider.cs
git commit -m "refactor: 移除 minimumUpdatedAtMs 机制，统一用局面指纹去重"
```

---

### Task 6: 同局面卡死兜底计数器

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在 MainLoop 中新增卡死计数逻辑**

在 MainLoop 中（`if (recommendation?.ShouldRetryWithoutAction == true)` 分支后，约第2603行），加入基于局面指纹的卡死检测：

需要在循环外声明两个变量（在 `lastTurnNumber` 等变量声明附近）：

```csharp
int sameBoardStalledCount = 0;
string sameBoardStalledFingerprint = string.Empty;
```

修改 retry 分支：

```csharp
if (recommendation?.ShouldRetryWithoutAction == true)
{
    if (string.Equals(currentBoardFingerprint, sameBoardStalledFingerprint, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(currentBoardFingerprint))
    {
        sameBoardStalledCount++;
    }
    else
    {
        sameBoardStalledCount = 1;
        sameBoardStalledFingerprint = currentBoardFingerprint;
    }

    if (sameBoardStalledCount >= 5)
    {
        Log($"[Action] same board stalled {sameBoardStalledCount} times (fp={currentBoardFingerprint}), resetting consumed state.");
        ResetHsBoxActionRecommendationTracking();
        sameBoardStalledCount = 0;
    }

    Thread.Sleep(120);
    continue;
}
sameBoardStalledCount = 0;
```

- [ ] **Step 2: 在 Arena 循环中做同样处理**

在 Arena 循环的 retry 分支中加入同样的逻辑（位置约第3651行），使用独立的局部变量。

- [ ] **Step 3: 编译确认无错误**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 4: 运行全量测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj -v n`
Expected: 全部通过

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 同局面卡死兜底——5次重试后重置消费记录"
```

---

### Task 7: 最终验证

**Files:** 无新改动

- [ ] **Step 1: 全量编译**

Run: `dotnet build Hearthbot.sln --no-restore -v q`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: 全量测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj -v n`
Expected: 全部通过

- [ ] **Step 3: 确认无遗留的 RefreshHsBoxActionMinimumUpdatedAtNow 引用**

Run: `grep -rn "RefreshHsBoxActionMinimumUpdatedAtNow\|_hsBoxActionMinimumUpdatedAtMs\|MinimumUpdatedAtMs\|RequireFreshSourcePayload\|staleFreshSourceRetryCount" BotMain/`
Expected: 无输出

- [ ] **Step 4: 确认 BuildBoardFingerprint 被正确使用**

Run: `grep -rn "BuildBoardFingerprint\|BoardFingerprint\|_lastConsumedBoardFingerprint" BotMain/`
Expected: 在 BotService.cs 和 GameRecommendationProvider.cs 和 HsBoxRecommendationProvider.cs 中有引用

- [ ] **Step 5: Commit（如有遗漏修复）**

```bash
git add -A
git commit -m "fix: 局面指纹去重最终清理"
```
