# HSBox 推荐 Key-Based 去重设计

## 背景

当前 HearthBot 的 HSBox 推荐去重依赖 `UpdatedAtMs` + `PayloadSignature` + `FreshnessSlackMs` 时间戳机制。存在一个问题：HSBox 可能在对手回合就算好推荐，等己方回合开始时推荐已超过 FreshnessSlackMs 阈值，被误判为旧数据丢弃。

参考 SmartBot 的 `_knownKeys` HashSet 方式，改为基于内容的 key 去重，不依赖时间戳。同时改进 SmartBot 的缺陷：操作确认成功后才标记为已消费，避免执行失败的操作被永久跳过。

## 目标

- 用 `turnNum:optionId:choiceId:firstCardId` 四元组 key 去重
- 整局对局记住所有已成功执行的推荐
- 操作确认成功后才标记已消费（SmartBot 改进）
- 移除 FreshnessSlackMs 时间戳去重
- 保留 ConsumptionTracker 连续重复计数保护

## 设计

### 新建 `BotMain/RecommendationDeduplicator.cs`

```csharp
public class RecommendationDeduplicator
{
    private readonly HashSet<string> _knownKeys = new();
    private readonly object _lock = new();

    /// <summary>
    /// 检查推荐是否已被成功执行过。
    /// </summary>
    public bool IsKnown(string key)
    {
        lock (_lock) { return _knownKeys.Contains(key); }
    }

    /// <summary>
    /// 操作确认成功后调用，标记该推荐为已消费。
    /// </summary>
    public void MarkConsumed(string key)
    {
        lock (_lock) { _knownKeys.Add(key); }
    }

    /// <summary>
    /// 对局开始/结束时清空所有已知 key。
    /// </summary>
    public void Clear()
    {
        lock (_lock) { _knownKeys.Clear(); }
    }

    /// <summary>
    /// 从推荐数据中构建去重 key。
    /// </summary>
    public static string BuildKey(int turnNum, int optionId, int choiceId, string firstCardId)
    {
        return $"{turnNum}:{optionId}:{choiceId}:{firstCardId ?? ""}";
    }
}
```

### 修改 BotService.cs

#### 新增字段

```csharp
private readonly RecommendationDeduplicator _actionDedup = new();
private readonly RecommendationDeduplicator _choiceDedup = new();
```

#### 消费推荐时的去重流程

```
1. 获取推荐 → 从 envelope 提取 turnNum, optionId, choiceId, firstCardId
2. BuildKey() → 如 "5:0:-1:CS2_162"
3. _actionDedup.IsKnown(key)? → 跳过，等待新推荐
4. 执行操作 → 通过 Pipe 发送 ACTION 命令
5. Pipe 返回成功响应 → _actionDedup.MarkConsumed(key)
6. Pipe 返回失败/超时 → 不标记，下次可重试
```

#### 对局生命周期

- **对局开始时：** `_actionDedup.Clear()` + `_choiceDedup.Clear()`
- **对局结束时：** `_actionDedup.Clear()` + `_choiceDedup.Clear()`

#### 移除的字段和逻辑

- 移除 `FreshnessSlackMs` 和 `ChoiceFreshnessSlackMs` 相关常量
- 移除推荐提供者中的 `MinimumUpdatedAtMs` 新鲜度过滤
- 保留 `_lastConsumedHsBoxActionUpdatedAtMs` / `PayloadSignature` 仅用于 ConsumptionTracker 的连续重复检测（防止 HSBox 卡住时无限重试）

### Key 字段来源

| 字段 | 来源 |
|------|------|
| `turnNum` | `HsBoxRecommendationEnvelope` 或 Board 的回合数 |
| `optionId` | `HsBoxRecommendationEnvelope` 中的操作序号 |
| `choiceId` | 发现/选择场景的 choiceId，普通出牌为 -1 |
| `firstCardId` | `HsBoxActionStep[0].CardToken.CardId` |

### 与 SmartBot 的差异

| 维度 | SmartBot | HearthBot（改进后） |
|------|----------|---------------------|
| 标记时机 | 获取推荐时立即标记 | **操作确认成功后**才标记 |
| 启动基线 | 扫描内存建立已知 key | 不需要（CDP 桥是事件驱动，不存在"已存在的旧推荐"问题） |
| 新鲜度检查 | 无 | 移除 |
| 连续重复保护 | 无 | 保留 ConsumptionTracker |
