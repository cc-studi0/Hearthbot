# HSBox 构筑模式 Hook 修复设计

日期：2026-03-28

## 1. 背景

当前仓库已经具备通过 CDP 连接盒子内嵌浏览器、读取构筑模式 `client-jipaiqi/ladder-opp` 推荐数据的能力，主要入口包括：

- `BotMain/HsBoxRecommendationProvider.cs`
- `tools/hsbox_standard.py`

现有实现的核心策略是：

1. 通过 `Runtime.evaluate` 向页面注入一段 hook 脚本
2. 包装 `window.onUpdateLadderActionRecommend` 或其他匹配 `onUpdate*Recommend` 的回调
3. 在回调触发时缓存 `raw/data/updatedAt/sourceCallback`
4. Bot 或调试脚本再读取缓存状态

表面上这套链路已经可以工作，但在真实构筑对局里出现了新的失效现象：

- 页面已经显示“推荐打法”
- `bodyText` 明显能看到动作文本
- 但 `count=0`
- `raw=null`
- `data=null`
- `sourceCallback=''`
- `reason='waiting_for_box_payload'`

这说明问题不在“盒子没有推荐”，而在“我们的 hook 没抓到推荐 payload”。

## 2. 问题定义

### 2.1 当前症状

以 `hsbox_standard.py` 的输出为例，当前会出现：

- 页面 URL 正确：`/client-jipaiqi/ladder-opp`
- 页面标题正确：`炉石盒子记牌器`
- 页面正文中已经渲染出“推荐打法”“打出2号位随从”“打出1号位法术”
- 但结构化回调计数仍为 0

这会直接导致两类问题：

- 调试工具误判为“盒子尚未推送推荐”
- Bot 无法获得结构化 `HsBoxActionStep`，只能退化到文本解析或等待

### 2.2 根因判断

结合当前 hook 代码与盒子前端 bundle，可以把根因收敛为两个：

1. `Runtime.evaluate` 注入时机过晚  
   如果页面先加载、推荐回调先触发，再去包装回调，则第一次 payload 永远丢失。

2. 现有 hook 只按“名字是否包过”判断，不按“当前函数身份”判断  
   当前脚本里一旦 `window.__hbHsBoxHooked[key] = true`，后续即使前端重新给 `window.onUpdateLadderActionRecommend` 赋了一个新函数，我们也不会重新包装，导致 hook 失效。

### 2.3 关键结论

这次问题的主目标不是“增强文本兜底”，而是：

`把构筑模式 ladder-opp 页面上的推荐回调 hook 链路修稳定。`

## 3. 目标与约束

### 3.1 主目标

在构筑模式 `client-jipaiqi/ladder-opp` 页面中，只要盒子后续推送推荐回调，Bot 与调试脚本都必须稳定抓到结构化 payload。

### 3.2 运行时原则

本次修复采用严格的 `hook-only` 原则：

- 运行时推荐源只认 hook 抓到的结构化 payload
- `bodyText` 仅保留为诊断信息
- 文本不参与动作推荐、choice 决策、执行链路

### 3.3 范围

本次只处理构筑模式：

- 目标页面：`/client-jipaiqi/ladder-opp`
- 目标回调：`onUpdateLadderActionRecommend` 及同类 `onUpdate*Recommend`

不包含：

- 战旗模式 `client-wargame`
- DOM 结构化回填
- 文本兜底增强

### 3.4 非目标

- 不从已渲染页面逆向恢复“过去已经错过”的历史 JSON
- 不让 `bodyText` 成为运行时动作来源
- 不在本次设计里处理“打牌后选手牌 bug”的执行层修复

## 4. 方案比较

### 4.1 方案 A：继续依赖文本兜底

当 hook 抓不到时，直接用 `bodyText` 解析动作。

优点：

- 改动最小
- 能在部分页面上继续给出结果

缺点：

- 文本语义损失严重
- 目标归属、子选项、手牌目标、英雄目标等容易丢信息
- 会掩盖真正的 hook 缺陷

结论：

不采用。

### 4.2 方案 B：Hook + DOM 回填

hook 抓不到时，从推荐区域 DOM 逆向恢复结构化动作。

优点：

- 可补救“已渲染但错过回调”的页面

缺点：

- 本质还是回填，不是解决 hook
- 维护成本高
- 容易随着前端样式调整而失效

结论：

本次不采用。可作为未来调试工具单独能力考虑，但不进入运行时主链路。

### 4.3 方案 C：Hook 生命周期重构

只修 hook 本身，确保：

- 注入尽量提前
- 回调被替换时自动重包
- 读取状态时能看出当前 hook 是否真实生效

优点：

- 直接修根因
- 运行时结构最干净
- 与 `hook-only` 原则一致

缺点：

- 无法恢复已经错过且不会再次推送的历史 payload

结论：

采用 `方案 C`。

## 5. 总体设计

### 5.1 双阶段注入

当前做法只有一段：

- 连接 CDP
- 调用 `Runtime.evaluate`
- 当场注入 hook

新设计改为两段：

1. **预注入阶段**  
   通过 `Page.addScriptToEvaluateOnNewDocument` 注册 hook 脚本，使后续页面导航、刷新、重新加载时，hook 能在页面脚本执行前安装。

2. **即时注入阶段**  
   对当前已经打开的 `ladder-opp` 页面仍执行一次 `Runtime.evaluate`，兼容现有已存在页面实例。

这样可以同时覆盖：

- 未来的新文档加载
- 当前已打开的页面

### 5.2 固定名优先 + 模式扫描

构筑页面当前仍然明确使用：

- `window.onUpdateLadderActionRecommend`

因此新设计采用双轨策略：

1. **固定名优先**
   - 对 `onUpdateLadderActionRecommend` 单独强化处理
   - 优先做 setter 拦截与函数身份重包

2. **模式扫描补充**
   - 继续扫描匹配 `^onUpdate\w*Recommend$` 的全局函数
   - 用于兼容盒子将来新增但未提前知道的同类回调

### 5.3 函数身份追踪

现有逻辑的问题是只记录：

- 这个名字是否包过

新设计改为记录：

- 当前槽位看到的原始函数引用
- 当前槽位安装的包装函数引用
- 最近一次观测到的函数引用

建议脚本内部状态结构：

- `window.__hbHsBoxHooks[name].original`
- `window.__hbHsBoxHooks[name].wrapped`
- `window.__hbHsBoxHooks[name].lastSeen`

重包规则：

- 如果当前 `window[name]` 是一个函数
- 且它既不是我们当前的 `wrapped`
- 也不是我们记录的 `original`
- 则视为前端重新赋值，立即重新包装

### 5.4 setter 拦截

对 `onUpdateLadderActionRecommend`，优先尝试用 `Object.defineProperty` 建立 setter：

- 前端每次重新赋值时立即触发
- setter 内自动调用包装逻辑

如果目标属性不可配置或定义失败：

- 回退到“每次状态读取时按函数身份重包”的轮询策略

这能保证在不同前端实现下都有可行路径。

### 5.5 统一缓存状态

hook 成功后，仍沿用当前的全局缓存变量，但语义更明确：

- `__hbHsBoxCount`
- `__hbHsBoxUpdatedAt`
- `__hbHsBoxLastRaw`
- `__hbHsBoxLastData`
- `__hbHsBoxLastSource`

其中：

- `LastSource` 必须记录真实回调名
- 不再伪装成“页面已有文本就算 ready”
- 只有真实回调发生过，才允许进入 ready 状态

## 6. 数据流

### 6.1 C# 主链路

`BotMain/HsBoxRecommendationProvider.cs` 的构筑采集流程调整为：

1. 通过 `/json/list` 找到 `ladder-opp` 页面的 `webSocketDebuggerUrl`
2. 建立 CDP WebSocket 连接
3. 调用 `Page.addScriptToEvaluateOnNewDocument`
4. 调用 `Runtime.evaluate` 执行当前页面即时注入
5. 再调用 `Runtime.evaluate` 读取状态对象
6. 如果 `count > 0`，按真实 payload 继续后续映射
7. 如果 `count == 0`，明确返回 `waiting_for_box_payload`

### 6.2 Python 调试工具链路

`tools/hsbox_standard.py` 必须与 C# 侧保持同一套脚本语义：

1. 连接目标页面
2. 先预注册 hook
3. 再对当前页即时注入
4. 再读取状态

这样调试工具和 bot 看到的结果才一致。

### 6.3 状态语义

建议构筑模式状态 `reason` 只保留这些取值：

- `ready_callback`
- `waiting_for_box_payload`
- `callback_missing`
- `hook_install_failed`
- `eval_exception`

其中：

- `ready_callback` 只在 `count > 0` 时出现
- `waiting_for_box_payload` 表示 hook 已安装，但还没收到 payload
- `callback_missing` 表示页面当前根本不存在可 hook 的回调槽位

## 7. 运行时行为约束

### 7.1 Hook-only 原则

构筑推荐运行时必须满足：

- `data == null` 时，不走文本兜底生成动作
- `bodyText` 只用于日志和人工诊断
- 不能因为正文看起来像有推荐，就伪造 `ready`

### 7.2 已错过历史 payload 的处理

如果当前页面已经显示推荐，但我们之前没有及时安装 hook，且该页面后续不会再次推送推荐回调，那么：

- 本次不会尝试从 DOM 反推 JSON
- 状态应继续保持 `waiting_for_box_payload` 或更明确的诊断值

这是 `hook-only` 策略的已知边界，必须接受。

## 8. 模块改动点

### 8.1 `BotMain/HsBoxRecommendationProvider.cs`

需要改动：

- CDP 调用逻辑：增加 `Page.addScriptToEvaluateOnNewDocument`
- `BuildStateScript()`：重写 hook 安装逻辑
- 状态返回逻辑：`reason` 改为 hook-only 语义
- 保留 `bodyText` 字段，但不再把它作为“ready”的信号

### 8.2 `tools/hsbox_standard.py`

需要改动：

- 注入脚本与 C# 同步
- 先注册新文档脚本，再做当前页即时注入
- 输出文案与新 `reason` 对齐

### 8.3 测试

主要修改：

- `BotCore.Tests/HsBoxRecommendationProviderTests.cs`

新增测试方向：

- callback 被重新赋值后，hook 能继续抓取
- 读取状态时如果检测到函数身份变化，会自动重包
- `count=0` 时即使 `bodyText` 非空，也不能标记为结构化 `ready`
- `ready` 仅由真实 callback 驱动

## 9. 测试策略

### 9.1 单元测试

建议新增以下测试：

1. **固定名重包**
   - 初始 `window.onUpdateLadderActionRecommend = f1`
   - 注入后包装为 `wrapped1`
   - 前端重新赋值为 `f2`
   - 再次读取状态后必须重新包装为 `wrapped2`

2. **ready 语义正确**
   - `bodyText` 非空、`count=0`
   - `reason` 仍应是 `waiting_for_box_payload`
   - `data` 必须为空

3. **callback 命中后缓存正确**
   - 回调触发一次
   - `count=1`
   - `sourceCallback=onUpdateLadderActionRecommend`
   - `raw/data/updatedAt` 都正确写入

4. **模式扫描兼容**
   - 页面存在另一个 `onUpdateFooRecommend`
   - 同样能被包装并记录来源

### 9.2 手工验收

在真实构筑对局里，用 `hsbox_standard.py` 做如下验证：

1. 对局刚开始时运行工具
   - 如果推荐尚未到来，应看到 `waiting_for_box_payload`

2. 推荐出现后再次运行
   - 必须看到 `count > 0`
   - 必须看到 `raw` 非空
   - 必须看到 `data` 非空
   - `sourceCallback` 应为真实回调名

3. 盒子页面切模块、重新渲染、重新赋值回调后再次验证
   - hook 仍然有效
   - 不应再次退回长期 `count=0`

## 10. 验收标准

本次设计完成后的验收标准为：

- 构筑 `ladder-opp` 页面后续推送推荐时，Bot 能稳定拿到结构化 callback payload
- 构筑调试工具能稳定显示 `raw/data/sourceCallback`
- callback 被前端重新赋值后，不会因为“已包过”而永久失效
- 运行时推荐链路不再依赖文本兜底
- 页面正文即使已经出现推荐文本，也不会被误判成结构化 `ready`

## 11. 风险与后续

### 11.1 已知风险

- 如果盒子未来彻底改掉 `onUpdateLadderActionRecommend` 机制，仍需重新适配
- 如果页面已经错过唯一一次 callback 且不再重推，`hook-only` 无法追溯恢复

### 11.2 后续工作

在 hook 链路修稳定之后，再回头处理更上层的构筑问题：

- “打牌后选择手牌目标”映射错误
- `OPTION` 与 `PLAY.position` 语义混淆
- 构筑 choice 映射与执行层的回归测试补齐

这些问题应建立在“结构化 payload 稳定可得”之后再修，顺序不能反。
