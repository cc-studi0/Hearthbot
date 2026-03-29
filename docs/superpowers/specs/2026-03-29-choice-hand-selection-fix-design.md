# 修复：打牌后选择手牌失败导致无限卡住

## 问题

打出一张牌后触发"从手牌中选择一张"（如弃牌、复制手牌），手牌不弹出到中间，需要在手牌区域直接点击高亮的卡。

故障链条：
1. 手牌位置不精确 → 鼠标点击落空
2. 验证逻辑出现 false positive（手牌重排导致快照签名变化，误判为选择成功）
3. 返回 OK → 消费追踪器标记推荐为已消费
4. Choice 消费追踪器无释放机制 → 永久消费 → 无法获取新推荐
5. 主循环检测选择界面仍在 → `ShouldRetryWithoutAction=true` → `waitingForChoiceState=true` → 无限循环

## 修复方案

### 改动 1：Choice 消费追踪器增加释放阈值

**文件：** `BotMain/GameRecommendationProvider.cs` — `ChoiceRecommendationConsumptionTracker`

仿照 `ConstructedRecommendationConsumptionTracker` 的模式：

- 新增 `ReleaseThreshold = 2`
- 新增 `ShouldTreatAsConsumed()` 方法：
  - 比较当前推荐与已消费推荐是否相同（UpdatedAtMs + PayloadSignature）
  - 如果相同，递增 `repeatedRecommendationCount`
  - 达到阈值后清空消费状态，返回 false（释放）
  - 如果不同，重置计数，返回 false
- 保留现有 `TryRememberConsumed()` 和 `Reset()` 不变

**文件：** `BotMain/BotService.cs`

- 新增字段 `_choiceRepeatedRecommendationCount`
- 获取 Choice 推荐时调用 `ShouldTreatAsConsumed` 替代直接传入已消费参数
- 释放时日志输出 `[Choice] consumption_released_due_to_repetition`

### 改动 2：强化 MouseClickChoice 验证

**文件：** `HearthstonePayload/ActionExecutor.cs` — `MouseClickChoice` 方法

在现有验证基础上增加二次确认：

- 当验证结果为 `changed@mouseN`（快照变化但未关闭）时，额外检查 `ChosenEntityIds` 是否包含目标 entityId
- 如果不包含 → 不认为选择成功，继续下一次尝试
- 仅当能获取到 `ChosenEntityIds` 时做此检查，获取失败则沿用现有逻辑
- `closed` 状态不做额外检查（选择界面关闭 = 确认成功）

新增辅助方法 `IsEntityInChosenList(int entityId)`：从当前 ChoiceSnapshot 读取 ChosenEntityIds 检查目标是否在其中。

## 不受影响的场景

- 发现/抉择等弹出式选择：坐标来自 ChoiceCardMgr，位置精确
- 链式选择：释放计数在获取到新推荐后重置
- 正常成功选择：验证通过 + 界面关闭 → 不触发释放逻辑

## 涉及文件

| 文件 | 改动 |
|------|------|
| `BotMain/GameRecommendationProvider.cs` | ChoiceRecommendationConsumptionTracker 增加释放阈值 |
| `BotMain/BotService.cs` | 新增字段，调用 ShouldTreatAsConsumed |
| `HearthstonePayload/ActionExecutor.cs` | MouseClickChoice 验证强化 + IsEntityInChosenList |
