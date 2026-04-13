# Cloud Mobile Ops Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a mobile-first cloud dashboard that groups devices by actionable state, auto-resets orders on account changes, supports completion reminders, and exposes the minimal high-frequency controls needed for daily operation.

**Architecture:** Keep the existing SignalR + ASP.NET Core + Vue architecture. Put persistent order lifecycle rules in `HearthBot.Cloud`, keep formal completion on the server, derive suspected completion and UI grouping in front-end helper modules, and drive browser reminders from `DeviceUpdated` transitions rather than adding a second real-time channel.

**Tech Stack:** ASP.NET Core 8, EF Core Sqlite, SignalR, Vue 3, TypeScript, Naive UI, Vite, Vitest, xUnit

---

## Notes Before Starting

- Save all edited files as UTF-8.
- Use Chinese git commit messages to match repo requirements.
- Follow `@test-driven-development` for each behavior change.
- Do not hand-edit `HearthBot.Cloud/wwwroot/assets/*`; build from `hearthbot-web`.
- Reuse the approved spec at `docs/superpowers/specs/2026-04-12-cloud-mobile-ops-design.md`.

## File Structure

### Backend files to modify

- `HearthBot.Cloud/Models/Device.cs`
  Add persisted order-account snapshot metadata used to detect account switches without guessing from transient UI state.
- `HearthBot.Cloud/Program.cs`
  Register new services and add compatibility `ALTER TABLE` statements for any new `Devices` columns.
- `HearthBot.Cloud/Services/DeviceManager.cs`
  Centralize order binding, account-switch reset, per-order start snapshot logic, and idempotent completion transitions.
- `HearthBot.Cloud/Services/DeviceWatchdog.cs`
  Clear any new order-lifecycle fields during next-day archive.
- `HearthBot.Cloud/Controllers/DeviceController.cs`
  Stop mutating `Device` rows inline for order changes; call `DeviceManager` instead.
- `HearthBot.Cloud/Controllers/CommandController.cs`
  Expose the minimal command surface used by the new detail drawer.
- `HearthBot.Cloud/Hubs/BotHub.cs`
  Use the new idempotent completion result so formal completion alerts fire once.
- `HearthBot.Cloud/Services/AlertService.cs`
  Implement a testable interface so completion alerts can be verified without real Server酱 calls.

### Backend files to create

- `HearthBot.Cloud/Services/IAlertService.cs`
  Small abstraction used by completion-notification code and tests.
- `HearthBot.Cloud/Services/OrderCompletionUpdate.cs`
  Result type describing whether a completion transition is new or already persisted.
- `HearthBot.Cloud/Services/OrderCompletionNotifier.cs`
  Formats and sends one formal completion alert for newly completed orders.
- `HearthBot.Cloud/Models/CloudCommandTypes.cs`
  Shared command-name constants and validation set for controller tests.

### Test files to create or modify

- `BotCore.Tests/BotCore.Tests.csproj`
  Add a project reference to `HearthBot.Cloud` and include new Cloud-focused tests.
- `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
  Shared Sqlite-backed EF Core context helper for Cloud tests.
- `BotCore.Tests/Cloud/DeviceOrderLifecycleTests.cs`
  Covers order binding, start-rank snapshotting, and account-switch reset.
- `BotCore.Tests/Cloud/OrderCompletionTests.cs`
  Covers idempotent completion and alert notification behavior.
- `BotCore.Tests/Cloud/CloudCommandTypesTests.cs`
  Covers accepted command names needed by the new UI.

### Frontend files to modify

- `hearthbot-web/package.json`
  Add test scripts and `vitest`.
- `hearthbot-web/src/types.ts`
  Extend `Device` shape with any new backend fields and new local dashboard view types.
- `hearthbot-web/src/api/index.ts`
  Expose target-rank/profile/start command helpers used by the drawer.
- `hearthbot-web/src/utils/rankMapping.ts`
  Support Chinese rank strings from `RankHelper.FormatRank`.
- `hearthbot-web/src/views/Dashboard.vue`
  Replace the current kanban page with the new grouped mobile dashboard.

### Frontend files to create

- `hearthbot-web/vitest.config.ts`
  Minimal Vitest config for pure TypeScript utility tests.
- `hearthbot-web/src/utils/rankOptions.ts`
  Shared target-rank option builder mirroring `BotMain/RankHelper.cs`.
- `hearthbot-web/src/utils/dashboardState.ts`
  Pure state-derivation layer for grouping devices and computing suspected completion.
- `hearthbot-web/src/utils/browserNotifications.ts`
  Small wrapper for browser permission checks, dedupe keys, and notification payloads.
- `hearthbot-web/src/utils/rankMapping.test.ts`
  Covers Chinese rank parsing and formatting.
- `hearthbot-web/src/utils/dashboardState.test.ts`
  Covers grouping, abnormal-state detection, and suspected completion logic.
- `hearthbot-web/src/utils/browserNotifications.test.ts`
  Covers client-side dedupe and permission fallbacks.
- `hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue`
  Top four metrics.
- `hearthbot-web/src/components/dashboard/CompletionBanner.vue`
  Sticky completion summary banner.
- `hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue`
  Mobile-friendly segmented state filter.
- `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
  First-screen device card for active/pending/abnormal/completed states.
- `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`
  Secondary detail layer for editing and commands.

## Task 1: Persist Order Session State And Reset On Account Switch

**Files:**
- Create: `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
- Create: `BotCore.Tests/Cloud/DeviceOrderLifecycleTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `HearthBot.Cloud/Models/Device.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`
- Modify: `HearthBot.Cloud/Controllers/DeviceController.cs`
- Modify: `HearthBot.Cloud/Services/DeviceWatchdog.cs`

- [ ] **Step 1: Write the failing backend lifecycle tests**

```csharp
[Fact]
public async Task SetOrderNumber_BindsCurrentAccountSnapshot_AndClearsOldCompletion()
{
    var env = await CloudDbContextTestFactory.CreateAsync();
    env.Db.Devices.Add(new Device
    {
        DeviceId = "pc-01",
        CurrentAccount = "账号A",
        OrderNumber = "OLD-1",
        IsCompleted = true,
        CompletedRank = "传说"
    });
    await env.Db.SaveChangesAsync();

    var manager = env.CreateDeviceManager();
    var updated = await manager.SetOrderNumber("pc-01", "NEW-1");

    Assert.Equal("NEW-1", updated!.OrderNumber);
    Assert.Equal("账号A", updated.OrderAccountName);
    Assert.False(updated.IsCompleted);
    Assert.Equal(string.Empty, updated.CompletedRank);
}

[Fact]
public async Task UpdateHeartbeat_WhenOrderAccountChanges_ClearsOrderSession()
{
    var env = await CloudDbContextTestFactory.CreateAsync();
    env.Db.Devices.Add(new Device
    {
        DeviceId = "pc-02",
        OrderNumber = "A-2026",
        OrderAccountName = "账号A",
        StartRank = "钻石5",
        TargetRank = "传说"
    });
    await env.Db.SaveChangesAsync();

    var manager = env.CreateDeviceManager();
    var updated = await manager.UpdateHeartbeat("pc-02", "Running", "账号B", "钻石4", "猎人", "脚本A", "Standard", 3, 1, "传说", "");

    Assert.NotNull(updated);
    Assert.Equal(string.Empty, updated!.OrderNumber);
    Assert.Equal(string.Empty, updated.OrderAccountName);
    Assert.Equal(string.Empty, updated.StartRank);
    Assert.Null(updated.StartedAt);
}
```

- [ ] **Step 2: Run the lifecycle tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceOrderLifecycleTests -v minimal`

Expected: FAIL because `Device` has no `OrderAccountName`, `DeviceManager` has no `SetOrderNumber`, and heartbeat does not clear stale orders.

- [ ] **Step 3: Implement the minimal order-session persistence**

```csharp
public class Device
{
    public string OrderAccountName { get; set; } = string.Empty;
}

public async Task<Device?> SetOrderNumber(string deviceId, string orderNumber)
{
    var normalized = orderNumber?.Trim() ?? string.Empty;
    var device = await db.Devices.FindAsync(deviceId);
    if (device == null) return null;

    var isNewOrder = !string.Equals(device.OrderNumber, normalized, StringComparison.Ordinal);
    device.OrderNumber = normalized;
    device.OrderAccountName = string.IsNullOrEmpty(normalized) ? string.Empty : device.CurrentAccount;

    if (isNewOrder)
    {
        device.IsCompleted = false;
        device.CompletedAt = null;
        device.CompletedRank = string.Empty;
        device.StartRank = string.Empty;
        device.StartedAt = null;
    }

    await db.SaveChangesAsync();
    return device;
}
```

```csharp
var accountChanged = !string.IsNullOrWhiteSpace(device.OrderNumber)
    && !string.IsNullOrWhiteSpace(device.OrderAccountName)
    && !string.IsNullOrWhiteSpace(currentAccount)
    && !string.Equals(currentAccount, device.OrderAccountName, StringComparison.Ordinal);

if (accountChanged)
{
    device.OrderNumber = string.Empty;
    device.OrderAccountName = string.Empty;
    device.StartRank = string.Empty;
    device.StartedAt = null;
    device.TargetRank = string.Empty;
    device.IsCompleted = false;
    device.CompletedAt = null;
    device.CompletedRank = string.Empty;
}

if (!string.IsNullOrEmpty(device.OrderNumber)
    && string.IsNullOrEmpty(device.StartRank)
    && !string.IsNullOrEmpty(currentRank))
{
    device.StartRank = currentRank;
    device.StartedAt = DateTime.UtcNow;
}
```

- [ ] **Step 4: Run the lifecycle tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceOrderLifecycleTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the order-session persistence**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/CloudDbContextTestFactory.cs BotCore.Tests/Cloud/DeviceOrderLifecycleTests.cs HearthBot.Cloud/Models/Device.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Services/DeviceManager.cs HearthBot.Cloud/Controllers/DeviceController.cs HearthBot.Cloud/Services/DeviceWatchdog.cs
git commit -m "feat: 增加换号自动清单的订单会话状态"
```

## Task 2: Make Formal Completion Idempotent And Send Server酱 Alerts Once

**Files:**
- Create: `HearthBot.Cloud/Services/IAlertService.cs`
- Create: `HearthBot.Cloud/Services/OrderCompletionUpdate.cs`
- Create: `HearthBot.Cloud/Services/OrderCompletionNotifier.cs`
- Create: `BotCore.Tests/Cloud/OrderCompletionTests.cs`
- Modify: `HearthBot.Cloud/Services/AlertService.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`
- Modify: `HearthBot.Cloud/Hubs/BotHub.cs`
- Modify: `HearthBot.Cloud/Controllers/DeviceController.cs`

- [ ] **Step 1: Write the failing completion tests**

```csharp
[Fact]
public async Task MarkOrderCompleted_WhenAlreadyCompleted_ReturnsExistingStateWithoutNewTransition()
{
    var env = await CloudDbContextTestFactory.CreateAsync();
    env.Db.Devices.Add(new Device
    {
        DeviceId = "pc-03",
        OrderNumber = "DONE-1",
        IsCompleted = true,
        CompletedAt = DateTime.UtcNow.AddMinutes(-3),
        CompletedRank = "传说"
    });
    await env.Db.SaveChangesAsync();

    var manager = env.CreateDeviceManager();
    var result = await manager.MarkOrderCompleted("pc-03", "传说");

    Assert.NotNull(result);
    Assert.False(result!.WasNewlyCompleted);
}

[Fact]
public async Task NotifyAsync_SendsAlertForNewCompletionOnly()
{
    var fakeAlert = new FakeAlertService();
    var notifier = new OrderCompletionNotifier(fakeAlert);

    await notifier.NotifyAsync(new Device
    {
        DeviceId = "pc-03",
        DisplayName = "机器3",
        OrderNumber = "DONE-1",
        CurrentAccount = "账号C",
        CompletedRank = "传说"
    });

    Assert.Single(fakeAlert.Messages);
}
```

- [ ] **Step 2: Run the completion tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter OrderCompletionTests -v minimal`

Expected: FAIL because `MarkOrderCompleted` is not idempotent and no notifier abstraction exists.

- [ ] **Step 3: Implement idempotent completion + notifier**

```csharp
public sealed record OrderCompletionUpdate(Device Device, bool WasNewlyCompleted);

public async Task<OrderCompletionUpdate?> MarkOrderCompleted(string deviceId, string reachedRank)
{
    var device = await db.Devices.FindAsync(deviceId);
    if (device == null) return null;

    if (device.IsCompleted)
        return new OrderCompletionUpdate(device, false);

    device.IsCompleted = true;
    device.CompletedAt = DateTime.UtcNow;
    device.CompletedRank = string.IsNullOrEmpty(reachedRank) ? device.CurrentRank : reachedRank;
    await db.SaveChangesAsync();
    return new OrderCompletionUpdate(device, true);
}
```

```csharp
public sealed class OrderCompletionNotifier
{
    private readonly IAlertService _alerts;

    public async Task NotifyAsync(Device device)
    {
        var title = $"订单完成: {device.DisplayName}";
        var content = $"订单号: {device.OrderNumber}\n账号: {device.CurrentAccount}\n完成段位: {device.CompletedRank}";
        await _alerts.SendAlert(title, content);
    }
}
```

- [ ] **Step 4: Run the completion tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter OrderCompletionTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the formal-completion pipeline**

```bash
git add BotCore.Tests/Cloud/OrderCompletionTests.cs HearthBot.Cloud/Services/IAlertService.cs HearthBot.Cloud/Services/OrderCompletionUpdate.cs HearthBot.Cloud/Services/OrderCompletionNotifier.cs HearthBot.Cloud/Services/AlertService.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Services/DeviceManager.cs HearthBot.Cloud/Hubs/BotHub.cs HearthBot.Cloud/Controllers/DeviceController.cs
git commit -m "feat: 增加正式完成去重与云控完成提醒"
```

## Task 3: Expose The Minimal Cloud Command Surface For The Detail Drawer

**Files:**
- Create: `HearthBot.Cloud/Models/CloudCommandTypes.cs`
- Create: `BotCore.Tests/Cloud/CloudCommandTypesTests.cs`
- Modify: `HearthBot.Cloud/Controllers/CommandController.cs`
- Modify: `BotMain/Cloud/CommandExecutor.cs`

- [ ] **Step 1: Write the failing command-surface tests**

```csharp
[Theory]
[InlineData(CloudCommandTypes.Start)]
[InlineData(CloudCommandTypes.Stop)]
[InlineData(CloudCommandTypes.ChangeDeck)]
[InlineData(CloudCommandTypes.ChangeProfile)]
[InlineData(CloudCommandTypes.ChangeTarget)]
public void ValidCommands_ContainsDashboardDetailActions(string commandType)
{
    Assert.Contains(commandType, CloudCommandTypes.Valid);
}
```

- [ ] **Step 2: Run the command-surface tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CloudCommandTypesTests -v minimal`

Expected: FAIL because `CloudCommandTypes` does not exist and `ChangeTarget` / `Start` are not in the controller validation set.

- [ ] **Step 3: Implement shared command constants and BotMain handling**

```csharp
public static class CloudCommandTypes
{
    public const string Start = "Start";
    public const string Stop = "Stop";
    public const string ChangeDeck = "ChangeDeck";
    public const string ChangeProfile = "ChangeProfile";
    public const string ChangeTarget = "ChangeTarget";

    public static readonly HashSet<string> Valid = new(StringComparer.OrdinalIgnoreCase)
    {
        Start, Stop, ChangeDeck, ChangeProfile, ChangeTarget, "Concede", "Restart"
    };
}
```

```csharp
case CloudCommandTypes.ChangeProfile:
{
    using var doc = JsonDocument.Parse(payload);
    var profileName = doc.RootElement.GetProperty("ProfileName").GetString() ?? "";
    var account = _accounts.CurrentAccount;
    if (account != null)
    {
        account.ProfileName = profileName;
        _accounts.Save();
        _log($"[云控] 策略已切换为: {profileName}");
    }
    break;
}
```

- [ ] **Step 4: Run the command-surface tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CloudCommandTypesTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the command-surface changes**

```bash
git add BotCore.Tests/Cloud/CloudCommandTypesTests.cs HearthBot.Cloud/Models/CloudCommandTypes.cs HearthBot.Cloud/Controllers/CommandController.cs BotMain/Cloud/CommandExecutor.cs
git commit -m "feat: 打通云控详情页的目标段位和策略指令"
```

## Task 4: Add Frontend Utility Tests And Extract Dashboard State Logic

**Files:**
- Create: `hearthbot-web/vitest.config.ts`
- Create: `hearthbot-web/src/utils/rankOptions.ts`
- Create: `hearthbot-web/src/utils/dashboardState.ts`
- Create: `hearthbot-web/src/utils/rankMapping.test.ts`
- Create: `hearthbot-web/src/utils/dashboardState.test.ts`
- Modify: `hearthbot-web/package.json`
- Modify: `hearthbot-web/src/utils/rankMapping.ts`
- Modify: `hearthbot-web/src/types.ts`

- [ ] **Step 1: Write the failing frontend utility tests**

```ts
import { describe, expect, it } from 'vitest'
import { rankToNumber } from './rankMapping'

describe('rankToNumber', () => {
  it('parses Chinese ranks from RankHelper', () => {
    expect(rankToNumber('钻石5')).toBe(46)
    expect(rankToNumber('传说')).toBe(51)
  })
})
```

```ts
import { getDeviceBucket, isCompletionSuspected } from './dashboardState'

it('puts a switched-account device into pending', () => {
  expect(getDeviceBucket({
    orderNumber: '',
    orderAccountName: '',
    currentAccount: '账号B',
    status: 'Running',
    isCompleted: false
  } as any)).toBe('pending')
})

it('marks target reached without formal completion as suspected', () => {
  expect(isCompletionSuspected({
    orderNumber: 'A-1',
    isCompleted: false,
    currentRank: '传说',
    targetRank: '钻石5'
  } as any)).toBe(true)
})
```

- [ ] **Step 2: Run the frontend tests to verify they fail**

Run: `npm --prefix hearthbot-web run test -- src/utils/rankMapping.test.ts src/utils/dashboardState.test.ts`

Expected: FAIL because there is no test runner, Chinese rank parsing is unsupported, and the dashboard-state helpers do not exist.

- [ ] **Step 3: Implement the pure utility layer**

```ts
const TIER_BASE: Record<string, number> = {
  青铜: 0,
  白银: 10,
  黄金: 20,
  白金: 30,
  钻石: 40,
  Bronze: 0,
  Silver: 10,
  Gold: 20,
  Platinum: 30,
  Diamond: 40
}
```

```ts
export function isCompletionSuspected(device: Device): boolean {
  const current = rankToNumber(device.currentRank)
  const target = rankToNumber(device.targetRank)
  return Boolean(device.orderNumber)
    && !device.isCompleted
    && current !== null
    && target !== null
    && current >= target
}

export function getDeviceBucket(device: Device): DashboardBucket {
  if (device.isCompleted) return 'completed'
  if (device.status === 'Offline') return 'abnormal'
  if (!device.orderNumber) return 'pending'
  if (device.status === 'Switching') return 'abnormal'
  return 'active'
}
```

- [ ] **Step 4: Run the frontend tests again**

Run: `npm --prefix hearthbot-web run test -- src/utils/rankMapping.test.ts src/utils/dashboardState.test.ts`

Expected: PASS

- [ ] **Step 5: Commit the frontend utility layer**

```bash
git add hearthbot-web/package.json hearthbot-web/vitest.config.ts hearthbot-web/src/utils/rankOptions.ts hearthbot-web/src/utils/dashboardState.ts hearthbot-web/src/utils/rankMapping.ts hearthbot-web/src/utils/rankMapping.test.ts hearthbot-web/src/utils/dashboardState.test.ts hearthbot-web/src/types.ts
git commit -m "feat: 增加云控首页状态推导与段位映射测试"
```

## Task 5: Replace The Dashboard With The Mobile-First Grouped UI

**Files:**
- Create: `hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue`
- Create: `hearthbot-web/src/components/dashboard/CompletionBanner.vue`
- Create: `hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue`
- Create: `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
- Create: `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`
- Modify: `hearthbot-web/src/views/Dashboard.vue`
- Modify: `hearthbot-web/src/api/index.ts`

- [ ] **Step 1: Write the failing browser-notification tests**

```ts
import { describe, expect, it } from 'vitest'
import { shouldNotifyCompletion } from './browserNotifications'

describe('shouldNotifyCompletion', () => {
  it('dedupes the same completed order', () => {
    const device = { deviceId: 'pc-01', orderNumber: 'A-1', completedAt: '2026-04-12T12:00:00Z' } as any
    expect(shouldNotifyCompletion(device, new Set())).toBe(true)
    expect(shouldNotifyCompletion(device, new Set(['pc-01|A-1|2026-04-12T12:00:00Z']))).toBe(false)
  })
})
```

- [ ] **Step 2: Run the browser-notification tests to verify they fail**

Run: `npm --prefix hearthbot-web run test -- src/utils/browserNotifications.test.ts`

Expected: FAIL because the notification helper does not exist.

- [ ] **Step 3: Implement the new dashboard components and notification helper**

```ts
export function completionNoticeKey(device: Device): string {
  return [device.deviceId, device.orderNumber, device.completedAt].join('|')
}

export function shouldNotifyCompletion(device: Device, seen: Set<string>): boolean {
  const key = completionNoticeKey(device)
  return Boolean(device.isCompleted && device.completedAt && device.orderNumber && !seen.has(key))
}
```

```vue
<template>
  <div class="dashboard-page">
    <DashboardHeaderStats :stats="stats" />
    <CompletionBanner :items="newlyCompleted" @dismiss="dismissCompletion" />
    <DeviceOverviewTabs :counts="counts" :active-tab="activeTab" @change="activeTab = $event" />
    <div class="device-list">
      <DeviceStatusCard
        v-for="device in filteredDevices"
        :key="device.deviceId"
        :device="device"
        :bucket="getDeviceBucket(device)"
        :suspected-completion="isCompletionSuspected(device)"
        @open="openDevice(device)"
        @save-order="saveOrder"
      />
    </div>
    <DeviceDetailDrawer
      v-model:show="detailOpen"
      :device="selectedDevice"
      @change-target="changeTarget"
      @change-deck="changeDeck"
      @change-profile="changeProfile"
      @start="startDevice"
      @stop="stopDevice"
    />
  </div>
</template>
```

- [ ] **Step 4: Run the frontend tests and build**

Run: `npm --prefix hearthbot-web run test`
Expected: PASS

Run: `npm --prefix hearthbot-web run build`
Expected: PASS and refreshed assets under `HearthBot.Cloud/wwwroot`

- [ ] **Step 5: Commit the mobile dashboard UI**

```bash
git add hearthbot-web/src/views/Dashboard.vue hearthbot-web/src/api/index.ts hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue hearthbot-web/src/components/dashboard/CompletionBanner.vue hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue hearthbot-web/src/components/dashboard/DeviceStatusCard.vue hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue hearthbot-web/src/utils/browserNotifications.ts hearthbot-web/src/utils/browserNotifications.test.ts HearthBot.Cloud/wwwroot
git commit -m "feat: 重构云控首页为手机优先巡检台"
```

## Task 6: Run End-To-End Verification Before Declaring The Feature Done

**Files:**
- Modify: `docs/superpowers/specs/2026-04-12-cloud-mobile-ops-design.md` (only if implementation drift requires updating the approved design)

- [ ] **Step 1: Run the Cloud-focused .NET tests**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "DeviceOrderLifecycleTests|OrderCompletionTests|CloudCommandTypesTests" -v minimal`

Expected: PASS

- [ ] **Step 2: Run the full frontend utility suite and production build**

Run: `npm --prefix hearthbot-web run test`

Expected: PASS

Run: `npm --prefix hearthbot-web run build`

Expected: PASS

- [ ] **Step 3: Smoke-test the critical operator flows manually**

Run:

```bash
dotnet run --project HearthBot.Cloud/HearthBot.Cloud.csproj
```

Then verify manually in the browser:

- Pending device can save a new order number
- Changing `CurrentAccount` via heartbeat clears the old order and moves the card into `待录单`
- A formal completion transition shows `已完成`, emits a browser notification, and sends one Server酱 alert
- The detail drawer can send `ChangeTarget`, `ChangeDeck`, `ChangeProfile`, `Start`, and `Stop`

- [ ] **Step 4: Review generated assets and final diff**

Run:

```bash
git status --short
git diff --stat
```

Expected: only the planned backend, test, and frontend files are changed.

- [ ] **Step 5: Commit the final verified implementation**

```bash
git add -A
git commit -m "feat: 完成云控手机优先巡检台改造"
```
