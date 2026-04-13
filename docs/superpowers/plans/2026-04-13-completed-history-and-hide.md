# Completed History And Hide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 7-day frozen completed-order history plus manual hide behavior for real-time device cards without affecting BotMain’s local account queue.

**Architecture:** Keep `Device` as the real-time machine/session row, and add two new persistence models: one for frozen completed-order snapshots and one for hidden live-card identities. The dashboard’s first three tabs continue reading filtered `Device` rows, while the `已完成` tab switches to a separate completed-snapshot query.

**Tech Stack:** ASP.NET Core 8, EF Core Sqlite, SignalR, Vue 3, TypeScript, Naive UI, Vite, Vitest, xUnit

---

## Notes Before Starting

- Save all edited files as UTF-8.
- Use Chinese git commit messages.
- Follow `@test-driven-development`.
- Do not hand-edit `HearthBot.Cloud/wwwroot/assets/*`; always rebuild from `hearthbot-web`.
- Reuse the approved spec at `docs/superpowers/specs/2026-04-13-completed-history-and-hide-design.md`.
- This plan is intentionally scoped to the new “7-day completed history + hide” behavior. Do not re-open unrelated dashboard design decisions.

## File Structure

### Backend files to modify

- `HearthBot.Cloud/Data/CloudDbContext.cs`
  Register new tables and indexes for completed snapshots and hidden live entries.
- `HearthBot.Cloud/Program.cs`
  Add compatibility `ALTER TABLE` / `CREATE TABLE IF NOT EXISTS` bootstrap logic for the new persistence models.
- `HearthBot.Cloud/Services/DeviceManager.cs`
  Create a frozen completion snapshot when a device is formally completed and expose real-time filtering helpers.
- `HearthBot.Cloud/Services/DeviceWatchdog.cs`
  Delete expired completed snapshots and stale hidden-entry rows.
- `HearthBot.Cloud/Controllers/DeviceController.cs`
  Add a live-card hide endpoint and return filtered live devices.
- `HearthBot.Cloud/Controllers/GameRecordController.cs`
  Leave existing game-history endpoints intact; no history logic should leak into completed snapshots.
- `hearthbot-web/src/api/index.ts`
  Add new API calls for hiding live cards and querying / hiding completed snapshots.
- `hearthbot-web/src/types.ts`
  Add new `CompletedOrderSnapshot` and hidden-state related types.
- `hearthbot-web/src/utils/dashboardState.ts`
  Split live-device grouping from completed-history grouping.
- `hearthbot-web/src/views/Dashboard.vue`
  Switch the `已完成` tab to completed snapshots and wire hide actions.

### Backend files to create

- `HearthBot.Cloud/Models/CompletedOrderSnapshot.cs`
  Frozen 7-day completed-order record.
- `HearthBot.Cloud/Models/HiddenDeviceEntry.cs`
  Temporary hide record keyed by live device identity.
- `HearthBot.Cloud/Controllers/CompletedOrderController.cs`
  Query and hide completed snapshots.
- `HearthBot.Cloud/Services/CompletedOrderService.cs`
  Encapsulate completed-snapshot creation, querying, and soft-delete behavior.
- `HearthBot.Cloud/Services/HiddenDeviceService.cs`
  Encapsulate real-time hide behavior and hide-key invalidation logic.

### Test files to create or modify

- `BotCore.Tests/BotCore.Tests.csproj`
  Link new Cloud source files into the existing test project.
- `BotCore.Tests/Cloud/CompletedOrderSnapshotTests.cs`
  Covers snapshot creation, 7-day retention, and independence from `Device`.
- `BotCore.Tests/Cloud/HiddenDeviceEntryTests.cs`
  Covers hide-on-live-device and hide invalidation on identity changes.
- `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
  Extend helper if needed for new services.

### Frontend files to create

- `hearthbot-web/src/components/dashboard/CompletedOrderCard.vue`
  Frozen completed-history card for the `已完成` tab.
- `hearthbot-web/src/utils/completedHistory.ts`
  Front-end helpers for display-only formatting of retention windows.
- `hearthbot-web/src/utils/completedHistory.test.ts`
  Covers remaining-days display and filtering.

### Frontend files to modify

- `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
  Add a live-card `隐藏` action.
- `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`
  Add hide-live-card action and remove any assumption that `已完成` is sourced from `Device`.
- `hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue`
  Use counts from live devices plus completed snapshots.
- `hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue`
  Change completion stat copy to 7-day completed history if needed.

## Task 1: Persist Frozen Completed Snapshots

**Files:**
- Create: `HearthBot.Cloud/Models/CompletedOrderSnapshot.cs`
- Create: `HearthBot.Cloud/Services/CompletedOrderService.cs`
- Create: `BotCore.Tests/Cloud/CompletedOrderSnapshotTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `BotCore.Tests/Cloud/CloudDbContextTestFactory.cs`
- Modify: `HearthBot.Cloud/Data/CloudDbContext.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`

- [ ] **Step 1: Write the failing snapshot tests**

```csharp
[Fact]
public async Task MarkOrderCompleted_CreatesFrozenSnapshotForSevenDays()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    env.Db.Devices.Add(new Device
    {
        DeviceId = "pc-01",
        DisplayName = "机器1",
        CurrentAccount = "账号A",
        OrderNumber = "A-1001",
        StartRank = "钻石5",
        TargetRank = "传说",
        CurrentRank = "传说",
        CurrentDeck = "标准猎人",
        CurrentProfile = "脚本A",
        GameMode = "Standard",
        SessionWins = 12,
        SessionLosses = 5
    });
    await env.Db.SaveChangesAsync();

    var manager = env.CreateDeviceManager();
    var result = await manager.MarkOrderCompleted("pc-01", "传说");

    Assert.True(result!.WasNewlyCompleted);
    var snapshot = await env.Db.CompletedOrderSnapshots.SingleAsync();
    Assert.Equal("A-1001", snapshot.OrderNumber);
    Assert.Equal("账号A", snapshot.AccountName);
    Assert.Equal("标准猎人", snapshot.DeckName);
    Assert.Equal(snapshot.CompletedAt.AddDays(7).Date, snapshot.ExpiresAt.Date);
}

[Fact]
public async Task CompletedSnapshots_RemainAfterDeviceStartsNewOrder()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    // seed snapshot + update device to a new order after completion
    // assert the snapshot row stays unchanged
}
```

- [ ] **Step 2: Run the snapshot tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CompletedOrderSnapshotTests -v minimal`

Expected: FAIL because `CompletedOrderSnapshot` does not exist and `MarkOrderCompleted` only mutates `Device`.

- [ ] **Step 3: Implement the minimal frozen-snapshot persistence**

```csharp
public class CompletedOrderSnapshot
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string StartRank { get; set; } = string.Empty;
    public string TargetRank { get; set; } = string.Empty;
    public string CompletedRank { get; set; } = string.Empty;
    public string DeckName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string GameMode { get; set; } = string.Empty;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DateTime CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

```csharp
if (completionWasNew)
{
    db.CompletedOrderSnapshots.Add(new CompletedOrderSnapshot
    {
        DeviceId = device.DeviceId,
        DisplayName = device.DisplayName,
        OrderNumber = device.OrderNumber,
        AccountName = device.CurrentAccount,
        StartRank = device.StartRank,
        TargetRank = device.TargetRank,
        CompletedRank = device.CompletedRank,
        DeckName = device.CurrentDeck,
        ProfileName = device.CurrentProfile,
        GameMode = device.GameMode,
        Wins = device.SessionWins,
        Losses = device.SessionLosses,
        CompletedAt = completedAt,
        ExpiresAt = completedAt.AddDays(7)
    });
}
```

- [ ] **Step 4: Run the snapshot tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CompletedOrderSnapshotTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the completed-snapshot persistence**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/CloudDbContextTestFactory.cs BotCore.Tests/Cloud/CompletedOrderSnapshotTests.cs HearthBot.Cloud/Models/CompletedOrderSnapshot.cs HearthBot.Cloud/Data/CloudDbContext.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Services/CompletedOrderService.cs HearthBot.Cloud/Services/DeviceManager.cs
git commit -m "feat: 增加已完成订单7天冻结快照"
```

## Task 2: Add Hide Behavior For Live Device Cards

**Files:**
- Create: `HearthBot.Cloud/Models/HiddenDeviceEntry.cs`
- Create: `HearthBot.Cloud/Services/HiddenDeviceService.cs`
- Create: `BotCore.Tests/Cloud/HiddenDeviceEntryTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `HearthBot.Cloud/Data/CloudDbContext.cs`
- Modify: `HearthBot.Cloud/Program.cs`
- Modify: `HearthBot.Cloud/Controllers/DeviceController.cs`
- Modify: `HearthBot.Cloud/Services/DeviceManager.cs`
- Modify: `HearthBot.Cloud/Services/DeviceWatchdog.cs`

- [ ] **Step 1: Write the failing hide tests**

```csharp
[Fact]
public async Task HideLiveDevice_HidesMatchingIdentityOnly()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    env.Db.Devices.Add(new Device
    {
        DeviceId = "pc-02",
        CurrentAccount = "账号B",
        OrderNumber = "B-1002",
        Status = "Running"
    });
    await env.Db.SaveChangesAsync();

    var hidden = await env.HiddenDevices.HideAsync("pc-02", "账号B", "B-1002");
    var visible = await env.HiddenDevices.IsVisibleAsync("pc-02", "账号B", "B-1002");

    Assert.NotNull(hidden);
    Assert.False(visible);
}

[Fact]
public async Task HideEntry_BecomesIneffectiveWhenIdentityChanges()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    // seed hidden entry for account A / order A-1
    // assert a later check for account C / order C-9 returns visible
}
```

- [ ] **Step 2: Run the hide tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter HiddenDeviceEntryTests -v minimal`

Expected: FAIL because no hidden-entry model or service exists.

- [ ] **Step 3: Implement minimal live-hide behavior**

```csharp
public class HiddenDeviceEntry
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string CurrentAccount { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime HiddenAt { get; set; }
}
```

```csharp
public bool Matches(Device device, HiddenDeviceEntry hidden) =>
    hidden.DeviceId == device.DeviceId
    && string.Equals(hidden.CurrentAccount, device.CurrentAccount, StringComparison.Ordinal)
    && string.Equals(hidden.OrderNumber, device.OrderNumber, StringComparison.Ordinal);
```

```csharp
[HttpPost("{deviceId}/hide")]
public async Task<IActionResult> Hide(string deviceId, [FromBody] HideDeviceRequest request)
{
    await _hiddenDevices.HideAsync(deviceId, request.CurrentAccount, request.OrderNumber);
    return NoContent();
}
```

- [ ] **Step 4: Run the hide tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter HiddenDeviceEntryTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the live-hide behavior**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/HiddenDeviceEntryTests.cs HearthBot.Cloud/Models/HiddenDeviceEntry.cs HearthBot.Cloud/Data/CloudDbContext.cs HearthBot.Cloud/Program.cs HearthBot.Cloud/Services/HiddenDeviceService.cs HearthBot.Cloud/Controllers/DeviceController.cs HearthBot.Cloud/Services/DeviceWatchdog.cs HearthBot.Cloud/Services/DeviceManager.cs
git commit -m "feat: 增加实时设备卡隐藏能力"
```

## Task 3: Expose Completed-History Query And Soft-Hide Endpoints

**Files:**
- Create: `HearthBot.Cloud/Controllers/CompletedOrderController.cs`
- Modify: `HearthBot.Cloud/Services/CompletedOrderService.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`
- Modify: `BotCore.Tests/Cloud/CompletedOrderSnapshotTests.cs`

- [ ] **Step 1: Write the failing completed-history API tests**

```csharp
[Fact]
public async Task GetVisibleCompletedOrders_ReturnsOnlyNonDeletedAndNonExpiredRows()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    // seed one active snapshot, one deleted snapshot, one expired snapshot
    var rows = await env.CompletedOrders.GetVisibleAsync(DateTime.UtcNow);
    Assert.Single(rows);
}

[Fact]
public async Task HideCompletedSnapshot_SetsDeletedAtWithoutRemovingRow()
{
    await using var env = await CloudTestEnvironment.CreateAsync();
    // seed row, call HideAsync(id), assert DeletedAt != null and row still exists
}
```

- [ ] **Step 2: Run the completed-history tests to verify they fail**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CompletedOrderSnapshotTests -v minimal`

Expected: FAIL because visible-query and soft-hide behavior do not exist.

- [ ] **Step 3: Implement completed-history queries and hide endpoints**

```csharp
[HttpGet]
public async Task<IActionResult> GetAll()
{
    var rows = await _completedOrders.GetVisibleAsync(DateTime.UtcNow);
    return Ok(rows);
}

[HttpPost("{id}/hide")]
public async Task<IActionResult> Hide(int id)
{
    var row = await _completedOrders.HideAsync(id, DateTime.UtcNow);
    return row == null ? NotFound() : Ok(row);
}
```

```csharp
public Task<List<CompletedOrderSnapshot>> GetVisibleAsync(DateTime now) =>
    _db.CompletedOrderSnapshots
        .Where(x => x.DeletedAt == null && x.ExpiresAt > now)
        .OrderByDescending(x => x.CompletedAt)
        .ToListAsync();
```

- [ ] **Step 4: Run the completed-history tests again**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter CompletedOrderSnapshotTests -v minimal`

Expected: PASS

- [ ] **Step 5: Commit the completed-history API**

```bash
git add BotCore.Tests/BotCore.Tests.csproj BotCore.Tests/Cloud/CompletedOrderSnapshotTests.cs HearthBot.Cloud/Controllers/CompletedOrderController.cs HearthBot.Cloud/Services/CompletedOrderService.cs
git commit -m "feat: 增加已完成快照查询与移除接口"
```

## Task 4: Update Frontend State And Completed-History UI

**Files:**
- Create: `hearthbot-web/src/components/dashboard/CompletedOrderCard.vue`
- Create: `hearthbot-web/src/utils/completedHistory.ts`
- Create: `hearthbot-web/src/utils/completedHistory.test.ts`
- Modify: `hearthbot-web/src/api/index.ts`
- Modify: `hearthbot-web/src/types.ts`
- Modify: `hearthbot-web/src/utils/dashboardState.ts`
- Modify: `hearthbot-web/src/components/dashboard/DeviceStatusCard.vue`
- Modify: `hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue`
- Modify: `hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue`
- Modify: `hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue`
- Modify: `hearthbot-web/src/views/Dashboard.vue`

- [ ] **Step 1: Write the failing frontend history tests**

```ts
import { describe, expect, it } from 'vitest'
import { getRemainingRetentionDays } from './completedHistory'

describe('completedHistory', () => {
  it('rounds up remaining retention window for active snapshots', () => {
    expect(getRemainingRetentionDays('2026-04-20T12:00:00Z', new Date('2026-04-13T13:00:00Z'))).toBe(7)
  })
})
```

```ts
it('does not count completed snapshots in live-device buckets', () => {
  expect(countDevicesByBucket([], Date.now()).completed).toBe(0)
})
```

- [ ] **Step 2: Run the frontend history tests to verify they fail**

Run: `npm --prefix hearthbot-web run test -- src/utils/completedHistory.test.ts`

Expected: FAIL because completed-history helpers and snapshot UI types do not exist.

- [ ] **Step 3: Implement completed-history types and UI**

```ts
export interface CompletedOrderSnapshot {
  id: number
  deviceId: string
  displayName: string
  orderNumber: string
  accountName: string
  startRank: string
  targetRank: string
  completedRank: string
  deckName: string
  profileName: string
  gameMode: string
  wins: number
  losses: number
  completedAt: string
  expiresAt: string
  deletedAt: string | null
}
```

```ts
export function getRemainingRetentionDays(expiresAt: string, now = new Date()): number {
  const end = new Date(expiresAt).getTime()
  const diff = end - now.getTime()
  if (diff <= 0) return 0
  return Math.ceil(diff / 86_400_000)
}
```

```vue
<CompletedOrderCard
  v-for="snapshot in completedSnapshots"
  :key="snapshot.id"
  :snapshot="snapshot"
  @hide="hideCompletedSnapshot(snapshot.id)"
/></template>
```

- [ ] **Step 4: Run the frontend tests and build**

Run: `npm --prefix hearthbot-web run test`

Expected: PASS

Run: `npm --prefix hearthbot-web run build`

Expected: PASS and refreshed assets under `HearthBot.Cloud/wwwroot`

- [ ] **Step 5: Commit the completed-history UI**

```bash
git add hearthbot-web/src/api/index.ts hearthbot-web/src/types.ts hearthbot-web/src/utils/dashboardState.ts hearthbot-web/src/utils/completedHistory.ts hearthbot-web/src/utils/completedHistory.test.ts hearthbot-web/src/components/dashboard/DeviceStatusCard.vue hearthbot-web/src/components/dashboard/DeviceDetailDrawer.vue hearthbot-web/src/components/dashboard/DeviceOverviewTabs.vue hearthbot-web/src/components/dashboard/DashboardHeaderStats.vue hearthbot-web/src/components/dashboard/CompletedOrderCard.vue hearthbot-web/src/views/Dashboard.vue HearthBot.Cloud/wwwroot
git commit -m "feat: 增加已完成7天历史与实时卡隐藏界面"
```

## Task 5: Run Final Verification For Hide + 7-Day History

**Files:**
- Modify: `docs/superpowers/specs/2026-04-13-completed-history-and-hide-design.md` (only if implementation drift requires updating the approved design)

- [ ] **Step 1: Run the Cloud-specific tests**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "CompletedOrderSnapshotTests|HiddenDeviceEntryTests" -v minimal`

Expected: PASS

- [ ] **Step 2: Run the full frontend utility suite and production build**

Run: `npm --prefix hearthbot-web run test`

Expected: PASS

Run: `npm --prefix hearthbot-web run build`

Expected: PASS

- [ ] **Step 3: Run the backend app build**

Run: `dotnet build HearthBot.Cloud/HearthBot.Cloud.csproj -v minimal`

Expected: PASS

- [ ] **Step 4: Manually smoke-test the two new operator flows**

Run:

```bash
dotnet run --project HearthBot.Cloud/HearthBot.Cloud.csproj
```

Then verify manually:

- A live `进行中` or `异常` card can be hidden and disappears immediately
- The same device returns after a later identity-changing heartbeat
- Formal completion creates a frozen `已完成` card
- A device can start a new order while the old frozen completed card still remains visible
- A completed card can be removed from `已完成` without affecting the live device card

- [ ] **Step 5: Commit the final verified implementation**

```bash
git add -A
git commit -m "feat: 增加已完成保留与手动隐藏功能"
```
