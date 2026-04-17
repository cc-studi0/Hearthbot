# HSBox 学习系统云端化与排序模型升级设计

日期：2026-04-17

## 1. 背景

### 1.1 现状

仓库 `BotMain/Learning/` 已有一套本地学习系统：

- `TeacherDatasetRecorder` + `SqliteTeacherDatasetStore` 采样到本地 `Data/HsBoxTeacher/dataset.db`
- `LearnedStrategyRuntime` 基于"规则权重补丁"做推荐
- `SqliteConsistencyStore` 统计一致率
- `LearnedStrategyTrainer` 做聚合与权重增量

该系统存在两个结构性限制：

1. **数据本地化**：每台 Hearthbot 各跑一份 SQLite，样本互不共享
2. **算法天花板低**：规则权重补丁本质是聚合后的粗粒度经验，按 2026-03-25 spec 的分析，样本再多也学不出泛化打法

### 1.2 触发诉求

用户有 5 台机器同时跑 Hearthbot，希望：

- 数据上云，多机一起累积样本
- 算法同步升级，让"多机多数据"的价值真正兑现
- 云服务器配置不高（4H4G 香港），不能承担训练计算
- 用户训练机为 4070S，算力充足

### 1.3 非目标

- 第一版不做云端实时推理（所有 Hearthbot 本机 ONNX 推理）
- 第一版不做端到端深度模型（LightGBM LambdaRank 是基线）
- 第一版不重建 2026-03-25 spec 未完成的候选枚举逻辑（直接复用即可）
- 不摆脱 HSBox 登录体系，不碰 Hearthstone.exe 内存

## 2. 总体架构

```
┌──────────────── 云端（4H4G 香港） ────────────────────┐
│                                                        │
│   ┌─────────┐      ┌───────────────┐    ┌──────────┐  │
│   │ Caddy   │ ◀──▶ │ Hearthbot API │───▶│PostgreSQL│  │
│   │ (TLS)   │      │ (Go 单二进制)  │    │ (样本+索引)│  │
│   └─────────┘      └───────────────┘    └──────────┘  │
│         │                  │                  │         │
│         │              ┌───▼────┐             │         │
│         │              │ 磁盘   │◀──── 原始   │         │
│         │              │ /data  │   JSONL    │         │
│         │              │ /models│   模型      │         │
│         │              └────────┘             │         │
└─────────┼───────────────┼────────────────────┼─────────┘
          │               │                    │
          │HTTPS上传样本    │HTTPS下载模型       │训练机pull
          │               │                    │
    ┌─────┴─────┐    ┌────┴─────┐       ┌──────▼──────┐
    │Hearthbot×5│    │Hearthbot │       │训练机(4070S) │
    │ 各机采样    │    │ 拉模型   │       │ 拉数据+训练 │
    │ 本地推理    │    │ 本地推理 │       │ 推送新ONNX   │
    └───────────┘    └──────────┘       └─────────────┘
```

### 2.1 三个角色

| 角色 | 职责 | 不做什么 |
|------|------|----------|
| Hearthbot × 5（采样节点） | 采集决策样本 → 本地 SQLite 缓冲 → 批量上传；拉最新模型做本地推理 | 不训练、不做云端 I/O 强依赖（断网继续跑） |
| 云端服务器（存储/分发中心） | 接收样本入库；托管模型文件；提供数据查询/下载 API；健康监控 | 不训练、不做实时推理 |
| 训练机 × 1（4070S） | 定时/手动拉最近数据 → 训练 LightGBM → 导出 ONNX → 推送云端；保留训练集与评估报告 | 不采样（用户挂 Hearthbot 一起采也可，非必须） |

### 2.2 关键设计约束

1. **云端不可达，5 台机器照跑**：本地 SQLite 作缓冲队列，离线累积，回线后补传
2. **云端不做实时推理**：所有推理本机 ONNX，云端挂了推理不受影响
3. **新机器加入不拖全库**：启动只拉最新模型，不拉历史数据
4. **训练和运行时完全解耦**：训练 Python，运行时 C#，通过 ONNX 契约对齐
5. **单数据源原则**：所有样本入云端同一张表，通过 `sample_id` 幂等

## 3. 样本采集与上传协议

### 3.1 三类决策点

延用 2026-03-25 spec 的三类决策（**Action / Choice / Mulligan**），每类样本加 `machine_id` 字段区分来源。

| 类型 | 触发时机 | 样本单位 |
|------|----------|----------|
| Action | 盒子返回"下一步最优动作"时 | 一条 `action_decision` + N 条 `action_candidates` |
| Choice | 发现/抉择弹窗弹出 | 一条 `choice_decision` + N 条 `choice_options` |
| Mulligan | 起手弹窗 | 一条 `mulligan_decision` + N 条 `mulligan_cards` |

### 3.2 样本字段（以 Action 为例）

```json
{
  "sample_id":    "<machine_id>_<uuid>",
  "machine_id":   "hb-dorm-01",
  "match_id":     "<machine_id>_<uuid>",
  "decision_type":"action",
  "turn":         8,
  "step_index":   2,
  "seed":         1234567890,
  "payload_sig":  "...",
  "board_snapshot": {...},
  "candidates":   [{...}, {...}, ...],
  "teacher_pick": {"candidate_id":3, "raw_command":"PLAY|EX1_012|0|2"},
  "mapping_status":"matched|fuzzy|failed",
  "local_pick":   {"candidate_id":1},
  "created_at_ms":1713345678000
}
```

`local_pick` 通过异步跑一次**老规则库 AI**（`LearnedStrategyRuntime`）得到，不阻塞主循环。这个字段仅为"训练期对照数据"用，阶段 5 老规则库降级后此字段写入 `null`，训练忽略。

**`sample_id` 与 `decision_id` 关系**：
- `sample_id` 是上传协议级幂等键（客户端生成，`<machine_id>_<uuid>`）
- `decision_id` 是云端 PostgreSQL 表主键（由 PG 生成 `BIGSERIAL`）
- 云端落库时 `action_decisions` 表新增一列 `client_sample_id UNIQUE`，存 `sample_id`；重复上传按此列冲突处理

### 3.3 本地缓冲

每台机器独立 SQLite：`Data/HsBoxTeacher/outbox.db`

三张表：
- `pending_samples`（未上传，WAL 模式）
- `match_outcomes`（对局结束后补录结果）
- `upload_cursor`（最后成功上传的游标）

**大小控制**：上限任一触达即触发淘汰（**7 天到期** **或** **总量 200 MB**，OR 语义，先到先丢最旧），避免长期断网撑爆磁盘。

### 3.4 上传协议

| 项目 | 规格 |
|------|------|
| 触发 | 每局结束 + 每 5 分钟兜底，两定时器并行 |
| 方式 | HTTPS POST `/v1/samples/batch` |
| 格式 | `gzip(JSON-Lines)`，一次最多 200 条 / 1 MB |
| 鉴权 | 每台机器预配静态 Bearer Token（5 台 = 5 个 token，手工发放） |
| 幂等 | 云端按 `sample_id` 唯一索引，重复提交返回 200 OK + 已存在列表 |
| 重试 | 指数退避，间隔从 1s 起每次倍增（1→2→4→...），上限 5min 封顶；失败项不出队列，无限重试 |

### 3.5 对局结束补录

一局结束后发一个 PATCH `/v1/matches/{match_id}/outcome`，带胜负、终局回合数、排名变化。
有无 outcome 云端均收；训练时优先用有 outcome 的样本。

### 3.6 映射失败样本也上传

`mapping_status=failed` 的样本**不丢**，照常上传。训练时跳过，但保留用于统计"映射失败率"——这是项目最大风险指标。

## 4. 云端服务器栈与存储

### 4.1 技术选型

| 组件 | 选型 | 内存占用 | 理由 |
|------|------|----------|------|
| 反向代理 + TLS | Caddy 2 | ~50 MB | 自动 Let's Encrypt，配置最简 |
| API 服务 | Go 单二进制 | ~150 MB | 冷启动快、内存稳，无运行时依赖 |
| 数据库 | PostgreSQL 16 | ~600 MB（`shared_buffers=256MB`） | JSONB + GIN 索引灵活 |
| 进程守护 | systemd | 0 | 不引入 Docker |
| 日志 | journalctl + logrotate | 0 | 不装 ELK |

**不用 Docker 的理由**：4G 内存跑容器栈浪费 ~500MB，规模 5 台机器不需要编排能力。

### 4.2 资源预算（4GB 切分）

```
PostgreSQL         600 MB
API 服务（Go）      150 MB
Caddy              50 MB
系统内核+缓冲       500 MB
────────────────────────
固定占用           1.3 GB
余量（PG 缓存峰值） 2.7 GB
```

### 4.3 磁盘布局（按 40GB 系统盘）

```
/var/lib/postgresql/           # PG 数据（结构化样本表）
/var/hearthbot/raw/            # 原始 JSONL 按日期分片（归档）
  2026-04/17.jsonl.gz
/var/hearthbot/models/         # ONNX 模型版本
  action-v20260417-0300.onnx
  choice-v20260417-0300.onnx
  mulligan-v20260417-0300.onnx
  latest.json                  # {"action":"v20260417-0300", ...}
/var/hearthbot/reports/        # 训练评估报告 JSON
/etc/caddy/Caddyfile
/etc/hearthbot/config.yaml
```

### 4.4 数据库 Schema

九张核心表：

- `machines(machine_id, token_hash, created_at, last_seen_at)`
- `matches(match_id, machine_id, deck_signature, mode, start_at, end_at, outcome)`
- `action_decisions(decision_id, match_id, turn, step, seed, payload_sig, board_snapshot JSONB, teacher_candidate_id, mapping_status, local_pick_id, created_at)`
- `action_candidates(candidate_id, decision_id, action_command, action_type, features JSONB, is_teacher_pick, is_local_pick)`
- `choice_decisions` / `choice_options`（同构于 action）
- `mulligan_decisions` / `mulligan_cards`（同构于 action）
- `model_versions(version, model_type, sha256, trained_at, metrics JSONB, prev_version, feature_schema_hash)`

JSONB + GIN 索引允许后期特征调整不改 schema。

### 4.5 数据保留与归档

| 层 | 保留 | 超期处理 |
|----|------|----------|
| 结构化表（PG） | 90 天 | 按月打包 `raw/YYYY-MM.jsonl.gz` → 删库行 |
| 冷归档（磁盘） | 磁盘用量 <80% 时不动 | 超 80% 手动拉到训练机，云端清最旧 |
| 模型文件 | 云端最近 10 版 | 自动清旧 |
| 备份 | `pg_dump` 每日凌晨 1 点 | 保留最近 7 份 |

### 4.6 监控

- `/healthz`：API + PG 连接健康
- 训练机每次拉数据前 ping，异常发邮件/Telegram 给用户
- 不引入 Prometheus/Grafana

## 5. 训练管线（训练机 / 4070S）

### 5.1 技术栈

| 环节 | 选型 | 理由 |
|------|------|------|
| 数据拉取 | Python + `psycopg`（TLS 直连 PG） | 省一层 API |
| 特征工程 | `pandas` + `pyarrow` | 列式，百万级样本毫秒级处理 |
| 主排序模型 | **LightGBM LambdaRank**（`objective=lambdarank`） | 表格排序业界首选 |
| 后备深度模型 | PyTorch（样本 >500 万才考虑） | 升级预留，第一版不用 |
| ONNX 导出 | `onnxmltools` + `skl2onnx` | C# ONNX Runtime 直接跑 |
| 实验管理 | MLflow 本地（单文件存储） | 记录每次训练超参、指标、模型版本 |
| 调度 | Windows Task Scheduler（训练机是 Windows） | `schtasks` 简单 |

### 5.2 训练管线（每次训练执行）

**Step 1 拉数据**

```sql
SELECT * FROM action_decisions JOIN action_candidates
WHERE created_at > last_trained_at AND mapping_status='matched';
```

落地 `data/raw-YYYYMMDD.parquet`

**Step 2 特征抽取 + 切分**

- 按 `match_id` 切分（同局不能跨 train/val/test）
- train : val : test = 7 : 2 : 1
- 每条样本 ~60 维特征，遵循 2026-03-25 spec 的特征表

**Step 3 训练**

- `objective=lambdarank, metric=ndcg@1`
- early_stopping on val NDCG@1
- 5-fold CV + `optuna` 20 trials 超参搜索
- 产出 booster + 特征重要性报告

**Step 4 离线评估（硬门槛）**

在 test 集计算：
- top-1 命中率（模型 top1 == 老师 pick）
- top-3 命中率
- 按动作类型/职业/回合段分组命中

**硬门槛**：top-1 必须 ≥ 上一版模型的 top-1。不达标不推送，日志入 `reports/`，发警报。

**Step 5 回放评估（软门槛）**

最近 7 天真实对局重放：
- 老师推荐 vs 新模型推荐分歧率
- 分歧点人工抽查 10 条（HTML 报告）

**Step 6 ONNX 导出 + 自测**

- LightGBM → ONNX
- 训练机装 .NET SDK，用 ONNX Runtime 跑 100 条测试样本
- Python 输出与 ONNX 输出差异 >1e-5 报错

**Step 7 推送云端**

- `POST /v1/models/upload`，带 metadata
- 云端原子替换 `latest.json` 指针

**Step 8 冷却观察期**

- 推送后 Hearthbot 6h 内拉新模型
- 上线 24h 监控各机 `top1_match_rate`
- 24h 平均一致率比上一版掉 >3pp 自动回滚
- 用户邮箱/Telegram 收到通知

### 5.3 三个模型的并行训练策略

训练管线内部三条独立（action/choice/mulligan），一个训崩不影响另两个。

**但云端发布是原子三连推**：`latest.json` 一次更新三个字段，要么整批落地要么整批作废。这一约束与 Hearthbot 侧"UI 开关不拆三子项、学就三个一起学"的诉求对齐。

### 5.4 调度

- 每天凌晨 **03:00** 自动触发
- 手动入口：`python train.py --all --push`
- 跳过条件：距上次训练新增样本 <1000 条

### 5.5 模型元数据

每个模型附带 JSON：

```json
{
  "version": "action-v20260417-0300",
  "trained_at": "2026-04-17T03:00:00Z",
  "sample_count": 47832,
  "feature_schema_hash": "sha256:abc...",
  "metrics": {
    "test_top1": 0.732,
    "test_top3": 0.891,
    "test_by_turn": {"early": 0.78, "mid": 0.71, "late": 0.69}
  },
  "prev_version": "action-v20260416-0300",
  "trained_by": "machine-4070s"
}
```

`feature_schema_hash` 是 C# 运行时加载时的硬校验值，不匹配拒绝加载。

### 5.6 失败自救

| 失败 | 处理 |
|------|------|
| 拉数据失败 | 重试 3 次 → 放弃本次，发警报 |
| 训练 NaN/异常 | 记日志，跳过推送 |
| ONNX 自测失败 | 不推送，线上模型不变 |
| 上线后 24h 回滚 | 自动回到 `prev_version` |

### 5.7 长期升级路径（第一版不做）

- 样本 >100 万：对比 CatBoost
- 样本 >500 万：尝试 Transformer 排序（4070S 够跑）
- 仅预留口子，不在第一版实施

## 6. Hearthbot 运行时（C# 侧）

### 6.1 模块布局

```
BotMain/
├── Learning/                      # 现有，保留作 legacy fallback
└── CloudLearning/                 # 新增
    ├── CloudLearningOrchestrator.cs
    ├── SampleOutboxStore.cs
    ├── SampleUploader.cs
    ├── ModelArtifactManager.cs
    ├── ModelRuntimeHost.cs
    ├── DecisionRanker.cs
    ├── FeatureExtractor.cs
    ├── CandidateGenerationService.cs
    ├── CloudHealthMonitor.cs
    └── Config/
        ├── CloudLearningOptions.cs
        └── FeatureSchemaRegistry.cs
```

### 6.2 决策数据流

```
BotService.RequestRecommendationAsync:
  1. 盒子返回推荐                              ← 教师
  2. CandidateGenerationService.Enumerate()  ← 枚举合法候选
  3. DecisionRanker.RankAsync(ctx, candidates)
        ├─ 本地模型可用 + schema 匹配 → ONNX 打分 → top1
        └─ 否则 → 回退盒子/老规则库
  4. 执行选中动作
  5. SampleOutboxStore.Enqueue(...)  ← 样本入队（非阻塞）

后台线程：
  · SampleUploader 每 5min / 每局结束 → 批量上传
  · ModelArtifactManager 每 6h → 查 latest.json
  · CloudHealthMonitor 每 10min → 上报本机指标
```

### 6.3 模型加载优先级与就绪定义

**每类模型单独加载**（按优先级自上而下）：
1. `Data/HsBoxTeacher/models/current/{action,choice,mulligan}.onnx` ← 云端下发最新版
2. `Data/HsBoxTeacher/models/current/{action,choice,mulligan}-fallback.onnx` ← 上一稳定版（自救用）
3. 均缺失 → 该类模型判为"未就绪"

**IsRankerReady 定义**：三类模型**均处于第 1 或第 2 层**才算就绪。任一类掉到第 3 层（无任何可用模型）→ `IsRankerReady=false` → 整个 `DecisionRanker` 对外返回空 → 外层三类决策都回退到盒子/老规则库。

理由："要学就都学" → 不允许"action 走排序模型、mulligan 走老规则"这种混合状态，否则打法一致性难保证。

### 6.4 热更新流程

```
下载 → .staging/*.onnx.tmp
  ↓
校验 metadata.feature_schema_hash == FeatureSchemaRegistry.CurrentHash
  ↓ 不匹配：拒绝，记警告（"该更新 Hearthbot 了"）
  ↓ 匹配：
校验 sha256
  ↓
InferenceSession 预热（5 条测试输入）
  ↓
原子替换 .staging/*.onnx → current/*.onnx
  ↓
ModelRuntimeHost.Reload()，下次决策用新模型
```

进行中的决策不会被中途切换模型。

### 6.5 FeatureSchemaRegistry

```csharp
public static class FeatureSchemaRegistry
{
    public const string CurrentHash = "sha256:a3f2...";
    public static readonly FeatureSchema ActionSchema = new(
        version: "action-v3",
        features: [
            new FeatureDef("mana_ratio",     FeatureType.Float, index: 0),
            new FeatureDef("friend_minions", FeatureType.Int,   index: 1),
            // ...
        ]);
}
```

训练侧 Python 导出 ONNX 时把同一 hash 写入 metadata；运行时加载必须匹配。**任何一方改特征都必须同步改 hash**。

### 6.6 BotService 挂钩（最小侵入）

构造器新增：
```csharp
_cloudLearning = new CloudLearningOrchestrator(cloudOptions, Log);
_cloudLearning.Start();
```

`RequestRecommendationAsync` 新增：
```csharp
if (_cloudLearning.IsRankerReady && UseLearnedLocal)
{
    var ranked = await _cloudLearning.RankAsync(ctx, candidates);
    if (ranked.HasValue) finalAction = ranked.Value;
}
_cloudLearning.Record(ctx, candidates, teacherPick, localPick);
```

`IsRankerReady` 仅在三类模型（action/choice/mulligan）均就绪时返回 true——**三个一起学，缺一不点亮**。

老模块 `LearnedStrategyRuntime / LearnedStrategyCoordinator` 整体保留作 fallback，阶段 5 才标记 `[Obsolete]`。

### 6.7 断网降级

| 云端状态 | Hearthbot 行为 | 用户感知 |
|----------|----------------|----------|
| 正常 | 样本实时上传，模型定期更新 | 无 |
| 上传 API 不可达 | outbox 累积，指数退避重试 | 无 |
| 模型下载不可达 | 继续用本地 `current/*.onnx` | 无 |
| 首次启动 + 云端不可达 | 模型缺失，`DecisionRanker` 空返 → 回退盒子 | 退化为老行为 |
| outbox 满 200MB | 丢最旧，日志警告 | 数据损失，不影响打牌 |

**硬约束**：云端问题绝不中断对局。

### 6.8 观测上报（CloudHealthMonitor）

每 10min POST `/v1/heartbeat`：

```json
{
  "machine_id": "hb-dorm-01",
  "hb_version": "1.2.3",
  "model_versions": {"action":"v20260417-0300", "choice":"...", "mulligan":"..."},
  "outbox_depth": 42,
  "last_upload_ok_at": 1713345678000,
  "rolling_stats_24h": {
    "decisions": 5823,
    "top1_match_rate": 0.74,
    "mapping_fail_rate": 0.03,
    "illegal_action_rate": 0.001
  }
}
```

云端 `machines.last_seen_at` 更新 + 历史指标累积，支持 SQL 查询"哪台机器异常"。

### 6.9 appsettings.json 新增

```json
{
  "CloudLearning": {
    "Enabled": true,
    "MachineId": "hb-dorm-01",
    "ServerBaseUrl": "https://hearthbot.your-hk-server.com",
    "Token": "<per-machine-bearer-token>",
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
}
```

`UseLearnedLocal` 保持原开关位置（MainViewModel），语义不变：开启即走 `DecisionRanker`（需三类模型均就绪）。

### 6.10 UI 面板（SettingsWindow 新增 Cloud Learning）

显示内容：
- Machine ID（只读）
- Server 连接状态（实时绿/红灯）
- 三个模型的当前版本 + 上次更新时间
- 本地 outbox 深度 + 最近一次上传时间
- "Upload Now" 手动触发按钮（立即 flush 当前 outbox，不等 5min 兜底定时器；debug 用）

### 6.11 测试策略

**单元测试**（`BotCore.Tests/CloudLearning/`）：
- `SampleOutboxStoreTests`：入队、出队、大小上限、幂等
- `ModelArtifactManagerTests`：下载、校验、原子替换、版本回滚
- `FeatureExtractorTests`：特征向量稳定性（与 Python 黄金样本 byte-for-byte 对比）
- `DecisionRankerTests`：模型缺失 / schema 不匹配 / 推理异常的兜底行为

**集成测试**（人工）：5 台机器跑 1 小时 → 云端样本数 → 训练机拉数据训一次 → 推送 → 各机 6h 拉到新模型 → 一致率上报完整链路

## 7. 分阶段落地

### 阶段 0 / 云端基建（约 4 天）

**交付**：
- Caddy + Go API + PostgreSQL + systemd 部署完成
- Let's Encrypt 自动证书
- `POST /v1/samples/batch`（收到即 OK，暂不入库）、`POST /v1/heartbeat`、`/healthz`
- 5 个预分配 Bearer Token

**验收**：
- `curl https://.../healthz` 返 200
- 错误 token 返 401
- 正确 token 返 200

**回退**：未接 Hearthbot，关服务器即等于没发生

### 阶段 1 / 5 台机器全量采样（约 5 天）

**交付**：
- `CloudLearning/SampleOutboxStore + SampleUploader + CloudHealthMonitor`
- `TeacherActionMapper` 加固（Ⅱ 路线最大风险专项）
- 云端 API 补全入库 + `machines.last_seen_at`
- 此阶段 `Use Learned Local` 仍走老规则库

**验收（硬门槛）**：
- 映射成功率 `mapping_status='matched'`：**action ≥ 85% / choice ≥ 90% / mulligan ≥ 95%**
- 5 台机器 `last_seen_at` 均在 1 小时内
- outbox 深度稳定不爆炸
- 断网演练：断 30min 后补传零丢失

**不达标**：回阶段 1 排查，不进阶段 2

**回退**：`CloudLearning.Enabled=false`

### 阶段 2 / 训练机管线 + 离线评估（约 7 天）

**交付**：
- 4070S 装 Python + LightGBM + onnxmltools + MLflow
- 三条训练管线（action/choice/mulligan）
- Feature Schema 第一版定型（编译期常量写入 C#）
- 产出三个 ONNX + HTML 评估报告
- **不推送云端，仅本地训**

**验收（硬门槛）**：
- top-1 命中率：**action ≥ 0.55 / choice ≥ 0.60 / mulligan ≥ 0.65**
- ONNX 自测：Python vs C# ONNX Runtime 差异 <1e-5
- 报告含职业/回合段分组命中率分布

**不达标**：回阶段 1 排查映射/采样/特征

**回退**：产出仅本地，云端与 Hearthbot 零影响

### 阶段 3 / 云端模型分发 + 灰度 1 台机器（约 4 天）

**交付**：
- 云端 API 补 `POST /v1/models/upload` + `GET /v1/models/latest`
- Hearthbot 侧 `ModelArtifactManager + ModelRuntimeHost + DecisionRanker`
- 手动推送第一版 **三个 ONNX 一起**（action/choice/mulligan），`latest.json` 一次更新三个字段
- 只开 1 台机器的 `Use Learned Local`（该机三类决策全走 ranker）

**验收**（三类均要测）：
- 灰度机 `illegal_action_rate` < 1%（动作执行被游戏拒的比例）
- `action.top1_match_rate` > 0.55
- `choice.top1_match_rate` > 0.60
- `mulligan.top1_match_rate` > 0.65
- 对局能打完、起手能留、发现能选
- 其余 4 台不受影响

**回退**：灰度机 `UseLearnedLocal=false`

### 阶段 4 / 全量放量 + 自动训练（约 3 天）

**交付**：
- 训练机加 cron（每天 03:00）
- 云端加 24h 一致率监控 + 自动回滚
- 5 台全部 `Use Learned Local`
- SettingsWindow Cloud Learning 面板

**验收**：
- 连续 7 天每天有新模型或明确日志说明"不如旧不推送"
- 5 台 `top1_match_rate` > 0.60
- 至少 1 次自动回滚演练（可手动推差模型触发）

**回退**：单机 `UseLearnedLocal=false` 或全局 `CloudLearning.Enabled=false`

### 阶段 5 / Legacy 降级（约 2 天）

**交付**：
- `LearnedStrategyRuntime / LearnedStrategyCoordinator / SqliteLearnedStrategyStore` 标记 `[Obsolete]`
- 默认路径切到 `CloudLearning`
- 老规则库仅在"云端模型缺失 + 用户显式配置"时启用
- `CLAUDE.md` 补充新架构说明

**验收**：老代码路径编译 warning 清零或明确忽略

**回退**：老代码仅 `[Obsolete]` 未删，注解还原即可

### 阶段 6 / 老代码物理删除（可选，阶段 5 稳跑 30 天后）

减少阅读负担，避免误用。不计入主工期。

### 总工期

`4 + 5 + 7 + 4 + 3 + 2 = 25 天` ≈ 4 周主线。
含返修缓冲，实际完成 **4-6 周**。阶段 6 不计入。

## 8. 风险矩阵

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| HSBox → 本地候选映射率不达标（<85%） | 中 | 高（后续训练全沙塔） | 阶段 1 硬门槛；`TeacherActionMapper` 专项加固；失败样本全量落库便于分析 |
| 模型过拟合某些卡组 | 中 | 中 | 样本按 `match_id` 切分；加 `deck_signature/class` 条件特征；第一版只做通用模型 |
| 云服务器磁盘 80% 告警频繁 | 中 | 中 | 90 天归档策略；手动 rsync 到训练机冷存 |
| 特征 schema 升级需要重抽历史样本 | 高（每几个月发生） | 低-中 | 原始 `board_snapshot` JSONB 完整保留；训练前 Python 基于新 schema 从 JSONB 重抽特征即可，老数据不丢只是需要多跑一次 |
| HK 服务器到国内机器网络抖动 | 中 | 低 | outbox 缓冲 + 指数退避；断网 30min 不丢；5min 兜底上传 |
| ONNX 跨版本兼容问题 | 低 | 高 | ONNX opset 固定；导出后 C# 自测；不匹配不推送 |
| 新模型上线胜率骤降 | 中 | 高 | 24h 一致率监控 + 自动回滚；最近 10 版云端保留 |
| PostgreSQL 单点故障 | 低 | 高 | 每日 pg_dump；rsync 到训练机作二级备份 |
| Bearer Token 泄露 | 低 | 中 | 单 token 吊销 API；5 台规模可手工换发 |
| 训练机 4070S 宕机 | 低 | 低 | 云端样本继续累积；训练恢复后批量补训；期间 Hearthbot 用旧模型 |

## 9. 放弃条件

- 阶段 1 映射率连续两轮加固后仍不达标（例：action <70%），说明候选枚举/动作字符串架构有根本偏差 → 暂停本方案，转"仅数据层上云"（路线 Ⅰ 降级）
- 阶段 2 三模型任一 top-1 低于"仅靠热门 action 猜"的 baseline → 说明特征设计不足 → 暂停，回到 2026-03-25 spec 的特征表重做
- 云服务器成本/稳定性不可接受 → 评估切换国内云（腾讯云轻量 2C2G 够跑但内存紧）
- 阶段 4 自动回滚机制反复触发（连续 3 天无新模型成功上线）→ 说明训练数据偏移严重或评估门槛错 → 暂停自动推送，回到手动模式

## 10. 文件清单

### 新增

**云端**（独立仓库或子目录，部署工件）：
- `cloud/server/main.go`（API 入口）
- `cloud/server/handlers/*.go`（samples / models / heartbeat / health）
- `cloud/server/store/postgres.go`
- `cloud/server/config.go`
- `cloud/sql/migrations/001_init.sql`
- `cloud/Caddyfile`
- `cloud/systemd/hearthbot-api.service`
- `cloud/systemd/hearthbot-api.env.example`
- `cloud/scripts/deploy.sh`
- `cloud/scripts/backup.sh`

**训练机**（Python）：
- `training/pull_data.py`
- `training/features.py`
- `training/train_action.py`
- `training/train_choice.py`
- `training/train_mulligan.py`
- `training/export_onnx.py`
- `training/validate_onnx.py`（调 C# `dotnet run` 跑对比）
- `training/push_model.py`
- `training/eval_replay.py`
- `training/requirements.txt`
- `training/schedule.xml`（Windows Task Scheduler 导入）

**Hearthbot C# 侧**（`BotMain/CloudLearning/`）：
- `CloudLearningOrchestrator.cs`
- `SampleOutboxStore.cs`
- `SampleUploader.cs`
- `ModelArtifactManager.cs`
- `ModelRuntimeHost.cs`
- `DecisionRanker.cs`
- `FeatureExtractor.cs`
- `CandidateGenerationService.cs`（若 2026-03-25 spec 未实施则在此落地）
- `CloudHealthMonitor.cs`
- `Config/CloudLearningOptions.cs`
- `Config/FeatureSchemaRegistry.cs`

**测试**（`BotCore.Tests/CloudLearning/`）：
- `SampleOutboxStoreTests.cs`
- `ModelArtifactManagerTests.cs`
- `FeatureExtractorTests.cs`
- `DecisionRankerTests.cs`

### 修改

- `BotMain/BotService.cs`（构造器 + `RequestRecommendationAsync` 挂钩）
- `BotMain/SettingsWindow.xaml` + `.xaml.cs`（新增 Cloud Learning 面板）
- `BotMain/MainViewModel.cs`（面板绑定）
- `BotMain/appsettings.json`（新增 `CloudLearning` 段）
- `BotMain/Learning/*`（阶段 5 整体 `[Obsolete]` 标注，不删）

### 阶段 6 删除（可选）

- `BotMain/Learning/LearnedStrategyRuntime.cs`
- `BotMain/Learning/LearnedStrategyCoordinator.cs`
- `BotMain/Learning/LearnedStrategyTrainer.cs`
- `BotMain/Learning/LearnedStrategyFeatureExtractor.cs`
- `BotMain/Learning/SqliteLearnedStrategyStore.cs`

## 11. 与既有 spec 的关系

- **2026-03-25 `hsbox-local-teacher-design`**：本 spec 的"排序模型"思路延续此设计；本 spec 在其基础上叠加云端层。两者互补非替代
- **2026-03-30 `learning-system-activation-design`**：该 spec 提出"纯 C# 学习管线"，本 spec 实质性放弃此路径（改用 Python 训练 + ONNX）。阶段 5 老代码 `[Obsolete]` 正是降级该 spec 的实现
- **2026-04-17 `hsbox-standard-legend-bypass-design`**：本 spec 与此独立；受限绕过解决"拿不到老师数据"，本 spec 解决"拿到数据后怎么用"。长期目标一致（不依赖盒子也能打），路径互补

## 12. 当前结论

- 目标：5 台机器共享学习数据，算法从规则补丁升级为排序模型
- 架构：云端存储分发 / 训练机本地训 / Hearthbot 本机 ONNX 推理
- 主力算法：LightGBM LambdaRank → ONNX
- 工期：4-6 周主线，阶段 6 可延后
- 硬门槛：映射率 / 离线 top-1 / 运行时一致率三道防线串联
- 硬约束：云端挂了不影响对局
