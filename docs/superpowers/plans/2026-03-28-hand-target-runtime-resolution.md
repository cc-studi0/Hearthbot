# Hand Target Runtime Resolution Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PLAY` post-cast target resolution trust Hearthstone runtime legal targets so cards that ask the player to choose from hand click a real hand card instead of a board minion.

**Architecture:** Add a small pure resolver inside `HearthstonePayload` that accepts runtime candidates plus an upstream target hint and decides whether the current interaction is `BoardTarget`, `HandTarget`, or `Unknown`. Keep all reflection-heavy candidate collection inside `ActionExecutor`, wire the resolver into `MousePlayCardByMouseFlow` after the source card leaves hand, and fail closed if live runtime state says “hand target” but no unique hand match can be found.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2 payload assembly, .NET 8 xUnit tests, reflection over Hearthstone `Assembly-CSharp`, `dotnet test`, `dotnet build`

---

### Task 1: Create the pure runtime target resolver test seam

**Files:**
- Create: `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Test: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: Write the failing test that treats `GENERAL + HAND` candidates as a hand-target interaction**

```csharp
[Fact]
public void Resolve_WhenGeneralChoiceContainsHandCandidate_ReturnsHandTargetMode()
{
    var resolution = PlayRuntimeTargetResolver.Resolve(
        new PlayRuntimeTargetHint { OriginalTargetEntityId = 44, CardId = "CATA_499", ZonePosition = 2 },
        new[]
        {
            new PlayRuntimeTargetCandidate { EntityId = 91, Zone = "PLAY", ZonePosition = 2, CardId = "CATA_499" },
            new PlayRuntimeTargetCandidate { EntityId = 132, Zone = "HAND", ZonePosition = 2, CardId = "CATA_499" }
        },
        explicitHandTarget: false,
        rawChoiceType: "GENERAL");

    Assert.Equal(PlayRuntimeTargetMode.HandTarget, resolution.Mode);
    Assert.Equal(132, resolution.ResolvedEntityId);
    Assert.Equal("card_slot", resolution.MatchReason);
}
```

- [ ] **Step 2: Write the failing test that forbids board fallback once hand mode is detected**

```csharp
[Fact]
public void Resolve_WhenHandModeButNoUniqueHandMatch_DoesNotFallBackToBoardEntity()
{
    var resolution = PlayRuntimeTargetResolver.Resolve(
        new PlayRuntimeTargetHint { OriginalTargetEntityId = 91, CardId = "CATA_499", ZonePosition = 2 },
        new[]
        {
            new PlayRuntimeTargetCandidate { EntityId = 201, Zone = "HAND", ZonePosition = 1, CardId = "OTHER_001" }
        },
        explicitHandTarget: true,
        rawChoiceType: "GENERAL");

    Assert.Equal(PlayRuntimeTargetMode.HandTarget, resolution.Mode);
    Assert.False(resolution.HasResolvedEntity);
    Assert.Equal(0, resolution.ResolvedEntityId);
}
```

- [ ] **Step 3: Run the focused test command and confirm it fails because the resolver file is still missing**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: FAIL with compile errors because `PlayRuntimeTargetResolver` types do not exist yet.

- [ ] **Step 4: Link the new payload source file into the test project and add a minimal skeleton**

```xml
<Compile Include="..\HearthstonePayload\PlayRuntimeTargetResolver.cs" Link="HearthstonePayload\PlayRuntimeTargetResolver.cs" />
```

```csharp
namespace HearthstonePayload
{
    internal enum PlayRuntimeTargetMode { Unknown = 0, BoardTarget = 1, HandTarget = 2 }
}
```

- [ ] **Step 5: Re-run the focused tests and keep them red for behavior, not missing symbols**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: FAIL only on the new assertions.

- [ ] **Step 6: Commit the test seam**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/PlayRuntimeTargetResolverTests.cs HearthstonePayload/PlayRuntimeTargetResolver.cs
git commit -m "测试：补充手牌目标运行时解析器骨架"
```

### Task 2: Implement the pure resolver and matching rules

**Files:**
- Modify: `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- Test: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: Add the minimal DTOs required by the resolver**

```csharp
internal sealed class PlayRuntimeTargetHint
{
    public int OriginalTargetEntityId;
    public string CardId = string.Empty;
    public int ZonePosition;
}

internal sealed class PlayRuntimeTargetCandidate
{
    public int EntityId;
    public string Zone = string.Empty;
    public int ZonePosition;
    public string CardId = string.Empty;
}

internal sealed class PlayRuntimeTargetResolution
{
    public PlayRuntimeTargetMode Mode;
    public int ResolvedEntityId;
    public string MatchReason = string.Empty;
    public bool HasResolvedEntity { get { return ResolvedEntityId > 0; } }
}
```

- [ ] **Step 2: Implement `Resolve(...)` so `HAND` candidates beat board candidates even when `rawChoiceType` is `GENERAL`**

```csharp
var hasHandCandidate = candidates.Any(candidate => IsHandZone(candidate.Zone));
if (explicitHandTarget || hasHandCandidate)
    resolution.Mode = PlayRuntimeTargetMode.HandTarget;
else if (candidates.Any())
    resolution.Mode = PlayRuntimeTargetMode.BoardTarget;
```

- [ ] **Step 3: Implement the fixed hand matching order**

```csharp
// order:
// 1. direct entity
// 2. cardId + zonePosition
// 3. cardId
// 4. zonePosition
```

```csharp
if (hint.OriginalTargetEntityId > 0 && handCandidates.Any(candidate => candidate.EntityId == hint.OriginalTargetEntityId))
    return Match(handCandidates.Single(candidate => candidate.EntityId == hint.OriginalTargetEntityId), "entity_direct");
```

- [ ] **Step 4: Keep the resolver compatible with `HearthstonePayload.csproj` language constraints**

```csharp
// Do not use records, switch expressions, collection expressions,
// or other syntax beyond C# 7.3 in this file.
```

- [ ] **Step 5: Run the focused resolver tests and confirm they pass**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: PASS

- [ ] **Step 6: Commit the pure resolver implementation**

```bash
git add HearthstonePayload/PlayRuntimeTargetResolver.cs BotCore.Tests/PlayRuntimeTargetResolverTests.cs
git commit -m "修复：新增手牌目标运行时解析器"
```

### Task 3: Build runtime candidate collection in `ActionExecutor`

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Modify: `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- Test: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: Extract helper methods in `ActionExecutor.cs` that can build runtime candidates from live game state**

```csharp
private static List<PlayRuntimeTargetCandidate> CollectPlayRuntimeTargetCandidates(object gameState)
{
    var candidates = new List<PlayRuntimeTargetCandidate>();
    AppendSelectedOptionTargets(gameState, candidates);
    AppendChoiceSnapshotTargets(gameState, candidates);
    return candidates;
}
```

- [ ] **Step 2: Reuse the existing choice snapshot path instead of inventing a second choice parser**

```csharp
if (TryBuildChoiceSnapshot(gameState, out var snapshot) && snapshot != null)
{
    foreach (var entityId in snapshot.ChoiceEntityIds)
        TryAppendRuntimeTargetCandidate(gameState, entityId, candidates);
}
```

- [ ] **Step 3: Make sure candidate extraction captures `Zone`, `ZonePosition`, and `CardId` from live entities**

```csharp
candidate.Zone = ResolveEntityZoneName(gameState, entityId);
candidate.ZonePosition = ResolveEntityZonePosition(gameState, entityId);
candidate.CardId = ResolveEntityCardId(gameState, entityId) ?? string.Empty;
```

- [ ] **Step 4: Add a helper that converts the upstream `targetEntityId` into a `PlayRuntimeTargetHint`**

```csharp
private static PlayRuntimeTargetHint BuildRuntimeTargetHint(object gameState, int targetEntityId)
{
    return new PlayRuntimeTargetHint
    {
        OriginalTargetEntityId = targetEntityId,
        CardId = ResolveEntityCardId(gameState, targetEntityId) ?? string.Empty,
        ZonePosition = ResolveEntityZonePosition(gameState, targetEntityId)
    };
}
```

- [ ] **Step 5: Run the focused resolver tests plus a payload build smoke test**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: PASS

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal`

Expected: BUILD SUCCEEDED

- [ ] **Step 6: Commit the runtime candidate collection helpers**

```bash
git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/PlayRuntimeTargetResolver.cs
git commit -m "修复：补齐运行时目标候选采集"
```

### Task 4: Replace the one-shot hand correction with runtime re-resolution in `PLAY`

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Test: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: Replace the current single `TryCorrectHandTargetEntityId(...)` block in `MousePlayCardByMouseFlow()` with a polling helper**

```csharp
var runtimeResolution = WaitForRuntimePlayTargetResolution(entityId, targetEntityId, 900);
if (runtimeResolution != null && runtimeResolution.Mode == PlayRuntimeTargetMode.HandTarget)
{
    if (!runtimeResolution.HasResolvedEntity)
    {
        _coroutine.SetResult("FAIL:PLAY:hand_target_unresolved:" + entityId + ":" + targetEntityId);
        yield break;
    }

    targetEntityId = runtimeResolution.ResolvedEntityId;
}
```

- [ ] **Step 2: Implement the polling helper so it waits briefly for the hand-target interaction to become observable**

```csharp
private static PlayRuntimeTargetResolution WaitForRuntimePlayTargetResolution(int sourceEntityId, int hintedTargetEntityId, int timeoutMs)
{
    var deadline = Environment.TickCount + Math.Max(120, timeoutMs);
    while (Environment.TickCount - deadline < 0)
    {
        var gs = GetGameState();
        var resolution = TryResolveRuntimePlayTarget(gs, hintedTargetEntityId);
        if (resolution != null && resolution.Mode != PlayRuntimeTargetMode.Unknown)
            return resolution;

        Thread.Sleep(40);
    }

    return null;
}
```

- [ ] **Step 3: Keep the board-target path unchanged when runtime state does not indicate hand selection**

```csharp
if (runtimeResolution == null || runtimeResolution.Mode != PlayRuntimeTargetMode.HandTarget)
{
    // continue existing board target click flow
}
```

- [ ] **Step 4: Delete or reduce the old `TryCorrectHandTargetEntityId(...)` logic so there is only one source of truth**

```csharp
// Either remove the method or keep it as a thin wrapper over the new resolver.
// Do not leave two separate hand-target correction paths active.
```

- [ ] **Step 5: Run the focused tests and a payload build smoke test**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: PASS

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal`

Expected: BUILD SUCCEEDED

- [ ] **Step 6: Commit the `PLAY` flow integration**

```bash
git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/PlayRuntimeTargetResolver.cs
git commit -m "修复：出牌后手牌目标改为运行时重解析"
```

### Task 5: Add diagnostics that explain how the runtime target was chosen

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Modify: `HearthstonePayload/PlayRuntimeTargetResolver.cs`

- [ ] **Step 1: Add a formatter that summarizes runtime candidates in action traces**

```csharp
private static string FormatRuntimeTargetCandidates(IEnumerable<PlayRuntimeTargetCandidate> candidates)
{
    return string.Join(";", candidates.Select(candidate =>
        candidate.EntityId + ":" + candidate.Zone + ":" + candidate.ZonePosition + ":" + (candidate.CardId ?? string.Empty)));
}
```

- [ ] **Step 2: Emit a trace when hand-target mode is detected and a replacement entity is chosen**

```csharp
AppendActionTrace(
    "PLAY(runtime-target) mode=" + runtimeResolution.Mode
    + " hintedTarget=" + targetEntityId
    + " resolvedTarget=" + runtimeResolution.ResolvedEntityId
    + " reason=" + runtimeResolution.MatchReason
    + " candidates=" + runtimeResolution.CandidateSummary);
```

- [ ] **Step 3: Emit distinct failure traces for timeout, no match, and ambiguous match**

```csharp
AppendActionTrace("PLAY(runtime-target) hand_target_detected_but_no_match ...");
AppendActionTrace("PLAY(runtime-target) hand_target_detected_but_multiple_matches ...");
AppendActionTrace("PLAY(runtime-target) target_context_not_ready_timeout ...");
```

- [ ] **Step 4: Run the payload build after the trace additions**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal`

Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit the diagnostics**

```bash
git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/PlayRuntimeTargetResolver.cs
git commit -m "调试：补充手牌目标运行时解析日志"
```

### Task 6: Verify tests, build, and live in-game behavior

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Modify: `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: Run the focused resolver tests**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v minimal`

Expected: PASS

- [ ] **Step 2: Run the existing target-confirmation tests to ensure no regression in adjacent play flow logic**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayTargetConfirmationTests" -v minimal`

Expected: PASS

- [ ] **Step 3: Run the full test project**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj -v minimal`

Expected: PASS

- [ ] **Step 4: Run a payload build from the real project**

Run: `dotnet build HearthstonePayload/HearthstonePayload.csproj -v minimal`

Expected: BUILD SUCCEEDED

- [ ] **Step 5: Perform a live manual verification in Hearthstone**

Manual check:
- enter a match with a card that says “choose a card from your hand” after play
- confirm the trace shows `mode=HandTarget`
- confirm the click lands on the correct hand card
- confirm ordinary board-target battlecries still click board entities

- [ ] **Step 6: Commit the verified final state**

```bash
git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/PlayRuntimeTargetResolver.cs
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/PlayRuntimeTargetResolverTests.cs
git commit -m "完成出牌后手牌目标运行时重解析修复"
```
