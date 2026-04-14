# 构筑模式移除连续攻击机制设计

**日期**：2026-04-14  
**作者**：Codex 与用户协作  
**状态**：已确认实现方向

## 目标

移除构筑模式中的连续攻击快捷机制，让构筑攻击统一回到普通动作流程，不再因为“上一批动作全是攻击”而跳过棋盘恢复、上下文刷新、拟人化前奏或进入特殊的快速等待分支。

## 范围

本次只处理 `BotMain/BotService.cs` 中围绕构筑攻击链的执行优化，不改：

- 构筑动作级 `WaitForConstructedActionReady(...)` 机制本身
- 攻击失败恢复和 `HsBox` 跟随策略
- 战旗模式逻辑

## 要移除的行为

1. 不再向攻击指令追加 `|CHAIN` 标记。
2. 不再使用 `lastRecommendationWasAttackOnly` 之类的跨轮攻击链状态。
3. 不再因为“上一轮推荐全是攻击”而跳过：
   - `board recovery`
   - `deck state / friendly entity context` 刷新
   - `TryRunHumanizedTurnPrelude(...)`
   - follow-box 路径下的常规短暂停顿
4. 不再使用攻击后专门的 `ready_chain_attack*` 快速等待分支。

## 保留的行为

1. 同一动作后的常规 `Sleep + choice probe + post-ready` 流程保留。
2. `WaitForConstructedActionReady(...)` 仍可在普通动作链里用于“下一动作可执行”判断。
3. 既有失败恢复逻辑 `GetConstructedActionFailureRecovery(...)` 保留。

## 风险与控制

主要风险是构筑连续攻击的节奏会变慢，但这是本次调整的预期结果。为避免误伤其他执行链路，本次只删除连续攻击专属状态、命令拼接、日志标签和分支，不改普通动作等待参数。
