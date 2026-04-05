# 项目全面审查实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 Hearthbot 项目进行 6 模块深度审查，产出按 P0-P3 优先级排序的技术债问题清单。

**Architecture:** 6 个审查任务并行执行（每个由独立子 agent 完成），第 7 个任务汇总所有发现并生成最终报告。每个审查任务读取指定模块的所有源文件，按照统一的分级标准记录问题。

**Tech Stack:** C# (.NET 8 / .NET 4.7.2), WPF, BepInEx, ASP.NET Core, Vue 3, PowerShell

---

### Task 1: 审查 BotService 核心循环

**Files (只读):**
- `BotMain/BotService.cs`
- `BotMain/BotProtocol.cs`
- `BotMain/MatchFlowState.cs`
- `BotMain/PipeServer.cs`
- `BotMain/ProfileLoader.cs`
- `BotMain/GameRecommendationProvider.cs`
- `BotMain/HsBoxRecommendationProvider.cs`
- `BotMain/ActionStringParser.cs`
- `BotMain/BotApiHandler.cs`
- `BotMain/AccountController.cs`
- `BotMain/AccountEntry.cs`
- `BotMain/AccountQueuePersistence.cs`
- `BotMain/HearthstoneWatchdog.cs`
- `BotMain/BattleNetWindowManager.cs`
- `BotMain/BattleNetRestartState.cs`
- `BotMain/RecommendationDeduplicator.cs`
- `BotMain/RankHelper.cs`
- `BotMain/Cloud/CloudAgent.cs`
- `BotMain/Cloud/CloudConfig.cs`
- `BotMain/Cloud/AutoUpdater.cs`
- `BotMain/Cloud/CommandExecutor.cs`
- `BotMain/Cloud/DeviceStatusCollector.cs`
- `BotMain/PluginSystem.cs`

**审查清单：**

- [ ] **Step 1: 读取 BotService.cs 全文**
  - 记录总行数
  - 识别类的公共方法数量、字段数量
  - 标记职责边界（哪些逻辑本应拆分出去）

- [ ] **Step 2: 分析状态机复杂度**
  - 读取 MatchFlowState.cs，理解状态枚举和转换
  - 在 BotService.cs 中搜索状态转换逻辑，检查：
    - 是否有遗漏的状态转换
    - 是否有 switch/if-else 超过 10 个分支
    - 状态转换是否有防护（防止非法跳转）

- [ ] **Step 3: 分析管道通信健壮性**
  - 读取 PipeServer.cs，检查：
    - 超时处理
    - 断连重连机制
    - 并发安全（多线程访问管道）
    - 异常是否被吞掉（空 catch 块）

- [ ] **Step 4: 分析协议解析**
  - 读取 BotProtocol.cs 和 ActionStringParser.cs
  - 检查：输入验证、畸形数据处理、边界情况

- [ ] **Step 5: 分析错误恢复与容错**
  - 在所有文件中搜索 `catch` 块
  - 标记：空 catch、catch-all without logging、未恢复的异常状态
  - 检查 HearthstoneWatchdog.cs 和 BattleNetRestartState.cs 的恢复逻辑

- [ ] **Step 6: 分析耦合度**
  - 检查 BotService 对外部类的直接依赖数量
  - 检查是否存在循环依赖
  - 检查 Cloud/ 目录下的类与 BotService 的耦合方式

- [ ] **Step 7: 分析账号管理子系统**
  - 读取 AccountController.cs, AccountEntry.cs, AccountQueuePersistence.cs
  - 检查：持久化安全性、队列逻辑正确性、多账号切换的边界情况

- [ ] **Step 8: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 每个问题包含：位置(文件:行号)、描述、风险、建议
  - 将结果以纯文本形式返回

---

### Task 2: 审查 HearthstonePayload 注入层

**Files (只读):**
- `HearthstonePayload/Entry.cs`
- `HearthstonePayload/GameReader.cs`
- `HearthstonePayload/ActionExecutor.cs`
- `HearthstonePayload/ChoiceController.cs`
- `HearthstonePayload/AntiCheatPatches.cs`
- `HearthstonePayload/MouseSimulator.cs`
- `HearthstonePayload/InputHook.cs`
- `HearthstonePayload/InactivityPatch.cs`
- `HearthstonePayload/DialogAutoDismissPatch.cs`
- `HearthstonePayload/BackgroundKeepAliveClicker.cs`
- `HearthstonePayload/PipeClient.cs`
- `HearthstonePayload/SeedBuilder.cs`
- `HearthstonePayload/GameStateData.cs`
- `HearthstonePayload/BattlegroundStateData.cs`
- `HearthstonePayload/BattlegroundStateReader.cs`
- `HearthstonePayload/BattlegroundsMagneticDropMath.cs`
- `HearthstonePayload/PlayRuntimeTargetResolver.cs`
- `HearthstonePayload/PlayTargetConfirmation.cs`
- `HearthstonePayload/DeckReader.cs`
- `HearthstonePayload/ReflectionContext.cs`
- `HearthstonePayload/CoroutineExecutor.cs`
- `HearthstonePayload/GameObjectFinder.cs`
- `HearthstonePayload/SceneNavigator.cs`

**审查清单：**

- [ ] **Step 1: 审查插件入口与生命周期**
  - 读取 Entry.cs 全文
  - 检查：Awake/Update 循环中的异常处理、资源释放、初始化顺序
  - 检查主循环是否有防护避免重入

- [ ] **Step 2: 审查反检测措施**
  - 读取 AntiCheatPatches.cs, InactivityPatch.cs, InputHook.cs
  - 检查：
    - Harmony patch 是否正确应用（Prefix/Postfix/Transpiler）
    - 是否有遗漏的检测点
    - 补丁失败时是否有回退机制
    - 是否有特征可被内存扫描检测到（硬编码字符串、可预测的模式）

- [ ] **Step 3: 审查动作执行系统**
  - 读取 ActionExecutor.cs 全文
  - 检查：
    - 操作间延时是否拟人化
    - 失败重试逻辑
    - 游戏状态验证（执行前是否确认状态正确）
    - 并发安全（UI 线程 vs 后台线程）

- [ ] **Step 4: 审查鼠标模拟与输入**
  - 读取 MouseSimulator.cs, BackgroundKeepAliveClicker.cs
  - 检查：坐标计算正确性、分辨率适配、时序模式是否可被统计检测

- [ ] **Step 5: 审查游戏状态读取**
  - 读取 GameReader.cs, GameStateData.cs, SeedBuilder.cs
  - 检查：
    - 反射调用的异常处理（游戏更新后类/方法可能不存在）
    - 数据一致性（读取过程中游戏状态变更）
    - 空引用防护

- [ ] **Step 6: 审查管道客户端**
  - 读取 PipeClient.cs
  - 与 Task 1 中 PipeServer.cs 的协议对比一致性
  - 检查：序列化/反序列化安全性、缓冲区溢出防护

- [ ] **Step 7: 审查选择与目标解析**
  - 读取 ChoiceController.cs, PlayRuntimeTargetResolver.cs, PlayTargetConfirmation.cs
  - 检查：Discover/Choose 场景全覆盖、超时处理、异常选择的回退

- [ ] **Step 8: 审查场景导航与对话框处理**
  - 读取 SceneNavigator.cs, DialogAutoDismissPatch.cs, GameObjectFinder.cs
  - 检查：场景切换时的状态清理、UI 元素查找的容错

- [ ] **Step 9: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 特别关注 P0 级别的反检测缺陷
  - 将结果以纯文本形式返回

---

### Task 3: 审查 AI 决策引擎

**Files (只读):**
- `BotMain/AI/AIEngine.cs`
- `BotMain/AI/SearchEngine.cs`
- `BotMain/AI/BoardSimulator.cs`
- `BotMain/AI/BoardEvaluator.cs`
- `BotMain/AI/ActionGenerator.cs`
- `BotMain/AI/LethalFinder.cs`
- `BotMain/AI/EnemyBoardLethalFinder.cs`
- `BotMain/AI/TradeEvaluator.cs`
- `BotMain/AI/SimEntity.cs`
- `BotMain/AI/SimBoard.cs`
- `BotMain/AI/CardEffectDB.cs`
- `BotMain/AI/CardEffectScriptRuntime.cs`
- `BotMain/AI/CardEffectScriptLoader.cs`
- `BotMain/AI/CardEffectScriptDefinitions.cs`
- `BotMain/AI/ProfileActionScorer.cs`
- `BotMain/AI/HeuristicRules.cs`
- `BotMain/AI/ActionPriorityModel.cs`
- `BotMain/AI/AggroInteractionModel.cs`
- `BotMain/AI/SearchDiagnostics.cs`
- `BotMain/Learning/LearnedStrategyCoordinator.cs`
- `BotMain/Learning/LearnedStrategyRuntime.cs`
- `BotMain/Learning/LearnedStrategyTrainer.cs`
- `BotMain/Learning/LearnedStrategyFeatureExtractor.cs`
- `BotMain/Learning/FeatureVectorExtractor.cs`
- `BotMain/Learning/LinearScoringModel.cs`
- `BotMain/Learning/LearnedEvalWeights.cs`
- `BotMain/Learning/LearnedStrategyContracts.cs`
- `BotMain/Learning/SqliteLearnedStrategyStore.cs`
- `BotMain/Learning/SqliteTeacherDatasetStore.cs`
- `BotMain/Learning/TeacherActionMapper.cs`
- `BotMain/Learning/TeacherDatasetContracts.cs`
- `BotMain/Learning/TeacherDatasetRecorder.cs`
- `BotMain/Learning/ConsistencyTracker.cs`
- `BotMain/Learning/SqliteConsistencyStore.cs`
- `BotMain/Learning/ActionPatternClassifier.cs`
- `BotMain/Learning/ReadinessMonitor.cs`
- `BotMain/Learning/MatchEntityProvenanceRegistry.cs`
- `BotMain/SimpleAI.cs`

**审查清单：**

- [ ] **Step 1: 审查搜索引擎核心**
  - 读取 SearchEngine.cs 全文
  - 检查：
    - 搜索深度限制、时间限制
    - Alpha-beta 剪枝实现正确性
    - 是否有无限循环风险
    - GC 压力（搜索过程中的内存分配）

- [ ] **Step 2: 审查棋盘模拟**
  - 读取 BoardSimulator.cs, SimBoard.cs, SimEntity.cs
  - 检查：
    - 模拟与实际游戏状态的一致性
    - 深拷贝/浅拷贝正确性
    - 边界情况（空棋盘、满棋盘、满手牌）

- [ ] **Step 3: 审查评估函数**
  - 读取 BoardEvaluator.cs, HeuristicRules.cs
  - 检查：魔法数字、评估权重的合理性、是否有可配置化的空间

- [ ] **Step 4: 审查斩杀检测**
  - 读取 LethalFinder.cs, EnemyBoardLethalFinder.cs
  - 检查：搜索完整性、性能（最坏情况下的耗时）

- [ ] **Step 5: 审查卡牌效果系统**
  - 读取 CardEffectDB.cs, CardEffectScriptRuntime.cs, CardEffectScriptLoader.cs
  - 检查：
    - 脚本加载的安全性
    - 效果缺失时的回退
    - 动态编译（Roslyn）的性能和内存影响

- [ ] **Step 6: 审查学习系统**
  - 读取 Learning/ 目录下所有文件
  - 检查：
    - 训练数据的存储和加载（SQLite 操作安全性）
    - 模型切换的线程安全
    - 特征提取与模型预测的一致性
    - 学习系统与主 AI 引擎的集成边界

- [ ] **Step 7: 审查 SimpleAI 回退**
  - 读取 SimpleAI.cs
  - 检查：在主引擎故障时能否可靠接管

- [ ] **Step 8: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 特别关注性能和内存相关问题
  - 将结果以纯文本形式返回

---

### Task 4: 审查 Cloud + Web

**Files (只读):**
- `HearthBot.Cloud/Program.cs`
- `HearthBot.Cloud/Controllers/AuthController.cs`
- `HearthBot.Cloud/Controllers/DeviceController.cs`
- `HearthBot.Cloud/Controllers/CommandController.cs`
- `HearthBot.Cloud/Controllers/GameRecordController.cs`
- `HearthBot.Cloud/Services/AuthService.cs`
- `HearthBot.Cloud/Services/DeviceManager.cs`
- `HearthBot.Cloud/Services/DeviceWatchdog.cs`
- `HearthBot.Cloud/Services/AlertService.cs`
- `HearthBot.Cloud/Hubs/DashboardHub.cs`
- `HearthBot.Cloud/Hubs/BotHub.cs`
- `HearthBot.Cloud/Data/CloudDbContext.cs`
- `HearthBot.Cloud/Models/Device.cs`
- `HearthBot.Cloud/Models/GameRecord.cs`
- `HearthBot.Cloud/Models/PendingCommand.cs`
- `HearthBot.Cloud/HearthBot.Cloud.csproj`
- `BotMain/Cloud/CloudAgent.cs`
- `BotMain/Cloud/CloudConfig.cs`
- `BotMain/Cloud/AutoUpdater.cs`
- `BotMain/Cloud/CommandExecutor.cs`
- `BotMain/Cloud/DeviceStatusCollector.cs`

**审查清单：**

- [ ] **Step 1: 审查认证与授权**
  - 读取 AuthController.cs, AuthService.cs, Program.cs
  - 检查：
    - JWT 配置（密钥强度、过期时间、算法）
    - 端点的授权保护是否完整
    - 密码/密钥是否硬编码

- [ ] **Step 2: 审查 API 安全**
  - 读取所有 Controller 文件
  - 检查：输入验证、SQL 注入防护（EF Core 参数化）、速率限制

- [ ] **Step 3: 审查数据库操作**
  - 读取 CloudDbContext.cs, 所有 Model 文件
  - 检查：迁移策略、连接管理、事务使用

- [ ] **Step 4: 审查实时通信**
  - 读取 DashboardHub.cs, BotHub.cs
  - 检查：SignalR hub 的认证、消息验证、并发处理

- [ ] **Step 5: 审查客户端云集成**
  - 读取 BotMain/Cloud/ 下所有文件
  - 检查：
    - AutoUpdater 的下载验证（是否校验签名/哈希）
    - 命令执行的安全边界
    - 网络异常处理

- [ ] **Step 6: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 特别关注认证和远程命令执行的安全问题
  - 将结果以纯文本形式返回

---

### Task 5: 审查构建与部署

**Files (只读):**
- `Scripts/build_release.ps1`
- `Scripts/deploy_bot.ps1`
- `Scripts/deploy_cloud.ps1`
- `Scripts/deploy_bepinex.bat`
- `Scripts/ResolveLatestPayloadBuild.ps1`
- `Scripts/update_payload_when_hearthstone_exits.ps1`
- `obfuscar.xml`
- `BotMain/BotMain.csproj`
- `BotCore/BotCore.csproj`
- `HearthstonePayload/HearthstonePayload.csproj`
- `HearthBot.Cloud/HearthBot.Cloud.csproj`
- `BotMain/app.manifest`

**审查清单：**

- [ ] **Step 1: 审查构建流程**
  - 读取 build_release.ps1 全文
  - 检查：
    - 构建步骤的完整性和顺序
    - 错误处理（某步骤失败是否会继续执行后续步骤）
    - 输出验证是否充分
    - 清理逻辑

- [ ] **Step 2: 审查混淆配置**
  - 读取 obfuscar.xml
  - 检查：
    - 排除规则是否合理（不该混淆的是否被排除）
    - 混淆强度
    - 是否有遗漏的程序集

- [ ] **Step 3: 审查部署脚本**
  - 读取 deploy_bot.ps1, deploy_cloud.ps1, deploy_bepinex.bat
  - 检查：
    - 上传过程的安全性（凭据管理、传输加密）
    - 回滚机制
    - 幂等性

- [ ] **Step 4: 审查项目配置**
  - 读取所有 .csproj 文件和 app.manifest
  - 检查：
    - 依赖版本是否有已知漏洞
    - 框架版本是否合理
    - 编译选项（unsafe、nullable 等）

- [ ] **Step 5: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 将结果以纯文本形式返回

---

### Task 6: 审查测试体系

**Files (只读):**
- `BotCore.Tests/BotProtocolTests.cs`
- `BotCore.Tests/BotServiceProfileRefreshTests.cs`
- `BotCore.Tests/BotServiceRestartStatusTests.cs`
- `BotCore.Tests/BoardTradeInteractionTests.cs`
- `BotCore.Tests/LearnedStrategyTests.cs`
- `BotCore.Tests/SeedBuilderTests.cs`
- `BotCore.Tests/PlayRuntimeTargetResolverTests.cs`
- `BotCore.Tests/BattlegroundsMagneticDropMathTests.cs`
- `BotCore.Tests/HsBoxRecommendationProviderTests.cs`
- `BotCore.Tests/PipeClientTests.cs`
- `BotCore.Tests/MatchFlowStateTests.cs`
- `BotCore.Tests/MulliganProtocolTests.cs`
- `BotCore.Tests/StatsBridgeTests.cs`
- `BotCore.Tests/BattlegroundHeroPowerSelectionTests.cs`
- `BotCore.Tests/PlayTargetConfirmationTests.cs`
- `BotCore.Tests/HumanizerPlannerTests.cs`
- `BotCore.Tests/EnemyBoardLethalFinderTests.cs`
- `BotCore.Tests/SeedCompatibilityTests.cs`
- `BotCore.Tests/ReadyWaitDiagnosticsTests.cs`
- `BotCore.Tests/TeacherActionMapperTests.cs`
- `BotCore.Tests/TeacherDatasetRecorderTests.cs`
- `BotCore.Tests/TeacherDatasetStoreTests.cs`
- `BotCore.Tests/BattleNetRestartStateTests.cs`
- `BotCore.Tests/CardTemplateCollection.cs`
- `BotCore.Tests/BattleNetWindowManagerTestShim.cs`
- `BotCore.Tests/BotCore.Tests.csproj`

**审查清单：**

- [ ] **Step 1: 绘制覆盖率地图**
  - 列出所有生产代码文件
  - 对比测试文件，标记哪些模块有测试、哪些没有
  - 重点标记无测试的高风险模块

- [ ] **Step 2: 评估测试质量**
  - 抽样读取 5-8 个测试文件
  - 检查：
    - 是否只测试 happy path
    - 是否有边界情况测试
    - 断言是否充分（不只是 "不抛异常"）
    - Mock/Stub 使用是否合理

- [ ] **Step 3: 识别缺失的关键测试**
  - 基于 Task 1-5 发现的高风险区域，列出应该有但没有的测试
  - 优先级：状态机转换、管道通信、反检测补丁、搜索引擎

- [ ] **Step 4: 检查测试基础设施**
  - 读取 BotCore.Tests.csproj
  - 检查：测试框架版本、辅助类（CardTemplateCollection, BattleNetWindowManagerTestShim）的设计

- [ ] **Step 5: 汇总模块问题**
  - 按 P0-P3 格式记录所有发现的问题
  - 将结果以纯文本形式返回

---

### Task 7: 汇总报告

**依赖:** Task 1-6 全部完成

**Files (创建):**
- Create: `docs/superpowers/specs/2026-04-05-project-review-report.md`

- [ ] **Step 1: 收集所有模块审查结果**
  - 合并 Task 1-6 的问题清单

- [ ] **Step 2: 统一去重和重新分级**
  - 跨模块的同类问题合并
  - 根据全局视角调整个别问题的优先级

- [ ] **Step 3: 撰写执行摘要**
  - 项目整体健康度（一段话）
  - 最严重的 3 个问题提要

- [ ] **Step 4: 撰写关键数据**
  - 各模块行数/文件数
  - 测试覆盖率缺口统计

- [ ] **Step 5: 撰写问题清单**
  - 按 P0→P3 排序
  - 每个问题使用标准格式

- [ ] **Step 6: 撰写技术债热力图**
  - 按模块汇总问题数量和严重程度
  - 排出建议优先处理顺序

- [ ] **Step 7: 撰写下一步建议**
  - 立即修复清单（P0 + 高影响 P1）
  - 短期清理清单（剩余 P1 + 高影响 P2）
  - 长期改善清单（P2 + P3）

- [ ] **Step 8: 写入报告文件**
  - 将完整报告写入 `docs/superpowers/specs/2026-04-05-project-review-report.md`

- [ ] **Step 9: 提交到 git**

```bash
git add docs/superpowers/specs/2026-04-05-project-review-report.md
git commit -m "docs: 项目全面审查报告 — 技术债摸底"
```
