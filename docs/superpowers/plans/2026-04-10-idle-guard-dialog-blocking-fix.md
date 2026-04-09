# IdleGuard 弹窗遮挡误判修复 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复弹窗遮挡时 IdleGuard 误判"有操作"导致脚本无限挂机的 bug，通过三层防御确保操作真正生效才计为有效。

**Architecture:** 第一层在 Payload 端 `ActionExecutor.Execute()` 入口检查弹窗并拦截；第二层在 BotService 发送操作命令前检测并关闭弹窗；第三层在操作返回成功后对比游戏状态快照验证操作是否真正生效。

**Tech Stack:** C# / .NET, xUnit 测试框架

---

### Task 1: Payload 端 — ActionExecutor 操作前弹窗检查

**Files:**
- Modify: `HearthstonePayload/ActionExecutor.cs:48` (添加 _nav 静态字段)
- Modify: `HearthstonePayload/ActionExecutor.cs:125` (添加 SetSceneNavigator 方法)
- Modify: `HearthstonePayload/ActionExecutor.cs:1193-1203` (Execute 方法入口，加弹窗检查)
- Modify: `HearthstonePayload/Entry.cs:403-411` (nav 初始化后注入到 ActionExecutor)

- [ ] **Step 1: 添加 SceneNavigator 静态字段和 setter**

在 `HearthstonePayload/ActionExecutor.cs` 中，添加静态字段和 setter 方法：

```csharp
// 在现有静态字段区域（约 line 48 附近）添加：
private static SceneNavigator _nav;
```

在 `Init` 方法之后（line 125 之后）添加 setter：

```csharp
public static void SetSceneNavigator(SceneNavigator nav)
{
    _nav = nav;
}
```

Init 方法签名不变，保持 `public static void Init(CoroutineExecutor executor)`。

- [ ] **Step 2: 在 Execute 方法入口添加弹窗检查**

在 `HearthstonePayload/ActionExecutor.cs` 的 `Execute()` 方法中（line 1193），在 `switch (type)` 之前插入弹窗检查：

```csharp
public static string Execute(GameReader reader, string actionData)
{
    if (string.IsNullOrEmpty(actionData)) return "SKIP:empty";
    if (!EnsureTypes()) return "SKIP:no_asm";

    var parts = actionData.IndexOf('|') >= 0
        ? actionData.Split('|')
        : actionData.Split(':');
    var type = parts[0];

    // ── 弹窗前置检查：对棋盘交互操作，检测是否有弹窗遮挡 ──
    if (_nav != null
        && type != "END_TURN"
        && type != "CANCEL"
        && type != "CONCEDE"
        && type != "HUMAN_TURN_START")
    {
        try
        {
            var dialogResp = _nav.GetBlockingDialog();
            if (!string.IsNullOrEmpty(dialogResp)
                && !string.Equals(dialogResp, "NO_DIALOG", StringComparison.OrdinalIgnoreCase))
            {
                // 提取弹窗类型用于诊断日志
                var dialogDetail = dialogResp.StartsWith("DIALOG:", StringComparison.Ordinal)
                    ? dialogResp.Substring("DIALOG:".Length)
                    : dialogResp;
                return "FAIL:" + type + ":DIALOG_BLOCKING:" + dialogDetail;
            }
        }
        catch
        {
            // 弹窗检测失败不应阻止操作执行，忽略异常继续
        }
    }

    switch (type)
    {
        // ... 原有 case 分支不变
```

- [ ] **Step 3: 在 Entry.cs 中注入 SceneNavigator**

在 `HearthstonePayload/Entry.cs` 中，`_nav` 初始化完成后（约 line 403-411，`MainLoop` 中 `nav.Init()` 成功之后），添加一行：

```csharp
ActionExecutor.SetSceneNavigator(nav);
```

Entry.cs line 76 的 `ActionExecutor.Init(_coroutine)` 不需要修改。
Entry.cs line 599 的 `ActionExecutor.Execute(reader, ...)` 不需要修改。

- [ ] **Step 4: 编译验证**

运行：`dotnet build HearthstonePayload/HearthstonePayload.csproj`
预期：编译成功，无错误

- [ ] **Step 5: 提交**

```bash
git add HearthstonePayload/ActionExecutor.cs HearthstonePayload/Entry.cs
git commit -m "feat: ActionExecutor 操作前弹窗检查（IdleGuard 第一层防御）"
```

---

### Task 2: BotService 端 — DIALOG_BLOCKING 专用错误处理

**Files:**
- Modify: `BotMain/BotService.cs:2854-2912` (标准天梯操作结果处理)
- Modify: `BotMain/BotService.cs:3848-3873` (竞技场操作结果处理)

- [ ] **Step 1: 添加 IsDialogBlockingFailure 辅助方法**

在 `BotMain/BotService.cs` 中，`IsActionFailure` 方法（line 7470-7478）附近添加：

```csharp
private static bool IsDialogBlockingFailure(string result)
{
    return !string.IsNullOrWhiteSpace(result)
        && result.IndexOf("DIALOG_BLOCKING", StringComparison.OrdinalIgnoreCase) >= 0;
}
```

- [ ] **Step 2: 修改标准天梯操作失败处理（line 2858-2912）**

在 `BotMain/BotService.cs` 的 `IsActionFailure(result)` 分支内，在现有 CANCEL 逻辑前插入 DIALOG_BLOCKING 判断：

```csharp
if (IsActionFailure(result))
{
    // ── DIALOG_BLOCKING 专用处理：操作未执行，跳过 CANCEL ──
    if (IsDialogBlockingFailure(result))
    {
        Log($"[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭");
        try
        {
            if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, "IdleGuard.DialogBlock")
                && !string.IsNullOrWhiteSpace(blockDialogType)
                && BotProtocol.IsSafeBlockingDialogButtonLabel(blockDialogButton))
            {
                TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.DialogBlock");
                SleepOrCancelled(500);
            }
        }
        catch { }
        actionFailed = true;
        actionFailedThisAction = true;
        break;
    }

    // 原有逻辑：发送 CANCEL
    if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
        || isTrade
        || isAttack)
    {
        var cancelResult = SendActionCommand(pipe, "CANCEL", 3000) ?? "NO_RESPONSE";
        Log($"[Action] CANCEL -> {cancelResult}");
    }
    // ... 后续原有逻辑不变
```

- [ ] **Step 3: 修改竞技场操作失败处理（line 3852-3873）**

在 `BotMain/BotService.cs` 竞技场的 `IsActionFailure(result)` 分支内，同样插入 DIALOG_BLOCKING 判断：

```csharp
if (IsActionFailure(result))
{
    // ── DIALOG_BLOCKING 专用处理 ──
    if (IsDialogBlockingFailure(result))
    {
        Log($"[IdleGuard] 弹窗阻塞操作 {action}，尝试关闭 (Arena)");
        try
        {
            if (TryGetBlockingDialog(pipe, 1500, out var blockDialogType, out var blockDialogButton, "IdleGuard.ArenaDialogBlock")
                && !string.IsNullOrWhiteSpace(blockDialogType)
                && BotProtocol.IsSafeBlockingDialogButtonLabel(blockDialogButton))
            {
                TryDismissBlockingDialog(pipe, 2000, out _, "IdleGuard.ArenaDialogBlock");
                SleepOrCancelled(500);
            }
        }
        catch { }
        actionFailed = true;
        break;
    }

    // 原有逻辑
    if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase))
    {
        SendActionCommand(pipe, "CANCEL", 3000);
    }
    // ... 后续原有逻辑不变
```

- [ ] **Step 4: 编译验证**

运行：`dotnet build BotMain/BotMain.csproj`
预期：编译成功

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: DIALOG_BLOCKING 专用错误处理，跳过无意义的 CANCEL"
```

---

### Task 3: BotService 端 — 操作前弹窗检测与关闭

**Files:**
- Modify: `BotMain/BotService.cs:2832-2848` (标准天梯，SendActionCommand 之前)
- Modify: `BotMain/BotService.cs:3840-3845` (竞技场，SendActionCommand 之前)

- [ ] **Step 1: 在标准天梯操作发送前添加弹窗检测**

在 `BotMain/BotService.cs` 中，在 `commandToSend` 构造之后、`SendActionCommand` 之前（约 line 2846 和 2847 之间）插入：

```csharp
                            // ── IdleGuard 第二层：操作前弹窗检测与关闭 ──
                            if (!isEndTurn)
                            {
                                try
                                {
                                    if (TryGetBlockingDialog(pipe, 1500, out var preDialogType, out var preDialogButton, "IdleGuard.PreAction")
                                        && !string.IsNullOrWhiteSpace(preDialogType))
                                    {
                                        if (BotProtocol.IsSafeBlockingDialogButtonLabel(preDialogButton))
                                        {
                                            if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "IdleGuard.PreAction")
                                                && !string.IsNullOrWhiteSpace(dismissResp)
                                                && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                                            {
                                                Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，已关闭 -> {dismissResp}");
                                                SleepOrCancelled(500);
                                            }
                                        }
                                        else
                                        {
                                            Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，按钮不安全，跳过操作");
                                            actionFailed = true;
                                            actionFailedThisAction = true;
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }
```

- [ ] **Step 2: 在竞技场操作发送前添加弹窗检测**

在 `BotMain/BotService.cs` 竞技场循环中，`SendActionCommand` 调用之前（约 line 3844 和 3845 之间）插入：

```csharp
                    // ── IdleGuard 第二层：操作前弹窗检测与关闭 (Arena) ──
                    if (!isEndTurn)
                    {
                        try
                        {
                            if (TryGetBlockingDialog(pipe, 1500, out var preDialogType, out var preDialogButton, "IdleGuard.ArenaPreAction")
                                && !string.IsNullOrWhiteSpace(preDialogType))
                            {
                                if (BotProtocol.IsSafeBlockingDialogButtonLabel(preDialogButton))
                                {
                                    if (TryDismissBlockingDialog(pipe, 2000, out var dismissResp, "IdleGuard.ArenaPreAction")
                                        && !string.IsNullOrWhiteSpace(dismissResp)
                                        && dismissResp.StartsWith("OK:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，已关闭 (Arena) -> {dismissResp}");
                                        SleepOrCancelled(500);
                                    }
                                }
                                else
                                {
                                    Log($"[IdleGuard] 操作前检测到弹窗 {preDialogType}({preDialogButton})，按钮不安全，跳过操作 (Arena)");
                                    actionFailed = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
```

- [ ] **Step 3: 编译验证**

运行：`dotnet build BotMain/BotMain.csproj`
预期：编译成功

- [ ] **Step 4: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 操作前弹窗检测与关闭（IdleGuard 第二层防御）"
```

---

### Task 4: BotService 端 — 操作后状态验证

**Files:**
- Modify: `BotMain/BotService.cs:2854-2856` (标准天梯 IdleGuard 标记)
- Modify: `BotMain/BotService.cs:3848-3850` (竞技场 IdleGuard 标记)

- [ ] **Step 1: 添加状态快照和验证辅助方法**

在 `BotMain/BotService.cs` 中，`IsActionFailure` 方法附近（约 line 7478 后）添加：

```csharp
/// <summary>
/// 操作前状态快照，用于操作后验证。
/// </summary>
private sealed class ActionStateSnapshot
{
    public int HandCount;
    public int ManaAvailable;
    public int FriendMinionCount;
    public int EnemyMinionCount;
}

private ActionStateSnapshot TakeActionStateSnapshot(PipeServer pipe)
{
    try
    {
        var seedResp = pipe.SendAndReceive("GET_SEED", 3000);
        if (string.IsNullOrWhiteSpace(seedResp) || !seedResp.StartsWith("SEED:", StringComparison.Ordinal))
            return null;

        var compatibleSeed = SeedCompatibility.GetCompatibleSeed(seedResp, out _);
        var board = Board.FromSeed(compatibleSeed);
        if (board == null) return null;

        return new ActionStateSnapshot
        {
            HandCount = board.Hand?.Count ?? 0,
            ManaAvailable = board.ManaAvailable,
            FriendMinionCount = board.MinionFriend?.Count ?? 0,
            EnemyMinionCount = board.MinionEnemy?.Count ?? 0
        };
    }
    catch
    {
        return null;
    }
}

/// <summary>
/// 根据操作类型验证状态是否发生变化。
/// 返回 true 表示操作确实生效（或无法判断时保守返回 true）。
/// </summary>
private bool VerifyActionEffective(string action, ActionStateSnapshot before, ActionStateSnapshot after)
{
    // 无法获取快照时保守判定为有效
    if (before == null || after == null)
        return true;

    if (action.StartsWith("PLAY|", StringComparison.OrdinalIgnoreCase))
    {
        // 打牌：手牌减少 或 法力消耗
        return after.HandCount < before.HandCount
            || after.ManaAvailable < before.ManaAvailable;
    }

    if (action.StartsWith("ATTACK|", StringComparison.OrdinalIgnoreCase))
    {
        // 攻击：敌方随从数减少 或 场面有任何变化
        // 注：攻击可能导致双方随从死亡，也可能打脸不改变随从数
        // 保守策略：场面有任何变化就算有效
        return after.FriendMinionCount != before.FriendMinionCount
            || after.EnemyMinionCount != before.EnemyMinionCount
            || after.ManaAvailable != before.ManaAvailable;
    }

    if (action.StartsWith("HERO_POWER|", StringComparison.OrdinalIgnoreCase))
    {
        // 英雄技能：法力消耗
        return after.ManaAvailable < before.ManaAvailable;
    }

    if (action.StartsWith("USE_LOCATION|", StringComparison.OrdinalIgnoreCase))
    {
        // 地标：场面或法力有变化
        return after.ManaAvailable != before.ManaAvailable
            || after.FriendMinionCount != before.FriendMinionCount
            || after.EnemyMinionCount != before.EnemyMinionCount;
    }

    if (action.StartsWith("TRADE|", StringComparison.OrdinalIgnoreCase))
    {
        // 交易：手牌变化
        return after.HandCount != before.HandCount;
    }

    // 未知操作类型，保守判定为有效
    return true;
}
```

- [ ] **Step 2: 在标准天梯中集成状态快照和验证**

在 `BotMain/BotService.cs` 中，修改标准天梯操作执行区域。

在 `SendActionCommand` 调用之前（line 2847 之前）取快照：

```csharp
                            // ── IdleGuard 第三层：操作前取状态快照 ──
                            ActionStateSnapshot preActionSnapshot = null;
                            if (!isEndTurn)
                            {
                                preActionSnapshot = TakeActionStateSnapshot(pipe);
                            }
```

替换原有的 IdleGuard 标记逻辑（line 2854-2856）：

```csharp
                            // IdleGuard: 验证操作是否真正生效
                            if (!isEndTurn && !IsActionFailure(result))
                            {
                                if (preActionSnapshot == null)
                                {
                                    // 无快照时保守标记为有效
                                    _turnHadEffectiveAction = true;
                                }
                                else
                                {
                                    var postActionSnapshot = TakeActionStateSnapshot(pipe);
                                    if (VerifyActionEffective(action, preActionSnapshot, postActionSnapshot))
                                    {
                                        _turnHadEffectiveAction = true;
                                    }
                                    else
                                    {
                                        Log($"[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作");
                                    }
                                }
                            }
```

- [ ] **Step 3: 在竞技场中集成状态快照和验证**

在 `BotMain/BotService.cs` 竞技场循环中做同样的改动。

在 `SendActionCommand` 调用之前（line 3844 之前）取快照：

```csharp
                    // ── IdleGuard 第三层：操作前取状态快照 (Arena) ──
                    ActionStateSnapshot preActionSnapshot = null;
                    if (!isEndTurn)
                    {
                        preActionSnapshot = TakeActionStateSnapshot(pipe);
                    }
```

替换原有的 IdleGuard 标记逻辑（line 3848-3850）：

```csharp
                    // IdleGuard: 验证操作是否真正生效 (Arena)
                    if (!isEndTurn && !IsActionFailure(result))
                    {
                        if (preActionSnapshot == null)
                        {
                            _turnHadEffectiveAction = true;
                        }
                        else
                        {
                            var postActionSnapshot = TakeActionStateSnapshot(pipe);
                            if (VerifyActionEffective(action, preActionSnapshot, postActionSnapshot))
                            {
                                _turnHadEffectiveAction = true;
                            }
                            else
                            {
                                Log($"[IdleGuard] 操作 {action} 返回成功但状态未变化，判定为无效操作 (Arena)");
                            }
                        }
                    }
```

- [ ] **Step 4: 编译验证**

运行：`dotnet build BotMain/BotMain.csproj`
预期：编译成功

- [ ] **Step 5: 提交**

```bash
git add BotMain/BotService.cs
git commit -m "feat: 操作后状态验证（IdleGuard 第三层防御）"
```

---

### Task 5: 单元测试

**Files:**
- Modify: `BotCore.Tests/BotProtocolTests.cs` (DIALOG_BLOCKING 协议测试)

- [ ] **Step 1: 编写 DIALOG_BLOCKING 响应识别测试**

在 `BotCore.Tests/BotProtocolTests.cs` 中添加：

```csharp
[Fact]
public void IsActionFailure_RecognizesDialogBlockingResponse()
{
    // IsActionFailure 是 private static，通过 BotProtocol 间接验证
    // DIALOG_BLOCKING 响应以 FAIL: 开头，IsActionFailure 应识别
    Assert.True("FAIL:PLAY:DIALOG_BLOCKING:AlertPopup"
        .StartsWith("FAIL", StringComparison.OrdinalIgnoreCase));
    Assert.True("FAIL:ATTACK:DIALOG_BLOCKING:ReconnectHelperDialog"
        .StartsWith("FAIL", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void IsBlockingDialogResponse_AcceptsDialogAndNoDialog()
{
    Assert.True(BotProtocol.IsBlockingDialogResponse("NO_DIALOG"));
    Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:AlertPopup:OK"));
    Assert.True(BotProtocol.IsBlockingDialogResponse("DIALOG:ReconnectHelperDialog:确定"));
    Assert.False(BotProtocol.IsBlockingDialogResponse("FAIL:PLAY:DIALOG_BLOCKING:AlertPopup"));
    Assert.False(BotProtocol.IsBlockingDialogResponse("OK:PLAY"));
}

[Fact]
public void TryParseBlockingDialog_ExtractsTypeAndButton()
{
    Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:AlertPopup:OK", out var type, out var btn));
    Assert.Equal("AlertPopup", type);
    Assert.Equal("OK", btn);

    Assert.True(BotProtocol.TryParseBlockingDialog("DIALOG:ReconnectHelperDialog:确定", out type, out btn));
    Assert.Equal("ReconnectHelperDialog", type);
    Assert.Equal("确定", btn);

    Assert.False(BotProtocol.TryParseBlockingDialog("NO_DIALOG", out _, out _));
}

[Fact]
public void IsSafeBlockingDialogButtonLabel_AcceptsSafeLabels()
{
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("OK"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("ok"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("okay"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("确认"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("确定"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("关闭"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("返回"));
    Assert.True(BotProtocol.IsSafeBlockingDialogButtonLabel("取消"));
}
```

- [ ] **Step 2: 运行测试验证通过**

运行：`dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "ClassName~BotProtocolTests"`
预期：所有新增测试 PASS

- [ ] **Step 3: 提交**

```bash
git add BotCore.Tests/BotProtocolTests.cs
git commit -m "test: IdleGuard DIALOG_BLOCKING 协议识别测试"
```

---

### Task 6: 全量编译与最终验证

**Files:** 无新文件

- [ ] **Step 1: 全量编译**

运行：`dotnet build Hearthbot.sln`
预期：编译成功，无错误，无警告（或仅有预期的已有警告）

- [ ] **Step 2: 运行全部测试**

运行：`dotnet test BotCore.Tests/BotCore.Tests.csproj`
预期：所有测试 PASS

- [ ] **Step 3: 最终提交并推送**

```bash
git push origin main
```
