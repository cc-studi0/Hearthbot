# HSBox 原始样本采集 Phase A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `Learn From HSBox` 的主职责改造成“原始决策样本采集”，在不破坏现有 `teacher.db` 逻辑的前提下，将动作、发现、留牌决策点完整落到 `dataset.db`。

**Architecture:** 本计划只覆盖设计文档中的 `Phase A`，因为完整 spec 同时包含样本采集、离线训练、ONNX 推理和运行时替换，拆成单独可交付阶段更稳。实现上新增一个独立的 `TeacherDatasetRecorder` 和 `SqliteTeacherDatasetStore`，由 `BotService` 在现有 `_learnedStrategyCoordinator` 旁边双写；动作阶段新增合法候选动作快照与老师动作映射，发现/留牌先落结构化决策样本，为后续训练计划准备原始数据。

**Tech Stack:** C# / .NET 8、`Microsoft.Data.Sqlite`、xUnit、现有 `BotMain.Learning` / `BotCore.Tests` 基础设施

**Spec:** `docs/superpowers/specs/2026-03-25-hsbox-local-teacher-design.md`

---

## 范围说明

这份 spec 涵盖多个相对独立的子系统：

- 原始样本采集
- 离线特征导出与训练
- ONNX 运行时接入
- 旧 learned runtime 退役

本计划只实现第一个可独立上线的子项目：`原始样本采集 Phase A`。训练管线、模型导出和 `Use Learned Local` 运行时替换会在后续独立计划中继续推进。

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `.gitignore` | 修改 | 忽略 `Data/HsBoxTeacher/*.db-wal`、`*.db-shm` 等 SQLite 侧边文件 |
| `BotMain/Learning/TeacherDatasetContracts.cs` | 新建 | 定义原始样本数据模型、枚举、序列化负载结构 |
| `BotMain/Learning/SqliteTeacherDatasetStore.cs` | 新建 | 创建 `dataset.db`、维护 schema、插入/更新样本 |
| `BotMain/Learning/TeacherActionMapper.cs` | 新建 | 枚举动作候选、把老师动作映射到候选动作 |
| `BotMain/Learning/TeacherDatasetRecorder.cs` | 新建 | 统一对外入口，负责 action / choice / mulligan / outcome 的双写记录 |
| `BotMain/BotService.cs` | 修改 | 初始化 recorder，接入 `TryEnqueue*Learning` 和 outcome 回填 |
| `BotCore.Tests/TeacherDatasetStoreTests.cs` | 新建 | 验证 schema、去重、候选持久化、outcome 回填 |
| `BotCore.Tests/TeacherActionMapperTests.cs` | 新建 | 验证动作候选生成与老师动作映射 |
| `BotCore.Tests/TeacherDatasetRecorderTests.cs` | 新建 | 验证 recorder 协调逻辑和 dual-write 接线 |

> 注：Phase A 不修改 `LearnedStrategyTrainer` / `LearnedStrategyRuntime` 的行为，只在 `BotService` 上新增双写，确保现有 `teacher.db` 逻辑继续可用。

---

### Task 1: 建立原始样本数据模型与最小 Store 接口

**Files:**
- Create: `BotMain/Learning/TeacherDatasetContracts.cs`
- Create: `BotCore.Tests/TeacherDatasetStoreTests.cs`

- [ ] **Step 1: 写 Store 契约的失败测试**

  在 `BotCore.Tests/TeacherDatasetStoreTests.cs` 新建以下测试，先只验证接口级行为，不实现 SQLite：

  ```csharp
  [Fact]
  public void DecisionRecord_UsesStableSampleKey()
  {
      var record = new TeacherActionDecisionRecord
      {
          MatchId = "match-1",
          PayloadSignature = "payload-1",
          TeacherActionCommand = "ATTACK|101|202",
          Seed = "seed-1"
      };

      Assert.False(string.IsNullOrWhiteSpace(record.BuildSampleKey()));
      Assert.Equal(record.BuildSampleKey(), record.BuildSampleKey());
  }

  [Fact]
  public void CandidateRecord_FlagsTeacherPick()
  {
      var candidate = new TeacherActionCandidateRecord
      {
          ActionCommand = "PLAY|101|0|0",
          IsTeacherPick = true
      };

      Assert.True(candidate.IsTeacherPick);
  }
  ```

- [ ] **Step 2: 运行测试，确认当前失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetStoreTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `TeacherActionDecisionRecord` / `TeacherActionCandidateRecord` 不存在。

- [ ] **Step 3: 写最小数据模型与 Store 接口**

  在 `BotMain/Learning/TeacherDatasetContracts.cs` 写出最小版本的类型定义，至少包括：

  ```csharp
  internal enum TeacherDecisionKind
  {
      Action = 0,
      Choice = 1,
      Mulligan = 2
  }

  internal enum TeacherActionMappingStatus
  {
      NotAttempted = 0,
      Mapped = 1,
      NoCandidates = 2,
      NoTeacherAction = 3,
      NoMatch = 4
  }

  internal sealed class TeacherActionDecisionRecord
  {
      public string DecisionId { get; set; } = string.Empty;
      public string MatchId { get; set; } = string.Empty;
      public string PayloadSignature { get; set; } = string.Empty;
      public string Seed { get; set; } = string.Empty;
      public string TeacherActionCommand { get; set; } = string.Empty;
      public string BoardSnapshotJson { get; set; } = string.Empty;
      public string ContextSnapshotJson { get; set; } = string.Empty;
      public TeacherActionMappingStatus MappingStatus { get; set; }
      public string BuildSampleKey() => LearnedStrategyFeatureExtractor.HashComposite(
          MatchId ?? string.Empty,
          PayloadSignature ?? string.Empty,
          Seed ?? string.Empty,
          TeacherActionCommand ?? string.Empty);
  }

  internal sealed class TeacherActionCandidateRecord
  {
      public string CandidateId { get; set; } = string.Empty;
      public string DecisionId { get; set; } = string.Empty;
      public string ActionCommand { get; set; } = string.Empty;
      public string ActionType { get; set; } = string.Empty;
      public string SourceCardId { get; set; } = string.Empty;
      public string TargetCardId { get; set; } = string.Empty;
      public string CandidateSnapshotJson { get; set; } = string.Empty;
      public bool IsTeacherPick { get; set; }
  }

  internal interface ITeacherDatasetStore
  {
      bool TryStoreActionDecision(
          TeacherActionDecisionRecord decision,
          IReadOnlyList<TeacherActionCandidateRecord> candidates,
          out string detail);

      bool TryStoreChoiceDecision(
          TeacherChoiceDecisionRecord decision,
          out string detail);

      bool TryStoreMulliganDecision(
          TeacherMulliganDecisionRecord decision,
          out string detail);

      bool TryApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome, out string detail);
  }
  ```

  同文件内补齐 `TeacherChoiceDecisionRecord`、`TeacherMulliganDecisionRecord` 的最小字段集合，字段名对齐 spec。

- [ ] **Step 4: 再跑一次测试，确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetStoreTests" -v normal
  ```

  Expected:
  `PASS`，至少这两个新测试通过。

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/Learning/TeacherDatasetContracts.cs BotCore.Tests/TeacherDatasetStoreTests.cs
  git commit -m "新增原始样本采集数据模型与接口"
  ```

---

### Task 2: 实现 `dataset.db` 持久化与 outcome 回填

**Files:**
- Create: `BotMain/Learning/SqliteTeacherDatasetStore.cs`
- Modify: `BotCore.Tests/TeacherDatasetStoreTests.cs`

- [ ] **Step 1: 扩展失败测试，覆盖 SQLite schema 与回填行为**

  在 `BotCore.Tests/TeacherDatasetStoreTests.cs` 增加这两个测试：

  ```csharp
  [Fact]
  public void SqliteTeacherDatasetStore_PersistsDecisionAndCandidates()
  {
      var dbPath = Path.Combine(Path.GetTempPath(), "dataset-" + Guid.NewGuid().ToString("N") + ".db");
      try
      {
          var store = new SqliteTeacherDatasetStore(dbPath);
          var decision = new TeacherActionDecisionRecord
          {
              MatchId = "match-1",
              DecisionId = "decision-1",
              PayloadSignature = "payload-1",
              TeacherActionCommand = "ATTACK|101|202",
              MappingStatus = TeacherActionMappingStatus.Mapped
          };
          var candidates = new[]
          {
              new TeacherActionCandidateRecord
              {
                  CandidateId = "c1",
                  DecisionId = "decision-1",
                  ActionCommand = "ATTACK|101|202",
                  ActionType = "Attack",
                  IsTeacherPick = true
              }
          };

          Assert.True(store.TryStoreActionDecision(decision, candidates, out _));
          Assert.False(store.TryStoreActionDecision(decision, candidates, out _));
      }
      finally
      {
          TryDelete(dbPath);
      }
  }

  [Fact]
  public void SqliteTeacherDatasetStore_AppliesOutcomeToMatchRows()
  {
      var dbPath = Path.Combine(Path.GetTempPath(), "dataset-outcome-" + Guid.NewGuid().ToString("N") + ".db");
      try
      {
          var store = new SqliteTeacherDatasetStore(dbPath);
          var decision = new TeacherActionDecisionRecord
          {
              MatchId = "match-2",
              DecisionId = "decision-2",
              PayloadSignature = "payload-2",
              TeacherActionCommand = "PLAY|101|0|0",
              MappingStatus = TeacherActionMappingStatus.NoMatch
          };

          Assert.True(store.TryStoreActionDecision(decision, Array.Empty<TeacherActionCandidateRecord>(), out _));
          Assert.True(store.TryApplyMatchOutcome("match-2", LearnedMatchOutcome.Win, out _));
      }
      finally
      {
          TryDelete(dbPath);
      }
  }
  ```

- [ ] **Step 2: 运行测试，确认失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetStoreTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `SqliteTeacherDatasetStore` 不存在。

- [ ] **Step 3: 写最小 SQLite Store**

  在 `BotMain/Learning/SqliteTeacherDatasetStore.cs` 实现：

  - 默认数据库路径：`H:\桌面\炉石脚本\Hearthbot\Data\HsBoxTeacher\dataset.db`
  - `matches`
  - `action_decisions`
  - `action_candidates`
  - `choice_decisions`
  - `mulligan_decisions`

  初版 schema 直接用 `TEXT + INTEGER + REAL`，不要提前做过度抽象。核心 SQL 结构按下面实现：

  ```csharp
  CREATE TABLE IF NOT EXISTS action_decisions (
      decision_id TEXT PRIMARY KEY,
      match_id TEXT NOT NULL,
      payload_signature TEXT NOT NULL,
      seed TEXT NOT NULL,
      teacher_action_command TEXT NOT NULL,
      board_snapshot_json TEXT NOT NULL,
      context_snapshot_json TEXT NOT NULL,
      mapping_status INTEGER NOT NULL,
      outcome INTEGER NOT NULL DEFAULT 0,
      created_at_ms INTEGER NOT NULL
  );

  CREATE TABLE IF NOT EXISTS action_candidates (
      candidate_id TEXT PRIMARY KEY,
      decision_id TEXT NOT NULL,
      action_command TEXT NOT NULL,
      action_type TEXT NOT NULL,
      source_card_id TEXT NOT NULL,
      target_card_id TEXT NOT NULL,
      candidate_snapshot_json TEXT NOT NULL,
      is_teacher_pick INTEGER NOT NULL,
      FOREIGN KEY(decision_id) REFERENCES action_decisions(decision_id)
  );
  ```

  `TryStoreActionDecision` 要求：

  - `decision_id` 去重
  - 同一事务中写 decision 和 candidates
  - 重复写返回 `false`

  `TryApplyMatchOutcome` 要求：

  - 按 `match_id` 批量更新 `matches` 与各决策表里的 `outcome`
  - 如果没有待更新记录，返回 `false`

- [ ] **Step 4: 跑测试确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetStoreTests" -v normal
  ```

  Expected:
  `PASS`。

- [ ] **Step 5: 再跑一遍现有 learned strategy 回归测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~LearnedStrategyTests" -v normal
  ```

  Expected:
  `PASS`，证明新 store 没影响旧 `teacher.db` 逻辑。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/Learning/SqliteTeacherDatasetStore.cs BotCore.Tests/TeacherDatasetStoreTests.cs
  git commit -m "新增 dataset.db 样本存储与 outcome 回填"
  ```

---

### Task 3: 实现动作候选快照与老师动作映射

**Files:**
- Create: `BotMain/Learning/TeacherActionMapper.cs`
- Create: `BotCore.Tests/TeacherActionMapperTests.cs`

- [ ] **Step 1: 写映射失败测试**

  在 `BotCore.Tests/TeacherActionMapperTests.cs` 添加：

  ```csharp
  [Fact]
  public void TeacherActionMapper_MapsAttackTeacherCommandToGeneratedCandidate()
  {
      var board = new Board
      {
          TurnCount = 3,
          ManaAvailable = 2,
          MaxMana = 2,
          HeroEnemy = TestCards.CreateHero(900, false),
          MinionFriend = new List<Card>
          {
              TestCards.CreateMinion(101, Card.Cards.CORE_CS2_231, atk: 2, health: 3, canAttack: true, isFriend: true)
          }
      };

      var result = TeacherActionMapper.BuildActionDecision(
          seed: "seed",
          board: board,
          deckSignature: "deck-1",
          teacherActionCommand: "ATTACK|101|900");

      Assert.Equal(TeacherActionMappingStatus.Mapped, result.Decision.MappingStatus);
      Assert.Contains(result.Candidates, candidate => candidate.IsTeacherPick);
  }

  [Fact]
  public void TeacherActionMapper_ReportsNoMatchWhenTeacherCommandIsNotGenerated()
  {
      var board = new Board
      {
          TurnCount = 1,
          ManaAvailable = 0,
          MaxMana = 0
      };

      var result = TeacherActionMapper.BuildActionDecision(
          seed: "seed",
          board: board,
          deckSignature: "deck-1",
          teacherActionCommand: "PLAY|999|0|0");

      Assert.Equal(TeacherActionMappingStatus.NoMatch, result.Decision.MappingStatus);
      Assert.DoesNotContain(result.Candidates, candidate => candidate.IsTeacherPick);
  }
  ```

- [ ] **Step 2: 运行测试，确认失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherActionMapperTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `TeacherActionMapper` 不存在。

- [ ] **Step 3: 实现 `TeacherActionMapper`**

  在 `BotMain/Learning/TeacherActionMapper.cs` 写下列最小实现：

  ```csharp
  internal sealed class TeacherActionDecisionBuildResult
  {
      public TeacherActionDecisionRecord Decision { get; set; } = new TeacherActionDecisionRecord();
      public List<TeacherActionCandidateRecord> Candidates { get; } = new();
  }

  internal static class TeacherActionMapper
  {
      public static TeacherActionDecisionBuildResult BuildActionDecision(
          string seed,
          Board board,
          string deckSignature,
          string teacherActionCommand)
      {
          var simBoard = SimBoard.FromBoard(board);
          var generator = new ActionGenerator();
          var candidates = generator.Generate(simBoard);
          ...
      }
  }
  ```

  具体要求：

  - 用 `ActionGenerator.Generate(simBoard)` 枚举当前合法候选动作
  - 候选动作统一转成 `GameAction.ToActionString()`
  - `END_TURN` 也保留在候选列表里，但映射老师动作时优先匹配老师原字符串
  - `decision_id` 用 `HashComposite(match/payload/teacherAction/seed)` 生成
  - `candidate_id` 用 `HashComposite(decision_id/action_command/index)` 生成
  - `board_snapshot_json` 初版允许只保存最小摘要，例如 turn/mana/hand/friendly board/enemy board
  - `MappingStatus` 只做 4 种：`NoTeacherAction`、`NoCandidates`、`Mapped`、`NoMatch`

- [ ] **Step 4: 跑测试确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherActionMapperTests" -v normal
  ```

  Expected:
  `PASS`。

- [ ] **Step 5: 补跑现有 HSBox recommendation 相关测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~HsBoxRecommendationProviderTests" -v normal
  ```

  Expected:
  `PASS`。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/Learning/TeacherActionMapper.cs BotCore.Tests/TeacherActionMapperTests.cs
  git commit -m "新增老师动作候选映射与快照构建"
  ```

---

### Task 4: 实现 `TeacherDatasetRecorder`，统一记录 action / choice / mulligan

**Files:**
- Create: `BotMain/Learning/TeacherDatasetRecorder.cs`
- Create: `BotCore.Tests/TeacherDatasetRecorderTests.cs`

- [ ] **Step 1: 写 recorder 协调逻辑的失败测试**

  在 `BotCore.Tests/TeacherDatasetRecorderTests.cs` 新建一个 fake store，并写测试：

  ```csharp
  [Fact]
  public void TeacherDatasetRecorder_StoresActionDecisionThroughStore()
  {
      var store = new FakeTeacherDatasetStore();
      var recorder = new TeacherDatasetRecorder(store);

      recorder.RecordActionDecision(
          matchId: "match-1",
          request: BuildActionRequest("seed-1"),
          teacherRecommendation: new ActionRecommendationResult(
              null,
              new[] { "ATTACK|101|900" },
              "teacher"),
          localRecommendation: new ActionRecommendationResult(
              null,
              new[] { "END_TURN" },
              "local"));

      Assert.Single(store.ActionDecisions);
  }

  [Fact]
  public void TeacherDatasetRecorder_AppliesMatchOutcome()
  {
      var store = new FakeTeacherDatasetStore();
      var recorder = new TeacherDatasetRecorder(store);

      recorder.ApplyMatchOutcome("match-9", LearnedMatchOutcome.Win);

      Assert.Equal("match-9", store.LastOutcomeMatchId);
      Assert.Equal(LearnedMatchOutcome.Win, store.LastOutcome);
  }
  ```

- [ ] **Step 2: 运行测试，确认失败**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetRecorderTests" -v normal
  ```

  Expected:
  `FAIL`，提示 `TeacherDatasetRecorder` 不存在。

- [ ] **Step 3: 实现 recorder**

  在 `BotMain/Learning/TeacherDatasetRecorder.cs` 实现以下 public API：

  ```csharp
  internal sealed class TeacherDatasetRecorder
  {
      public TeacherDatasetRecorder(ITeacherDatasetStore store = null)
      {
          ...
      }

      public Action<string> OnLog { get; set; }

      public void RecordActionDecision(
          string matchId,
          ActionRecommendationRequest request,
          ActionRecommendationResult teacherRecommendation,
          ActionRecommendationResult localRecommendation)
      {
          ...
      }

      public void RecordChoiceDecision(
          string matchId,
          ChoiceRecommendationRequest request,
          ChoiceRecommendationResult teacherRecommendation,
          ChoiceRecommendationResult localRecommendation)
      {
          ...
      }

      public void RecordDiscoverDecision(
          string matchId,
          DiscoverRecommendationRequest request,
          DiscoverRecommendationResult teacherRecommendation,
          DiscoverRecommendationResult localRecommendation)
      {
          ...
      }

      public void RecordMulliganDecision(
          string matchId,
          MulliganRecommendationRequest request,
          MulliganRecommendationResult teacherRecommendation,
          MulliganRecommendationResult localRecommendation)
      {
          ...
      }

      public void ApplyMatchOutcome(string matchId, LearnedMatchOutcome outcome)
      {
          ...
      }
  }
  ```

  要求：

  - `RecordActionDecision` 内部调用 `TeacherActionMapper.BuildActionDecision(...)`
  - `choice` / `discover` / `mulligan` 先记录结构化原始字段，不要求第一版就生成完整候选特征
  - 任何异常都只记日志，不抛出打断主流程
  - 统一日志前缀用 `[TeacherDataset]`

- [ ] **Step 4: 跑测试确认通过**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetRecorderTests" -v normal
  ```

  Expected:
  `PASS`。

- [ ] **Step 5: 跑 store + mapper + recorder 三组合测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~Teacher" -v normal
  ```

  Expected:
  新增 `Teacher*` 测试全部通过。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/Learning/TeacherDatasetRecorder.cs BotCore.Tests/TeacherDatasetRecorderTests.cs
  git commit -m "新增原始样本采集记录器"
  ```

---

### Task 5: 在 `BotService` 里接入 recorder，并保持 legacy 双写

**Files:**
- Modify: `BotMain/BotService.cs:886-1035`
- Modify: `BotMain/BotService.cs:1483-1486`
- Modify: `BotCore.Tests/TeacherDatasetRecorderTests.cs`

- [ ] **Step 1: 扩展失败测试，验证 `BotService` 会调用 recorder**

  在 `TeacherDatasetRecorderTests.cs` 中新增一个 `BotService` 接线测试。先给 `BotService` 增加测试用内部构造入口，允许注入自定义 `TeacherDatasetRecorder`，然后通过反射调用私有的 `TryEnqueueActionLearning`。测试目标是确认 recorder 被真正调用，而旧 coordinator 是否保留则由回归测试兜底。

  ```csharp
  [Fact]
  public void BotService_TryEnqueueActionLearning_CallsTeacherDatasetRecorder()
  {
      var fakeStore = new FakeTeacherDatasetStore();
      var recorder = new TeacherDatasetRecorder(fakeStore);
      var service = BotServiceFactory.CreateForLearningTests(recorder: recorder);

      InvokePrivate(
          service,
          "TryEnqueueActionLearning",
          BuildActionRequest("seed-1"),
          new ActionRecommendationResult(null, new[] { "ATTACK|101|900" }, "teacher"),
          new ActionRecommendationResult(null, new[] { "END_TURN" }, "local"));

      Assert.Single(fakeStore.ActionDecisions);
  }
  ```

  这里不要求直接断言旧 coordinator 的调用次数；旧链路是否保留由 Step 5 的回归测试确认。

- [ ] **Step 2: 运行相关测试，确认当前失败或无法编写**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~TeacherDatasetRecorderTests" -v normal
  ```

  Expected:
  当前要么失败，要么无法验证 `BotService` 双写，作为下一步修改依据。

- [ ] **Step 3: 修改 `BotService`，新增 recorder 字段与双写**

  在 `BotMain/BotService.cs` 做以下改动：

  1. 在 `_learnedStrategyCoordinator` 字段附近新增：
     ```csharp
     private readonly TeacherDatasetRecorder _teacherDatasetRecorder;
     ```

  2. 在构造函数中初始化：
     ```csharp
     _teacherDatasetRecorder = new TeacherDatasetRecorder();
     _teacherDatasetRecorder.OnLog = msg => Log(msg);
     ```

  3. 在以下方法里先调用 recorder，再保留旧 coordinator：
     - `TryEnqueueActionLearning`：当前入口在 `BotService.cs:886`
     - `TryEnqueueMulliganLearning`：当前入口在 `BotService.cs:919`
     - `TryEnqueueChoiceLearning`：当前入口在 `BotService.cs:953`
     - `TryEnqueueDiscoverLearning`：当前入口在 `BotService.cs:992`

     示例写法：
     ```csharp
     _teacherDatasetRecorder.RecordActionDecision(
         _currentLearningMatchId,
         request,
         teacherRecommendation,
         localRecommendation);

     _learnedStrategyCoordinator.EnqueueActionSample(...existing payload...);
     ```

  4. 在对局结束 outcome 回填处（当前在 `BotService.cs:1483-1486`）改成双写：
     ```csharp
     _teacherDatasetRecorder.ApplyMatchOutcome(_currentLearningMatchId, decision.LearnedOutcome);
     _learnedStrategyCoordinator.ApplyMatchOutcome(_currentLearningMatchId, decision.LearnedOutcome);
     ```

- [ ] **Step 4: 跑新增 Teacher 测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~Teacher" -v normal
  ```

  Expected:
  `PASS`。

- [ ] **Step 5: 跑现有回归测试，确保 dual-write 没打断旧链路**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj --filter "FullyQualifiedName~LearnedStrategyTests|FullyQualifiedName~HsBoxRecommendationProviderTests|FullyQualifiedName~MulliganProtocolTests" -v normal
  ```

  Expected:
  全部 `PASS`。

- [ ] **Step 6: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add BotMain/BotService.cs BotCore.Tests/TeacherDatasetRecorderTests.cs
  git commit -m "接入原始样本采集并保留旧学习链路"
  ```

---

### Task 6: 清理工作区噪音并补手动验证说明

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: 修改 `.gitignore` 忽略 SQLite sidecar**

  在 `.gitignore` 末尾追加：

  ```gitignore
  # ========== HSBox teacher datasets ==========
  Data/HsBoxTeacher/*.db-wal
  Data/HsBoxTeacher/*.db-shm
  Data/HsBoxTeacher/*.db-journal
  ```

- [ ] **Step 2: 检查工作区里 SQLite 临时文件是否还显示为未跟踪**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git status --short
  ```

  Expected:
  `Data/HsBoxTeacher/teacher.db-wal` 和 `.db-shm` 不再显示。

- [ ] **Step 3: 做一轮手动验证**

  用真实运行环境验证以下场景，并把结果写在提交说明里：

  1. 勾选 `Learn From HSBox`，打一局标准对局。
  2. 对局中确认 `dataset.db` 被创建。
  3. 对局结束后确认 `matches`、`action_decisions`、`action_candidates` 至少有一条记录。
  4. 如果老师动作映射失败，确认 `mapping_status` 被正确记录，而不是静默丢样本。
  5. 确认原有 `teacher.db` 仍继续写入，不影响现有 learned runtime。

  推荐使用：
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  @'
  import sqlite3
  con = sqlite3.connect(r"Data/HsBoxTeacher/dataset.db")
  print(con.execute("select count(*) from action_decisions").fetchone()[0])
  for row in con.execute("select mapping_status, count(*) from action_decisions group by mapping_status"):
      print(row)
  con.close()
  '@ | python -
  ```

- [ ] **Step 4: 运行完整相关测试**

  Run:
  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  dotnet test BotCore.Tests/BotCore.Tests.csproj -v normal
  ```

  Expected:
  全量测试通过；如果全量太慢，至少记录并保留以下组合：
  `Teacher*`、`LearnedStrategyTests`、`HsBoxRecommendationProviderTests`、`MulliganProtocolTests`。

- [ ] **Step 5: 提交**

  ```bash
  cd H:/桌面/炉石脚本/Hearthbot
  git add .gitignore
  git commit -m "忽略 HSBox 样本库 SQLite 临时文件"
  ```

---

## 手动验证清单

### 场景 1：动作样本正常落库
1. 打开 `Learn From HSBox`
2. 进入标准对局
3. 确认 `dataset.db` 出现
4. 对局后检查 `action_decisions` 和 `action_candidates`

### 场景 2：老师动作映射失败也保留样本
1. 构造一个老师动作明显和本地候选集不一致的局面
2. 确认 `mapping_status=NoMatch`
3. 确认 `action_candidates` 仍完整写入

### 场景 3：旧学习链路仍工作
1. 同时检查 `teacher.db` 是否继续有更新
2. 确认 `Use Learned Local` 旧行为未被本计划破坏

### 场景 4：发现与留牌样本落库
1. 进入留牌阶段
2. 确认 `mulligan_decisions` 增长
3. 触发发现/抉择
4. 确认 `choice_decisions` 增长

---

## 后续计划入口

当本计划完成并验证后，再单独写下一份 implementation plan：

1. `Phase B：离线特征导出与排序训练`
2. `Phase C：Use Learned Local 动作运行时接入`
3. `Phase D：choice / mulligan 模型上线`

---

## 计划审阅说明

本计划已做本地自审，重点核对了：

- 文件边界是否贴合现有 `BotMain/Learning` 结构
- `BotService` 接线点是否准确落在现有 `TryEnqueue*Learning` 与 outcome 回填位置
- Phase A 是否能独立交付可测试的软件

按会话中的工具权限约束，本次未使用子代理执行 plan-document-reviewer；如后续你明确要求用子代理协作，我再补做独立计划审阅。
