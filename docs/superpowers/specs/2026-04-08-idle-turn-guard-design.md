# 空回合紧急刹车（Idle Turn Guard）设计文档

**日期**: 2026-04-08
**状态**: 已批准

## 需求

如果在游戏内，连续三个我方回合脚本没有执行任何有效操作（只发了 `END_TURN`），则：

1. 关闭游戏进程（不重启）
2. 停止脚本
3. 通过微信推送通知用户"脚本异常停止"

这是一个**紧急刹车**机制，触发后需要用户手动重新启动。

## 定义

- **有效操作**：除 `END_TURN` 以外的任何成功执行的游戏动作（`PLAY`、`ATTACK`、`HERO_POWER`、`USE_LOCATION`、`TRADE`、`OPTION` 等）
- **空回合**：我方回合内未执行任何有效操作，仅发送了 `END_TURN`（包括确实无牌可出的情况）
- **触发阈值**：连续 3 个空回合，硬编码不可配置

## 实现方案：主循环内联计数

### 数据结构

在 `BotService` 中新增：

```csharp
private int _consecutiveIdleTurns;      // 连续空回合计数
private bool _turnHadEffectiveAction;   // 当前回合是否有有效动作
```

### 事件

在 `BotService` 中新增事件：

```csharp
public event Action OnIdleGuardTriggered;
```

### 计数逻辑

| 时机 | 操作 |
|------|------|
| 进入我方回合（收到 `SEED:xxx`，开始动作规划前） | `_turnHadEffectiveAction = false` |
| 执行非 `END_TURN` 动作且结果不是失败 | `_turnHadEffectiveAction = true` |
| `END_TURN` 执行后 | 若 `_turnHadEffectiveAction == false` 则 `_consecutiveIdleTurns++`，否则 `_consecutiveIdleTurns = 0` |
| 对局开始（进入留牌阶段） | `_consecutiveIdleTurns = 0` |
| 对局结束 | `_consecutiveIdleTurns = 0` |
| 脚本启动 | `_consecutiveIdleTurns = 0` |

### 触发流程

当 `_consecutiveIdleTurns >= 3` 时，在 `END_TURN` 后的判断点执行：

1. `Log("[IdleGuard] 连续3回合无操作，触发紧急停止")`
2. `KillHearthstone()` — 杀掉炉石进程
3. `_running = false` — 停止主循环
4. 触发 `OnIdleGuardTriggered` 事件
5. 主循环自然退出

### 插入点

**天梯主循环**（BotService.cs ~3109 行）：在 `END_TURN` 后等待回合切换之前，插入空回合判断和刹车逻辑。

**竞技场主循环**：同理，在竞技场的 `END_TURN` 处理段落中插入相同逻辑。

## 推送通知

`MainViewModel` 监听 `OnIdleGuardTriggered` 事件：

```
标题: [{DeviceName}] 脚本异常停止
内容: 设备: {DeviceName}
      原因: 连续3回合无任何操作
      时间: {yyyy-MM-dd HH:mm:ss}
```

**推送条件**：`NotifyToken` 非空时推送，复用现有 `NotificationService` 和用户已配置的推送渠道（PushPlus / Server酱）。无需额外开关。

## 不涉及的内容

- 不新增 UI 控件或配置项
- 不修改 Watchdog 逻辑
- 不引入自动恢复机制
- 阈值 3 硬编码，不可配置
