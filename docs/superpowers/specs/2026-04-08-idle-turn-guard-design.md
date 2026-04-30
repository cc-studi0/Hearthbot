# 空回合紧急刹车（Idle Turn Guard）设计文档

**日期**: 2026-04-08
**状态**: 已批准
**修订**: 2026-04-30 — 重新定义"空回合"语义，触发点从 END_TURN 后挪至 SEED→NOT_OUR_TURN 切换点

## 需求

如果在游戏内，连续三个我方回合脚本完全卡住、一次命令都没发出去（连 `END_TURN` 都没发，靠游戏读秒强制结束），则：

1. 关闭游戏进程（不重启）
2. 停止脚本
3. 通过微信推送通知用户"脚本异常停止"

这是一个**紧急刹车**机制，触发后需要用户手动重新启动。

## 定义（2026-04-30 修订）

- **操作**：本回合主循环成功调用过任何一次 `SendActionCommand`，无论返回成功或失败、状态是否变化、命令是 `PLAY` / `ATTACK` / `HERO_POWER` / `USE_LOCATION` / `TRADE` / `OPTION` 还是 `END_TURN`。**`END_TURN` 也算操作**。
- **空回合**：我方回合从开始到结束（被 `NOT_OUR_TURN` 接管那一刻），脚本主循环**一次 `SendActionCommand` 都没成功调用过**——即脚本完全卡死、靠游戏读秒强制切到对方回合。
- **触发阈值**：连续 3 个空回合，硬编码不可配置。

> 修订动机：旧定义把"只发 END_TURN、没打牌"也算空回合，会误伤合法的"无牌可出"局面，且与"成功但状态没变"的快照判定耦合过重；新定义只关心脚本是否完全卡死。

## 实现方案：主循环内联计数

### 数据结构

```csharp
private int _consecutiveIdleTurns;   // 连续空回合计数
private bool _turnHadAnyAction;      // 当前回合是否调用过 SendActionCommand
private bool _inOurTurn;             // 我方回合中标志（用于检测 SEED→NOT_OUR_TURN 切换）
```

### 事件

```csharp
public event Action OnIdleGuardTriggered;
```

### 计数逻辑

| 时机 | 操作 |
|------|------|
| 收到 `SEED:xxx` 且 `_inOurTurn == false`（即从对方回合切回我方） | `_inOurTurn = true; _turnHadAnyAction = false` |
| 主循环走到任意 `SendActionCommand(...)`（含 END_TURN、含失败响应） | `_turnHadAnyAction = true` |
| 收到 `NOT_OUR_TURN` 且 `_inOurTurn == true`（我方回合刚结束） | `_inOurTurn = false`；若 `!_turnHadAnyAction` 则 `_consecutiveIdleTurns++`，否则 `_consecutiveIdleTurns = 0` |
| 对局开始（进入留牌阶段）/ 对局结束 / 脚本启动 | `_consecutiveIdleTurns = 0; _inOurTurn = false; _turnHadAnyAction = false` |

### 触发流程

当 `_consecutiveIdleTurns >= 3` 时，在 NOT_OUR_TURN 入口的判断点执行：

1. `Log("[IdleGuard] 连续3回合无任何操作，触发紧急停止")`
2. `KillHearthstone()` — 杀掉炉石进程
3. `_running = false` — 停止主循环
4. 触发 `OnIdleGuardTriggered` 事件
5. 主循环自然退出

### 插入点

**天梯主循环**：
- SEED: 入口（BotService.cs ~2384 行）：进入我方回合首帧重置 `_turnHadAnyAction`。
- 任何 `SendActionCommand` 紧随其后置 `_turnHadAnyAction = true`（含动作主送出、`空推荐 END_TURN`、`force END_TURN` 三处）。
- NOT_OUR_TURN 入口（BotService.cs ~2275 行）：检测 `_inOurTurn` 转 false 那一帧做空回合计数与紧急停止。

**竞技场主循环**：同理，对应 SEED 入口、各 `SendActionCommand` 调用点、NOT_OUR_TURN 入口处理。

## 与"操作返回成功但状态未变化"的关系

旧设计把 `ResolveActionEffectConfirmation` 的 `MarkTurnHadEffectiveAction` 直接喂给空回合计数。新设计**完全解耦**：
- `MarkTurnHadEffectiveAction` 仅用于内部下游决策（`SkipNextTurnStartReadyWait`、`ConsumeRecommendation` 等）。
- 空回合计数只看 `_turnHadAnyAction`，与状态变化无关。
- 相关诊断日志保留（前缀从 `[IdleGuard]` 改为 `[ActionEffect]`）。

## 兜底：完全卡死无法走到主循环的情况

主循环本身挂死（连 `GET_SEED` 都不发）走 `HearthstoneWatchdog.GameTimeoutSeconds`（默认 5 分钟无 `_lastEffectiveActionUtc` 刷新）触发恢复，与本机制互补。

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
