# 竞技场自动模式实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Hearthbot 新增竞技场完整自动循环模式（买票→选职业→选牌→排队→对局→结算→领奖→重开），跟随盒子 CEF 推荐。

**Architecture:** 在 BotService 主循环的 `_modeIndex == 2` 分支新增竞技场状态机循环（参照战旗模式 `_modeIndex == 100` 的结构）。选牌阶段通过新建 `HsBoxArenaDraftBridge` 从 CEF 页面 `/client-jipaiqi/arena` 读取推荐；对局阶段复用现有 `HsBoxRecommendationBridge`，切换回调名为 `onUpdateArenaRecommend`。Payload 端新增竞技场专用管道命令。

**Tech Stack:** C# / .NET 8 / WPF / BepInEx / Unity 反射 / Chrome DevTools Protocol (CDP)

**Spec:** `docs/superpowers/specs/2026-04-05-arena-mode-design.md`

---

## 文件结构

| 文件 | 职责 | 操作 |
|------|------|------|
| `BotMain/MainViewModel.cs` | 新增 Arena UI 常量、配置属性、模式映射 | 修改 |
| `BotMain/MainWindow.xaml` | ComboBox 增加 Arena 选项 | 修改 |
| `BotMain/SettingsWindow.xaml` | 竞技场设置区域（金币开关、保底阈值） | 修改 |
| `HearthstonePayload/Entry.cs` | 竞技场管道命令分发 | 修改 |
| `HearthstonePayload/SceneNavigator.cs` | 竞技场状态查询、购票、领奖等 UI 操作 | 修改 |
| `HearthstonePayload/ActionExecutor.cs` | 竞技场选牌/选职业鼠标操作 | 修改 |
| `BotMain/HsBoxRecommendationProvider.cs` | 新增 `HsBoxArenaDraftBridge` 内部类；`HsBoxRecommendationBridge` 支持竞技场回调名切换 | 修改 |
| `BotMain/BotService.cs` | 竞技场主循环状态机 | 修改 |

---

### Task 1: UI 与配置 — 新增 Arena 模式选项和设置

**Files:**
- Modify: `BotMain/MainViewModel.cs:25-28` (常量), `:282-290` (模式属性), `:825-831` (模式映射), `:1130-1155` (保存), `:1200-1212` (加载)
- Modify: `BotMain/MainWindow.xaml:27-32` (ComboBox)
- Modify: `BotMain/SettingsWindow.xaml:75-76` (行为区后添加竞技场区)

- [ ] **Step 1: 在 MainViewModel.cs 添加 Arena 常量和配置字段**

在现有常量定义（行25-28）之后添加：

```csharp
private const int UiModeArena = 4;
private const int ServiceModeArena = 2;
```

在私有字段区域添加：

```csharp
private bool _arenaUseGold;
private int _arenaGoldReserve;
```

- [ ] **Step 2: 在 MainViewModel.cs 添加公开属性**

在现有属性区域添加：

```csharp
public bool IsArenaMode => ModeIndex == UiModeArena;

public bool ArenaUseGold
{
    get => _arenaUseGold;
    set { _arenaUseGold = value; Notify(); AutoSave(); }
}

public int ArenaGoldReserve
{
    get => _arenaGoldReserve;
    set { _arenaGoldReserve = Math.Max(0, value); Notify(); AutoSave(); }
}
```

- [ ] **Step 3: 更新 ModeIndex setter 通知 IsArenaMode**

在 `ModeIndex` 属性的 setter 中（行362-370），在 `Notify(nameof(IsBattlegroundsMode));` 之后添加：

```csharp
Notify(nameof(IsArenaMode));
```

- [ ] **Step 4: 更新 CurrentModeName 和 TopStatusText**

在 `CurrentModeName` switch 表达式（行283-290）中添加 Arena 分支：

```csharp
UiModeArena => "Arena",
```

在 `TopStatusText`（行281）中，让竞技场也显示 "HSBox" 推荐：

将 `FollowHsBoxOperation || IsBattlegroundsMode` 改为 `FollowHsBoxOperation || IsBattlegroundsMode || IsArenaMode`。

- [ ] **Step 5: 更新模式映射（启动时 UI→Service）**

在 `OnMainButton()` 的 switch 语句（行826-831）中添加 Arena 映射：

```csharp
case UiModeArena: serviceMode = ServiceModeArena; break;
```

同时在 `if (IsBattlegroundsMode)` 之后添加：

```csharp
if (IsArenaMode)
    _bot.SetFollowHsBoxRecommendations(true);
```

- [ ] **Step 6: 更新 SaveSettings 和 LoadSettings**

**SaveSettings**（行1130+区域）添加：

```csharp
dict["ArenaUseGold"] = JsonSerializer.SerializeToElement(ArenaUseGold);
dict["ArenaGoldReserve"] = JsonSerializer.SerializeToElement(ArenaGoldReserve);
```

**LoadSettings**（行1200+区域）添加：

```csharp
if (dict.TryGetValue("ArenaUseGold", out v)) ArenaUseGold = v.GetBoolean();
if (dict.TryGetValue("ArenaGoldReserve", out v)) ArenaGoldReserve = ReadOptionalInt32(v, 0);
```

- [ ] **Step 7: 更新 LocalRecommendationControlsEnabled**

在行616的条件中加入竞技场：

```csharp
public bool LocalRecommendationControlsEnabled => !FollowHsBoxOperation && !IsBattlegroundsMode && !IsArenaMode;
```

同样更新 `DeckSelectionVisible`，竞技场不需要选卡组：竞技场和战旗一样隐藏卡组选择。

- [ ] **Step 8: MainWindow.xaml ComboBox 添加 Arena 项**

在 `MainWindow.xaml` 行31（`<ComboBoxItem Content="Test"/>` 之后）添加：

```xml
<ComboBoxItem Content="Arena"/>
```

注意：这使得 ComboBox 索引变为 0=Standard, 1=Wild, 2=Battlegrounds, 3=Test, 4=Arena。

- [ ] **Step 9: SettingsWindow.xaml 添加竞技场设置区**

在 `SettingsWindow.xaml` 的 `<!-- 行为 -->` GroupBox 之后（行76之后）添加：

```xml
<!-- 竞技场 -->
<GroupBox Header="竞技场" Padding="8,6" Margin="0,0,0,8">
    <StackPanel>
        <CheckBox Content="允许使用金币购买门票" FontSize="11" IsChecked="{Binding ArenaUseGold}" Margin="0,0,0,4"
                  ToolTip="票用完后用金币继续，否则票用完即停止"/>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
            <TextBlock Text="金币保底:" FontSize="11" VerticalAlignment="Center" Margin="0,0,3,0"/>
            <TextBox Width="50" Height="20" Text="{Binding ArenaGoldReserve}" FontSize="11"
                     VerticalContentAlignment="Center"
                     ToolTip="金币低于此值时停止消耗金币，默认0"/>
        </StackPanel>
    </StackPanel>
</GroupBox>
```

- [ ] **Step 10: 更新向后兼容逻辑**

在 LoadSettings 的 ModeIndex 加载逻辑（行1206-1210）中，确保 index 4 不会被错误映射。现有代码只对 `loadedMode == 2` 做旧设置兼容，index 4 是新值，无需特殊处理。验证不影响即可。

- [ ] **Step 11: 提交**

```bash
git add BotMain/MainViewModel.cs BotMain/MainWindow.xaml BotMain/SettingsWindow.xaml
git commit -m "feat(arena): 新增 Arena 模式 UI 选项和竞技场配置（金币开关、保底阈值）"
```

---

### Task 2: Payload 端 — 竞技场管道命令（SceneNavigator + Entry）

**Files:**
- Modify: `HearthstonePayload/SceneNavigator.cs` — 新增竞技场状态查询和 UI 操作方法
- Modify: `HearthstonePayload/Entry.cs:581+` — 新增命令分发
- Modify: `HearthstonePayload/ActionExecutor.cs:1497+` — 新增选牌鼠标操作

**前置知识：** Payload 运行在游戏进程内（BepInEx 插件），通过 Unity 反射访问游戏类。竞技场的核心游戏类是 `DraftManager`（选牌管理），需要通过反射发现其 API。SceneNavigator 已有 `m_ArenaButton` 和 `DRAFT` 场景定义。

#### 子任务 2A: SceneNavigator 新增竞技场方法

- [ ] **Step 1: 在 SceneNavigator.cs 中新增 GetArenaStatus 方法**

此方法通过反射读取 `DraftManager` 单例判断竞技场状态。在 SceneNavigator 类中添加：

```csharp
/// <summary>
/// 返回竞技场当前状态：
/// NO_DRAFT | HERO_PICK | CARD_DRAFT:N/30 | DRAFT_COMPLETE | REWARDS | UNKNOWN
/// </summary>
public string GetArenaStatus()
{
    return OnMain(() =>
    {
        try
        {
            // 获取 DraftManager 单例
            var draftMgrType = _ctx.FindType("DraftManager");
            if (draftMgrType == null) return "UNKNOWN:no_type";

            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "NO_DRAFT";

            // 读取 DraftMode 枚举
            var draftMode = _ctx.GetFieldOrProp(draftMgr, "m_currentMode")
                         ?? _ctx.GetFieldOrProp(draftMgr, "m_draftMode");
            var modeStr = draftMode?.ToString() ?? "";

            // IN_REWARDS 状态
            if (modeStr.Contains("REWARDS", StringComparison.OrdinalIgnoreCase))
                return "REWARDS";

            // NO_ACTIVE_DRAFT
            if (modeStr.Contains("NO_ACTIVE", StringComparison.OrdinalIgnoreCase))
                return "NO_DRAFT";

            // ACTIVE_DRAFT_DECK - 正在选牌或已完成
            // 检查选牌进度
            var draftDeck = _ctx.GetFieldOrProp(draftMgr, "m_draftDeck");
            if (draftDeck != null)
            {
                var slotList = _ctx.GetFieldOrProp(draftDeck, "m_slots") as System.Collections.IList;
                var validSlots = _ctx.GetFieldOrProp(draftDeck, "m_validSlotCount")
                              ?? (slotList?.Count ?? 0);
                int picked = Convert.ToInt32(validSlots);

                // 检查是否有待选择的 choices
                var choices = _ctx.GetFieldOrProp(draftMgr, "m_choices") as System.Collections.IList;
                bool hasChoices = choices != null && choices.Count > 0;

                if (picked >= 30 || (!hasChoices && picked > 0))
                    return "DRAFT_COMPLETE";

                // 检查是否在选英雄阶段（picked == 0 且 choices 存在）
                var heroCard = _ctx.GetFieldOrProp(draftMgr, "m_currentHero")
                            ?? _ctx.GetFieldOrProp(draftDeck, "HeroCardID");
                bool hasHero = heroCard != null && heroCard.ToString() != "" && heroCard.ToString() != "0";

                if (!hasHero && hasChoices)
                    return "HERO_PICK";

                if (hasChoices)
                    return $"CARD_DRAFT:{picked}/30";

                return picked == 0 ? "HERO_PICK" : $"CARD_DRAFT:{picked}/30";
            }

            return "NO_DRAFT";
        }
        catch (Exception ex)
        {
            return "UNKNOWN:" + ex.GetType().Name;
        }
    });
}
```

**重要：** `DraftManager` 的具体字段名（`m_currentMode`、`m_draftDeck`、`m_choices`、`m_slots` 等）需要在实现时通过反射枚举确认。可以用以下辅助方法列举字段：

```csharp
// 调试用：列举对象的所有字段和属性
private string DumpMembers(object obj)
{
    if (obj == null) return "null";
    var type = obj.GetType();
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Type: {type.FullName}");
    foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
        sb.AppendLine($"  field: {f.Name} = {f.GetValue(obj)}");
    foreach (var p in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
    {
        try { sb.AppendLine($"  prop: {p.Name} = {p.GetValue(obj)}"); }
        catch { sb.AppendLine($"  prop: {p.Name} = <error>"); }
    }
    return sb.ToString();
}
```

- [ ] **Step 2: 新增 GetArenaTicketInfo 方法**

读取玩家的门票数和金币数：

```csharp
/// <summary>返回 TICKETS:N|GOLD:M</summary>
public string GetArenaTicketInfo()
{
    return OnMain(() =>
    {
        try
        {
            // 通过 NetCache 或 PlayerProfile 获取
            var netCacheType = _ctx.FindType("NetCache");
            var netCache = _ctx.CallStaticAny(netCacheType, "Get");

            // 获取 NetCacheArenaTickets
            var ticketCache = _ctx.CallAny(netCache, "GetNetObject",
                new[] { _ctx.FindType("NetCache+NetCacheArenaTickets") });
            int tickets = 0;
            if (ticketCache != null)
            {
                var ticketCount = _ctx.GetFieldOrProp(ticketCache, "Balance")
                               ?? _ctx.GetFieldOrProp(ticketCache, "m_balance");
                tickets = ticketCount != null ? Convert.ToInt32(ticketCount) : 0;
            }

            // 获取金币
            var goldCache = _ctx.CallAny(netCache, "GetNetObject",
                new[] { _ctx.FindType("NetCache+NetCacheGoldBalance") });
            int gold = 0;
            if (goldCache != null)
            {
                var goldAmt = _ctx.GetFieldOrProp(goldCache, "GetTotal")
                           ?? _ctx.GetFieldOrProp(goldCache, "m_balance")
                           ?? _ctx.CallAny(goldCache, "GetTotal");
                gold = goldAmt != null ? Convert.ToInt32(goldAmt) : 0;
            }

            return $"TICKETS:{tickets}|GOLD:{gold}";
        }
        catch (Exception ex)
        {
            return $"ERROR:{ex.GetType().Name}";
        }
    });
}
```

**注意：** `NetCache` 类的具体结构需要在实现时通过反射确认。`NetCacheArenaTickets` 和 `NetCacheGoldBalance` 是已知的炉石内部类型名，但字段名可能不同。

- [ ] **Step 3: 新增 ArenaBuyTicket 方法**

```csharp
/// <summary>点击购买竞技场门票</summary>
public string ArenaBuyTicket()
{
    return OnMain(() =>
    {
        try
        {
            // DraftManager 应有开始新轮次的方法
            var scene = GetSceneInternal();
            if (scene != "DRAFT" && scene != "HUB")
                return "ERROR:wrong_scene:" + scene;

            // 尝试通过 DraftManager 的 RequestNewDraft / BeginDraft 方法
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            // 发送购买请求
            _ctx.CallAny(draftMgr, "RequestBeginDraft");
            return "OK:BUY";
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}
```

- [ ] **Step 4: 新增 GetArenaHeroChoices 和 PickArenaHero 方法**

```csharp
/// <summary>返回 HEROES:classId1,classId2,classId3 或从 choices 中提取英雄卡 ID</summary>
public string GetArenaHeroChoices()
{
    return OnMain(() =>
    {
        try
        {
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            var choices = _ctx.GetFieldOrProp(draftMgr, "m_choices") as System.Collections.IList;
            if (choices == null || choices.Count == 0)
                return "ERROR:no_choices";

            var ids = new List<string>();
            foreach (var choice in choices)
            {
                // 每个 choice 可能是 DraftChoice 对象，包含 CardID
                var cardId = _ctx.GetFieldOrProp(choice, "m_cardId")
                          ?? _ctx.GetFieldOrProp(choice, "CardID")
                          ?? choice.ToString();
                ids.Add(cardId.ToString());
            }
            return "HEROES:" + string.Join(",", ids);
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}

/// <summary>选择第 index 个英雄（0-based）</summary>
public string PickArenaHero(int index)
{
    return OnMain(() =>
    {
        try
        {
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            // 调用 DraftManager 的选择方法
            _ctx.CallAny(draftMgr, "OnChosen", index);
            return "OK:HERO_PICKED";
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}
```

- [ ] **Step 5: 新增 GetArenaDraftChoices 和 PickArenaCard 方法**

```csharp
/// <summary>返回当前 3 选 1 的卡牌 ID：CHOICES:cardId1,cardId2,cardId3</summary>
public string GetArenaDraftChoices()
{
    return OnMain(() =>
    {
        try
        {
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            var choices = _ctx.GetFieldOrProp(draftMgr, "m_choices") as System.Collections.IList;
            if (choices == null || choices.Count == 0)
                return "ERROR:no_choices";

            var ids = new List<string>();
            foreach (var choice in choices)
            {
                var cardId = _ctx.GetFieldOrProp(choice, "m_cardId")
                          ?? _ctx.GetFieldOrProp(choice, "CardID")
                          ?? choice.ToString();
                ids.Add(cardId.ToString());
            }
            return "CHOICES:" + string.Join(",", ids);
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}

/// <summary>选择第 index 张卡（0-based）</summary>
public string PickArenaCard(int index)
{
    return OnMain(() =>
    {
        try
        {
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            _ctx.CallAny(draftMgr, "OnChosen", index);
            return "OK:CARD_PICKED";
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}
```

- [ ] **Step 6: 新增 ClaimArenaRewards 方法**

```csharp
/// <summary>领取竞技场奖励</summary>
public string ClaimArenaRewards()
{
    return OnMain(() =>
    {
        try
        {
            var draftMgrType = _ctx.FindType("DraftManager");
            var draftMgr = _ctx.CallStaticAny(draftMgrType, "Get");
            if (draftMgr == null) return "ERROR:no_draft_mgr";

            // 尝试调用领取奖励方法
            _ctx.CallAny(draftMgr, "ClaimRewards");
            // 如果没有直接方法，可能需要通过 UI 按钮点击
            return "OK:CLAIMED";
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    });
}
```

- [ ] **Step 7: 在 Entry.cs 中注册竞技场命令分发**

在 `Entry.cs` 的命令分发链（约行581之后，`GET_BG_STATE` 附近）添加：

```csharp
else if (cmd == "ARENA_GET_STATUS")
{
    _pipe.Write(nav.GetArenaStatus());
}
else if (cmd == "ARENA_GET_TICKET_INFO")
{
    _pipe.Write(nav.GetArenaTicketInfo());
}
else if (cmd == "ARENA_BUY_TICKET")
{
    _pipe.Write(nav.ArenaBuyTicket());
}
else if (cmd == "ARENA_GET_HERO_CHOICES")
{
    _pipe.Write(nav.GetArenaHeroChoices());
}
else if (cmd.StartsWith("ARENA_PICK_HERO:", StringComparison.Ordinal))
{
    int index = int.Parse(cmd.Substring("ARENA_PICK_HERO:".Length));
    _pipe.Write(nav.PickArenaHero(index));
}
else if (cmd == "ARENA_GET_DRAFT_CHOICES")
{
    _pipe.Write(nav.GetArenaDraftChoices());
}
else if (cmd.StartsWith("ARENA_PICK_CARD:", StringComparison.Ordinal))
{
    int index = int.Parse(cmd.Substring("ARENA_PICK_CARD:".Length));
    _pipe.Write(nav.PickArenaCard(index));
}
else if (cmd == "ARENA_CLAIM_REWARDS")
{
    _pipe.Write(nav.ClaimArenaRewards());
}
```

- [ ] **Step 8: 提交**

```bash
git add HearthstonePayload/SceneNavigator.cs HearthstonePayload/Entry.cs
git commit -m "feat(arena): Payload 端竞技场管道命令（状态查询、购票、选牌、领奖）"
```

---

### Task 3: 选牌桥接 — HsBoxArenaDraftBridge

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs` — 新增 `HsBoxArenaDraftBridge` 内部类

**背景：** 盒子的竞技场选牌推荐通过 CEF 页面 `https://hs-web-embed.lushi.163.com/client-jipaiqi/arena` 的 `window.fRecommend(hero, guideList, cardStage)` 全局函数推送。需要通过 CDP hook 此函数捕获推荐数据。

- [ ] **Step 1: 定义 ArenaDraftRecommendation DTO**

在 `HsBoxRecommendationProvider.cs` 文件中（与其他内部类相邻的位置）添加：

```csharp
internal sealed class ArenaDraftRecommendation
{
    public bool Ok { get; set; }
    public string Reason { get; set; }
    public JToken Hero { get; set; }
    public JArray GuideList { get; set; }
    public int CardStage { get; set; }
    public long UpdatedAtMs { get; set; }
    public long Count { get; set; }
}
```

- [ ] **Step 2: 创建 HsBoxArenaDraftBridge 类骨架**

在 `HsBoxRecommendationProvider.cs` 中添加内部类，结构参照 `HsBoxBattlegroundsBridge`（行969+）：

```csharp
internal sealed class HsBoxArenaDraftBridge
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    private readonly object _sync = new object();
    private readonly Action<string> _log;
    private string _cachedWsUrl;
    private DateTime _cachedWsUrlUntilUtc = DateTime.MinValue;

    public HsBoxArenaDraftBridge(Action<string> log) { _log = log; }

    /// <summary>从盒子 CEF 页面读取竞技场选牌推荐</summary>
    public bool TryReadDraft(out ArenaDraftRecommendation recommendation, out string detail)
    {
        recommendation = null;
        detail = "unknown";
        lock (_sync)
        {
            var wsUrl = GetArenaDebuggerUrl(out detail);
            if (wsUrl == null) return false;

            if (!TryEvaluateArenaDraft(wsUrl, out var json, out detail))
                return false;

            if (string.IsNullOrWhiteSpace(json))
            {
                detail = "arena_eval_empty";
                return false;
            }

            try
            {
                var obj = JObject.Parse(json);
                recommendation = new ArenaDraftRecommendation
                {
                    Ok = obj.Value<bool>("ok"),
                    Reason = obj.Value<string>("reason") ?? "",
                    Hero = obj["hero"],
                    GuideList = obj["guideList"] as JArray,
                    CardStage = obj.Value<int>("cardStage"),
                    UpdatedAtMs = obj.Value<long>("updatedAt"),
                    Count = obj.Value<long>("count")
                };
                detail = recommendation.Ok ? "ready" : recommendation.Reason;
                return recommendation.Ok;
            }
            catch (Exception ex)
            {
                detail = "arena_parse_failed:" + ex.GetType().Name;
                return false;
            }
        }
    }

    public void InvalidateCache() { _cachedWsUrlUntilUtc = DateTime.MinValue; }
}
```

- [ ] **Step 3: 实现 GetArenaDebuggerUrl 方法**

参照现有 `HsBoxRecommendationBridge.GetDebuggerUrl()`（行3247-3298），但过滤条件改为 `/client-jipaiqi/arena`：

```csharp
private string GetArenaDebuggerUrl(out string detail)
{
    if (_cachedWsUrl != null && DateTime.UtcNow < _cachedWsUrlUntilUtc)
    {
        detail = "cached";
        return _cachedWsUrl;
    }

    detail = "arena_debugger_missing";
    try
    {
        var json = Http.GetStringAsync("http://127.0.0.1:9222/json/list").GetAwaiter().GetResult();
        var targets = JArray.Parse(json);

        foreach (var t in targets)
        {
            var url = t.Value<string>("url") ?? "";
            // 匹配竞技场选牌页面
            if (url.IndexOf("/client-jipaiqi/arena", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var wsUrl = t.Value<string>("webSocketDebuggerUrl");
                if (!string.IsNullOrWhiteSpace(wsUrl))
                {
                    _cachedWsUrl = wsUrl;
                    _cachedWsUrlUntilUtc = DateTime.UtcNow.AddSeconds(8);
                    detail = "ok";
                    return wsUrl;
                }
            }
        }
        detail = "arena_page_not_found";
        return null;
    }
    catch (Exception ex)
    {
        detail = "arena_debugger_probe_failed:" + ex.GetType().Name;
        return null;
    }
}
```

- [ ] **Step 4: 实现 Hook 注入脚本 BuildArenaDraftHookScript**

```csharp
private static string BuildArenaDraftHookScript()
{
    return @"
(function() {
    if (window.__hbArenaDraftInstalled) return JSON.stringify({ ok: true, installed: true });
    window.__hbArenaDraftInstalled = true;
    window.__hbArenaDraftCount = 0;
    window.__hbArenaDraftUpdatedAt = 0;
    window.__hbArenaDraftHero = null;
    window.__hbArenaDraftGuideList = null;
    window.__hbArenaDraftCardStage = 0;

    var orig = window.fRecommend;
    function wrapFRecommend() {
        if (typeof window.fRecommend !== 'function') return;
        if (window.fRecommend.__hbWrapped) return;
        var fn = window.fRecommend;
        var wrapped = function(hero, guideList, cardStage) {
            window.__hbArenaDraftHero = hero;
            window.__hbArenaDraftGuideList = guideList;
            window.__hbArenaDraftCardStage = cardStage;
            window.__hbArenaDraftUpdatedAt = Date.now();
            window.__hbArenaDraftCount++;
            return fn.apply(this, arguments);
        };
        wrapped.__hbWrapped = true;
        wrapped.__hbOriginal = fn;
        window.fRecommend = wrapped;
    }

    // 立即尝试包装
    wrapFRecommend();

    // setter 拦截器，防止页面后续重新赋值 fRecommend
    try {
        var current = window.fRecommend;
        Object.defineProperty(window, 'fRecommend', {
            configurable: true,
            enumerable: true,
            get: function() { return current; },
            set: function(v) {
                current = v;
                if (typeof v === 'function' && !v.__hbWrapped) {
                    var fn = v;
                    var w = function(hero, guideList, cardStage) {
                        window.__hbArenaDraftHero = hero;
                        window.__hbArenaDraftGuideList = guideList;
                        window.__hbArenaDraftCardStage = cardStage;
                        window.__hbArenaDraftUpdatedAt = Date.now();
                        window.__hbArenaDraftCount++;
                        return fn.apply(this, arguments);
                    };
                    w.__hbWrapped = true;
                    w.__hbOriginal = fn;
                    current = w;
                }
            }
        });
    } catch(e) {}

    return JSON.stringify({ ok: true, installed: true });
})();
";
}
```

- [ ] **Step 5: 实现状态读取脚本 BuildArenaDraftStateScript**

```csharp
private static string BuildArenaDraftStateScript()
{
    return @"
(function() {
    var r = {
        ok: false,
        reason: '',
        hero: null,
        guideList: null,
        cardStage: 0,
        updatedAt: 0,
        count: 0
    };

    if (!window.__hbArenaDraftInstalled) {
        r.reason = 'hook_not_installed';
        return JSON.stringify(r);
    }

    r.count = window.__hbArenaDraftCount || 0;
    r.updatedAt = window.__hbArenaDraftUpdatedAt || 0;

    if (r.count === 0) {
        r.reason = 'waiting_for_box_push';
        return JSON.stringify(r);
    }

    r.hero = window.__hbArenaDraftHero;
    r.guideList = window.__hbArenaDraftGuideList;
    r.cardStage = window.__hbArenaDraftCardStage || 0;
    r.ok = true;
    r.reason = 'ready';
    return JSON.stringify(r);
})();
";
}
```

- [ ] **Step 6: 实现 TryEvaluateArenaDraft 方法**

参照现有 `TryEvaluateState`（行3300-3340），通过 WebSocket 执行 CDP Runtime.evaluate：

```csharp
private static bool TryEvaluateArenaDraft(string wsUrl, out string json, out string detail)
{
    json = null;
    detail = "unknown";
    try
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        if (!socket.ConnectAsync(new Uri(wsUrl), CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(3)))
        {
            detail = "ws_connect_timeout";
            return false;
        }

        // Step A: 注入 hook
        var hookScript = BuildArenaDraftHookScript();
        var hookResp = CdpEvaluate(socket, hookScript, id: 1);
        if (hookResp == null)
        {
            detail = "hook_inject_failed";
            return false;
        }

        // Step B: 读取状态
        var stateScript = BuildArenaDraftStateScript();
        json = CdpEvaluate(socket, stateScript, id: 2);
        if (json == null)
        {
            detail = "state_eval_empty";
            return false;
        }

        detail = "ok";
        return true;
    }
    catch (Exception ex)
    {
        detail = "arena_eval_error:" + ex.GetType().Name;
        return false;
    }
}

/// <summary>发送 CDP Runtime.evaluate 并返回 result.value 字符串</summary>
private static string CdpEvaluate(System.Net.WebSockets.ClientWebSocket socket, string expression, int id)
{
    var request = new JObject
    {
        ["id"] = id,
        ["method"] = "Runtime.evaluate",
        ["params"] = new JObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = false
        }
    };

    var requestBytes = System.Text.Encoding.UTF8.GetBytes(request.ToString(Newtonsoft.Json.Formatting.None));
    socket.SendAsync(new ArraySegment<byte>(requestBytes),
        System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None)
        .GetAwaiter().GetResult();

    var buffer = new byte[65536];
    var sb = new System.Text.StringBuilder();
    System.Net.WebSockets.WebSocketReceiveResult result;
    do
    {
        result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                       .GetAwaiter().GetResult();
        sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
    } while (!result.EndOfMessage);

    var resp = JObject.Parse(sb.ToString());
    return resp?["result"]?["result"]?["value"]?.ToString();
}
```

**注意：** `CdpEvaluate` 辅助方法可以提取为共享工具，现有代码中可能已有类似实现。实现时先检查是否能复用 `HsBoxRecommendationBridge` 中的现有 WebSocket 通信逻辑。

- [ ] **Step 7: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat(arena): 新增 HsBoxArenaDraftBridge，通过 CDP hook fRecommend 读取盒子选牌推荐"
```

---

### Task 4: 对局内桥接 — HsBoxRecommendationBridge 支持竞技场回调

**Files:**
- Modify: `BotMain/HsBoxRecommendationProvider.cs` — `HsBoxRecommendationBridge` 支持模式切换

**背景：** 竞技场对局内出牌推荐来自 CEF 页面 `/client-jipaiqi/ai-recommend` 的 `window.onUpdateArenaRecommend` 回调。现有 Bridge 固定连接 `/client-jipaiqi/ladder-opp` 并 hook `onUpdateLadderActionRecommend`。需要根据模式切换目标。

- [ ] **Step 1: 为 HsBoxRecommendationBridge 添加模式字段**

在 `HsBoxRecommendationBridge` 类中添加：

```csharp
private bool _arenaMode;

public void SetArenaMode(bool enabled)
{
    if (_arenaMode == enabled) return;
    _arenaMode = enabled;
    // 切换模式时清除缓存，强制重新连接正确的页面
    _cachedDebuggerUrl = null;
    _cachedDebuggerUrlUntilUtc = DateTime.MinValue;
}
```

- [ ] **Step 2: 修改 GetDebuggerUrl 的页面过滤逻辑**

在 `GetDebuggerUrl` 方法（行3264-3279）中，根据 `_arenaMode` 切换过滤条件：

原始代码过滤 `/client-jipaiqi/` 并优先选 `/ladder-opp`。修改为：

```csharp
// 在筛选 targets 的循环中：
if (_arenaMode)
{
    // 竞技场模式：找 /client-jipaiqi/ai-recommend
    if (url.IndexOf("/client-jipaiqi/ai-recommend", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        bestUrl = wsUrl;
        break;  // 精确匹配，直接使用
    }
}
else
{
    // 原有逻辑：找 /client-jipaiqi/ 优先 /ladder-opp
    // ... 保持原样 ...
}
```

- [ ] **Step 3: 修改 BuildConstructedHookBootstrapScript 的回调名**

在 `BuildConstructedHookBootstrapScript`（行3445+）中，hook 的核心回调名是 `onUpdateLadderActionRecommend`。需要根据模式切换：

在 Bridge 中添加一个属性返回当前回调名：

```csharp
private string TargetCallbackName => _arenaMode
    ? "onUpdateArenaRecommend"
    : "onUpdateLadderActionRecommend";
```

然后在 `BuildConstructedHookBootstrapScript` 中将硬编码的 `'onUpdateLadderActionRecommend'` 替换为参数化。

**具体做法：** 将 `BuildConstructedHookBootstrapScript()` 改为接受回调名参数：

```csharp
private string BuildConstructedHookBootstrapScript()
{
    var callbackName = TargetCallbackName;
    // 在生成的 JS 中使用 callbackName 变量替换原来硬编码的名称
    return $@"
(function() {{
    // ... 脚本中的 'onUpdateLadderActionRecommend' 全部替换为 '{callbackName}' ...
}})();
";
}
```

**注意：** 实际修改时需要仔细替换 JS 脚本中所有出现的 `onUpdateLadderActionRecommend`，确保 setter 拦截器和模式匹配 hook 都使用正确的回调名。

- [ ] **Step 4: 在 HsBoxGameRecommendationProvider 中暴露模式切换**

在 `HsBoxGameRecommendationProvider` 类中添加公开方法：

```csharp
public void SetArenaMode(bool enabled)
{
    _bridge.SetArenaMode(enabled);
}
```

- [ ] **Step 5: 提交**

```bash
git add BotMain/HsBoxRecommendationProvider.cs
git commit -m "feat(arena): HsBoxRecommendationBridge 支持竞技场回调名切换（onUpdateArenaRecommend）"
```

---

### Task 5: 竞技场主循环 — BotService 状态机

**Files:**
- Modify: `BotMain/BotService.cs` — 在主循环添加 `_modeIndex == 2` 分支

**背景：** 参照战旗模式循环（行1903-1997），在主循环入口添加竞技场分支。竞技场状态机在 `ArenaLoop()` 方法中实现。

- [ ] **Step 1: 在主循环入口添加竞技场分支**

在 `MainLoop()` 中（行1903附近），`_modeIndex == 100` 分支之前或之后，添加：

```csharp
if (_modeIndex == 2)  // Arena
{
    ArenaLoop();
    return;
}
```

- [ ] **Step 2: 实现 ArenaLoop 方法骨架**

```csharp
private void ArenaLoop()
{
    var pipe = _pipe;
    Log("[Arena] ── 竞技场模式启动 ──");

    // 切换推荐系统到竞技场模式
    _hsBoxRecommendationProvider?.SetArenaMode(true);

    int runCount = 0;
    try
    {
        while (_running && !_finishAfterGame)
        {
            // Step 1: 导航到竞技场场景
            if (!ArenaEnsureDraftScene(pipe))
            {
                SleepOrCancelled(2000);
                continue;
            }

            // Step 2: 检查竞技场状态
            if (!TrySendAndReceiveExpected(pipe, "ARENA_GET_STATUS", 5000,
                    out var statusResp, "ARENA_STATUS", "Arena.Check"))
            {
                SleepOrCancelled(1000);
                continue;
            }

            Log($"[Arena] 状态: {statusResp}");

            if (statusResp == "NO_DRAFT")
            {
                // 需要购票
                if (!ArenaTryBuyTicket(pipe))
                {
                    Log("[Arena] 无法购票，停止循环。");
                    break;
                }
                SleepOrCancelled(3000);  // 等待购票生效
                continue;
            }
            else if (statusResp == "HERO_PICK")
            {
                ArenaPickHero(pipe);
                SleepOrCancelled(2000);
                continue;
            }
            else if (statusResp.StartsWith("CARD_DRAFT:", StringComparison.Ordinal))
            {
                ArenaPickCard(pipe);
                SleepOrCancelled(1500);
                continue;
            }
            else if (statusResp == "DRAFT_COMPLETE")
            {
                // 选牌完成，开始排队
                ArenaQueueAndPlay(pipe);
                // 对局结束后回到循环顶部检查状态
                continue;
            }
            else if (statusResp == "REWARDS")
            {
                ArenaClaimRewards(pipe);
                runCount++;
                Log($"[Arena] 第 {runCount} 轮竞技场完成。");
                SleepOrCancelled(3000);
                continue;
            }
            else
            {
                Log($"[Arena] 未知状态: {statusResp}，等待重试...");
                SleepOrCancelled(2000);
            }
        }
    }
    finally
    {
        _hsBoxRecommendationProvider?.SetArenaMode(false);
        Log($"[Arena] ── 竞技场模式结束，共完成 {runCount} 轮 ──");
    }
}
```

- [ ] **Step 3: 实现 ArenaEnsureDraftScene**

```csharp
private bool ArenaEnsureDraftScene(PipeServer pipe)
{
    if (!TryGetSceneValue(pipe, 3000, out var scene, "Arena.EnsureScene"))
        return false;

    if (string.Equals(scene, "DRAFT", StringComparison.OrdinalIgnoreCase))
        return true;

    if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
        return true;  // 可能在对局中，允许继续

    // 导航到竞技场
    Log($"[Arena] 当前场景: {scene}，导航到竞技场...");
    if (!TrySendAndReceiveExpected(pipe, "CLICK_HUB_BUTTON:arena", 10000,
            out var navResp, "OK", "Arena.Navigate"))
    {
        Log($"[Arena] 导航失败: {navResp}");
        return false;
    }

    SleepOrCancelled(3000);
    return true;
}
```

- [ ] **Step 4: 实现 ArenaTryBuyTicket**

```csharp
private bool ArenaTryBuyTicket(PipeServer pipe)
{
    // 查询票和金币
    if (!TrySendAndReceiveExpected(pipe, "ARENA_GET_TICKET_INFO", 5000,
            out var infoResp, "TICKETS:", "Arena.TicketInfo"))
    {
        Log($"[Arena] 查询票务信息失败: {infoResp}");
        return false;
    }

    // 解析 TICKETS:N|GOLD:M
    int tickets = 0, gold = 0;
    var parts = infoResp.Split('|');
    foreach (var part in parts)
    {
        if (part.StartsWith("TICKETS:", StringComparison.Ordinal))
            int.TryParse(part.Substring(8), out tickets);
        else if (part.StartsWith("GOLD:", StringComparison.Ordinal))
            int.TryParse(part.Substring(5), out gold);
    }

    Log($"[Arena] 票: {tickets}, 金币: {gold}");

    if (tickets > 0)
    {
        Log("[Arena] 使用门票购买。");
    }
    else if (_arenaUseGold && gold >= 150 + _arenaGoldReserve)
    {
        Log($"[Arena] 使用金币购买（金币 {gold} >= {150 + _arenaGoldReserve}）。");
    }
    else
    {
        if (!_arenaUseGold)
            Log("[Arena] 票已用完且金币开关关闭，停止。");
        else
            Log($"[Arena] 金币不足（{gold} < {150 + _arenaGoldReserve}），停止。");
        return false;
    }

    // 执行购买
    if (!TrySendAndReceiveExpected(pipe, "ARENA_BUY_TICKET", 10000,
            out var buyResp, "OK", "Arena.Buy"))
    {
        Log($"[Arena] 购票失败: {buyResp}");
        return false;
    }

    return true;
}
```

- [ ] **Step 5: 实现 ArenaPickHero（跟随盒子推荐）**

```csharp
private void ArenaPickHero(PipeServer pipe)
{
    Log("[Arena] 选择职业阶段...");

    // 从盒子读取推荐
    var draftBridge = _hsBoxArenaDraftBridge;
    int bestIndex = 0;

    if (draftBridge != null && draftBridge.TryReadDraft(out var rec, out var detail))
    {
        if (rec.GuideList != null && rec.GuideList.Count > 0)
        {
            // guideList 中找评分最高的
            double bestScore = double.MinValue;
            for (int i = 0; i < rec.GuideList.Count; i++)
            {
                var item = rec.GuideList[i];
                // 尝试从 item 中提取评分（具体字段名取决于盒子数据结构）
                double score = item.Value<double?>("score")
                            ?? item.Value<double?>("value")
                            ?? item.Value<double?>("winRate")
                            ?? 0;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            Log($"[Arena] 盒子推荐职业 index={bestIndex}, score={bestScore:F1}");
        }
        else
        {
            Log($"[Arena] 盒子无推荐数据 ({detail})，选第一个职业。");
        }
    }
    else
    {
        Log($"[Arena] 盒子连接失败 ({detail ?? "null"})，选第一个职业。");
    }

    TrySendAndReceiveExpected(pipe, $"ARENA_PICK_HERO:{bestIndex}", 5000,
        out var pickResp, "OK", "Arena.PickHero");
    Log($"[Arena] 选职业结果: {pickResp}");
}
```

- [ ] **Step 6: 实现 ArenaPickCard（跟随盒子推荐）**

```csharp
private void ArenaPickCard(PipeServer pipe)
{
    // 从盒子读取推荐
    var draftBridge = _hsBoxArenaDraftBridge;
    int bestIndex = 0;

    if (draftBridge != null && draftBridge.TryReadDraft(out var rec, out var detail))
    {
        if (rec.GuideList != null && rec.GuideList.Count > 0)
        {
            double bestScore = double.MinValue;
            for (int i = 0; i < rec.GuideList.Count; i++)
            {
                var item = rec.GuideList[i];
                double score = item.Value<double?>("score")
                            ?? item.Value<double?>("value")
                            ?? item.Value<double?>("winRate")
                            ?? 0;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            Log($"[Arena] 盒子推荐卡牌 index={bestIndex}, score={bestScore:F1}, stage={rec.CardStage}");
        }
        else
        {
            Log($"[Arena] 盒子无选牌推荐 ({detail})，选第一张卡。");
        }
    }
    else
    {
        Log($"[Arena] 盒子连接失败 ({detail ?? "null"})，选第一张卡。");
    }

    TrySendAndReceiveExpected(pipe, $"ARENA_PICK_CARD:{bestIndex}", 5000,
        out var pickResp, "OK", "Arena.PickCard");
    Log($"[Arena] 选牌结果: {pickResp}");
}
```

- [ ] **Step 7: 实现 ArenaQueueAndPlay**

核心对局循环，复用现有构筑模式的主循环逻辑：

```csharp
private void ArenaQueueAndPlay(PipeServer pipe)
{
    Log("[Arena] 选牌完成，开始排队...");

    // 点击开始匹配
    TrySendAndReceiveExpected(pipe, "CLICK_PLAY", 5000, out _, "OK", "Arena.ClickPlay");
    SleepOrCancelled(2000);

    // 等待进入对局（复用现有等待逻辑）
    var matchStarted = DateTime.UtcNow;
    while (_running && !_finishAfterGame)
    {
        if ((DateTime.UtcNow - matchStarted).TotalSeconds > _matchmakingTimeoutSeconds)
        {
            Log("[Arena] 排队超时，重试...");
            TrySendAndReceiveExpected(pipe, "CLICK_PLAY", 5000, out _, "OK", "Arena.RetryQueue");
            matchStarted = DateTime.UtcNow;
            SleepOrCancelled(2000);
            continue;
        }

        if (!TryGetSceneValue(pipe, 3000, out var scene, "Arena.WaitGame"))
        {
            SleepOrCancelled(1000);
            continue;
        }

        if (string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
        {
            Log("[Arena] 已进入对局！");
            break;
        }

        SleepOrCancelled(2000);
    }

    if (!_running || _finishAfterGame) return;

    // 对局主循环 — 复用现有构筑对局逻辑
    // 调用现有的对局处理方法（留牌、出牌、结束回合等）
    RunConstructedGameLoop(pipe);

    // 对局结束处理
    Log("[Arena] 对局结束，处理结算...");
    ArenaPostGameSettle(pipe);
}
```

**注意：** `RunConstructedGameLoop` 是对现有构筑对局逻辑的复用。实现时需要将现有 `MainLoopReconnect` 标签之后的构筑对局循环提取为独立方法，或者直接在 `ArenaQueueAndPlay` 中内联复用。具体取决于现有代码的组织方式。一种替代方案是直接复用现有的种子轮询→推荐→执行循环。

- [ ] **Step 8: 实现 ArenaPostGameSettle**

```csharp
private void ArenaPostGameSettle(PipeServer pipe)
{
    // 关闭结算弹窗
    for (int i = 0; i < 5; i++)
    {
        TrySendStatusCommand(pipe, "CLICK_DISMISS", 2000, out _, "Arena.Dismiss");
        SleepOrCancelled(1500);

        if (TryGetSceneValue(pipe, 3000, out var scene, "Arena.PostGame"))
        {
            if (!string.Equals(scene, "GAMEPLAY", StringComparison.OrdinalIgnoreCase))
            {
                Log($"[Arena] 已离开对局界面: {scene}");
                break;
            }
        }
    }

    SleepOrCancelled(2000);
}
```

- [ ] **Step 9: 实现 ArenaClaimRewards**

```csharp
private void ArenaClaimRewards(PipeServer pipe)
{
    Log("[Arena] 领取奖励...");
    TrySendAndReceiveExpected(pipe, "ARENA_CLAIM_REWARDS", 10000,
        out var claimResp, "OK", "Arena.Claim");
    Log($"[Arena] 领奖结果: {claimResp}");

    // 多次尝试关闭弹窗
    for (int i = 0; i < 3; i++)
    {
        SleepOrCancelled(2000);
        TrySendStatusCommand(pipe, "CLICK_DISMISS", 2000, out _, "Arena.ClaimDismiss");
    }
}
```

- [ ] **Step 10: 添加配置字段和初始化**

在 `BotService` 类中添加竞技场相关字段：

```csharp
private bool _arenaUseGold;
private int _arenaGoldReserve;
private HsBoxArenaDraftBridge _hsBoxArenaDraftBridge;
```

在 `SetRunConfiguration` 或适当的初始化位置创建 Bridge：

```csharp
if (serviceMode == 2)  // Arena
{
    _hsBoxArenaDraftBridge = new HsBoxArenaDraftBridge(Log);
}
```

添加公开方法供 MainViewModel 调用：

```csharp
public void SetArenaUseGold(bool value) => _arenaUseGold = value;
public void SetArenaGoldReserve(int value) => _arenaGoldReserve = value;
```

在 `MainViewModel.OnMainButton()` 中启动时传递配置：

```csharp
if (IsArenaMode)
{
    _bot.SetArenaUseGold(ArenaUseGold);
    _bot.SetArenaGoldReserve(ArenaGoldReserve);
}
```

- [ ] **Step 11: 提交**

```bash
git add BotMain/BotService.cs BotMain/HsBoxRecommendationProvider.cs BotMain/MainViewModel.cs
git commit -m "feat(arena): 竞技场主循环状态机（买票、选牌、排队、对局、结算完整循环）"
```

---

### Task 6: 集成验证与调试

**Files:** 所有上述文件

- [ ] **Step 1: 编译验证**

```bash
cd BotMain && dotnet build
cd ../HearthstonePayload && dotnet build
```

修复所有编译错误。

- [ ] **Step 2: 手动测试清单**

逐项在运行的炉石中验证：

1. UI 中能看到 Arena 模式选项
2. 设置窗口中能看到竞技场设置区
3. 选择 Arena 模式启动后，日志显示 `[Arena] ── 竞技场模式启动 ──`
4. `ARENA_GET_STATUS` 正确返回当前状态
5. `ARENA_GET_TICKET_INFO` 正确返回票数和金币
6. 盒子 CEF hook 成功（日志无 `hook_not_installed`）
7. 选职业/选牌跟随盒子推荐
8. 对局内出牌使用 `onUpdateArenaRecommend` 回调
9. 对局结束后正确结算和重开

- [ ] **Step 3: 修复 DraftManager 反射字段名**

Payload 端的 `GetArenaStatus`、`GetArenaDraftChoices` 等方法中使用的反射字段名（`m_currentMode`、`m_choices` 等）可能不准确。需要在游戏运行时通过调试输出确认实际字段名，然后修正代码。

方法：在 `GetArenaStatus` 方法中临时加入 `DumpMembers` 调用，读取 `DraftManager` 实例的所有字段名，根据输出修正代码。

- [ ] **Step 4: 修复 guideList 数据结构**

盒子 `fRecommend` 推送的 `guideList` 的具体字段名（评分字段是 `score`、`value`、`winRate` 还是其他）需要通过实际 CDP 抓包确认。在 `ArenaPickCard` 中添加临时日志输出 `guideList` 的 JSON 内容来确认。

- [ ] **Step 5: 最终提交**

```bash
git add -A
git commit -m "fix(arena): 修复竞技场反射字段名和盒子数据结构适配"
```

---

## 实现顺序与依赖

```
Task 1 (UI/Config) ──┐
                      ├──→ Task 5 (主循环) ──→ Task 6 (集成验证)
Task 2 (Payload)  ────┤
Task 3 (选牌Bridge) ──┤
Task 4 (对局Bridge) ──┘
```

- Task 1-4 互相独立，可并行实现
- Task 5 依赖 Task 1-4 全部完成
- Task 6 依赖 Task 5 完成
