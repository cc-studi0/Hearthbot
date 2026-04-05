# 项目全面审查设计

## 目标

对 Hearthbot 项目进行全面技术债审查，产出按优先级排序的问题清单，为后续清理工作提供依据。

## 驱动因素

项目经过密集开发（尤其是近期竞技场功能），积累了技术债务，需要系统性摸底。

## 审查方案

**逐模块深度审查**：按模块逐一深入审查，每个模块产出独立的问题清单，最后汇总排序。每个模块由并行子 agent 执行。

## 审查模块

| # | 模块 | 范围 | 审查重点 |
|---|------|------|----------|
| 1 | BotService 核心循环 | BotService.cs, BotProtocol.cs, MatchFlowState.cs, PipeServer.cs, ProfileLoader.cs | 状态机复杂度、文件膨胀（2000+行）、错误恢复、耦合度 |
| 2 | HearthstonePayload 注入层 | Entry.cs, GameReader.cs, ActionExecutor.cs, ChoiceController.cs, AntiCheatPatches.cs, MouseSimulator.cs | 注入安全、反检测健壮性、游戏 API 调用正确性、异常处理 |
| 3 | AI 决策引擎 | AIEngine.cs, SearchEngine.cs, BoardSimulator.cs, BoardEvaluator.cs, LethalFinder.cs, Learning/* | 搜索性能、内存分配、算法正确性、学习系统集成 |
| 4 | Cloud + Web | HearthBot.Cloud/*, hearthbot-web/ 关键文件 | 认证安全、API 设计、数据库操作、前后端通信 |
| 5 | 构建与部署 | Scripts/*, DeployTool/, obfuscar.xml | 构建可靠性、混淆配置、部署流程安全 |
| 6 | 测试体系 | BotCore.Tests/* | 覆盖率缺口、测试质量、缺失的关键测试场景 |

## 问题分级标准

| 级别 | 含义 | 示例 |
|------|------|------|
| P0 — 致命 | 会导致封号、数据丢失、崩溃 | 反检测缺陷、未处理异常导致进程崩溃 |
| P1 — 严重 | 影响核心功能或可维护性 | 上帝类、模块间硬耦合 |
| P2 — 中等 | 技术债但当前可控 | 重复代码、魔法数字、缺少错误日志 |
| P3 — 建议 | 改善代码质量但不紧急 | 命名不一致、可优化的性能 |

## 每个问题的记录格式

```
### [P级别] 问题标题
- **位置**: 文件:行号
- **描述**: 问题是什么
- **风险**: 不修的后果
- **建议**: 修复方向
```

## 最终产出

一份 Markdown 文档（`docs/superpowers/specs/2026-04-05-project-review-report.md`），结构为：

1. **执行摘要** — 项目整体健康度评估
2. **关键数据** — 各模块行数、文件数、测试覆盖情况
3. **问题清单** — 按 P0→P3 排序
4. **技术债热力图** — 哪些模块债务最重，建议优先处理顺序
5. **下一步建议** — 哪些问题值得立刻修，哪些可以等

## 审查不包含

- 不修改任何代码
- 不重构任何模块
- 产出仅为问题清单和建议，实际修复在后续阶段进行
