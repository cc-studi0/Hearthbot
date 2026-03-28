# 攻击目标修正与 confirm_short_timeout 兜底

## 问题

连续攻击时，前一次攻击击杀目标导致棋盘随从重排。后续攻击使用重排前的坐标点击空位，攻击未打出。但 ActionExecutor 返回 `OK:ATTACK:...:confirm_short_timeout`（非 FAIL），BotService 将其视为成功并标记 hsbox 推荐为已消费，导致该攻击指令被吞掉。

## 方案：两层防御

### A) Payload 端预防：点击前坐标修正（ActionExecutor.cs MouseAttack）

在 `MoveCursorConstructed` 完成后、最终 `LeftDown` 点击前，重新读取目标实体坐标。如果与当前鼠标位置偏移超过阈值（12px），做一次快速直线修正移动。

**修改位置：** MouseAttack 方法，行3598 之后、行3599 之前

**逻辑：**
```
1. 贝塞尔曲线移动到目标位置（已有）
2. 新增：重新读取目标实体屏幕坐标
3. 如果读取成功且偏移 > 12px，用 SmoothMove 快速修正到新坐标（3步，极短延迟）
4. 点击目标（已有）
```

对英雄目标同样适用（通过 `GetHeroScreenPos`）。

修正移动使用 `SmoothMove(newX, newY, 3, 0.005f)` — 3步 × 5ms = 15ms，几乎不影响攻击速度。

### B) BotService 端兜底：confirm_short_timeout 不消费推荐

**修改位置：** BotService.cs，`RememberConsumedHsBoxActionRecommendation` 调用处

**当前逻辑：**
- `IsActionFailure(result)` 为 false（因为前缀是 `OK:`）→ 标记推荐已消费

**改为：**
- 额外检查结果是否包含 `confirm_short_timeout`
- 如果包含，视为攻击未确认生效：
  1. 不调用 `RememberConsumedHsBoxActionRecommendation()`
  2. 设置 `requestResimulation = true`，触发重新模拟
  3. 调用 `RefreshHsBoxActionMinimumUpdatedAtNow()` 强制下次获取新推荐

## 安全边界

- 坐标修正仅在偏移 > 12px 时触发，正常情况不额外耗时
- 修正移动极短（~15ms），不影响攻击速度
- confirm_short_timeout 兜底确保即使修正也没打中，推荐不会被吞
- 非攻击动作（PLAY、HERO_POWER 等）不受影响

## 涉及文件

- `HearthstonePayload/ActionExecutor.cs` — MouseAttack 方法，点击前坐标修正
- `BotMain/BotService.cs` — confirm_short_timeout 处理逻辑
