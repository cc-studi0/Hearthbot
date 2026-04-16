# 多选卡组按局随机 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让主界面和中控都支持多选卡组，并在每一局开始匹配前从候选池中随机选出一套卡组进入对局，同时保证本局留牌、发现和学习上下文都绑定到实际选中的卡组。

**Architecture:** 先在 `BotService` 收敛“候选卡组列表”和“本局实际卡组”两层状态，并用可测试的辅助方法实现候选池规范化、摘要展示和按局随机。随后把主界面和中控账号配置都切到同一套多选对话框与数组持久化模型，最后通过构筑自动排队入口和 `ResolveDeckContext()` 把本局实际卡组贯穿到运行时。

**Tech Stack:** C# / .NET 8 / WPF (`BotMain`) / xUnit (`BotCore.Tests`) / JSON 持久化 / Git

---

### Task 1: 先锁定候选池归一化、摘要和按局随机的纯行为

**Files:**
- Create: `BotMain/DeckSelectionState.cs`
- Create: `BotCore.Tests/DeckSelectionStateTests.cs`
- Modify: `BotCore.Tests/BotCore.Tests.csproj`

- [ ] **Step 1: 写失败测试，覆盖候选池清洗、摘要和随机结果范围**

```csharp
public class DeckSelectionStateTests
{
    [Fact]
    public void Normalize_UsesLegacyDeckWhenArrayMissing()
    {
        var result = DeckSelectionState.Normalize(null, "星灵法 (Mage)");

        Assert.Equal(new[] { "星灵法 (Mage)" }, result);
    }

    [Fact]
    public void Normalize_RemovesEmptyAndDuplicateDecks()
    {
        var result = DeckSelectionState.Normalize(
            new[] { "", "星灵法 (Mage)", "星灵法 (Mage)", "机械猎 (Hunter)" },
            null);

        Assert.Equal(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" }, result);
    }

    [Fact]
    public void BuildSummary_ReturnsAutoForEmptySelection()
    {
        Assert.Equal("(auto)", DeckSelectionState.BuildSummary(Array.Empty<string>()));
    }

    [Fact]
    public void BuildSummary_ReturnsRandomCountForMultipleDecks()
    {
        Assert.Equal("随机(3)", DeckSelectionState.BuildSummary(new[]
        {
            "星灵法 (Mage)",
            "机械猎 (Hunter)",
            "污手骑 (Paladin)"
        }));
    }

    [Fact]
    public void ChooseActiveDeck_ReturnsOnlyCandidateDeck()
    {
        var deck = DeckSelectionState.ChooseActiveDeck(
            new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
            new Random(0));

        Assert.Contains(deck, new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });
    }
}
```

- [ ] **Step 2: 运行测试，确认类型尚不存在**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~DeckSelectionStateTests"`

Expected: FAIL，提示 `DeckSelectionState` 未定义

- [ ] **Step 3: 实现最小辅助类**

在 `BotMain/DeckSelectionState.cs` 中实现：

```csharp
internal static class DeckSelectionState
{
    internal static IReadOnlyList<string> Normalize(IEnumerable<string> selectedDecks, string legacyDeckName = null)
    {
        // 去空、去重；如果数组为空则回退 legacyDeckName；仍为空则返回空数组
    }

    internal static string BuildSummary(IReadOnlyList<string> selectedDecks)
    {
        // 0 -> "(auto)", 1 -> deckName, 2+ -> $"随机({count})"
    }

    internal static string ChooseActiveDeck(IReadOnlyList<string> selectedDecks, Random random)
    {
        // 空返回 "(auto)"，1 个直接返回，多个均匀随机
    }
}
```

- [ ] **Step 4: 把新文件链接进测试项目**

在 `BotCore.Tests/BotCore.Tests.csproj` 中添加：

```xml
<Compile Include="..\BotMain\DeckSelectionState.cs" Link="DeckSelectionState.cs" />
```

- [ ] **Step 5: 重新运行测试，确认纯行为通过**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~DeckSelectionStateTests"`

Expected: PASS

- [ ] **Step 6: 提交**

```bash
git add BotMain/DeckSelectionState.cs BotCore.Tests/DeckSelectionStateTests.cs BotCore.Tests/BotCore.Tests.csproj
git commit -m "feat: 新增卡组候选池归一化与随机辅助逻辑"
```

### Task 2: 先把 BotService 变成“候选池 + 本局实际卡组”双层状态

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotCore.Tests/DeckSelectionStateTests.cs`
- Create: `BotCore.Tests/BotServiceDeckSelectionTests.cs`

- [ ] **Step 1: 写失败测试，锁定 BotService 的按局随机入口**

```csharp
public class BotServiceDeckSelectionTests
{
    [Fact]
    public void ResolveConfiguredDeckName_ReturnsSummaryWhenMultipleDecksConfigured()
    {
        var service = new BotService();

        service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

        Assert.Equal("随机(2)", service.SelectedDeckName);
    }

    [Fact]
    public void ResolveDeckNameForQueue_PicksOnlyFromConfiguredCandidates()
    {
        var service = new BotService();
        service.SetDeckRandomFactoryForTests(() => new Random(0));
        service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

        var actual = service.ResolveDeckNameForQueueForTests();

        Assert.Contains(actual, new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });
    }
}
```

- [ ] **Step 2: 运行测试，确认多卡组接口尚不存在**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceDeckSelectionTests"`

Expected: FAIL，提示 `SetDecksByName` / `ResolveDeckNameForQueueForTests` 未定义

- [ ] **Step 3: 在 BotService 中加入候选池与本局卡组状态**

在 `BotMain/BotService.cs` 中新增最小字段与接口：

```csharp
private List<string> _selectedDecks = new() { "(auto)" };
private string _activeMatchDeck = "(auto)";
private Func<Random> _deckRandomFactory = () => new Random();

public string SelectedDeckName => DeckSelectionState.BuildSummary(_selectedDecks);

public void SetDecksByName(IEnumerable<string> deckNames)
{
    _selectedDecks = DeckSelectionState.Normalize(deckNames).ToList();
    _activeMatchDeck = _selectedDecks.Count == 1 ? _selectedDecks[0] : "(auto)";
}

internal string ResolveDeckNameForQueueForTests() => ResolveDeckNameForQueue();
internal void SetDeckRandomFactoryForTests(Func<Random> factory) => _deckRandomFactory = factory ?? (() => new Random());
```

并保留旧接口兼容：

```csharp
public void SetDeckByName(string name) => SetDecksByName(new[] { name });
public void SetRunConfiguration(int modeIndex, string deckName, string mulliganProfile, string discoverProfile = null)
{
    // 仍保留旧签名，内部走 SetDeckByName
}
```

- [ ] **Step 4: 实现本局卡组解析 helper**

在 `BotService.cs` 中新增：

```csharp
private string ResolveDeckNameForQueue()
{
    var normalized = DeckSelectionState.Normalize(_selectedDecks);
    _selectedDecks = normalized.ToList();
    _activeMatchDeck = DeckSelectionState.ChooseActiveDeck(normalized, _deckRandomFactory());
    return _activeMatchDeck;
}
```

- [ ] **Step 5: 让 `ResolveDeckContext()` 优先使用 `_activeMatchDeck`**

把原来查 `_selectedDeck` 的逻辑改成优先查 `_activeMatchDeck`，没有实际卡组时再回退候选池单元素。

- [ ] **Step 6: 运行测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~DeckSelectionStateTests|FullyQualifiedName~BotServiceDeckSelectionTests"`

Expected: PASS

- [ ] **Step 7: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/DeckSelectionStateTests.cs BotCore.Tests/BotServiceDeckSelectionTests.cs
git commit -m "feat: 为 BotService 引入候选卡组池和按局随机状态"
```

### Task 3: 把构筑自动排队改成每局开排前随机一次，并写清日志

**Files:**
- Modify: `BotMain/BotService.cs`
- Modify: `BotCore.Tests/BotServiceDeckSelectionTests.cs`

- [ ] **Step 1: 写失败测试，锁定本局卡组会在自动排队前刷新**

```csharp
[Fact]
public void BeginMatch_UsesResolvedActiveDeckContext()
{
    var service = new BotService();
    service.SetDeckRandomFactoryForTests(() => new Random(0));
    service.SetDecksByName(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" });

    var selected = service.ResolveDeckNameForQueueForTests();

    Assert.Equal(selected, service.ActiveMatchDeckNameForTests);
}
```

- [ ] **Step 2: 运行测试，确认还没有暴露活动卡组状态**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceDeckSelectionTests"`

Expected: FAIL，提示 `ActiveMatchDeckNameForTests` 未定义

- [ ] **Step 3: 在自动排队入口接入本局随机**

把 `BotService.AutoQueueConstructedByUi()` 中：

```csharp
var deckName = StripClassSuffix(_selectedDeck);
```

改成：

```csharp
var selectedDeck = ResolveDeckNameForQueue();
var deckName = StripClassSuffix(selectedDeck);
Log($"[DeckRandom] 候选={_selectedDecks.Count} 实际={selectedDeck}");
```

并在测试辅助里暴露：

```csharp
internal string ActiveMatchDeckNameForTests => _activeMatchDeck;
```

- [ ] **Step 4: 确保每局开始时当前上下文绑定到本局实际卡组**

检查并修正：

- `BeginMatchSession()` 中的 `_currentDeckContext = ResolveDeckContext(null);`
- 留牌前的 `_currentDeckContext = ResolveDeckContext(null) ?? _currentDeckContext;`

都应以 `_activeMatchDeck` 为准。

- [ ] **Step 5: 运行测试并编译**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~BotServiceDeckSelectionTests"`

Expected: PASS

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/BotService.cs BotCore.Tests/BotServiceDeckSelectionTests.cs
git commit -m "feat: 构筑自动排队支持每局随机选择候选卡组"
```

### Task 4: 接入主界面配置，支持多选对话框和数组持久化

**Files:**
- Create: `BotMain/DeckSelectionDialog.xaml`
- Create: `BotMain/DeckSelectionDialog.xaml.cs`
- Modify: `BotMain/MainWindow.xaml`
- Modify: `BotMain/MainViewModel.cs`

- [ ] **Step 1: 写失败测试，先锁定 ViewModel 层的持久化辅助行为**

```csharp
[Fact]
public void NormalizeDeckSelection_PrefersArrayOverLegacyDeckName()
{
    var result = DeckSelectionState.Normalize(
        new[] { "星灵法 (Mage)", "机械猎 (Hunter)" },
        "旧卡组 (Mage)");

    Assert.Equal(new[] { "星灵法 (Mage)", "机械猎 (Hunter)" }, result);
}
```

- [ ] **Step 2: 运行已有 `DeckSelectionStateTests`，确认辅助行为已覆盖**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~DeckSelectionStateTests"`

Expected: PASS

- [ ] **Step 3: 新增多选对话框**

在 `DeckSelectionDialog` 中提供：

- 全部卡组复选列表
- `确定` / `取消`
- 初始化时勾选现有候选池
- 结果输出 `SelectedDeckNames`

- [ ] **Step 4: 主界面改成摘要 + 多选按钮**

在 `MainWindow.xaml` 中：

- 把 `ComboBoxDeck` 的主体用途改为摘要显示或隐藏
- 新增绑定 `DeckSelectionSummary`
- 新增 `SelectDecksCmd`

在 `MainViewModel.cs` 中：

- 新增 `SelectedDeckNames`
- 新增 `DeckSelectionSummary`
- 新增 `SelectDecksCmd`
- `SaveSettings()` 写入 `SelectedDeckNames`
- `LoadSettings()` 优先读取 `SelectedDeckNames`，否则回退 `DeckName`
- 启动时改为 `_bot.SetDecksByName(SelectedDeckNames)`

- [ ] **Step 5: 编译主项目**

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 6: 提交**

```bash
git add BotMain/DeckSelectionDialog.xaml BotMain/DeckSelectionDialog.xaml.cs BotMain/MainWindow.xaml BotMain/MainViewModel.cs
git commit -m "feat: 主界面支持多选卡组并保存候选池"
```

### Task 5: 接入中控账号配置，支持多选卡组和兼容旧 accounts.json

**Files:**
- Modify: `BotMain/AccountEntry.cs`
- Modify: `BotMain/AccountQueuePersistence.cs`
- Modify: `BotMain/AccountEditDialog.xaml`
- Modify: `BotMain/AccountEditDialog.xaml.cs`
- Modify: `BotMain/AccountController.cs`
- Modify: `BotMain/AccountControllerWindow.xaml`

- [ ] **Step 1: 写失败测试，锁定账号候选池摘要行为**

```csharp
[Fact]
public void BuildSummary_CanBeReusedForAccountDeckDisplay()
{
    var summary = DeckSelectionState.BuildSummary(new[]
    {
        "星灵法 (Mage)",
        "机械猎 (Hunter)"
    });

    Assert.Equal("随机(2)", summary);
}
```

- [ ] **Step 2: 运行 `DeckSelectionStateTests`，确认账号摘要可复用同一辅助层**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~DeckSelectionStateTests"`

Expected: PASS

- [ ] **Step 3: 账号模型改成数组 + 摘要**

在 `AccountEntry.cs` 中新增：

- `SelectedDeckNames`
- `DeckSummary`

并让旧 `DeckName` 兼容输出“首个候选卡组”。

- [ ] **Step 4: 持久化优先读写数组**

在 `AccountQueuePersistence.cs` 中：

- 读 `SelectedDeckNames`
- 没有数组时回退 `DeckName`
- 保存时同时写 `SelectedDeckNames` 和兼容字段 `DeckName`

- [ ] **Step 5: 编辑对话框与账号应用改走多选**

在 `AccountEditDialog` 中：

- 用摘要 + `多选` 按钮替代原来的 `DeckCombo`
- 调用 `DeckSelectionDialog`

在 `AccountController.ApplyAccountSettings()` 中：

```csharp
if (account.SelectedDeckNames?.Count > 0)
    _bot.SetDecksByName(account.SelectedDeckNames);
else if (!string.IsNullOrWhiteSpace(account.DeckName))
    _bot.SetDeckByName(account.DeckName);
```

- [ ] **Step 6: 编译主项目**

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 7: 提交**

```bash
git add BotMain/AccountEntry.cs BotMain/AccountQueuePersistence.cs BotMain/AccountEditDialog.xaml BotMain/AccountEditDialog.xaml.cs BotMain/AccountController.cs BotMain/AccountControllerWindow.xaml
git commit -m "feat: 中控账号配置支持多选卡组和按局随机候选池"
```

### Task 6: 全量验证、回归和最终提交推送

**Files:**
- Modify: `docs/superpowers/specs/2026-04-16-multi-deck-random-queue-design.md`（仅当实现偏离设计时）

- [ ] **Step 1: 跑测试**

Run: `dotnet test BotCore.Tests/BotCore.Tests.csproj`

Expected: All tests pass

- [ ] **Step 2: 编译主项目**

Run: `dotnet build BotMain/BotMain.csproj`

Expected: Build succeeded

- [ ] **Step 3: 做手动回归**

至少验证：

1. 主界面可勾选多个卡组并保存
2. 连续两局前日志能看到不同的实际卡组
3. 中控编辑账号后，重启程序仍保留候选池
4. 老 `settings.json` / `accounts.json` 只有 `DeckName` 时仍可运行
5. 留牌或发现阶段使用的是本局实际卡组上下文

- [ ] **Step 4: 如实现与 spec 不一致，补文档**

只在真实实现和设计有偏差时更新：

`docs/superpowers/specs/2026-04-16-multi-deck-random-queue-design.md`

- [ ] **Step 5: 最终提交**

```bash
git add BotMain BotCore.Tests docs/superpowers/specs/2026-04-16-multi-deck-random-queue-design.md
git commit -m "feat: 支持多选卡组并在每局开始前随机选择"
```

- [ ] **Step 6: 推送**

```bash
git push
```
