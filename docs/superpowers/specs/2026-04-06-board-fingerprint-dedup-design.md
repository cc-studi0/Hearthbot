# 构筑模式盒子跟随去重机制重构：基于局面指纹

## 问题

当前构筑模式跟随盒子推荐的去重逻辑完全依赖盒子侧元数据（`UpdatedAtMs` + `PayloadSignature`）。
脚本执行动作后需要盒子刷新时间戳才能拿到下一个推荐，但盒子刷新速度不可控，导致三类卡死：

1. **盒子未及时刷新**：脚本执行了动作，局面已变，但盒子时间戳没更新 → `consumed_same_or_older_payload` → 无限 retry
2. **操作未实际生效**：`SendActionCommand` 返回非 FAIL 但游戏端动作未生效 → 局面没变 + 消费记录已标记 → 盒子推荐不变 → 卡死
3. **发现/选择后卡死**：`RefreshHsBoxActionMinimumUpdatedAtNow()` 把最低时间戳抬到当前时间，盒子的新推荐时间戳可能早于此值 → `below_minimum` → 即使盒子有新推荐也被拦截

## 方案

用**局面指纹（Board Fingerprint）**替代盒子时间戳作为去重的主要依据。

核心原则：**局面变了 → 任何推荐都是新的；只有同一局面下的同一动作才是重复。**

## 涉及文件

| 文件 | 改动 |
|------|------|
| `BotMain/BotService.cs` | 生成局面指纹、传入请求、调整消费记录、移除 `minimumUpdatedAtMs` 机制 |
| `BotMain/HsBoxRecommendationProvider.cs` | 改造 `RecommendActions` 新鲜度判断逻辑 |
| `BotMain/GameRecommendationProvider.cs` | `ActionRecommendationRequest` 加字段、`ConstructedRecommendationConsumptionTracker` 加指纹相关方法 |

## 详细设计

### 1. 局面指纹生成

在 `BotService` 中新增 `BuildBoardFingerprint(Board planningBoard)` 方法。

**指纹内容**（用 `|` 拼接后取 SHA256 前16字符）：
- `TurnCount`（回合数）
- `ManaAvailable`（当前可用法力）
- 手牌 Entity ID 列表（排序后拼接）
- 友方场面 Entity ID 列表（排序后拼接）
- 敌方场面 Entity ID 列表（排序后拼接）
- 友方英雄当前血量
- 敌方英雄当前血量

**不包含**：墓地、牌库数量、疲劳等（这些变化不影响出牌决策的去重判断）。

```csharp
private static string BuildBoardFingerprint(Board board)
{
    if (board == null) return string.Empty;
    var sb = new StringBuilder(256);
    sb.Append(board.TurnCount).Append('|');
    sb.Append(board.ManaAvailable).Append('|');
    // 手牌
    if (board.Hand != null)
        foreach (var c in board.Hand.Where(c => c != null).OrderBy(c => c.Id))
            sb.Append(c.Id).Append(',');
    sb.Append('|');
    // 友方场面
    if (board.MinionFriend != null)
        foreach (var m in board.MinionFriend.Where(m => m != null).OrderBy(m => m.Id))
            sb.Append(m.Id).Append(':').Append(m.CurrentHealth).Append(',');
    sb.Append('|');
    // 敌方场面
    if (board.MinionEnemy != null)
        foreach (var m in board.MinionEnemy.Where(m => m != null).OrderBy(m => m.Id))
            sb.Append(m.Id).Append(':').Append(m.CurrentHealth).Append(',');
    sb.Append('|');
    sb.Append(board.HeroFriend?.CurrentHealth ?? 0).Append('|');
    sb.Append(board.HeroEnemy?.CurrentHealth ?? 0);

    using (var sha = SHA256.Create())
    {
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
```

### 2. 消费记录扩展

在 `BotService` 中新增字段：

```csharp
private string _lastConsumedBoardFingerprint = string.Empty;
```

`RememberConsumedHsBoxActionRecommendation` 增加 `boardFingerprint` 参数，一并记录。

`ResetHsBoxActionRecommendationTracking` 同时清除 `_lastConsumedBoardFingerprint`。

### 3. ActionRecommendationRequest 扩展

新增属性：

```csharp
public string BoardFingerprint { get; }
public string LastConsumedBoardFingerprint { get; }
```

在 `BotService.MainLoop` 构造 `ActionRecommendationRequest` 时传入当前局面指纹和上次消费时的局面指纹。

### 4. 新鲜度判断改造

替换 `HsBoxGameRecommendationProvider.RecommendActions` 中的新鲜度判断逻辑。

**新判断流程**（替代 `IsActionPayloadFreshEnough` 的调用位置）：

```
步骤 1：局面指纹比对
  if (request.BoardFingerprint != request.LastConsumedBoardFingerprint
      && 两者都非空)
    → 局面已变，放行 ✅（reason = "board_changed"）

步骤 2：盒子元数据比对（降级为辅助判断）
  if (state.UpdatedAtMs > lastConsumedUpdatedAtMs)
    → 放行 ✅（reason = "updated_after_last_consumed"）
  if (state.UpdatedAtMs == lastConsumedUpdatedAtMs
      && PayloadSignature 不同)
    → 放行 ✅（reason = "same_updated_at_new_signature"）

步骤 3：同 payload 下的动作比对
  if (首条动作 != lastConsumedActionCommand)
    → 放行 ✅（reason = "same_payload_new_action"）

步骤 4：真正的重复
  → 拦截 ❌（reason = "duplicate_same_board_same_action"）
```

移除 `minimumUpdatedAtMs` 相关的所有逻辑（`GetHsBoxActionMinimumUpdatedAtMs`、`RefreshHsBoxActionMinimumUpdatedAtNow`、`_hsBoxActionMinimumUpdatedAtMs` 字段、`below_minimum` 判断）。这个机制是为弥补时间戳去重的缺陷而引入的，局面指纹去重后不再需要。

### 5. 同局面重复拦截的兜底

当步骤4拦截时（真正的重复），保留现有的 `samePayloadRepeatedActionCount` 计数逻辑。
但增加一个基于局面指纹的计数器：

```csharp
private int _sameBoardStalledCount;
private string _sameBoardStalledFingerprint = string.Empty;
```

在 `RecommendActions` 返回 `shouldRetryWithoutAction` 时，主循环中：
- 如果 `boardFingerprint == _sameBoardStalledFingerprint`：`_sameBoardStalledCount++`
- 否则：重置计数
- 当 `_sameBoardStalledCount >= 5`：`ResetHsBoxActionRecommendationTracking()` 并强制接受当前推荐

这解决场景2（操作没生效但被标记为已消费）。

### 6. 发现/选择后的处理调整

**当前代码**（[BotService.cs:2892](Hearthbot/BotMain/BotService.cs#L2892)）：
```csharp
RefreshHsBoxActionMinimumUpdatedAtNow();  // 抬高时间戳门槛 → 导致卡死
```

**改为**：
```csharp
ResetHsBoxActionRecommendationTracking();  // 清除所有消费记录
```

理由：发现完成后局面必然已变（手牌变了），新的局面指纹自然与上次不同，无需人为设卡。
清除消费记录确保即使指纹比对出现边界情况，也不会被旧记录拦截。

同样修改所有其他调用 `RefreshHsBoxActionMinimumUpdatedAtNow` 的位置（resimulation 请求、play fail 后的 choice 处理等），统一改为 `ResetHsBoxActionRecommendationTracking`。

### 7. resimulation 路径补漏

当前 resimulation 路径（[BotService.cs:2948-2957](Hearthbot/BotMain/BotService.cs#L2948-L2957)）没有重置消费记录。
新增 `ResetHsBoxActionRecommendationTracking()` 调用：

```csharp
if (requestResimulation)
{
    resimulationCount++;
    if (resimulationCount <= 5)
    {
        ResetHsBoxActionRecommendationTracking();  // 新增
        Log($"[AI] resimulation requested ...");
        ...
    }
}
```

### 8. 移除 minimumUpdatedAtMs 机制

以下内容全部移除：
- `_hsBoxActionMinimumUpdatedAtMs` 字段
- `GetHsBoxActionMinimumUpdatedAtMs()` 方法
- `RefreshHsBoxActionMinimumUpdatedAtNow()` 方法
- `ActionRecommendationRequest.MinimumUpdatedAtMs` 属性
- `TryEvaluateActionPayloadFreshness` 中的 `below_minimum` 分支
- `staleFreshSourceRetryCount` 及相关的3次重试清除逻辑

这些全部被局面指纹机制取代。

## 诊断日志

保留现有的 `[diag:]` 输出格式，新增字段：
- `boardFp=<当前指纹>`
- `lastConsumedBoardFp=<上次消费指纹>`
- `boardChanged=true/false`

便于排查问题时快速定位是否为指纹相关原因。

## 不改动的部分

- **Choice/Discover 推荐的去重**：Choice 有独立的消费跟踪（`_lastConsumedHsBoxChoiceUpdatedAtMs`），不受本次改动影响
- **Battlegrounds 模式**：战旗有独立的推荐消费逻辑，不受影响
- **Arena 模式**：Arena 的 action 路径同样使用 `_lastConsumedHsBoxAction*` 字段，需要同步应用局面指纹（改动方式与构筑模式一致）
- **连续攻击快速通道**：保持现有逻辑不变，只是 `RememberConsumed` 时额外记录指纹
- **ReleaseThreshold 机制**：保留作为兜底，但不再是主要去重路径

## 风险

| 风险 | 缓解 |
|------|------|
| Board 属性名与实际 SmartBot API 不一致 | 实现前先确认 Board 类的实际属性名 |
| 指纹计算引入额外延迟 | SHA256 对 ~200 字节输入 < 1ms，可忽略 |
| 某些动作不改变指纹涵盖的字段（如 secret） | 盒子时间戳作为备选判断仍在，兜底计数器 5 次后强制放行 |
| Arena 模式遗漏 | Arena 路径与构筑类似，需同步改动 |
