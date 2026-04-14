# 构筑模式移除连续攻击机制 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底移除构筑模式的连续攻击快捷机制，让攻击动作统一走普通执行流程。

**Architecture:** 先用测试锁定 `BotService.cs` 中不应再出现攻击链标记和状态，再删除 `|CHAIN` 命令拼接、跨轮攻击缓存、攻击链专用等待与跳过刷新分支，最后跑定向测试确认失败恢复逻辑仍然存在。

**Tech Stack:** C# / .NET 8 / xUnit / BotMain / HsBox 构筑执行链

---

### Task 1: 先锁定“连续攻击机制必须消失”

**Files:**
- Modify: `BotCore.Tests/BotServiceAttackChainTests.cs`
- Test: `BotCore.Tests/BotServiceAttackChainTests.cs`

- [ ] **Step 1: 写失败测试**

把旧的 `ShouldUseAttackChainFastPath(...)` 测试改成源码级回归断言，要求 `BotService.cs` 中不再出现：

- `|CHAIN`
- `lastRecommendationWasAttackOnly`
- `ShouldUseAttackChainFastPath`
- `ready_chain_attack`

- [ ] **Step 2: 运行测试确认先失败**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter BotServiceAttackChainTests -v minimal`  
Expected: FAIL，因为源码里仍然存在上述标记。

### Task 2: 删除构筑攻击链专属执行分支

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 删除跨轮攻击状态**

移除：

- `lastRecommendationWasAttackOnly`
- `cachedFriendlyEntities`
- `cachedDeckCards`

- [ ] **Step 2: 删除攻击链专属跳过逻辑**

统一恢复：

- `TurnStart` 常规 ready interval
- `board recovery`
- `deck state / friendly entity` 刷新
- `TryRunHumanizedTurnPrelude(...)`
- follow-box 常规短暂停顿

- [ ] **Step 3: 删除攻击链专属命令和等待**

移除：

- `ATTACK|...|CHAIN` 拼接
- `ShouldUseAttackChainFastPath(...)`
- `ready_chain_attack*` 专用后等待分支

- [ ] **Step 4: 运行定向测试确认通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter BotServiceAttackChainTests -v minimal`  
Expected: PASS

### Task 3: 做最小验证并提交

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotCore.Tests/BotServiceAttackChainTests.cs`
- Create: `docs/superpowers/specs/2026-04-14-remove-constructed-attack-chain-design.md`
- Create: `docs/superpowers/plans/2026-04-14-remove-constructed-attack-chain.md`

- [ ] **Step 1: 运行额外编译级验证**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "ConstructedActionReadyDiagnosticsTests|ConstructedActionReadyEvaluatorTests" -v minimal`  
Expected: PASS

- [ ] **Step 2: 使用中文提交并推送**

```bash
git add BotMain/BotService.cs BotCore.Tests/BotServiceAttackChainTests.cs docs/superpowers/specs/2026-04-14-remove-constructed-attack-chain-design.md docs/superpowers/plans/2026-04-14-remove-constructed-attack-chain.md
git commit -m "构筑：移除连续攻击快捷机制"
git push
```
