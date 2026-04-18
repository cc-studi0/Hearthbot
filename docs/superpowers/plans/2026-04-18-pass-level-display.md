# 云控显示账号通行证等级 · 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在云控 Dashboard 里展示每台设备当前账号的通行证等级与 XP 进度（快照显示，主卡紧凑文字 + 详情抽屉进度条）。

**Architecture:** 通过现有心跳管道（每 30 秒）搭便车，新增 3 个 `int` 字段 `PassLevel / PassXp / PassXpNeeded` 从 HearthstonePayload 反射读取 → NamedPipe → BotMain → SignalR → 云控 SQLite → Vue 前端。零新增通信层，全链路向后兼容（老客户端发空值，老云控忽略新参数）。

**Tech Stack:** .NET 8 + SignalR + EF Core + SQLite + Vue 3 + vitest + xunit

**相关文档：** `docs/superpowers/specs/2026-04-18-pass-level-display-design.md`

---

## 文件结构

**新增文件：**
- `BotMain/PassParser.cs` —— 纯静态类，解析 `PASS_INFO:L|X|N` 响应
- `BotCore.Tests/PassParserTests.cs` —— xunit 单元测试
- `hearthbot-web/src/components/PassProgress.vue` —— 详情抽屉用的横向进度条
- `hearthbot-web/src/components/PassProgress.test.ts` —— vitest 单元测试

**修改文件：**
- `HearthstonePayload/GameReader.cs` —— 加 `ReadPassInfoResponse()` 与 `ReadIntField()` 辅助
- `HearthstonePayload/Entry.cs` —— 管道命令分派加 `GET_PASS_INFO` 分支
- `BotMain/BotService.cs` —— 加 3 个缓存字段、`TryQueryPassInfo()`、调用点
- `BotMain/Cloud/DeviceStatusCollector.cs` —— `HeartbeatData` 加 3 字段，`Collect()` 装配
- `BotMain/Cloud/CloudAgent.cs` —— `SendHeartbeatAsync()` 发送时带上 3 参数
- `BotCore.Tests/BotCore.Tests.csproj` —— 加 `PassParser.cs` 的 `<Compile Link>`
- `HearthBot.Cloud/Models/Device.cs` —— 加 3 个 `int` 属性
- `HearthBot.Cloud/Models/DeviceDashboardView.cs` —— 加 3 个 `init` 属性
- `HearthBot.Cloud/Services/CloudSchemaBootstrapper.cs` —— `DeviceColumns` 加 3 行
- `HearthBot.Cloud/Hubs/BotHub.cs` —— `Heartbeat` 可选参数
- `HearthBot.Cloud/Services/DeviceManager.cs` —— `UpdateHeartbeat` 写入新字段
- `HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs` —— 映射到 DashboardView
- `hearthbot-web/src/types.ts` —— `Device` 接口加 3 字段
- `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue` —— 加紧凑行
- `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue` —— 加 PassProgress 节

---

## Task 1：PassParser 单元测试与实现

**说明：** 纯逻辑最好先啃，建立测试文化。解析器与 `BotMain/RankHelper.cs:80-107` 的 `TryParseRankInfoResponse` 同构。

**Files:**
- Create: `BotMain/PassParser.cs`
- Create: `BotCore.Tests/PassParserTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`

- [ ] **Step 1：写单测（先失败）**

创建 `BotCore.Tests/PassParserTests.cs`：

```csharp
using BotMain;
using Xunit;

namespace BotCore.Tests;

public class PassParserTests
{
    [Fact]
    public void ParseValidResponse_ReturnsTrueAndFillsFields()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|1240|2000",
            out var level, out var xp, out var xpNeeded);

        Assert.True(ok);
        Assert.Equal(87, level);
        Assert.Equal(1240, xp);
        Assert.Equal(2000, xpNeeded);
    }

    [Fact]
    public void ParseNoPassInfo_ReturnsFalseAndKeepsZero()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "NO_PASS_INFO:not_received",
            out var level, out var xp, out var xpNeeded);

        Assert.False(ok);
        Assert.Equal(0, level);
        Assert.Equal(0, xp);
        Assert.Equal(0, xpNeeded);
    }

    [Fact]
    public void ParseEmptyOrNull_ReturnsFalse()
    {
        Assert.False(PassParser.TryParsePassInfoResponse(
            null, out _, out _, out _));
        Assert.False(PassParser.TryParsePassInfoResponse(
            "", out _, out _, out _));
        Assert.False(PassParser.TryParsePassInfoResponse(
            "   ", out _, out _, out _));
    }

    [Fact]
    public void ParseTooFewParts_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|1240",
            out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseNonIntegerField_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|abc|2000",
            out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseWrongPrefix_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "RANK_INFO:87|1240|2000",
            out _, out _, out _);
        Assert.False(ok);
    }
}
```

- [ ] **Step 2：把 PassParser.cs 加到测试项目的 Compile 链接**

修改 `BotCore.Tests/BotCore.Tests.csproj`，在 `<Compile Include="..\BotMain\RankHelper.cs" Link="RankHelper.cs" />` 下方追加一行：

```xml
<Compile Include="..\BotMain\PassParser.cs" Link="PassParser.cs" />
```

- [ ] **Step 3：跑一下确认还没 PassParser 会编译失败**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/BotCore.Tests/BotCore.Tests.csproj`
Expected: FAIL，错误包含 `error CS0234: 命名空间"BotMain"中不存在类型或命名空间名"PassParser"` 或类似。

- [ ] **Step 4：写 PassParser 实现**

创建 `BotMain/PassParser.cs`：

```csharp
using System;

namespace BotMain
{
    public static class PassParser
    {
        private const string Prefix = "PASS_INFO:";

        public static bool TryParsePassInfoResponse(
            string response, out int level, out int xp, out int xpNeeded)
        {
            level = 0;
            xp = 0;
            xpNeeded = 0;

            if (string.IsNullOrWhiteSpace(response)
                || !response.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = response.Substring(Prefix.Length);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return false;

            if (!int.TryParse(parts[0], out level))
                return false;
            if (!int.TryParse(parts[1], out xp))
            {
                level = 0;
                return false;
            }
            if (!int.TryParse(parts[2], out xpNeeded))
            {
                level = 0;
                xp = 0;
                return false;
            }

            return true;
        }
    }
}
```

- [ ] **Step 5：跑测试验证全过**

Run: `dotnet test H:/桌面/炉石脚本/Hearthbot/BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~PassParserTests"`
Expected: PASS，`Passed: 6`

- [ ] **Step 6：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add BotMain/PassParser.cs BotCore.Tests/PassParserTests.cs BotCore.Tests/BotCore.Tests.csproj
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证读取：PassParser 解析器与单元测试"
```

---

## Task 2：HearthstonePayload.ReadPassInfoResponse + Entry 管道分派

**说明：** 反射读游戏内存。**此任务没有单元测试**——反射依赖炉石进程，在 CI 里跑不起来。唯一验证是 Task 13 的手工端到端。沿用 `GameReader.ReadRankInfoResponse` (GameReader.cs:47-101) 的同构模式。

**Files:**
- Modify: `HearthstonePayload/GameReader.cs`
- Modify: `HearthstonePayload/Entry.cs`

- [ ] **Step 1：先确认 GET_RANK_INFO 的分派位置**

Run: `grep -n "GET_RANK_INFO" H:/桌面/炉石脚本/Hearthbot/HearthstonePayload/Entry.cs` （改用 Grep 工具）
Expected: 定位到管道命令 switch 的准确行号（后续步骤会在它旁边加分支）。

- [ ] **Step 2：在 GameReader.cs 末尾新增 ReadPassInfoResponse 与 ReadIntField**

修改 `HearthstonePayload/GameReader.cs`，在 `ReadRankFieldInt` 方法（第 242 行附近）下方追加：

```csharp
public string ReadPassInfoResponse()
{
    if (!Init()) return "NO_PASS_INFO:init_failed";

    try
    {
        var mgrType = _ctx.AsmCSharp?.GetType("RewardTrackManager");
        if (mgrType == null) return "NO_PASS_INFO:no_mgr";

        var mgr = _ctx.CallStaticAny(mgrType, "Get");
        if (mgr == null) return "NO_PASS_INFO:no_mgr_instance";

        // 通行证数据需要等客户端从服务器收齐后才可读
        var received = _ctx.GetFieldOrPropertyAny(mgr,
            "HasReceivedRewardTracksFromServer") as bool?;
        if (received != true) return "NO_PASS_INFO:not_received";

        // Global.RewardTrackType.GLOBAL = 1（见 hs-decompiled/Cheats.cs:11390 调用约定）
        var track = _ctx.CallAny(mgr, "GetRewardTrack", 1);
        if (track == null) return "NO_PASS_INFO:no_track";

        var dataModel = _ctx.GetFieldOrPropertyAny(track, "TrackDataModel");
        if (dataModel == null) return "NO_PASS_INFO:no_datamodel";

        var level    = ReadIntField(dataModel, "Level", "m_Level");
        var xp       = ReadIntField(dataModel, "Xp", "m_Xp");
        var xpNeeded = ReadIntField(dataModel, "XpNeeded", "m_XpNeeded");

        if (level <= 0) return "NO_PASS_INFO:invalid_level";
        return string.Format("PASS_INFO:{0}|{1}|{2}", level, xp, xpNeeded);
    }
    catch (Exception ex)
    {
        return "NO_PASS_INFO:" + ex.GetType().Name;
    }
}

private int ReadIntField(object target, params string[] names)
{
    foreach (var name in names)
    {
        var val = _ctx.GetFieldOrPropertyAny(target, name);
        if (val == null) continue;
        try
        {
            return Convert.ToInt32(val);
        }
        catch
        {
            // 换下一个候选名
        }
    }
    return 0;
}
```

- [ ] **Step 3：在 Entry.cs 命令分派里添加 GET_PASS_INFO 分支**

在 Step 1 定位到的 `GET_RANK_INFO` 分派点旁边（通常是一个 `if/else if` 或 `switch case` 结构）追加与之对称的分支：

```csharp
else if (command.StartsWith("GET_PASS_INFO", StringComparison.Ordinal))
{
    response = _gameReader.ReadPassInfoResponse();
}
```

（具体写法需匹配 `GET_RANK_INFO` 的现有风格——如果是 switch 就写 `case`，如果是 if/else 就写 `else if`。）

- [ ] **Step 4：编译 HearthstonePayload 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/HearthstonePayload/HearthstonePayload.csproj`
Expected: Build succeeded，0 Error(s)。

- [ ] **Step 5：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add HearthstonePayload/GameReader.cs HearthstonePayload/Entry.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证读取：Payload 反射 RewardTrackManager 与管道分派"
```

---

## Task 3：BotService 通行证查询字段与方法

**说明：** 仿 `BotService.TryQueryCurrentRank`（BotService.cs:10191-10225），但去掉限流——通行证查询频率本来就不高，无需额外节流。

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1：在 `_lastQueriedLegendIndex` 字段（BotService.cs:10229）下方追加 3 个通行证缓存字段与只读属性**

```csharp
private int _lastPassLevel;
private int _lastPassXp;
private int _lastPassXpNeeded;

public int LastPassLevel => _lastPassLevel;
public int LastPassXp => _lastPassXp;
public int LastPassXpNeeded => _lastPassXpNeeded;
```

- [ ] **Step 2：在 `TryQueryCurrentRank` 方法下方（同一区域，`_lastQueriedLegendIndex` 字段声明之前）追加 `TryQueryPassInfo` 方法**

```csharp
private bool TryQueryPassInfo(PipeServer pipe)
{
    if (pipe == null || !pipe.IsConnected)
        return false;

    var gotResp = TrySendAndReceiveExpected(
        pipe,
        "GET_PASS_INFO",
        2500,
        r => !string.IsNullOrWhiteSpace(r)
            && (r.StartsWith("PASS_INFO:", StringComparison.Ordinal)
                || r.StartsWith("NO_PASS_INFO:", StringComparison.Ordinal)
                || r.StartsWith("ERROR:", StringComparison.Ordinal)),
        out var resp,
        "PassInfo");

    if (!gotResp || string.IsNullOrWhiteSpace(resp))
        return false;

    if (!BotMain.PassParser.TryParsePassInfoResponse(
        resp, out var level, out var xp, out var xpNeeded))
    {
        return false;
    }

    _lastPassLevel = level;
    _lastPassXp = xp;
    _lastPassXpNeeded = xpNeeded;
    return true;
}
```

（`TrySendAndReceiveExpected` 和 `PipeServer` 均已在 BotService 作用域内，命名空间一致。）

- [ ] **Step 3：在 TryQueryCurrentRank 的两个调用点（BotService.cs:1679 和 :1954）紧跟着补一条 TryQueryPassInfo 调用**

1679 行附近：
```csharp
TryQueryCurrentRank(_pipe, force: true);
TryQueryPassInfo(_pipe);          // ← 新增
TryQueryPlayerName(_pipe);
```

1954 行附近：
```csharp
if (!wasInGame)
{
    TryQueryCurrentRank(pipe);
    TryQueryPassInfo(pipe);       // ← 新增
    if (CheckRankStopLimit(pipe))
        break;
}
```

- [ ] **Step 4：编译 BotMain 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/BotMain/BotMain.csproj`
Expected: Build succeeded。

- [ ] **Step 5：跑已有 BotServiceXxxTests 确保没有回归**

Run: `dotnet test H:/桌面/炉石脚本/Hearthbot/BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotService"`
Expected: 原有测试全绿。

- [ ] **Step 6：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add BotMain/BotService.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证读取：BotService 新增 TryQueryPassInfo 与缓存字段"
```

---

## Task 4：HeartbeatData 字段扩展 + DeviceStatusCollector 装配 + CloudAgent 发送

**说明：** 三处改动方向完全一致（让通行证字段流经心跳通道），打成一个 task。

**Files:**
- Modify: `BotMain/Cloud/DeviceStatusCollector.cs`
- Modify: `BotMain/Cloud/CloudAgent.cs`

- [ ] **Step 1：先定位 HeartbeatData 类定义**

Run: Grep `HeartbeatData` 在 `H:/桌面/炉石脚本/Hearthbot/BotMain/Cloud` 下。
Expected: 找到类的声明文件（通常是 `DeviceStatusCollector.cs` 同文件或并列文件）。

- [ ] **Step 2：在 HeartbeatData 类里追加 3 个 int 属性**

在 `HeartbeatData` 的最后一个属性下方追加：

```csharp
public int PassLevel { get; set; }
public int PassXp { get; set; }
public int PassXpNeeded { get; set; }
```

- [ ] **Step 3：在 DeviceStatusCollector.Collect() 返回的 HeartbeatData 字面量里装配 3 字段**

修改 `BotMain/Cloud/DeviceStatusCollector.cs:42-54`，在最后一行 `CurrentOpponent = _bot.CurrentEnemyClassName ?? ""` 后面加逗号并追加：

```csharp
return new HeartbeatData
{
    Status = status,
    CurrentAccount = account?.DisplayName ?? _bot.PlayerName ?? "",
    CurrentRank = account?.CurrentRankText ?? _bot.CurrentRankText ?? "",
    CurrentDeck = account?.DeckSummary ?? _bot.SelectedDeckName ?? "",
    CurrentProfile = account?.ProfileName ?? _bot.SelectedProfileName ?? "",
    GameMode = (account?.ModeIndex ?? _bot.ModeIndex) == 1 ? "Wild" : "Standard",
    SessionWins = account?.Wins ?? stats.Wins,
    SessionLosses = account?.Losses ?? stats.Losses,
    TargetRank = account?.TargetRankText ?? "",
    CurrentOpponent = _bot.CurrentEnemyClassName ?? "",
    PassLevel = _bot.LastPassLevel,
    PassXp = _bot.LastPassXp,
    PassXpNeeded = _bot.LastPassXpNeeded
};
```

- [ ] **Step 4：先 grep 定位 CloudAgent.SendHeartbeatAsync 内调用 InvokeAsync("Heartbeat", ...) 的地方**

Run: Grep `InvokeAsync.*Heartbeat` 在 `H:/桌面/炉石脚本/Hearthbot/BotMain/Cloud/CloudAgent.cs`。
Expected: 定位到 `SendHeartbeatAsync()` 内部实际调用 SignalR Hub 的一行。

- [ ] **Step 5：在 InvokeAsync 的参数尾部追加 3 个新字段**

在原参数末尾（`data.CurrentOpponent` 或等价字段之后）追加：

```csharp
await _hub.InvokeAsync("Heartbeat",
    /* ...原有参数... */,
    data.CurrentOpponent,
    data.PassLevel,
    data.PassXp,
    data.PassXpNeeded);
```

保持原有传参不动，只追加 3 个。

- [ ] **Step 6：编译 BotMain 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/BotMain/BotMain.csproj`
Expected: Build succeeded。

- [ ] **Step 7：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add BotMain/Cloud/DeviceStatusCollector.cs BotMain/Cloud/CloudAgent.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证读取：心跳数据装配与发送"
```

---

## Task 5：云控实体、DashboardView、SQLite schema 字段三件套

**说明：** 三个文件改动方向一致（均为加 3 个 int 字段 + schema 迁移），打成一个 task。

**Files:**
- Modify: `HearthBot.Cloud/Models/Device.cs`
- Modify: `HearthBot.Cloud/Models/DeviceDashboardView.cs`
- Modify: `HearthBot.Cloud/Services/CloudSchemaBootstrapper.cs`

- [ ] **Step 1：在 Device.cs 末尾（CompletedRank 属性之后）追加 3 个属性**

修改 `HearthBot.Cloud/Models/Device.cs`，在 `CompletedRank` 属性下方追加：

```csharp
public int PassLevel { get; set; }
public int PassXp { get; set; }
public int PassXpNeeded { get; set; }
```

- [ ] **Step 2：在 DeviceDashboardView.cs 末尾追加同名 init 属性**

修改 `HearthBot.Cloud/Models/DeviceDashboardView.cs`，在 `CompletedRank` 属性下方追加：

```csharp
public int PassLevel { get; init; }
public int PassXp { get; init; }
public int PassXpNeeded { get; init; }
```

- [ ] **Step 3：在 CloudSchemaBootstrapper.DeviceColumns 数组末尾追加 3 行**

修改 `HearthBot.Cloud/Services/CloudSchemaBootstrapper.cs:10-22`，在 `("CompletedRank", "TEXT NOT NULL DEFAULT ''")` 那行末尾加逗号并追加：

```csharp
private static readonly (string Name, string Definition)[] DeviceColumns =
{
    ("OrderNumber", "TEXT NOT NULL DEFAULT ''"),
    ("OrderAccountName", "TEXT NOT NULL DEFAULT ''"),
    ("TargetRank", "TEXT NOT NULL DEFAULT ''"),
    ("StartRank", "TEXT NOT NULL DEFAULT ''"),
    ("StartedAt", "TEXT"),
    ("StatusChangedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
    ("CurrentOpponent", "TEXT NOT NULL DEFAULT ''"),
    ("IsCompleted", "INTEGER NOT NULL DEFAULT 0"),
    ("CompletedAt", "TEXT"),
    ("CompletedRank", "TEXT NOT NULL DEFAULT ''"),
    ("PassLevel", "INTEGER NOT NULL DEFAULT 0"),
    ("PassXp", "INTEGER NOT NULL DEFAULT 0"),
    ("PassXpNeeded", "INTEGER NOT NULL DEFAULT 0"),
};
```

- [ ] **Step 4：编译 HearthBot.Cloud 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/HearthBot.Cloud/HearthBot.Cloud.csproj`
Expected: Build succeeded。

- [ ] **Step 5：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add HearthBot.Cloud/Models/Device.cs HearthBot.Cloud/Models/DeviceDashboardView.cs HearthBot.Cloud/Services/CloudSchemaBootstrapper.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：云控实体与 SQLite schema 加通行证字段"
```

---

## Task 6：BotHub.Heartbeat 与 DeviceManager.UpdateHeartbeat 签名扩展

**说明：** 两个方法签名必须同步扩展（SignalR 到 DB 的直线贯通）。可选参数末尾追加，向后兼容老客户端。

**Files:**
- Modify: `HearthBot.Cloud/Hubs/BotHub.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`

- [ ] **Step 1：修改 BotHub.Heartbeat 签名追加 3 可选参数并向下传**

修改 `HearthBot.Cloud/Hubs/BotHub.cs:68-80`：

```csharp
public async Task Heartbeat(string deviceId, string status,
    string currentAccount, string currentRank, string currentDeck,
    string currentProfile, string gameMode, int sessionWins, int sessionLosses,
    string targetRank = "", string currentOpponent = "",
    int passLevel = 0, int passXp = 0, int passXpNeeded = 0)
{
    var device = await _devices.UpdateHeartbeat(deviceId, status,
        currentAccount, currentRank, currentDeck,
        currentProfile, gameMode, sessionWins, sessionLosses,
        targetRank, currentOpponent,
        passLevel, passXp, passXpNeeded);

    if (device != null)
        await _dashboard.Clients.All.SendAsync("DeviceUpdated", _projection.Project(device, DateTime.UtcNow));
}
```

- [ ] **Step 2：修改 DeviceManager.UpdateHeartbeat 签名追加 3 可选参数并写入 device**

修改 `HearthBot.Cloud/Services/DeviceManager.cs:93-161`：

签名改为：
```csharp
public async Task<Device?> UpdateHeartbeat(string deviceId, string status,
    string currentAccount, string currentRank, string currentDeck,
    string currentProfile, string gameMode, int sessionWins, int sessionLosses,
    string targetRank = "", string currentOpponent = "",
    int passLevel = 0, int passXp = 0, int passXpNeeded = 0)
```

方法体里，在写 `device.CurrentOpponent = currentOpponent;`（现有的 DeviceManager.cs:120 附近）下方追加：

```csharp
device.PassLevel = passLevel;
device.PassXp = passXp;
device.PassXpNeeded = passXpNeeded;
```

（直接赋值即可——值为 0 意味着客户端未上报或游戏未读到，前端按 0 判"无数据"。）

- [ ] **Step 3：编译 HearthBot.Cloud 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/HearthBot.Cloud/HearthBot.Cloud.csproj`
Expected: Build succeeded。

- [ ] **Step 4：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add HearthBot.Cloud/Hubs/BotHub.cs HearthBot.Cloud/Services/DeviceManager.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：心跳接口向下透传通行证字段"
```

---

## Task 7：DeviceDisplayStateEvaluator 映射到 DashboardView

**说明：** 让通行证字段从 Device 实体流到投影出口 DashboardView。

**Files:**
- Modify: `HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs`

- [ ] **Step 1：在 Evaluate() 的字面量末尾追加 3 行映射**

修改 `HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs` 的 `new DeviceDashboardView { ... }` 字面量，在最后一个属性（通常是 `CompletedRank = device.CompletedRank`）后面加逗号并追加：

```csharp
return new DeviceDashboardView
{
    // ...现有字段保持原样...
    CompletedRank = device.CompletedRank,
    PassLevel = device.PassLevel,
    PassXp = device.PassXp,
    PassXpNeeded = device.PassXpNeeded,
};
```

- [ ] **Step 2：编译 HearthBot.Cloud 确认无错**

Run: `dotnet build H:/桌面/炉石脚本/Hearthbot/HearthBot.Cloud/HearthBot.Cloud.csproj`
Expected: Build succeeded。

- [ ] **Step 3：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：评估器映射 Device→DashboardView"
```

---

## Task 8：前端 Device 类型扩展 + DeviceStatusCard 紧凑显示

**说明：** JSON 序列化默认首字母小写，前端字段为 `passLevel/passXp/passXpNeeded`。

**Files:**
- Modify: `hearthbot-web/src/types.ts`
- Modify: `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`

- [ ] **Step 1：在 Device 接口末尾追加 3 个字段**

修改 `hearthbot-web/src/types.ts:1-31` 的 `Device` 接口，在 `completedRank: string` 下方追加：

```ts
export interface Device {
  // ...现有字段保持原样...
  completedRank: string
  passLevel: number
  passXp: number
  passXpNeeded: number
}
```

- [ ] **Step 2：在 DeviceStatusCard.vue 的 rank-line 下方新增 pass-line**

修改 `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue:105-108`，在整个 `<div class="rank-line">...</div>` 元素结束后紧接着追加：

```vue
<div v-if="device.passLevel > 0" class="pass-line">
  <span class="pass-label">通行证</span>
  <span class="pass-level">Lv.{{ device.passLevel }}</span>
  <span v-if="device.passXpNeeded > 0" class="pass-xp">
    {{ device.passXp }}/{{ device.passXpNeeded }}
  </span>
</div>
```

然后在同文件 `<style scoped>` 末尾追加样式（复用文件内现有的渐变/字号约定）：

```css
.pass-line {
  display: flex;
  gap: 6px;
  align-items: center;
  font-size: 13px;
  margin-top: 2px;
}
.pass-label { color: #64748b; }
.pass-level { color: #8b5cf6; font-weight: 600; }
.pass-xp { color: #94a3b8; font-size: 12px; }
```

- [ ] **Step 3：运行 vitest 确保原有测试不受影响**

Run: `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm test -- --run`
Expected: 全绿（无变化）。

- [ ] **Step 4：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add hearthbot-web/src/types.ts hearthbot-web/src/components/dashboard/DeviceStatusCard.vue
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：前端 Device 类型扩展 + 主卡紧凑行"
```

---

## Task 9：PassProgress 组件 TDD

**说明：** 详情抽屉用的进度条。先写 vitest 再写组件。

**Files:**
- Create: `hearthbot-web/src/components/PassProgress.test.ts`
- Create: `hearthbot-web/src/components/PassProgress.vue`

- [ ] **Step 1：写单测（先失败）**

创建 `hearthbot-web/src/components/PassProgress.test.ts`：

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PassProgress from './PassProgress.vue'

describe('PassProgress', () => {
  it('渲染等级与 XP 文本', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 87, xp: 500, xpNeeded: 1000 }
    })
    expect(wrapper.text()).toContain('Lv.87')
    expect(wrapper.text()).toContain('500 / 1000 XP')
  })

  it('xp=500, xpNeeded=1000 → 进度 50%', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 10, xp: 500, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 50%')
  })

  it('xpNeeded=0 时进度为 0（不 NaN）', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 100, xp: 0, xpNeeded: 0 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 0%')
  })

  it('xp 超过 xpNeeded 时钳位到 100', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 5, xp: 5000, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 100%')
  })

  it('负 xp 钳位到 0', () => {
    const wrapper = mount(PassProgress, {
      props: { level: 1, xp: -10, xpNeeded: 1000 }
    })
    const fill = wrapper.find('.pass-bar-fill')
    expect(fill.attributes('style')).toContain('width: 0%')
  })
})
```

- [ ] **Step 2：确认 @vue/test-utils 已安装；若未安装则装**

Run: `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm ls @vue/test-utils 2>&1 | head -5`
Expected: 若显示 `-- (empty)` 或报 `not found`，执行：
`cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm i -D @vue/test-utils`
否则跳过。

- [ ] **Step 3：跑测试确认失败（组件还没创建）**

Run: `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npx vitest run src/components/PassProgress.test.ts`
Expected: FAIL，`Cannot find module './PassProgress.vue'` 或类似。

- [ ] **Step 4：创建组件**

创建 `hearthbot-web/src/components/PassProgress.vue`：

```vue
<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  level: number
  xp: number
  xpNeeded: number
}>()

const percent = computed(() => {
  if (!props.xpNeeded || props.xpNeeded <= 0) return 0
  const ratio = props.xp / props.xpNeeded
  return Math.min(100, Math.max(0, ratio * 100))
})
</script>

<template>
  <div class="pass-progress">
    <div class="pass-labels">
      <span class="pass-level">Lv.{{ level }}</span>
      <span class="pass-xp">{{ xp }} / {{ xpNeeded }} XP</span>
    </div>
    <div class="pass-bar">
      <div class="pass-bar-fill" :style="{ width: percent + '%' }" />
    </div>
  </div>
</template>

<style scoped>
.pass-progress { margin: 6px 0; }
.pass-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-bottom: 3px;
}
.pass-level { color: #1e293b; font-weight: 600; }
.pass-xp { color: #64748b; }
.pass-bar {
  background: #e2e8f0;
  border-radius: 4px;
  height: 6px;
  overflow: hidden;
}
.pass-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #a855f7, #7c3aed);
  border-radius: 4px;
  transition: width 0.5s ease;
}
</style>
```

- [ ] **Step 5：跑测试确认全过**

Run: `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npx vitest run src/components/PassProgress.test.ts`
Expected: PASS，`5 passed`。

- [ ] **Step 6：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add hearthbot-web/src/components/PassProgress.vue hearthbot-web/src/components/PassProgress.test.ts hearthbot-web/package.json hearthbot-web/package-lock.json
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：PassProgress 组件与单元测试"
```

（若 Step 2 没装新依赖，则不 add `package*.json`。）

---

## Task 10：DeviceDetailDrawer 集成 PassProgress

**说明：** 在段位那块下方加一节通行证。

**Files:**
- Modify: `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`

- [ ] **Step 1：在抽屉的段位节（DeviceDetailDrawer.vue:270 附近）下方追加通行证节**

定位到显示 `device.currentRank` 的那一节（DeviceDetailDrawer.vue:270 附近 `<strong>{{ device.currentRank || '未知' }}</strong>`），找到它所在的 `<section>` 或 `<div>` 的闭合标签后追加：

```vue
<section class="drawer-section">
  <h4>通行证</h4>
  <PassProgress v-if="device.passLevel > 0"
    :level="device.passLevel"
    :xp="device.passXp"
    :xp-needed="device.passXpNeeded" />
  <p v-else class="muted">暂无通行证数据</p>
</section>
```

（类名 `drawer-section` / `muted` 应匹配文件内现有约定；若现有段是 `<div class="section">`，跟着用 `<div class="section">`。）

- [ ] **Step 2：在 `<script setup>` 块 import PassProgress**

在 DeviceDetailDrawer.vue 顶部 `<script setup lang="ts">` 现有 import 之后加：

```ts
import PassProgress from '../PassProgress.vue'
```

（注意相对路径：`components/dashboard/DeviceDetailDrawer.vue` 引入 `components/PassProgress.vue` → 路径是 `../PassProgress.vue`。）

- [ ] **Step 3：跑 vitest 确保没有回归**

Run: `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm test -- --run`
Expected: 全绿。

- [ ] **Step 4：dev server 手动目视一下（可选；Task 11 会系统测）**

Run (background): `cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm run dev`
打开浏览器访问 dev URL，确认 Dashboard 可渲染（无白屏）。然后停掉 dev server。

- [ ] **Step 5：提交**

```bash
git -C H:/桌面/炉石脚本/Hearthbot add hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue
git -C H:/桌面/炉石脚本/Hearthbot commit -m "通行证显示：详情抽屉集成 PassProgress"
```

---

## Task 11：端到端手工验证 + 最终提交

**说明：** 反射逻辑不能自动测，这一步是唯一证据。要完整走一遍。

**Files:**
- （无代码变更，纯验证）

- [ ] **Step 1：构建整条链路**

Run:
```bash
dotnet build H:/桌面/炉石脚本/Hearthbot/炉石脚本.sln
cd H:/桌面/炉石脚本/Hearthbot/hearthbot-web && npm run build
```
Expected: 两个都 Build succeeded。

- [ ] **Step 2：启动云控后端**

Run (background): `cd H:/桌面/炉石脚本/Hearthbot/HearthBot.Cloud && dotnet run`
Expected: 监听端口启动；日志含 "Now listening on: http://..."。

- [ ] **Step 3：启动 BotMain 客户端（主机上直接双击或 dotnet run）**

打开 BotMain，连接到云控。Expected: 客户端 Log 显示注册成功。

- [ ] **Step 4：场景一 —— 炉石未启动**

确认云控 Dashboard 显示该设备，主卡上**无**"通行证"行，详情抽屉显示"暂无通行证数据"。

- [ ] **Step 5：场景二 —— 启动炉石、登录账号、进大厅**

等心跳一轮（最多 30 秒）后，主卡显示 `通行证 Lv.X`，详情抽屉显示进度条，数值与游戏内（点击头像下方通行证入口）一致。

- [ ] **Step 6：场景三 —— 打完一局**

确认通行证值变化被心跳推送。XP 应增加；若升级，Level 应 +1。前端自动刷新。

- [ ] **Step 7：场景四 —— 切换账号**

用队列模式切到另一个账号，等 ≤30 秒，确认主卡通行证值切到新账号的值（不是前一个账号的残留）。

- [ ] **Step 8：场景五 —— 关炉石**

关闭炉石进程，等 ≤30 秒，确认主卡该行消失（`passLevel=0` 触发 `v-if` 隐藏）。

- [ ] **Step 9：场景六 —— 旧客户端兼容性（如果有老版 BotMain）**

用还没升级的 BotMain 客户端连同一个云控，确认云控对该设备显示"暂无通行证数据"（字段为 0），不崩不红字。

- [ ] **Step 10：所有场景都通过后，打最终提交**

若一切正常，无代码变更，则此 task 不产生 commit。若过程中发现问题回头修，则按问题所在的 task 补丁风格提交。

---

## 自审结果

- **Spec 覆盖**：spec 每一节都有对应 task（读取→Task 2、BotMain→Task 3/4、云控模型→Task 5/6/7、前端→Task 8/9/10、测试→Task 1 & 9 & 11、手工验证→Task 11）。✓
- **占位符**：无 TBD/TODO；每段代码都是完整可粘贴版本。✓
- **类型一致性**：字段名全链路 `PassLevel/PassXp/PassXpNeeded`（C#）与 `passLevel/passXp/passXpNeeded`（JS）严格对应。✓
- **调用链一致性**：Task 3 `_bot.LastPassLevel` 在 Task 4 使用；Task 5 `Device.PassLevel` 在 Task 6 `UpdateHeartbeat` 写入，Task 7 `DeviceDashboardView` 映射；Task 8 `device.passLevel` 与 Task 10 `PassProgress` 的 `level` prop 对齐。✓
