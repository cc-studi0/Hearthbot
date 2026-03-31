# 匹配等待阶段弹窗检测

## 问题

点击"开始匹配"后，游戏可能因服务器错误弹出"开始游戏时出现错误，请稍后再试"等弹窗（带"确定"按钮）。弹窗在以下三个阶段均可能出现：

1. 点击匹配按钮后立即弹出
2. 进入匹配队列后弹出
3. 匹配到对手后弹出

当前脚本在 `AutoQueue()` 的加载等待阶段（L8274）已有弹窗检测，但在匹配等待阶段（`IS_FINDING == YES` 循环，L8226-8250）缺失弹窗检测。

**后果**：弹窗覆盖游戏界面 → 脚本无法操作 → 对局内全程挂机 → 无限循环被举报。

## 方案

在 `AutoQueue()` 的 `IS_FINDING == YES` 分支中，`SleepOrCancelled(2000)` 之前插入弹窗检测逻辑。

### 修改位置

`BotMain/BotService.cs`，`AutoQueue()` 方法，`IS_FINDING == YES` 分支内（约 L8246-8250 之间）。

### 逻辑

```
匹配等待循环（IS_FINDING == YES）:
  ├─ 检查匹配超时 → 超时则重启（已有，不动）
  ├─ [新增] TryGetBlockingDialog 检测弹窗
  │   ├─ 有弹窗 + 按钮在白名单 → TryDismissBlockingDialog → ResetMatchmakingTracking → return
  │   ├─ 有弹窗 + 按钮不在白名单 → 记日志，继续等待超时兜底
  │   └─ 无弹窗 → 继续正常等待
  ├─ 打印等待日志（已有，不动）
  └─ Sleep 2秒（已有，不动）
```

### 复用

- `TryGetBlockingDialog()` — 现有弹窗检测方法
- `TryDismissBlockingDialog()` — 现有弹窗关闭方法
- `BotProtocol.IsSafeBlockingDialogButtonLabel()` — 现有按钮白名单（确认/确定/关闭/返回/重连/重试）
- `ResetMatchmakingTracking()` — 现有匹配状态重置方法
- scope 标记：`"AutoQueueFinding"`

### 不改动

- 加载等待阶段弹窗检测（L8274，已有）
- MainLoop 主循环
- Payload 侧弹窗检测逻辑
- 按钮安全白名单
