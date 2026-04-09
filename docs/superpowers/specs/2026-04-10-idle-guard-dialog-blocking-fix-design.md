# IdleGuard 弹窗遮挡误判修复设计

## 问题描述

当游戏内出现弹窗（如 AlertPopup、ReconnectHelperDialog 等）遮挡时，脚本向 payload 发送操作命令（PLAY、ATTACK 等），payload 返回成功，但操作实际被弹窗吞掉未生效。IdleGuard 因为检测到"有操作"而不触发关闭，导致脚本无限挂机。

**根因：** payload 端对弹窗无感知，操作被遮挡时仍返回成功；BotService 端仅依据返回值判断操作有效性，缺乏实际状态验证。

## 设计方案：三层防御

### 第一层：Payload 端操作前弹窗检查

**文件：** `HearthstonePayload/ActionExecutor.cs`  
**位置：** `Execute()` 方法（line 1193），解析操作类型之后、执行鼠标操作之前

**逻辑：**
- 对 PLAY、ATTACK、HERO_POWER、USE_LOCATION、OPTION、TRADE 操作，在执行前调用 `SceneNavigator.GetBlockingDialog()`
- 如果返回非 `NO_DIALOG`，直接返回 `FAIL:<action>:DIALOG_BLOCKING:<dialogType>`，不执行任何鼠标操作
- 不检查的操作：END_TURN、CANCEL、CONCEDE、HUMAN_TURN_START（控制命令，不涉及棋盘交互）

**返回格式示例：** `FAIL:PLAY:DIALOG_BLOCKING:AlertPopup`  
符合现有 `FAIL` 前缀约定，`IsActionFailure()` 自动识别。

### 第二层：BotService 端操作前弹窗检测与关闭

**文件：** `BotMain/BotService.cs`  
**位置：** 标准天梯循环（~line 2838）和竞技场循环中，`SendActionCommand()` 之前

**逻辑：**
- 在发送非 END_TURN 操作命令之前，调用已有的 `TryGetBlockingDialog()`
- 如果检测到弹窗且按钮标签安全（`IsSafeBlockingDialogButtonLabel`）：
  - 调用 `TryDismissBlockingDialog()` 关闭弹窗
  - Log `[IdleGuard] 操作前检测到弹窗 {dialogType}，已关闭`
  - 短暂等待（~500ms）让 UI 恢复
  - 继续执行原操作
- 如果检测到弹窗但按钮不安全：
  - Log 警告，跳过本次操作（break），让主循环重新评估

### 第三层：BotService 端操作后精确状态验证

**文件：** `BotMain/BotService.cs`  
**位置：** 操作成功返回后（~line 2854），替换当前简单的 `_turnHadEffectiveAction = true`

**逻辑：**
- 操作前记录快照（手牌数、法力值等关键指标）
- 操作返回成功后，根据操作类型做精确验证：

| 操作类型 | 验证方式 |
|----------|----------|
| PLAY（打牌） | 手牌数减少 或 法力值消耗 |
| ATTACK（攻击） | 攻击者已攻击次数增加 或 目标血量减少 |
| HERO_POWER（英雄技能） | 英雄技能 exhausted 标记 或 法力值消耗 |
| USE_LOCATION（地标） | 地标 cooldown 状态变化 |
| TRADE（交易） | 手牌变化 |

- 验证通过 → `_turnHadEffectiveAction = true`
- 验证失败 → 不标记，Log `[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作`
- 状态读取失败时保守标记为有效，避免误触 IdleGuard

## 错误处理

### DIALOG_BLOCKING 专用分支

在 `IsActionFailure` 处理分支（~line 2858）中：
- 如果 result 包含 `DIALOG_BLOCKING`：
  - 跳过 CANCEL 发送（操作根本未开始执行）
  - 尝试调用 `TryDismissBlockingDialog()` 关闭弹窗
  - Log `[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭`
  - break 回主循环重新读取棋盘

### 状态验证时序

- 状态对比在 payload 返回结果之后进行，此时动画通常已完成
- 如果对比不确定，保守标记为有效

### 竞技场同步

竞技场循环（~line 3934）的 IdleGuard 逻辑与标准天梯完全对称，所有改动同步到两个循环。

## 改动范围

| 层级 | 文件 | 改动 |
|------|------|------|
| Payload 前置检查 | `ActionExecutor.cs` | Execute() 入口加弹窗检测 |
| BotService 前置检查 | `BotService.cs` | 操作前检测并关闭弹窗 |
| BotService 后置验证 | `BotService.cs` | 操作后状态对比验证 |
| BotService 错误处理 | `BotService.cs` | DIALOG_BLOCKING 专用处理分支 |
| 竞技场同步 | `BotService.cs` | 竞技场循环同步所有改动 |

## 测试策略

### 单元测试
- `IsActionFailure("FAIL:PLAY:DIALOG_BLOCKING:AlertPopup")` → true
- 状态验证：模拟操作前后快照对比，验证各操作类型判定
- 边界：状态读取失败时保守标记为有效

### 集成验证（手动）
- 游戏中触发弹窗，观察 payload 返回 FAIL、BotService 不标记有效操作、弹窗被关闭
- 连续 3 回合弹窗遮挡后 IdleGuard 正确触发
- 正常对局无弹窗时 IdleGuard 不误触发

### 日志
所有新增逻辑带 `[IdleGuard]` 前缀：
- `[IdleGuard] 操作前检测到弹窗 {type}，已关闭`
- `[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭`
- `[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作`
