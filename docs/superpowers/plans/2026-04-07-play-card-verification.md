# 出牌 CardId 验证 — 双层防误操作 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 防止手牌变动导致脚本打出错误的牌，通过映射层移除纯位置兜底 + 执行层 cardId 验证双重保障。

**Architecture:** 第一层在 `HsBoxRecommendationProvider.ResolveOrderedEntityId` 移除 `ordered_slot_fallback` 分支，cardId 匹配失败直接返回 0 触发重试；第二层在 `ActionExecutor.MousePlayCardByMouseFlow` 拖拽前验证实体的 cardId 与预期一致，不一致返回 FAIL。PLAY 命令格式追加第5段传递预期 cardId。

**Tech Stack:** C# / .NET

---

### Task 1: 映射层 — 移除 ResolveOrderedEntityId 的纯位置兜底

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:5882-5892`

- [ ] **Step 1: 删除 ordered_slot_fallback 分支**

在 `ResolveOrderedEntityId`（第5858行）中，删除第5882~5892行的纯位置兜底代码块：

```csharp
// 删除以下代码（第5882~5892行）：
            // seed_compat 可能将卡牌替换为其他 cardId，位置不变。
            // cardId 匹配全部失败时，按纯位置兜底。
            if (zonePosition > 0 && zonePosition <= cards.Count)
            {
                var positional = cards[zonePosition - 1];
                if (positional != null)
                {
                    resolutionDetail = "ordered_slot_fallback";
                    return positional.Id;
                }
            }
```

改动后，`ResolveOrderedEntityId` 完整方法体：

```csharp
private static int ResolveOrderedEntityId(IReadOnlyList<Card> cards, HsBoxCardRef card, out string resolutionDetail)
{
    resolutionDetail = string.Empty;
    if (cards == null || card == null || cards.Count == 0)
        return 0;

    var zonePosition = card.GetZonePosition();
    if (zonePosition > 0 && zonePosition <= cards.Count)
    {
        var candidate = cards[zonePosition - 1];
        if (MatchesCardId(candidate, card.CardId))
        {
            resolutionDetail = "ordered_exact_slot_card";
            return candidate?.Id ?? 0;
        }
    }

    var byCard = cards.FirstOrDefault(candidate => MatchesCardId(candidate, card.CardId));
    if (byCard != null)
    {
        resolutionDetail = "ordered_card_match";
        return byCard.Id;
    }

    return 0;
}
```

- [ ] **Step 2: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "fix: 移除 ResolveOrderedEntityId 纯位置兜底，防止手牌变动打错牌"
```

---

### Task 2: TryMapPlayAction 追加 cardId 到 PLAY 命令

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:5228`

- [ ] **Step 1: 修改 TryMapPlayAction 的 command 构造**

在 `TryMapPlayAction`（第5196行），第5228行：

当前代码：
```csharp
            command = $"PLAY|{source}|{target}|{position}";
```

改为：
```csharp
            var sourceCardId = step.GetPrimaryCard()?.CardId ?? string.Empty;
            command = $"PLAY|{source}|{target}|{position}|{sourceCardId}";
```

- [ ] **Step 2: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: TryMapPlayAction PLAY 命令追加预期 cardId"
```

---

### Task 3: TryMapPlayActionFromBodyText 追加 cardId

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:6275`

- [ ] **Step 1: 修改 TryMapPlayActionFromBodyText 的 command 构造**

在 `TryMapPlayActionFromBodyText`（第6217行），第6275行：

当前代码：
```csharp
            command = $"PLAY|{source}|{target}|0";
```

改为：
```csharp
            var sourceCardId = ResolveCardIdFromHandByPosition(board, friendlyEntities, oneBasedIndex);
            command = $"PLAY|{source}|{target}|0|{sourceCardId}";
```

- [ ] **Step 2: 添加辅助方法 ResolveCardIdFromHandByPosition**

在 `ResolveEntityIdByZonePosition` 方法（第6506行）附近添加：

```csharp
        private static string ResolveCardIdFromHandByPosition(Board board, IReadOnlyList<EntityContextSnapshot> friendlyEntities, int oneBasedIndex)
        {
            // 优先从快照取
            if (friendlyEntities != null)
            {
                var handEntities = friendlyEntities
                    .Where(e => IsFriendlyZone(e, "HAND"))
                    .OrderBy(e => e.ZonePosition)
                    .ToList();
                if (oneBasedIndex > 0 && oneBasedIndex <= handEntities.Count)
                {
                    var cardId = handEntities[oneBasedIndex - 1].CardId;
                    if (!string.IsNullOrWhiteSpace(cardId))
                        return cardId;
                }
            }

            // 降级从 board.Hand 取
            if (board?.Hand != null && oneBasedIndex > 0 && oneBasedIndex <= board.Hand.Count)
            {
                return board.Hand[oneBasedIndex - 1]?.Template?.Id.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
```

- [ ] **Step 3: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: TryMapPlayActionFromBodyText PLAY 命令追加预期 cardId"
```

---

### Task 4: TryMapPlayActionFromBodyText_Legacy 追加 cardId

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs:6205`

- [ ] **Step 1: 修改 Legacy 方法的 command 构造**

在 `TryMapPlayActionFromBodyText_Legacy`（第6180行），第6205行：

当前代码：
```csharp
            command = $"PLAY|{source}|0|0";
```

改为：
```csharp
            var sourceCardId = board.Hand[oneBasedIndex - 1]?.Template?.Id.ToString() ?? string.Empty;
            command = $"PLAY|{source}|0|0|{sourceCardId}";
```

注：Legacy 方法没有 `friendlyEntities` 参数，直接从 `board.Hand` 取即可。此时 `oneBasedIndex` 已经通过了 `board.Hand.Count` 范围检查（第6198行的 `ResolveEntityIdByZonePosition` 会拒绝越界），安全可用。

- [ ] **Step 2: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat: TryMapPlayActionFromBodyText_Legacy PLAY 命令追加预期 cardId"
```

---

### Task 5: ActionExecutor PLAY case 解析第5段 expectedCardId

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:1209-1232`

- [ ] **Step 1: 解析 expectedCardId 并传入 MousePlayCardByMouseFlow**

在 `ActionExecutor.cs` 的 PLAY case（第1209行），修改为：

当前代码：
```csharp
                case "PLAY":
                    {
                        int sourceId = int.Parse(parts[1]);
                        int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        int targetHeroSide = -1; // -1: 不是英雄目标, 0: 我方英雄, 1: 敌方英雄
                        bool sourceUsesBoardDrop = false;

                        try
                        {
                            var s = reader?.ReadGameState();
                            if (targetId > 0)
                            {
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetId) targetHeroSide = 1;
                            }

                            var gs = GetGameState();
                            if (gs != null)
                                TryUsesBoardDropForPlay(gs, sourceId, out sourceUsesBoardDrop);
                        }
                        catch { }

                        return _coroutine.RunAndWait(MousePlayCardByMouseFlow(sourceId, targetId, position, targetHeroSide, sourceUsesBoardDrop));
                    }
```

改为：
```csharp
                case "PLAY":
                    {
                        int sourceId = int.Parse(parts[1]);
                        int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                        int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
                        string expectedCardId = parts.Length > 4 ? parts[4] : null;
                        int targetHeroSide = -1; // -1: 不是英雄目标, 0: 我方英雄, 1: 敌方英雄
                        bool sourceUsesBoardDrop = false;

                        try
                        {
                            var s = reader?.ReadGameState();
                            if (targetId > 0)
                            {
                                if (s?.HeroFriend != null && s.HeroFriend.EntityId == targetId) targetHeroSide = 0;
                                else if (s?.HeroEnemy != null && s.HeroEnemy.EntityId == targetId) targetHeroSide = 1;
                            }

                            var gs = GetGameState();
                            if (gs != null)
                                TryUsesBoardDropForPlay(gs, sourceId, out sourceUsesBoardDrop);
                        }
                        catch { }

                        return _coroutine.RunAndWait(MousePlayCardByMouseFlow(sourceId, targetId, position, targetHeroSide, sourceUsesBoardDrop, expectedCardId));
                    }
```

变动点：新增 `expectedCardId` 解析（第5段），传入 `MousePlayCardByMouseFlow`。

- [ ] **Step 2: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "feat: PLAY case 解析第5段 expectedCardId 并传入执行函数"
```

---

### Task 6: MousePlayCardByMouseFlow 增加 cardId 验证

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:2213-2265`

- [ ] **Step 1: 修改方法签名**

第2213行，当前签名：
```csharp
        private static IEnumerator<float> MousePlayCardByMouseFlow(int entityId, int targetEntityId, int position, int targetHeroSide, bool sourceUsesBoardDrop)
```

改为：
```csharp
        private static IEnumerator<float> MousePlayCardByMouseFlow(int entityId, int targetEntityId, int position, int targetHeroSide, bool sourceUsesBoardDrop, string expectedCardId = null)
```

- [ ] **Step 2: 在位置稳定后、鼠标按下前插入 cardId 验证**

在位置稳定检查通过后（第2265行 `yield break` 之后），手牌布局等待之前（第2267行 `// 拖拽前等待手牌布局完成` 之前），插入验证代码：

即在第2266行（`positionStable` 检查的 `}` 之后）和第2267行（`// 拖拽前等待手牌布局完成`）之间，插入：

```csharp
            // ── cardId 验证：确保即将拖拽的牌是预期的牌 ──
            if (!string.IsNullOrWhiteSpace(expectedCardId))
            {
                var gsVerify = GetGameState();
                var actualCardId = ResolveEntityCardId(gsVerify, entityId);
                if (!string.IsNullOrWhiteSpace(actualCardId)
                    && !string.Equals(actualCardId, expectedCardId, StringComparison.OrdinalIgnoreCase))
                {
                    AppendActionTrace(
                        "PLAY(mouse-flow) card_mismatch entity=" + entityId
                        + " expected=" + expectedCardId
                        + " actual=" + actualCardId);
                    _coroutine.SetResult("FAIL:PLAY:card_mismatch:" + entityId + ":" + expectedCardId + ":" + actualCardId);
                    yield break;
                }
            }

```

- [ ] **Step 3: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs
git commit -m "feat: MousePlayCardByMouseFlow 拖拽前验证 cardId，不匹配返回 FAIL"
```

---

### Task 7: 最终验证与合并提交

- [ ] **Step 1: 全局搜索确认无遗漏**

搜索所有构造 `PLAY|` 命令的位置，确认都已追加 cardId 或兼容空值：

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
grep -rn '"PLAY|' BotMain/ HearthstonePayload/ --include="*.cs" | grep -v "case\|//"
```

预期：所有 `$"PLAY|{source}|{target}|{position}|{cardId}"` 格式已更新。

- [ ] **Step 2: 搜索确认 MousePlayCardByMouseFlow 无其他调用点遗漏**

```bash
grep -rn "MousePlayCardByMouseFlow" HearthstonePayload/ActionExecutor.cs
```

预期：只有两处——方法定义和 PLAY case 的调用。确认调用处已传入 `expectedCardId`。

- [ ] **Step 3: 检查是否有其他地方调用 ResolveOrderedEntityId 依赖 slot_fallback**

```bash
grep -rn "ordered_slot_fallback\|slot_fallback" BotMain/ --include="*.cs"
```

预期：无匹配结果（已删除）。
