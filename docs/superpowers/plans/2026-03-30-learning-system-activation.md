# 学习系统激活与渐进独立 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 激活现有学习管线，增加一致率跟踪、局面评估学习、特征评分层和独立运行门控，使脚本能从盒子学习并逐步独立。

**Architecture:** 现有学习管线（Coordinator→Trainer→Store→Runtime）已完整接线到 BotService。本计划在此基础上新增四层能力：ConsistencyTracker（P1）、ActionPatternClassifier + LearnedEvalWeights（P2）、LinearScoringModel（P3）、ReadinessMonitor（P4）。每层通过接口与现有系统解耦。

**Tech Stack:** C# .NET 8.0, SQLite (Microsoft.Data.Sqlite), xunit

---

## 现有代码要点

学习管线已在 BotService 中完整接线：
- 动作采样: `BotService.cs:798-799` (`TryEnqueueActionLearning`)
- 留牌采样: `BotService.cs:855-856` (`TryEnqueueMulliganLearning`)
- 选择采样: `BotService.cs:879-880` (`TryEnqueueChoiceLearning`)
- 发现采样: `BotService.cs:903-904` (`TryEnqueueDiscoverLearning`)
- 结果反馈: `BotService.cs:1531-1532` (`ApplyMatchOutcome`)
- 运行时应用: `BotService.cs:6038-6044` (action patches), `6133-6137` (mulligan), `6164-6167` (discover), `6190-6194` (choice)
- UI设置: `MainViewModel.cs:424-449` (LearnFromHsBox, UseLearnedLocalStrategy 已绑定)

测试项目 `BotCore.Tests` 已通过 `<Compile Include="..\BotMain\Learning\**\*.cs">` 引入全部 Learning 文件。

## 文件结构

### 新建文件
| 文件 | 职责 |
|------|------|
| `BotMain/Learning/ConsistencyTracker.cs` | P1: 滑动窗口一致率跟踪 |
| `BotMain/Learning/SqliteConsistencyStore.cs` | P1: 一致率数据 SQLite 持久化 |
| `BotMain/Learning/ActionPatternClassifier.cs` | P2: 从盒子动作推断评估倾向 |
| `BotMain/Learning/LearnedEvalWeights.cs` | P2: 局面评估学到权重 + EMA 更新 |
| `BotMain/Learning/FeatureVectorExtractor.cs` | P3: 局面+动作特征向量提取 |
| `BotMain/Learning/LinearScoringModel.cs` | P3: 线性评分模型 + 在线梯度下降 |
| `BotMain/Learning/ReadinessMonitor.cs` | P4: 独立运行就绪监控 |
| `BotCore.Tests/Learning/ConsistencyTrackerTests.cs` | P1 测试 |
| `BotCore.Tests/Learning/ActionPatternClassifierTests.cs` | P2 测试 |
| `BotCore.Tests/Learning/LinearScoringModelTests.cs` | P3 测试 |
| `BotCore.Tests/Learning/ReadinessMonitorTests.cs` | P4 测试 |

### 修改文件
| 文件 | 改动 |
|------|------|
| `BotMain/Learning/LearnedStrategyCoordinator.cs` | 注入 ConsistencyTracker，暴露一致率 |
| `BotMain/BotService.cs` | 一致率日志输出 + P2/P3/P4 集成 |
| `BotMain/AI/BoardEvaluator.cs` | P2: 接受并应用学到的评估权重 |
| `BotMain/Learning/LearnedStrategyRuntime.cs` | P3: 融合特征评分与规则层 |
| `BotMain/MainViewModel.cs` | P4: 一致率 + 就绪状态 UI 绑定 |
| `BotMain/Learning/SqliteLearnedStrategyStore.cs` | P2: 评估权重表 + P3: 模型权重表 |

---

## P1: 一致率跟踪与日志

### Task 1: ConsistencyTracker 核心逻辑

**Files:**
- Create: `BotMain/Learning/ConsistencyTracker.cs`
- Test: `BotCore.Tests/Learning/ConsistencyTrackerTests.cs`

- [ ] **Step 1: 编写 ConsistencyTracker 测试**

```csharp
// BotCore.Tests/Learning/ConsistencyTrackerTests.cs
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ConsistencyTrackerTests
    {
        [Fact]
        public void RecordMatch_ReturnsCorrectRate()
        {
            var tracker = new ConsistencyTracker(windowSize: 5);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, false);

            var rate = tracker.GetRate(ConsistencyDimension.Action);
            Assert.True(Math.Abs(rate - 66.67) < 0.1);
        }

        [Fact]
        public void SlidingWindow_EvictsOldEntries()
        {
            var tracker = new ConsistencyTracker(windowSize: 3);
            tracker.Record(ConsistencyDimension.Action, false); // will be evicted
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, true);

            Assert.Equal(100.0, tracker.GetRate(ConsistencyDimension.Action));
        }

        [Fact]
        public void EmptyTracker_ReturnsZero()
        {
            var tracker = new ConsistencyTracker(windowSize: 200);
            Assert.Equal(0.0, tracker.GetRate(ConsistencyDimension.Action));
        }

        [Fact]
        public void MultipleDimensions_TrackIndependently()
        {
            var tracker = new ConsistencyTracker(windowSize: 100);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Mulligan, false);

            Assert.Equal(100.0, tracker.GetRate(ConsistencyDimension.Action));
            Assert.Equal(0.0, tracker.GetRate(ConsistencyDimension.Mulligan));
        }

        [Fact]
        public void TotalCount_TracksAllRecords()
        {
            var tracker = new ConsistencyTracker(windowSize: 100);
            tracker.Record(ConsistencyDimension.Action, true);
            tracker.Record(ConsistencyDimension.Action, false);
            tracker.Record(ConsistencyDimension.Action, true);

            Assert.Equal(3, tracker.GetTotalCount(ConsistencyDimension.Action));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ConsistencyTrackerTests" --no-restore -v minimal`
Expected: 编译失败，ConsistencyTracker 不存在

- [ ] **Step 3: 实现 ConsistencyTracker**

```csharp
// BotMain/Learning/ConsistencyTracker.cs
using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public enum ConsistencyDimension
    {
        Action,
        Mulligan,
        Choice
    }

    public sealed class ConsistencyTracker
    {
        private readonly int _windowSize;
        private readonly object _sync = new object();
        private readonly Dictionary<ConsistencyDimension, Queue<bool>> _windows = new();
        private readonly Dictionary<ConsistencyDimension, int> _windowMatchCount = new();
        private readonly Dictionary<ConsistencyDimension, long> _totalCounts = new();

        public ConsistencyTracker(int windowSize = 200)
        {
            _windowSize = Math.Max(1, windowSize);
            foreach (ConsistencyDimension dim in Enum.GetValues(typeof(ConsistencyDimension)))
            {
                _windows[dim] = new Queue<bool>();
                _windowMatchCount[dim] = 0;
                _totalCounts[dim] = 0;
            }
        }

        public void Record(ConsistencyDimension dimension, bool isMatch)
        {
            lock (_sync)
            {
                var queue = _windows[dimension];
                queue.Enqueue(isMatch);
                if (isMatch) _windowMatchCount[dimension]++;
                _totalCounts[dimension]++;

                while (queue.Count > _windowSize)
                {
                    var evicted = queue.Dequeue();
                    if (evicted) _windowMatchCount[dimension]--;
                }
            }
        }

        public double GetRate(ConsistencyDimension dimension)
        {
            lock (_sync)
            {
                var queue = _windows[dimension];
                if (queue.Count == 0) return 0.0;
                return Math.Round(_windowMatchCount[dimension] * 100.0 / queue.Count, 2);
            }
        }

        public int GetWindowCount(ConsistencyDimension dimension)
        {
            lock (_sync) { return _windows[dimension].Count; }
        }

        public long GetTotalCount(ConsistencyDimension dimension)
        {
            lock (_sync) { return _totalCounts[dimension]; }
        }

        public ConsistencySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new ConsistencySnapshot
                {
                    ActionRate = GetRate(ConsistencyDimension.Action),
                    MulliganRate = GetRate(ConsistencyDimension.Mulligan),
                    ChoiceRate = GetRate(ConsistencyDimension.Choice),
                    ActionCount = GetWindowCount(ConsistencyDimension.Action),
                    MulliganCount = GetWindowCount(ConsistencyDimension.Mulligan),
                    ChoiceCount = GetWindowCount(ConsistencyDimension.Choice),
                    TotalActions = GetTotalCount(ConsistencyDimension.Action),
                    TotalMulligans = GetTotalCount(ConsistencyDimension.Mulligan),
                    TotalChoices = GetTotalCount(ConsistencyDimension.Choice),
                };
            }
        }
    }

    public sealed class ConsistencySnapshot
    {
        public double ActionRate { get; set; }
        public double MulliganRate { get; set; }
        public double ChoiceRate { get; set; }
        public int ActionCount { get; set; }
        public int MulliganCount { get; set; }
        public int ChoiceCount { get; set; }
        public long TotalActions { get; set; }
        public long TotalMulligans { get; set; }
        public long TotalChoices { get; set; }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ConsistencyTrackerTests" --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/ConsistencyTracker.cs BotCore.Tests/Learning/ConsistencyTrackerTests.cs
git commit -m "P1: 新增 ConsistencyTracker 一致率滑动窗口跟踪"
```

---

### Task 2: 一致率 SQLite 持久化

**Files:**
- Create: `BotMain/Learning/SqliteConsistencyStore.cs`

- [ ] **Step 1: 实现 SqliteConsistencyStore**

```csharp
// BotMain/Learning/SqliteConsistencyStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BotMain.Learning
{
    internal sealed class SqliteConsistencyStore : IDisposable
    {
        private readonly SqliteConnection _conn;

        public SqliteConsistencyStore(string dbPath = null)
        {
            dbPath ??= Path.Combine(AppPaths.LocalDataDir, "HsBoxTeacher", "consistency.db");
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS consistency_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    dimension TEXT NOT NULL,
                    is_match INTEGER NOT NULL,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                CREATE INDEX IF NOT EXISTS idx_consistency_dim_id
                    ON consistency_records(dimension, id DESC);

                CREATE TABLE IF NOT EXISTS match_outcomes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    match_id TEXT NOT NULL,
                    is_win INTEGER NOT NULL,
                    learning_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public void RecordConsistency(string dimension, bool isMatch)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO consistency_records(dimension, is_match) VALUES(@d, @m)";
            cmd.Parameters.AddWithValue("@d", dimension);
            cmd.Parameters.AddWithValue("@m", isMatch ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void RecordMatchOutcome(string matchId, bool isWin, bool learningActive)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO match_outcomes(match_id, is_win, learning_active) VALUES(@id, @w, @la)";
            cmd.Parameters.AddWithValue("@id", matchId);
            cmd.Parameters.AddWithValue("@w", isWin ? 1 : 0);
            cmd.Parameters.AddWithValue("@la", learningActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public List<bool> LoadRecentRecords(string dimension, int count)
        {
            var results = new List<bool>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT is_match FROM consistency_records
                WHERE dimension = @d
                ORDER BY id DESC LIMIT @c";
            cmd.Parameters.AddWithValue("@d", dimension);
            cmd.Parameters.AddWithValue("@c", count);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(reader.GetInt32(0) == 1);
            results.Reverse();
            return results;
        }

        public int GetTotalMatchCount()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM match_outcomes";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public double GetRecentWinRate(int count)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(AVG(CAST(is_win AS REAL)), 0)
                FROM (SELECT is_win FROM match_outcomes ORDER BY id DESC LIMIT @c)";
            cmd.Parameters.AddWithValue("@c", count);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) * 100.0 : 0.0;
        }

        public double GetLearningPhaseWinRate(int count)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(AVG(CAST(is_win AS REAL)), 0)
                FROM (SELECT is_win FROM match_outcomes WHERE learning_active = 1 ORDER BY id DESC LIMIT @c)";
            cmd.Parameters.AddWithValue("@c", count);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToDouble(result) * 100.0 : 0.0;
        }

        public void Dispose()
        {
            _conn?.Dispose();
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/SqliteConsistencyStore.cs
git commit -m "P1: 新增 SqliteConsistencyStore 一致率持久化"
```

---

### Task 3: 将 ConsistencyTracker 接入 LearnedStrategyCoordinator

**Files:**
- Modify: `BotMain/Learning/LearnedStrategyCoordinator.cs`

- [ ] **Step 1: 在 Coordinator 中注入 ConsistencyTracker**

在 `LearnedStrategyCoordinator` 的字段和构造函数中新增 `ConsistencyTracker` 和 `SqliteConsistencyStore`：

```csharp
// 在现有字段区域（约第9-16行后）新增：
private readonly ConsistencyTracker _consistencyTracker;
private readonly SqliteConsistencyStore _consistencyStore;

// 构造函数中新增初始化（约第23-25行位置）：
_consistencyStore = new SqliteConsistencyStore();
_consistencyTracker = new ConsistencyTracker(windowSize: 200);
LoadConsistencyHistory();

// 新增公开属性：
public ConsistencyTracker Consistency => _consistencyTracker;
```

- [ ] **Step 2: 添加一致率记录逻辑**

在 `EnqueueActionSample` 方法中（第40-62行），在调用 `_trainer.TryBuildActionTraining` 之前添加一致率记录：

```csharp
public void EnqueueActionSample(ActionLearningSample sample)
{
    if (sample == null)
        return;

    // 一致率记录（主线程，不入队）
    var isMatch = !string.IsNullOrEmpty(sample.TeacherAction)
        && !string.IsNullOrEmpty(sample.LocalAction)
        && string.Equals(
            NormalizeActionForComparison(sample.TeacherAction),
            NormalizeActionForComparison(sample.LocalAction),
            StringComparison.OrdinalIgnoreCase);
    _consistencyTracker.Record(ConsistencyDimension.Action, isMatch);
    try { _consistencyStore?.RecordConsistency("Action", isMatch); } catch { }

    var rate = _consistencyTracker.GetRate(ConsistencyDimension.Action);
    var turn = sample.PlanningBoard?.TurnCount ?? 0;
    Log($"[Learning] T{turn} 动作{(isMatch ? "一致 ✓" : "不一致 ✗")} (盒子:{Truncate(sample.TeacherAction, 40)} 本地:{Truncate(sample.LocalAction, 40)}) 滑动一致率:{rate:0.0}%");

    Enqueue(() =>
    {
        // ... existing training logic unchanged ...
    });
}
```

在 `EnqueueMulliganSample` 中添加类似逻辑（对比 `TeacherReplaceEntityIds` 与 `LocalReplaceEntityIds`）。

在 `EnqueueChoiceSample` 中添加类似逻辑（对比 `TeacherSelectedEntityIds` 与 `LocalSelectedEntityIds`）。

- [ ] **Step 3: 添加辅助方法**

```csharp
// 在 Coordinator 末尾（Dispose 之前）新增：

private static string NormalizeActionForComparison(string action)
{
    if (string.IsNullOrWhiteSpace(action)) return string.Empty;
    // 取动作类型和前两个参数（忽略slot等）
    var parts = action.Split('|');
    if (parts.Length >= 3) return $"{parts[0]}|{parts[1]}|{parts[2]}";
    if (parts.Length >= 2) return $"{parts[0]}|{parts[1]}";
    return parts[0];
}

private static bool AreEntityIdSetsEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
{
    if (a == null && b == null) return true;
    if (a == null || b == null) return false;
    if (a.Count != b.Count) return false;
    var setA = new HashSet<int>(a);
    return b.All(id => setA.Contains(id));
}

private static string Truncate(string s, int maxLen)
{
    if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s ?? "";
    return s.Substring(0, maxLen) + "...";
}

private void LoadConsistencyHistory()
{
    try
    {
        foreach (ConsistencyDimension dim in Enum.GetValues(typeof(ConsistencyDimension)))
        {
            var records = _consistencyStore?.LoadRecentRecords(dim.ToString(), 200);
            if (records == null) continue;
            foreach (var r in records)
                _consistencyTracker.Record(dim, r);
        }
        var snapshot = _consistencyTracker.GetSnapshot();
        Log($"[Learning] 一致率历史已加载: 动作={snapshot.ActionRate:0.0}%({snapshot.ActionCount}), 留牌={snapshot.MulliganRate:0.0}%({snapshot.MulliganCount}), 选择={snapshot.ChoiceRate:0.0}%({snapshot.ChoiceCount})");
    }
    catch (Exception ex)
    {
        Log($"[Learning] 一致率历史加载失败: {ex.Message}");
    }
}
```

- [ ] **Step 4: 在 Dispose 中清理 ConsistencyStore**

```csharp
public void Dispose()
{
    _disposed = true;
    _queueSignal.Set();
    try { _worker.Join(1000); } catch { }
    _queueSignal.Dispose();
    _consistencyStore?.Dispose();  // 新增
}
```

- [ ] **Step 5: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 6: 运行现有测试回归**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 7: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/LearnedStrategyCoordinator.cs
git commit -m "P1: ConsistencyTracker 接入 Coordinator，一致率日志输出"
```

---

### Task 4: 一致率摘要日志

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在对局结束时输出一致率摘要**

找到 `BotService.cs` 中 `ApplyMatchOutcome` 的调用位置（约第1531-1532行），在其后添加一致率摘要日志：

```csharp
// 在 _learnedStrategyCoordinator.ApplyMatchOutcome(...) 之后新增：
if (_learnFromHsBoxRecommendations)
{
    var snap = _learnedStrategyCoordinator.Consistency.GetSnapshot();
    Log($"[Learning] 对局结束一致率摘要: 动作={snap.ActionRate:0.0}%({snap.ActionCount}/{snap.TotalActions}) 留牌={snap.MulliganRate:0.0}%({snap.MulliganCount}) 选择={snap.ChoiceRate:0.0}%({snap.ChoiceCount})");
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/BotService.cs
git commit -m "P1: 对局结束时输出一致率摘要日志"
```

---

## P2: 局面评估学习

### Task 5: ActionPatternClassifier

**Files:**
- Create: `BotMain/Learning/ActionPatternClassifier.cs`
- Test: `BotCore.Tests/Learning/ActionPatternClassifierTests.cs`

- [ ] **Step 1: 编写测试**

```csharp
// BotCore.Tests/Learning/ActionPatternClassifierTests.cs
using System.Collections.Generic;
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ActionPatternClassifierTests
    {
        [Fact]
        public void ClassifyActions_FaceHeavy_DetectsHighFaceRatio()
        {
            var actions = new List<string>
            {
                "ATTACK|10|1",  // 随从打英雄(entityId=1 为敌方英雄)
                "ATTACK|11|1",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.True(signals.FaceDamageRatio > 0.8);
        }

        [Fact]
        public void ClassifyActions_AllTrade_DetectsZeroFaceRatio()
        {
            var actions = new List<string>
            {
                "ATTACK|10|20",  // 随从打随从
                "ATTACK|11|21",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.Equal(0.0, signals.FaceDamageRatio);
        }

        [Fact]
        public void ClassifyActions_ManaEfficiency()
        {
            var actions = new List<string>
            {
                "PLAY|100|0|0",
                "PLAY|101|0|1",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 2, maxMana: 7, handCount: 5);

            Assert.Equal(2, signals.CardsPlayed);
        }

        [Fact]
        public void ClassifyActions_HeroPowerUsed()
        {
            var actions = new List<string>
            {
                "HERO_POWER|5|0",
                "END_TURN"
            };

            var signals = ActionPatternClassifier.Classify(actions, enemyHeroEntityId: 1, manaAvailable: 5, maxMana: 5, handCount: 3);

            Assert.True(signals.UsedHeroPower);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ActionPatternClassifierTests" --no-restore -v minimal`
Expected: 编译失败

- [ ] **Step 3: 实现 ActionPatternClassifier**

```csharp
// BotMain/Learning/ActionPatternClassifier.cs
using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public sealed class ActionPatternSignals
    {
        public double FaceDamageRatio { get; set; }
        public int AttackCount { get; set; }
        public int FaceAttackCount { get; set; }
        public int TradeAttackCount { get; set; }
        public int CardsPlayed { get; set; }
        public bool UsedHeroPower { get; set; }
        public double ManaEfficiency { get; set; }
        public double PlayRatio { get; set; }
    }

    public static class ActionPatternClassifier
    {
        public static ActionPatternSignals Classify(
            IReadOnlyList<string> actions,
            int enemyHeroEntityId,
            int manaAvailable,
            int maxMana,
            int handCount)
        {
            var signals = new ActionPatternSignals();
            if (actions == null || actions.Count == 0)
                return signals;

            int faceAttacks = 0;
            int totalAttacks = 0;
            int cardsPlayed = 0;
            bool usedHeroPower = false;

            foreach (var action in actions)
            {
                if (string.IsNullOrWhiteSpace(action)) continue;
                var parts = action.Split('|');
                var type = parts[0].Trim().ToUpperInvariant();

                switch (type)
                {
                    case "ATTACK":
                        totalAttacks++;
                        if (parts.Length >= 3 && int.TryParse(parts[2], out var targetId) && targetId == enemyHeroEntityId)
                            faceAttacks++;
                        break;

                    case "PLAY":
                        cardsPlayed++;
                        break;

                    case "HERO_POWER":
                        usedHeroPower = true;
                        break;
                }
            }

            signals.AttackCount = totalAttacks;
            signals.FaceAttackCount = faceAttacks;
            signals.TradeAttackCount = totalAttacks - faceAttacks;
            signals.FaceDamageRatio = totalAttacks > 0 ? (double)faceAttacks / totalAttacks : 0.0;
            signals.CardsPlayed = cardsPlayed;
            signals.UsedHeroPower = usedHeroPower;
            signals.ManaEfficiency = maxMana > 0 ? Math.Max(0, 1.0 - (double)manaAvailable / maxMana) : 0.0;
            signals.PlayRatio = handCount > 0 ? (double)cardsPlayed / handCount : 0.0;

            return signals;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ActionPatternClassifierTests" --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/ActionPatternClassifier.cs BotCore.Tests/Learning/ActionPatternClassifierTests.cs
git commit -m "P2: 新增 ActionPatternClassifier 从盒子动作推断评估倾向"
```

---

### Task 6: LearnedEvalWeights（局面评估权重 + EMA 更新 + SQLite）

**Files:**
- Create: `BotMain/Learning/LearnedEvalWeights.cs`

- [ ] **Step 1: 实现 LearnedEvalWeights**

```csharp
// BotMain/Learning/LearnedEvalWeights.cs
using System;
using System.Collections.Generic;

namespace BotMain.Learning
{
    public enum EvalBucketPhase { Early, Mid, Late }
    public enum EvalBucketPosture { Ahead, Even, Behind }

    public readonly struct EvalBucketKey : IEquatable<EvalBucketKey>
    {
        public EvalBucketPhase Phase { get; }
        public EvalBucketPosture HpPosture { get; }
        public EvalBucketPosture BoardPosture { get; }

        public EvalBucketKey(EvalBucketPhase phase, EvalBucketPosture hpPosture, EvalBucketPosture boardPosture)
        {
            Phase = phase;
            HpPosture = hpPosture;
            BoardPosture = boardPosture;
        }

        public string ToKey() => $"{Phase}|{HpPosture}|{BoardPosture}";

        public static EvalBucketKey FromBoardState(int turn, int friendHp, int enemyHp, int friendMinions, int enemyMinions)
        {
            var phase = turn <= 4 ? EvalBucketPhase.Early : turn <= 7 ? EvalBucketPhase.Mid : EvalBucketPhase.Late;
            var hpPosture = (friendHp - enemyHp) > 10 ? EvalBucketPosture.Ahead
                : (enemyHp - friendHp) > 10 ? EvalBucketPosture.Behind
                : EvalBucketPosture.Even;
            var boardPosture = friendMinions > enemyMinions + 1 ? EvalBucketPosture.Ahead
                : enemyMinions > friendMinions + 1 ? EvalBucketPosture.Behind
                : EvalBucketPosture.Even;
            return new EvalBucketKey(phase, hpPosture, boardPosture);
        }

        public bool Equals(EvalBucketKey other) => Phase == other.Phase && HpPosture == other.HpPosture && BoardPosture == other.BoardPosture;
        public override bool Equals(object obj) => obj is EvalBucketKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(Phase, HpPosture, BoardPosture);
    }

    public sealed class EvalWeightSet
    {
        public float FaceBiasScale { get; set; } = 1.0f;
        public float BoardControlScale { get; set; } = 1.0f;
        public float TempoPenaltyScale { get; set; } = 1.0f;
        public float HandValueScale { get; set; } = 1.0f;
        public float HeroPowerBonusScale { get; set; } = 1.0f;
        public int SampleCount { get; set; }
    }

    public sealed class LearnedEvalWeights
    {
        private const float Alpha = 0.02f;
        private readonly object _sync = new object();
        private readonly Dictionary<string, EvalWeightSet> _buckets = new(StringComparer.Ordinal);

        public void Update(EvalBucketKey bucket, ActionPatternSignals signals)
        {
            lock (_sync)
            {
                var key = bucket.ToKey();
                if (!_buckets.TryGetValue(key, out var weights))
                {
                    weights = new EvalWeightSet();
                    _buckets[key] = weights;
                }

                // EMA: new = α * observed + (1 - α) * current
                // FaceBias: 高 faceDamageRatio → 偏打脸 → scale > 1
                weights.FaceBiasScale = Ema(weights.FaceBiasScale, 1.0f + (float)(signals.FaceDamageRatio - 0.5) * 0.6f);

                // BoardControl: 高 tradeRatio → 偏控场 → scale > 1
                var tradeRatio = signals.AttackCount > 0 ? (float)signals.TradeAttackCount / signals.AttackCount : 0.5f;
                weights.BoardControlScale = Ema(weights.BoardControlScale, 1.0f + (tradeRatio - 0.5f) * 0.6f);

                // TempoPenalty: 高 manaEfficiency → 重节奏
                weights.TempoPenaltyScale = Ema(weights.TempoPenaltyScale, 1.0f + (float)(signals.ManaEfficiency - 0.5) * 0.6f);

                // HandValue: 低 playRatio → 偏保手牌
                weights.HandValueScale = Ema(weights.HandValueScale, 1.0f + (float)(0.5 - signals.PlayRatio) * 0.4f);

                // HeroPowerBonus: 用了技能 → 偏好技能
                weights.HeroPowerBonusScale = Ema(weights.HeroPowerBonusScale, signals.UsedHeroPower ? 1.15f : 0.95f);

                weights.SampleCount++;
            }
        }

        public bool TryGet(EvalBucketKey bucket, out EvalWeightSet weights)
        {
            lock (_sync)
            {
                return _buckets.TryGetValue(bucket.ToKey(), out weights);
            }
        }

        public Dictionary<string, EvalWeightSet> GetAll()
        {
            lock (_sync)
            {
                return new Dictionary<string, EvalWeightSet>(_buckets);
            }
        }

        public void Load(Dictionary<string, EvalWeightSet> data)
        {
            lock (_sync)
            {
                _buckets.Clear();
                foreach (var kv in data)
                    _buckets[kv.Key] = kv.Value;
            }
        }

        private static float Ema(float current, float observed)
        {
            return Alpha * observed + (1f - Alpha) * current;
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/LearnedEvalWeights.cs
git commit -m "P2: 新增 LearnedEvalWeights EMA 权重更新与分桶存储"
```

---

### Task 7: 评估权重持久化（扩展 SQLite）

**Files:**
- Modify: `BotMain/Learning/SqliteLearnedStrategyStore.cs`

- [ ] **Step 1: 在 SqliteLearnedStrategyStore 中添加评估权重表**

在 `EnsureSchema()` 方法中追加建表语句（该方法内末尾）：

```sql
CREATE TABLE IF NOT EXISTS eval_weights (
    bucket_key TEXT PRIMARY KEY,
    face_bias_scale REAL NOT NULL DEFAULT 1.0,
    board_control_scale REAL NOT NULL DEFAULT 1.0,
    tempo_penalty_scale REAL NOT NULL DEFAULT 1.0,
    hand_value_scale REAL NOT NULL DEFAULT 1.0,
    hero_power_bonus_scale REAL NOT NULL DEFAULT 1.0,
    sample_count INTEGER NOT NULL DEFAULT 0
);
```

- [ ] **Step 2: 添加 SaveEvalWeights / LoadEvalWeights 方法**

```csharp
public void SaveEvalWeights(Dictionary<string, EvalWeightSet> weights)
{
    if (weights == null || weights.Count == 0) return;
    using var tx = _conn.BeginTransaction();
    try
    {
        foreach (var kv in weights)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO eval_weights(bucket_key, face_bias_scale, board_control_scale, tempo_penalty_scale, hand_value_scale, hero_power_bonus_scale, sample_count)
                VALUES(@k, @fb, @bc, @tp, @hv, @hp, @sc)
                ON CONFLICT(bucket_key) DO UPDATE SET
                    face_bias_scale = @fb, board_control_scale = @bc, tempo_penalty_scale = @tp,
                    hand_value_scale = @hv, hero_power_bonus_scale = @hp, sample_count = @sc";
            cmd.Parameters.AddWithValue("@k", kv.Key);
            cmd.Parameters.AddWithValue("@fb", kv.Value.FaceBiasScale);
            cmd.Parameters.AddWithValue("@bc", kv.Value.BoardControlScale);
            cmd.Parameters.AddWithValue("@tp", kv.Value.TempoPenaltyScale);
            cmd.Parameters.AddWithValue("@hv", kv.Value.HandValueScale);
            cmd.Parameters.AddWithValue("@hp", kv.Value.HeroPowerBonusScale);
            cmd.Parameters.AddWithValue("@sc", kv.Value.SampleCount);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    catch { tx.Rollback(); throw; }
}

public Dictionary<string, EvalWeightSet> LoadEvalWeights()
{
    var result = new Dictionary<string, EvalWeightSet>(StringComparer.Ordinal);
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = "SELECT bucket_key, face_bias_scale, board_control_scale, tempo_penalty_scale, hand_value_scale, hero_power_bonus_scale, sample_count FROM eval_weights";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        result[reader.GetString(0)] = new EvalWeightSet
        {
            FaceBiasScale = (float)reader.GetDouble(1),
            BoardControlScale = (float)reader.GetDouble(2),
            TempoPenaltyScale = (float)reader.GetDouble(3),
            HandValueScale = (float)reader.GetDouble(4),
            HeroPowerBonusScale = (float)reader.GetDouble(5),
            SampleCount = reader.GetInt32(6)
        };
    }
    return result;
}
```

- [ ] **Step 3: 构建验证 + 测试回归**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal && dotnet test BotCore.Tests --no-restore -v minimal`
Expected: 编译成功，测试全部 PASS

- [ ] **Step 4: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/SqliteLearnedStrategyStore.cs
git commit -m "P2: 评估权重 SQLite 表 + Save/Load 方法"
```

---

### Task 8: BoardEvaluator 集成学到的权重

**Files:**
- Modify: `BotMain/AI/BoardEvaluator.cs`

- [ ] **Step 1: 添加 LearnedEvalWeights 注入点**

在 `BoardEvaluator` 类中添加可选的 `LearnedEvalWeights` 字段：

```csharp
// 在 class BoardEvaluator 的字段区域（约第17-20行后）新增：
private LearnedEvalWeights _learnedWeights;

public void SetLearnedWeights(LearnedEvalWeights weights)
{
    _learnedWeights = weights;
}
```

- [ ] **Step 2: 在 Evaluate 方法中应用学到的权重**

在 `Evaluate()` 方法中（约第72行 `float score = 0;` 之后），插入权重查询逻辑：

```csharp
// 查询学到的评估权重
float learnedFaceBias = 1f, learnedBoardControl = 1f, learnedTempo = 1f, learnedHandValue = 1f;
if (_learnedWeights != null && board.FriendHero != null && board.EnemyHero != null)
{
    var evalBucket = EvalBucketKey.FromBoardState(
        board.TurnCount,
        board.FriendHero.Health + board.FriendHero.Armor,
        board.EnemyHero.Health + board.EnemyHero.Armor,
        board.FriendMinions.Count,
        board.EnemyMinions.Count);
    if (_learnedWeights.TryGet(evalBucket, out var learnedSet) && learnedSet.SampleCount >= 20)
    {
        learnedFaceBias = learnedSet.FaceBiasScale;
        learnedBoardControl = learnedSet.BoardControlScale;
        learnedTempo = learnedSet.TempoPenaltyScale;
        learnedHandValue = learnedSet.HandValueScale;
    }
}
```

然后在各评估维度中乘以对应的 learned scale：

- 场面控制（约115-119行）：`score += W_BoardControlBonus * learnedBoardControl;` （替换原来的 `score += W_BoardControlBonus;`）
- 英雄血量打脸部分（约150-157行）：在 `aggroCtx.FaceBias` 后乘以 `learnedFaceBias`
- 法力效率（约182行）：`score += board.Mana * W_WastedMana * learnedTempo;`
- 手牌（约168-176行）：在手牌得分行乘以 `learnedHandValue`

- [ ] **Step 3: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 4: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/AI/BoardEvaluator.cs
git commit -m "P2: BoardEvaluator 接受并应用学到的评估权重"
```

---

### Task 9: 将评估学习接入 Coordinator

**Files:**
- Modify: `BotMain/Learning/LearnedStrategyCoordinator.cs`

- [ ] **Step 1: 在 Coordinator 中添加评估权重管理**

```csharp
// 新增字段：
private readonly LearnedEvalWeights _evalWeights = new LearnedEvalWeights();

// 新增公开属性：
public LearnedEvalWeights EvalWeights => _evalWeights;

// 在构造函数中加载历史权重（SafeReloadRuntime 之后）：
LoadEvalWeights();
```

- [ ] **Step 2: 添加评估信号处理**

在 `EnqueueActionSample` 的 `Enqueue` lambda 内（训练逻辑之后），添加评估信号提取：

```csharp
// 在 existing training logic 之后新增：
try
{
    if (sample.PlanningBoard != null && !string.IsNullOrEmpty(sample.TeacherAction))
    {
        var board = sample.PlanningBoard;
        var enemyHeroId = board.HeroEnemy?.Id ?? 0;
        var signals = ActionPatternClassifier.Classify(
            new[] { sample.TeacherAction },
            enemyHeroId,
            board.ManaAvailable,
            board.MaxMana,
            board.Hand?.Count ?? 0);

        var bucketKey = EvalBucketKey.FromBoardState(
            board.TurnCount,
            (board.HeroFriend?.CurrentHealth ?? 30) + (board.HeroFriend?.Armor ?? 0),
            (board.HeroEnemy?.CurrentHealth ?? 30) + (board.HeroEnemy?.Armor ?? 0),
            board.MinionFriend?.Count ?? 0,
            board.MinionEnemy?.Count ?? 0);

        _evalWeights.Update(bucketKey, signals);
    }
}
catch (Exception ex)
{
    Log($"[Learning] eval signal error: {ex.Message}");
}
```

- [ ] **Step 3: 添加定期保存逻辑**

在 `WorkerLoop` 中每处理 50 个任务后保存一次评估权重：

```csharp
// WorkerLoop 方法中，work() 调用之后新增计数器逻辑：
_evalSaveCounter++;
if (_evalSaveCounter >= 50)
{
    _evalSaveCounter = 0;
    try
    {
        _store.SaveEvalWeights(_evalWeights.GetAll());
    }
    catch (Exception ex)
    {
        Log($"[Learning] eval weights save error: {ex.Message}");
    }
}
```

```csharp
// 新增字段：
private int _evalSaveCounter;

// 新增方法：
private void LoadEvalWeights()
{
    try
    {
        var data = _store.LoadEvalWeights();
        if (data.Count > 0)
        {
            _evalWeights.Load(data);
            Log($"[Learning] 评估权重已加载: {data.Count} 个桶");
        }
    }
    catch (Exception ex)
    {
        Log($"[Learning] 评估权重加载失败: {ex.Message}");
    }
}
```

- [ ] **Step 4: 将 SaveEvalWeights / LoadEvalWeights 添加到 ILearnedStrategyStore 接口**

在 `LearnedStrategyContracts.cs` 的 `ILearnedStrategyStore` 接口中新增：

```csharp
void SaveEvalWeights(Dictionary<string, EvalWeightSet> weights);
Dictionary<string, EvalWeightSet> LoadEvalWeights();
```

- [ ] **Step 5: 构建验证 + 测试回归**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal && dotnet test BotCore.Tests --no-restore -v minimal`
Expected: 编译成功，测试全部 PASS

- [ ] **Step 6: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/LearnedStrategyCoordinator.cs BotMain/Learning/LearnedStrategyContracts.cs BotMain/Learning/SqliteLearnedStrategyStore.cs
git commit -m "P2: 评估权重学习接入 Coordinator，定期持久化"
```

---

### Task 10: 在 BotService 中将评估权重传递给 BoardEvaluator

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 将评估权重注入 BoardEvaluator**

找到 `BotService` 中 `BoardEvaluator` 的创建/使用位置，在合适的初始化时机调用 `SetLearnedWeights`。

在 `BotService` 的 `SetUseLearnedLocalStrategy` 方法（约519行）中或在 `RecommendLocalActions` 方法（约6035行）中添加：

```csharp
// 在 RecommendLocalActions 方法开头新增：
if (_useLearnedLocalStrategy && _ai.Evaluator != null)
{
    _ai.Evaluator.SetLearnedWeights(_learnedStrategyCoordinator.EvalWeights);
}
```

注意：需要确认 `AIEngine` 是否暴露了 `Evaluator` 属性。如果没有，需要添加。

在 `AIEngine.cs` 中添加：

```csharp
public BoardEvaluator Evaluator => _evaluator;
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/BotService.cs BotMain/AI/AIEngine.cs
git commit -m "P2: 评估权重传递到 BoardEvaluator"
```

---

## P3: 特征评分层

### Task 11: FeatureVectorExtractor

**Files:**
- Create: `BotMain/Learning/FeatureVectorExtractor.cs`

- [ ] **Step 1: 实现特征提取**

```csharp
// BotMain/Learning/FeatureVectorExtractor.cs
using System;
using System.Collections.Generic;
using SmartBot.Plugins.API;

namespace BotMain.Learning
{
    public static class FeatureVectorExtractor
    {
        public const int BoardFeatureCount = 14;
        public const int ActionFeatureCount = 12;
        public const int TotalFeatureCount = BoardFeatureCount + ActionFeatureCount;

        public static double[] ExtractBoardFeatures(Board board)
        {
            var f = new double[BoardFeatureCount];
            if (board == null) return f;

            int maxMana = Math.Max(1, board.MaxMana);
            f[0] = (double)board.ManaAvailable / maxMana;                           // mana_ratio
            f[1] = board.MinionFriend?.Count ?? 0;                                  // friend_minion_count
            f[2] = board.MinionEnemy?.Count ?? 0;                                   // enemy_minion_count
            f[3] = SafeRatio(SumAtk(board.MinionFriend), SumAtk(board.MinionEnemy)); // board_atk_ratio
            f[4] = SafeRatio(SumHp(board.MinionFriend), SumHp(board.MinionEnemy));  // board_hp_ratio
            f[5] = ((board.HeroFriend?.CurrentHealth ?? 30) - (board.HeroEnemy?.CurrentHealth ?? 30)) / 30.0; // hero_hp_diff_norm
            f[6] = board.Hand?.Count ?? 0;                                          // hand_count
            f[7] = HasTaunt(board.MinionEnemy) ? 1.0 : 0.0;                        // has_taunt_enemy
            f[8] = board.SecretEnemy?.Count ?? 0;                                   // enemy_secret_count
            f[9] = board.TurnCount <= 4 ? 0.0 : board.TurnCount <= 7 ? 0.5 : 1.0; // turn_bucket
            f[10] = (board.HeroFriend?.Armor ?? 0) / 10.0;                         // friend_armor_norm
            f[11] = (board.HeroEnemy?.Armor ?? 0) / 10.0;                          // enemy_armor_norm
            f[12] = board.MinionFriend?.Count > board.MinionEnemy?.Count ? 1.0 : 0.0; // board_advantage
            f[13] = board.MinionEnemy?.Count > 0 && board.MinionFriend?.Count == 0 ? 1.0 : 0.0; // empty_board

            return f;
        }

        public static double[] ExtractActionFeatures(string action, Board board, int enemyHeroEntityId)
        {
            var f = new double[ActionFeatureCount];
            if (string.IsNullOrWhiteSpace(action) || board == null) return f;

            var parts = action.Split('|');
            var type = parts[0].Trim().ToUpperInvariant();

            // one-hot action type
            f[0] = type == "PLAY" ? 1.0 : 0.0;
            f[1] = type == "ATTACK" ? 1.0 : 0.0;
            f[2] = type == "HERO_POWER" ? 1.0 : 0.0;
            f[3] = type == "END_TURN" ? 1.0 : 0.0;

            // target is face?
            if (parts.Length >= 3 && int.TryParse(parts[2], out var targetId))
                f[4] = targetId == enemyHeroEntityId ? 1.0 : 0.0;

            // is trading (attack non-hero)
            if (type == "ATTACK" && f[4] == 0.0)
                f[5] = 1.0;

            // cost ratio (for PLAY actions, approximate)
            if (type == "PLAY" && parts.Length >= 2 && int.TryParse(parts[1], out var sourceId))
            {
                var card = FindCardByEntityId(board.Hand, sourceId);
                if (card != null && board.MaxMana > 0)
                    f[6] = (double)card.CurrentCost / Math.Max(1, board.MaxMana);
            }

            // remaining mana after (approximation)
            f[7] = (double)board.ManaAvailable / Math.Max(1, board.MaxMana);

            // hand count normalized
            f[8] = (board.Hand?.Count ?? 0) / 10.0;

            // board count normalized
            f[9] = (board.MinionFriend?.Count ?? 0) / 7.0;

            // target has taunt (if attacking)
            if (type == "ATTACK" && parts.Length >= 3 && int.TryParse(parts[2], out var targetId2))
            {
                var target = FindCardByEntityId(board.MinionEnemy, targetId2);
                f[10] = target?.IsTaunt == true ? 1.0 : 0.0;
            }

            // can kill target
            if (type == "ATTACK" && parts.Length >= 3)
            {
                if (int.TryParse(parts[1], out var atkId) && int.TryParse(parts[2], out var defId))
                {
                    var attacker = FindCardByEntityId(board.MinionFriend, atkId) ?? board.HeroFriend;
                    var defender = FindCardByEntityId(board.MinionEnemy, defId);
                    if (attacker != null && defender != null)
                        f[11] = attacker.CurrentAtk >= defender.CurrentHealth ? 1.0 : 0.0;
                }
            }

            return f;
        }

        public static double[] Combine(double[] boardFeatures, double[] actionFeatures)
        {
            var combined = new double[TotalFeatureCount];
            Array.Copy(boardFeatures, 0, combined, 0, BoardFeatureCount);
            Array.Copy(actionFeatures, 0, combined, BoardFeatureCount, ActionFeatureCount);
            return combined;
        }

        private static double SafeRatio(int a, int b)
        {
            if (a == 0 && b == 0) return 0.5;
            return (double)a / Math.Max(1, a + b);
        }

        private static int SumAtk(IReadOnlyList<Card> minions)
        {
            if (minions == null) return 0;
            int sum = 0;
            foreach (var m in minions) sum += Math.Max(0, m.CurrentAtk);
            return sum;
        }

        private static int SumHp(IReadOnlyList<Card> minions)
        {
            if (minions == null) return 0;
            int sum = 0;
            foreach (var m in minions) sum += Math.Max(0, m.CurrentHealth);
            return sum;
        }

        private static bool HasTaunt(IReadOnlyList<Card> minions)
        {
            if (minions == null) return false;
            foreach (var m in minions)
                if (m.IsTaunt) return true;
            return false;
        }

        private static Card FindCardByEntityId(IReadOnlyList<Card> cards, int entityId)
        {
            if (cards == null) return null;
            foreach (var c in cards)
                if (c.Id == entityId) return c;
            return null;
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/FeatureVectorExtractor.cs
git commit -m "P3: 新增 FeatureVectorExtractor 局面+动作特征提取"
```

---

### Task 12: LinearScoringModel

**Files:**
- Create: `BotMain/Learning/LinearScoringModel.cs`
- Test: `BotCore.Tests/Learning/LinearScoringModelTests.cs`

- [ ] **Step 1: 编写测试**

```csharp
// BotCore.Tests/Learning/LinearScoringModelTests.cs
using System;
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class LinearScoringModelTests
    {
        [Fact]
        public void Score_InitialWeights_ReturnsZero()
        {
            var model = new LinearScoringModel(featureCount: 4);
            var features = new double[] { 1.0, 2.0, 3.0, 4.0 };
            Assert.Equal(0.0, model.Score(features));
        }

        [Fact]
        public void UpdatePairwise_TeacherGetsHigherScore()
        {
            var model = new LinearScoringModel(featureCount: 3, learningRate: 0.1);
            var teacherFeatures = new double[] { 1.0, 0.0, 0.0 };
            var otherFeatures = new double[] { 0.0, 1.0, 0.0 };

            for (int i = 0; i < 100; i++)
                model.UpdatePairwise(teacherFeatures, otherFeatures);

            Assert.True(model.Score(teacherFeatures) > model.Score(otherFeatures));
        }

        [Fact]
        public void SerializeDeserialize_PreservesWeights()
        {
            var model = new LinearScoringModel(featureCount: 3, learningRate: 0.1);
            var teacherFeatures = new double[] { 1.0, 0.5, 0.0 };
            var otherFeatures = new double[] { 0.0, 0.5, 1.0 };

            for (int i = 0; i < 50; i++)
                model.UpdatePairwise(teacherFeatures, otherFeatures);

            var serialized = model.Serialize();
            var restored = LinearScoringModel.Deserialize(serialized);

            Assert.Equal(model.Score(teacherFeatures), restored.Score(teacherFeatures), precision: 6);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "LinearScoringModelTests" --no-restore -v minimal`
Expected: 编译失败

- [ ] **Step 3: 实现 LinearScoringModel**

```csharp
// BotMain/Learning/LinearScoringModel.cs
using System;
using System.Globalization;
using System.Text;

namespace BotMain.Learning
{
    public sealed class LinearScoringModel
    {
        private readonly double[] _weights;
        private double _bias;
        private readonly double _learningRate;
        private readonly double _margin;

        public int FeatureCount => _weights.Length;

        public LinearScoringModel(int featureCount, double learningRate = 0.001, double margin = 0.5)
        {
            _weights = new double[featureCount];
            _bias = 0.0;
            _learningRate = learningRate;
            _margin = margin;
        }

        private LinearScoringModel(double[] weights, double bias, double learningRate, double margin)
        {
            _weights = weights;
            _bias = bias;
            _learningRate = learningRate;
            _margin = margin;
        }

        public double Score(double[] features)
        {
            if (features == null || features.Length != _weights.Length) return 0.0;
            double score = _bias;
            for (int i = 0; i < _weights.Length; i++)
                score += _weights[i] * features[i];
            return score;
        }

        public void UpdatePairwise(double[] teacherFeatures, double[] otherFeatures)
        {
            if (teacherFeatures == null || otherFeatures == null) return;
            if (teacherFeatures.Length != _weights.Length || otherFeatures.Length != _weights.Length) return;

            double teacherScore = Score(teacherFeatures);
            double otherScore = Score(otherFeatures);
            double loss = _margin - teacherScore + otherScore;

            if (loss <= 0) return;

            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] += _learningRate * (teacherFeatures[i] - otherFeatures[i]);
            }
            _bias += _learningRate;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(_weights.Length);
            sb.Append('|');
            sb.Append(_learningRate.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(_margin.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(_bias.ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i < _weights.Length; i++)
            {
                sb.Append('|');
                sb.Append(_weights[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static LinearScoringModel Deserialize(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            var parts = data.Split('|');
            if (parts.Length < 4) return null;

            int featureCount = int.Parse(parts[0], CultureInfo.InvariantCulture);
            double lr = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double margin = double.Parse(parts[2], CultureInfo.InvariantCulture);
            double bias = double.Parse(parts[3], CultureInfo.InvariantCulture);

            var weights = new double[featureCount];
            for (int i = 0; i < featureCount && i + 4 < parts.Length; i++)
                weights[i] = double.Parse(parts[i + 4], CultureInfo.InvariantCulture);

            return new LinearScoringModel(weights, bias, lr, margin);
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "LinearScoringModelTests" --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/LinearScoringModel.cs BotCore.Tests/Learning/LinearScoringModelTests.cs
git commit -m "P3: 新增 LinearScoringModel 线性评分 + 在线梯度下降"
```

---

### Task 13: 特征模型持久化 + 训练接入 Coordinator

**Files:**
- Modify: `BotMain/Learning/SqliteLearnedStrategyStore.cs`
- Modify: `BotMain/Learning/LearnedStrategyCoordinator.cs`
- Modify: `BotMain/Learning/LearnedStrategyContracts.cs`

- [ ] **Step 1: 在 SqliteLearnedStrategyStore 中添加模型权重存取**

在 `EnsureSchema()` 中追加：

```sql
CREATE TABLE IF NOT EXISTS scoring_model (
    key TEXT PRIMARY KEY,
    serialized_weights TEXT NOT NULL
);
```

添加方法：

```csharp
public void SaveScoringModel(string key, string serialized)
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"INSERT INTO scoring_model(key, serialized_weights) VALUES(@k, @s)
                        ON CONFLICT(key) DO UPDATE SET serialized_weights = @s";
    cmd.Parameters.AddWithValue("@k", key);
    cmd.Parameters.AddWithValue("@s", serialized);
    cmd.ExecuteNonQuery();
}

public string LoadScoringModel(string key)
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = "SELECT serialized_weights FROM scoring_model WHERE key = @k";
    cmd.Parameters.AddWithValue("@k", key);
    return cmd.ExecuteScalar() as string;
}
```

在 `ILearnedStrategyStore` 接口中添加对应方法。

- [ ] **Step 2: 在 Coordinator 中管理 LinearScoringModel**

```csharp
// 新增字段：
private LinearScoringModel _scoringModel;

// 新增属性：
public LinearScoringModel ScoringModel => _scoringModel;

// 构造函数中加载：
LoadScoringModel();

// 新增方法：
private void LoadScoringModel()
{
    try
    {
        var serialized = _store.LoadScoringModel("action_v1");
        _scoringModel = !string.IsNullOrEmpty(serialized)
            ? LinearScoringModel.Deserialize(serialized)
            : new LinearScoringModel(FeatureVectorExtractor.TotalFeatureCount);
        Log($"[Learning] 评分模型已加载 ({_scoringModel.FeatureCount} 维)");
    }
    catch (Exception ex)
    {
        _scoringModel = new LinearScoringModel(FeatureVectorExtractor.TotalFeatureCount);
        Log($"[Learning] 评分模型加载失败，使用初始模型: {ex.Message}");
    }
}
```

- [ ] **Step 3: 在 EnqueueActionSample 的训练逻辑后添加特征模型训练**

在 `Enqueue` lambda 内（评估权重更新之后）：

```csharp
// P3: 特征模型在线训练
try
{
    if (_scoringModel != null && sample.PlanningBoard != null
        && !string.IsNullOrEmpty(sample.TeacherAction)
        && !string.IsNullOrEmpty(sample.LocalAction)
        && !string.Equals(sample.TeacherAction, sample.LocalAction, StringComparison.OrdinalIgnoreCase))
    {
        var boardFeatures = FeatureVectorExtractor.ExtractBoardFeatures(sample.PlanningBoard);
        var enemyHeroId = sample.PlanningBoard.HeroEnemy?.Id ?? 0;

        var teacherActionFeatures = FeatureVectorExtractor.ExtractActionFeatures(sample.TeacherAction, sample.PlanningBoard, enemyHeroId);
        var localActionFeatures = FeatureVectorExtractor.ExtractActionFeatures(sample.LocalAction, sample.PlanningBoard, enemyHeroId);

        var teacherCombined = FeatureVectorExtractor.Combine(boardFeatures, teacherActionFeatures);
        var localCombined = FeatureVectorExtractor.Combine(boardFeatures, localActionFeatures);

        _scoringModel.UpdatePairwise(teacherCombined, localCombined);
    }
}
catch (Exception ex)
{
    Log($"[Learning] scoring model update error: {ex.Message}");
}
```

- [ ] **Step 4: 定期保存模型（复用 _evalSaveCounter）**

在已有的评估权重保存逻辑旁边追加：

```csharp
if (_scoringModel != null)
{
    try { _store.SaveScoringModel("action_v1", _scoringModel.Serialize()); }
    catch { }
}
```

- [ ] **Step 5: 构建验证 + 测试回归**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal && dotnet test BotCore.Tests --no-restore -v minimal`
Expected: 编译成功，测试全部 PASS

- [ ] **Step 6: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/SqliteLearnedStrategyStore.cs BotMain/Learning/LearnedStrategyCoordinator.cs BotMain/Learning/LearnedStrategyContracts.cs
git commit -m "P3: 特征评分模型接入 Coordinator，在线训练+持久化"
```

---

### Task 14: 规则层与特征层融合

**Files:**
- Modify: `BotMain/Learning/LearnedStrategyRuntime.cs`

- [ ] **Step 1: 在 TryApplyActionPatch 中添加特征评分回退**

在 `TryApplyActionPatch` 方法末尾（返回 false 之前），添加特征评分逻辑：

```csharp
// 在 TryApplyActionPatch 中，当规则层无匹配或置信度低时：
// 由 Coordinator 注入 ScoringModel 引用
private LinearScoringModel _scoringModel;

public void SetScoringModel(LinearScoringModel model)
{
    _scoringModel = model;
}
```

注意：完整的融合逻辑（规则分×0.6 + 特征分×0.4）需要在动作决策层面实现，不是在参数修补层面。

更好的接入点是在 `BotService.RecommendLocalActions` 中，当 `_useLearnedLocalStrategy` 为 true 时，如果规则层返回了修补（confidence 高），直接用。如果规则层无匹配，用特征模型对候选动作打分后选择最优。

这需要在 `RecommendLocalActions`（`BotService.cs:6035`）中添加后处理逻辑。具体实现：

```csharp
// BotService.cs RecommendLocalActions 方法中，在 return 之前：
if (_useLearnedLocalStrategy && _learnedStrategyCoordinator.ScoringModel != null && actions.Count > 1)
{
    // 尝试用特征模型对首个动作进行置信度评估
    var model = _learnedStrategyCoordinator.ScoringModel;
    var planBoard = request.PlanningBoard;
    if (planBoard != null)
    {
        var boardFeatures = FeatureVectorExtractor.ExtractBoardFeatures(planBoard);
        var enemyHeroId = planBoard.HeroEnemy?.Id ?? 0;
        var firstAction = actions.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("END_TURN", StringComparison.OrdinalIgnoreCase));
        if (firstAction != null)
        {
            var actionFeatures = FeatureVectorExtractor.ExtractActionFeatures(firstAction, planBoard, enemyHeroId);
            var combined = FeatureVectorExtractor.Combine(boardFeatures, actionFeatures);
            var score = model.Score(combined);
            Log($"[Learned] feature_score={score:0.###} for {firstAction.Substring(0, Math.Min(firstAction.Length, 30))}");
        }
    }
}
```

完整的"替代规则层选动作"功能需要枚举所有合法动作并打分，这超出了当前 `RecommendLocalActions` 的架构。作为 P3 阶段，先做特征模型训练和评分日志，动作替换在数据积累充足后再迭代。

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/LearnedStrategyRuntime.cs BotMain/BotService.cs
git commit -m "P3: 特征评分日志集成，规则-特征融合基础"
```

---

## P4: 独立运行门控

### Task 15: ReadinessMonitor

**Files:**
- Create: `BotMain/Learning/ReadinessMonitor.cs`
- Test: `BotCore.Tests/Learning/ReadinessMonitorTests.cs`

- [ ] **Step 1: 编写测试**

```csharp
// BotCore.Tests/Learning/ReadinessMonitorTests.cs
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class ReadinessMonitorTests
    {
        [Fact]
        public void NotReady_WhenBelowThreshold()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 85.0,
                MulliganRate = 91.0,
                ChoiceRate = 92.0,
                TotalMatches = 150,
                RecentWinRate = 55.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("动作一致率", status.BlockingReason);
        }

        [Fact]
        public void Ready_WhenAllConditionsMet()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 92.0,
                MulliganRate = 91.0,
                ChoiceRate = 93.0,
                TotalMatches = 150,
                RecentWinRate = 56.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.True(status.IsReady);
        }

        [Fact]
        public void NotReady_WhenTooFewMatches()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 95.0,
                MulliganRate = 95.0,
                ChoiceRate = 95.0,
                TotalMatches = 50,
                RecentWinRate = 60.0,
                LearningPhaseWinRate = 60.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("对局数", status.BlockingReason);
        }

        [Fact]
        public void NotReady_WhenWinRateDropTooLarge()
        {
            var status = ReadinessMonitor.Evaluate(new ReadinessInput
            {
                ActionRate = 95.0,
                MulliganRate = 95.0,
                ChoiceRate = 95.0,
                TotalMatches = 150,
                RecentWinRate = 45.0,
                LearningPhaseWinRate = 58.0
            });

            Assert.False(status.IsReady);
            Assert.Contains("胜率", status.BlockingReason);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ReadinessMonitorTests" --no-restore -v minimal`
Expected: 编译失败

- [ ] **Step 3: 实现 ReadinessMonitor**

```csharp
// BotMain/Learning/ReadinessMonitor.cs
using System.Text;

namespace BotMain.Learning
{
    public sealed class ReadinessInput
    {
        public double ActionRate { get; set; }
        public double MulliganRate { get; set; }
        public double ChoiceRate { get; set; }
        public int TotalMatches { get; set; }
        public double RecentWinRate { get; set; }
        public double LearningPhaseWinRate { get; set; }
    }

    public sealed class ReadinessStatus
    {
        public bool IsReady { get; set; }
        public string BlockingReason { get; set; }
        public string Summary { get; set; }
    }

    public static class ReadinessMonitor
    {
        private const double ConsistencyThreshold = 90.0;
        private const int MinTotalMatches = 100;
        private const double MaxWinRateDrop = 5.0;

        public static ReadinessStatus Evaluate(ReadinessInput input)
        {
            var blocks = new StringBuilder();

            if (input.ActionRate < ConsistencyThreshold)
                blocks.AppendLine($"  动作一致率 {input.ActionRate:0.0}% < {ConsistencyThreshold}%");
            if (input.MulliganRate < ConsistencyThreshold)
                blocks.AppendLine($"  留牌一致率 {input.MulliganRate:0.0}% < {ConsistencyThreshold}%");
            if (input.ChoiceRate < ConsistencyThreshold)
                blocks.AppendLine($"  选择一致率 {input.ChoiceRate:0.0}% < {ConsistencyThreshold}%");
            if (input.TotalMatches < MinTotalMatches)
                blocks.AppendLine($"  对局数 {input.TotalMatches} < {MinTotalMatches}");
            if (input.LearningPhaseWinRate - input.RecentWinRate > MaxWinRateDrop)
                blocks.AppendLine($"  胜率差 {input.LearningPhaseWinRate - input.RecentWinRate:0.0}% > {MaxWinRateDrop}%");

            bool isReady = blocks.Length == 0;
            var winRateDiff = input.RecentWinRate - input.LearningPhaseWinRate;

            var summary = $"动作: {input.ActionRate:0.0}% | 留牌: {input.MulliganRate:0.0}% | 选择: {input.ChoiceRate:0.0}% | " +
                          $"累计: {input.TotalMatches}局 | 胜率差: {winRateDiff:+0.0;-0.0}%";

            return new ReadinessStatus
            {
                IsReady = isReady,
                BlockingReason = isReady ? null : blocks.ToString().TrimEnd(),
                Summary = summary
            };
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --filter "ReadinessMonitorTests" --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 5: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/Learning/ReadinessMonitor.cs BotCore.Tests/Learning/ReadinessMonitorTests.cs
git commit -m "P4: 新增 ReadinessMonitor 独立运行就绪评估"
```

---

### Task 16: 就绪状态 UI 绑定

**Files:**
- Modify: `BotMain/MainViewModel.cs`
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在 BotService 中暴露就绪状态**

```csharp
// 新增方法：
public ReadinessStatus GetLearningReadiness()
{
    var snapshot = _learnedStrategyCoordinator.Consistency.GetSnapshot();
    var store = _learnedStrategyCoordinator.ConsistencyStore;
    var totalMatches = store?.GetTotalMatchCount() ?? 0;
    var recentWinRate = store?.GetRecentWinRate(30) ?? 0;
    var learningWinRate = store?.GetLearningPhaseWinRate(30) ?? 0;

    return ReadinessMonitor.Evaluate(new ReadinessInput
    {
        ActionRate = snapshot.ActionRate,
        MulliganRate = snapshot.MulliganRate,
        ChoiceRate = snapshot.ChoiceRate,
        TotalMatches = totalMatches,
        RecentWinRate = recentWinRate,
        LearningPhaseWinRate = learningWinRate
    });
}
```

在 `LearnedStrategyCoordinator` 中暴露 `ConsistencyStore`：

```csharp
public SqliteConsistencyStore ConsistencyStore => _consistencyStore;
```

- [ ] **Step 2: 在 MainViewModel 中添加就绪状态属性**

```csharp
// 新增属性：
private string _learningStatusText = "";
public string LearningStatusText
{
    get => _learningStatusText;
    set { _learningStatusText = value; Notify(); }
}
```

- [ ] **Step 3: 定期刷新就绪状态**

在 `_timer.Tick` 事件（每秒触发一次，约136-137行）中，添加学习状态刷新（每 10 秒一次即可）：

```csharp
// 在 _timer.Tick lambda 中新增：
if (_bot.State == BotState.Running && _learnFromHsBox && DateTime.Now.Second % 10 == 0)
{
    try
    {
        var readiness = _bot.GetLearningReadiness();
        LearningStatusText = readiness.IsReady
            ? $"[学习] 独立运行就绪 ✓ {readiness.Summary}"
            : $"[学习] 学习中... {readiness.Summary}";
    }
    catch { }
}
```

- [ ] **Step 4: 在对局结束时记录胜负到 ConsistencyStore**

在 `BotService.cs` 的 `ApplyMatchOutcome` 调用附近（约1531行），添加：

```csharp
try
{
    _learnedStrategyCoordinator.ConsistencyStore?.RecordMatchOutcome(
        _currentLearningMatchId,
        decision.LearnedOutcome == LearnedMatchOutcome.Win,
        _learnFromHsBoxRecommendations);
}
catch { }
```

- [ ] **Step 5: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 6: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/BotService.cs BotMain/MainViewModel.cs BotMain/Learning/LearnedStrategyCoordinator.cs
git commit -m "P4: 就绪状态 UI 绑定 + 对局胜负记录"
```

---

### Task 17: 安全监控（胜率骤降警告）

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 在独立运行模式下监控胜率**

在对局结束逻辑中（`ApplyMatchOutcome` 之后），添加安全监控：

```csharp
// 独立运行安全监控
if (_useLearnedLocalStrategy && !_followHsBoxRecommendations)
{
    var store = _learnedStrategyCoordinator.ConsistencyStore;
    if (store != null)
    {
        var recentWinRate = store.GetRecentWinRate(10);
        var baselineWinRate = store.GetLearningPhaseWinRate(30);
        if (baselineWinRate - recentWinRate > 15.0)
        {
            Log($"[Learning] ⚠ 胜率骤降警告: 最近10局胜率 {recentWinRate:0.0}% vs 学习期基线 {baselineWinRate:0.0}%，建议重新开启盒子学习");
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功

- [ ] **Step 3: 运行全部测试回归**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --no-restore -v minimal`
Expected: 全部 PASS

- [ ] **Step 4: 提交**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add BotMain/BotService.cs
git commit -m "P4: 独立运行安全监控，胜率骤降警告"
```

---

## 最终验证

### Task 18: 全量构建 + 测试 + 功能检查

- [ ] **Step 1: 全量构建**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet build BotMain -c Release --no-restore -v minimal`
Expected: 编译成功，无警告

- [ ] **Step 2: 全量测试**

Run: `cd "H:/桌面/炉石脚本/Hearthbot" && dotnet test BotCore.Tests --no-restore -v normal`
Expected: 全部 PASS

- [ ] **Step 3: 检查新文件完整性**

确认以下文件全部存在：
- `BotMain/Learning/ConsistencyTracker.cs`
- `BotMain/Learning/SqliteConsistencyStore.cs`
- `BotMain/Learning/ActionPatternClassifier.cs`
- `BotMain/Learning/LearnedEvalWeights.cs`
- `BotMain/Learning/FeatureVectorExtractor.cs`
- `BotMain/Learning/LinearScoringModel.cs`
- `BotMain/Learning/ReadinessMonitor.cs`

- [ ] **Step 4: 提交计划文档**

```bash
cd "H:/桌面/炉石脚本/Hearthbot"
git add docs/superpowers/plans/2026-03-30-learning-system-activation.md
git commit -m "docs: 学习系统激活实施计划"
```
