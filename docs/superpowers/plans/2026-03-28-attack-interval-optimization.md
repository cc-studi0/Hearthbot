# 连续攻击间隔优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将连续攻击之间的冗余间隔从 ~900ms 压缩到 ~60ms，通过跨批次感知连续攻击状态来触发快速通道。

**Architecture:** 利用已有的 `lastRecommendationWasAttackOnly` 布尔标识，在 BotService.cs 的三个阶段（post-action、批次间等待、主循环准备）中跳过冗余操作。新增一个 `cachedFriendlyEntities` 变量缓存友方实体信息供连续攻击周期复用。

**Tech Stack:** C# / .NET, 修改 BotMain/BotService.cs

---

### Task 1: Post-Action 阶段 — 扩展 chain-attack 快速路径条件

**Files:**
- Modify: `BotMain/BotService.cs:2644-2651`

- [ ] **Step 1: 修改 chain-attack 条件判断**

将行 2645 的条件从仅检查批次内下一个动作，扩展为也检查跨批次连续攻击状态：

```csharp
// 连续攻击：快速轮询就绪，跳过固定延迟
// 扩展条件：批次内下一个是攻击，或上一批全部为攻击（跨批次连续攻击）
if (isAttack && (nextIsAttack || lastRecommendationWasAttackOnly))
```

原始代码：
```csharp
if (isAttack && nextIsAttack)
```

替换为：
```csharp
if (isAttack && (nextIsAttack || lastRecommendationWasAttackOnly))
```

这样当 hsbox 每次返回单个 ATTACK 动作（count=1）时，只要上一批也是纯攻击，就走快速轮询 `WaitForGameReady(40, 50)` 而非慢速路径的 `actionDelayMs` + `WaitForGameReady(30, 300)`。

- [ ] **Step 2: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "优化：扩展 chain-attack 快速路径条件支持跨批次连续攻击"
```

---

### Task 2: 批次间等待 — 连续攻击时跳过 120ms Sleep

**Files:**
- Modify: `BotMain/BotService.cs:2789-2797`

- [ ] **Step 1: 在 Thread.Sleep(120) 前添加连续攻击跳过条件**

原始代码（行 2789-2797）：
```csharp
var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
if (_followHsBoxRecommendations
    && !string.IsNullOrWhiteSpace(lastAction)
    && !lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
{
    actionFailStreak = 0;
    Thread.Sleep(120);
    continue;
}
```

替换为：
```csharp
var lastAction = actions.Count > 0 ? actions[actions.Count - 1] : null;
if (_followHsBoxRecommendations
    && !string.IsNullOrWhiteSpace(lastAction)
    && !lastAction.Equals("END_TURN", StringComparison.OrdinalIgnoreCase))
{
    actionFailStreak = 0;
    if (!lastRecommendationWasAttackOnly)
        Thread.Sleep(120);
    continue;
}
```

当本批全部为攻击时（`lastRecommendationWasAttackOnly` 刚在上方行 2786 更新为 true），跳过 120ms 等待直接 continue 进入下一循环。

- [ ] **Step 2: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "优化：连续攻击批次间跳过 120ms 固定等待"
```

---

### Task 3: 主循环准备阶段 — 连续攻击时跳过冗余查询

**Files:**
- Modify: `BotMain/BotService.cs:1880` (新增缓存变量)
- Modify: `BotMain/BotService.cs:2349-2437` (条件跳过准备步骤)

- [ ] **Step 1: 在循环外添加缓存变量**

在行 1880（`bool lastRecommendationWasAttackOnly = false;` 之后）添加：

```csharp
bool lastRecommendationWasAttackOnly = false;
IReadOnlyList<EntityContextSnapshot> cachedFriendlyEntities = null;
List<Card.Cards> cachedDeckCards = null;
```

- [ ] **Step 2: 将准备阶段代码包裹在连续攻击跳过条件中**

原始代码（行 2346-2375）：
```csharp
var recommendationStage = "plugin_simulation";
_pluginSystem?.FireOnSimulation();

// 查询牌库剩余卡牌
List<Card.Cards> deckCards = null;
try
{
    recommendationStage = "deck_state";
    var deckResp = pipe.SendAndReceive("GET_DECK_STATE", 3000);
    if (deckResp != null && deckResp.StartsWith("DECK_STATE:", StringComparison.Ordinal))
    {
        var raw = deckResp.Substring("DECK_STATE:".Length);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            deckCards = new List<Card.Cards>();
            foreach (var part in raw.Split('|'))
            {
                if (TryParseCardId(part, out var cid))
                    deckCards.Add(cid);
            }
            Log($"[Deck] remaining cards: {deckCards.Count}");
        }
    }
}
catch { /* 查询失败时 deckCards 保持 null，不影响 AI 运行 */ }

recommendationStage = "resolve_deck_context";
_currentDeckContext = ResolveDeckContext(deckCards) ?? _currentDeckContext;
recommendationStage = "friendly_entity_context";
var friendlyEntities = RefreshFriendlyEntityContext(pipe, planningBoard?.TurnCount ?? 0, "Action");
```

替换为：
```csharp
var recommendationStage = "plugin_simulation";
List<Card.Cards> deckCards = null;
IReadOnlyList<EntityContextSnapshot> friendlyEntities = null;

if (lastRecommendationWasAttackOnly && planningBoard != null)
{
    // 连续攻击快速通道：跳过牌库查询、牌组上下文、友方实体刷新
    deckCards = cachedDeckCards;
    friendlyEntities = cachedFriendlyEntities;
    Log("[Action] skipped preparation (consecutive attack cycle)");
}
else
{
    _pluginSystem?.FireOnSimulation();

    // 查询牌库剩余卡牌
    try
    {
        recommendationStage = "deck_state";
        var deckResp = pipe.SendAndReceive("GET_DECK_STATE", 3000);
        if (deckResp != null && deckResp.StartsWith("DECK_STATE:", StringComparison.Ordinal))
        {
            var raw = deckResp.Substring("DECK_STATE:".Length);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                deckCards = new List<Card.Cards>();
                foreach (var part in raw.Split('|'))
                {
                    if (TryParseCardId(part, out var cid))
                        deckCards.Add(cid);
                }
                Log($"[Deck] remaining cards: {deckCards.Count}");
            }
        }
    }
    catch { /* 查询失败时 deckCards 保持 null，不影响 AI 运行 */ }

    recommendationStage = "resolve_deck_context";
    _currentDeckContext = ResolveDeckContext(deckCards) ?? _currentDeckContext;
    recommendationStage = "friendly_entity_context";
    friendlyEntities = RefreshFriendlyEntityContext(pipe, planningBoard?.TurnCount ?? 0, "Action");
}

// 更新缓存供下次连续攻击使用
cachedDeckCards = deckCards;
cachedFriendlyEntities = friendlyEntities;
```

- [ ] **Step 3: 跳过 TryRunHumanizedTurnPrelude**

原始代码（行 2436-2437）：
```csharp
if (actions != null && actions.Count > 0)
    TryRunHumanizedTurnPrelude(pipe, planningBoard, friendlyEntities, actions.Count);
```

替换为：
```csharp
if (actions != null && actions.Count > 0 && !lastRecommendationWasAttackOnly)
    TryRunHumanizedTurnPrelude(pipe, planningBoard, friendlyEntities, actions.Count);
```

注意：这里检查的是旧的 `lastRecommendationWasAttackOnly`（上一批的值），因为新的值在行 2786 才更新。这正是我们想要的 — 如果上一批全是攻击，就跳过本次 turn prelude。

- [ ] **Step 4: 验证编译**

Run: `dotnet build BotMain/BotMain.csproj --no-restore -v q`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "优化：连续攻击周期跳过冗余准备步骤（牌库查询、实体刷新、拟人化前奏）"
```

---

### Task 4: 验证与日志确认

- [ ] **Step 1: 启动 bot 并观察连续攻击日志**

启动脚本进入对战，等待出现连续攻击场景，观察日志确认：
1. 出现 `[Action] skipped preparation (consecutive attack cycle)` 日志
2. `postReadyStatus` 显示 `ready_chain_attack` 或 `timeout_chain_attack`（而非之前的 `ready`）
3. `postDelayMs` 为 0（不再出现 ~210ms）
4. 两次攻击的 `[Recommend]` 时间戳间隔明显缩短

- [ ] **Step 2: Commit 最终确认**

如果一切正常，进行最终提交：
```bash
git add BotMain/BotService.cs
git commit -m "优化：连续攻击间隔优化完成，验证通过"
```
