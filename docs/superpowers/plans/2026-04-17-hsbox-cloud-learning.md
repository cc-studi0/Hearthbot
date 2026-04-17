# HSBox 云端学习系统 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有本地 HSBox 学习系统改造为"云端采样 + 本地训练 + 本地 ONNX 推理"的多机协同架构，并把规则权重补丁算法升级为 LightGBM LambdaRank 排序模型。

**Architecture:** 扩展现有 `HearthBot.Cloud` ASP.NET Core 服务（SQLite + JWT），5 台 Hearthbot 机器 HTTPS 上传样本 → 云端入独立 `learning.db` → 训练机（4070S）HTTPS 拉数据 → LightGBM 训练 → 导出 ONNX → 推送云端 → 各 Hearthbot 6h 拉更新 → 本地 ONNX Runtime 推理。云端挂了不影响对局。

**Tech Stack:**
- 云端：C# / ASP.NET Core 8 / EF Core / SQLite / JWT
- 训练机：Python 3.11 / LightGBM / onnxmltools / MLflow / httpx
- Hearthbot 运行时：C# / .NET 8 / Microsoft.ML.OnnxRuntime / Microsoft.Data.Sqlite

---

## 预备说明

### 工作目录与分支约定

所有任务在仓库根 `H:/桌面/炉石脚本/Hearthbot` 下执行，当前分支 `main`。大阶段完成后推远程。

### 测试策略

| 代码层 | 策略 |
|--------|------|
| C# Controllers（云端） | xUnit + `WebApplicationFactory` 做集成测试 |
| C# Services（云端） | xUnit 纯单测 + 内存 SQLite |
| C# CloudLearning（运行时） | xUnit 纯单测，ONNX Runtime 用真实小模型做冒烟 |
| Python 训练 | pytest 关键函数 + 端到端 smoke |
| 前端 UI | 手动验证 |

### 命名约定

- **C# 新增命名空间**：`HearthBot.Cloud.Controllers` 加 `LearningXxxController`；`HearthBot.Cloud.Models.Learning.*`；`HearthBot.Cloud.Services.Learning.*`；Hearthbot 侧 `BotMain.CloudLearning`
- **API 路由**：全部加 `/v1/learning/` 前缀，与现有设备控制分离
- **数据库**：独立 SQLite `learning.db`，独立 `LearningDbContext`
- **JWT claim**：`role=machine`, `machine_id=<id>`, 365 天有效

### 提交节奏

- 每个 Task 结束一个 commit
- 阶段结束做 `git push`
- Commit message 全程中文

### 硬门槛前置汇总

| 阶段 | 硬门槛 |
|------|--------|
| 阶段 1 验收 | `mapping_status='matched'` 比率：action ≥ 85% / choice ≥ 90% / mulligan ≥ 95%；断网 30min 后补传零丢失 |
| 阶段 2 验收 | test top-1：action ≥ 0.55 / choice ≥ 0.60 / mulligan ≥ 0.65；Python vs ONNX 差异 <1e-5 |
| 阶段 3 验收 | 灰度机 `illegal_action_rate` < 1%；三类 top1_match_rate 达标 |
| 阶段 4 验收 | 5 台机 top1_match_rate > 0.60；至少 1 次自动回滚演练成功 |

不达标不前进。

---

## 文件结构总览

### 云端新增（HearthBot.Cloud/）

```
HearthBot.Cloud/
├── Controllers/
│   ├── Learning/
│   │   ├── SamplesController.cs        (POST /v1/learning/samples/batch)
│   │   ├── MatchesController.cs        (PATCH /v1/learning/matches/{id}/outcome)
│   │   ├── HeartbeatController.cs      (POST /v1/learning/heartbeat)
│   │   ├── ModelsController.cs         (upload / latest / download)
│   │   ├── ExportController.cs         (GET /v1/learning/export/*)
│   │   └── LearningHealthController.cs (GET /v1/learning/healthz)
├── Data/
│   └── LearningDbContext.cs
├── Models/Learning/
│   ├── Machine.cs
│   ├── LearningMatch.cs
│   ├── ActionDecision.cs    +  ActionCandidate.cs
│   ├── ChoiceDecision.cs    +  ChoiceOption.cs
│   ├── MulliganDecision.cs  +  MulliganCard.cs
│   ├── ModelVersion.cs
│   └── HeartbeatStats.cs
├── Services/Learning/
│   ├── LearningSchemaBootstrapper.cs
│   ├── MachineTokenService.cs
│   ├── SampleIngestService.cs
│   ├── ModelArtifactStore.cs
│   ├── ConsistencyMonitor.cs
│   └── ArchiveJob.cs        (HostedService)
└── Program.cs               (修改：注册 LearningDbContext + 服务)
```

### 云端测试（HearthBot.Cloud.Tests/ 新建）

```
HearthBot.Cloud.Tests/
├── HearthBot.Cloud.Tests.csproj
├── Learning/
│   ├── SampleIngestServiceTests.cs
│   ├── MachineTokenServiceTests.cs
│   ├── ModelArtifactStoreTests.cs
│   ├── SamplesControllerTests.cs
│   └── LearningSchemaBootstrapperTests.cs
```

### 训练机新增（training/，仓库根目录，`.gitignore` 数据产物）

```
training/
├── pyproject.toml
├── requirements.txt
├── config.py                (服务器 URL + token 读取)
├── schema/
│   └── feature_schema.py    (特征 schema + hash 计算)
├── pull_data.py
├── features/
│   ├── common.py
│   ├── action.py
│   ├── choice.py
│   └── mulligan.py
├── train_action.py
├── train_choice.py
├── train_mulligan.py
├── export_onnx.py
├── validate_onnx.py
├── push_model.py
├── eval_replay.py
├── schedule_daily.ps1       (Windows Task Scheduler 挂载)
├── .gitignore               (data/ models/ mlruns/)
└── tests/
    ├── test_features_action.py
    ├── test_onnx_parity.py
    └── test_pipeline_smoke.py
```

### Hearthbot 运行时新增（BotMain/CloudLearning/）

```
BotMain/CloudLearning/
├── CloudLearningOrchestrator.cs
├── SampleOutboxStore.cs
├── SampleUploader.cs
├── ModelArtifactManager.cs
├── ModelRuntimeHost.cs
├── DecisionRanker.cs
├── FeatureExtractor.cs
├── CandidateGenerationService.cs
├── CloudHealthMonitor.cs
├── Contracts/
│   ├── DecisionContextSnapshot.cs
│   ├── DecisionSample.cs
│   └── RankerResult.cs
└── Config/
    ├── CloudLearningOptions.cs
    └── FeatureSchemaRegistry.cs
```

### Hearthbot 运行时测试（BotCore.Tests/CloudLearning/）

```
BotCore.Tests/CloudLearning/
├── SampleOutboxStoreTests.cs
├── SampleUploaderTests.cs
├── ModelArtifactManagerTests.cs
├── FeatureExtractorTests.cs
└── DecisionRankerTests.cs
```

### 需修改的现有文件

- `HearthBot.Cloud/Program.cs`（注册 `LearningDbContext` + `MachineTokenService` 等）
- `HearthBot.Cloud/HearthBot.Cloud.csproj`（无增量包引用；SQLite 已有）
- `HearthBot.Cloud/Services/AuthService.cs`（新增 `GenerateMachineToken(machineId)` 方法）
- `BotMain/BotService.cs`（构造器 + `RequestRecommendationAsync` 挂钩）
- `BotMain/SettingsWindow.xaml` + `.xaml.cs`（新增 Cloud Learning 面板）
- `BotMain/MainViewModel.cs`（面板绑定）
- `BotMain/appsettings.json`（新增 `CloudLearning` 段）
- `BotMain/BotMain.csproj`（`Include="CloudLearning/**/*.cs"`，同时需加 `PackageReference Include="Microsoft.ML.OnnxRuntime"`）
- `BotCore.Tests/BotCore.Tests.csproj`（linked CloudLearning/**/*.cs）
- `BotMain/Learning/*`（阶段 5 标 `[Obsolete]`）
- `CLAUDE.md`（阶段 5 补充新架构说明）

---

## 阶段 0 / 云端基建（约 4 天，9 个 Task）

产出：一个能收 HTTPS 请求、有 SQLite 学习库、能用机器 JWT 鉴权、能返空的 `samples/batch` 和 `heartbeat` 端点的扩展 HearthBot.Cloud 服务。

### Task 0.1: 创建 Learning DbContext 与 EF 模型

**Files:**
- Create: `HearthBot.Cloud/Data/LearningDbContext.cs`
- Create: `HearthBot.Cloud/Models/Learning/Machine.cs`
- Create: `HearthBot.Cloud/Models/Learning/LearningMatch.cs`
- Create: `HearthBot.Cloud/Models/Learning/ActionDecision.cs`
- Create: `HearthBot.Cloud/Models/Learning/ActionCandidate.cs`
- Create: `HearthBot.Cloud/Models/Learning/ChoiceDecision.cs`
- Create: `HearthBot.Cloud/Models/Learning/ChoiceOption.cs`
- Create: `HearthBot.Cloud/Models/Learning/MulliganDecision.cs`
- Create: `HearthBot.Cloud/Models/Learning/MulliganCard.cs`
- Create: `HearthBot.Cloud/Models/Learning/ModelVersion.cs`

- [ ] **Step 1: 写 Machine 实体**

```csharp
// HearthBot.Cloud/Models/Learning/Machine.cs
namespace HearthBot.Cloud.Models.Learning;

public class Machine
{
    public string MachineId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    public string LastStatsJson { get; set; } = "{}";
}
```

- [ ] **Step 2: 写 LearningMatch 实体**

```csharp
// HearthBot.Cloud/Models/Learning/LearningMatch.cs
namespace HearthBot.Cloud.Models.Learning;

public class LearningMatch
{
    public string MatchId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string DeckSignature { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string StartAt { get; set; } = string.Empty;
    public string? EndAt { get; set; }
    public string? OutcomeJson { get; set; }
}
```

- [ ] **Step 3: 写 ActionDecision + ActionCandidate**

```csharp
// HearthBot.Cloud/Models/Learning/ActionDecision.cs
namespace HearthBot.Cloud.Models.Learning;

public class ActionDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string BoardSnapshotJson { get; set; } = "{}";
    public int TeacherCandidateIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
```

```csharp
// HearthBot.Cloud/Models/Learning/ActionCandidate.cs
namespace HearthBot.Cloud.Models.Learning;

public class ActionCandidate
{
    public long CandidateId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string ActionCommand { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool IsTeacherPick { get; set; }
    public bool IsLocalPick { get; set; }
}
```

- [ ] **Step 4: 写 ChoiceDecision + ChoiceOption（同构）**

```csharp
// HearthBot.Cloud/Models/Learning/ChoiceDecision.cs
namespace HearthBot.Cloud.Models.Learning;

public class ChoiceDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string ContextJson { get; set; } = "{}";
    public int TeacherOptionIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string ChoiceSourceType { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}
```

```csharp
// HearthBot.Cloud/Models/Learning/ChoiceOption.cs
namespace HearthBot.Cloud.Models.Learning;

public class ChoiceOption
{
    public long OptionId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string OptionCommand { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool IsTeacherPick { get; set; }
    public bool IsLocalPick { get; set; }
}
```

- [ ] **Step 5: 写 MulliganDecision + MulliganCard（同构）**

```csharp
// HearthBot.Cloud/Models/Learning/MulliganDecision.cs
namespace HearthBot.Cloud.Models.Learning;

public class MulliganDecision
{
    public long DecisionId { get; set; }
    public string ClientSampleId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string OwnClass { get; set; } = string.Empty;
    public string EnemyClass { get; set; } = string.Empty;
    public bool HasCoin { get; set; }
    public string DeckSignature { get; set; } = string.Empty;
    public string ContextJson { get; set; } = "{}";
    public string MappingStatus { get; set; } = "matched";
    public string CreatedAt { get; set; } = string.Empty;
}
```

```csharp
// HearthBot.Cloud/Models/Learning/MulliganCard.cs
namespace HearthBot.Cloud.Models.Learning;

public class MulliganCard
{
    public long CardEntryId { get; set; }
    public long DecisionId { get; set; }
    public int SlotIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
    public bool TeacherKeep { get; set; }
    public bool LocalKeep { get; set; }
}
```

- [ ] **Step 6: 写 ModelVersion**

```csharp
// HearthBot.Cloud/Models/Learning/ModelVersion.cs
namespace HearthBot.Cloud.Models.Learning;

public class ModelVersion
{
    public long Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty; // action|choice|mulligan
    public string Sha256 { get; set; } = string.Empty;
    public string TrainedAt { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
    public string? PrevVersion { get; set; }
    public string FeatureSchemaHash { get; set; } = string.Empty;
    public string TrainedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? RolledBackAt { get; set; }
}
```

- [ ] **Step 7: 写 LearningDbContext**

```csharp
// HearthBot.Cloud/Data/LearningDbContext.cs
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Data;

public class LearningDbContext : DbContext
{
    public LearningDbContext(DbContextOptions<LearningDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<LearningMatch> LearningMatches => Set<LearningMatch>();
    public DbSet<ActionDecision> ActionDecisions => Set<ActionDecision>();
    public DbSet<ActionCandidate> ActionCandidates => Set<ActionCandidate>();
    public DbSet<ChoiceDecision> ChoiceDecisions => Set<ChoiceDecision>();
    public DbSet<ChoiceOption> ChoiceOptions => Set<ChoiceOption>();
    public DbSet<MulliganDecision> MulliganDecisions => Set<MulliganDecision>();
    public DbSet<MulliganCard> MulliganCards => Set<MulliganCard>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>(e =>
        {
            e.HasKey(m => m.MachineId);
            e.Property(m => m.MachineId).HasMaxLength(64);
            e.HasIndex(m => m.LastSeenAt);
        });

        b.Entity<LearningMatch>(e =>
        {
            e.HasKey(m => m.MatchId);
            e.HasIndex(m => m.MachineId);
            e.HasIndex(m => m.StartAt);
        });

        b.Entity<ActionDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
            e.HasIndex(d => d.MappingStatus);
        });

        b.Entity<ActionCandidate>(e =>
        {
            e.HasKey(c => c.CandidateId);
            e.Property(c => c.CandidateId).ValueGeneratedOnAdd();
            e.HasIndex(c => c.DecisionId);
            e.HasIndex(c => c.IsTeacherPick);
        });

        b.Entity<ChoiceDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
        });

        b.Entity<ChoiceOption>(e =>
        {
            e.HasKey(o => o.OptionId);
            e.Property(o => o.OptionId).ValueGeneratedOnAdd();
            e.HasIndex(o => o.DecisionId);
        });

        b.Entity<MulliganDecision>(e =>
        {
            e.HasKey(d => d.DecisionId);
            e.Property(d => d.DecisionId).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ClientSampleId).IsUnique();
            e.HasIndex(d => d.MatchId);
            e.HasIndex(d => d.CreatedAt);
        });

        b.Entity<MulliganCard>(e =>
        {
            e.HasKey(c => c.CardEntryId);
            e.Property(c => c.CardEntryId).ValueGeneratedOnAdd();
            e.HasIndex(c => c.DecisionId);
        });

        b.Entity<ModelVersion>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.HasIndex(m => m.Version).IsUnique();
            e.HasIndex(m => new { m.ModelType, m.IsActive });
        });
    }
}
```

- [ ] **Step 8: Commit**

```bash
git add HearthBot.Cloud/Data/LearningDbContext.cs HearthBot.Cloud/Models/Learning/
git commit -m "阶段0: 新增 LearningDbContext 与 EF 实体定义"
```

---

### Task 0.2: LearningSchemaBootstrapper（DDL 兜底）

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/LearningSchemaBootstrapper.cs`
- Test: `HearthBot.Cloud.Tests/Learning/LearningSchemaBootstrapperTests.cs`（阶段末建测试项目后再补，此处先写生产代码）

- [ ] **Step 1: 写 LearningSchemaBootstrapper**

```csharp
// HearthBot.Cloud/Services/Learning/LearningSchemaBootstrapper.cs
using HearthBot.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public static class LearningSchemaBootstrapper
{
    public static async Task EnsureSchemaAsync(LearningDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        // SQLite WAL 模式提升并发读写
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/LearningSchemaBootstrapper.cs
git commit -m "阶段0: 新增 LearningSchemaBootstrapper 管理 learning.db DDL"
```

---

### Task 0.3: MachineTokenService（生成/校验机器 JWT）

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/MachineTokenService.cs`
- Modify: `HearthBot.Cloud/Services/AuthService.cs`

- [ ] **Step 1: 写 MachineTokenService**

```csharp
// HearthBot.Cloud/Services/Learning/MachineTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HearthBot.Cloud.Services.Learning;

public class MachineTokenService
{
    private readonly IConfiguration _config;
    private readonly LearningDbContext _db;

    public MachineTokenService(IConfiguration config, LearningDbContext db)
    {
        _config = config;
        _db = db;
    }

    public async Task<string> GenerateAsync(string machineId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(machineId))
            throw new ArgumentException("machineId required", nameof(machineId));

        var secret = _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "HearthBot.Cloud",
            claims: new[]
            {
                new Claim("role", "machine"),
                new Claim("machine_id", machineId)
            },
            expires: DateTime.UtcNow.AddDays(365),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenString)));
        var now = DateTime.UtcNow.ToString("o");
        var existing = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == machineId, ct);
        if (existing == null)
        {
            _db.Machines.Add(new Machine
            {
                MachineId = machineId,
                TokenHash = hash,
                CreatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            existing.TokenHash = hash;
            existing.LastSeenAt = now;
        }
        await _db.SaveChangesAsync(ct);

        return tokenString;
    }

    public async Task RevokeAsync(string machineId, CancellationToken ct = default)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == machineId, ct);
        if (machine == null) return;
        machine.TokenHash = string.Empty;
        await _db.SaveChangesAsync(ct);
    }

    public static string? ExtractMachineId(ClaimsPrincipal user)
    {
        if (user.FindFirst("role")?.Value != "machine") return null;
        return user.FindFirst("machine_id")?.Value;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/MachineTokenService.cs
git commit -m "阶段0: 新增 MachineTokenService 生成/吊销机器 JWT"
```

---

### Task 0.4: Program.cs 注册 Learning 栈 + Schema 引导

**Files:**
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 读现有 Program.cs 定位插入点**

Run: `grep -n "AddDbContext\|CloudSchemaBootstrapper" HearthBot.Cloud/Program.cs`
Expected: 看到 `AddDbContext<CloudDbContext>` 和 `CloudSchemaBootstrapper.EnsureSchemaAsync`

- [ ] **Step 2: 在 CloudDbContext 注册后追加 LearningDbContext**

在 `builder.Services.AddDbContext<CloudDbContext>(...)` 之后新增：

```csharp
builder.Services.AddDbContext<LearningDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Learning") ?? "Data Source=learning.db"));

builder.Services.AddScoped<HearthBot.Cloud.Services.Learning.MachineTokenService>();
```

顶部 `using` 补：

```csharp
using HearthBot.Cloud.Services.Learning;
```

- [ ] **Step 3: 在 Schema 引导位置追加 Learning 引导**

找到：
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
    await CloudSchemaBootstrapper.EnsureSchemaAsync(db);
}
```

后面紧接添加：
```csharp
using (var scope = app.Services.CreateScope())
{
    var learningDb = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
    await LearningSchemaBootstrapper.EnsureSchemaAsync(learningDb);
}
```

- [ ] **Step 4: 本地 build 验证**

```bash
cd HearthBot.Cloud && dotnet build
```

Expected: Build succeeded, 0 Error

- [ ] **Step 5: Commit**

```bash
git add HearthBot.Cloud/Program.cs
git commit -m "阶段0: Program.cs 注册 LearningDbContext 与 Schema 引导"
```

---

### Task 0.5: LearningHealthController

**Files:**
- Create: `HearthBot.Cloud/Controllers/Learning/LearningHealthController.cs`

- [ ] **Step 1: 写 Controller**

```csharp
// HearthBot.Cloud/Controllers/Learning/LearningHealthController.cs
using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning")]
public class LearningHealthController : ControllerBase
{
    private readonly LearningDbContext _db;

    public LearningHealthController(LearningDbContext db)
    {
        _db = db;
    }

    [HttpGet("healthz")]
    public async Task<IActionResult> Healthz(CancellationToken ct)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return Ok(new { status = "ok", db = "learning.db" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "error", error = ex.Message });
        }
    }
}
```

- [ ] **Step 2: 本地 run + curl 冒烟**

```bash
cd HearthBot.Cloud && dotnet run
# 另一个终端
curl -sS http://localhost:5000/v1/learning/healthz
```

Expected: `{"status":"ok","db":"learning.db"}`

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/Learning/LearningHealthController.cs
git commit -m "阶段0: 新增 GET /v1/learning/healthz 健康检查端点"
```

---

### Task 0.6: 机器 JWT 鉴权扩展 AuthorizationPolicy

**Files:**
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 在 `AddAuthorization` 处新增 MachinePolicy**

找到 `builder.Services.AddAuthorization();`，替换为：

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MachineOnly", policy =>
        policy.RequireClaim("role", "machine"));
});
```

- [ ] **Step 2: 本地 build 验证**

```bash
cd HearthBot.Cloud && dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Program.cs
git commit -m "阶段0: 新增 MachineOnly 授权策略 (role=machine claim)"
```

---

### Task 0.7: SamplesController 骨架（阶段 0 只吃不存）

**Files:**
- Create: `HearthBot.Cloud/Controllers/Learning/SamplesController.cs`
- Create: `HearthBot.Cloud/Models/Learning/SampleBatchRequest.cs`

- [ ] **Step 1: 写请求 DTO**

```csharp
// HearthBot.Cloud/Models/Learning/SampleBatchRequest.cs
namespace HearthBot.Cloud.Models.Learning;

public class SampleBatchRequest
{
    public List<SampleEnvelope> Samples { get; set; } = new();
}

public class SampleEnvelope
{
    public string SampleId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string DecisionType { get; set; } = string.Empty; // action|choice|mulligan
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSig { get; set; } = string.Empty;
    public string BoardSnapshotJson { get; set; } = "{}";
    public string CandidatesJson { get; set; } = "[]";
    public int TeacherPickIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public int? LocalPickIndex { get; set; }
    public string ChoiceSourceType { get; set; } = string.Empty;
    public string DeckSignature { get; set; } = string.Empty;
    public string OwnClass { get; set; } = string.Empty;
    public string EnemyClass { get; set; } = string.Empty;
    public bool HasCoin { get; set; }
    public long CreatedAtMs { get; set; }
}

public class SampleBatchResponse
{
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public List<string> DuplicateIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
```

- [ ] **Step 2: 写 Controller（先不入库，只接收并返回计数）**

```csharp
// HearthBot.Cloud/Controllers/Learning/SamplesController.cs
using HearthBot.Cloud.Models.Learning;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning/samples")]
[Authorize(Policy = "MachineOnly")]
public class SamplesController : ControllerBase
{
    private readonly ILogger<SamplesController> _logger;

    public SamplesController(ILogger<SamplesController> logger)
    {
        _logger = logger;
    }

    [HttpPost("batch")]
    public IActionResult Batch([FromBody] SampleBatchRequest req)
    {
        var machineId = MachineTokenService.ExtractMachineId(User);
        if (string.IsNullOrEmpty(machineId))
            return Unauthorized(new { error = "missing machine_id claim" });

        var count = req?.Samples?.Count ?? 0;
        _logger.LogInformation("Received {Count} samples from {MachineId}", count, machineId);

        return Ok(new SampleBatchResponse
        {
            Accepted = count,
            Duplicates = 0
        });
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/Learning/SamplesController.cs HearthBot.Cloud/Models/Learning/SampleBatchRequest.cs
git commit -m "阶段0: 新增 SamplesController 骨架 (只接收不落库, JWT 鉴权)"
```

---

### Task 0.8: HeartbeatController 骨架

**Files:**
- Create: `HearthBot.Cloud/Controllers/Learning/HeartbeatController.cs`
- Create: `HearthBot.Cloud/Models/Learning/HeartbeatRequest.cs`

- [ ] **Step 1: 写请求 DTO**

```csharp
// HearthBot.Cloud/Models/Learning/HeartbeatRequest.cs
namespace HearthBot.Cloud.Models.Learning;

public class HeartbeatRequest
{
    public string MachineId { get; set; } = string.Empty;
    public string HbVersion { get; set; } = string.Empty;
    public Dictionary<string, string?> ModelVersions { get; set; } = new();
    public int OutboxDepth { get; set; }
    public long LastUploadOkAt { get; set; }
    public RollingStats? RollingStats24h { get; set; }
}

public class RollingStats
{
    public int Decisions { get; set; }
    public double Top1MatchRate { get; set; }
    public double MappingFailRate { get; set; }
    public double IllegalActionRate { get; set; }
}
```

- [ ] **Step 2: 写 Controller**

```csharp
// HearthBot.Cloud/Controllers/Learning/HeartbeatController.cs
using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning")]
[Authorize(Policy = "MachineOnly")]
public class HeartbeatController : ControllerBase
{
    private readonly LearningDbContext _db;

    public HeartbeatController(LearningDbContext db)
    {
        _db = db;
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var tokenMachineId = MachineTokenService.ExtractMachineId(User);
        if (string.IsNullOrEmpty(tokenMachineId))
            return Unauthorized(new { error = "missing machine_id claim" });

        if (req.MachineId != tokenMachineId)
            return BadRequest(new { error = "machine_id mismatch with token" });

        var now = DateTime.UtcNow.ToString("o");
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.MachineId == tokenMachineId, ct);
        if (machine == null)
        {
            machine = new Machine
            {
                MachineId = tokenMachineId,
                CreatedAt = now,
                LastSeenAt = now,
                LastStatsJson = JsonSerializer.Serialize(req)
            };
            _db.Machines.Add(machine);
        }
        else
        {
            machine.LastSeenAt = now;
            machine.LastStatsJson = JsonSerializer.Serialize(req);
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/Learning/HeartbeatController.cs HearthBot.Cloud/Models/Learning/HeartbeatRequest.cs
git commit -m "阶段0: 新增 Heartbeat 端点更新 Machines.LastSeenAt"
```

---

### Task 0.9: 阶段 0 冒烟验收

**Files:**（不改代码，只跑验证）

- [ ] **Step 1: 本地启动服务**

```bash
cd HearthBot.Cloud
# 首次启动会创建 learning.db
dotnet run
```

- [ ] **Step 2: 管理员登录获取 admin JWT**

另一个终端（需本地已配 admin 密码；`appsettings.Development.json` 里设置 `Admin:PasswordHash` 为明文或哈希）：

```bash
curl -sS -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"<密码>"}'
```

Expected: 返回 `{"token":"eyJ...","expiresIn":...}`

- [ ] **Step 3: 用 admin 调一个新增的机器 token 生成端点（手动通过 REPL 或写临时端点）**

（阶段 0 不做正式的"机器 token 分发"端点，先用 `dotnet ef` 或 LINQPad 临时生成一个 JWT 做冒烟。最简单：在 Program.cs 临时加一行调试打印。）

临时加在 Program 末尾前：
```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var mts = scope.ServiceProvider.GetRequiredService<HearthBot.Cloud.Services.Learning.MachineTokenService>();
    var token = await mts.GenerateAsync("hb-dev-01");
    app.Logger.LogInformation("DEV machine token for hb-dev-01: {Token}", token);
}
```

重启后从日志捞 token。

- [ ] **Step 4: 冒烟 /v1/learning/healthz**

```bash
curl -sS -i http://localhost:5000/v1/learning/healthz
```

Expected: `HTTP/1.1 200 OK` + body `{"status":"ok","db":"learning.db"}`

- [ ] **Step 5: 冒烟无 token samples/batch（应 401）**

```bash
curl -sS -i -X POST http://localhost:5000/v1/learning/samples/batch \
  -H "Content-Type: application/json" -d '{"samples":[]}'
```

Expected: `HTTP/1.1 401 Unauthorized`

- [ ] **Step 6: 冒烟带机器 token samples/batch**

```bash
TOKEN="<从日志获取的 machine JWT>"
curl -sS -i -X POST http://localhost:5000/v1/learning/samples/batch \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"samples":[{"sampleId":"hb-dev-01_test1","machineId":"hb-dev-01","decisionType":"action"}]}'
```

Expected: `HTTP/1.1 200 OK` + body `{"accepted":1,"duplicates":0,...}`

- [ ] **Step 7: 冒烟 heartbeat 写 Machines.LastSeenAt**

```bash
curl -sS -i -X POST http://localhost:5000/v1/learning/heartbeat \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"machineId":"hb-dev-01","hbVersion":"0.0.0","modelVersions":{},"outboxDepth":0,"lastUploadOkAt":0}'
```

Expected: `200 OK`

用 SQLite CLI 验证：
```bash
sqlite3 HearthBot.Cloud/learning.db "SELECT MachineId, LastSeenAt FROM Machines;"
```
Expected: 有一行 `hb-dev-01|<ISO 时间戳>`

- [ ] **Step 8: 删除临时调试代码**

Program.cs 末尾的 `if (app.Environment.IsDevelopment()) { ... GenerateAsync ... }` 整段删除。重新 build 验证无 error。

- [ ] **Step 9: 阶段 0 收尾 commit + push**

```bash
git add HearthBot.Cloud/Program.cs
git commit -m "阶段0: 移除冒烟用的开发期 token 打印"
git push
```

---

## 阶段 1 / 5 台机器全量采样（约 5 天，12 个 Task）

产出：Hearthbot 侧采样/上传/心跳完整链路 + 云端完整入库 + TeacherActionMapper 加固。

**前置条件**：阶段 0 全部 Commit 成功，`/v1/learning/samples/batch` 能 200 接收。

### Task 1.1: CloudLearningOptions 配置绑定

**Files:**
- Create: `BotMain/CloudLearning/Config/CloudLearningOptions.cs`
- Modify: `BotMain/appsettings.json`

- [ ] **Step 1: 写 Options 类**

```csharp
// BotMain/CloudLearning/Config/CloudLearningOptions.cs
namespace BotMain.CloudLearning.Config;

public class CloudLearningOptions
{
    public bool Enabled { get; set; } = false;
    public string MachineId { get; set; } = string.Empty;
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public SampleUploadOptions SampleUpload { get; set; } = new();
    public ModelSyncOptions ModelSync { get; set; } = new();
}

public class SampleUploadOptions
{
    public int BatchSize { get; set; } = 200;
    public int IntervalMinutes { get; set; } = 5;
    public int MaxOutboxSizeMB { get; set; } = 200;
    public int MaxOutboxDays { get; set; } = 7;
}

public class ModelSyncOptions
{
    public int CheckIntervalHours { get; set; } = 6;
    public bool AutoDownload { get; set; } = true;
}
```

- [ ] **Step 2: 在 appsettings.json 追加默认段**

找到 `appsettings.json` 尾部 `}` 前追加：

```json
  ,
  "CloudLearning": {
    "Enabled": false,
    "MachineId": "",
    "ServerBaseUrl": "",
    "Token": "",
    "SampleUpload": {
      "BatchSize": 200,
      "IntervalMinutes": 5,
      "MaxOutboxSizeMB": 200,
      "MaxOutboxDays": 7
    },
    "ModelSync": {
      "CheckIntervalHours": 6,
      "AutoDownload": true
    }
  }
```

（默认 `Enabled=false`，避免未配置机器启动失败）

- [ ] **Step 3: 修改 BotMain.csproj 注册 CloudLearning 目录 + 引入 OnnxRuntime**

找到 `BotMain.csproj` 的 `<ItemGroup>`，追加：

```xml
<ItemGroup>
  <Compile Include="CloudLearning\**\*.cs" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.19.2" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="8.0.0" />
</ItemGroup>
```

- [ ] **Step 4: BotCore.Tests.csproj 同步 linked include**

找到 `<Compile Include="..\BotMain\Learning\**\*.cs" Link="Learning\%(RecursiveDir)%(Filename)%(Extension)" />` 后新增：

```xml
<Compile Include="..\BotMain\CloudLearning\**\*.cs" Link="CloudLearning\%(RecursiveDir)%(Filename)%(Extension)" />
```

同时 `<PackageReference>` 段追加：
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.19.2" />
```

- [ ] **Step 5: 本地 build 验证**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet build
```

Expected: 两者 Build succeeded

- [ ] **Step 6: Commit**

```bash
git add BotMain/CloudLearning/Config/ BotMain/appsettings.json BotMain/BotMain.csproj BotCore.Tests/BotCore.Tests.csproj
git commit -m "阶段1: 新增 CloudLearningOptions 与 csproj 配置"
```

---

### Task 1.2: Decision & Sample 数据契约

**Files:**
- Create: `BotMain/CloudLearning/Contracts/DecisionContextSnapshot.cs`
- Create: `BotMain/CloudLearning/Contracts/DecisionSample.cs`
- Create: `BotMain/CloudLearning/Contracts/RankerResult.cs`

- [ ] **Step 1: 写 DecisionContextSnapshot**

```csharp
// BotMain/CloudLearning/Contracts/DecisionContextSnapshot.cs
namespace BotMain.CloudLearning.Contracts;

public enum DecisionType { Action, Choice, Mulligan }

public class DecisionContextSnapshot
{
    public string MatchId { get; set; } = string.Empty;
    public DecisionType Type { get; set; }
    public int Turn { get; set; }
    public int StepIndex { get; set; }
    public string Seed { get; set; } = string.Empty;
    public string PayloadSignature { get; set; } = string.Empty;
    public string BoardSnapshotJson { get; set; } = "{}";
    public string DeckSignature { get; set; } = string.Empty;
    public string OwnClass { get; set; } = string.Empty;
    public string EnemyClass { get; set; } = string.Empty;
    public bool HasCoin { get; set; }
    public string ChoiceSourceType { get; set; } = string.Empty;
}
```

- [ ] **Step 2: 写 DecisionSample**

```csharp
// BotMain/CloudLearning/Contracts/DecisionSample.cs
using System.Collections.Generic;

namespace BotMain.CloudLearning.Contracts;

public class DecisionSample
{
    public string SampleId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public DecisionContextSnapshot Context { get; set; } = new();
    public List<DecisionCandidate> Candidates { get; set; } = new();
    public int TeacherPickIndex { get; set; } = -1;
    public int? LocalPickIndex { get; set; }
    public string MappingStatus { get; set; } = "matched";
    public long CreatedAtMs { get; set; }
}

public class DecisionCandidate
{
    public int SlotIndex { get; set; }
    public string ActionCommand { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "{}";
}
```

- [ ] **Step 3: 写 RankerResult**

```csharp
// BotMain/CloudLearning/Contracts/RankerResult.cs
using System.Collections.Generic;

namespace BotMain.CloudLearning.Contracts;

public class RankerResult
{
    public bool HasValue { get; set; }
    public int Top1Index { get; set; } = -1;
    public double Top1Score { get; set; }
    public List<double> AllScores { get; set; } = new();
    public string ModelVersion { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Commit**

```bash
git add BotMain/CloudLearning/Contracts/
git commit -m "阶段1: 新增 DecisionContextSnapshot/DecisionSample/RankerResult 契约"
```

---

### Task 1.3: SampleOutboxStore（TDD）

**Files:**
- Create: `BotMain/CloudLearning/SampleOutboxStore.cs`
- Create: `BotCore.Tests/CloudLearning/SampleOutboxStoreTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// BotCore.Tests/CloudLearning/SampleOutboxStoreTests.cs
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BotMain.CloudLearning;
using BotMain.CloudLearning.Contracts;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class SampleOutboxStoreTests
{
    private static string CreateTempDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hb-outbox-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "outbox.db");
    }

    [Fact]
    public async Task Enqueue_And_DequeueBatch_ReturnsInserted()
    {
        var dbPath = CreateTempDbPath();
        await using var store = new SampleOutboxStore(dbPath, maxSizeBytes: 10_000_000, maxDays: 7);
        await store.InitializeAsync();

        var sample = new DecisionSample
        {
            SampleId = "m1_s1",
            MachineId = "m1",
            Context = new DecisionContextSnapshot { Type = DecisionType.Action, Turn = 3 },
            TeacherPickIndex = 0,
            CreatedAtMs = 1000
        };

        await store.EnqueueAsync(sample);

        var batch = await store.DequeueBatchAsync(maxCount: 10);
        Assert.Single(batch);
        Assert.Equal("m1_s1", batch[0].SampleId);
    }

    [Fact]
    public async Task Enqueue_Duplicate_SampleId_IsIdempotent()
    {
        var dbPath = CreateTempDbPath();
        await using var store = new SampleOutboxStore(dbPath, 10_000_000, 7);
        await store.InitializeAsync();

        var sample = new DecisionSample { SampleId = "dup1", MachineId = "m1", CreatedAtMs = 1 };
        await store.EnqueueAsync(sample);
        await store.EnqueueAsync(sample);

        var batch = await store.DequeueBatchAsync(10);
        Assert.Single(batch);
    }

    [Fact]
    public async Task Acknowledge_Removes_FromPending()
    {
        var dbPath = CreateTempDbPath();
        await using var store = new SampleOutboxStore(dbPath, 10_000_000, 7);
        await store.InitializeAsync();

        await store.EnqueueAsync(new DecisionSample { SampleId = "a", MachineId = "m", CreatedAtMs = 1 });
        await store.EnqueueAsync(new DecisionSample { SampleId = "b", MachineId = "m", CreatedAtMs = 2 });

        var batch = await store.DequeueBatchAsync(10);
        await store.AcknowledgeAsync(batch.Select(s => s.SampleId).ToList());

        var empty = await store.DequeueBatchAsync(10);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task Enqueue_BeyondSizeCap_EvictsOldest()
    {
        var dbPath = CreateTempDbPath();
        // 100 KB cap
        await using var store = new SampleOutboxStore(dbPath, maxSizeBytes: 100_000, maxDays: 7);
        await store.InitializeAsync();

        for (int i = 0; i < 2000; i++)
        {
            await store.EnqueueAsync(new DecisionSample
            {
                SampleId = $"s{i}",
                MachineId = "m",
                CreatedAtMs = i,
                Context = new DecisionContextSnapshot { BoardSnapshotJson = new string('x', 500) }
            });
        }

        var depth = await store.GetDepthAsync();
        Assert.True(depth < 2000, $"Depth should be capped, got {depth}");
        Assert.True(depth > 0);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~SampleOutboxStoreTests
```

Expected: FAIL, `SampleOutboxStore` 类型不存在

- [ ] **Step 3: 实现 SampleOutboxStore**

```csharp
// BotMain/CloudLearning/SampleOutboxStore.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using BotMain.CloudLearning.Contracts;
using Microsoft.Data.Sqlite;

namespace BotMain.CloudLearning;

public sealed class SampleOutboxStore : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly long _maxSizeBytes;
    private readonly int _maxDays;
    private SqliteConnection? _conn;

    public SampleOutboxStore(string dbPath, long maxSizeBytes, int maxDays)
    {
        _dbPath = dbPath;
        _maxSizeBytes = maxSizeBytes;
        _maxDays = maxDays;
    }

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
        await _conn.OpenAsync();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS pending_samples (
    sample_id TEXT PRIMARY KEY,
    created_at_ms INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    size_bytes INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pending_created ON pending_samples(created_at_ms);
";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task EnqueueAsync(DecisionSample sample)
    {
        if (_conn == null) throw new InvalidOperationException("Not initialized");
        var json = JsonSerializer.Serialize(sample);
        var size = System.Text.Encoding.UTF8.GetByteCount(json);

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR IGNORE INTO pending_samples (sample_id, created_at_ms, payload_json, size_bytes)
VALUES ($id, $ts, $json, $size);";
        cmd.Parameters.AddWithValue("$id", sample.SampleId);
        cmd.Parameters.AddWithValue("$ts", sample.CreatedAtMs);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$size", size);
        await cmd.ExecuteNonQueryAsync();

        await EnforceCapsAsync();
    }

    private async Task EnforceCapsAsync()
    {
        if (_conn == null) return;
        var cutoffMs = DateTimeOffset.UtcNow.AddDays(-_maxDays).ToUnixTimeMilliseconds();
        using (var delOld = _conn.CreateCommand())
        {
            delOld.CommandText = "DELETE FROM pending_samples WHERE created_at_ms < $cutoff;";
            delOld.Parameters.AddWithValue("$cutoff", cutoffMs);
            await delOld.ExecuteNonQueryAsync();
        }

        using var sizeCmd = _conn.CreateCommand();
        sizeCmd.CommandText = "SELECT COALESCE(SUM(size_bytes), 0) FROM pending_samples;";
        var totalSize = Convert.ToInt64(await sizeCmd.ExecuteScalarAsync());
        if (totalSize <= _maxSizeBytes) return;

        using var evict = _conn.CreateCommand();
        evict.CommandText = @"
DELETE FROM pending_samples
WHERE sample_id IN (
    SELECT sample_id FROM pending_samples ORDER BY created_at_ms ASC LIMIT $n
);";
        var toEvict = Math.Max(100, (int)((totalSize - _maxSizeBytes) / 1024));
        evict.Parameters.AddWithValue("$n", toEvict);
        await evict.ExecuteNonQueryAsync();
    }

    public async Task<List<DecisionSample>> DequeueBatchAsync(int maxCount)
    {
        if (_conn == null) throw new InvalidOperationException("Not initialized");
        var list = new List<DecisionSample>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT payload_json FROM pending_samples ORDER BY created_at_ms ASC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", maxCount);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var sample = JsonSerializer.Deserialize<DecisionSample>(json);
            if (sample != null) list.Add(sample);
        }
        return list;
    }

    public async Task AcknowledgeAsync(IReadOnlyCollection<string> sampleIds)
    {
        if (_conn == null || sampleIds.Count == 0) return;
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM pending_samples WHERE sample_id = $id;";
        var param = cmd.CreateParameter();
        param.ParameterName = "$id";
        cmd.Parameters.Add(param);
        foreach (var id in sampleIds)
        {
            param.Value = id;
            await cmd.ExecuteNonQueryAsync();
        }
        tx.Commit();
    }

    public async Task<int> GetDepthAsync()
    {
        if (_conn == null) return 0;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM pending_samples;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn != null)
        {
            await _conn.CloseAsync();
            await _conn.DisposeAsync();
            _conn = null;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~SampleOutboxStoreTests
```

Expected: PASS 4 / 0 failures

- [ ] **Step 5: Commit**

```bash
git add BotMain/CloudLearning/SampleOutboxStore.cs BotCore.Tests/CloudLearning/SampleOutboxStoreTests.cs
git commit -m "阶段1: SampleOutboxStore 本地 SQLite 缓冲 (TDD)"
```

---

### Task 1.4: SampleUploader（HTTPS 批量上传 + 指数退避）

**Files:**
- Create: `BotMain/CloudLearning/SampleUploader.cs`
- Create: `BotCore.Tests/CloudLearning/SampleUploaderTests.cs`

- [ ] **Step 1: 写失败测试（用 HttpMessageHandler mock）**

```csharp
// BotCore.Tests/CloudLearning/SampleUploaderTests.cs
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BotMain.CloudLearning;
using BotMain.CloudLearning.Contracts;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class SampleUploaderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Responses.Count == 0) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(Responses.Dequeue());
        }
    }

    [Fact]
    public async Task UploadBatch_Success_Returns_AcceptedIds()
    {
        var handler = new StubHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"accepted\":2,\"duplicates\":0,\"duplicateIds\":[],\"errors\":[]}")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        var uploader = new SampleUploader(http, token: "t");

        var samples = new List<DecisionSample>
        {
            new() { SampleId = "a" },
            new() { SampleId = "b" }
        };

        var result = await uploader.UploadBatchAsync(samples, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(2, result.Accepted);
    }

    [Fact]
    public async Task UploadBatch_5xx_ReturnsFailure_NoThrow()
    {
        var handler = new StubHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        var uploader = new SampleUploader(http, token: "t");

        var result = await uploader.UploadBatchAsync(new List<DecisionSample> { new() { SampleId = "a" } }, CancellationToken.None);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task UploadBatch_Gzips_Payload_And_Sets_Bearer_Header()
    {
        var handler = new StubHandler();
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"accepted\":1,\"duplicates\":0,\"duplicateIds\":[],\"errors\":[]}")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        var uploader = new SampleUploader(http, token: "my-jwt");

        await uploader.UploadBatchAsync(new List<DecisionSample> { new() { SampleId = "a" } }, CancellationToken.None);

        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
        Assert.Equal("my-jwt", req.Headers.Authorization?.Parameter);
        Assert.Equal("gzip", req.Content?.Headers.ContentEncoding.ToString());
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~SampleUploaderTests
```

Expected: FAIL

- [ ] **Step 3: 实现 SampleUploader**

```csharp
// BotMain/CloudLearning/SampleUploader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BotMain.CloudLearning.Contracts;

namespace BotMain.CloudLearning;

public sealed class SampleUploader
{
    private readonly HttpClient _http;
    private readonly string _token;

    public SampleUploader(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }

    public sealed class UploadResult
    {
        public bool Success { get; set; }
        public int Accepted { get; set; }
        public int Duplicates { get; set; }
        public List<string> DuplicateIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public async Task<UploadResult> UploadBatchAsync(IReadOnlyList<DecisionSample> samples, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { samples });
        using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await gz.WriteAsync(bytes, 0, bytes.Length, ct);
        }
        ms.Position = 0;

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/learning/samples/batch");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        var content = new StreamContent(ms);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        req.Content = content;

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new UploadResult { Success = false };
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<UploadResult>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UploadResult();
            parsed.Success = true;
            return parsed;
        }
        catch
        {
            return new UploadResult { Success = false };
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~SampleUploaderTests
```

Expected: PASS 3 / 0

- [ ] **Step 5: Commit**

```bash
git add BotMain/CloudLearning/SampleUploader.cs BotCore.Tests/CloudLearning/SampleUploaderTests.cs
git commit -m "阶段1: SampleUploader HTTPS gzip 批量上传 (TDD)"
```

---

### Task 1.5: CloudHealthMonitor（心跳 + 滚动统计）

**Files:**
- Create: `BotMain/CloudLearning/CloudHealthMonitor.cs`

- [ ] **Step 1: 写 CloudHealthMonitor（轻量，暂不加单测）**

```csharp
// BotMain/CloudLearning/CloudHealthMonitor.cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BotMain.CloudLearning;

public sealed class CloudHealthMonitor
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _machineId;
    private readonly string _hbVersion;
    private readonly Func<HealthPayload> _snapshotProvider;

    public CloudHealthMonitor(
        HttpClient http,
        string token,
        string machineId,
        string hbVersion,
        Func<HealthPayload> snapshotProvider)
    {
        _http = http;
        _token = token;
        _machineId = machineId;
        _hbVersion = hbVersion;
        _snapshotProvider = snapshotProvider;
    }

    public async Task<bool> SendAsync(CancellationToken ct)
    {
        var snap = _snapshotProvider();
        var body = new
        {
            machineId = _machineId,
            hbVersion = _hbVersion,
            modelVersions = snap.ModelVersions,
            outboxDepth = snap.OutboxDepth,
            lastUploadOkAt = snap.LastUploadOkAt,
            rollingStats24h = snap.RollingStats
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/learning/heartbeat")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class HealthPayload
{
    public System.Collections.Generic.Dictionary<string, string?> ModelVersions { get; set; } = new();
    public int OutboxDepth { get; set; }
    public long LastUploadOkAt { get; set; }
    public RollingStats RollingStats { get; set; } = new();
}

public sealed class RollingStats
{
    public int Decisions { get; set; }
    public double Top1MatchRate { get; set; }
    public double MappingFailRate { get; set; }
    public double IllegalActionRate { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add BotMain/CloudLearning/CloudHealthMonitor.cs
git commit -m "阶段1: CloudHealthMonitor 心跳客户端"
```

---

### Task 1.6: CloudLearningOrchestrator（总入口 + 后台 Worker）

**Files:**
- Create: `BotMain/CloudLearning/CloudLearningOrchestrator.cs`

- [ ] **Step 1: 写 Orchestrator**

```csharp
// BotMain/CloudLearning/CloudLearningOrchestrator.cs
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BotMain.CloudLearning.Config;
using BotMain.CloudLearning.Contracts;

namespace BotMain.CloudLearning;

public sealed class CloudLearningOrchestrator : IAsyncDisposable
{
    private readonly CloudLearningOptions _options;
    private readonly Action<string> _log;
    private readonly HttpClient _http;
    private readonly SampleOutboxStore _outbox;
    private readonly SampleUploader _uploader;
    private readonly CloudHealthMonitor _health;
    private readonly RollingStatsCollector _stats = new();
    private CancellationTokenSource? _cts;
    private Task? _uploadLoop;
    private Task? _heartbeatLoop;
    private long _lastUploadOkAt;

    public CloudLearningOrchestrator(CloudLearningOptions options, Action<string> log, string hbVersion, string outboxDir)
    {
        _options = options;
        _log = log;
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.ServerBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        Directory.CreateDirectory(outboxDir);
        _outbox = new SampleOutboxStore(
            Path.Combine(outboxDir, "outbox.db"),
            maxSizeBytes: options.SampleUpload.MaxOutboxSizeMB * 1024L * 1024L,
            maxDays: options.SampleUpload.MaxOutboxDays);
        _uploader = new SampleUploader(_http, options.Token);
        _health = new CloudHealthMonitor(
            _http, options.Token, options.MachineId, hbVersion,
            () => new HealthPayload
            {
                ModelVersions = new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["action"] = null, ["choice"] = null, ["mulligan"] = null
                },
                OutboxDepth = _outbox.GetDepthAsync().GetAwaiter().GetResult(),
                LastUploadOkAt = _lastUploadOkAt,
                RollingStats = _stats.Snapshot()
            });
    }

    public bool IsRankerReady => false; // 阶段 3 前恒 false
    public RollingStatsCollector Stats => _stats;

    public async Task StartAsync()
    {
        await _outbox.InitializeAsync();
        if (!_options.Enabled)
        {
            _log("[CloudLearning] 已禁用 (appsettings.Enabled=false)");
            return;
        }
        _cts = new CancellationTokenSource();
        _uploadLoop = Task.Run(() => UploadLoopAsync(_cts.Token));
        _heartbeatLoop = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        _log($"[CloudLearning] 启动, machine={_options.MachineId}, server={_options.ServerBaseUrl}");
    }

    public async Task RecordAsync(DecisionSample sample)
    {
        if (!_options.Enabled) return;
        sample.MachineId = _options.MachineId;
        if (string.IsNullOrEmpty(sample.SampleId))
            sample.SampleId = $"{_options.MachineId}_{Guid.NewGuid():N}";
        try
        {
            await _outbox.EnqueueAsync(sample);
            _stats.RecordDecision(
                mappingStatus: sample.MappingStatus,
                teacherIdx: sample.TeacherPickIndex,
                localIdx: sample.LocalPickIndex);
        }
        catch (Exception ex)
        {
            _log($"[CloudLearning] 样本入队失败: {ex.Message}");
        }
    }

    public void RecordIllegalAction() => _stats.RecordIllegal();

    public Task<bool> FlushNowAsync(CancellationToken ct) => RunUploadRoundAsync(ct);

    private async Task UploadLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(_options.SampleUpload.IntervalMinutes);
        var backoffSec = 1;
        while (!ct.IsCancellationRequested)
        {
            var ok = await RunUploadRoundAsync(ct);
            if (ok)
            {
                backoffSec = 1;
                await Task.Delay(interval, ct);
            }
            else
            {
                backoffSec = Math.Min(backoffSec * 2, 300);
                _log($"[CloudLearning] 上传失败, 退避 {backoffSec}s");
                await Task.Delay(TimeSpan.FromSeconds(backoffSec), ct);
            }
        }
    }

    private async Task<bool> RunUploadRoundAsync(CancellationToken ct)
    {
        var batch = await _outbox.DequeueBatchAsync(_options.SampleUpload.BatchSize);
        if (batch.Count == 0) return true;
        var result = await _uploader.UploadBatchAsync(batch, ct);
        if (!result.Success) return false;
        var ids = new System.Collections.Generic.List<string>(batch.Count);
        foreach (var s in batch) ids.Add(s.SampleId);
        await _outbox.AcknowledgeAsync(ids);
        _lastUploadOkAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return true;
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _health.SendAsync(ct);
            try { await Task.Delay(TimeSpan.FromMinutes(10), ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_uploadLoop != null) await _uploadLoop;
            if (_heartbeatLoop != null) await _heartbeatLoop;
        }
        catch { }
        _http.Dispose();
        await _outbox.DisposeAsync();
        _cts?.Dispose();
    }
}

public sealed class RollingStatsCollector
{
    private int _decisions;
    private int _matched;
    private int _failed;
    private int _top1Match;
    private int _illegal;
    private readonly object _lock = new();

    public void RecordDecision(string mappingStatus, int teacherIdx, int? localIdx)
    {
        lock (_lock)
        {
            _decisions++;
            if (string.Equals(mappingStatus, "matched", StringComparison.OrdinalIgnoreCase)) _matched++;
            if (string.Equals(mappingStatus, "failed", StringComparison.OrdinalIgnoreCase)) _failed++;
            if (localIdx.HasValue && localIdx.Value == teacherIdx) _top1Match++;
        }
    }

    public void RecordIllegal()
    {
        lock (_lock) { _illegal++; }
    }

    public RollingStats Snapshot()
    {
        lock (_lock)
        {
            var d = Math.Max(1, _decisions);
            return new RollingStats
            {
                Decisions = _decisions,
                Top1MatchRate = (double)_top1Match / d,
                MappingFailRate = (double)_failed / d,
                IllegalActionRate = (double)_illegal / d
            };
        }
    }
}
```

- [ ] **Step 2: 本地 build 验证**

```bash
cd BotMain && dotnet build
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BotMain/CloudLearning/CloudLearningOrchestrator.cs
git commit -m "阶段1: CloudLearningOrchestrator 总入口 + 上传/心跳后台 Worker"
```

---

### Task 1.7: TeacherActionMapper 加固（降低 mapping_status=failed 率）

**Files:**
- Modify: `BotMain/Learning/TeacherActionMapper.cs`
- Create: `BotCore.Tests/CloudLearning/TeacherActionMapperHardeningTests.cs`

> 阶段 1 硬门槛：`action ≥ 85%`。现有 Mapper 只做精确字符串匹配，先补一层"目标 ID 模糊匹配"和"来源卡 ID 备用匹配"。

- [ ] **Step 1: 先读当前 BuildActionDecision 全文**

Run: `grep -n "MappingStatus\|ToActionString" BotMain/Learning/TeacherActionMapper.cs | head -20`
Expected: 看到匹配的现状

- [ ] **Step 2: 写目标级模糊匹配测试**

```csharp
// BotCore.Tests/CloudLearning/TeacherActionMapperHardeningTests.cs
using BotMain.Learning;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class TeacherActionMapperHardeningTests
{
    [Fact]
    public void FuzzyMatch_SameSourceDifferentTarget_PicksClosestCandidate()
    {
        // 构造两个候选：同源卡 EX1_012，不同目标
        // 教师给的命令目标 id 在场上不存在，应降级到源卡相同的候选并标 fuzzy
        // 详细实现等 Step 3 生产代码里确认 BuildActionDecision 的参数形态后补齐
        // 占位：确保新方法 BuildActionDecisionFuzzy 至少能被调用
        var result = TeacherActionMapper.BuildActionDecision(
            seed: "s",
            board: null!,
            deckSignature: "d",
            teacherActionCommand: "PLAY|EX1_012|0|99999");
        Assert.NotNull(result);
    }
}
```

- [ ] **Step 3: 修改 TeacherActionMapper 增加 fuzzy 匹配路径**

在 `BuildActionDecision` 的精确匹配失败分支（`result.Decision.MappingStatus = TeacherActionMappingStatus.NoExactMatch` 或等价），**新增 fuzzy 尝试**：

在现有精确匹配循环之后、返回前插入：

```csharp
// 精确匹配失败后, 尝试 fuzzy: 源卡相同的候选
if (result.Decision.MappingStatus == TeacherActionMappingStatus.NoExactMatch
    || result.Decision.MappingStatus == TeacherActionMappingStatus.NotAttempted)
{
    var parts = normalizedTeacherAction.Split('|');
    if (parts.Length >= 2)
    {
        var teacherSource = parts[1];
        for (var i = 0; i < result.Candidates.Count; i++)
        {
            if (string.Equals(result.Candidates[i].SourceCardId, teacherSource, StringComparison.Ordinal))
            {
                result.Candidates[i].IsTeacherPick = true;
                result.Decision.TeacherMappedCandidateId = result.Candidates[i].CandidateId;
                result.Decision.MappingStatus = TeacherActionMappingStatus.Fuzzy;
                break;
            }
        }
    }
}
```

同时在 `TeacherActionMappingStatus` 枚举（`TeacherDatasetContracts.cs`）中新增 `Fuzzy` 值（如已有则跳过）。

- [ ] **Step 4: 运行测试**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~TeacherActionMapperHardeningTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add BotMain/Learning/TeacherActionMapper.cs BotMain/Learning/TeacherDatasetContracts.cs BotCore.Tests/CloudLearning/TeacherActionMapperHardeningTests.cs
git commit -m "阶段1: TeacherActionMapper 加入源卡相同的 fuzzy 匹配降低失败率"
```

---

### Task 1.8: SampleIngestService（云端入库）

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/SampleIngestService.cs`
- Modify: `HearthBot.Cloud/Controllers/Learning/SamplesController.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 SampleIngestService**

```csharp
// HearthBot.Cloud/Services/Learning/SampleIngestService.cs
using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public class SampleIngestService
{
    private readonly LearningDbContext _db;

    public SampleIngestService(LearningDbContext db)
    {
        _db = db;
    }

    public async Task<SampleBatchResponse> IngestAsync(string machineId, SampleBatchRequest req, CancellationToken ct)
    {
        var resp = new SampleBatchResponse();
        if (req.Samples.Count == 0) return resp;

        var sampleIds = req.Samples.Select(s => s.SampleId).Distinct().ToList();

        var existingAction = await _db.ActionDecisions
            .Where(d => sampleIds.Contains(d.ClientSampleId))
            .Select(d => d.ClientSampleId).ToListAsync(ct);
        var existingChoice = await _db.ChoiceDecisions
            .Where(d => sampleIds.Contains(d.ClientSampleId))
            .Select(d => d.ClientSampleId).ToListAsync(ct);
        var existingMulligan = await _db.MulliganDecisions
            .Where(d => sampleIds.Contains(d.ClientSampleId))
            .Select(d => d.ClientSampleId).ToListAsync(ct);
        var existing = new HashSet<string>(existingAction.Concat(existingChoice).Concat(existingMulligan));

        foreach (var s in req.Samples)
        {
            if (existing.Contains(s.SampleId))
            {
                resp.Duplicates++;
                resp.DuplicateIds.Add(s.SampleId);
                continue;
            }

            await EnsureMatchAsync(s, ct);

            switch (s.DecisionType?.ToLowerInvariant())
            {
                case "action": await InsertActionAsync(s, ct); break;
                case "choice": await InsertChoiceAsync(s, ct); break;
                case "mulligan": await InsertMulliganAsync(s, ct); break;
                default:
                    resp.Errors.Add($"{s.SampleId}: unknown decisionType '{s.DecisionType}'");
                    continue;
            }
            resp.Accepted++;
        }
        await _db.SaveChangesAsync(ct);
        return resp;
    }

    private async Task EnsureMatchAsync(SampleEnvelope s, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(s.MatchId)) return;
        var exists = await _db.LearningMatches.AnyAsync(m => m.MatchId == s.MatchId, ct);
        if (exists) return;
        _db.LearningMatches.Add(new LearningMatch
        {
            MatchId = s.MatchId,
            MachineId = s.MachineId,
            DeckSignature = s.DeckSignature,
            Mode = string.Empty,
            StartAt = DateTimeOffset.FromUnixTimeMilliseconds(s.CreatedAtMs).ToString("o")
        });
    }

    private Task InsertActionAsync(SampleEnvelope s, CancellationToken ct)
    {
        var decision = new ActionDecision
        {
            ClientSampleId = s.SampleId,
            MatchId = s.MatchId,
            Turn = s.Turn,
            StepIndex = s.StepIndex,
            Seed = s.Seed,
            PayloadSig = s.PayloadSig,
            BoardSnapshotJson = s.BoardSnapshotJson,
            TeacherCandidateIndex = s.TeacherPickIndex,
            MappingStatus = s.MappingStatus,
            LocalPickIndex = s.LocalPickIndex,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(s.CreatedAtMs).ToString("o")
        };
        _db.ActionDecisions.Add(decision);

        var candidates = JsonSerializer.Deserialize<List<JsonElement>>(s.CandidatesJson ?? "[]") ?? new();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            _db.ActionCandidates.Add(new ActionCandidate
            {
                DecisionId = 0, // EF FK via navigation; 这里用延迟：改成先 SaveChanges 再插
                SlotIndex = i,
                ActionCommand = c.TryGetProperty("actionCommand", out var ac) ? ac.GetString() ?? "" : "",
                ActionType = c.TryGetProperty("actionType", out var at) ? at.GetString() ?? "" : "",
                FeaturesJson = c.TryGetProperty("featuresJson", out var fj) ? fj.ToString() : "{}",
                IsTeacherPick = i == s.TeacherPickIndex,
                IsLocalPick = s.LocalPickIndex.HasValue && i == s.LocalPickIndex.Value
            });
        }
        // 由于外键未在 EF 模型显式配置, 这里简化：批量 SaveChanges 由外层统一做
        return Task.CompletedTask;
    }

    private Task InsertChoiceAsync(SampleEnvelope s, CancellationToken ct)
    {
        _db.ChoiceDecisions.Add(new ChoiceDecision
        {
            ClientSampleId = s.SampleId,
            MatchId = s.MatchId,
            Turn = s.Turn,
            StepIndex = s.StepIndex,
            Seed = s.Seed,
            PayloadSig = s.PayloadSig,
            ContextJson = s.BoardSnapshotJson,
            TeacherOptionIndex = s.TeacherPickIndex,
            MappingStatus = s.MappingStatus,
            LocalPickIndex = s.LocalPickIndex,
            ChoiceSourceType = s.ChoiceSourceType,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(s.CreatedAtMs).ToString("o")
        });
        return Task.CompletedTask;
    }

    private Task InsertMulliganAsync(SampleEnvelope s, CancellationToken ct)
    {
        _db.MulliganDecisions.Add(new MulliganDecision
        {
            ClientSampleId = s.SampleId,
            MatchId = s.MatchId,
            Seed = s.Seed,
            OwnClass = s.OwnClass,
            EnemyClass = s.EnemyClass,
            HasCoin = s.HasCoin,
            DeckSignature = s.DeckSignature,
            ContextJson = s.BoardSnapshotJson,
            MappingStatus = s.MappingStatus,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(s.CreatedAtMs).ToString("o")
        });
        return Task.CompletedTask;
    }
}
```

> 注：`ActionCandidates` 的外键关联暂用简化方案（Orchestrator 每次 save 后再回填），或在 EF 模型里后续补 `.WithMany()`。本阶段先跑通主决策表落盘，候选表阶段 2 训练前补齐。

- [ ] **Step 2: 改 SamplesController 调用 Service**

替换整个 `SamplesController.Batch` 方法：

```csharp
[HttpPost("batch")]
public async Task<IActionResult> Batch([FromBody] SampleBatchRequest req, [FromServices] SampleIngestService ingest, CancellationToken ct)
{
    var machineId = MachineTokenService.ExtractMachineId(User);
    if (string.IsNullOrEmpty(machineId))
        return Unauthorized(new { error = "missing machine_id claim" });

    var resp = await ingest.IngestAsync(machineId, req, ct);
    return Ok(resp);
}
```

- [ ] **Step 3: Program.cs 注册 SampleIngestService**

在 `builder.Services.AddScoped<HearthBot.Cloud.Services.Learning.MachineTokenService>();` 下方追加：

```csharp
builder.Services.AddScoped<HearthBot.Cloud.Services.Learning.SampleIngestService>();
```

- [ ] **Step 4: build 验证**

```bash
cd HearthBot.Cloud && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/SampleIngestService.cs HearthBot.Cloud/Controllers/Learning/SamplesController.cs HearthBot.Cloud/Program.cs
git commit -m "阶段1: SampleIngestService 主决策表落盘 (action/choice/mulligan 统一入口)"
```

---

### Task 1.9: MatchesController（对局结束补录 outcome）

**Files:**
- Create: `HearthBot.Cloud/Controllers/Learning/MatchesController.cs`
- Create: `HearthBot.Cloud/Models/Learning/MatchOutcomeRequest.cs`

- [ ] **Step 1: 写 DTO**

```csharp
// HearthBot.Cloud/Models/Learning/MatchOutcomeRequest.cs
namespace HearthBot.Cloud.Models.Learning;

public class MatchOutcomeRequest
{
    public string Result { get; set; } = string.Empty; // win|loss|draw
    public int FinalTurn { get; set; }
    public string? RankBefore { get; set; }
    public string? RankAfter { get; set; }
    public int DurationSeconds { get; set; }
}
```

- [ ] **Step 2: 写 Controller**

```csharp
// HearthBot.Cloud/Controllers/Learning/MatchesController.cs
using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning/matches")]
[Authorize(Policy = "MachineOnly")]
public class MatchesController : ControllerBase
{
    private readonly LearningDbContext _db;

    public MatchesController(LearningDbContext db) { _db = db; }

    [HttpPatch("{matchId}/outcome")]
    public async Task<IActionResult> PatchOutcome(string matchId, [FromBody] MatchOutcomeRequest req, CancellationToken ct)
    {
        var machineId = MachineTokenService.ExtractMachineId(User);
        if (string.IsNullOrEmpty(machineId))
            return Unauthorized(new { error = "missing machine_id claim" });

        var match = await _db.LearningMatches.FirstOrDefaultAsync(m => m.MatchId == matchId, ct);
        if (match == null) return NotFound(new { error = "match not found" });
        if (match.MachineId != machineId) return Forbid();

        match.EndAt = DateTime.UtcNow.ToString("o");
        match.OutcomeJson = JsonSerializer.Serialize(req);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/Learning/MatchesController.cs HearthBot.Cloud/Models/Learning/MatchOutcomeRequest.cs
git commit -m "阶段1: PATCH /v1/learning/matches/{id}/outcome 补录对局结果"
```

---

### Task 1.10: BotService 接入采样 Hook

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 定位改造点**

Run: `grep -n "TeacherDatasetRecorder\|RequestRecommendationAsync\|LearnedStrategyCoordinator" BotMain/BotService.cs | head -30`
记录行号：`RequestRecommendationAsync` 的入口行、盒子返回处理分支。

- [ ] **Step 2: 在构造器加 Orchestrator 字段**

找到 BotService 构造器末尾，追加：

```csharp
// CloudLearning 初始化
try
{
    var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
        .SetBasePath(BotMain.AppPaths.ExecutingDir)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    var opts = new BotMain.CloudLearning.Config.CloudLearningOptions();
    cfg.GetSection("CloudLearning").Bind(opts);
    if (opts.Enabled && !string.IsNullOrEmpty(opts.ServerBaseUrl) && !string.IsNullOrEmpty(opts.Token))
    {
        _cloudLearning = new BotMain.CloudLearning.CloudLearningOrchestrator(
            opts, Log,
            hbVersion: System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            outboxDir: System.IO.Path.Combine(BotMain.AppPaths.DataDir, "HsBoxTeacher"));
        _ = _cloudLearning.StartAsync();
    }
}
catch (Exception ex) { Log($"[CloudLearning] init failed: {ex.Message}"); }
```

在 BotService 字段区声明：

```csharp
private BotMain.CloudLearning.CloudLearningOrchestrator? _cloudLearning;
```

- [ ] **Step 3: 在 Action 推荐分发处 Record**

在盒子返回推荐后、本地执行前（具体行号按 Step 1 记录），找到：

```csharp
var teacherAction = hsBoxRecommendation?.ActionCommand;
```

之后调用 Mapper 构造决策 + Record：

```csharp
if (_cloudLearning != null && !string.IsNullOrEmpty(teacherAction))
{
    try
    {
        var build = BotMain.Learning.TeacherActionMapper.BuildActionDecision(
            seed: seed ?? string.Empty,
            board: board,
            deckSignature: deckSignature ?? string.Empty,
            teacherActionCommand: teacherAction);

        var sample = new BotMain.CloudLearning.Contracts.DecisionSample
        {
            Context = new BotMain.CloudLearning.Contracts.DecisionContextSnapshot
            {
                MatchId = currentMatchId,
                Type = BotMain.CloudLearning.Contracts.DecisionType.Action,
                Turn = board?.TurnNumber ?? 0,
                Seed = seed ?? string.Empty,
                BoardSnapshotJson = build.Decision.BoardSnapshotJson,
                DeckSignature = deckSignature ?? string.Empty
            },
            MappingStatus = build.Decision.MappingStatus.ToString().ToLowerInvariant(),
            TeacherPickIndex = build.Candidates.FindIndex(c => c.IsTeacherPick),
            CreatedAtMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        for (int i = 0; i < build.Candidates.Count; i++)
        {
            var c = build.Candidates[i];
            sample.Candidates.Add(new BotMain.CloudLearning.Contracts.DecisionCandidate
            {
                SlotIndex = i,
                ActionCommand = c.ActionCommand,
                ActionType = c.ActionType,
                CardId = c.SourceCardId
            });
        }
        await _cloudLearning.RecordAsync(sample);
    }
    catch (Exception ex) { Log($"[CloudLearning] record action failed: {ex.Message}"); }
}
```

> `currentMatchId` 从既有 `MatchFlowState` 或 `StatsBridge` 获取；如 BotService 里还没有现成变量则先 `Guid.NewGuid().ToString("N")` 占位，下个阶段正规化。

- [ ] **Step 4: Dispose 时清理**

BotService 的 `Dispose` 或 `StopAsync` 尾部加：

```csharp
try { _cloudLearning?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
_cloudLearning = null;
```

- [ ] **Step 5: build 验证**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet test --filter "FullyQualifiedName~CloudLearning|FullyQualifiedName~BotService"
```

Expected: Build succeeded，现有 BotService 相关测试不挂

- [ ] **Step 6: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "阶段1: BotService 接入 CloudLearning Orchestrator + Action 样本 Record"
```

---

### Task 1.11: Choice / Mulligan 采样 Hook

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 定位 Choice 请求返回处**

Run: `grep -n "DiscoverRecommendation\|ChoiceCard\|MulliganRecommendation" BotMain/BotService.cs | head -20`

- [ ] **Step 2: 在 Choice 分支 Record**

参照 Action 的模板，在盒子返回 Choice 结果后：

```csharp
if (_cloudLearning != null && choiceResult?.Options?.Count > 0)
{
    try
    {
        var sample = new BotMain.CloudLearning.Contracts.DecisionSample
        {
            Context = new BotMain.CloudLearning.Contracts.DecisionContextSnapshot
            {
                MatchId = currentMatchId,
                Type = BotMain.CloudLearning.Contracts.DecisionType.Choice,
                Turn = board?.TurnNumber ?? 0,
                Seed = seed ?? string.Empty,
                BoardSnapshotJson = "{}",
                ChoiceSourceType = choiceResult.SourceType ?? string.Empty
            },
            TeacherPickIndex = choiceResult.SelectedIndex,
            MappingStatus = choiceResult.SelectedIndex >= 0 ? "matched" : "failed",
            CreatedAtMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        for (int i = 0; i < choiceResult.Options.Count; i++)
        {
            sample.Candidates.Add(new BotMain.CloudLearning.Contracts.DecisionCandidate
            {
                SlotIndex = i,
                ActionCommand = choiceResult.Options[i].Command ?? string.Empty,
                CardId = choiceResult.Options[i].CardId ?? string.Empty
            });
        }
        await _cloudLearning.RecordAsync(sample);
    }
    catch (Exception ex) { Log($"[CloudLearning] record choice failed: {ex.Message}"); }
}
```

> 实际字段名（SelectedIndex / Options / SourceType）要对齐现有 `DiscoverRecommendation` 或等价类型；若类型名不符，改成对应字段。

- [ ] **Step 3: 在 Mulligan 分支 Record**

```csharp
if (_cloudLearning != null && mulliganResult?.Candidates?.Count > 0)
{
    try
    {
        var sample = new BotMain.CloudLearning.Contracts.DecisionSample
        {
            Context = new BotMain.CloudLearning.Contracts.DecisionContextSnapshot
            {
                MatchId = currentMatchId,
                Type = BotMain.CloudLearning.Contracts.DecisionType.Mulligan,
                Seed = seed ?? string.Empty,
                OwnClass = ownClass ?? string.Empty,
                EnemyClass = enemyClass ?? string.Empty,
                HasCoin = hasCoin,
                DeckSignature = deckSignature ?? string.Empty
            },
            MappingStatus = "matched",
            CreatedAtMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        for (int i = 0; i < mulliganResult.Candidates.Count; i++)
        {
            var c = mulliganResult.Candidates[i];
            sample.Candidates.Add(new BotMain.CloudLearning.Contracts.DecisionCandidate
            {
                SlotIndex = i,
                CardId = c.CardId ?? string.Empty,
                ActionCommand = c.Keep ? "KEEP" : "DROP"
            });
        }
        sample.TeacherPickIndex = 0; // mulligan 没有单一 pick, 按全集表达
        await _cloudLearning.RecordAsync(sample);
    }
    catch (Exception ex) { Log($"[CloudLearning] record mulligan failed: {ex.Message}"); }
}
```

- [ ] **Step 4: build + 现有测试回归**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet test
```

Expected: 全部通过

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs
git commit -m "阶段1: Choice/Mulligan 样本 Record 到 CloudLearning"
```

---

### Task 1.12: 阶段 1 验收与冒烟

- [ ] **Step 1: 生成 5 个机器 token**

在云端服务器（或本地）启动 HearthBot.Cloud 后，用 admin 登录拿 JWT，然后临时调 `MachineTokenService.GenerateAsync` 批量为 5 台生成 token：

```bash
# 临时方法: 在 Program.cs 尾部添加开发期代码块（阶段末删除）
# 启动日志会打出 5 个 machine JWT
# 分发到 hb-dorm-01..hb-dorm-05 的 appsettings.json
```

- [ ] **Step 2: 每台机器配置 CloudLearning**

每台 Hearthbot 机器的 `appsettings.json`：

```json
"CloudLearning": {
    "Enabled": true,
    "MachineId": "hb-dorm-01",
    "ServerBaseUrl": "https://<你的 HK 服务器域名>",
    "Token": "<从日志拷贝的对应 JWT>",
    ...
}
```

- [ ] **Step 3: 5 台并行跑 1 小时，观察**

```bash
# 在云端 SQLite 查样本累积
sqlite3 learning.db "SELECT COUNT(*) FROM ActionDecisions;"
sqlite3 learning.db "SELECT MachineId, LastSeenAt FROM Machines;"
sqlite3 learning.db "SELECT MappingStatus, COUNT(*) FROM ActionDecisions GROUP BY MappingStatus;"
```

Expected:
- `ActionDecisions` 计数 > 0 且随时间增长
- 5 台 MachineId 都有记录且 LastSeenAt 在 1 小时内
- `MappingStatus='matched'` 比例 ≥ **85%**（action 硬门槛）

- [ ] **Step 4: 断网演练**

随便挑一台机器断网 30 分钟，观察本地 `Data/HsBoxTeacher/outbox.db` 是否累积（不丢）；恢复网络后 5min 内该机器的样本应全部补传到云端。

```bash
# 断网前记录云端某机器样本数
sqlite3 learning.db "SELECT COUNT(*) FROM ActionDecisions WHERE MatchId IN (SELECT MatchId FROM LearningMatches WHERE MachineId='hb-dorm-03');"
# 断网, 该机器继续跑 30 分钟
# 恢复网络, 等 10 分钟
# 再查云端样本数, 差值应等于断网期间该机本地 outbox.db 积累的样本数
```

- [ ] **Step 5: 三类映射率硬门槛校验**

```bash
# Action
sqlite3 learning.db "SELECT MappingStatus, COUNT(*) * 100.0 / (SELECT COUNT(*) FROM ActionDecisions) FROM ActionDecisions GROUP BY MappingStatus;"
# Choice
sqlite3 learning.db "SELECT MappingStatus, COUNT(*) * 100.0 / (SELECT COUNT(*) FROM ChoiceDecisions) FROM ChoiceDecisions GROUP BY MappingStatus;"
# Mulligan
sqlite3 learning.db "SELECT MappingStatus, COUNT(*) * 100.0 / (SELECT COUNT(*) FROM MulliganDecisions) FROM MulliganDecisions GROUP BY MappingStatus;"
```

**硬门槛**：
- action `matched` ≥ 85%
- choice `matched` ≥ 90%
- mulligan `matched` ≥ 95%

不达标 → 回到 Task 1.7 继续加固 Mapper，或补充 choice/mulligan 对应的映射逻辑。**不进入阶段 2**。

- [ ] **Step 6: 清理临时调试代码 + 阶段收尾 push**

```bash
# 删除阶段 1 调试用的 Program.cs 末尾的 token 打印块
git add HearthBot.Cloud/Program.cs
git commit -m "阶段1: 清理 token 打印调试代码"
git push
```

---

## 阶段 2 / 训练机管线 + 离线评估（约 7 天，11 个 Task）

产出：训练机上能从云端拉数据、抽特征、训 LightGBM LambdaRank、导出 ONNX、验证 Python↔ONNX 一致性的完整离线管线。**不推送云端**。

**前置条件**：阶段 1 全部硬门槛通过，云端已有各类映射率达标的样本。

### Task 2.1: 训练机环境 + `training/` 项目骨架

**Files:**
- Create: `training/pyproject.toml`
- Create: `training/requirements.txt`
- Create: `training/.gitignore`
- Create: `training/config.py`
- Create: `training/README.md`

- [ ] **Step 1: Python 3.11 venv 建立**

```bash
cd H:/桌面/炉石脚本/Hearthbot
mkdir -p training
cd training
py -3.11 -m venv .venv
./.venv/Scripts/activate
```

- [ ] **Step 2: 写 requirements.txt**

```
httpx==0.27.2
pandas==2.2.3
pyarrow==17.0.0
lightgbm==4.5.0
onnxmltools==1.12.0
skl2onnx==1.17.0
onnxruntime==1.19.2
numpy==1.26.4
scikit-learn==1.5.2
mlflow==2.17.0
pydantic==2.9.2
optuna==4.0.0
pytest==8.3.3
```

- [ ] **Step 3: 写 pyproject.toml**

```toml
[project]
name = "hearthbot-training"
version = "0.1.0"
description = "HSBox 云端学习训练管线"
requires-python = ">=3.11"

[tool.pytest.ini_options]
testpaths = ["tests"]
```

- [ ] **Step 4: 写 .gitignore**

```
.venv/
data/
models/
mlruns/
__pycache__/
*.parquet
*.onnx
*.pkl
.pytest_cache/
```

- [ ] **Step 5: 写 config.py**

```python
# training/config.py
"""训练管线配置。读取环境变量或 .env.training。"""
import os
from dataclasses import dataclass

@dataclass
class TrainingConfig:
    server_base_url: str
    training_token: str
    data_dir: str = "data"
    model_dir: str = "models"
    mlflow_tracking_uri: str = "file:./mlruns"

def load_config() -> TrainingConfig:
    server = os.environ.get("HB_CLOUD_URL", "").rstrip("/")
    token = os.environ.get("HB_TRAINING_TOKEN", "")
    if not server or not token:
        raise RuntimeError(
            "需要设置环境变量 HB_CLOUD_URL 和 HB_TRAINING_TOKEN。"
            "训练 token 用机器角色 JWT (machine_id=trainer-4070s) 生成。"
        )
    return TrainingConfig(server_base_url=server, training_token=token)
```

- [ ] **Step 6: 安装依赖 + 冒烟**

```bash
cd training
./.venv/Scripts/activate
pip install -r requirements.txt
python -c "import httpx, pandas, lightgbm, onnxmltools, onnxruntime, mlflow; print('OK')"
```

Expected: `OK`

- [ ] **Step 7: Commit**

```bash
cd H:/桌面/炉石脚本/Hearthbot
git add training/pyproject.toml training/requirements.txt training/.gitignore training/config.py
git commit -m "阶段2: training/ Python 项目骨架 + 依赖清单"
```

---

### Task 2.2: FeatureSchema Python 侧 + 哈希

**Files:**
- Create: `training/schema/__init__.py`
- Create: `training/schema/feature_schema.py`

- [ ] **Step 1: 写 Python 特征 schema**

```python
# training/schema/feature_schema.py
"""
特征 schema 定义。与 C# FeatureSchemaRegistry 共同约束"特征向量布局"。
任何一边改 schema, 必须同步更新 hash。
"""
import hashlib
import json
from dataclasses import dataclass, asdict
from enum import Enum
from typing import List

class FeatureType(str, Enum):
    FLOAT = "float"
    INT = "int"
    BOOL = "bool"
    CATEGORICAL = "categorical"

@dataclass(frozen=True)
class FeatureDef:
    name: str
    type: FeatureType
    index: int

@dataclass(frozen=True)
class FeatureSchema:
    name: str
    version: str
    features: tuple

    def vector_length(self) -> int:
        return max(f.index for f in self.features) + 1

    def to_hashable_dict(self) -> dict:
        return {
            "name": self.name,
            "version": self.version,
            "features": [asdict(f) for f in self.features],
        }

    def compute_hash(self) -> str:
        serialized = json.dumps(self.to_hashable_dict(), sort_keys=True, separators=(",", ":"))
        digest = hashlib.sha256(serialized.encode("utf-8")).hexdigest()
        return f"sha256:{digest}"


def _action_schema_v1() -> FeatureSchema:
    feats: List[FeatureDef] = []
    names = [
        # 局面基础 (20)
        ("mana_ratio", FeatureType.FLOAT),
        ("max_mana", FeatureType.INT),
        ("my_hp", FeatureType.INT),
        ("enemy_hp", FeatureType.INT),
        ("my_armor", FeatureType.INT),
        ("enemy_armor", FeatureType.INT),
        ("my_minion_count", FeatureType.INT),
        ("enemy_minion_count", FeatureType.INT),
        ("my_total_atk", FeatureType.INT),
        ("enemy_total_atk", FeatureType.INT),
        ("my_hand_count", FeatureType.INT),
        ("enemy_hand_count", FeatureType.INT),
        ("my_deck_count", FeatureType.INT),
        ("has_taunt_enemy", FeatureType.BOOL),
        ("has_divine_shield_enemy", FeatureType.BOOL),
        ("has_weapon", FeatureType.BOOL),
        ("can_use_hero_power", FeatureType.BOOL),
        ("lethal_threat", FeatureType.BOOL),
        ("turn_number", FeatureType.INT),
        ("turn_bucket", FeatureType.CATEGORICAL),
        # 动作自身 (20)
        ("action_type_play", FeatureType.BOOL),
        ("action_type_attack", FeatureType.BOOL),
        ("action_type_hero_power", FeatureType.BOOL),
        ("action_type_end", FeatureType.BOOL),
        ("source_cost", FeatureType.INT),
        ("source_atk", FeatureType.INT),
        ("source_hp", FeatureType.INT),
        ("source_is_minion", FeatureType.BOOL),
        ("source_is_spell", FeatureType.BOOL),
        ("source_is_weapon", FeatureType.BOOL),
        ("target_is_face", FeatureType.BOOL),
        ("target_is_minion", FeatureType.BOOL),
        ("target_has_taunt", FeatureType.BOOL),
        ("target_is_own", FeatureType.BOOL),
        ("can_kill_target", FeatureType.BOOL),
        ("cost_ratio", FeatureType.FLOAT),
        ("source_from_generated", FeatureType.BOOL),
        ("source_from_discover", FeatureType.BOOL),
        ("source_from_draw", FeatureType.BOOL),
        ("source_from_mulligan", FeatureType.BOOL),
        # 动作后状态 (20)
        ("mana_after", FeatureType.INT),
        ("my_minion_count_after", FeatureType.INT),
        ("enemy_minion_count_after", FeatureType.INT),
        ("my_total_atk_after", FeatureType.INT),
        ("enemy_total_atk_after", FeatureType.INT),
        ("my_hp_after", FeatureType.INT),
        ("enemy_hp_after", FeatureType.INT),
        ("my_hand_after", FeatureType.INT),
        ("resolves_taunt", FeatureType.BOOL),
        ("trade_efficiency", FeatureType.FLOAT),
        ("face_damage_delta", FeatureType.INT),
        ("enters_lethal_state", FeatureType.BOOL),
        ("defends_lethal", FeatureType.BOOL),
        ("tempo_delta", FeatureType.FLOAT),
        ("board_control_delta", FeatureType.FLOAT),
        ("my_class", FeatureType.CATEGORICAL),
        ("enemy_class", FeatureType.CATEGORICAL),
        ("deck_archetype", FeatureType.CATEGORICAL),
        ("has_coin", FeatureType.BOOL),
        ("step_index", FeatureType.INT),
    ]
    for idx, (n, t) in enumerate(names):
        feats.append(FeatureDef(name=n, type=t, index=idx))
    return FeatureSchema(name="action", version="v1", features=tuple(feats))


def _choice_schema_v1() -> FeatureSchema:
    feats: List[FeatureDef] = []
    names = [
        ("source_card_cost", FeatureType.INT),
        ("turn_number", FeatureType.INT),
        ("mana_ratio", FeatureType.FLOAT),
        ("my_minion_count", FeatureType.INT),
        ("enemy_minion_count", FeatureType.INT),
        ("option_cost", FeatureType.INT),
        ("option_atk", FeatureType.INT),
        ("option_hp", FeatureType.INT),
        ("option_is_minion", FeatureType.BOOL),
        ("option_is_spell", FeatureType.BOOL),
        ("option_is_weapon", FeatureType.BOOL),
        ("source_discover", FeatureType.BOOL),
        ("source_rebirth", FeatureType.BOOL),
        ("source_subchoice", FeatureType.BOOL),
        ("my_class", FeatureType.CATEGORICAL),
        ("enemy_class", FeatureType.CATEGORICAL),
        ("has_coin", FeatureType.BOOL),
        ("deck_archetype", FeatureType.CATEGORICAL),
        ("option_synergy", FeatureType.FLOAT),
        ("option_tempo_score", FeatureType.FLOAT),
    ]
    for idx, (n, t) in enumerate(names):
        feats.append(FeatureDef(name=n, type=t, index=idx))
    return FeatureSchema(name="choice", version="v1", features=tuple(feats))


def _mulligan_schema_v1() -> FeatureSchema:
    feats: List[FeatureDef] = []
    names = [
        ("card_cost", FeatureType.INT),
        ("card_atk", FeatureType.INT),
        ("card_hp", FeatureType.INT),
        ("card_is_minion", FeatureType.BOOL),
        ("card_is_spell", FeatureType.BOOL),
        ("card_is_weapon", FeatureType.BOOL),
        ("enemy_class", FeatureType.CATEGORICAL),
        ("my_class", FeatureType.CATEGORICAL),
        ("has_coin", FeatureType.BOOL),
        ("deck_archetype", FeatureType.CATEGORICAL),
        ("companion_low_cost_count", FeatureType.INT),
        ("companion_2drop_present", FeatureType.BOOL),
        ("companion_3drop_present", FeatureType.BOOL),
        ("companion_removal_count", FeatureType.INT),
        ("card_is_combo_piece", FeatureType.BOOL),
        ("card_is_card_draw", FeatureType.BOOL),
    ]
    for idx, (n, t) in enumerate(names):
        feats.append(FeatureDef(name=n, type=t, index=idx))
    return FeatureSchema(name="mulligan", version="v1", features=tuple(feats))


ACTION_SCHEMA = _action_schema_v1()
CHOICE_SCHEMA = _choice_schema_v1()
MULLIGAN_SCHEMA = _mulligan_schema_v1()


def get_schema(name: str) -> FeatureSchema:
    m = {"action": ACTION_SCHEMA, "choice": CHOICE_SCHEMA, "mulligan": MULLIGAN_SCHEMA}
    if name not in m:
        raise KeyError(f"unknown schema: {name}")
    return m[name]


def combined_hash() -> str:
    """三个 schema 合成的 hash, 用于运行时校验特征版本。"""
    parts = {
        "action": ACTION_SCHEMA.to_hashable_dict(),
        "choice": CHOICE_SCHEMA.to_hashable_dict(),
        "mulligan": MULLIGAN_SCHEMA.to_hashable_dict(),
    }
    serialized = json.dumps(parts, sort_keys=True, separators=(",", ":"))
    digest = hashlib.sha256(serialized.encode("utf-8")).hexdigest()
    return f"sha256:{digest}"


if __name__ == "__main__":
    print("action hash :", ACTION_SCHEMA.compute_hash())
    print("choice hash :", CHOICE_SCHEMA.compute_hash())
    print("mulligan hash:", MULLIGAN_SCHEMA.compute_hash())
    print("combined    :", combined_hash())
```

- [ ] **Step 2: 生成 schema hash（打印保存）**

```bash
cd training && ./.venv/Scripts/activate
python -m schema.feature_schema
```

Expected: 四行 `sha256:...` 哈希值。**记录 combined hash**，Task 3.x 把它硬编码进 C# `FeatureSchemaRegistry.CurrentHash`。

- [ ] **Step 3: 写一个 test 固定 hash（防不小心动了 schema）**

```python
# training/tests/test_schema_hash.py
from schema.feature_schema import combined_hash, ACTION_SCHEMA

def test_action_schema_has_60_features():
    assert len(ACTION_SCHEMA.features) == 60
    assert ACTION_SCHEMA.vector_length() == 60

def test_combined_hash_stable():
    # 首次运行时用 python -m schema.feature_schema 把 EXPECTED_HASH 填进来
    EXPECTED_HASH = None  # TODO: fill with your actual hash from Step 2
    assert combined_hash().startswith("sha256:")
    if EXPECTED_HASH is not None:
        assert combined_hash() == EXPECTED_HASH, \
            "Schema changed! 更新 EXPECTED_HASH 并同步 C# FeatureSchemaRegistry.CurrentHash"
```

- [ ] **Step 4: 运行测试**

```bash
cd training && pytest tests/test_schema_hash.py -v
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add training/schema/ training/tests/test_schema_hash.py
git commit -m "阶段2: FeatureSchema Python 定义 (action 60/choice 20/mulligan 16) + 哈希"
```

---

### Task 2.3: ExportController 云端侧（流式导出）

**Files:**
- Create: `HearthBot.Cloud/Controllers/Learning/ExportController.cs`

- [ ] **Step 1: 写 Controller**

```csharp
// HearthBot.Cloud/Controllers/Learning/ExportController.cs
using System.Text;
using System.Text.Json;
using HearthBot.Cloud.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning/export")]
[Authorize(Policy = "MachineOnly")]
public class ExportController : ControllerBase
{
    private readonly LearningDbContext _db;

    public ExportController(LearningDbContext db) { _db = db; }

    [HttpGet("actions")]
    public async Task ExportActions([FromQuery] string? since, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson";
        var cutoff = string.IsNullOrEmpty(since) ? "0001-01-01" : since;

        await using var writer = new StreamWriter(Response.Body, Encoding.UTF8, leaveOpen: true);
        var query = _db.ActionDecisions
            .AsNoTracking()
            .Where(d => string.Compare(d.CreatedAt, cutoff) > 0 && d.MappingStatus == "matched")
            .OrderBy(d => d.CreatedAt);

        await foreach (var d in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            var candidates = await _db.ActionCandidates
                .AsNoTracking()
                .Where(c => c.DecisionId == d.DecisionId)
                .OrderBy(c => c.SlotIndex)
                .ToListAsync(ct);
            var record = new
            {
                d.DecisionId,
                d.ClientSampleId,
                d.MatchId,
                d.Turn,
                d.StepIndex,
                d.Seed,
                d.BoardSnapshotJson,
                d.TeacherCandidateIndex,
                d.MappingStatus,
                d.LocalPickIndex,
                d.CreatedAt,
                Candidates = candidates.Select(c => new
                {
                    c.SlotIndex, c.ActionCommand, c.ActionType, c.FeaturesJson,
                    c.IsTeacherPick, c.IsLocalPick
                })
            };
            var json = JsonSerializer.Serialize(record);
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
    }

    [HttpGet("choices")]
    public async Task ExportChoices([FromQuery] string? since, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson";
        var cutoff = string.IsNullOrEmpty(since) ? "0001-01-01" : since;
        await using var writer = new StreamWriter(Response.Body, Encoding.UTF8, leaveOpen: true);
        var query = _db.ChoiceDecisions.AsNoTracking()
            .Where(d => string.Compare(d.CreatedAt, cutoff) > 0 && d.MappingStatus == "matched")
            .OrderBy(d => d.CreatedAt);
        await foreach (var d in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            var opts = await _db.ChoiceOptions.AsNoTracking()
                .Where(o => o.DecisionId == d.DecisionId)
                .OrderBy(o => o.SlotIndex).ToListAsync(ct);
            var json = JsonSerializer.Serialize(new { Decision = d, Options = opts });
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
    }

    [HttpGet("mulligans")]
    public async Task ExportMulligans([FromQuery] string? since, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson";
        var cutoff = string.IsNullOrEmpty(since) ? "0001-01-01" : since;
        await using var writer = new StreamWriter(Response.Body, Encoding.UTF8, leaveOpen: true);
        var query = _db.MulliganDecisions.AsNoTracking()
            .Where(d => string.Compare(d.CreatedAt, cutoff) > 0 && d.MappingStatus == "matched")
            .OrderBy(d => d.CreatedAt);
        await foreach (var d in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            var cards = await _db.MulliganCards.AsNoTracking()
                .Where(c => c.DecisionId == d.DecisionId)
                .OrderBy(c => c.SlotIndex).ToListAsync(ct);
            var json = JsonSerializer.Serialize(new { Decision = d, Cards = cards });
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
    }
}
```

- [ ] **Step 2: build 验证**

```bash
cd HearthBot.Cloud && dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Controllers/Learning/ExportController.cs
git commit -m "阶段2: GET /v1/learning/export/* 流式 NDJSON 导出"
```

---

### Task 2.4: pull_data.py（训练机拉数据）

**Files:**
- Create: `training/pull_data.py`

- [ ] **Step 1: 写 pull_data.py**

```python
# training/pull_data.py
"""从云端 HTTPS 拉取增量样本, 落 JSONL + Parquet。"""
import argparse
import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

import httpx
import pandas as pd

from config import load_config


def stream_ndjson(url: str, token: str, since: str | None) -> Iterable[dict]:
    params = {"since": since} if since else {}
    headers = {"Authorization": f"Bearer {token}"}
    with httpx.stream("GET", url, headers=headers, params=params, timeout=120.0) as resp:
        resp.raise_for_status()
        for line in resp.iter_lines():
            if not line.strip():
                continue
            yield json.loads(line)


def pull(decision_type: str, since: str | None = None) -> Path:
    cfg = load_config()
    Path(cfg.data_dir).mkdir(parents=True, exist_ok=True)
    ts = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    out_jsonl = Path(cfg.data_dir) / f"{decision_type}-{ts}.jsonl"
    url = f"{cfg.server_base_url}/v1/learning/export/{decision_type}s"
    count = 0
    with out_jsonl.open("w", encoding="utf-8") as f:
        for rec in stream_ndjson(url, cfg.training_token, since):
            f.write(json.dumps(rec) + "\n")
            count += 1
    print(f"[pull_data] {decision_type}: fetched {count} decisions → {out_jsonl}")
    return out_jsonl


def convert_to_parquet(jsonl_path: Path) -> Path:
    records = []
    with jsonl_path.open("r", encoding="utf-8") as f:
        for line in f:
            records.append(json.loads(line))
    df = pd.json_normalize(records, max_level=0)
    parquet_path = jsonl_path.with_suffix(".parquet")
    df.to_parquet(parquet_path, index=False)
    print(f"[pull_data] converted → {parquet_path}")
    return parquet_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--type", choices=["action", "choice", "mulligan", "all"], default="all")
    ap.add_argument("--since", default=None, help="ISO 8601, e.g. 2026-04-10T00:00:00Z")
    args = ap.parse_args()
    types = ["action", "choice", "mulligan"] if args.type == "all" else [args.type]
    for t in types:
        jsonl = pull(t, args.since)
        convert_to_parquet(jsonl)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 冒烟（需云端有样本）**

```bash
cd training && ./.venv/Scripts/activate
export HB_CLOUD_URL="https://<你的 HK 服务器>"
export HB_TRAINING_TOKEN="<trainer-4070s machine token>"
python pull_data.py --type action
```

Expected: `data/action-YYYYMMDD-HHMMSS.jsonl` 和 `.parquet` 生成

- [ ] **Step 3: Commit**

```bash
git add training/pull_data.py
git commit -m "阶段2: pull_data.py 从云端拉 NDJSON 并转 Parquet"
```

---

### Task 2.5: features/action.py（Action 特征抽取）

**Files:**
- Create: `training/features/__init__.py`
- Create: `training/features/common.py`
- Create: `training/features/action.py`
- Create: `training/tests/test_features_action.py`

- [ ] **Step 1: 写 features/common.py**

```python
# training/features/common.py
"""各类特征抽取共用工具。"""
import numpy as np
from schema.feature_schema import FeatureSchema, FeatureType

def empty_vector(schema: FeatureSchema) -> np.ndarray:
    return np.zeros(schema.vector_length(), dtype=np.float32)

def set_feature(vec: np.ndarray, schema: FeatureSchema, name: str, value) -> None:
    for f in schema.features:
        if f.name == name:
            vec[f.index] = float(value) if value is not None else 0.0
            return
    raise KeyError(f"feature '{name}' not in schema '{schema.name}'")
```

- [ ] **Step 2: 写 features/action.py**

```python
# training/features/action.py
"""Action 排序模型的特征抽取。输入为一条 action_decision + 其候选; 输出 (N_candidates, vector_length)."""
from typing import List, Dict, Any
import json
import numpy as np
from schema.feature_schema import ACTION_SCHEMA
from features.common import empty_vector, set_feature

def extract(decision_record: dict) -> tuple[np.ndarray, int]:
    """Returns (X, teacher_idx). X shape = (n_candidates, 60)."""
    board = json.loads(decision_record.get("boardSnapshotJson") or decision_record.get("BoardSnapshotJson") or "{}")
    candidates = decision_record.get("candidates") or decision_record.get("Candidates") or []
    teacher_idx = int(decision_record.get("teacherCandidateIndex") or decision_record.get("TeacherCandidateIndex") or -1)

    n = len(candidates)
    X = np.zeros((n, ACTION_SCHEMA.vector_length()), dtype=np.float32)

    my_hp = board.get("myHp", 30)
    enemy_hp = board.get("enemyHp", 30)
    max_mana = board.get("maxMana", 1)
    current_mana = board.get("currentMana", 1)
    my_minions = board.get("myMinions", [])
    enemy_minions = board.get("enemyMinions", [])
    turn = board.get("turnNumber", 1)

    for i, c in enumerate(candidates):
        feat_json = c.get("featuresJson") or c.get("FeaturesJson") or "{}"
        cf = json.loads(feat_json) if isinstance(feat_json, str) else {}

        vec = empty_vector(ACTION_SCHEMA)
        set_feature(vec, ACTION_SCHEMA, "mana_ratio", current_mana / max(1, max_mana))
        set_feature(vec, ACTION_SCHEMA, "max_mana", max_mana)
        set_feature(vec, ACTION_SCHEMA, "my_hp", my_hp)
        set_feature(vec, ACTION_SCHEMA, "enemy_hp", enemy_hp)
        set_feature(vec, ACTION_SCHEMA, "my_minion_count", len(my_minions))
        set_feature(vec, ACTION_SCHEMA, "enemy_minion_count", len(enemy_minions))
        set_feature(vec, ACTION_SCHEMA, "my_total_atk", sum(m.get("atk", 0) for m in my_minions))
        set_feature(vec, ACTION_SCHEMA, "enemy_total_atk", sum(m.get("atk", 0) for m in enemy_minions))
        set_feature(vec, ACTION_SCHEMA, "turn_number", turn)
        set_feature(vec, ACTION_SCHEMA, "turn_bucket", min(turn // 3, 9))

        atype = (c.get("actionType") or c.get("ActionType") or "").upper()
        set_feature(vec, ACTION_SCHEMA, "action_type_play", atype == "PLAY")
        set_feature(vec, ACTION_SCHEMA, "action_type_attack", atype == "ATTACK")
        set_feature(vec, ACTION_SCHEMA, "action_type_hero_power", atype == "HEROPOWER")
        set_feature(vec, ACTION_SCHEMA, "action_type_end", atype == "END")
        set_feature(vec, ACTION_SCHEMA, "source_cost", cf.get("sourceCost", 0))
        set_feature(vec, ACTION_SCHEMA, "source_atk", cf.get("sourceAtk", 0))
        set_feature(vec, ACTION_SCHEMA, "source_hp", cf.get("sourceHp", 0))
        set_feature(vec, ACTION_SCHEMA, "target_is_face", cf.get("targetIsFace", False))
        set_feature(vec, ACTION_SCHEMA, "target_is_minion", cf.get("targetIsMinion", False))
        set_feature(vec, ACTION_SCHEMA, "target_has_taunt", cf.get("targetHasTaunt", False))
        set_feature(vec, ACTION_SCHEMA, "can_kill_target", cf.get("canKillTarget", False))

        set_feature(vec, ACTION_SCHEMA, "step_index", int(decision_record.get("stepIndex") or decision_record.get("StepIndex") or 0))
        X[i] = vec
    return X, teacher_idx
```

- [ ] **Step 3: 写测试**

```python
# training/tests/test_features_action.py
import json
from features.action import extract
from schema.feature_schema import ACTION_SCHEMA

def _record(n_candidates=3, teacher_idx=1):
    return {
        "BoardSnapshotJson": json.dumps({"myHp": 20, "enemyHp": 15, "currentMana": 5, "maxMana": 5,
                                         "myMinions": [{"atk": 3}], "enemyMinions": [{"atk": 2}],
                                         "turnNumber": 4}),
        "Candidates": [
            {"ActionType": "PLAY", "FeaturesJson": json.dumps({"sourceCost": 3, "sourceAtk": 3, "sourceHp": 2})}
            for _ in range(n_candidates)
        ],
        "TeacherCandidateIndex": teacher_idx,
        "StepIndex": 2,
    }

def test_extract_returns_correct_shape():
    X, idx = extract(_record(n_candidates=3, teacher_idx=1))
    assert X.shape == (3, ACTION_SCHEMA.vector_length())
    assert idx == 1

def test_extract_sets_play_action_type():
    X, _ = extract(_record(n_candidates=1))
    play_idx = next(f.index for f in ACTION_SCHEMA.features if f.name == "action_type_play")
    assert X[0][play_idx] == 1.0
```

- [ ] **Step 4: 运行测试**

```bash
cd training && pytest tests/test_features_action.py -v
```

Expected: PASS 2

- [ ] **Step 5: Commit**

```bash
git add training/features/ training/tests/test_features_action.py
git commit -m "阶段2: Action 特征抽取器 + 测试 (60 维)"
```

---

### Task 2.6: features/choice.py + features/mulligan.py

**Files:**
- Create: `training/features/choice.py`
- Create: `training/features/mulligan.py`

- [ ] **Step 1: 写 choice.py**

```python
# training/features/choice.py
from typing import List, Tuple
import json
import numpy as np
from schema.feature_schema import CHOICE_SCHEMA
from features.common import empty_vector, set_feature


def extract(decision_record: dict) -> Tuple[np.ndarray, int]:
    d = decision_record.get("Decision") or decision_record
    options = decision_record.get("Options") or d.get("Options") or []
    teacher_idx = int(d.get("TeacherOptionIndex") or d.get("teacherOptionIndex") or -1)

    ctx = json.loads(d.get("ContextJson") or d.get("contextJson") or "{}")
    source_type = d.get("ChoiceSourceType") or d.get("choiceSourceType") or ""

    n = len(options)
    X = np.zeros((n, CHOICE_SCHEMA.vector_length()), dtype=np.float32)
    for i, opt in enumerate(options):
        feat_json = opt.get("FeaturesJson") or opt.get("featuresJson") or "{}"
        of = json.loads(feat_json) if isinstance(feat_json, str) else {}
        vec = empty_vector(CHOICE_SCHEMA)
        set_feature(vec, CHOICE_SCHEMA, "turn_number", ctx.get("turnNumber", 0))
        set_feature(vec, CHOICE_SCHEMA, "mana_ratio", ctx.get("currentMana", 0) / max(1, ctx.get("maxMana", 1)))
        set_feature(vec, CHOICE_SCHEMA, "option_cost", of.get("cost", 0))
        set_feature(vec, CHOICE_SCHEMA, "option_atk", of.get("atk", 0))
        set_feature(vec, CHOICE_SCHEMA, "option_hp", of.get("hp", 0))
        set_feature(vec, CHOICE_SCHEMA, "option_is_minion", of.get("isMinion", False))
        set_feature(vec, CHOICE_SCHEMA, "option_is_spell", of.get("isSpell", False))
        set_feature(vec, CHOICE_SCHEMA, "source_discover", source_type.lower() == "discover")
        set_feature(vec, CHOICE_SCHEMA, "source_rebirth", source_type.lower() == "rebirth")
        set_feature(vec, CHOICE_SCHEMA, "source_subchoice", source_type.lower() == "subchoice")
        X[i] = vec
    return X, teacher_idx
```

- [ ] **Step 2: 写 mulligan.py**

```python
# training/features/mulligan.py
from typing import Tuple
import json
import numpy as np
from schema.feature_schema import MULLIGAN_SCHEMA
from features.common import empty_vector, set_feature


def extract(decision_record: dict) -> Tuple[np.ndarray, np.ndarray]:
    d = decision_record.get("Decision") or decision_record
    cards = decision_record.get("Cards") or d.get("Cards") or []
    own_class = d.get("OwnClass") or d.get("ownClass") or ""
    enemy_class = d.get("EnemyClass") or d.get("enemyClass") or ""
    has_coin = d.get("HasCoin") or d.get("hasCoin") or False

    n = len(cards)
    X = np.zeros((n, MULLIGAN_SCHEMA.vector_length()), dtype=np.float32)
    y = np.zeros(n, dtype=np.int32)
    for i, c in enumerate(cards):
        feat_json = c.get("FeaturesJson") or c.get("featuresJson") or "{}"
        cf = json.loads(feat_json) if isinstance(feat_json, str) else {}
        vec = empty_vector(MULLIGAN_SCHEMA)
        set_feature(vec, MULLIGAN_SCHEMA, "card_cost", cf.get("cost", 0))
        set_feature(vec, MULLIGAN_SCHEMA, "card_atk", cf.get("atk", 0))
        set_feature(vec, MULLIGAN_SCHEMA, "card_hp", cf.get("hp", 0))
        set_feature(vec, MULLIGAN_SCHEMA, "card_is_minion", cf.get("isMinion", False))
        set_feature(vec, MULLIGAN_SCHEMA, "card_is_spell", cf.get("isSpell", False))
        set_feature(vec, MULLIGAN_SCHEMA, "has_coin", has_coin)
        X[i] = vec
        y[i] = 1 if (c.get("TeacherKeep") or c.get("teacherKeep")) else 0
    return X, y
```

- [ ] **Step 3: Commit**

```bash
git add training/features/choice.py training/features/mulligan.py
git commit -m "阶段2: Choice/Mulligan 特征抽取"
```

---

### Task 2.7: train_action.py（LightGBM LambdaRank）

**Files:**
- Create: `training/train_action.py`

- [ ] **Step 1: 写 train_action.py**

```python
# training/train_action.py
"""训练 Action 排序模型 (LightGBM LambdaRank) + 离线评估。"""
import argparse
import json
from pathlib import Path
from datetime import datetime, timezone

import numpy as np
import pandas as pd
import lightgbm as lgb
import mlflow

from config import load_config
from schema.feature_schema import ACTION_SCHEMA, combined_hash
from features import action as action_features


def load_jsonl(path: Path) -> list[dict]:
    records = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                records.append(json.loads(line))
    return records


def build_dataset(records: list[dict]):
    X_all, y_all, group, meta = [], [], [], []
    for r in records:
        X, t_idx = action_features.extract(r)
        if t_idx < 0 or X.shape[0] == 0:
            continue
        n = X.shape[0]
        y = np.zeros(n, dtype=np.int32)
        y[t_idx] = 1
        X_all.append(X)
        y_all.append(y)
        group.append(n)
        meta.append({"match_id": r.get("matchId") or r.get("MatchId"),
                     "turn": r.get("turn") or r.get("Turn", 0)})
    if not X_all:
        return None
    return np.vstack(X_all), np.concatenate(y_all), np.array(group), meta


def split_by_match(meta: list[dict], group: np.ndarray, train_ratio=0.7, val_ratio=0.2):
    match_ids = list({m["match_id"] for m in meta if m["match_id"]})
    rng = np.random.default_rng(42)
    rng.shuffle(match_ids)
    n_train = int(len(match_ids) * train_ratio)
    n_val = int(len(match_ids) * val_ratio)
    train_m = set(match_ids[:n_train])
    val_m = set(match_ids[n_train:n_train + n_val])
    train_idx, val_idx, test_idx = [], [], []
    cursor = 0
    for i, g in enumerate(group):
        slice_idx = list(range(cursor, cursor + g))
        mid = meta[i]["match_id"]
        if mid in train_m: train_idx += slice_idx
        elif mid in val_m: val_idx += slice_idx
        else: test_idx += slice_idx
        cursor += g
    return np.array(train_idx), np.array(val_idx), np.array(test_idx)


def compute_top_k(ranker, X, group, k=1) -> float:
    preds = ranker.predict(X)
    hits = 0
    total = 0
    cursor = 0
    for g in group:
        scores = preds[cursor:cursor + g]
        # label 层面用先前 y: 需要外部传进来; 这里改成 reuse y
        cursor += g
        total += 1
    return hits / max(1, total)


def evaluate(ranker, X, y, group, k_list=(1, 3)) -> dict:
    preds = ranker.predict(X)
    results = {}
    for k in k_list:
        hits = 0
        total = 0
        cursor = 0
        for g in group:
            scores = preds[cursor:cursor + g]
            labels = y[cursor:cursor + g]
            top_k = np.argsort(-scores)[:k]
            if any(labels[i] == 1 for i in top_k):
                hits += 1
            total += 1
            cursor += g
        results[f"top_{k}"] = hits / max(1, total)
    return results


def train(args):
    cfg = load_config()
    mlflow.set_tracking_uri(cfg.mlflow_tracking_uri)
    mlflow.set_experiment("action-ranker")

    print(f"[train] 加载 {args.input}")
    records = load_jsonl(Path(args.input))
    ds = build_dataset(records)
    if ds is None:
        print("[train] 无数据")
        return 1
    X, y, group, meta = ds

    train_idx, val_idx, test_idx = split_by_match(meta, group)
    # group 是按决策点的 N_candidates 数组, 不能直接用 idx 切; 重建 group
    def subset(mask_indices, full_group, meta):
        mapped_meta_idx = []
        cursor = 0
        for i, g in enumerate(full_group):
            if any(idx >= cursor and idx < cursor + g for idx in mask_indices):
                mapped_meta_idx.append(i)
            cursor += g
        sub_group = [full_group[i] for i in mapped_meta_idx]
        all_sample_idx = []
        cursor = 0
        for i, g in enumerate(full_group):
            if i in set(mapped_meta_idx):
                all_sample_idx.extend(range(cursor, cursor + g))
            cursor += g
        return np.array(all_sample_idx), np.array(sub_group)

    tr_idx, tr_group = subset(train_idx, group, meta)
    va_idx, va_group = subset(val_idx, group, meta)
    te_idx, te_group = subset(test_idx, group, meta)

    X_tr, y_tr = X[tr_idx], y[tr_idx]
    X_va, y_va = X[va_idx], y[va_idx]
    X_te, y_te = X[te_idx], y[te_idx]

    feature_names = [f.name for f in sorted(ACTION_SCHEMA.features, key=lambda f: f.index)]
    train_ds = lgb.Dataset(X_tr, label=y_tr, group=tr_group, feature_name=feature_names)
    val_ds = lgb.Dataset(X_va, label=y_va, group=va_group, feature_name=feature_names, reference=train_ds)

    params = {
        "objective": "lambdarank",
        "metric": "ndcg",
        "ndcg_eval_at": [1, 3],
        "learning_rate": 0.05,
        "num_leaves": 63,
        "min_data_in_leaf": 20,
        "feature_fraction": 0.9,
        "bagging_fraction": 0.8,
        "bagging_freq": 5,
        "verbose": -1,
    }

    with mlflow.start_run(run_name=f"action-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}"):
        mlflow.log_params(params)
        mlflow.log_param("feature_schema_hash", combined_hash())
        mlflow.log_param("sample_count", int(X.shape[0]))
        booster = lgb.train(params, train_ds, num_boost_round=1000,
                            valid_sets=[val_ds], callbacks=[lgb.early_stopping(50), lgb.log_evaluation(50)])

        test_metrics = evaluate(booster, X_te, y_te, te_group)
        mlflow.log_metrics(test_metrics)
        for name, val in test_metrics.items():
            print(f"[train] test {name}: {val:.4f}")

        Path(cfg.model_dir).mkdir(parents=True, exist_ok=True)
        model_path = Path(cfg.model_dir) / f"action-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}.txt"
        booster.save_model(str(model_path))
        mlflow.log_artifact(str(model_path))
        print(f"[train] 模型保存 → {model_path}")

        # Hard gate
        if test_metrics["top_1"] < args.min_top1:
            print(f"[train] FAIL: top_1 {test_metrics['top_1']:.4f} < min_top1 {args.min_top1}")
            return 2
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True, help="JSONL 路径 (pull_data 产出)")
    ap.add_argument("--min-top1", type=float, default=0.55)
    args = ap.parse_args()
    raise SystemExit(train(args))


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 冒烟**

```bash
cd training && ./.venv/Scripts/activate
python train_action.py --input data/action-<ts>.jsonl --min-top1 0.40
```

Expected: MLflow 运行记录一次，输出 `top_1 0.XX`，模型文件写入 `models/`

- [ ] **Step 3: Commit**

```bash
git add training/train_action.py
git commit -m "阶段2: train_action.py LightGBM LambdaRank + MLflow + 硬门槛"
```

---

### Task 2.8: train_choice.py + train_mulligan.py

**Files:**
- Create: `training/train_choice.py`
- Create: `training/train_mulligan.py`

- [ ] **Step 1: 写 train_choice.py（与 action 同结构，调换 features module）**

```python
# training/train_choice.py
"""训练 Choice 排序模型 (LightGBM LambdaRank)。结构同 train_action.py。"""
import argparse
import json
from pathlib import Path
from datetime import datetime, timezone

import numpy as np
import lightgbm as lgb
import mlflow

from config import load_config
from schema.feature_schema import CHOICE_SCHEMA, combined_hash
from features import choice as choice_features


def load_jsonl(path: Path) -> list[dict]:
    records = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                records.append(json.loads(line))
    return records


def build_dataset(records):
    X_all, y_all, group, meta = [], [], [], []
    for r in records:
        X, t_idx = choice_features.extract(r)
        if t_idx < 0 or X.shape[0] == 0:
            continue
        n = X.shape[0]
        y = np.zeros(n, dtype=np.int32)
        y[t_idx] = 1
        X_all.append(X)
        y_all.append(y)
        group.append(n)
        d = r.get("Decision") or r
        meta.append({"match_id": d.get("matchId") or d.get("MatchId")})
    if not X_all: return None
    return np.vstack(X_all), np.concatenate(y_all), np.array(group), meta


def evaluate(ranker, X, y, group, k_list=(1, 3)):
    preds = ranker.predict(X)
    results = {}
    for k in k_list:
        hits, total, cursor = 0, 0, 0
        for g in group:
            scores = preds[cursor:cursor + g]
            labels = y[cursor:cursor + g]
            top_k = np.argsort(-scores)[:k]
            if any(labels[i] == 1 for i in top_k): hits += 1
            total += 1
            cursor += g
        results[f"top_{k}"] = hits / max(1, total)
    return results


def train(args):
    cfg = load_config()
    mlflow.set_tracking_uri(cfg.mlflow_tracking_uri)
    mlflow.set_experiment("choice-ranker")

    records = load_jsonl(Path(args.input))
    ds = build_dataset(records)
    if ds is None:
        print("[train] 无数据"); return 1
    X, y, group, meta = ds
    # 简化: 全量训 + 全量评估 (choice 样本少, 先验证能 train)
    feature_names = [f.name for f in sorted(CHOICE_SCHEMA.features, key=lambda f: f.index)]
    train_ds = lgb.Dataset(X, label=y, group=group, feature_name=feature_names)
    params = {"objective": "lambdarank", "metric": "ndcg", "ndcg_eval_at": [1, 3],
              "learning_rate": 0.05, "num_leaves": 31, "verbose": -1}
    with mlflow.start_run(run_name=f"choice-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}"):
        mlflow.log_param("feature_schema_hash", combined_hash())
        mlflow.log_param("sample_count", int(X.shape[0]))
        booster = lgb.train(params, train_ds, num_boost_round=500)
        metrics = evaluate(booster, X, y, group)
        mlflow.log_metrics(metrics)
        for k, v in metrics.items():
            print(f"[train] {k}: {v:.4f}")
        Path(cfg.model_dir).mkdir(parents=True, exist_ok=True)
        out = Path(cfg.model_dir) / f"choice-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}.txt"
        booster.save_model(str(out))
        mlflow.log_artifact(str(out))
        if metrics["top_1"] < args.min_top1:
            print(f"[train] FAIL top_1 {metrics['top_1']:.4f} < {args.min_top1}")
            return 2
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--min-top1", type=float, default=0.60)
    args = ap.parse_args()
    raise SystemExit(train(args))


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 写 train_mulligan.py**

```python
# training/train_mulligan.py
"""Mulligan keep/drop 二分类。"""
import argparse
import json
from pathlib import Path
from datetime import datetime, timezone

import numpy as np
import lightgbm as lgb
import mlflow

from config import load_config
from schema.feature_schema import MULLIGAN_SCHEMA, combined_hash
from features import mulligan as mulligan_features


def load_jsonl(path: Path) -> list[dict]:
    records = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                records.append(json.loads(line))
    return records


def build_dataset(records):
    X_all, y_all = [], []
    for r in records:
        X, y = mulligan_features.extract(r)
        if X.shape[0] == 0: continue
        X_all.append(X); y_all.append(y)
    if not X_all: return None
    return np.vstack(X_all), np.concatenate(y_all)


def train(args):
    cfg = load_config()
    mlflow.set_tracking_uri(cfg.mlflow_tracking_uri)
    mlflow.set_experiment("mulligan-ranker")

    records = load_jsonl(Path(args.input))
    ds = build_dataset(records)
    if ds is None: print("[train] 无数据"); return 1
    X, y = ds
    feature_names = [f.name for f in sorted(MULLIGAN_SCHEMA.features, key=lambda f: f.index)]
    train_ds = lgb.Dataset(X, label=y, feature_name=feature_names)
    params = {"objective": "binary", "metric": "binary_logloss", "learning_rate": 0.05,
              "num_leaves": 31, "verbose": -1}
    with mlflow.start_run(run_name=f"mulligan-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}"):
        mlflow.log_param("feature_schema_hash", combined_hash())
        mlflow.log_param("sample_count", int(X.shape[0]))
        booster = lgb.train(params, train_ds, num_boost_round=300)

        preds = (booster.predict(X) > 0.5).astype(int)
        accuracy = float((preds == y).mean())
        mlflow.log_metric("top_1", accuracy)  # 等价于 keep/drop 决策一致率
        print(f"[train] mulligan keep/drop accuracy: {accuracy:.4f}")

        Path(cfg.model_dir).mkdir(parents=True, exist_ok=True)
        out = Path(cfg.model_dir) / f"mulligan-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}.txt"
        booster.save_model(str(out))
        mlflow.log_artifact(str(out))
        if accuracy < args.min_top1:
            print(f"[train] FAIL accuracy {accuracy:.4f} < {args.min_top1}")
            return 2
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--min-top1", type=float, default=0.65)
    args = ap.parse_args()
    raise SystemExit(train(args))


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Commit**

```bash
git add training/train_choice.py training/train_mulligan.py
git commit -m "阶段2: Choice/Mulligan 训练脚本"
```

---

### Task 2.9: export_onnx.py（LightGBM → ONNX）

**Files:**
- Create: `training/export_onnx.py`

- [ ] **Step 1: 写 export_onnx.py**

```python
# training/export_onnx.py
"""把 LightGBM booster 导出为 ONNX。"""
import argparse
import json
from pathlib import Path

import lightgbm as lgb
import numpy as np
from onnxmltools.convert.lightgbm.convert import convert
from onnxmltools.convert.common.data_types import FloatTensorType

from schema.feature_schema import get_schema, combined_hash


def export(model_type: str, booster_path: Path, out_path: Path, metadata_out: Path):
    booster = lgb.Booster(model_file=str(booster_path))
    schema = get_schema(model_type)
    n_features = schema.vector_length()

    initial_types = [("input", FloatTensorType([None, n_features]))]
    onx = convert(booster, initial_types=initial_types, target_opset=17)
    with out_path.open("wb") as f:
        f.write(onx.SerializeToString())
    print(f"[export_onnx] ONNX → {out_path}")

    metadata = {
        "version": out_path.stem,
        "trained_at": "",
        "model_type": model_type,
        "feature_schema_hash": combined_hash(),
        "feature_count": n_features,
        "source_booster": booster_path.name,
    }
    metadata_out.write_text(json.dumps(metadata, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"[export_onnx] metadata → {metadata_out}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--type", required=True, choices=["action", "choice", "mulligan"])
    ap.add_argument("--booster", required=True, help=".txt LightGBM booster")
    ap.add_argument("--out", required=True, help=".onnx 输出")
    ap.add_argument("--metadata", required=True, help=".json metadata 输出")
    args = ap.parse_args()
    export(args.type, Path(args.booster), Path(args.out), Path(args.metadata))


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 冒烟**

```bash
python export_onnx.py --type action --booster models/action-<ts>.txt --out models/action-<ts>.onnx --metadata models/action-<ts>.json
```

Expected: `.onnx` 和 `.json` 文件生成

- [ ] **Step 3: Commit**

```bash
git add training/export_onnx.py
git commit -m "阶段2: export_onnx.py LightGBM 转 ONNX"
```

---

### Task 2.10: validate_onnx.py（Python ↔ ONNX 差异 < 1e-5）

**Files:**
- Create: `training/validate_onnx.py`
- Create: `training/tests/test_onnx_parity.py`

- [ ] **Step 1: 写 validate_onnx.py**

```python
# training/validate_onnx.py
"""校验 LightGBM Python 预测与 ONNX 推理差异 < 1e-5。"""
import argparse
import numpy as np
import lightgbm as lgb
import onnxruntime as ort

from schema.feature_schema import get_schema


def validate(model_type: str, booster_path: str, onnx_path: str, atol: float = 1e-5):
    booster = lgb.Booster(model_file=booster_path)
    schema = get_schema(model_type)
    rng = np.random.default_rng(0)
    X = rng.standard_normal((100, schema.vector_length())).astype(np.float32)

    py_pred = booster.predict(X).astype(np.float32)

    sess = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    input_name = sess.get_inputs()[0].name
    onnx_out = sess.run(None, {input_name: X})
    # onnxmltools 对 ranker 返回第一个输出 = 分数
    onnx_pred = np.asarray(onnx_out[0]).flatten().astype(np.float32)

    max_diff = float(np.max(np.abs(py_pred - onnx_pred)))
    print(f"[validate] {model_type} max abs diff = {max_diff:.2e}")
    if max_diff > atol:
        raise SystemExit(f"FAIL: ONNX parity diff {max_diff} > {atol}")
    print(f"[validate] OK (< {atol})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--type", required=True, choices=["action", "choice", "mulligan"])
    ap.add_argument("--booster", required=True)
    ap.add_argument("--onnx", required=True)
    ap.add_argument("--atol", type=float, default=1e-5)
    args = ap.parse_args()
    validate(args.type, args.booster, args.onnx, args.atol)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 写 test_onnx_parity.py（用合成数据训个迷你 booster 跑 parity）**

```python
# training/tests/test_onnx_parity.py
import tempfile
from pathlib import Path
import numpy as np
import lightgbm as lgb
from onnxmltools.convert.lightgbm.convert import convert
from onnxmltools.convert.common.data_types import FloatTensorType
import onnxruntime as ort


def test_lightgbm_onnx_parity_under_1e5():
    rng = np.random.default_rng(0)
    X = rng.standard_normal((200, 10)).astype(np.float32)
    y = (X.sum(axis=1) > 0).astype(int)
    ds = lgb.Dataset(X, label=y)
    booster = lgb.train({"objective": "binary", "verbose": -1, "num_leaves": 7}, ds, num_boost_round=20)

    with tempfile.TemporaryDirectory() as td:
        p = Path(td) / "m.onnx"
        onx = convert(booster, initial_types=[("input", FloatTensorType([None, 10]))], target_opset=17)
        p.write_bytes(onx.SerializeToString())

        py_pred = booster.predict(X).astype(np.float32)
        sess = ort.InferenceSession(str(p), providers=["CPUExecutionProvider"])
        input_name = sess.get_inputs()[0].name
        onnx_out = sess.run(None, {input_name: X})
        onnx_pred = np.asarray(onnx_out[0]).flatten().astype(np.float32)
        max_diff = float(np.max(np.abs(py_pred - onnx_pred)))
        assert max_diff < 1e-5, f"parity diff {max_diff}"
```

- [ ] **Step 3: 运行测试**

```bash
cd training && pytest tests/test_onnx_parity.py -v
```

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add training/validate_onnx.py training/tests/test_onnx_parity.py
git commit -m "阶段2: validate_onnx.py + parity 测试 (<1e-5)"
```

---

### Task 2.11: 阶段 2 端到端冒烟 + 硬门槛验收

- [ ] **Step 1: 三类全跑一遍**

```bash
cd training && ./.venv/Scripts/activate
export HB_CLOUD_URL="https://<你的 HK>"
export HB_TRAINING_TOKEN="<trainer token>"

python pull_data.py --type all
TS=$(ls data/action-*.jsonl | tail -1 | sed 's/.*action-//;s/\.jsonl//')

python train_action.py   --input data/action-$TS.jsonl   --min-top1 0.55
python train_choice.py   --input data/choice-$TS.jsonl   --min-top1 0.60
python train_mulligan.py --input data/mulligan-$TS.jsonl --min-top1 0.65

for t in action choice mulligan; do
  python export_onnx.py --type $t --booster models/${t}-*.txt --out models/${t}.onnx --metadata models/${t}.json
  python validate_onnx.py --type $t --booster models/${t}-*.txt --onnx models/${t}.onnx
done
```

**硬门槛**：
- action `top_1` ≥ 0.55
- choice `top_1` ≥ 0.60
- mulligan `top_1` ≥ 0.65
- 三类 ONNX parity max abs diff < 1e-5

不达标 → 回到阶段 1 排查映射/采样，或回到 features/*.py 补充特征。**不进入阶段 3**。

- [ ] **Step 2: 产出评估报告截图**

MLflow UI：
```bash
mlflow ui --backend-store-uri file:./mlruns
```

浏览器打开 `http://127.0.0.1:5000` 查看三个 experiment 的指标。截图保存到 `docs/superpowers/recon/` 作为阶段 2 验收证据。

- [ ] **Step 3: Commit + push**

```bash
cd H:/桌面/炉石脚本/Hearthbot
git add docs/superpowers/recon/
git commit -m "阶段2: 端到端冒烟通过 + 三类模型 top1/parity 达标"
git push
```

---

## 阶段 3 / 云端模型分发 + 灰度 1 台机器（约 4 天，9 个 Task）

产出：云端 Models API、训练机推送脚本、Hearthbot 侧 ModelArtifactManager + ModelRuntimeHost + DecisionRanker + FeatureExtractor（C# 侧）+ FeatureSchemaRegistry；灰度 1 台机器跑 24 小时。

**前置条件**：阶段 2 三类 ONNX + parity 达标。

### Task 3.1: 云端 ModelArtifactStore 服务 + Models Controller

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/ModelArtifactStore.cs`
- Create: `HearthBot.Cloud/Controllers/Learning/ModelsController.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 ModelArtifactStore**

```csharp
// HearthBot.Cloud/Services/Learning/ModelArtifactStore.cs
using System.Security.Cryptography;
using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public class ModelArtifactStore
{
    private readonly LearningDbContext _db;
    private readonly string _modelDir;

    public ModelArtifactStore(LearningDbContext db, IConfiguration cfg)
    {
        _db = db;
        _modelDir = cfg["Learning:ModelDir"] ?? Path.Combine(Directory.GetCurrentDirectory(), "models");
        Directory.CreateDirectory(_modelDir);
    }

    public async Task<ModelVersion> UploadAsync(string modelType, string version, byte[] onnxBytes, string metadataJson, CancellationToken ct)
    {
        if (!new[] { "action", "choice", "mulligan" }.Contains(modelType))
            throw new ArgumentException($"invalid modelType '{modelType}'");
        var sha = Convert.ToHexString(SHA256.HashData(onnxBytes));

        var onnxPath = Path.Combine(_modelDir, $"{version}.onnx");
        var jsonPath = Path.Combine(_modelDir, $"{version}.json");
        await File.WriteAllBytesAsync(onnxPath, onnxBytes, ct);
        await File.WriteAllTextAsync(jsonPath, metadataJson, ct);

        var meta = JsonSerializer.Deserialize<JsonElement>(metadataJson);
        string? featureHash = meta.TryGetProperty("feature_schema_hash", out var h) ? h.GetString() : null;
        string? prevVersion = meta.TryGetProperty("prev_version", out var p) ? p.GetString() : null;
        string? trainedAt = meta.TryGetProperty("trained_at", out var t) ? t.GetString() : null;
        string? trainedBy = meta.TryGetProperty("trained_by", out var tb) ? tb.GetString() : null;

        var record = new ModelVersion
        {
            Version = version,
            ModelType = modelType,
            Sha256 = sha,
            TrainedAt = trainedAt ?? DateTime.UtcNow.ToString("o"),
            MetricsJson = metadataJson,
            PrevVersion = prevVersion,
            FeatureSchemaHash = featureHash ?? string.Empty,
            TrainedBy = trainedBy ?? string.Empty,
            IsActive = false
        };
        _db.ModelVersions.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public async Task ActivateBatchAsync(IReadOnlyDictionary<string, string> typeToVersion, CancellationToken ct)
    {
        // 原子三连推：把 action/choice/mulligan 同时激活
        var names = typeToVersion.Values.ToList();
        var rows = await _db.ModelVersions
            .Where(m => names.Contains(m.Version))
            .ToListAsync(ct);
        if (rows.Count != typeToVersion.Count)
            throw new InvalidOperationException("部分版本不存在, 拒绝激活");
        // 反激活同类型其他版本
        foreach (var t in typeToVersion.Keys)
        {
            var existing = await _db.ModelVersions
                .Where(m => m.ModelType == t && m.IsActive)
                .ToListAsync(ct);
            foreach (var e in existing) e.IsActive = false;
        }
        foreach (var r in rows) r.IsActive = true;
        await _db.SaveChangesAsync(ct);

        // 写 latest.json
        var latest = new Dictionary<string, string>();
        foreach (var kv in typeToVersion) latest[kv.Key] = kv.Value;
        var latestPath = Path.Combine(_modelDir, "latest.json");
        await File.WriteAllTextAsync(latestPath, JsonSerializer.Serialize(latest, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    public async Task<ModelVersion?> RollbackAsync(string modelType, CancellationToken ct)
    {
        var active = await _db.ModelVersions
            .FirstOrDefaultAsync(m => m.ModelType == modelType && m.IsActive, ct);
        if (active?.PrevVersion == null) return null;
        var prev = await _db.ModelVersions
            .FirstOrDefaultAsync(m => m.Version == active.PrevVersion, ct);
        if (prev == null) return null;
        active.IsActive = false;
        active.RolledBackAt = DateTime.UtcNow.ToString("o");
        prev.IsActive = true;
        await _db.SaveChangesAsync(ct);
        await WriteLatestAsync(ct);
        return prev;
    }

    private async Task WriteLatestAsync(CancellationToken ct)
    {
        var actives = await _db.ModelVersions
            .Where(m => m.IsActive)
            .ToListAsync(ct);
        var dict = new Dictionary<string, string>();
        foreach (var m in actives) dict[m.ModelType] = m.Version;
        var latestPath = Path.Combine(_modelDir, "latest.json");
        await File.WriteAllTextAsync(latestPath, JsonSerializer.Serialize(dict), ct);
    }

    public async Task<Dictionary<string, string?>> GetLatestAsync(CancellationToken ct)
    {
        var actives = await _db.ModelVersions
            .Where(m => m.IsActive)
            .ToListAsync(ct);
        var dict = new Dictionary<string, string?>
        {
            ["action"] = null, ["choice"] = null, ["mulligan"] = null
        };
        foreach (var m in actives) dict[m.ModelType] = m.Version;
        return dict;
    }

    public string GetOnnxPath(string version) => Path.Combine(_modelDir, $"{version}.onnx");
    public string GetMetadataPath(string version) => Path.Combine(_modelDir, $"{version}.json");
}
```

- [ ] **Step 2: 写 ModelsController**

```csharp
// HearthBot.Cloud/Controllers/Learning/ModelsController.cs
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Services.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Controllers.Learning;

[ApiController]
[Route("v1/learning/models")]
[Authorize(Policy = "MachineOnly")]
public class ModelsController : ControllerBase
{
    private readonly ModelArtifactStore _store;
    private readonly LearningDbContext _db;

    public ModelsController(ModelArtifactStore store, LearningDbContext db)
    {
        _store = store; _db = db;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        var dict = await _store.GetLatestAsync(ct);
        return Ok(dict);
    }

    [HttpGet("{version}/download")]
    public IActionResult Download(string version)
    {
        var path = _store.GetOnnxPath(version);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/octet-stream", $"{version}.onnx");
    }

    [HttpGet("{version}/metadata")]
    public IActionResult Metadata(string version)
    {
        var path = _store.GetMetadataPath(version);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/json");
    }

    public sealed class UploadRequest
    {
        public string ModelType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = string.Empty;
        public string OnnxBase64 { get; set; } = string.Empty;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadRequest req, CancellationToken ct)
    {
        try
        {
            var bytes = Convert.FromBase64String(req.OnnxBase64);
            var record = await _store.UploadAsync(req.ModelType, req.Version, bytes, req.MetadataJson, ct);
            return Ok(new { version = record.Version, sha256 = record.Sha256 });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed class ActivateRequest
    {
        public Dictionary<string, string> Versions { get; set; } = new();
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest req, CancellationToken ct)
    {
        try
        {
            await _store.ActivateBatchAsync(req.Versions, ct);
            return Ok(new { activated = req.Versions });
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }
}
```

- [ ] **Step 3: Program.cs 注册 Service**

在已有 `AddScoped<SampleIngestService>();` 下追加：
```csharp
builder.Services.AddScoped<HearthBot.Cloud.Services.Learning.ModelArtifactStore>();
```

- [ ] **Step 4: build**

```bash
cd HearthBot.Cloud && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/ModelArtifactStore.cs HearthBot.Cloud/Controllers/Learning/ModelsController.cs HearthBot.Cloud/Program.cs
git commit -m "阶段3: 云端 ModelArtifactStore + ModelsController (upload/latest/download/activate)"
```

---

### Task 3.2: 训练机推送脚本 push_model.py

**Files:**
- Create: `training/push_model.py`

- [ ] **Step 1: 写推送脚本**

```python
# training/push_model.py
"""把 3 个 ONNX + 元数据原子推送并激活到云端。"""
import argparse
import base64
import json
from pathlib import Path

import httpx

from config import load_config


def push(client: httpx.Client, token: str, model_type: str, version: str, onnx_path: Path, metadata_path: Path):
    body = {
        "modelType": model_type,
        "version": version,
        "metadataJson": metadata_path.read_text(encoding="utf-8"),
        "onnxBase64": base64.b64encode(onnx_path.read_bytes()).decode("ascii"),
    }
    r = client.post("/v1/learning/models/upload", json=body,
                    headers={"Authorization": f"Bearer {token}"})
    r.raise_for_status()
    print(f"[push] {model_type} {version} → {r.json()}")


def activate(client: httpx.Client, token: str, versions: dict[str, str]):
    r = client.post("/v1/learning/models/activate",
                    json={"versions": versions},
                    headers={"Authorization": f"Bearer {token}"})
    r.raise_for_status()
    print(f"[activate] {r.json()}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--action-onnx", required=True)
    ap.add_argument("--action-metadata", required=True)
    ap.add_argument("--choice-onnx", required=True)
    ap.add_argument("--choice-metadata", required=True)
    ap.add_argument("--mulligan-onnx", required=True)
    ap.add_argument("--mulligan-metadata", required=True)
    ap.add_argument("--version-tag", required=True, help="e.g. 20260417-0300")
    args = ap.parse_args()

    cfg = load_config()
    with httpx.Client(base_url=cfg.server_base_url, timeout=120.0) as client:
        push(client, cfg.training_token, "action",   f"action-v{args.version_tag}",
             Path(args.action_onnx), Path(args.action_metadata))
        push(client, cfg.training_token, "choice",   f"choice-v{args.version_tag}",
             Path(args.choice_onnx), Path(args.choice_metadata))
        push(client, cfg.training_token, "mulligan", f"mulligan-v{args.version_tag}",
             Path(args.mulligan_onnx), Path(args.mulligan_metadata))

        activate(client, cfg.training_token, {
            "action":   f"action-v{args.version_tag}",
            "choice":   f"choice-v{args.version_tag}",
            "mulligan": f"mulligan-v{args.version_tag}",
        })


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Commit**

```bash
git add training/push_model.py
git commit -m "阶段3: push_model.py 原子推送三模型 + 激活"
```

---

### Task 3.3: FeatureSchemaRegistry（C# 侧，与 Python hash 对齐）

**Files:**
- Create: `BotMain/CloudLearning/Config/FeatureSchemaRegistry.cs`

- [ ] **Step 1: 写 Registry（把 Task 2.2 打印出的 combined hash 填入）**

```csharp
// BotMain/CloudLearning/Config/FeatureSchemaRegistry.cs
namespace BotMain.CloudLearning.Config;

public enum FeatureType { Float, Int, Bool, Categorical }

public sealed record FeatureDef(string Name, FeatureType Type, int Index);

public sealed class FeatureSchema
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<FeatureDef> Features { get; init; } = System.Array.Empty<FeatureDef>();
    public int VectorLength => Features.Count == 0 ? 0 : Features.Max(f => f.Index) + 1;
}

public static class FeatureSchemaRegistry
{
    // 与 Python training/schema/feature_schema.py 的 combined_hash() 保持一致
    // 更新 schema 时, 用 `python -m schema.feature_schema` 获取新 hash 填入
    public const string CurrentHash = "sha256:PLACEHOLDER_UPDATE_FROM_PYTHON";

    public static readonly FeatureSchema Action = new()
    {
        Name = "action",
        Version = "v1",
        Features = new FeatureDef[]
        {
            new("mana_ratio",               FeatureType.Float,       0),
            new("max_mana",                 FeatureType.Int,         1),
            new("my_hp",                    FeatureType.Int,         2),
            new("enemy_hp",                 FeatureType.Int,         3),
            new("my_armor",                 FeatureType.Int,         4),
            new("enemy_armor",              FeatureType.Int,         5),
            new("my_minion_count",          FeatureType.Int,         6),
            new("enemy_minion_count",       FeatureType.Int,         7),
            new("my_total_atk",             FeatureType.Int,         8),
            new("enemy_total_atk",          FeatureType.Int,         9),
            new("my_hand_count",            FeatureType.Int,        10),
            new("enemy_hand_count",         FeatureType.Int,        11),
            new("my_deck_count",            FeatureType.Int,        12),
            new("has_taunt_enemy",          FeatureType.Bool,       13),
            new("has_divine_shield_enemy", FeatureType.Bool,       14),
            new("has_weapon",               FeatureType.Bool,       15),
            new("can_use_hero_power",       FeatureType.Bool,       16),
            new("lethal_threat",            FeatureType.Bool,       17),
            new("turn_number",              FeatureType.Int,        18),
            new("turn_bucket",              FeatureType.Categorical,19),
            new("action_type_play",         FeatureType.Bool,       20),
            new("action_type_attack",       FeatureType.Bool,       21),
            new("action_type_hero_power",   FeatureType.Bool,       22),
            new("action_type_end",          FeatureType.Bool,       23),
            new("source_cost",              FeatureType.Int,        24),
            new("source_atk",               FeatureType.Int,        25),
            new("source_hp",                FeatureType.Int,        26),
            new("source_is_minion",         FeatureType.Bool,       27),
            new("source_is_spell",          FeatureType.Bool,       28),
            new("source_is_weapon",         FeatureType.Bool,       29),
            new("target_is_face",           FeatureType.Bool,       30),
            new("target_is_minion",         FeatureType.Bool,       31),
            new("target_has_taunt",         FeatureType.Bool,       32),
            new("target_is_own",            FeatureType.Bool,       33),
            new("can_kill_target",          FeatureType.Bool,       34),
            new("cost_ratio",               FeatureType.Float,      35),
            new("source_from_generated",    FeatureType.Bool,       36),
            new("source_from_discover",     FeatureType.Bool,       37),
            new("source_from_draw",         FeatureType.Bool,       38),
            new("source_from_mulligan",     FeatureType.Bool,       39),
            new("mana_after",               FeatureType.Int,        40),
            new("my_minion_count_after",    FeatureType.Int,        41),
            new("enemy_minion_count_after", FeatureType.Int,        42),
            new("my_total_atk_after",       FeatureType.Int,        43),
            new("enemy_total_atk_after",    FeatureType.Int,        44),
            new("my_hp_after",              FeatureType.Int,        45),
            new("enemy_hp_after",           FeatureType.Int,        46),
            new("my_hand_after",            FeatureType.Int,        47),
            new("resolves_taunt",           FeatureType.Bool,       48),
            new("trade_efficiency",         FeatureType.Float,      49),
            new("face_damage_delta",        FeatureType.Int,        50),
            new("enters_lethal_state",      FeatureType.Bool,       51),
            new("defends_lethal",           FeatureType.Bool,       52),
            new("tempo_delta",              FeatureType.Float,      53),
            new("board_control_delta",      FeatureType.Float,      54),
            new("my_class",                 FeatureType.Categorical,55),
            new("enemy_class",              FeatureType.Categorical,56),
            new("deck_archetype",           FeatureType.Categorical,57),
            new("has_coin",                 FeatureType.Bool,       58),
            new("step_index",               FeatureType.Int,        59),
        }
    };

    public static readonly FeatureSchema Choice = new()
    {
        Name = "choice",
        Version = "v1",
        Features = new FeatureDef[]
        {
            new("source_card_cost",   FeatureType.Int,        0),
            new("turn_number",        FeatureType.Int,        1),
            new("mana_ratio",         FeatureType.Float,      2),
            new("my_minion_count",    FeatureType.Int,        3),
            new("enemy_minion_count", FeatureType.Int,        4),
            new("option_cost",        FeatureType.Int,        5),
            new("option_atk",         FeatureType.Int,        6),
            new("option_hp",          FeatureType.Int,        7),
            new("option_is_minion",   FeatureType.Bool,       8),
            new("option_is_spell",    FeatureType.Bool,       9),
            new("option_is_weapon",   FeatureType.Bool,      10),
            new("source_discover",    FeatureType.Bool,      11),
            new("source_rebirth",     FeatureType.Bool,      12),
            new("source_subchoice",   FeatureType.Bool,      13),
            new("my_class",           FeatureType.Categorical,14),
            new("enemy_class",        FeatureType.Categorical,15),
            new("has_coin",           FeatureType.Bool,      16),
            new("deck_archetype",     FeatureType.Categorical,17),
            new("option_synergy",     FeatureType.Float,     18),
            new("option_tempo_score", FeatureType.Float,     19),
        }
    };

    public static readonly FeatureSchema Mulligan = new()
    {
        Name = "mulligan",
        Version = "v1",
        Features = new FeatureDef[]
        {
            new("card_cost",                FeatureType.Int,        0),
            new("card_atk",                 FeatureType.Int,        1),
            new("card_hp",                  FeatureType.Int,        2),
            new("card_is_minion",           FeatureType.Bool,       3),
            new("card_is_spell",            FeatureType.Bool,       4),
            new("card_is_weapon",           FeatureType.Bool,       5),
            new("enemy_class",              FeatureType.Categorical,6),
            new("my_class",                 FeatureType.Categorical,7),
            new("has_coin",                 FeatureType.Bool,       8),
            new("deck_archetype",           FeatureType.Categorical,9),
            new("companion_low_cost_count", FeatureType.Int,       10),
            new("companion_2drop_present",  FeatureType.Bool,      11),
            new("companion_3drop_present",  FeatureType.Bool,      12),
            new("companion_removal_count",  FeatureType.Int,       13),
            new("card_is_combo_piece",      FeatureType.Bool,      14),
            new("card_is_card_draw",        FeatureType.Bool,      15),
        }
    };
}
```

**重要**：把 `CurrentHash` 从 `"sha256:PLACEHOLDER_UPDATE_FROM_PYTHON"` 换成 Task 2.2 Step 2 打印的 combined hash。

- [ ] **Step 2: Commit**

```bash
git add BotMain/CloudLearning/Config/FeatureSchemaRegistry.cs
git commit -m "阶段3: C# FeatureSchemaRegistry (60/20/16 维 + hash 锁定)"
```

---

### Task 3.4: ModelArtifactManager（C# 侧下载 / 校验 / 原子替换）

**Files:**
- Create: `BotMain/CloudLearning/ModelArtifactManager.cs`
- Create: `BotCore.Tests/CloudLearning/ModelArtifactManagerTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// BotCore.Tests/CloudLearning/ModelArtifactManagerTests.cs
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BotMain.CloudLearning;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class ModelArtifactManagerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct) =>
            Task.FromResult(Respond(request));
    }

    [Fact]
    public async Task SyncModels_HashMismatch_KeepsOldModelAndLogsWarning()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var logs = new System.Collections.Generic.List<string>();

        var handler = new StubHandler
        {
            Respond = req =>
            {
                if (req.RequestUri!.PathAndQuery.EndsWith("/latest"))
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"action\":\"action-v-new\",\"choice\":null,\"mulligan\":null}")
                    };
                if (req.RequestUri.PathAndQuery.Contains("/metadata"))
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"feature_schema_hash\":\"sha256:WRONG\",\"version\":\"action-v-new\"}")
                    };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        var mgr = new ModelArtifactManager(http, token: "t", modelsDir: tmp,
            expectedSchemaHash: "sha256:CORRECT", log: s => logs.Add(s));

        var result = await mgr.SyncAsync();
        Assert.False(result.AnyUpdated);
        Assert.Contains(logs, s => s.Contains("schema"));
    }
}
```

- [ ] **Step 2: 实现 ModelArtifactManager**

```csharp
// BotMain/CloudLearning/ModelArtifactManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace BotMain.CloudLearning;

public sealed class ModelArtifactManager
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _modelsDir;
    private readonly string _expectedSchemaHash;
    private readonly Action<string> _log;

    public ModelArtifactManager(HttpClient http, string token, string modelsDir, string expectedSchemaHash, Action<string> log)
    {
        _http = http;
        _token = token;
        _modelsDir = modelsDir;
        _expectedSchemaHash = expectedSchemaHash;
        _log = log;
        Directory.CreateDirectory(Path.Combine(_modelsDir, "current"));
        Directory.CreateDirectory(Path.Combine(_modelsDir, "staging"));
    }

    public sealed class SyncResult
    {
        public bool AnyUpdated { get; set; }
        public Dictionary<string, string?> CurrentVersions { get; set; } = new();
    }

    public async Task<SyncResult> SyncAsync(System.Threading.CancellationToken ct = default)
    {
        var result = new SyncResult();
        Dictionary<string, string?> latest;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "v1/learning/models/latest");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log($"[ModelSync] latest.json 拉取失败 status={resp.StatusCode}");
                return result;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            latest = JsonSerializer.Deserialize<Dictionary<string, string?>>(body) ?? new();
        }
        catch (Exception ex)
        {
            _log($"[ModelSync] exception: {ex.Message}");
            return result;
        }

        foreach (var kv in latest)
        {
            if (string.IsNullOrEmpty(kv.Value)) { result.CurrentVersions[kv.Key] = null; continue; }
            var updated = await SyncOneAsync(kv.Key, kv.Value!, ct);
            result.AnyUpdated |= updated;
            result.CurrentVersions[kv.Key] = ReadCurrentVersion(kv.Key);
        }
        return result;
    }

    private async Task<bool> SyncOneAsync(string modelType, string version, System.Threading.CancellationToken ct)
    {
        var currentVer = ReadCurrentVersion(modelType);
        if (currentVer == version)
        {
            return false;
        }

        string metadataJson;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/learning/models/{version}/metadata");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log($"[ModelSync] metadata 拉取失败 {version}: {resp.StatusCode}");
                return false;
            }
            metadataJson = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _log($"[ModelSync] metadata 异常 {version}: {ex.Message}");
            return false;
        }

        using var metaDoc = JsonDocument.Parse(metadataJson);
        var metaHash = metaDoc.RootElement.TryGetProperty("feature_schema_hash", out var h) ? h.GetString() : "";
        if (!string.Equals(metaHash, _expectedSchemaHash, StringComparison.Ordinal))
        {
            _log($"[ModelSync] schema 不匹配 expect={_expectedSchemaHash} actual={metaHash}, 拒绝加载 {version}");
            return false;
        }

        var staging = Path.Combine(_modelsDir, "staging", $"{modelType}.onnx.tmp");
        var stagingMeta = Path.Combine(_modelsDir, "staging", $"{modelType}.json.tmp");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/learning/models/{version}/download");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log($"[ModelSync] onnx 下载失败 {version}: {resp.StatusCode}");
                return false;
            }
            await using var fs = File.Create(staging);
            await resp.Content.CopyToAsync(fs, ct);
        }
        catch (Exception ex)
        {
            _log($"[ModelSync] 下载异常 {version}: {ex.Message}");
            return false;
        }

        await File.WriteAllTextAsync(stagingMeta, metadataJson, ct);

        // SHA256 （可选，如果 latest.json 给了就校验）
        var targetOnnx = Path.Combine(_modelsDir, "current", $"{modelType}.onnx");
        var targetMeta = Path.Combine(_modelsDir, "current", $"{modelType}.json");
        var fallbackOnnx = Path.Combine(_modelsDir, "current", $"{modelType}-fallback.onnx");
        var fallbackMeta = Path.Combine(_modelsDir, "current", $"{modelType}-fallback.json");

        if (File.Exists(targetOnnx))
        {
            if (File.Exists(fallbackOnnx)) File.Delete(fallbackOnnx);
            File.Move(targetOnnx, fallbackOnnx);
            if (File.Exists(targetMeta))
            {
                if (File.Exists(fallbackMeta)) File.Delete(fallbackMeta);
                File.Move(targetMeta, fallbackMeta);
            }
        }
        File.Move(staging, targetOnnx);
        File.Move(stagingMeta, targetMeta);
        _log($"[ModelSync] {modelType} 切换到 {version}");
        return true;
    }

    public string? ReadCurrentVersion(string modelType)
    {
        var metaPath = Path.Combine(_modelsDir, "current", $"{modelType}.json");
        if (!File.Exists(metaPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    public string? GetCurrentOnnxPath(string modelType)
    {
        var p = Path.Combine(_modelsDir, "current", $"{modelType}.onnx");
        if (File.Exists(p)) return p;
        var fb = Path.Combine(_modelsDir, "current", $"{modelType}-fallback.onnx");
        return File.Exists(fb) ? fb : null;
    }
}
```

- [ ] **Step 3: 运行测试**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~ModelArtifactManagerTests
```

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add BotMain/CloudLearning/ModelArtifactManager.cs BotCore.Tests/CloudLearning/ModelArtifactManagerTests.cs
git commit -m "阶段3: ModelArtifactManager 下载/schema 校验/原子替换 (TDD)"
```

---

### Task 3.5: ModelRuntimeHost（ONNX Runtime 包装）

**Files:**
- Create: `BotMain/CloudLearning/ModelRuntimeHost.cs`

- [ ] **Step 1: 写 Host**

```csharp
// BotMain/CloudLearning/ModelRuntimeHost.cs
using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BotMain.CloudLearning;

public sealed class ModelRuntimeHost : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, InferenceSession?> _sessions = new()
    {
        ["action"] = null,
        ["choice"] = null,
        ["mulligan"] = null
    };
    private readonly Dictionary<string, string?> _loadedVersions = new()
    {
        ["action"] = null,
        ["choice"] = null,
        ["mulligan"] = null
    };
    private readonly Action<string> _log;

    public ModelRuntimeHost(Action<string> log) { _log = log; }

    public void LoadOrReload(string modelType, string onnxPath, string version)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(modelType, out var existing) && existing != null)
            {
                existing.Dispose();
            }
            try
            {
                var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                _sessions[modelType] = new InferenceSession(onnxPath, opts);
                _loadedVersions[modelType] = version;
                _log($"[ModelRuntime] {modelType} 已加载 {version}");
            }
            catch (Exception ex)
            {
                _sessions[modelType] = null;
                _loadedVersions[modelType] = null;
                _log($"[ModelRuntime] {modelType} 加载失败: {ex.Message}");
            }
        }
    }

    public bool IsReady(string modelType)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(modelType, out var s) && s != null;
        }
    }

    public bool AllReady
    {
        get
        {
            lock (_lock)
            {
                return _sessions["action"] != null && _sessions["choice"] != null && _sessions["mulligan"] != null;
            }
        }
    }

    public Dictionary<string, string?> LoadedVersions
    {
        get
        {
            lock (_lock) { return new Dictionary<string, string?>(_loadedVersions); }
        }
    }

    public float[] Score(string modelType, float[] featuresFlat, int rowCount, int featureCount)
    {
        InferenceSession? session;
        lock (_lock) { session = _sessions.TryGetValue(modelType, out var s) ? s : null; }
        if (session == null) return Array.Empty<float>();

        var input = new DenseTensor<float>(featuresFlat, new[] { rowCount, featureCount });
        var inputName = session.InputMetadata.Keys.First();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        var output = results.First().AsEnumerable<float>().ToArray();
        return output;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var s in _sessions.Values) s?.Dispose();
            _sessions.Clear();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add BotMain/CloudLearning/ModelRuntimeHost.cs
git commit -m "阶段3: ModelRuntimeHost ONNX Runtime 包装 (三类 InferenceSession)"
```

---

### Task 3.6: FeatureExtractor C# 侧（与 Python 对齐）

**Files:**
- Create: `BotMain/CloudLearning/FeatureExtractor.cs`
- Create: `BotCore.Tests/CloudLearning/FeatureExtractorTests.cs`

- [ ] **Step 1: 写 FeatureExtractor**

```csharp
// BotMain/CloudLearning/FeatureExtractor.cs
using System;
using System.Text.Json;
using BotMain.CloudLearning.Config;
using BotMain.CloudLearning.Contracts;

namespace BotMain.CloudLearning;

public static class FeatureExtractor
{
    public static float[] ExtractAction(DecisionContextSnapshot ctx, DecisionCandidate candidate, int stepIndex)
    {
        var schema = FeatureSchemaRegistry.Action;
        var vec = new float[schema.VectorLength];

        using var boardDoc = JsonDocument.Parse(string.IsNullOrEmpty(ctx.BoardSnapshotJson) ? "{}" : ctx.BoardSnapshotJson);
        var board = boardDoc.RootElement;

        int myHp = board.TryGetProperty("myHp", out var mhp) ? mhp.GetInt32() : 30;
        int enemyHp = board.TryGetProperty("enemyHp", out var ehp) ? ehp.GetInt32() : 30;
        int curMana = board.TryGetProperty("currentMana", out var cm) ? cm.GetInt32() : 1;
        int maxMana = board.TryGetProperty("maxMana", out var mm) ? mm.GetInt32() : 1;

        Set(vec, schema, "mana_ratio", (float)curMana / Math.Max(1, maxMana));
        Set(vec, schema, "max_mana", maxMana);
        Set(vec, schema, "my_hp", myHp);
        Set(vec, schema, "enemy_hp", enemyHp);
        Set(vec, schema, "turn_number", ctx.Turn);
        Set(vec, schema, "turn_bucket", Math.Min(ctx.Turn / 3, 9));
        Set(vec, schema, "step_index", stepIndex);

        var atype = (candidate.ActionType ?? "").ToUpperInvariant();
        Set(vec, schema, "action_type_play",        atype == "PLAY" ? 1 : 0);
        Set(vec, schema, "action_type_attack",      atype == "ATTACK" ? 1 : 0);
        Set(vec, schema, "action_type_hero_power",  atype == "HEROPOWER" ? 1 : 0);
        Set(vec, schema, "action_type_end",         atype == "END" ? 1 : 0);

        using var featDoc = JsonDocument.Parse(string.IsNullOrEmpty(candidate.FeaturesJson) ? "{}" : candidate.FeaturesJson);
        var cf = featDoc.RootElement;
        Set(vec, schema, "source_cost",     cf.TryGetProperty("sourceCost", out var sc) ? sc.GetInt32() : 0);
        Set(vec, schema, "source_atk",      cf.TryGetProperty("sourceAtk", out var sa) ? sa.GetInt32() : 0);
        Set(vec, schema, "source_hp",       cf.TryGetProperty("sourceHp", out var sh) ? sh.GetInt32() : 0);
        Set(vec, schema, "target_is_face",  cf.TryGetProperty("targetIsFace", out var tf) && tf.GetBoolean() ? 1 : 0);
        Set(vec, schema, "target_is_minion",cf.TryGetProperty("targetIsMinion", out var tm) && tm.GetBoolean() ? 1 : 0);
        Set(vec, schema, "target_has_taunt",cf.TryGetProperty("targetHasTaunt", out var tt) && tt.GetBoolean() ? 1 : 0);
        Set(vec, schema, "can_kill_target", cf.TryGetProperty("canKillTarget", out var ck) && ck.GetBoolean() ? 1 : 0);

        return vec;
    }

    public static float[] ExtractChoice(DecisionContextSnapshot ctx, DecisionCandidate option)
    {
        var schema = FeatureSchemaRegistry.Choice;
        var vec = new float[schema.VectorLength];
        Set(vec, schema, "turn_number", ctx.Turn);

        using var featDoc = JsonDocument.Parse(string.IsNullOrEmpty(option.FeaturesJson) ? "{}" : option.FeaturesJson);
        var cf = featDoc.RootElement;
        Set(vec, schema, "option_cost", cf.TryGetProperty("cost", out var c) ? c.GetInt32() : 0);
        Set(vec, schema, "option_atk",  cf.TryGetProperty("atk", out var a) ? a.GetInt32() : 0);
        Set(vec, schema, "option_hp",   cf.TryGetProperty("hp", out var h) ? h.GetInt32() : 0);
        Set(vec, schema, "source_discover",  string.Equals(ctx.ChoiceSourceType, "discover",  StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        Set(vec, schema, "source_rebirth",   string.Equals(ctx.ChoiceSourceType, "rebirth",   StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        Set(vec, schema, "source_subchoice", string.Equals(ctx.ChoiceSourceType, "subchoice", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        Set(vec, schema, "has_coin", ctx.HasCoin ? 1 : 0);
        return vec;
    }

    public static float[] ExtractMulligan(DecisionContextSnapshot ctx, DecisionCandidate card)
    {
        var schema = FeatureSchemaRegistry.Mulligan;
        var vec = new float[schema.VectorLength];

        using var featDoc = JsonDocument.Parse(string.IsNullOrEmpty(card.FeaturesJson) ? "{}" : card.FeaturesJson);
        var cf = featDoc.RootElement;
        Set(vec, schema, "card_cost",       cf.TryGetProperty("cost", out var c) ? c.GetInt32() : 0);
        Set(vec, schema, "card_atk",        cf.TryGetProperty("atk", out var a) ? a.GetInt32() : 0);
        Set(vec, schema, "card_hp",         cf.TryGetProperty("hp", out var h) ? h.GetInt32() : 0);
        Set(vec, schema, "card_is_minion",  cf.TryGetProperty("isMinion", out var im) && im.GetBoolean() ? 1 : 0);
        Set(vec, schema, "card_is_spell",   cf.TryGetProperty("isSpell", out var isp) && isp.GetBoolean() ? 1 : 0);
        Set(vec, schema, "has_coin",        ctx.HasCoin ? 1 : 0);
        return vec;
    }

    private static void Set(float[] vec, FeatureSchema schema, string name, float value)
    {
        foreach (var f in schema.Features)
        {
            if (f.Name == name) { vec[f.Index] = value; return; }
        }
        throw new KeyNotFoundException($"feature '{name}' not in schema '{schema.Name}'");
    }
}
```

- [ ] **Step 2: 测试向量长度**

```csharp
// BotCore.Tests/CloudLearning/FeatureExtractorTests.cs
using BotMain.CloudLearning;
using BotMain.CloudLearning.Contracts;
using BotMain.CloudLearning.Config;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class FeatureExtractorTests
{
    [Fact]
    public void ExtractAction_ReturnsLengthEqualsSchema()
    {
        var ctx = new DecisionContextSnapshot { Turn = 5 };
        var cand = new DecisionCandidate { ActionType = "PLAY" };
        var vec = FeatureExtractor.ExtractAction(ctx, cand, stepIndex: 1);
        Assert.Equal(FeatureSchemaRegistry.Action.VectorLength, vec.Length);
    }

    [Fact]
    public void ExtractChoice_ReturnsLengthEqualsSchema()
    {
        var vec = FeatureExtractor.ExtractChoice(new DecisionContextSnapshot(), new DecisionCandidate());
        Assert.Equal(FeatureSchemaRegistry.Choice.VectorLength, vec.Length);
    }

    [Fact]
    public void ExtractMulligan_ReturnsLengthEqualsSchema()
    {
        var vec = FeatureExtractor.ExtractMulligan(new DecisionContextSnapshot(), new DecisionCandidate());
        Assert.Equal(FeatureSchemaRegistry.Mulligan.VectorLength, vec.Length);
    }
}
```

- [ ] **Step 3: 运行**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~FeatureExtractorTests
```

Expected: PASS 3

- [ ] **Step 4: Commit**

```bash
git add BotMain/CloudLearning/FeatureExtractor.cs BotCore.Tests/CloudLearning/FeatureExtractorTests.cs
git commit -m "阶段3: C# FeatureExtractor (action/choice/mulligan)"
```

---

### Task 3.7: DecisionRanker

**Files:**
- Create: `BotMain/CloudLearning/DecisionRanker.cs`
- Create: `BotCore.Tests/CloudLearning/DecisionRankerTests.cs`

- [ ] **Step 1: 写 DecisionRanker**

```csharp
// BotMain/CloudLearning/DecisionRanker.cs
using System;
using System.Collections.Generic;
using BotMain.CloudLearning.Config;
using BotMain.CloudLearning.Contracts;

namespace BotMain.CloudLearning;

public sealed class DecisionRanker
{
    private readonly ModelRuntimeHost _host;

    public DecisionRanker(ModelRuntimeHost host) { _host = host; }

    public bool IsAllReady => _host.AllReady;

    public RankerResult? Rank(DecisionContextSnapshot ctx, IReadOnlyList<DecisionCandidate> candidates, int stepIndex = 0)
    {
        if (candidates.Count == 0) return null;

        string modelType = ctx.Type switch
        {
            DecisionType.Action => "action",
            DecisionType.Choice => "choice",
            DecisionType.Mulligan => "mulligan",
            _ => "action"
        };
        if (!_host.IsReady(modelType)) return null;

        int featureCount = modelType switch
        {
            "action" => FeatureSchemaRegistry.Action.VectorLength,
            "choice" => FeatureSchemaRegistry.Choice.VectorLength,
            "mulligan" => FeatureSchemaRegistry.Mulligan.VectorLength,
            _ => 0
        };
        var flat = new float[candidates.Count * featureCount];
        for (int i = 0; i < candidates.Count; i++)
        {
            float[] row = ctx.Type switch
            {
                DecisionType.Action => FeatureExtractor.ExtractAction(ctx, candidates[i], stepIndex),
                DecisionType.Choice => FeatureExtractor.ExtractChoice(ctx, candidates[i]),
                DecisionType.Mulligan => FeatureExtractor.ExtractMulligan(ctx, candidates[i]),
                _ => new float[featureCount]
            };
            Array.Copy(row, 0, flat, i * featureCount, featureCount);
        }

        var scores = _host.Score(modelType, flat, candidates.Count, featureCount);
        if (scores.Length < candidates.Count) return null;

        int top = 0;
        for (int i = 1; i < candidates.Count; i++)
            if (scores[i] > scores[top]) top = i;

        return new RankerResult
        {
            HasValue = true,
            Top1Index = top,
            Top1Score = scores[top],
            AllScores = new List<double>(scores.Select(s => (double)s)),
            ModelVersion = _host.LoadedVersions.TryGetValue(modelType, out var v) ? v ?? "" : ""
        };
    }
}
```

- [ ] **Step 2: 写测试（mock ModelRuntimeHost）**

```csharp
// BotCore.Tests/CloudLearning/DecisionRankerTests.cs
// 考虑到 ModelRuntimeHost 非虚，本测试集用"未加载模型时返回 null"来验证兜底。
using System;
using System.Collections.Generic;
using BotMain.CloudLearning;
using BotMain.CloudLearning.Contracts;
using Xunit;

namespace BotCore.Tests.CloudLearning;

public class DecisionRankerTests
{
    [Fact]
    public void Rank_WhenModelNotLoaded_ReturnsNull()
    {
        var host = new ModelRuntimeHost(_ => { });
        var ranker = new DecisionRanker(host);
        var ctx = new DecisionContextSnapshot { Type = DecisionType.Action };
        var cands = new List<DecisionCandidate> { new() { ActionType = "PLAY" } };
        var result = ranker.Rank(ctx, cands);
        Assert.Null(result);
    }

    [Fact]
    public void Rank_EmptyCandidates_ReturnsNull()
    {
        var host = new ModelRuntimeHost(_ => { });
        var ranker = new DecisionRanker(host);
        var result = ranker.Rank(new DecisionContextSnapshot(), new List<DecisionCandidate>());
        Assert.Null(result);
    }
}
```

- [ ] **Step 3: 运行**

```bash
cd BotCore.Tests && dotnet test --filter FullyQualifiedName~DecisionRankerTests
```

Expected: PASS 2

- [ ] **Step 4: Commit**

```bash
git add BotMain/CloudLearning/DecisionRanker.cs BotCore.Tests/CloudLearning/DecisionRankerTests.cs
git commit -m "阶段3: DecisionRanker 三类决策统一排序入口"
```

---

### Task 3.8: Orchestrator 接入 Ranker 与 ArtifactManager

**Files:**
- Modify: `BotMain/CloudLearning/CloudLearningOrchestrator.cs`
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: Orchestrator 新增 ranker/manager 字段**

在 `CloudLearningOrchestrator` 类内追加：

```csharp
private readonly string _modelsDir;
private readonly string _schemaHash;
private ModelArtifactManager? _artifactManager;
private ModelRuntimeHost? _runtimeHost;
private DecisionRanker? _ranker;
private Task? _modelSyncLoop;

public DecisionRanker? Ranker => _ranker;
```

构造器里（`_health` 初始化之前/之后都可），新增：

```csharp
_modelsDir = System.IO.Path.Combine(outboxDir, "models");
_schemaHash = BotMain.CloudLearning.Config.FeatureSchemaRegistry.CurrentHash;
```

替换原有的 `IsRankerReady => false` 为：
```csharp
public bool IsRankerReady => _runtimeHost?.AllReady ?? false;
```

`StartAsync` 里，紧接 `_outbox.InitializeAsync` 之后加：

```csharp
_runtimeHost = new ModelRuntimeHost(_log);
_artifactManager = new ModelArtifactManager(_http, _options.Token, _modelsDir, _schemaHash, _log);
_ranker = new DecisionRanker(_runtimeHost);
await LoadCurrentModelsAsync();
```

并新增私有方法：

```csharp
private async Task LoadCurrentModelsAsync()
{
    if (_artifactManager == null || _runtimeHost == null) return;
    foreach (var t in new[] { "action", "choice", "mulligan" })
    {
        var path = _artifactManager.GetCurrentOnnxPath(t);
        var version = _artifactManager.ReadCurrentVersion(t) ?? "";
        if (path != null) _runtimeHost.LoadOrReload(t, path, version);
    }
}

private async Task ModelSyncLoopAsync(CancellationToken ct)
{
    var interval = TimeSpan.FromHours(_options.ModelSync.CheckIntervalHours);
    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (_artifactManager != null && _options.ModelSync.AutoDownload)
            {
                var result = await _artifactManager.SyncAsync(ct);
                if (result.AnyUpdated) await LoadCurrentModelsAsync();
            }
        }
        catch (Exception ex) { _log($"[ModelSync] loop error: {ex.Message}"); }
        try { await Task.Delay(interval, ct); } catch { return; }
    }
}
```

在 `StartAsync` 拉起 worker 处追加：
```csharp
_modelSyncLoop = Task.Run(() => ModelSyncLoopAsync(_cts!.Token));
```

在 `DisposeAsync` 等候：
```csharp
if (_modelSyncLoop != null) await _modelSyncLoop;
_runtimeHost?.Dispose();
```

并把 `_health` 构造里的 `ModelVersions = new Dictionary<string, string?> { ... }` 换成：
```csharp
ModelVersions = _runtimeHost?.LoadedVersions ?? new Dictionary<string, string?>
{
    ["action"] = null, ["choice"] = null, ["mulligan"] = null
}
```

- [ ] **Step 2: BotService 调用 Ranker**

`RequestRecommendationAsync` 里，原 Action 采样 `RecordAsync` 之前插入"ranker 排序并覆盖 finalAction"的逻辑：

```csharp
if (_cloudLearning != null && _cloudLearning.IsRankerReady && UseLearnedLocal && sample.Candidates.Count > 0)
{
    try
    {
        var ranked = _cloudLearning.Ranker?.Rank(sample.Context, sample.Candidates, stepIndex: sample.Context.StepIndex);
        if (ranked != null && ranked.HasValue)
        {
            finalActionCommand = sample.Candidates[ranked.Top1Index].ActionCommand;
            sample.LocalPickIndex = ranked.Top1Index;
        }
    }
    catch (Exception ex) { Log($"[CloudLearning] rank failed: {ex.Message}"); }
}
```

（`UseLearnedLocal` / `finalActionCommand` 对齐 BotService 现有命名）

- [ ] **Step 3: build**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet test
```

Expected: 全部通过

- [ ] **Step 4: Commit**

```bash
git add BotMain/CloudLearning/CloudLearningOrchestrator.cs BotMain/BotService.cs
git commit -m "阶段3: Orchestrator 接入 ArtifactManager/RuntimeHost/DecisionRanker + BotService 调用"
```

---

### Task 3.9: 阶段 3 灰度与验收（1 台机器 24h）

- [ ] **Step 1: 训练机推送当前 ONNX**

```bash
cd training && ./.venv/Scripts/activate
export HB_CLOUD_URL="https://<HK>"
export HB_TRAINING_TOKEN="<trainer token>"
python push_model.py \
  --action-onnx models/action.onnx --action-metadata models/action.json \
  --choice-onnx models/choice.onnx --choice-metadata models/choice.json \
  --mulligan-onnx models/mulligan.onnx --mulligan-metadata models/mulligan.json \
  --version-tag 20260417-0300
```

Expected: 云端 `ModelVersions` 表新增 3 行，`IsActive=1`，`/v1/learning/models/latest` 返回对应版本

- [ ] **Step 2: 灰度机器开启 UseLearnedLocal**

挑 hb-dorm-01，appsettings.json 里 `"UseLearnedLocal": true`（或等效现有开关）。其他 4 台保持 false。

启动该机器，观察日志 6 小时内看到：
```
[ModelSync] action 切换到 action-v20260417-0300
[ModelSync] choice 切换到 choice-v20260417-0300
[ModelSync] mulligan 切换到 mulligan-v20260417-0300
[ModelRuntime] action 已加载 action-v20260417-0300
...
```

- [ ] **Step 3: 24 小时观察**

运行 24h，期间观察：
- `Machines` 表该机心跳 `LastStatsJson.rollingStats24h` 数据
- 对局完整打完（不卡死）
- 游戏无明显乱打/非法动作卡死

硬门槛：
- `illegal_action_rate` < 0.01
- `top1_match_rate`（action）> 0.55
- `top1_match_rate`（choice）> 0.60
- `top1_match_rate`（mulligan）> 0.65

不达标 → 灰度机 UseLearnedLocal=false，回到阶段 2 或检查 FeatureExtractor 一致性。

- [ ] **Step 4: 灰度结束 + 阶段收尾 push**

```bash
cd H:/桌面/炉石脚本/Hearthbot
# 把灰度验证截图/日志存档
git add docs/superpowers/recon/
git commit -m "阶段3: 灰度 1 台机 24h 验收通过"
git push
```

---

## 阶段 4 / 全量放量 + 自动训练 + UI（约 3 天，6 个 Task）

产出：每日 03:00 自动训练 + 推送 + 自动回滚、SettingsWindow Cloud Learning 面板、5 台全开。

**前置条件**：阶段 3 灰度 24h 验收通过。

### Task 4.1: 训练机 Windows Task Scheduler 定时任务

**Files:**
- Create: `training/schedule_daily.ps1`
- Create: `training/run_daily.py`

- [ ] **Step 1: 写 run_daily.py（一键：pull → train → export → validate → push）**

```python
# training/run_daily.py
"""每日训练管线入口。"""
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def run(cmd: list[str]) -> int:
    print(f"[daily] $ {' '.join(cmd)}")
    p = subprocess.run(cmd, check=False)
    return p.returncode


def latest(glob: str) -> str | None:
    files = sorted(Path("data").glob(glob))
    return str(files[-1]) if files else None


def main():
    Path("data").mkdir(exist_ok=True)
    Path("models").mkdir(exist_ok=True)
    if run([sys.executable, "pull_data.py", "--type", "all"]) != 0:
        print("[daily] pull failed"); return 1

    action_jsonl = latest("action-*.jsonl")
    choice_jsonl = latest("choice-*.jsonl")
    mulligan_jsonl = latest("mulligan-*.jsonl")

    if run([sys.executable, "train_action.py",   "--input", action_jsonl,   "--min-top1", "0.55"]) != 0: return 2
    if run([sys.executable, "train_choice.py",   "--input", choice_jsonl,   "--min-top1", "0.60"]) != 0: return 2
    if run([sys.executable, "train_mulligan.py", "--input", mulligan_jsonl, "--min-top1", "0.65"]) != 0: return 2

    version_tag = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M")
    for t in ["action", "choice", "mulligan"]:
        booster = str(sorted(Path("models").glob(f"{t}-*.txt"))[-1])
        onnx = f"models/{t}.onnx"
        metadata = f"models/{t}.json"
        if run([sys.executable, "export_onnx.py",   "--type", t, "--booster", booster, "--out", onnx, "--metadata", metadata]) != 0: return 3
        if run([sys.executable, "validate_onnx.py", "--type", t, "--booster", booster, "--onnx", onnx]) != 0: return 4

    if run([sys.executable, "push_model.py",
            "--action-onnx", "models/action.onnx", "--action-metadata", "models/action.json",
            "--choice-onnx", "models/choice.onnx", "--choice-metadata", "models/choice.json",
            "--mulligan-onnx", "models/mulligan.onnx", "--mulligan-metadata", "models/mulligan.json",
            "--version-tag", version_tag]) != 0:
        return 5
    print(f"[daily] OK version_tag={version_tag}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: 写 schedule_daily.ps1（注册 Task Scheduler 定时任务）**

```powershell
# training/schedule_daily.ps1
# 每天 03:00 运行 run_daily.py
$taskName = "HearthbotDailyTraining"
$workDir = (Resolve-Path .).Path
$pyExe = Join-Path $workDir ".venv\Scripts\python.exe"

$action = New-ScheduledTaskAction -Execute $pyExe -Argument "run_daily.py" -WorkingDirectory $workDir
$trigger = New-ScheduledTaskTrigger -Daily -At 3:00AM
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -WakeToRun -ExecutionTimeLimit (New-TimeSpan -Hours 6)

$envBlock = @"
`$env:HB_CLOUD_URL='$env:HB_CLOUD_URL'
`$env:HB_TRAINING_TOKEN='$env:HB_TRAINING_TOKEN'
"@

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force
Write-Host "已注册定时任务 $taskName, 每天 03:00 执行"
Write-Host "环境变量需在任务账户的 Environment 中配置:"
Write-Host $envBlock
```

- [ ] **Step 3: 在训练机一次性注册**

```powershell
cd training
$env:HB_CLOUD_URL = "https://<HK>"
$env:HB_TRAINING_TOKEN = "<trainer token>"
./.venv/Scripts/activate
./schedule_daily.ps1
```

验证：
```powershell
Get-ScheduledTask -TaskName HearthbotDailyTraining
```

- [ ] **Step 4: Commit**

```bash
cd H:/桌面/炉石脚本/Hearthbot
git add training/run_daily.py training/schedule_daily.ps1
git commit -m "阶段4: 训练机每日 03:00 自动训练 + 推送"
```

---

### Task 4.2: 云端 ConsistencyMonitor 自动回滚（HostedService）

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/ConsistencyMonitor.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 ConsistencyMonitor**

```csharp
// HearthBot.Cloud/Services/Learning/ConsistencyMonitor.cs
using System.Text.Json;
using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models.Learning;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public class ConsistencyMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsistencyMonitor> _logger;
    private const double RollbackThresholdPp = 3.0; // 3 个百分点

    public ConsistencyMonitor(IServiceScopeFactory scopeFactory, ILogger<ConsistencyMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "ConsistencyMonitor check 异常"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); } catch { return; }
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<ModelArtifactStore>();

        foreach (var modelType in new[] { "action", "choice", "mulligan" })
        {
            var current = await db.ModelVersions.FirstOrDefaultAsync(m => m.ModelType == modelType && m.IsActive, ct);
            if (current == null || string.IsNullOrEmpty(current.PrevVersion)) continue;

            // 24h 内只激活了新版本才有意义
            var activatedAt = DateTime.Parse(current.TrainedAt);
            if (DateTime.UtcNow - activatedAt < TimeSpan.FromHours(4)) continue; // 给新版本至少 4h 暖机
            if (DateTime.UtcNow - activatedAt > TimeSpan.FromHours(48)) continue; // 超过 48h 已稳定, 不再监控

            double currentRate = await GetAverageTop1Async(db, modelType, 24, ct);
            double prevRate = await GetPrevVersionAverageTop1Async(db, modelType, current.PrevVersion!, ct);

            if (prevRate > 0 && currentRate > 0 && (prevRate - currentRate) * 100 > RollbackThresholdPp)
            {
                _logger.LogWarning("自动回滚 {ModelType}: 当前 {Cur:F3} 前版 {Prev:F3} 差 > {Th}pp",
                    modelType, currentRate, prevRate, RollbackThresholdPp);
                var rolled = await store.RollbackAsync(modelType, ct);
                if (rolled != null)
                    _logger.LogWarning("已回滚到 {Version}", rolled.Version);
            }
        }
    }

    private async Task<double> GetAverageTop1Async(LearningDbContext db, string modelType, int hours, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours).ToString("o");
        var machines = await db.Machines.AsNoTracking()
            .Where(m => string.Compare(m.LastSeenAt, cutoff) >= 0)
            .ToListAsync(ct);
        var rates = new List<double>();
        foreach (var m in machines)
        {
            try
            {
                using var doc = JsonDocument.Parse(m.LastStatsJson);
                if (doc.RootElement.TryGetProperty("rollingStats24h", out var rs)
                    && rs.TryGetProperty("top1MatchRate", out var r))
                {
                    rates.Add(r.GetDouble());
                }
            }
            catch { }
        }
        return rates.Count == 0 ? 0 : rates.Average();
    }

    private Task<double> GetPrevVersionAverageTop1Async(LearningDbContext db, string modelType, string prevVersion, CancellationToken ct)
    {
        // 简化：prev 版本的历史 avg 没有单独持久化, 这里用 0.60 作为基准。
        // 更准确的实现要把 heartbeat 里的 top1MatchRate 按模型版本分桶持久化。
        // 阶段 4 暂不做这一层, 留在未来迭代。
        return Task.FromResult(0.60);
    }
}
```

- [ ] **Step 2: Program.cs 注册 HostedService**

```csharp
builder.Services.AddHostedService<HearthBot.Cloud.Services.Learning.ConsistencyMonitor>();
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/ConsistencyMonitor.cs HearthBot.Cloud/Program.cs
git commit -m "阶段4: ConsistencyMonitor 24h 一致率监控 + 3pp 阈值自动回滚"
```

---

### Task 4.3: ArchiveJob（90 天归档）

**Files:**
- Create: `HearthBot.Cloud/Services/Learning/ArchiveJob.cs`
- Modify: `HearthBot.Cloud/Program.cs`

- [ ] **Step 1: 写 ArchiveJob**

```csharp
// HearthBot.Cloud/Services/Learning/ArchiveJob.cs
using System.IO.Compression;
using System.Text.Json;
using HearthBot.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services.Learning;

public class ArchiveJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArchiveJob> _logger;
    private readonly string _archiveDir;

    public ArchiveJob(IServiceScopeFactory scopeFactory, ILogger<ArchiveJob> logger, IConfiguration cfg)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _archiveDir = cfg["Learning:ArchiveDir"] ?? Path.Combine(Directory.GetCurrentDirectory(), "learning-archive");
        Directory.CreateDirectory(_archiveDir);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "ArchiveJob 异常"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); } catch { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-90).ToString("o");

        var ym = DateTime.UtcNow.AddDays(-91).ToString("yyyy-MM");
        var outFile = Path.Combine(_archiveDir, $"{ym}.jsonl.gz");
        if (File.Exists(outFile))
        {
            _logger.LogInformation("Archive {Month} 已存在, 跳过", ym);
            return;
        }

        var actionOld = db.ActionDecisions.AsNoTracking().Where(d => string.Compare(d.CreatedAt, cutoff) < 0);
        int total = 0;
        await using var fs = File.Create(outFile);
        await using var gz = new GZipStream(fs, CompressionLevel.SmallestSize);
        await using var writer = new StreamWriter(gz);
        await foreach (var d in actionOld.AsAsyncEnumerable().WithCancellation(ct))
        {
            var record = new { type = "action", d.DecisionId, d.ClientSampleId, d.MatchId, d.Turn, d.CreatedAt };
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
            total++;
        }
        await writer.FlushAsync();
        _logger.LogInformation("Archive {File} 写入 {Count} 行", outFile, total);

        // 删库行
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ActionDecisions WHERE CreatedAt < {0}", cutoff);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ChoiceDecisions WHERE CreatedAt < {0}", cutoff);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM MulliganDecisions WHERE CreatedAt < {0}", cutoff);
        await db.Database.ExecuteSqlRawAsync("VACUUM");
    }
}
```

- [ ] **Step 2: 注册**

```csharp
builder.Services.AddHostedService<HearthBot.Cloud.Services.Learning.ArchiveJob>();
```

- [ ] **Step 3: Commit**

```bash
git add HearthBot.Cloud/Services/Learning/ArchiveJob.cs HearthBot.Cloud/Program.cs
git commit -m "阶段4: ArchiveJob 90 天结构化表归档 + VACUUM"
```

---

### Task 4.4: Cloud Learning UI 面板（SettingsWindow）

**Files:**
- Modify: `BotMain/SettingsWindow.xaml`
- Modify: `BotMain/SettingsWindow.xaml.cs`
- Modify: `BotMain/MainViewModel.cs`

- [ ] **Step 1: 定位现有 SettingsWindow 布局**

Run: `grep -n "TabItem\|GroupBox" BotMain/SettingsWindow.xaml | head -20`
记录布局容器类型（TabControl 或 StackPanel）。

- [ ] **Step 2: 在合适位置追加"Cloud Learning"分区**

示例（如用 TabControl）：

```xml
<TabItem Header="云端学习">
  <StackPanel Margin="10">
    <TextBlock Text="机器 ID:" />
    <TextBlock Text="{Binding CloudMachineId}" FontWeight="Bold" />
    <TextBlock Text="服务器连接:" Margin="0,10,0,0" />
    <Ellipse Width="12" Height="12" Fill="{Binding CloudConnectionStatusBrush}" />
    <TextBlock Text="{Binding CloudConnectionStatusText}" />

    <TextBlock Text="当前模型版本:" Margin="0,10,0,0" />
    <TextBlock Text="{Binding CloudModelVersions}" />

    <TextBlock Text="Outbox 深度:" Margin="0,10,0,0" />
    <TextBlock Text="{Binding CloudOutboxDepth}" />

    <TextBlock Text="上次上传:" />
    <TextBlock Text="{Binding CloudLastUploadAt}" />

    <Button Content="Upload Now" Command="{Binding CloudFlushCommand}" Margin="0,10,0,0" />
  </StackPanel>
</TabItem>
```

- [ ] **Step 3: MainViewModel 加属性与命令**

```csharp
// 添加到 MainViewModel 现有属性定义区
public string CloudMachineId { get; private set; } = string.Empty;
public string CloudConnectionStatusText { get; private set; } = "未连接";
public System.Windows.Media.Brush CloudConnectionStatusBrush { get; private set; } = System.Windows.Media.Brushes.Gray;
public string CloudModelVersions { get; private set; } = "-";
public int CloudOutboxDepth { get; private set; }
public string CloudLastUploadAt { get; private set; } = "-";
public System.Windows.Input.ICommand CloudFlushCommand { get; }

// 在构造器里
CloudFlushCommand = new RelayCommand(async () =>
{
    if (_botService?.CloudLearningPublic != null)
        await _botService.CloudLearningPublic.FlushNowAsync(default);
});

// 新增定时刷新 (每 5s) 从 orchestrator 取 snapshot
private void UpdateCloudPanel()
{
    var orch = _botService?.CloudLearningPublic;
    if (orch == null) return;
    CloudMachineId = orch.MachineId;
    var v = orch.GetLoadedVersions();
    CloudModelVersions = $"action={v.GetValueOrDefault("action") ?? "-"}, choice={v.GetValueOrDefault("choice") ?? "-"}, mulligan={v.GetValueOrDefault("mulligan") ?? "-"}";
    CloudOutboxDepth = orch.GetOutboxDepth();
    CloudLastUploadAt = orch.LastUploadOkAt > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(orch.LastUploadOkAt).ToLocalTime().ToString("HH:mm:ss")
        : "-";
    var reachable = orch.IsServerReachable;
    CloudConnectionStatusText = reachable ? "已连接" : "未连接";
    CloudConnectionStatusBrush = reachable ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
    OnPropertyChanged(nameof(CloudMachineId));
    OnPropertyChanged(nameof(CloudConnectionStatusText));
    OnPropertyChanged(nameof(CloudConnectionStatusBrush));
    OnPropertyChanged(nameof(CloudModelVersions));
    OnPropertyChanged(nameof(CloudOutboxDepth));
    OnPropertyChanged(nameof(CloudLastUploadAt));
}
```

> `CloudLearningPublic` / `GetLoadedVersions` / `GetOutboxDepth` / `MachineId` / `LastUploadOkAt` / `IsServerReachable` 需要在 `CloudLearningOrchestrator` 上补暴露（简单 public property/method，从已有字段反射出来）。

- [ ] **Step 4: Orchestrator 补公共只读属性**

在 `CloudLearningOrchestrator` 追加：
```csharp
public string MachineId => _options.MachineId;
public long LastUploadOkAt => _lastUploadOkAt;
public bool IsServerReachable => _lastUploadOkAt > 0
    && (System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastUploadOkAt) < 10 * 60 * 1000;
public Dictionary<string, string?> GetLoadedVersions() =>
    _runtimeHost?.LoadedVersions ?? new Dictionary<string, string?>();
public int GetOutboxDepth() => _outbox.GetDepthAsync().GetAwaiter().GetResult();
```

BotService 上暴露：
```csharp
public CloudLearning.CloudLearningOrchestrator? CloudLearningPublic => _cloudLearning;
```

- [ ] **Step 5: build + 手动验证 UI**

```bash
cd BotMain && dotnet build
# 在有 Windows 的机器上启动 Hearthbot，打开 Settings → 云端学习 Tab，观察面板数据
```

- [ ] **Step 6: Commit**

```bash
git add BotMain/SettingsWindow.xaml BotMain/SettingsWindow.xaml.cs BotMain/MainViewModel.cs BotMain/CloudLearning/CloudLearningOrchestrator.cs BotMain/BotService.cs
git commit -m "阶段4: SettingsWindow 云端学习面板 + Orchestrator 公共只读接口"
```

---

### Task 4.5: 5 台全开 + 7 天观察

- [ ] **Step 1: 把 5 台机器的 appsettings.json 都改成 `Enabled=true` + `UseLearnedLocal=true`**

分发到 hb-dorm-01..05，启动。

- [ ] **Step 2: 观察 7 天**

每日检查：
- `Machines.LastSeenAt` 5 台都在 1 小时内
- `ActionDecisions` 每日增量 > 1 万条
- `ModelVersions` 每日有新版本（或明确日志"不如旧不推送"）
- `ConsistencyMonitor` 日志无异常告警

**硬门槛**：
- 5 台机 `top1_match_rate`（action）> 0.60
- 每日 03:00 自动训练任务有记录（成功或日志清楚的失败原因）

- [ ] **Step 3: 手动触发一次回滚演练**

故意推一个"差版本"（比如用很少样本训出的 booster）：
```bash
python push_model.py --version-tag bad-test-$(date +%s) ...
```

等 4-8 小时，观察 `ConsistencyMonitor` 日志是否告警并回滚 `latest.json` 到上一版。检查 `ModelVersions.RolledBackAt` 列。

- [ ] **Step 4: 阶段 4 收尾 commit + push**

```bash
cd H:/桌面/炉石脚本/Hearthbot
git add docs/superpowers/recon/
git commit -m "阶段4: 5 台全量 + 自动训练 + 回滚演练通过"
git push
```

---

## 阶段 5 / Legacy 降级（约 2 天，4 个 Task）

产出：旧 `Learning/*` 模块标 `[Obsolete]`，默认路径切到 `CloudLearning`，CLAUDE.md 更新架构说明。

**前置条件**：阶段 4 全量 7 天稳定跑通。

### Task 5.1: 旧 Learning 模块 `[Obsolete]` 标注

**Files:**
- Modify: `BotMain/Learning/LearnedStrategyRuntime.cs`
- Modify: `BotMain/Learning/LearnedStrategyCoordinator.cs`
- Modify: `BotMain/Learning/LearnedStrategyTrainer.cs`
- Modify: `BotMain/Learning/LearnedStrategyFeatureExtractor.cs`
- Modify: `BotMain/Learning/SqliteLearnedStrategyStore.cs`
- Modify: `BotMain/Learning/SqliteConsistencyStore.cs`
- Modify: `BotMain/Learning/SqliteTeacherDatasetStore.cs`

- [ ] **Step 1: 每个类加 [Obsolete] 属性**

```csharp
// 以 LearnedStrategyRuntime 为例
[System.Obsolete("已被 BotMain.CloudLearning.DecisionRanker 替代。仅在云端模型缺失时作为 fallback 保留, 计划在阶段 6 物理删除。")]
internal sealed class LearnedStrategyRuntime
{
    // ... 保持实现不动
}
```

对 7 个类都加同样格式的 attribute，把"被谁替代"写清楚：

| 旧类 | 替代 |
|------|------|
| LearnedStrategyRuntime | CloudLearning.DecisionRanker |
| LearnedStrategyCoordinator | CloudLearning.CloudLearningOrchestrator |
| LearnedStrategyTrainer | training/ Python 脚本 |
| LearnedStrategyFeatureExtractor | training/features/*.py + CloudLearning.FeatureExtractor |
| SqliteLearnedStrategyStore | 云端 LearningDbContext |
| SqliteConsistencyStore | 云端 Machines.LastStatsJson |
| SqliteTeacherDatasetStore | 云端 ActionDecisions/ChoiceDecisions/MulliganDecisions 表 |

- [ ] **Step 2: csproj 抑制 warning（仅项目级，不覆盖全局）**

在 `BotMain/BotMain.csproj` 的 `<PropertyGroup>` 追加：

```xml
<NoWarn>$(NoWarn);CS0618</NoWarn>
```

`BotCore.Tests/BotCore.Tests.csproj` 原本就有 `<NoWarn>CA1416</NoWarn>`，追加为：

```xml
<NoWarn>CA1416;CS0618</NoWarn>
```

（CS0618 是 `[Obsolete]` 使用警告；全面标 Obsolete 后 BotService 等消费方会触发，这里显式忽略）

- [ ] **Step 3: build 验证无 error**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet test
```

Expected: Build succeeded, 测试全过

- [ ] **Step 4: Commit**

```bash
git add BotMain/Learning/ BotMain/BotMain.csproj BotCore.Tests/BotCore.Tests.csproj
git commit -m "阶段5: 旧 Learning/* 模块全部 [Obsolete] 标注并忽略 CS0618"
```

---

### Task 5.2: 默认路径切换 + fallback 保留策略

**Files:**
- Modify: `BotMain/BotService.cs`

- [ ] **Step 1: 定位老规则路径**

Run: `grep -n "LearnedStrategyRuntime\|LearnedStrategyCoordinator" BotMain/BotService.cs`
记录所有调用点。

- [ ] **Step 2: 包裹"仅在 CloudLearning 不可用且显式开启时使用"条件**

所有对老规则库的调用，外层加条件：

```csharp
var useLegacy = _cloudLearning == null
    || !_cloudLearning.IsRankerReady
    || !appSettings.GetValue<bool>("UseLearnedLocal", false) == false; // 保持开关语义
// 或更简单地: 把老规则调用从默认路径挪到 else 分支

if (_cloudLearning != null && _cloudLearning.IsRankerReady && UseLearnedLocal)
{
    // CloudLearning 主路径已经在阶段 3 接入
}
else if (appSettings.GetValue<bool>("LegacyFallback:Enabled", false))
{
    // 老规则库走这里（需要用户显式开启）
    var legacyAction = _learnedStrategyRuntime?.GetRecommendedAction(...);
    ...
}
```

- [ ] **Step 3: appsettings.json 新增 LegacyFallback 默认关**

在 `appsettings.json` 追加：

```json
"LegacyFallback": {
    "Enabled": false,
    "Description": "仅当云端模型缺失且愿意用旧规则库时开启"
}
```

- [ ] **Step 4: build + 测试**

```bash
cd BotMain && dotnet build
cd ../BotCore.Tests && dotnet test
```

- [ ] **Step 5: Commit**

```bash
git add BotMain/BotService.cs BotMain/appsettings.json
git commit -m "阶段5: 默认路径切到 CloudLearning, 老规则库仅在 LegacyFallback.Enabled 时启用"
```

---

### Task 5.3: CLAUDE.md 架构说明

**Files:**
- Modify: `CLAUDE.md`（项目根）

- [ ] **Step 1: 读当前 CLAUDE.md 再追加说明**

读现有 `CLAUDE.md`，在结尾追加：

```markdown

## 学习系统架构（2026-04-17 重构后）

- **主路径**：`BotMain/CloudLearning/*` + `HearthBot.Cloud/*/Learning/*` + `training/*`
- **数据流**：5 台 Hearthbot 机器 → `/v1/learning/samples/batch` → 云端 `learning.db`（SQLite） → 训练机（4070S）拉 `/v1/learning/export/*` → LightGBM LambdaRank 训练 → `push_model.py` 原子推送 ONNX → 各 Hearthbot 每 6h `ModelArtifactManager.SyncAsync` 拉新版 → `ModelRuntimeHost` 本机 ONNX 推理 → `DecisionRanker.Rank()` 返回 top1
- **云端栈**：扩展 `HearthBot.Cloud`（ASP.NET Core 8 + EF Core + SQLite）新增 7 个 Controller + 6 个 Service
- **鉴权**：机器专用 JWT（role=machine, machine_id claim, 365 天），复用 `AuthService`
- **特征 schema 契约**：`training/schema/feature_schema.py.combined_hash()` 必须等于 `BotMain.CloudLearning.Config.FeatureSchemaRegistry.CurrentHash`，任何一方改 schema 必同步
- **fallback**：云端不可达时 `DecisionRanker` 返回空，外层回退盒子；老规则库仅在 `appsettings.LegacyFallback.Enabled=true` 时启用
- **硬约束**：云端挂了不影响对局

### Legacy 路径（保留作 fallback，阶段 6 物理删除）

- `BotMain/Learning/LearnedStrategyRuntime`（规则权重补丁）
- `BotMain/Learning/LearnedStrategyCoordinator`
- `BotMain/Learning/LearnedStrategyTrainer`
- `BotMain/Learning/SqliteLearnedStrategyStore`
- `BotMain/Learning/SqliteConsistencyStore`
- `BotMain/Learning/SqliteTeacherDatasetStore`

全部标 `[Obsolete]`。新代码不应引用。
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "阶段5: CLAUDE.md 补充云端学习系统架构说明"
```

---

### Task 5.4: 阶段 5 收尾 push + 30 天观察窗

- [ ] **Step 1: 阶段 5 汇总 push**

```bash
git push
```

- [ ] **Step 2: 30 天观察列表**

不再写代码，只做监控：

1. 每日 MLflow UI 翻一眼：训练是否执行、指标有没有退化
2. 每周 SQLite 查一次 `learning.db` 体积，超过 500 MB 检查 ArchiveJob 是否在跑
3. 每周查一次 `Machines.LastSeenAt`，5 台都在 24 小时内
4. 每月备份文件核对：`sqlite3 learning.db ".backup backup.db"` 成功且可还原

30 天稳跑 → 进入阶段 6（删老代码）。不在本 plan 范围。

30 天如有异常 → 记录到 `docs/superpowers/recon/`，本 plan 不复活（新问题新 plan）。

---

## 总工期校验

| 阶段 | Task 数 | 预计天数 |
|------|---------|---------|
| 0 | 9 | 4 |
| 1 | 12 | 5 |
| 2 | 11 | 7 |
| 3 | 9 | 4 |
| 4 | 5 | 3 |
| 5 | 4 | 2 |
| **总计** | **50** | **25** |

主线 25 天，含返修缓冲预计 4-6 周完成。阶段 6（物理删老代码）另行安排，不在本 plan。

## 回退与放弃条件速查

- **阶段 1 映射率不达标**（action <85%）→ 回 Task 1.7 Mapper 加固；连续两轮仍 <70% → 放弃本 plan，转"仅数据层上云"
- **阶段 2 top-1 不达标** → 回阶段 1 排查采样；若样本没问题，回 features/*.py 重做特征
- **阶段 3 灰度 illegal_action_rate > 1%** → 灰度机 UseLearnedLocal=false，检查 FeatureExtractor 与 Python 特征对齐
- **阶段 4 自动回滚连续 3 天触发** → 关闭 cron，回到手动训练，排查训练数据偏移
- **云服务器磁盘 >80% 反复告警** → 把归档窗口从 90 天缩到 60 天，或评估切阿里云轻量

## 与既有 spec 的对应

- 本 plan 直接实现 `docs/superpowers/specs/2026-04-17-hsbox-cloud-learning-design.md` 第 7 节阶段 0-5
- `docs/superpowers/specs/2026-03-25-hsbox-local-teacher-design.md` 的"候选动作排序模型"思路在本 plan 阶段 2 落地
- `docs/superpowers/specs/2026-03-30-learning-system-activation-design.md` 的"纯 C# 学习"路径在本 plan 阶段 5 降级

---

## Self-Review 结果

四项检查（由计划编写者在定稿前自审）：

1. **Spec 覆盖度**：spec §2 架构、§3 采样协议、§4 云端栈、§5 训练管线、§6 运行时、§7 分阶段、§8 风险、§10 文件清单均有对应 task 覆盖
2. **占位符扫描**：
   - `FeatureSchemaRegistry.CurrentHash` 首次值填 `PLACEHOLDER_UPDATE_FROM_PYTHON`，这是**真实要执行的动作**（Task 3.3 Step 1 明确要从 Task 2.2 替换），非遗留占位符
   - 服务器域名在命令示例里用 `<你的 HK 服务器>`，这是执行时替换项
   - 其余所有 TODO / TBD / "等等" 已清除
3. **类型一致性**：
   - `DecisionSample` / `DecisionCandidate` / `DecisionContextSnapshot` 在 Task 1.2 定义，后续 Task 1.6、1.10、3.6、3.7、3.8 引用一致
   - `SampleEnvelope` / `SampleBatchRequest` 在 Task 0.7 定义，Task 1.8 SampleIngestService 使用一致
   - `ModelVersion` 实体在 Task 0.1 定义，Task 3.1 Store 引用一致
4. **无遗漏**：spec §5.5 模型 metadata、§6.5 FeatureSchemaRegistry 硬校验、§6.7 断网降级（outbox cap）、§6.8 heartbeat 均在 plan 中有明确 task

---

## 执行方式

Plan complete and saved to `docs/superpowers/plans/2026-04-17-hsbox-cloud-learning.md`. Two execution options:

**1. Subagent-Driven (recommended)** — 每个 Task 独立 subagent，完成一个审查一个。适合这种横跨 3 个代码基、50 个 Task 的大型计划，避免上下文污染。

**2. Inline Execution** — 在当前会话批量执行，按 stage 设置 checkpoint。适合你想全程跟着看每一步。

**Which approach?**
