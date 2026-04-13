# Cloud Status Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make cloud device status, bucket placement, and abnormal counts come from one backend rule set so normal devices stop bouncing between active, abnormal, and offline.

**Architecture:** Keep BotMain heartbeats as the raw source of truth, add one persisted raw-status transition timestamp in `HearthBot.Cloud`, and centralize all display-state derivation in a backend evaluator/projection service. Update the Vue dashboard to consume backend `displayStatus` and `bucket` fields first, while keeping a thin fallback path so mixed-version deploys do not break the page.

**Tech Stack:** ASP.NET Core 8, EF Core Sqlite, SignalR, Vue 3, TypeScript, Naive UI, Vite, Vitest, xUnit

---

## Notes Before Starting

- Save every edited file as UTF-8.
- Use Chinese git commit messages.
- Follow `@test-driven-development`: write the failing test first for each behavior change.
- Do not hand-edit `HearthBot.Cloud/wwwroot/assets/*`; build from `hearthbot-web`.
- Reuse the approved spec at `docs/superpowers/specs/2026-04-13-cloud-status-stability-design.md`.
- Keep raw BotMain heartbeat semantics unchanged:
  - `InGame`
  - `Running`
  - `Idle`
  - `Switching`
  - `Offline`
- New backend thresholds must be centralized in one place:
  - offline timeout: `150s`
  - switching abnormal timeout: `180s`

## File Structure

### Backend files to modify

- `HearthBot.Cloud/Models/Device.cs`
  Add persisted raw-status transition time so the server can tell whether `Switching` is brief or stuck.
- `HearthBot.Cloud/Program.cs`
  Register the new evaluator/projection services and add compatibility `ALTER TABLE` statements for new `Devices` columns.
- `HearthBot.Cloud/Services/DeviceManager.cs`
  Preserve raw heartbeat status, update `StatusChangedAt` only when the raw status really changes, and keep registration defaults consistent.
- `HearthBot.Cloud/Services/DeviceWatchdog.cs`
  Stop hardcoding 90-second timeout logic; reuse the shared status policy constants so watchdog offline writes match dashboard rules.
- `HearthBot.Cloud/Controllers/DeviceController.cs`
  Return projected device views from `GetAll` and `Get`, and compute abnormal/online stats from projected buckets instead of ad hoc heartbeat checks.
- `HearthBot.Cloud/Hubs/BotHub.cs`
  Broadcast projected device views from `Register`, `Heartbeat`, `ReportOrderCompleted`, and disconnect handling so SignalR matches REST.

### Backend files to create

- `HearthBot.Cloud/Models/DeviceDashboardView.cs`
  DTO returned to the dashboard with both raw and display fields.
- `HearthBot.Cloud/Models/DeviceDashboardStatsView.cs`
  Small DTO for the stats payload so counts are explicit and testable.
- `HearthBot.Cloud/Services/DeviceStatusPolicy.cs`
  Shared timeout constants and simple helpers used by evaluator and watchdog.
- `HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs`
  Single-device raw-to-display evaluator that decides `displayStatus`, `bucket`, `abnormalReason`, and age fields.
- `HearthBot.Cloud/Services/DeviceDashboardProjectionService.cs`
  Batch projector used by controllers and hubs to keep list, detail, stats, and SignalR payloads consistent.

### Test files to create or modify

- `BotCore.Tests/BotCore.Tests.csproj`
  Link the new Cloud service/model files into the existing test project.
- `BotCore.Tests/Cloud/DeviceHeartbeatStateTrackingTests.cs`
  Covers `StatusChangedAt` initialization and update behavior.
- `BotCore.Tests/Cloud/DeviceDisplayStateEvaluatorTests.cs`
  Covers active, pending, abnormal, offline, completed, and long-switching cases.
- `BotCore.Tests/Cloud/DeviceDashboardProjectionServiceTests.cs`
  Covers projected stats and online/abnormal count behavior.
- `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
  Register the new evaluator/projection services if any tests construct them through DI.

### Frontend files to modify

- `hearthbot-web/src/types.ts`
  Extend the `Device` shape with backend display-state fields and keep the old fields for compatibility.
- `hearthbot-web/src/utils/dashboardState.ts`
  Read backend `bucket` and `displayStatus` first, with a fallback to the current local logic only when new fields are absent.
- `hearthbot-web/src/utils/dashboardState.test.ts`
  Cover backend-first grouping and fallback behavior.
- `hearthbot-web/src/views/Dashboard.vue`
  Stop relying on the local 90-second abnormal calculation for the normal path and keep bucket/count logic aligned with backend payloads.
- `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
  Render status tags and abnormal messages from `displayStatus` and `abnormalReason`.
- `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`
  Render the detail header tag from `displayStatus` instead of raw `status`.

### Frontend files to create

- None expected unless the implementation needs a tiny display-state formatting helper. Prefer reusing `dashboardState.ts`.

## Task 1: Persist Raw Status Transition Time

**Files:**
- Create: `BotCore.Tests/Cloud/DeviceHeartbeatStateTrackingTests.cs`
- Modify: `HearthBot.Cloud/Models/Device.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`

- [ ] **Step 1: Write the failing heartbeat state-tracking tests**

```csharp
using HearthBot.Cloud.Models;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceHeartbeatStateTrackingTests
{
    [Fact]
    public async Task RegisterDevice_SeedsStatusChangedAt_ForNewDevice()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        var manager = env.CreateDeviceManager();

        await manager.RegisterDevice("pc-01", "一号机", Array.Empty<string>(), Array.Empty<string>(), "conn-1");

        var device = await env.Db.Devices.FindAsync("pc-01");
        Assert.NotNull(device);
        Assert.Equal("Idle", device!.Status);
        Assert.NotEqual(default, device.StatusChangedAt);
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenRawStatusChanges_RefreshesStatusChangedAt()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-02",
            Status = "Running",
            StatusChangedAt = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc),
            LastHeartbeat = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc)
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.UpdateHeartbeat("pc-02", "Switching", "账号A", "", "", "", "Standard", 0, 0, "", "");

        Assert.Equal("Switching", updated!.Status);
        Assert.True(updated.StatusChangedAt > new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdateHeartbeat_WhenRawStatusStaysSame_PreservesStatusChangedAt()
    {
        await using var env = await CloudTestEnvironment.CreateAsync();
        var changedAt = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc);
        env.Db.Devices.Add(new Device
        {
            DeviceId = "pc-03",
            Status = "Running",
            StatusChangedAt = changedAt,
            LastHeartbeat = changedAt
        });
        await env.Db.SaveChangesAsync();

        var manager = env.CreateDeviceManager();
        var updated = await manager.UpdateHeartbeat("pc-03", "Running", "账号A", "", "", "", "Standard", 0, 0, "", "");

        Assert.Equal(changedAt, updated!.StatusChangedAt);
    }
}
```

- [ ] **Step 2: Run the new test file and verify it fails**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceHeartbeatStateTrackingTests -v minimal`

Expected: FAIL because `Device` has no `StatusChangedAt` field and `DeviceManager` does not preserve raw-status transition time.

- [ ] **Step 3: Write the minimal persistence implementation**

```csharp
public class Device
{
    public string Status { get; set; } = "Offline";
    public DateTime StatusChangedAt { get; set; }
}
```

```csharp
var utcNow = DateTime.UtcNow;

device.Status = "Idle";
device.StatusChangedAt = utcNow;
device.LastHeartbeat = utcNow;
```

```csharp
var utcNow = DateTime.UtcNow;

if (!string.Equals(device.Status, status, StringComparison.Ordinal))
{
    device.Status = status;
    device.StatusChangedAt = utcNow;
}

device.LastHeartbeat = utcNow;
```

```csharp
try { db.Database.ExecuteSqlRaw("ALTER TABLE Devices ADD COLUMN StatusChangedAt TEXT"); } catch { }
```

- [ ] **Step 4: Run the heartbeat state-tracking tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceHeartbeatStateTrackingTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the raw-status transition persistence**

```bash
git add BotCore.Tests/Cloud/DeviceHeartbeatStateTrackingTests.cs HearthBot.Cloud/Models/Device.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Services/DeviceManager.cs
git commit -m "feat: 记录云控原始状态切换时间"
```

## Task 2: Add The Backend Display-State Evaluator

**Files:**
- Create: `HearthBot.Cloud/Models/DeviceDashboardView.cs`
- Create: `HearthBot.Cloud/Services/DeviceStatusPolicy.cs`
- Create: `HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs`
- Create: `BotCore.Tests/Cloud/DeviceDisplayStateEvaluatorTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`

- [ ] **Step 1: Write the failing evaluator tests**

```csharp
using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceDisplayStateEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_InGameWithFreshHeartbeat_IsActive()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-01",
            Status = "InGame",
            OrderNumber = "A-1",
            LastHeartbeat = Now.AddSeconds(-20),
            StatusChangedAt = Now.AddSeconds(-20)
        }, Now);

        Assert.Equal("InGame", view.RawStatus);
        Assert.Equal("InGame", view.DisplayStatus);
        Assert.Equal("active", view.Bucket);
        Assert.Null(view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_SwitchingWithinTimeout_StaysInNormalBucket()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-02",
            Status = "Switching",
            OrderNumber = "",
            LastHeartbeat = Now.AddSeconds(-15),
            StatusChangedAt = Now.AddSeconds(-90)
        }, Now);

        Assert.Equal("Switching", view.DisplayStatus);
        Assert.Equal("pending", view.Bucket);
        Assert.Null(view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_SwitchingTooLong_IsAbnormal()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-03",
            Status = "Switching",
            OrderNumber = "A-3",
            LastHeartbeat = Now.AddSeconds(-10),
            StatusChangedAt = Now.AddSeconds(-190)
        }, Now);

        Assert.Equal("abnormal", view.Bucket);
        Assert.Equal("Switching", view.DisplayStatus);
        Assert.Equal("SwitchingTooLong", view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_HeartbeatPastOfflineTimeout_IsOffline()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-04",
            Status = "Running",
            OrderNumber = "A-4",
            LastHeartbeat = Now.AddSeconds(-151),
            StatusChangedAt = Now.AddSeconds(-151)
        }, Now);

        Assert.Equal("Offline", view.DisplayStatus);
        Assert.Equal("abnormal", view.Bucket);
        Assert.Equal("HeartbeatTimeout", view.AbnormalReason);
    }

    [Fact]
    public void Evaluate_CompletedDevice_AlwaysStaysCompleted()
    {
        var evaluator = new DeviceDisplayStateEvaluator();
        var view = evaluator.Evaluate(new Device
        {
            DeviceId = "pc-05",
            Status = "Offline",
            OrderNumber = "A-5",
            IsCompleted = true,
            LastHeartbeat = Now.AddDays(-1),
            StatusChangedAt = Now.AddDays(-1)
        }, Now);

        Assert.Equal("completed", view.Bucket);
        Assert.Equal("Completed", view.DisplayStatus);
        Assert.Null(view.AbnormalReason);
    }
}
```

- [ ] **Step 2: Run the evaluator tests and verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDisplayStateEvaluatorTests -v minimal`

Expected: FAIL because the evaluator, policy, and dashboard view DTO do not exist.

- [ ] **Step 3: Implement the minimal evaluator and shared policy**

```csharp
namespace HearthBot.Cloud.Services;

public static class DeviceStatusPolicy
{
    public static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan SwitchingAbnormalTimeout = TimeSpan.FromSeconds(180);
}
```

```csharp
namespace HearthBot.Cloud.Models;

public sealed class DeviceDashboardView
{
    public string DeviceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RawStatus { get; init; } = string.Empty;
    public string DisplayStatus { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public string? AbnormalReason { get; init; }
    public double HeartbeatAgeSeconds { get; init; }
    public bool IsHeartbeatStale { get; init; }
    public bool IsSwitchingTooLong { get; init; }
    public string CurrentAccount { get; init; } = string.Empty;
    public string CurrentRank { get; init; } = string.Empty;
    public string CurrentDeck { get; init; } = string.Empty;
    public string CurrentProfile { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;
    public int SessionWins { get; init; }
    public int SessionLosses { get; init; }
    public DateTime LastHeartbeat { get; init; }
    public string AvailableDecksJson { get; init; } = "[]";
    public string AvailableProfilesJson { get; init; } = "[]";
    public string OrderNumber { get; init; } = string.Empty;
    public string OrderAccountName { get; init; } = string.Empty;
    public string TargetRank { get; init; } = string.Empty;
    public string StartRank { get; init; } = string.Empty;
    public DateTime? StartedAt { get; init; }
    public string CurrentOpponent { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string CompletedRank { get; init; } = string.Empty;
}
```

```csharp
public sealed class DeviceDisplayStateEvaluator
{
    public DeviceDashboardView Evaluate(Device device, DateTime utcNow)
    {
        var heartbeatAge = Math.Max(0, (utcNow - device.LastHeartbeat).TotalSeconds);
        var statusAge = Math.Max(0, (utcNow - device.StatusChangedAt).TotalSeconds);
        var heartbeatTimedOut = heartbeatAge >= DeviceStatusPolicy.OfflineTimeout.TotalSeconds;
        var switchingTooLong = string.Equals(device.Status, "Switching", StringComparison.Ordinal)
            && statusAge >= DeviceStatusPolicy.SwitchingAbnormalTimeout.TotalSeconds;

        var displayStatus = device.Status;
        var bucket = string.IsNullOrWhiteSpace(device.OrderNumber) ? "pending" : "active";
        string? abnormalReason = null;

        if (device.IsCompleted)
        {
            displayStatus = "Completed";
            bucket = "completed";
        }
        else if (heartbeatTimedOut)
        {
            displayStatus = "Offline";
            bucket = "abnormal";
            abnormalReason = "HeartbeatTimeout";
        }
        else if (switchingTooLong)
        {
            bucket = "abnormal";
            abnormalReason = "SwitchingTooLong";
        }

        return new DeviceDashboardView
        {
            DeviceId = device.DeviceId,
            DisplayName = device.DisplayName,
            Status = device.Status,
            RawStatus = device.Status,
            DisplayStatus = displayStatus,
            Bucket = bucket,
            AbnormalReason = abnormalReason,
            HeartbeatAgeSeconds = heartbeatAge,
            IsHeartbeatStale = heartbeatTimedOut,
            IsSwitchingTooLong = switchingTooLong,
            CurrentAccount = device.CurrentAccount,
            CurrentRank = device.CurrentRank,
            CurrentDeck = device.CurrentDeck,
            CurrentProfile = device.CurrentProfile,
            GameMode = device.GameMode,
            SessionWins = device.SessionWins,
            SessionLosses = device.SessionLosses,
            LastHeartbeat = device.LastHeartbeat,
            AvailableDecksJson = device.AvailableDecksJson,
            AvailableProfilesJson = device.AvailableProfilesJson,
            OrderNumber = device.OrderNumber,
            OrderAccountName = device.OrderAccountName,
            TargetRank = device.TargetRank,
            StartRank = device.StartRank,
            StartedAt = device.StartedAt,
            CurrentOpponent = device.CurrentOpponent,
            IsCompleted = device.IsCompleted,
            CompletedAt = device.CompletedAt,
            CompletedRank = device.CompletedRank
        };
    }
}
```

- [ ] **Step 4: Run the evaluator tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDisplayStateEvaluatorTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the evaluator**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/DeviceDisplayStateEvaluatorTests.cs HearthBot.Cloud/Models/DeviceDashboardView.cs HearthBot.Cloud/Services/DeviceStatusPolicy.cs HearthBot.Cloud/Services/DeviceDisplayStateEvaluator.cs
git commit -m "feat: 新增云控展示状态评估器"
```

## Task 3: Project Backend Views And Stats Through One Service

**Files:**
- Create: `HearthBot.Cloud/Models/DeviceDashboardStatsView.cs`
- Create: `HearthBot.Cloud/Services/DeviceDashboardProjectionService.cs`
- Create: `BotCore.Tests/Cloud/DeviceDashboardProjectionServiceTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Controllers/DeviceController.cs`
- Modify: `HearthBot.Cloud/Hubs/BotHub.cs`
- Modify: `HearthBot.Cloud/Services/DeviceWatchdog.cs`

- [ ] **Step 1: Write the failing projection tests**

```csharp
using HearthBot.Cloud.Models;
using HearthBot.Cloud.Services;
using Xunit;

namespace BotCore.Tests.Cloud;

public class DeviceDashboardProjectionServiceTests
{
    private static readonly DateTime Now = new(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildStats_UsesProjectedBuckets_InsteadOfRawHeartbeatMath()
    {
        var service = new DeviceDashboardProjectionService(new DeviceDisplayStateEvaluator());
        var views = service.ProjectMany(new[]
        {
            new Device
            {
                DeviceId = "pc-01",
                Status = "InGame",
                OrderNumber = "A-1",
                LastHeartbeat = Now.AddSeconds(-20),
                StatusChangedAt = Now.AddSeconds(-20)
            },
            new Device
            {
                DeviceId = "pc-02",
                Status = "Switching",
                OrderNumber = "A-2",
                LastHeartbeat = Now.AddSeconds(-20),
                StatusChangedAt = Now.AddSeconds(-190)
            },
            new Device
            {
                DeviceId = "pc-03",
                Status = "Running",
                OrderNumber = "A-3",
                LastHeartbeat = Now.AddSeconds(-151),
                StatusChangedAt = Now.AddSeconds(-151)
            }
        }, Now);

        var stats = service.BuildStats(views, todayGames: 9, todayWins: 5, todayLosses: 4, completedCount: 2);

        Assert.Equal(2, stats.OnlineCount);
        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(2, stats.AbnormalCount);
        Assert.Equal(9, stats.TodayGames);
        Assert.Equal(2, stats.CompletedCount);
    }
}
```

- [ ] **Step 2: Run the projection tests and verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDashboardProjectionServiceTests -v minimal`

Expected: FAIL because the projection service and stats DTO do not exist.

- [ ] **Step 3: Implement the projection service and wire it into REST, SignalR, and watchdog**

```csharp
namespace HearthBot.Cloud.Models;

public sealed class DeviceDashboardStatsView
{
    public int OnlineCount { get; init; }
    public int TotalCount { get; init; }
    public int TodayGames { get; init; }
    public int TodayWins { get; init; }
    public int TodayLosses { get; init; }
    public int AbnormalCount { get; init; }
    public int CompletedCount { get; init; }
}
```

```csharp
public sealed class DeviceDashboardProjectionService
{
    private readonly DeviceDisplayStateEvaluator _evaluator;

    public DeviceDashboardProjectionService(DeviceDisplayStateEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public List<DeviceDashboardView> ProjectMany(IEnumerable<Device> devices, DateTime utcNow) =>
        devices.Select(device => _evaluator.Evaluate(device, utcNow)).ToList();

    public DeviceDashboardStatsView BuildStats(
        IReadOnlyCollection<DeviceDashboardView> devices,
        int todayGames,
        int todayWins,
        int todayLosses,
        int completedCount)
    {
        return new DeviceDashboardStatsView
        {
            OnlineCount = devices.Count(device => !string.Equals(device.DisplayStatus, "Offline", StringComparison.Ordinal)),
            TotalCount = devices.Count,
            TodayGames = todayGames,
            TodayWins = todayWins,
            TodayLosses = todayLosses,
            AbnormalCount = devices.Count(device => string.Equals(device.Bucket, "abnormal", StringComparison.Ordinal)),
            CompletedCount = completedCount
        };
    }
}
```

```csharp
builder.Services.AddSingleton<DeviceDisplayStateEvaluator>();
builder.Services.AddSingleton<DeviceDashboardProjectionService>();
```

```csharp
var utcNow = DateTime.UtcNow;
var projected = _projection.ProjectMany(visibleDevices, utcNow);
return Ok(projected);
```

```csharp
var stats = _projection.BuildStats(projected, todayGames?.Count ?? 0, todayGames?.Wins ?? 0, todayGames?.Losses ?? 0, completedSnapshots.Count);
return Ok(stats);
```

```csharp
var view = _projection.Project(device, DateTime.UtcNow);
await _dashboard.Clients.All.SendAsync("DeviceUpdated", view);
```

```csharp
var cutoff = DateTime.UtcNow - DeviceStatusPolicy.OfflineTimeout;
```

Implementation note: if `DeviceDashboardProjectionService` does not already expose `Project(Device, DateTime)`, add it as a thin wrapper around `_evaluator.Evaluate(...)`.

- [ ] **Step 4: Run the projection tests and build the cloud server**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDashboardProjectionServiceTests -v minimal`
Expected: PASS

Run: `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj -v minimal`
Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit the backend projection wiring**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/CloudDbContextTestFactory.cs BotCore.Tests/Cloud/DeviceDashboardProjectionServiceTests.cs HearthBot.Cloud/Models/DeviceDashboardStatsView.cs HearthBot.Cloud/Services/DeviceDashboardProjectionService.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Controllers/DeviceController.cs HearthBot.Cloud/Hubs/BotHub.cs HearthBot.Cloud/Services/DeviceWatchdog.cs
git commit -m "feat: 统一云控状态投影与统计来源"
```

## Task 4: Make The Vue Dashboard Consume Backend Display Fields First

**Files:**
- Modify: `hearthbot-web/src/types.ts`
- Modify: `hearthbot-web/src/utils/dashboardState.ts`
- Modify: `hearthbot-web/src/utils/dashboardState.test.ts`
- Modify: `hearthbot-web/src/views/Dashboard.vue`
- Modify: `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
- Modify: `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`

- [ ] **Step 1: Extend the frontend tests to describe backend-first behavior**

```ts
import { describe, expect, it } from 'vitest'
import { getDeviceBucket, getDisplayStatus, isAbnormalDevice } from './dashboardState'

describe('dashboardState', () => {
  it('prefers backend bucket when present', () => {
    expect(getDeviceBucket({
      bucket: 'active',
      orderNumber: '',
      status: 'Offline',
      isCompleted: false
    } as any)).toBe('active')
  })

  it('treats switching as normal when backend says active', () => {
    expect(isAbnormalDevice({
      bucket: 'active',
      displayStatus: 'Switching',
      status: 'Switching',
      isCompleted: false
    } as any)).toBe(false)
  })

  it('falls back to legacy timeout logic when backend fields are missing', () => {
    expect(getDeviceBucket({
      orderNumber: 'A-1',
      status: 'Offline',
      isCompleted: false
    } as any)).toBe('abnormal')
  })

  it('prefers backend display status over raw status', () => {
    expect(getDisplayStatus({
      displayStatus: 'Offline',
      status: 'Running'
    } as any)).toBe('Offline')
  })
})
```

- [ ] **Step 2: Run the dashboardState tests and verify they fail**

Run: `npm --prefix hearthbot-web test -- src/utils/dashboardState.test.ts`

Expected: FAIL because `dashboardState.ts` does not expose `getDisplayStatus` and still hardcodes `Switching` and heartbeat timeout as abnormal.

- [ ] **Step 3: Implement backend-first state consumption with a compatibility fallback**

```ts
export interface Device {
  status: string
  rawStatus?: string
  displayStatus?: string
  bucket?: DashboardBucket
  abnormalReason?: string | null
  heartbeatAgeSeconds?: number
  isHeartbeatStale?: boolean
  isSwitchingTooLong?: boolean
}
```

```ts
export function getDisplayStatus(device: Device): string {
  return device.displayStatus || device.status || 'Unknown'
}

export function isAbnormalDevice(device: Device, now = Date.now()): boolean {
  if (device.isCompleted) return false
  if (device.bucket) return device.bucket === 'abnormal'
  return device.status === 'Offline' || device.status === 'Switching' || isHeartbeatStale(device, now)
}

export function getDeviceBucket(device: Device, now = Date.now()): DashboardBucket {
  if (device.bucket) return device.bucket
  if (device.isCompleted) return 'completed'
  if (isAbnormalDevice(device, now)) return 'abnormal'
  if (!device.orderNumber) return 'pending'
  return 'active'
}
```

```vue
<NTag :type="statusInfo.type" size="small" round>{{ statusInfo.label }}</NTag>
```

```ts
const displayStatus = computed(() => getDisplayStatus(props.device))
const statusInfo = computed(() => {
  const map = {
    InGame: { label: '对局中', type: 'success' },
    Running: { label: '运行中', type: 'success' },
    Switching: { label: '切换中', type: 'warning' },
    Idle: { label: '空闲', type: 'info' },
    Offline: { label: '离线', type: 'error' },
    Completed: { label: '已完成', type: 'success' }
  }
  return map[displayStatus.value as keyof typeof map] ?? { label: displayStatus.value || '未知', type: 'default' as const }
})
```

```ts
if (props.bucket === 'abnormal') {
  if (props.device.abnormalReason === 'HeartbeatTimeout') return '设备离线或心跳超时，请优先检查'
  if (props.device.abnormalReason === 'SwitchingTooLong') return '切号耗时过长，请检查脚本是否卡住'
}
```

Implementation note: keep `nowTick` only if the fallback path still needs it. Do not delete compatibility code until the dashboard renders correctly with both old and new payloads.

- [ ] **Step 4: Run the updated frontend tests and build**

Run: `npm --prefix hearthbot-web test -- src/utils/dashboardState.test.ts`
Expected: PASS

Run: `npm --prefix hearthbot-web run build`
Expected: Vite build succeeds and emits updated assets

- [ ] **Step 5: Commit the frontend dashboard alignment**

```bash
git add hearthbot-web/src/types.ts hearthbot-web/src/utils/dashboardState.ts hearthbot-web/src/utils/dashboardState.test.ts hearthbot-web/src/views/Dashboard.vue hearthbot-web/src/components/dashboard/DeviceStatusCard.vue hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue
git commit -m "feat: 前端优先消费云控展示状态"
```

## Task 5: Run Full Verification Before Handoff

**Files:**
- Modify: only the files touched above if verification reveals defects

- [ ] **Step 1: Run all Cloud-focused backend tests**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotCore.Tests.Cloud" -v minimal`

Expected: PASS

- [ ] **Step 2: Run focused frontend utility tests**

Run: `npm --prefix hearthbot-web test -- src/utils/dashboardState.test.ts src/utils/browserNotifications.test.ts src/utils/completedHistory.test.ts`

Expected: PASS

- [ ] **Step 3: Build both delivery artifacts**

Run: `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj -v minimal`
Expected: BUILD SUCCEEDED

Run: `npm --prefix hearthbot-web run build`
Expected: Vite build succeeds

- [ ] **Step 4: Smoke-check the dashboard state flow manually**

Check:
- device with `displayStatus=InGame` lands in `进行中`
- short `Switching` stays out of `异常`
- long `Switching` lands in `异常` with the correct reason
- timed-out heartbeat becomes `Offline` and `异常`
- REST refresh and SignalR update show the same tag and bucket

- [ ] **Step 5: Commit only if verification required follow-up fixes**

```bash
git add HearthBot.Cloud BotCore.Tests hearthbot-web
git commit -m "fix: 收尾云控状态稳定性校验问题"
```

## Final Verification Checklist

- `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceHeartbeatStateTrackingTests -v minimal`
- `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDisplayStateEvaluatorTests -v minimal`
- `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter DeviceDashboardProjectionServiceTests -v minimal`
- `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotCore.Tests.Cloud" -v minimal`
- `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj -v minimal`
- `npm --prefix hearthbot-web test -- src/utils/dashboardState.test.ts`
- `npm --prefix hearthbot-web run build`

## Handoff Notes

- Keep `Device.Status` as the raw BotMain heartbeat field; do not silently repurpose it into display semantics.
- `DeviceDashboardView.Status` may mirror `RawStatus` during the compatibility window, but new UI code should read `displayStatus`.
- If a mixed-version deploy needs to be supported longer than expected, keep the `dashboardState.ts` fallback path until both REST and SignalR always return `bucket` and `displayStatus`.
- If `Switching` still looks noisy after release, adjust `DeviceStatusPolicy.SwitchingAbnormalTimeout` in one place instead of touching front-end logic.
