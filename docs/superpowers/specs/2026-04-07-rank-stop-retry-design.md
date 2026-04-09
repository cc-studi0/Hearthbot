# 段位达标停止——重试轮询设计

## 问题

对局结束后立刻查询段位，由于炉石服务器延迟，可能拿到旧段位，导致已达标但未触发停止，Bot 继续排队下一局。

## 根因

1. `HandlePostGame` 中 `TryQueryCurrentRank(force:true)` + `CheckRankStopLimit(force:true)` 只查一次，服务器可能还没更新
2. 查询返回旧段位后直接走到 `AutoQueue()`，不再有二次检查机会

## 方案：重试轮询

### 触发条件

- `_maxRank > 0`（用户设置了目标段位）
- 仅在 `HandlePostGame`（对局结束后）的检查点生效

### 流程

```
对局结束 → wasInGame 分支内:
  HandleGameResult()
  CheckRunLimits()
  等待 500ms
  循环最多 3 次:
    TryQueryCurrentRank(force: true)
    如果 _lastQueriedStarLevel >= _maxRank → 停止，return
    否则等待 500ms 继续
  全部未达标 → 继续走 AutoQueue
```

### 改动范围

**仅改 `BotService.cs`：**

1. **新增方法** `CheckRankStopLimitWithRetry(PipeServer pipe, int retryCount = 3, int intervalMs = 500)`
   - 首次查询前等待 `intervalMs`（给服务器更新时间）
   - 循环 `retryCount` 次，每次 `TryQueryCurrentRank(force: true)` + 段位比对
   - 达标则 log + 触发事件 + `_running = false` + 返回 true
   - 未达标则 `SleepOrCancelled(intervalMs)` 后重试
   - 全部未达标返回 false

2. **修改 `HandlePostGame`**（行8831-8835区域）
   - 将 `TryQueryCurrentRank(pipe, force: true)` + `CheckRankStopLimit(pipe, force: true)` 替换为 `CheckRankStopLimitWithRetry(pipe)`
   - 当 `_maxRank <= 0` 时仍调用原有的单次 `TryQueryCurrentRank` 更新UI显示

3. **不改的部分**
   - 启动时（行1836-1839）的 `CheckRankStopLimit(force: true)` 保持不变
   - 主循环（行2115-2117）的轮询检查保持不变
   - `RankHelper.cs` 不变
   - `MainViewModel.cs` 不变

### 耗时影响

- 未设置目标段位：零影响
- 设置了目标段位但未达标：每局结束多等约 2 秒（500ms × 4次间隔）
- 已达标：首次查询命中则只多等 500ms，最慢 2 秒内必停

### 日志输出

```
[RankRetry] 对局结束，开始重试轮询段位 (目标: 钻石5, 最多3次)
[RankRetry] 第1次查询: 钻石6 2星，未达标，500ms后重试
[RankRetry] 第2次查询: 钻石5 0星，已达标，停止
```

或：

```
[RankRetry] 对局结束，开始重试轮询段位 (目标: 钻石5, 最多3次)
[RankRetry] 3次查询均未达标 (当前: 钻石6 3星)，继续排队
```
