# 严格手牌源牌身份校验 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让所有从手牌发起的动作都必须同时匹配 `cardId + zonePosition`，删除纯位置兜底，并在执行前做最终身份校验，失败时走短重试和重新拉推荐而不是误打。

**Architecture:** 这次实现分成四层收口：先在推荐层禁止 `ordered_slot_fallback` / `hand_snapshot_slot_fallback` 这类危险解析；再给 `PLAY` / `TRADE` / `BG_PLAY` 命令补充源牌元数据；然后在 payload 侧抽出一个可测试的 `HandSourceIdentityResolver`，在真正 grab 之前做最后校验；最后让 `BotService` 把 `source_identity_mismatch` 当成可恢复失败，触发短等待与重新拉推荐。这样可以同时覆盖 HsBox 构筑、body text 回退、AI 直接出牌和战旗手牌打出。

**Tech Stack:** C# / .NET 8、.NET Framework 4.7.2、xUnit、现有 `BotMain` / `HearthstonePayload` / `BotCore.Tests` 测试基础设施

**Spec:** `docs/superpowers/specs/2026-03-29-strict-hand-source-identity-design.md`

---

## 范围说明

这份 spec 聚焦的是一个单一子系统：`手牌源牌身份校验`。虽然会同时修改推荐层、调度层和 payload 层，但它们共同服务于同一个可独立上线的目标：

- 不再按纯位置猜测手牌源牌
- 无法唯一确认时，只允许重试或刷新推荐，不允许误打

本计划一次性覆盖构筑 `PLAY` / `TRADE`、战旗 `BG_PLAY`，以及打出后“从手牌中选择目标”的运行时解析。

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `BotMain/HandActionCommandMetadata.cs` | 新建 | 统一封装手牌动作命令的元数据追加与解析 |
| `BotCore.Tests/HandActionCommandMetadataTests.cs` | 新建 | 验证命令元数据的追加、解析与兼容旧格式 |
| `BotMain/HsBoxRecommendationProvider.cs` | 修改 | 收紧手牌源牌解析，删除纯位置 fallback，战旗 `BG_PLAY` 补充元数据 |
| `BotCore.Tests/HsBoxRecommendationProviderTests.cs` | 修改 | 把原本接受 fallback 的测试改成失败/重试预期，并新增严格匹配场景 |
| `BotMain/BotService.cs` | 修改 | 在发送动作前补充手牌源牌元数据，处理 `source_identity_mismatch` 恢复流程 |
| `BotCore.Tests/BotServiceHandSourceRecoveryTests.cs` | 新建 | 验证 `source_identity_mismatch` 分类与恢复判断逻辑 |
| `HearthstonePayload/HandSourceIdentityResolver.cs` | 新建 | 可测试的 payload 侧手牌身份校验器 |
| `BotCore.Tests/HandSourceIdentityResolverTests.cs` | 新建 | 验证 card/slot/duplicate 等校验结果 |
| `HearthstonePayload/ActionExecutor.cs` | 修改 | 解析新增元数据，在 `PLAY` / `TRADE` / `BG_PLAY` grab 前做最终校验 |
| `HearthstonePayload/PlayRuntimeTargetResolver.cs` | 修改 | 手牌目标解析禁用 slot-only fallback，要求 `cardId + zonePosition` 同时命中 |
| `BotCore.Tests/PlayRuntimeTargetResolverTests.cs` | 修改 | 增加“仅 slot 命中不允许成功”等严格模式测试 |

> 说明：`ActionExecutor.cs` 已经非常大，因此 payload 侧的身份判断必须抽成单独文件 `HandSourceIdentityResolver.cs`，避免继续把复杂分支堆进 `ActionExecutor.cs`。

---

### Task 1: 建立手牌动作元数据协议与测试

**Files:**
- Create: `BotMain/HandActionCommandMetadata.cs`
- Create: `BotCore.Tests/HandActionCommandMetadataTests.cs`

- [ ] **Step 1: 先写命令元数据的失败测试**

  在 `BotCore.Tests/HandActionCommandMetadataTests.cs` 新建以下测试：

  ```csharp
  using BotMain;
  using Xunit;

  namespace BotCore.Tests
  {
      public class HandActionCommandMetadataTests
      {
          [Fact]
          public void AppendPlayMetadata_PreservesBaseSegments_AndAddsCardAndSlot()
          {
              var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", "GAME_005", 5);

              Assert.Equal("PLAY|71|0|0|GAME_005|5", command);
          }

          [Fact]
          public void TryParsePlayMetadata_SupportsLegacyCommandWithoutMetadata()
          {
              Assert.True(HandActionCommandMetadata.TryParse("PLAY|71|0|0", out var parsed));
              Assert.Equal("PLAY", parsed.ActionType);
              Assert.Equal(71, parsed.SourceEntityId);
              Assert.Equal(string.Empty, parsed.SourceCardId);
              Assert.Equal(0, parsed.SourceZonePosition);
          }

          [Fact]
          public void TryParseTradeMetadata_ReadsSourceIdentity()
          {
              Assert.True(HandActionCommandMetadata.TryParse("TRADE|88|TLC_902|4", out var parsed));
              Assert.Equal("TRADE", parsed.ActionType);
              Assert.Equal(88, parsed.SourceEntityId);
              Assert.Equal("TLC_902", parsed.SourceCardId);
              Assert.Equal(4, parsed.SourceZonePosition);
          }
      }
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandActionCommandMetadataTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `HandActionCommandMetadata` 不存在。

- [ ] **Step 3: 写最小命令元数据帮助类**

  在 `BotMain/HandActionCommandMetadata.cs` 写出最小实现：

  ```csharp
  namespace BotMain
  {
      internal readonly struct HandActionCommandParts
      {
          public string ActionType { get; init; }
          public int SourceEntityId { get; init; }
          public int TargetEntityId { get; init; }
          public int Position { get; init; }
          public string SourceCardId { get; init; }
          public int SourceZonePosition { get; init; }
      }

      internal static class HandActionCommandMetadata
      {
          public static bool IsHandSourceAction(string actionType)
          {
              return string.Equals(actionType, "PLAY", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(actionType, "TRADE", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(actionType, "BG_PLAY", StringComparison.OrdinalIgnoreCase);
          }

          public static string AppendPlay(string command, string sourceCardId, int sourceZonePosition)
          {
              return string.IsNullOrWhiteSpace(sourceCardId) || sourceZonePosition <= 0
                  ? command
                  : command + "|" + sourceCardId + "|" + sourceZonePosition;
          }

          public static string AppendTrade(string command, string sourceCardId, int sourceZonePosition)
          {
              return string.IsNullOrWhiteSpace(sourceCardId) || sourceZonePosition <= 0
                  ? command
                  : command + "|" + sourceCardId + "|" + sourceZonePosition;
          }

          public static bool TryParse(string command, out HandActionCommandParts parts)
          {
              parts = default;
              if (string.IsNullOrWhiteSpace(command))
                  return false;

              var segments = command.Split('|');
              if (segments.Length < 2)
                  return false;

              var actionType = segments[0];
              if (!int.TryParse(segments[1], out var sourceEntityId))
                  return false;

              if (string.Equals(actionType, "TRADE", StringComparison.OrdinalIgnoreCase))
              {
                  parts = new HandActionCommandParts
                  {
                      ActionType = actionType,
                      SourceEntityId = sourceEntityId,
                      SourceCardId = segments.Length > 2 ? segments[2] ?? string.Empty : string.Empty,
                      SourceZonePosition = segments.Length > 3 && int.TryParse(segments[3], out var tradeZonePos) ? tradeZonePos : 0
                  };
                  return true;
              }

              var targetEntityId = segments.Length > 2 && int.TryParse(segments[2], out var parsedTarget) ? parsedTarget : 0;
              var position = segments.Length > 3 && int.TryParse(segments[3], out var parsedPosition) ? parsedPosition : 0;
              var sourceCardId = segments.Length > 4 ? segments[4] ?? string.Empty : string.Empty;
              var sourceZonePosition = segments.Length > 5 && int.TryParse(segments[5], out var parsedSourceZonePos) ? parsedSourceZonePos : 0;

              parts = new HandActionCommandParts
              {
                  ActionType = actionType,
                  SourceEntityId = sourceEntityId,
                  TargetEntityId = targetEntityId,
                  Position = position,
                  SourceCardId = sourceCardId,
                  SourceZonePosition = sourceZonePosition
              };
              return true;
          }
      }
  }
  ```

- [ ] **Step 4: 再跑一次测试，确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandActionCommandMetadataTests" -v normal
  ```

  Expected:
  `PASS`，三个新测试全部通过。

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/HandActionCommandMetadata.cs BotCore.Tests/HandActionCommandMetadataTests.cs
  git commit -m "新增手牌动作命令元数据协议"
  ```

---

### Task 2: 收紧 `HsBoxRecommendationProvider` 的手牌源牌解析

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs`
- Modify: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

- [ ] **Step 1: 先补失败测试，锁定“禁止 fallback”行为**

  在 `BotCore.Tests/HsBoxRecommendationProviderTests.cs` 增加以下测试：

  ```csharp
  [Fact]
  public void RecommendActions_DoesNotUseOrderedSlotFallback_ForHandSource()
  {
      var state = CreateState(
          414,
          raw: "coin-ordered-slot-fallback",
          actionName: "play_special",
          cardId: "GAME_005",
          cardName: "幸运币",
          zonePosition: 5,
          bodyText: "推荐打法 打出5号位法术 幸运币");
      var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

      var board = new Board
      {
          Hand = new List<Card>
          {
              CreateCard(19, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
              CreateCard(20, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
              CreateCard(21, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
              CreateCard(22, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
              CreateCard(23, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
          }
      };

      var result = provider.RecommendActions(new ActionRecommendationRequest("seed", board, null, null));

  Assert.True(result.ShouldRetryWithoutAction);
  Assert.Empty(result.Actions);
  Assert.Contains("hand_source", result.Detail, StringComparison.OrdinalIgnoreCase);
  Assert.DoesNotContain("ordered_slot_fallback", result.Detail, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void RecommendActions_FailsWhenCardMatchesButSlotChanged_ForHandSource()
  {
      var state = CreateState(
          415,
          raw: "coin-card-matches-slot-changed",
          actionName: "play_special",
          cardId: "GAME_005",
          cardName: "幸运币",
          zonePosition: 5,
          bodyText: "推荐打法 打出5号位法术 幸运币");
      var provider = new HsBoxGameRecommendationProvider(new FakeBridge(state), actionWaitTimeoutMs: 20, actionPollIntervalMs: 1);

      var result = provider.RecommendActions(new ActionRecommendationRequest(
          "seed",
          null,
          null,
          null,
          friendlyEntities: new[]
          {
              new EntityContextSnapshot { EntityId = 71, CardId = "GAME_005", Zone = "HAND", ZonePosition = 3 },
              new EntityContextSnapshot { EntityId = 72, CardId = "CORE_CS2_231", Zone = "HAND", ZonePosition = 5 }
          }));

      Assert.True(result.ShouldRetryWithoutAction);
      Assert.Empty(result.Actions);
      Assert.Contains("slot_changed", result.Detail, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void TryMapPlayActionFromBodyText_DoesNotGuessBySlotOnly()
  {
      var board = new Board
      {
          Hand = new List<Card>
          {
              CreateCard(11, Card.Cards.CORE_CS2_231, "小精灵", "Wisp"),
              CreateCard(12, Card.Cards.CORE_CS2_231, "小精灵", "Wisp")
          }
      };

      var ok = HsBoxRecommendationMapper.TryMapPlayFromBodyTextForTests(
          "推荐打法 打出2号位法术 幸运币",
          board,
          null,
          out var command,
          out var detail);

      Assert.False(ok);
      Assert.Null(command);
      Assert.Contains("hand_source", detail, StringComparison.OrdinalIgnoreCase);
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v normal
  ```

  Expected:
  `FAIL`，至少新加的三类测试失败，而且当前老测试仍然允许 `ordered_slot_fallback`。

- [ ] **Step 3: 改写手牌源牌解析，只允许 exact slot + card**

  在 `BotMain/HsBoxRecommendationProvider.cs` 完成以下改动：

  1. 新增严格解析帮助方法，放在 `ResolveFriendlyHandEntityId(...)` 附近：

  ```csharp
  private static int ResolveStrictFriendlyHandEntityId(
      Board board,
      IReadOnlyList<EntityContextSnapshot> friendlyEntities,
      HsBoxCardRef card,
      out string resolutionDetail)
  {
      resolutionDetail = "hand_source_exact_match_missing";
      if (card == null)
          return 0;

      var zonePosition = card.GetZonePosition();
      var cardId = card.CardId ?? string.Empty;
      if (zonePosition <= 0 || string.IsNullOrWhiteSpace(cardId))
      {
          resolutionDetail = "hand_source_identity_missing";
          return 0;
      }

      var snapshotExact = ResolveFriendlyEntityIdByZonePosition(
          friendlyEntities,
          "HAND",
          zonePosition,
          cardId,
          allowPureSlotFallback: false,
          out var snapshotDetail);
      if (snapshotExact > 0)
      {
          resolutionDetail = snapshotDetail;
          return snapshotExact;
      }

      if (friendlyEntities != null)
      {
          var sameCard = friendlyEntities
              .Where(entity => IsFriendlyZone(entity, "HAND") && MatchesCardId(entity.CardId, cardId))
              .ToList();
          if (sameCard.Count > 1)
          {
              resolutionDetail = "hand_source_duplicate_card_ambiguous";
              return 0;
          }

          if (sameCard.Count == 1 && sameCard[0].ZonePosition != zonePosition)
          {
              resolutionDetail = "hand_source_card_matched_but_slot_changed";
              return 0;
          }

          var sameSlot = friendlyEntities
              .FirstOrDefault(entity => IsFriendlyZone(entity, "HAND") && entity.ZonePosition == zonePosition);
          if (sameSlot != null && !MatchesCardId(sameSlot.CardId, cardId))
          {
              resolutionDetail = "hand_source_slot_matched_but_card_changed";
              return 0;
          }
      }

      if (board?.Hand != null && zonePosition <= board.Hand.Count)
      {
          var candidate = board.Hand[zonePosition - 1];
          if (MatchesCardId(candidate, cardId))
          {
              resolutionDetail = "ordered_exact_slot_card";
              return candidate?.Id ?? 0;
          }

          resolutionDetail = candidate != null
              ? "hand_source_slot_matched_but_card_changed"
              : "hand_source_exact_match_missing";
      }

      return 0;
  }
  ```

  2. 让 `TryMapPlayAction(...)` 和 `TryMapTradeAction(...)` 用这个新方法取代原来的 `ResolveFriendlyHandEntityId(...)`。
  3. 让 `TryMapPlayActionFromBodyText(...)` 与 `TryMapPlayActionFromBodyText_Legacy(...)` 不再按纯位置猜手牌；在 body text 没有可靠 `cardId` 的场景直接失败并返回可恢复 detail。
  4. 保留原有 `ResolveFriendlyHandEntityId(...)` 给非“源牌身份”场景使用，但任何“从手牌打出”的入口不再调用它。

- [ ] **Step 4: 更新旧测试预期**

  把现有会通过 `ordered_slot_fallback` 成功的测试改成新的严格模式预期。重点检查：

  - `RecommendActions_ReportsFallbackWhenOnlyOrderedHandSlotCanBeUsed`
  - 任何断言包含 `ordered_slot_fallback` / `hand_snapshot_slot_fallback` 的测试

  更新后应当体现：

  - 要么 `ShouldRetryWithoutAction == true`
  - 要么返回明确的 `hand_source_*` 失败 detail

- [ ] **Step 5: 跑测试确认推荐层收紧完成**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v normal
  ```

  Expected:
  `PASS`，新增测试通过，旧 fallback 测试已改成严格预期。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
  git commit -m "收紧手牌源牌解析并移除危险兜底"
  ```

---

### Task 3: 把手牌源牌元数据接入 `PLAY` / `TRADE` / `BG_PLAY`

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotMain/HsBoxRecommendationProvider.cs`
- Modify: `BotCore.Tests/HandActionCommandMetadataTests.cs`

- [ ] **Step 1: 先补元数据接线的测试**

  在 `BotCore.Tests/HandActionCommandMetadataTests.cs` 增加：

  ```csharp
  [Fact]
  public void AppendPlayMetadata_ReturnsOriginalCommand_WhenCardIdMissing()
  {
      var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", string.Empty, 5);
      Assert.Equal("PLAY|71|0|0", command);
  }

  [Fact]
  public void AppendPlayMetadata_ReturnsOriginalCommand_WhenZonePositionMissing()
  {
      var command = HandActionCommandMetadata.AppendPlay("PLAY|71|0|0", "GAME_005", 0);
      Assert.Equal("PLAY|71|0|0", command);
  }
  ```

- [ ] **Step 2: 运行测试，确认新增断言先失败或待实现**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandActionCommandMetadataTests" -v normal
  ```

  Expected:
  如果还没实现对应逻辑则 `FAIL`；如果 Task 1 已按推荐代码落地，这两条测试应直接 `PASS`。

- [ ] **Step 3: 在 `BotService` 为构筑 `PLAY` / `TRADE` 补充元数据**

  在 `BotMain/BotService.cs` 的动作发送环中，`SendActionCommand(pipe, action, 5000)` 之前新增一个包装步骤，例如：

  ```csharp
  var outboundAction = AttachHandSourceMetadata(action, planningBoard);
  var result = SendActionCommand(pipe, outboundAction, 5000) ?? "NO_RESPONSE";
  ```

  新增帮助方法放在 `TryGetActionSourceEntityId(...)` 附近：

  ```csharp
  private static string AttachHandSourceMetadata(string action, Board planningBoard)
  {
      if (string.IsNullOrWhiteSpace(action) || planningBoard == null)
          return action;

      if (!HandActionCommandMetadata.TryParse(action, out var parsed))
          return action;

      if (string.Equals(parsed.ActionType, "PLAY", StringComparison.OrdinalIgnoreCase)
          || string.Equals(parsed.ActionType, "TRADE", StringComparison.OrdinalIgnoreCase))
      {
          var source = planningBoard.Hand?.FirstOrDefault(card => card != null && card.Id == parsed.SourceEntityId);
          if (source?.Template == null)
              return action;

          var sourceCardId = source.Template.Id.ToString();
          var sourceZonePosition = planningBoard.Hand.IndexOf(source) + 1;
          return string.Equals(parsed.ActionType, "TRADE", StringComparison.OrdinalIgnoreCase)
              ? HandActionCommandMetadata.AppendTrade(action, sourceCardId, sourceZonePosition)
              : HandActionCommandMetadata.AppendPlay(action, sourceCardId, sourceZonePosition);
      }

      return action;
  }
  ```

- [ ] **Step 4: 在战旗桥接层为 `BG_PLAY` 直接补充元数据**

  在 `BotMain/HsBoxRecommendationProvider.cs` 的战旗 `ConvertStepToCommand(...)` 中，把：

  ```csharp
  return $"BG_PLAY|{cardEntityId}|{targetEntityId}|{dropPositionReference}";
  ```

  改为：

  ```csharp
  var command = $"BG_PLAY|{cardEntityId}|{targetEntityId}|{dropPositionReference}";
  return HandActionCommandMetadata.AppendPlay(command, card?.CardId ?? string.Empty, pos);
  ```

- [ ] **Step 5: 跑一轮针对性测试与编译**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandActionCommandMetadataTests" -v normal
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v normal
  dotnet build BotMain/BotMain.csproj --no-restore
  ```

  Expected:
  `PASS` / `Build succeeded.`，现有 `BG_PLAY` 命令测试如果直接断言整串命令，需要同步追加 metadata 预期。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs BotMain/HsBoxRecommendationProvider.cs BotCore.Tests/HandActionCommandMetadataTests.cs BotMain/HandActionCommandMetadata.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs
  git commit -m "为手牌动作补充源牌身份元数据"
  ```

---

### Task 4: 新建 payload 侧 `HandSourceIdentityResolver` 并先写测试

**Files:**
- Create: `HearthstonePayload/HandSourceIdentityResolver.cs`
- Create: `BotCore.Tests/HandSourceIdentityResolverTests.cs`

- [ ] **Step 1: 写 resolver 的失败测试**

  在 `BotCore.Tests/HandSourceIdentityResolverTests.cs` 新建以下测试：

  ```csharp
  using HearthstonePayload;
  using Xunit;

  namespace BotCore.Tests
  {
      public class HandSourceIdentityResolverTests
      {
          [Fact]
          public void Validate_ReturnsExactMatch_WhenCardAndSlotMatch()
          {
              var resolution = HandSourceIdentityResolver.Validate(
                  new HandSourceIdentityExpectation
                  {
                      SourceEntityId = 71,
                      SourceCardId = "GAME_005",
                      SourceZonePosition = 5
                  },
                  new HandSourceIdentitySnapshot
                  {
                      EntityId = 71,
                      InFriendlyHand = true,
                      CardId = "GAME_005",
                      ZonePosition = 5
                  });

              Assert.True(resolution.Success);
              Assert.Equal("exact_match", resolution.Detail);
          }

          [Fact]
          public void Validate_ReturnsSlotChanged_WhenCardMatchesButSlotChanged()
          {
              var resolution = HandSourceIdentityResolver.Validate(
                  new HandSourceIdentityExpectation
                  {
                      SourceEntityId = 71,
                      SourceCardId = "GAME_005",
                      SourceZonePosition = 5
                  },
                  new HandSourceIdentitySnapshot
                  {
                      EntityId = 71,
                      InFriendlyHand = true,
                      CardId = "GAME_005",
                      ZonePosition = 3
                  });

              Assert.False(resolution.Success);
              Assert.Equal("slot_changed", resolution.Detail);
          }

          [Fact]
          public void Validate_ReturnsCardChanged_WhenSlotMatchesButCardChanged()
          {
              var resolution = HandSourceIdentityResolver.Validate(
                  new HandSourceIdentityExpectation
                  {
                      SourceEntityId = 71,
                      SourceCardId = "GAME_005",
                      SourceZonePosition = 5
                  },
                  new HandSourceIdentitySnapshot
                  {
                      EntityId = 71,
                      InFriendlyHand = true,
                      CardId = "CORE_CS2_231",
                      ZonePosition = 5
                  });

              Assert.False(resolution.Success);
              Assert.Equal("card_changed", resolution.Detail);
          }
      }
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandSourceIdentityResolverTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `HandSourceIdentityResolver` 不存在。

- [ ] **Step 3: 写最小 resolver**

  在 `HearthstonePayload/HandSourceIdentityResolver.cs` 写出最小实现：

  ```csharp
  namespace HearthstonePayload
  {
      internal sealed class HandSourceIdentityExpectation
      {
          public int SourceEntityId;
          public string SourceCardId = string.Empty;
          public int SourceZonePosition;
      }

      internal sealed class HandSourceIdentitySnapshot
      {
          public int EntityId;
          public bool InFriendlyHand;
          public string CardId = string.Empty;
          public int ZonePosition;
      }

      internal sealed class HandSourceIdentityResolution
      {
          public bool Success;
          public string Detail = string.Empty;
      }

      internal static class HandSourceIdentityResolver
      {
          public static HandSourceIdentityResolution Validate(
              HandSourceIdentityExpectation expected,
              HandSourceIdentitySnapshot actual)
          {
              if (expected == null)
                  return new HandSourceIdentityResolution { Success = true, Detail = "no_expectation" };

              if (actual == null || !actual.InFriendlyHand)
                  return new HandSourceIdentityResolution { Success = false, Detail = "entity_left_hand" };

              var cardMatches = string.Equals(
                  expected.SourceCardId ?? string.Empty,
                  actual.CardId ?? string.Empty,
                  StringComparison.OrdinalIgnoreCase);
              var slotMatches = expected.SourceZonePosition > 0 && expected.SourceZonePosition == actual.ZonePosition;

              if (cardMatches && slotMatches)
                  return new HandSourceIdentityResolution { Success = true, Detail = "exact_match" };

              if (cardMatches)
                  return new HandSourceIdentityResolution { Success = false, Detail = "slot_changed" };

              if (slotMatches)
                  return new HandSourceIdentityResolution { Success = false, Detail = "card_changed" };

              return new HandSourceIdentityResolution { Success = false, Detail = "card_and_slot_changed" };
          }
      }
  }
  ```

- [ ] **Step 4: 再跑一次测试，确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandSourceIdentityResolverTests" -v normal
  ```

  Expected:
  `PASS`，三个 resolver 测试全部通过。

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add HearthstonePayload/HandSourceIdentityResolver.cs BotCore.Tests/HandSourceIdentityResolverTests.cs
  git commit -m "新增 payload 侧手牌源牌身份校验器"
  ```

---

### Task 5: 在 `ActionExecutor` 的 `PLAY` / `TRADE` / `BG_PLAY` grab 前接入最终身份校验

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs`
- Modify: `BotCore.Tests/HandSourceIdentityResolverTests.cs`

- [ ] **Step 1: 先扩展 resolver 测试，覆盖 `entity_left_hand` 和 `card_and_slot_changed`**

  在 `BotCore.Tests/HandSourceIdentityResolverTests.cs` 增加：

  ```csharp
  [Fact]
  public void Validate_ReturnsEntityLeftHand_WhenEntityNoLongerInHand()
  {
      var resolution = HandSourceIdentityResolver.Validate(
          new HandSourceIdentityExpectation
          {
              SourceEntityId = 71,
              SourceCardId = "GAME_005",
              SourceZonePosition = 5
          },
          new HandSourceIdentitySnapshot
          {
              EntityId = 71,
              InFriendlyHand = false,
              CardId = "GAME_005",
              ZonePosition = 5
          });

      Assert.False(resolution.Success);
      Assert.Equal("entity_left_hand", resolution.Detail);
  }
  ```

- [ ] **Step 2: 运行测试，确认新增断言先失败或待补**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandSourceIdentityResolverTests" -v normal
  ```

  Expected:
  如果前一步只做了最小实现，这条可能已经通过；如果未覆盖则补齐实现后通过。

- [ ] **Step 3: 在 `ActionExecutor` 解析可选元数据**

  在 `HearthstonePayload/ActionExecutor.cs` 的 `Execute(GameReader reader, string actionData)` 中，把 `PLAY` / `TRADE` / `BG_PLAY` 分支的参数解析从直接 `parts[1]` / `parts[2]` 改成基于 segments 数量的兼容解析。例如：

  ```csharp
  case "PLAY":
      {
          int sourceId = int.Parse(parts[1]);
          int targetId = parts.Length > 2 ? int.Parse(parts[2]) : 0;
          int position = parts.Length > 3 ? int.Parse(parts[3]) : 0;
          string expectedSourceCardId = parts.Length > 4 ? parts[4] ?? string.Empty : string.Empty;
          int expectedSourceZonePosition = parts.Length > 5 ? int.Parse(parts[5]) : 0;
          return _coroutine.RunAndWait(
              MousePlayCardByMouseFlow(
                  sourceId,
                  targetId,
                  position,
                  targetHeroSide,
                  sourceUsesBoardDrop,
                  expectedSourceCardId,
                  expectedSourceZonePosition));
      }
  ```

- [ ] **Step 4: 在三个执行路径中插入身份校验**

  修改以下协程签名并在 `TryGrabCardViaAPI(...)` 之前调用统一校验：

  - `MousePlayCardByMouseFlow(...)`
  - `MouseTradeCard(...)`
  - `BgMousePlayFromHand(...)`

  新增辅助方法放在 `TryIsEntityInFriendlyHand(...)` / `ResolveEntityCardId(...)` / `ResolveEntityZonePosition(...)` 附近：

  ```csharp
  private static bool TryValidateHandSourceIdentity(
      object gameState,
      int sourceEntityId,
      string expectedSourceCardId,
      int expectedSourceZonePosition,
      out string detail)
  {
      detail = "no_expectation";
      if (string.IsNullOrWhiteSpace(expectedSourceCardId) || expectedSourceZonePosition <= 0)
          return true;

      for (var attempt = 0; attempt < 3; attempt++)
      {
          var gs = attempt == 0 ? gameState : GetGameState();
          var snapshot = new HandSourceIdentitySnapshot
          {
              EntityId = sourceEntityId,
              InFriendlyHand = TryIsEntityInFriendlyHand(gs, sourceEntityId, out var inHand) && inHand,
              CardId = ResolveEntityCardId(gs, sourceEntityId),
              ZonePosition = ResolveEntityZonePosition(gs, sourceEntityId)
          };

          var resolution = HandSourceIdentityResolver.Validate(
              new HandSourceIdentityExpectation
              {
                  SourceEntityId = sourceEntityId,
                  SourceCardId = expectedSourceCardId,
                  SourceZonePosition = expectedSourceZonePosition
              },
              snapshot);

          if (resolution.Success)
          {
              detail = resolution.Detail;
              return true;
          }

          detail = resolution.Detail;
          Thread.Sleep(120);
      }

      return false;
  }
  ```

  `PLAY`、`TRADE`、`BG_PLAY` 分别返回：

  - `FAIL:PLAY:source_identity_mismatch:...`
  - `FAIL:TRADE:source_identity_mismatch:...`
  - `FAIL:BG_PLAY:source_identity_mismatch:...`

- [ ] **Step 5: 构建 payload，确保签名改动无编译错误**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HandSourceIdentityResolverTests" -v normal
  dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore
  ```

  Expected:
  `PASS` 与 `Build succeeded.`

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/HandSourceIdentityResolver.cs BotCore.Tests/HandSourceIdentityResolverTests.cs
  git commit -m "在手牌动作执行前增加最终身份校验"
  ```

---

### Task 6: 收紧打出后手牌目标解析，禁用 slot-only fallback

**Files:**
- Modify: `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- Modify: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`

- [ ] **Step 1: 先写 strict hand target 的失败测试**

  在 `BotCore.Tests/PlayRuntimeTargetResolverTests.cs` 增加：

  ```csharp
  [Fact]
  public void Resolve_WhenOnlySlotMatchesForHandTarget_DoesNotResolve()
  {
      var resolution = PlayRuntimeTargetResolver.Resolve(
          new PlayRuntimeTargetHint
          {
              OriginalTargetEntityId = 91,
              CardId = "CATA_499",
              ZonePosition = 2
          },
          new[]
          {
              new PlayRuntimeTargetCandidate
              {
                  EntityId = 201,
                  Zone = "HAND",
                  ZonePosition = 2,
                  CardId = "OTHER_001"
              }
          },
          explicitHandTarget: true,
          rawChoiceType: "GENERAL");

      Assert.Equal(PlayRuntimeTargetMode.HandTarget, resolution.Mode);
      Assert.False(resolution.HasResolvedEntity);
      Assert.Equal("hand_target_detected_but_no_match", resolution.FailureReason);
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v normal
  ```

  Expected:
  `FAIL`，因为当前 resolver 仍允许 hand target 走 `slot` fallback。

- [ ] **Step 3: 改写 `PlayRuntimeTargetResolver.Resolve(...)` 的 hand target 分支**

  在 `HearthstonePayload/PlayRuntimeTargetResolver.cs` 中：

  1. 保留 `entity_direct` 与 `card_slot`。
  2. 仅当没有 `ZonePosition` 时，允许单独 `card` 命中且必须唯一。
  3. 删除手牌目标里的 `slot` fallback。

  把这一段：

  ```csharp
  if (hint.ZonePosition > 0)
  {
      var bySlot = candidates.FirstOrDefault(candidate => candidate.ZonePosition == hint.ZonePosition);
      if (bySlot != null)
      {
          matchReason = "slot";
          return bySlot;
      }
  }
  ```

  改成直接失败，不再返回 `slot`。

- [ ] **Step 4: 再跑一次 resolver 测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PlayRuntimeTargetResolverTests" -v normal
  ```

  Expected:
  `PASS`，新增 strict 测试通过，旧测试仍保持通过。

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add HearthstonePayload/PlayRuntimeTargetResolver.cs BotCore.Tests/PlayRuntimeTargetResolverTests.cs
  git commit -m "收紧手牌目标解析并禁用位置兜底"
  ```

---

### Task 7: 让 `BotService` 把 `source_identity_mismatch` 当成可恢复失败

**Files:**
- Modify: `BotMain/BotService.cs`
- Create: `BotCore.Tests/BotServiceHandSourceRecoveryTests.cs`

- [ ] **Step 1: 先写纯逻辑测试，避免直接在主循环里拍脑袋改**

  在 `BotCore.Tests/BotServiceHandSourceRecoveryTests.cs` 新建：

  ```csharp
  using BotMain;
  using Xunit;

  namespace BotCore.Tests
  {
      public class BotServiceHandSourceRecoveryTests
      {
          [Theory]
          [InlineData("PLAY|71|0|0|GAME_005|5", "FAIL:PLAY:source_identity_mismatch:71:slot_changed")]
          [InlineData("TRADE|71|GAME_005|5", "FAIL:TRADE:source_identity_mismatch:71:card_changed")]
          [InlineData("BG_PLAY|71|0|1|GAME_005|5", "FAIL:BG_PLAY:source_identity_mismatch:71:entity_left_hand")]
          public void IsRecoverableHandSourceFailure_ReturnsTrue(string action, string result)
          {
              Assert.True(BotService.IsRecoverableHandSourceFailureForTests(action, result));
          }

          [Fact]
          public void IsRecoverableHandSourceFailure_ReturnsFalse_ForUnrelatedFailure()
          {
              Assert.False(BotService.IsRecoverableHandSourceFailureForTests(
                  "PLAY|71|0|0|GAME_005|5",
                  "FAIL:PLAY:target_pos:123"));
          }
      }
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceHandSourceRecoveryTests" -v normal
  ```

  Expected:
  `FAIL`，因为 `IsRecoverableHandSourceFailureForTests(...)` 还不存在。

- [ ] **Step 3: 提取并接线 recoverable failure 判断**

  在 `BotMain/BotService.cs` 新增两个方法，放在 `TryGetActionSourceEntityId(...)` 附近：

  ```csharp
  internal static bool IsRecoverableHandSourceFailure(string action, string result)
  {
      if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(result))
          return false;

      if (!result.Contains("source_identity_mismatch", StringComparison.OrdinalIgnoreCase))
          return false;

      return action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
          || action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase)
          || action.StartsWith("BG_PLAY|", StringComparison.OrdinalIgnoreCase);
  }

  internal static bool IsRecoverableHandSourceFailureForTests(string action, string result)
      => IsRecoverableHandSourceFailure(action, result);
  ```

  然后在动作发送主循环中，`if (IsActionFailure(result))` 里面最前面插入：

  ```csharp
  if (IsRecoverableHandSourceFailure(action, result))
  {
      RefreshHsBoxActionMinimumUpdatedAtNow();
      requestResimulation = true;
      resimulationReason = "hand_source_identity_mismatch";
      resimulationRequestedThisAction = true;
      resimulationReasonThisAction = resimulationReason;
      actionOutcome = result;
      if (choiceWatchArmed)
          ClearChoiceStateWatch("hand_source_identity_mismatch");
      break;
  }
  ```

- [ ] **Step 4: 跑测试并构建 `BotMain`**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceHandSourceRecoveryTests" -v normal
  dotnet build BotMain/BotMain.csproj --no-restore
  ```

  Expected:
  `PASS` 与 `Build succeeded.`

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs BotCore.Tests/BotServiceHandSourceRecoveryTests.cs
  git commit -m "将手牌身份不匹配接入可恢复重试流程"
  ```

---

### Task 8: 全量回归与手动验证

**Files:**
- Modify: `BotCore.Tests/HsBoxRecommendationProviderTests.cs`
- Modify: `BotCore.Tests/HandActionCommandMetadataTests.cs`
- Modify: `BotCore.Tests/HandSourceIdentityResolverTests.cs`
- Modify: `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`
- Modify: `BotCore.Tests/BotServiceHandSourceRecoveryTests.cs`

- [ ] **Step 1: 跑所有本次新增/修改的测试集合**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests|FullyQualifiedName~HandActionCommandMetadataTests|FullyQualifiedName~HandSourceIdentityResolverTests|FullyQualifiedName~PlayRuntimeTargetResolverTests|FullyQualifiedName~BotServiceHandSourceRecoveryTests" -v normal
  ```

  Expected:
  全部 `PASS`。

- [ ] **Step 2: 构建两个主项目**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet build BotMain/BotMain.csproj --no-restore
  dotnet build HearthstonePayload/HearthstonePayload.csproj --no-restore
  ```

  Expected:
  两个项目都 `Build succeeded.`

- [ ] **Step 3: 做手动日志回归**

  使用以下真实场景手动验证，并在日志中确认没有再出现危险 fallback：

  1. 手牌中两张同名牌，盒子明确推荐其中一张
  2. 抽牌动画刚结束立即执行出牌
  3. 发现牌/生成牌进入手牌后立即执行
  4. 战旗 `BG_PLAY`
  5. 打出后进入“从手牌中选择目标”

  重点检查日志中应出现：

  - `PLAY(source-identity) ... result=exact_match`
  - `FAIL:PLAY:source_identity_mismatch:...`
  - `PLAY(runtime-target) hand_target_detected_but_no_match`

  且不应再出现作为成功路径的：

  - `ordered_slot_fallback`
  - `hand_snapshot_slot_fallback`

- [ ] **Step 4: 提交最终整合结果**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/HandActionCommandMetadata.cs BotMain/HsBoxRecommendationProvider.cs BotMain/BotService.cs HearthstonePayload/HandSourceIdentityResolver.cs HearthstonePayload/ActionExecutor.cs HearthstonePayload/PlayRuntimeTargetResolver.cs BotCore.Tests/HandActionCommandMetadataTests.cs BotCore.Tests/HsBoxRecommendationProviderTests.cs BotCore.Tests/HandSourceIdentityResolverTests.cs BotCore.Tests/PlayRuntimeTargetResolverTests.cs BotCore.Tests/BotServiceHandSourceRecoveryTests.cs
  git commit -m "修复手牌源牌误判并统一严格身份校验"
  ```

---

## 执行提醒

- 任何一步如果发现必须继续依赖 `slot fallback` 才能让测试通过，先停下来，回到 spec 目标重新核对，不要偷偷保留旧兜底。
- 对 `ActionExecutor.cs` 的改动必须尽量抽 helper，避免在超大文件里直接堆新分支。
- `TryGetActionSourceEntityId(...)` 与其他旧解析逻辑要保持兼容，新增 metadata 不得破坏既有非手牌动作。
- 手动验证阶段比单测更关键，这次 bug 的根因就在“运行时实体身份漂移”，日志证据必须清楚。
