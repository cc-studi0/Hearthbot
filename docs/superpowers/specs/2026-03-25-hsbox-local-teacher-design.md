# HSBox 本地教师策略重构设计

日期：2026-03-25

## 1. 背景

当前仓库已经有 `Learn From HSBox` 和 `Use Learned Local` 两个入口，但现有实现更接近“基于老师结果做少量权重补丁”：

- `Learn From HSBox` 会把老师结果转成聚合后的规则权重写入 `teacher.db`
- `Use Learned Local` 在动作阶段主要通过 `LearnedStrategyRuntime` 给现有本地 AI 加偏置
- 留牌和发现也有本地学习逻辑，但仍以粗粒度规则/记忆为主

这条路径的主要问题不是“完全没学习”，而是：

- 学的是聚合后的规则，不是原始决策样本
- 动作学习特征过粗，核心是 `board bucket`
- 学不到“在当前局面下，所有合法候选动作里老师为什么选这一手”
- 适合做局部 bias，不适合做“所有卡组通用的本地盒子替代器”

本次重构目标不是继续增强旧的规则补丁系统，而是把它升级成一个真正的“本地逐步决策教师模型”。

## 2. 目标

### 2.1 主目标

在 `Use Learned Local` 启用时，本地系统能够在标准对局中逐步输出“下一步最优动作”，尽量替代 HSBox 的推荐打法，且适用于所有卡组，不依赖传说分段的盒子在线推荐。

### 2.2 约束

- 当前最稳定可获取的 HSBox 数据粒度是“当前最优一步”，不是整回合完整动作链
- 目标是所有卡组通用，而不是少数卡组专属规则
- 用户接受引入离线训练管线
- 用户期望 `Use Learned Local` 偏激进，不希望低置信度时默认回退现有本地 AI

### 2.3 非目标

- 第一版不追求直接预测整回合完整动作链
- 第一版不做端到端超大黑盒模型
- 第一版不继续以“自动累加少量规则权重”为主路径
- 第一版不强依赖每个卡组单独维护一套规则

## 3. 总体路线

推荐路线为：

1. `Learn From HSBox` 从“在线规则增量器”升级为“原始决策样本采集器”
2. `Use Learned Local` 从“现有 AI 偏置补丁”升级为“本地候选动作排序器”
3. 实时阶段按“每次只决策一步”的方式运行
4. 离线阶段训练通用排序模型，并导出 ONNX 供 C# 侧实时推理

一句话概括：

`state + candidate_action -> model score -> 选当前最优一步 -> 执行 -> 重新读局面`

## 4. 推荐方案比较

### 4.1 方案 A：增强现有规则库

继续沿用 `teacher.db + rule weight delta` 的逻辑，只是扩展更多条件字段。

优点：

- 接入成本最低
- 兼容现有 `LearnedStrategyRuntime`
- 可解释性好

缺点：

- 仍然是经验补丁，不是真正泛化学习
- 状态一细化就会爆炸
- 不适合“所有卡组通用替代盒子”

### 4.2 方案 B：候选动作排序模型

先生成当前所有合法候选动作，再由模型对每个候选动作打分，选择 top1。

优点：

- 完全贴合“老师只能提供当前最优一步”的现实
- 合法性由候选枚举器保证，稳定性高
- 泛化能力远强于规则库
- 可直接复用现有动作字符串、Board、Seed、实体来源信息

缺点：

- 需要新增离线训练管线
- 必须先把“老师动作映射到本地候选动作”做稳定

### 4.3 方案 C：端到端直接预测动作

模型直接从完整局面输出下一步动作，不走候选枚举。

优点：

- 最终形态更纯粹

缺点：

- 动作空间大，合法性难保
- 训练与调试难度最高
- 以当前代码形态不适合作为第一版

### 4.4 结论

本次重构采用 `方案 B：候选动作排序模型`。

## 5. 运行时架构

### 5.1 核心思路

`Use Learned Local` 不再只是给本地 AI 打补丁，而是成为一个真正的逐步决策器。

运行时流程：

1. 读取当前 `Board`、`seed`、牌组签名、剩余牌库、来源信息等
2. 构造统一 `DecisionContextSnapshot`
3. 枚举当前所有合法候选动作
4. 对每个候选动作抽取 `state + candidate_action` 特征
5. 模型输出每个候选动作分数
6. 选择分数最高的动作作为“当前最优一步”
7. 执行动作后重新读取局面，重复流程

### 5.2 模块划分

#### `DecisionContextSnapshot`

统一封装单步决策上下文：

- `Board`
- `Seed`
- `DeckName`
- `DeckSignature`
- `RemainingDeckCards`
- `FriendlyEntities`
- `MatchContext`
- `PendingOrigin`
- 最近一步执行结果与消费状态

#### `CandidateGenerationService`

职责：

- 动作阶段枚举合法候选动作
- 发现/抉择阶段枚举合法选项
- 留牌阶段枚举保留/替换决策候选

优先复用现有 AI 与动作映射能力，而不是另起一套动作语义。

#### `FeatureEncodingService`

职责：

- 把 `DecisionContextSnapshot + candidate` 编码为模型输入特征
- 支持动作、发现、留牌三类特征 schema

#### `LocalTeacherModelRuntime`

职责：

- 加载模型
- 对候选动作批量评分
- 返回当前一步 top1 结果

#### `ModelArtifactManager`

职责：

- 加载 `onnx + schema + metadata`
- 校验模型版本、特征版本
- 支持热更新与缺失保护

### 5.3 三类模型

尽管整体框架统一，但实时模型分为三类：

- `action-ranker`：出牌/攻击/英雄技能/地点/结束回合
- `choice-ranker`：发现、抉择、泰坦技能、子选项
- `mulligan-ranker`：留牌

## 6. 训练样本与数据存储

### 6.1 核心原则

旧系统的主要问题是只保存“聚合后的规则权重”，没有保存“老师面对的完整候选集合”。

新系统必须先满足两件事：

1. 一个老师动作必须能稳定映射到一个本地候选动作
2. 一个决策点必须完整保留当时的候选动作集合

### 6.2 数据库

新增独立原始样本库：

- `Hearthbot/Data/HsBoxTeacher/dataset.db`

保留旧的 `teacher.db` 作为 legacy 兼容层，但不再作为主学习来源。

### 6.3 表设计

#### `matches`

字段建议：

- `match_id`
- `deck_name`
- `deck_signature`
- `mode`
- `start_time_utc`
- `end_time_utc`
- `outcome`

#### `action_decisions`

一条记录对应一个“当前一步”动作决策点。

字段建议：

- `decision_id`
- `match_id`
- `turn`
- `step_index`
- `seed`
- `payload_signature`
- `board_snapshot_json`
- `context_snapshot_json`
- `teacher_action_command`
- `teacher_mapped_candidate_id`
- `mapping_status`
- `created_at_ms`

#### `action_candidates`

一条记录对应某个动作决策点下的一个候选动作。

字段建议：

- `candidate_id`
- `decision_id`
- `action_command`
- `action_type`
- `source_card_id`
- `target_card_id`
- `candidate_snapshot_json`
- `candidate_features_json`
- `is_teacher_pick`

#### `choice_decisions`

一条记录对应一个发现/抉择决策点。

#### `choice_options`

一条记录对应一个选项候选。

#### `mulligan_decisions`

一条记录对应一次起手留牌场景。

#### `mulligan_cards`

一条记录对应一个候选起手牌及上下文。

### 6.4 `Learn From HSBox` 的新职责

开启后不再主打“实时写规则权重”，而是：

1. 在决策点抓取当前上下文
2. 枚举当前本地合法候选动作
3. 将 HSBox 当前一步映射到候选动作
4. 记录 `state + candidates + teacher pick + local pick`
5. 对局结束后补录 outcome

## 7. 模型与特征设计

### 7.1 模型选择

第一版推荐：

- `LightGBM / LambdaMART` 排序模型
- 导出 ONNX
- C# 运行时使用 ONNX Runtime 做实时推理

不推荐第一版直接上大规模深度模型，原因：

- 当前问题本质上是表格排序问题
- 小样本/中样本下树模型更稳
- 训练、部署、解释与调试成本更低

### 7.2 动作模型特征

#### 局面基础特征

- 回合数
- 当前费用 / 最大费用
- 双方血量 / 护甲
- 双方场面数量
- 双方总攻击 / 可攻击数量
- 手牌数
- 剩余牌库数
- 是否有嘲讽 / 冻结 / 武器 / 技能可用
- 当前是否存在斩杀压力

#### 动作自身特征

- 动作类型
- 来源卡 `card_id`
- 来源费用 / 类型 / 身材
- 目标类型
- 目标卡 `card_id`
- 来源牌来源：起手 / 抽到 / 发现 / 生成 / 复制 / 变形

#### 动作后状态特征

基于模拟器执行该候选动作后的局面摘要：

- 我方场面变化
- 敌方场面变化
- 可用费用变化
- 手牌变化
- 嘲讽处理结果
- 是否进入可斩杀/防斩状态

#### 状态变化特征

- 我方总攻击增减
- 敌方威胁增减
- 解场收益
- 资源消耗
- 节奏变化

### 7.3 发现与抉择模型特征

- 来源卡
- 当前局面摘要
- 选项卡 `card_id`
- 选项费用 / 类型 / 关键词
- 选项是否与当前局面协同
- 来源是否为发现 / 回溯 / 子选项

### 7.4 留牌模型特征

第一版建议做“单卡 keep score”：

- 对手职业
- 是否有 coin
- 当前候选四张牌
- 单张牌 `card_id`
- 同手牌组合上下文
- 牌组签名 / 职业 / 套牌类型特征

后续再加留牌组合修正层。

### 7.5 泛化策略

模型必须是“全卡组通用主模型”，但允许加入条件特征：

- `deck_signature`
- `own_class`
- `enemy_class`
- `archetype`

不做“每个卡组一套大模型”的设计。

## 8. 代码接入方案

### 8.1 新模块

建议新增以下模块：

- `TeacherDatasetRecorder`
- `CandidateGenerationService`
- `FeatureEncodingService`
- `LocalTeacherModelRuntime`
- `ModelArtifactManager`

### 8.2 现有模块迁移

#### `BotService`

保留现有入口，但重构推荐分发：

- `HSBox`
- `LocalTeacherModel`
- `LegacyLocalAI`

`Use Learned Local` 开启后，动作/choice/mulligan 主路径都应优先走 `LocalTeacherModelRuntime`。

#### Legacy 学习模块

以下现有模块先保留，但降级为兼容层：

- `LearnedStrategyCoordinator`
- `LearnedStrategyTrainer`
- `LearnedStrategyRuntime`
- `SqliteLearnedStrategyStore`

重命名或逻辑层面降级为 `LegacyLearnedPatchRuntime` 更清晰，但第一阶段不强制重命名。

#### 现有通用脚本

以下通用脚本暂时保留兼容：

- `Profiles/通用盒子学习出牌.cs`
- `Profiles/通用盒子学习点击.cs`
- `MulliganProfiles/通用盒子学习留牌.cs`
- `DiscoverCC/通用盒子学习发现.cs`

但中期主路径应逐步迁移到 BotMain 的统一运行时，而不是继续让各脚本自己解析 OCR/状态文件。

## 9. 按钮语义调整

### `Learn From HSBox`

新语义：

- 启用老师样本原始采集
- 记录动作/发现/留牌的决策点数据
- 对局结束后补 outcome

旧的在线规则增量可以保留为兼容模式，但不再是主路径。

### `Use Learned Local`

新语义：

- 启用本地教师模型实时推理
- 动作阶段走 `action-ranker`
- choice/discover 走 `choice-ranker`
- mulligan 走 `mulligan-ranker`

该模式下默认不因为“低置信度”回退旧 AI；只在系统级异常时兜底，例如：

- 模型文件缺失
- 候选动作生成失败
- 动作映射失败
- 推理异常

## 10. 分阶段落地

### 阶段 A：原始样本采集

交付：

- `dataset.db`
- 动作/发现/留牌决策点记录
- 老师动作映射统计

验收：

- 决策点样本可完整落库
- 老师动作映射成功率可统计

### 阶段 B：离线训练动作排序模型

交付：

- 特征导出脚本
- 训练脚本
- ONNX 导出
- 基础离线评估报告

验收：

- `action-ranker` 能离线推理
- 有 top1 / top3 命中率

### 阶段 C：动作阶段线上接入

交付：

- `LocalTeacherModelRuntime` 动作主路径
- `Use Learned Local` 对动作决策生效

验收：

- 可稳定跑完整局
- 非法动作率受控

### 阶段 D：接入 choice 与 mulligan

交付：

- `choice-ranker`
- `mulligan-ranker`

验收：

- 三类决策全部统一到本地教师模型框架

### 阶段 E：逐步退役 legacy patch learning

交付：

- 旧学习逻辑降级为兼容模式
- 新主路径明确

## 11. 验证方案

### 11.1 数据层验证

验证内容：

- 每个决策点是否有完整候选集合
- 老师动作是否能稳定映射
- 上下文快照是否可复现

### 11.2 离线模型验证

指标建议：

- top1 命中率
- top3 命中率
- 按动作类型分组命中率
- 按职业分组命中率
- 按回合段分组命中率
- 按致命/非致命局面分组命中率

### 11.3 线上行为验证

重点观察：

- 非法动作
- 重复动作卡死
- 空过/明显乱打
- 实际胜率是否逼近 `Follow HSBox`

### 11.4 回放评估工具

建议新增 `replay evaluator`：

- 输入历史样本
- 比较 `HSBox 动作`
- 比较 `旧 Local AI 动作`
- 比较 `新模型动作`

用于快速评估新模型是否在逼近盒子。

## 12. 风险与控制

### 风险 1：老师动作映射不到本地候选

这是最大风险。

控制方式：

- 映射失败样本单独记录
- 统计失败原因
- 先补齐映射与候选枚举，再推进训练

### 风险 2：本地候选动作集合不完整

控制方式：

- 优先补齐 `CandidateGenerationService`
- 不把模型问题和候选生成问题混为一谈

### 风险 3：HSBox payload 与当前局面不同步

控制方式：

- 保留现有 `payload_signature / updated_at / consumed tracking`
- 采集阶段严格校验 freshness

### 风险 4：模型偏保守

控制方式：

- 对动作类型做样本平衡
- 对 `END_TURN` 等动作单独控制训练分布

### 风险 5：通用模型对小众卡组效果差

控制方式：

- 主模型通用
- 加 `deck_signature / class / archetype` 条件特征
- 不拆成重型 per-deck 专属模型

## 13. 与当前仓库的关系

本设计不是推翻现有系统，而是顺着已有基础升级：

- 复用现有 `Board / Seed / Entity provenance / HsBox payload signature`
- 复用现有动作字符串和 choice/discover 请求结构
- 复用现有 consumed tracking 机制
- 逐步替代当前过粗的 `LearnedStrategyRuntime`

## 14. 当前结论

对于本仓库，最合适的方向不是继续堆规则，而是：

- 以 `候选动作排序模型` 为主线
- 以 `原始样本采集` 为第一阶段
- 以 `ONNX 本地推理` 为运行时方案
- 以 `所有卡组通用` 为主目标
- 以 `逐步决策` 替代“整回合一次性预测”

## 15. 后续计划入口

在本设计确认后，下一步应进入 implementation plan，拆出：

- 数据采集改造
- 候选动作枚举统一化
- 特征抽取与离线训练脚本
- ONNX 运行时接入
- legacy 学习系统降级兼容

## 16. 评审说明

本次 spec 已基于当前仓库代码结构完成本地自审。

由于本会话没有显式授权使用子代理，未执行 spec reviewer 子代理流程；后续如用户允许，可补做独立 spec review。
