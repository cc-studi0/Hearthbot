# 云控显示账号通行证等级 · 设计文档

- 日期：2026-04-18
- 范围：HearthstonePayload / BotMain / HearthBot.Cloud / hearthbot-web 全链路
- 状态：待用户复核

---

## 目标

在云控 Dashboard 里展示每台设备当前账号的通行证（Reward Track）等级与 XP 进度，帮助主人一眼判断每个账号的冲级情况。

## 范围与非目标

**在本次范围内**：

- 读取炉石内通行证当前等级、当前 XP、升级所需 XP
- 通过现有心跳管道上报至云控
- 云控主卡片显示紧凑等级标签，详情抽屉显示带进度条的完整版

**明确不做（YAGNI）**：

- 不读取 XP 加成%、付费版标识、赛季结束剩余时间等附加字段
- 不做"今日涨幅"等跨时段对比
- 不做专门的通行证历史记录表
- 不在账号切换时做特殊事件处理（下次心跳自动矫正）
- 不新增 HTTP API，直接复用 SignalR `DeviceUpdated` 通道

## 需求定义

| 字段 | 示例 | 来源 |
| --- | --- | --- |
| 等级（PassLevel） | `87` | `RewardTrackDataModel.Level` |
| 当前 XP（PassXp） | `1240` | `RewardTrackDataModel.Xp` |
| 升级所需 XP（PassXpNeeded） | `2000` | `RewardTrackDataModel.XpNeeded` |

展示规则：

- 主卡片：一行 `通行证 Lv.87 1240/2000`，紧凑文字
- 详情抽屉：`PassProgress` 组件，横向进度条 + 等级/XP 标签
- `PassLevel === 0` 判定为"无数据"，主卡片整行不渲染，抽屉显示"暂无通行证数据"

## 架构与数据流

```
HearthstonePayload (注入炉石进程)
  └─ GameReader.ReadPassInfoResponse()
       ↑ 反射 RewardTrackManager.Get()
         .GetRewardTrack(Global.RewardTrackType.GLOBAL = 1)
         .TrackDataModel → {Level, Xp, XpNeeded}

           ▼ (NamedPipe, 命令 "GET_PASS_INFO")

BotMain
  ├─ PassParser.TryParsePassInfoResponse()
  ├─ BotService.TryQueryPassInfo()         ← 在已有 TryQueryCurrentRank 调用点附近并排发起
  ├─ BotService._lastPassLevel/_lastPassXp/_lastPassXpNeeded
  └─ DeviceStatusCollector.Collect()       ← 每 30 秒心跳
       └─ HeartbeatData { …, PassLevel, PassXp, PassXpNeeded }

           ▼ (SignalR, BotHub.Heartbeat)

HearthBot.Cloud
  ├─ Device (EF 实体, 新增 3 字段 int)       ← SQLite 持久化
  ├─ DeviceManager.UpdateHeartbeat(...)    ← 写入 device.PassLevel/...
  ├─ DeviceDisplayStateEvaluator.Evaluate  ← 映射到 DashboardView
  └─ SignalR 推送 DeviceDashboardView (现有 DeviceUpdated 事件)

           ▼

hearthbot-web (Vue)
  ├─ types.ts: Device 新增 passLevel/passXp/passXpNeeded
  ├─ DeviceStatusCard.vue: 紧凑一行
  └─ DeviceDetailDrawer.vue + PassProgress.vue: 进度条
```

## 组件清单

### 1. HearthstonePayload

**新增方法** `GameReader.ReadPassInfoResponse()`：

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

        var received = _ctx.GetFieldOrPropertyAny(mgr,
            "HasReceivedRewardTracksFromServer") as bool?;
        if (received != true) return "NO_PASS_INFO:not_received";

        // Global.RewardTrackType.GLOBAL 枚举值 = 1（来源：
        // hs-decompiled/Cheats.cs 及 JournalPopup.cs 的使用约定）
        var track = _ctx.CallAny(mgr, "GetRewardTrack", 1);
        if (track == null) return "NO_PASS_INFO:no_track";

        var dataModel = _ctx.GetFieldOrPropertyAny(track, "TrackDataModel");
        if (dataModel == null) return "NO_PASS_INFO:no_datamodel";

        var level    = ReadIntField(dataModel, "Level", "m_Level");
        var xp       = ReadIntField(dataModel, "Xp", "m_Xp");
        var xpNeeded = ReadIntField(dataModel, "XpNeeded", "m_XpNeeded");

        if (level <= 0) return "NO_PASS_INFO:invalid_level";
        return $"PASS_INFO:{level}|{xp}|{xpNeeded}";
    }
    catch (Exception ex) { return "NO_PASS_INFO:" + ex.GetType().Name; }
}
```

`ReadIntField` 与 `ReadRankFieldInt` 同构（通过 `_ctx.GetFieldOrPropertyAny` 尝试多个字段名取 int，失败返 0）。

**管道分派** `Entry.cs` 命令 switch 新增：

```csharp
case "GET_PASS_INFO":
    Respond(_gameReader.ReadPassInfoResponse());
    break;
```

### 2. BotMain

**新增文件** `BotMain/PassParser.cs`（与 `RankHelper.cs` 同级，单一职责）：

```csharp
public static bool TryParsePassInfoResponse(
    string response, out int level, out int xp, out int xpNeeded)
{
    level = xp = xpNeeded = 0;
    if (string.IsNullOrWhiteSpace(response)
        || !response.StartsWith("PASS_INFO:", StringComparison.Ordinal))
        return false;

    var parts = response.Substring("PASS_INFO:".Length).Split('|');
    if (parts.Length < 3) return false;

    return int.TryParse(parts[0], out level)
        && int.TryParse(parts[1], out xp)
        && int.TryParse(parts[2], out xpNeeded);
}
```

**修改** `BotService.cs`：

- 新增字段 `_lastPassLevel / _lastPassXp / _lastPassXpNeeded`
- 公开只读属性 `LastPassLevel / LastPassXp / LastPassXpNeeded`
- 新增方法 `TryQueryPassInfo(PipeServer pipe)`，仿 `TryQueryCurrentRank`：
  - 发 `GET_PASS_INFO`
  - 判断响应前缀为 `PASS_INFO:` / `NO_PASS_INFO:` / `ERROR:`
  - 解析成功则更新 3 个字段
- 在 `TryQueryCurrentRank` 被调用的位置紧跟一条 `TryQueryPassInfo(pipe)`（约 BotService.cs:1679、1954）
- 通行证查询不走限流节流（频率本来就低），与段位查询并列即可

**修改** `BotMain/Cloud/DeviceStatusCollector.cs`：

`HeartbeatData` 新增：

```csharp
public int PassLevel { get; set; }
public int PassXp { get; set; }
public int PassXpNeeded { get; set; }
```

`Collect()` 装配：

```csharp
PassLevel    = _bot.LastPassLevel,
PassXp       = _bot.LastPassXp,
PassXpNeeded = _bot.LastPassXpNeeded,
```

**修改** `BotMain/Cloud/CloudAgent.SendHeartbeatAsync()`：

`InvokeAsync("Heartbeat", …)` 尾部追加 3 个参数 `data.PassLevel, data.PassXp, data.PassXpNeeded`。

### 3. HearthBot.Cloud

**修改** `Models/Device.cs`：

```csharp
public int PassLevel { get; set; }
public int PassXp { get; set; }
public int PassXpNeeded { get; set; }
```

**修改** `Models/DeviceDashboardView.cs`：同名 3 字段（`init`）。

**修改** `Services/CloudSchemaBootstrapper.cs` 的 `DeviceColumns`：

```csharp
("PassLevel",    "INTEGER NOT NULL DEFAULT 0"),
("PassXp",       "INTEGER NOT NULL DEFAULT 0"),
("PassXpNeeded", "INTEGER NOT NULL DEFAULT 0"),
```

`EnsureColumnAsync` 现有逻辑会自动 `ALTER TABLE ADD COLUMN` 给老库补齐，老设备默认 0。

**修改** `Hubs/BotHub.Heartbeat` 签名追加可选参数：

```csharp
public async Task Heartbeat(..., string currentOpponent = "",
    int passLevel = 0, int passXp = 0, int passXpNeeded = 0)
```

调 `_devices.UpdateHeartbeat(...)` 时把 3 个参数透传过去。

**修改** `Services/DeviceManager.UpdateHeartbeat` 同步加 3 个可选参数，写入 `device.PassLevel/...`。

**修改** `Services/DeviceDisplayStateEvaluator.Evaluate()`，在 `new DeviceDashboardView { ... }` 字面量里加：

```csharp
PassLevel    = device.PassLevel,
PassXp       = device.PassXp,
PassXpNeeded = device.PassXpNeeded,
```

### 4. hearthbot-web

**修改** `src/types.ts` 的 `Device` 接口：

```ts
passLevel: number
passXp: number
passXpNeeded: number
```

**修改** `src/components/dashboard/DeviceStatusCard.vue`：

在 `rank-line` 下方插入：

```vue
<div v-if="device.passLevel > 0" class="pass-line">
  <span class="pass-label">通行证</span>
  <span class="pass-level">Lv.{{ device.passLevel }}</span>
  <span v-if="device.passXpNeeded > 0" class="pass-xp">
    {{ device.passXp }}/{{ device.passXpNeeded }}
  </span>
</div>
```

样式：`pass-level` 紫色（`#8b5cf6`）区别于段位蓝色。

**新增** `src/components/PassProgress.vue`（仿 `RankProgress.vue`）：

```vue
<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ level: number; xp: number; xpNeeded: number }>()
const percent = computed(() => {
  if (!props.xpNeeded || props.xpNeeded <= 0) return 0
  return Math.min(100, Math.max(0, (props.xp / props.xpNeeded) * 100))
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
  display: flex; justify-content: space-between;
  font-size: 11px; margin-bottom: 3px;
}
.pass-level { color: #1e293b; font-weight: 600; }
.pass-xp { color: #64748b; }
.pass-bar {
  background: #e2e8f0; border-radius: 4px; height: 6px; overflow: hidden;
}
.pass-bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #a855f7, #7c3aed);
  border-radius: 4px;
  transition: width 0.5s ease;
}
</style>
```

**修改** `src/components/dashboard/DeviceDetailDrawer.vue`：

在段位那节下方新增一节：

```vue
<div class="section">
  <h4>通行证</h4>
  <PassProgress v-if="device.passLevel > 0"
    :level="device.passLevel"
    :xp="device.passXp"
    :xp-needed="device.passXpNeeded" />
  <p v-else class="muted">暂无通行证数据</p>
</div>
```

## 错误处理

| 场景 | 行为 |
| --- | --- |
| Payload 反射失败（未 Init / 找不到类型 / 找不到字段） | 返回 `NO_PASS_INFO:<原因>`，BotMain 不更新 `_lastPassLevel`（保留上次值或 0） |
| 炉石未启动 | 心跳里 `PassLevel=0`，前端主卡不渲染该行，抽屉显示"暂无" |
| `HasReceivedRewardTracksFromServer == false`（刚启动，未登录或数据未到） | 同上 |
| 管道超时（`TrySendAndReceiveExpected` 返回 false） | 值不更新，下次心跳重试 |
| 老客户端 + 新云控 | SignalR 可选参数默认 0，字段保持 0，显示"暂无" |
| 新客户端 + 老云控 | SignalR 忽略多余参数，值写不进去，显示"暂无" |
| 账号切换 | 无特殊处理，下次心跳（≤30 秒）自动覆盖 |

## 测试策略

**单元测试**：

- `BotCore.Tests`（或就近目录）新增 `PassParserTests`：
  - 合法响应 `PASS_INFO:87|1240|2000` → 解析成功
  - `NO_PASS_INFO:xxx` → 返回 false，输出参数保持 0
  - 参数数量少于 3 段 → 返回 false
  - 非数字字段 → 返回 false
- `hearthbot-web` 新增 `PassProgress.test.ts`：
  - `xpNeeded = 0` → percent = 0（不 NaN）
  - `xp > xpNeeded` → percent = 100（钳位）
  - 正常 `xp=500, xpNeeded=1000` → percent = 50

**手工验证**：

1. 开炉石登录账号 → 主卡显示 `通行证 Lv.X`
2. 关炉石 → 主卡该行消失
3. 切换账号（队列模式）→ 下次心跳（≤30 秒）刷新到新账号的值
4. 刚启动炉石尚未登录时 → 主卡不显示该行（`not_received` 路径）

**不做**：

- 集成测试（涉及炉石反射，无法在 CI 复现）
- 性能测试（每 30 秒一次反射，开销可忽略）

## 迁移与部署

- **老库 → 新云控**：首次启动时 `CloudSchemaBootstrapper` 自动 `ALTER TABLE ADD COLUMN`，无人工干预
- **前端部署**：Vue 产物走现有 `DeployTool` 流程，无特殊步骤
- **客户端升级**：BotMain 新版通过现有 `UpdateAvailable` 推送机制到所有设备，无需强制
- **回滚**：如果 Payload 新读法出问题，可从客户端单独回滚 `HearthstonePayload.dll`，不影响云控

## 风险与取舍

| 风险 | 缓解 |
| --- | --- |
| `RewardTrackType.GLOBAL` 枚举值硬编码为 `1` | 注释写清来源（`hs-decompiled/Cheats.cs` 的调用约定）；如暴雪改枚举顺序，`level <= 0` 的防御会让字段回落 0（前端显示"暂无"），不至于显示错误数值 |
| `TrackDataModel.Level` / `Xp` / `XpNeeded` 字段名被混淆 | `ReadIntField` 支持多个候选名（`Level` / `m_Level`），与现有段位读取同构 |
| SignalR 参数列表过长 | 保留可选参数的增量方式，直到下次再加字段时再考虑重构为 `HeartbeatPayload` 对象 |
| 通行证 Manager 在账号切换瞬间可能读到前一个账号的缓存值 | 可接受：最多 30 秒后下次心跳矫正；用户不会依赖这个瞬间值做决策 |
