# 出牌 CardId 验证 — 双层防误操作设计

日期：2026-04-07

## 问题

盒子推荐以 cardId + 位置号标识手牌。当手牌变动（抽牌、火把、硬币等效果）后，位置号过时，导致：

1. `ResolveOrderedEntityId` 的 cardId 匹配全部失败，走到**纯位置兜底**（`ordered_slot_fallback`），映射到错误的实体
2. `ActionExecutor.MousePlayCardByMouseFlow` 只接收 entityId，不验证拿起的牌是否正确，直接执行

最终表现：脚本打出了错误的牌。

## 方案：双层验证（C）

### 第一层：映射层 — 移除纯位置兜底

**文件**：`HsBoxRecommendationProvider.cs`

**改动**：`ResolveOrderedEntityId`（~5858行）删除 `ordered_slot_fallback` 分支。

当前逻辑：
1. 位置 + cardId 精确匹配 → 返回 entityId（`ordered_exact_slot_card`）
2. cardId 扫描手牌匹配 → 返回 entityId（`ordered_card_match`）
3. 纯位置兜底 → 返回 entityId（`ordered_slot_fallback`）← **删除**

改后：cardId 匹配不上 → 返回 0 → 上游走到 `play_source_not_found` → 推荐被拒绝 → 等盒子刷新。

**影响**：`ResolveOrderedEntityId` 同时服务手牌和场上随从。场上随从 cardId 错配概率极低，影响可控。

### 第二层：执行层 — 出牌时 cardId 验证

**文件**：`ActionExecutor.cs` + `HsBoxRecommendationProvider.cs`

#### 2a. PLAY 命令格式扩展

当前格式：`PLAY|{sourceEntityId}|{targetEntityId}|{position}`

新格式：`PLAY|{sourceEntityId}|{targetEntityId}|{position}|{expectedCardId}`

- 第5段（index 4）为预期的 cardId
- 向下兼容：第5段缺失或为空时跳过验证

#### 2b. 上游构造命令时附加 cardId

**`TryMapPlayAction`**（~5196行）：
- 当前：`command = $"PLAY|{source}|{target}|{position}"`
- 改为：`command = $"PLAY|{source}|{target}|{position}|{card.CardId}"`
- `card` 即 `step.GetPrimaryCard()`，已有 CardId

**`TryMapPlayActionFromBodyText`**（~6217行）：
- 通过位置号找到 entityId 后，反查该实体的 cardId
- 从 `board.Hand[oneBasedIndex - 1]` 取 cardId 附加到命令

**`TryMapPlayActionFromBodyText_Legacy`**（~6180行）：
- 同上处理

#### 2c. ActionExecutor 解析并验证

**PLAY case**（~1209行）：
- 解析第5段作为 `expectedCardId`
- 传入 `MousePlayCardByMouseFlow`

**`MousePlayCardByMouseFlow`**（~2213行）：
- 签名扩展：增加 `string expectedCardId = null` 参数
- 在位置稳定后、鼠标按下前，执行验证：
  ```
  if expectedCardId 非空:
      actualCardId = ResolveEntityCardId(gameState, entityId)
      if actualCardId != expectedCardId:
          返回 FAIL:PLAY:card_mismatch:{entityId}:{expectedCardId}:{actualCardId}
  ```
- 验证时机在鼠标操作前，失败无副作用

#### 2d. 失败后行为

`FAIL:PLAY:card_mismatch` 返回到 BotService 主循环，与其他 FAIL 一致：
- 当前动作链中断
- 下次循环重新获取推荐和棋盘状态
- 盒子如已刷新 → 获取新推荐重新映射
- 盒子未刷新 → `ShouldRetryWithoutAction` 等待

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `HsBoxRecommendationProvider.cs` | 删除 `ordered_slot_fallback`；`TryMapPlayAction` / `TryMapPlayActionFromBodyText` / `TryMapPlayActionFromBodyText_Legacy` 追加 cardId 到 PLAY 命令 |
| `ActionExecutor.cs` | PLAY case 解析第5段；`MousePlayCardByMouseFlow` 增加 cardId 验证 |

## 不改动的部分

- ATTACK、TRADE、OPTION 等其他命令不受影响
- 消费确认逻辑不变
- 重试/兜底机制不变
