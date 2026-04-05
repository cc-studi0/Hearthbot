# Hearthbot 项目全面审查报告

**审查日期**: 2026-04-05
**审查范围**: 6 个模块全量审查（BotService 核心循环、HearthstonePayload 注入层、AI 决策引擎、Cloud+Web、构建部署、测试体系）
**驱动因素**: 技术债清理

---

## 一、执行摘要

项目整体架构清晰，模块划分合理，AI 引擎和学习系统设计水平较高。但经过密集的竞技场功能开发后，积累了**大量技术债**，尤其在三个方面问题严重：

1. **安全基础设施几乎不存在**：云控通信全程 HTTP 明文、JWT 密钥硬编码、BotHub 无认证、自动更新无签名校验、账号 Token 明文存储。任何中间人都能接管全部设备。
2. **反检测层极其脆弱**：AntiCheatPatches 依赖硬编码方法名黑名单且异常被静默吞掉，补丁失效时 bot 完全不知道自己正在被监控。
3. **BotService.cs 膨胀到 10339 行**，承担 15+ 项职责，是典型的上帝类，严重阻碍可维护性和测试。

测试覆盖率约 25%（101 个生产文件中仅 25 个有测试），**AI 搜索引擎和反检测核心完全裸奔**。

---

## 二、关键数据

| 模块 | 文件数 | 约行数 | 有测试的文件 | 覆盖率 |
|------|--------|--------|-------------|--------|
| BotMain（核心+UI） | 36 | 25,500 | 12 | 33% |
| BotMain/AI | 19 | ~10,000 | 4 | 21% |
| BotMain/Learning | 18 | ~5,000 | 9 | 50% |
| BotMain/Cloud | 5 | ~1,500 | 0 | 0% |
| HearthstonePayload | 23 | 23,500 | 5 | 22% |
| HearthBot.Cloud | 10+ | 942 | 0 | 0% |
| **合计** | **~111** | **~66,400** | **~30** | **~25%** |

---

## 三、问题清单

共发现 **84 个问题**：P0 = 16、P1 = 33、P2 = 28、P3 = 7

### P0 — 致命（16个）

#### 安全类（10个）

**S01. [P0] 自动更新无签名/哈希校验 + HTTP 明文传输**
- **位置**: BotMain/Cloud/AutoUpdater.cs:133-170, CloudConfig.cs:9
- **描述**: 从 `http://70.39.201.9:5000` 下载 ZIP 包后直接解压覆盖，无哈希校验、无签名、无 HTTPS。
- **风险**: 中间人替换更新包即可在所有客户端执行任意代码。供应链攻击零门槛。
- **建议**: 强制 HTTPS；manifest 包含 SHA256 哈希并在客户端校验；考虑代码签名。

**S02. [P0] 更新脚本存在命令注入**
- **位置**: BotMain/Cloud/AutoUpdater.cs:175-220
- **描述**: zipPath、version 等变量直接拼接到 bat 脚本，无转义。version 含 `& calc.exe` 即可注入。
- **风险**: 通过 MITM 篡改 manifest 的 version 字段即可 RCE。
- **建议**: 严格校验 version 格式（只允许 `[a-zA-Z0-9.\-]`），路径用双引号包裹。

**S03. [P0] JWT 密钥硬编码回退值**
- **位置**: HearthBot.Cloud/Program.cs:16
- **描述**: `Jwt:Secret` 未配置时使用硬编码明文字符串，已提交到代码仓库。
- **风险**: 任何看到源码的人可伪造 JWT，绕过全部 `[Authorize]` 端点。
- **建议**: 移除回退值，启动时缺失配置则拒绝启动。

**S04. [P0] 空密码时接受任意登录**
- **位置**: HearthBot.Cloud/Services/AuthService.cs:24
- **描述**: `PasswordHash` 为空时 `return true`，任意密码可登录。
- **风险**: 首次部署忘配密码 = 系统完全暴露。
- **建议**: PasswordHash 为空时拒绝登录，日志输出警告。

**S05. [P0] BotHub 无认证，任意客户端可冒充设备**
- **位置**: HearthBot.Cloud/Hubs/BotHub.cs:6
- **描述**: DashboardHub 有 `[Authorize]`，BotHub 完全没有。任何人可 Register/Heartbeat/ReportGame/CommandAck。
- **风险**: 伪造设备、劫持指令映射、污染数据、截获合法指令。
- **建议**: 给 BotHub 加 `[Authorize]`，为 Bot 设备分配独立认证。

**S06. [P0] 账号 Token 明文存储在 accounts.json**
- **位置**: BotMain/AccountEntry.cs:13, AccountQueuePersistence.cs:33
- **描述**: HearthstoneToken 直接序列化为 JSON 明文写入磁盘。
- **风险**: 文件被窃取则所有账号凭据泄露。
- **建议**: 使用 DPAPI（ProtectedData）加密敏感字段。

**S07. [P0] 部署脚本通过 HTTP 明文分发更新包**
- **位置**: Scripts/deploy_bot.ps1:110-112
- **描述**: 下载地址为 `http://...`，无 TLS。
- **风险**: 中间人攻击替换更新包。
- **建议**: 部署 HTTPS。

**S08. [P0] deploy_bot.ps1 使用 MD5 做完整性校验**
- **位置**: Scripts/deploy_bot.ps1:47
- **描述**: MD5 已可构造碰撞，不适合完整性校验。
- **风险**: 攻击者可构造 MD5 相同的恶意文件。
- **建议**: 改用 SHA256。

**S09. [P0] 账号登录信息泄露到日志**
- **位置**: BotMain/BotApiHandler.cs:479-481
- **描述**: 战网账号邮箱直接打印到日志。
- **风险**: 日志共享时泄露用户身份。
- **建议**: 脱敏处理。

**S10. [P0] 混淆临时目录硬编码，并发构建互相覆盖**
- **位置**: Scripts/build_release.ps1:18-19
- **描述**: `C:\temp\hb_obfuscate` 固定路径，并发构建会冲突。
- **风险**: 构建产物被污染或损坏。
- **建议**: 使用 GUID 随机目录名。

#### 反检测类（3个）

**D01. [P0] AntiCheatPatches 硬编码方法名覆盖不全**
- **位置**: HearthstonePayload/AntiCheatPatches.cs:28-43
- **描述**: 只 patch 6 个硬编码方法名。暴雪新增或重命名反作弊回调时补丁无效。
- **风险**: 反作弊上报绕过失败，直接导致封号。
- **建议**: 改为枚举 AntiCheatManager 所有方法并全部 patch（白名单放行而非黑名单拦截）。

**D02. [P0] AntiCheatPatches.Apply 吞掉所有异常**
- **位置**: HearthstonePayload/AntiCheatPatches.cs:45
- **描述**: `catch { }` 空 catch。Harmony patch 失败时插件继续运行但保护层已失效。
- **风险**: 最危险的故障模式——看起来一切正常但反检测已失效。
- **建议**: 异常时设置全局 `AntiCheatActive = false` 标志，让主循环感知并停止操作。

**D03. [P0] ReturnFalse prefix 对初始化方法可能产生副作用**
- **位置**: HearthstonePayload/AntiCheatPatches.cs:48
- **描述**: 对 Initialize/InitSDK 使用 prefix 返回 false 跳过原方法，可能导致内部状态未初始化。
- **风险**: NullReferenceException 被反作弊系统捕获，暴露注入痕迹。
- **建议**: 对初始化方法用 postfix 代替 prefix。

#### 功能类（3个）

**F01. [P0] PipeServer 端口硬编码 59723，多开场景冲突**
- **位置**: BotMain/PipeServer.cs:23
- **描述**: 多开时第二个实例 AddressAlreadyInUse。
- **风险**: 多开环境完全不可用。
- **建议**: 支持端口范围自动选择或使用命名管道。

**F02. [P0] EnemyBoardLethalFinder 静态 Lazy 初始化不可更新**
- **位置**: BotMain/AI/EnemyBoardLethalFinder.cs:21
- **描述**: CardEffectDB 只在首次访问时创建，后续扩展无效。
- **风险**: 运行时加载新卡效果时产生不一致。
- **建议**: 改为可重置的实例或接受外部注入。

**F03. [P0] 云控服务以 root 运行**
- **位置**: Scripts/deploy_cloud.ps1:15,90
- **描述**: systemd service 以 root 身份运行。
- **风险**: 服务被攻破后攻击者获得 root 权限。
- **建议**: 创建专用低权限用户。

---

### P1 — 严重（33个）

#### 架构与可维护性

**A01. BotService.cs 10339 行上帝类** — BotService.cs 全文件。承担 15+ 职责，333 个方法。建议拆分为独立的 MatchLoop 类和协作组件。

**A02. FinalizeMatchAndAutoQueue 13 个 ref 参数** — BotService.cs:8713。建议封装为 MatchSessionState 类。

**A03. 主循环是隐式状态机** — BotService.cs:2041-3060。16 个局部变量构成隐式状态，无显式枚举。建议定义 MatchPhase 枚举。

**A04. BotService 直接依赖超过 20 个外部类型** — BotService.cs using 声明。建议引入接口抽象和依赖注入。

**A05. BotApiHandler 与 BotService 双向耦合** — BotApiHandler.cs:78。建议定义 IBotControl 接口。

**A06. BotApiHandler 通过反射耦合 Bot 静态字段** — BotApiHandler.cs:86-118。50+ 字段名魔法字符串。

#### 安全

**A07. AutoUpdater bat 脚本路径注入** — AutoUpdater.cs:176-219。变量未转义。

**A08. CommandExecutor 无命令白名单** — CommandExecutor.cs:47-95。payload 无大小/格式限制。

**A09. CORS 完全开放** — HearthBot.Cloud/Program.cs:49-50。允许任意来源。

**A10. 缺少登录速率限制** — AuthController.cs:17-25。可暴力破解。

**A11. BotHub 方法无 deviceId 绑定校验** — BotHub.cs:19-63。设备可冒充其他设备。

**A12. JWT 默认 7 天过期，无 refresh/revoke** — AuthService.cs:35。

**A13. 数据库用 EnsureCreated + 手动 ALTER TABLE** — Program.cs:54-60。无正规迁移策略。

**A14. deploy_cloud.ps1 监听 0.0.0.0:5000 无 TLS** — deploy_cloud.ps1:90。

**A15. SSH 部署无 StrictHostKeyChecking** — deploy_bot.ps1:90, deploy_cloud.ps1:56。

**A16. deploy_cloud.ps1 备份/恢复逻辑不可靠** — deploy_cloud.ps1:64-69。

**A17. ProfileLoader 动态编译无沙箱** — ProfileLoader.cs:125-133。恶意 .cs 可执行任意代码。

**A18. BotMain.csproj 启用 AllowUnsafeBlocks** — BotMain.csproj:7。

#### 数据与稳定性

**A19. AccountQueuePersistence.Save 空 catch 致数据丢失** — AccountQueuePersistence.cs:68。

**A20. AccountQueuePersistence.Load 空 catch 返回空列表** — AccountQueuePersistence.cs:39-41。

**A21. CloudConfig.Load/Save 空 catch** — CloudConfig.cs:22-28,33-38。DeviceToken 可能丢失。

**A22. BotService.Stop() 不等待线程退出** — BotService.cs:691-701。可能两个主循环并发。

**A23. AccountController.SwitchToAccount 递归栈溢出风险** — AccountController.cs:169-253。

**A24. Payload 构建失败只警告不中断** — build_release.ps1:130-135。

**A25. SQLite runtimes 裁剪与目标平台不匹配** — build_release.ps1:163 vs BotMain.csproj:6。

#### 反检测与注入

**A26. InputHook 静态字段无 volatile** — InputHook.cs:15-19。多线程读写无内存屏障。

**A27. InactivityPatch 字段名硬编码无回退** — InactivityPatch.cs:36-37。

**A28. Plugin 无 OnDestroy，资源无法释放** — Entry.cs。TCP/线程/事件对象无释放路径。

**A29. ActionExecutor 多处 int.Parse 无异常处理** — ActionExecutor.cs:1211 等。协议错误丢失整回合操作。

**A30. BackgroundKeepAliveClicker SetForegroundWindow 抢焦点** — BackgroundKeepAliveClicker.cs:49。

#### AI 引擎

**A31. 吸血回血只给友方英雄，敌方吸血模拟错误** — BoardSimulator.cs:96-97。

**A32. ProcessDeaths 不处理连锁死亡** — BoardSimulator.cs:385-424。亡语连锁被忽略。

**A33. LethalFinder visited 哈希精度不足可能漏算斩杀** — LethalFinder.cs:103-104,120。

**A34. SqliteConsistencyStore 长驻连接无并发保护** — SqliteConsistencyStore.cs:22-24。

**A35. LearnedStrategyCoordinator 主线程直接写 SQLite** — LearnedStrategyCoordinator.cs:71。可能阻塞决策。

**A36. CardEffectScriptLoader 加载失败静默吞掉** — CardEffectScriptLoader.cs:13-21。

**A37. Beam Search 无节点上限保护** — SearchEngine.cs:181-450。极端情况内存爆炸。

---

### P2 — 中等（28个）

| # | 问题 | 位置 |
|---|------|------|
| B01 | goto 用于主循环重连 | BotService.cs:3059 |
| B02 | MatchFlowState 命名误导 | MatchFlowState.cs |
| B03 | PipeServer._pendingRead 无线程安全 | PipeServer.cs:29,132 |
| B04 | PipeServer.Receive 同步阻塞 | PipeServer.cs:135 |
| B05 | PipeServer 无 finalizer | PipeServer.cs:179 |
| B06 | BotProtocol 解码异常静默 | BotProtocol.cs:557 |
| B07 | ActionStringParser.Parse 全方法 catch-all | ActionStringParser.cs:66 |
| B08 | BotService 约 20 处空 catch | BotService.cs 多处 |
| B09 | AccountController 后台线程修改 ObservableCollection | AccountController.cs:87 |
| B10 | AccountQueuePersistence 无原子写入 | AccountQueuePersistence.cs:46 |
| B11 | 鼠标轨迹曲线过于规律可被统计检测 | ActionExecutor.cs:1592, MouseSimulator.cs |
| B12 | 协程超时后 InputHook 状态残留 | CoroutineExecutor.cs:86-97 |
| B13 | PipeClient 固定端口 59723 | PipeClient.cs:16 |
| B14 | GameReader 顶层 catch 吞异常 | GameReader.cs:41-44 |
| B15 | ReflectionContext 遍历时线程安全 | ReflectionContext.cs:246-291 |
| B16 | BattlegroundStateReader 硬编码 zone 魔数 | BattlegroundStateReader.cs:237 |
| B17 | PostMessage 输入可被高级反作弊检测 | BackgroundKeepAliveClicker.cs:223 |
| B18 | DialogAutoDismissPatch Queue 无大小限制 | DialogAutoDismissPatch.cs:16 |
| B19 | 每次搜索展开都全量 Clone 棋盘 | SearchEngine.cs:318 |
| B20 | 转置表 ulong 指纹碰撞风险 | SearchEngine.cs:137 |
| B21 | PlayCard 随从不检查满场 | BoardSimulator.cs:128 |
| B22 | 英雄攻击力取 Max 而非叠加 | BoardSimulator.cs:51-54 |
| B23 | BoardEvaluator 大量魔法数字 | BoardEvaluator.cs:37-68 |
| B24 | LinearScoringModel 无正则化 | LinearScoringModel.cs:41-56 |
| B25 | IsPriestDamageHeroPower 重复定义三处 | BoardSimulator/ActionGenerator/LethalFinder |
| B26 | SimpleAI 解析脆弱且不检查满场 | SimpleAI.cs:21-47 |
| B27 | UseUnicodeNames 混淆引发杀软误报 | obfuscar.xml:11 |
| B28 | HearthstonePayload 未纳入混淆 | obfuscar.xml 整体 |

---

### P3 — 建议（7个）

| # | 问题 | 位置 |
|---|------|------|
| C01 | BotProtocol 无消息长度限制 | BotProtocol.cs:286 |
| C02 | PluginSystem Fire 方法大量重复 | PluginSystem.cs:60-268 |
| C03 | BotService 使用 Thread 而非 Task | BotService.cs:687 |
| C04 | 部署脚本硬编码服务器 IP | deploy_bot.ps1:10 |
| C05 | HearthBot.Cloud 依赖版本固定 8.0.8 | HearthBot.Cloud.csproj |
| C06 | DeviceId 用 MachineName 多实例冲突 | CloudConfig.cs:44 |
| C07 | FeatureVectorExtractor 硬编码维度无兼容检查 | FeatureVectorExtractor.cs:9 |

---

## 四、技术债热力图

```
模块                    P0   P1   P2   P3   总计   债务评级
─────────────────────────────────────────────────────────
BotService 核心循环      5    10   12    4    31    ████████████ 极重
HearthstonePayload       3     6    8    3    20    ████████ 重
Cloud + Web              4     5    7    3    19    ████████ 重
构建与部署               3     7    7    3    20    ████████ 重
AI 决策引擎              1     5   13    6    25    ██████ 中重（P0少但P2多）
测试体系                 4    10    8    -    22    ████████ 重（覆盖率缺口）
```

**建议优先处理顺序**：
1. **Cloud + Web 安全** — P0 最密集，攻击面最大，修复成本最低
2. **AntiCheatPatches 重构** — 封号风险直接关联
3. **构建部署安全** — HTTP→HTTPS、MD5→SHA256
4. **BotService 拆分** — 可维护性的根本瓶颈
5. **AI 引擎修正** — 吸血/连锁死亡/斩杀哈希
6. **测试补全** — 优先覆盖搜索引擎和反检测

---

## 五、下一步建议

### 立即修复（P0 + 高影响 P1，建议 1 周内）

| 优先级 | 问题 | 工作量估算 |
|--------|------|-----------|
| 1 | BotHub 加 `[Authorize]` + deviceId 绑定 | 小 |
| 2 | 移除 JWT 默认密钥回退 | 小 |
| 3 | 修复空密码登录 | 小 |
| 4 | 部署 HTTPS（Let's Encrypt） | 中 |
| 5 | deploy_bot.ps1 MD5→SHA256 | 小 |
| 6 | AntiCheatPatches 改为白名单模式 + 失败告警 | 中 |
| 7 | AutoUpdater 加 SHA256 校验 | 中 |
| 8 | bat 脚本变量转义防注入 | 小 |
| 9 | 账号 Token 加密存储 | 中 |
| 10 | 日志脱敏 | 小 |

### 短期清理（剩余 P1 + 高影响 P2，建议 1-2 个月）

- BotService 拆分为多个 MatchLoop + 协作组件
- 引入接口抽象解耦核心依赖
- 修复 AI 引擎模拟错误（吸血方向、连锁死亡、斩杀哈希）
- SQLite 并发保护
- 空 catch 块全面清理（至少加日志）
- AccountQueuePersistence 原子写入
- 补全核心模块测试（SearchEngine、AntiCheatPatches、AutoUpdater）

### 长期改善（P2 + P3，持续进行）

- 引入 Mock 框架减少测试中的反射
- 搜索引擎性能优化（对象池、Copy-on-Write）
- BoardEvaluator 权重可配置化
- 学习系统正则化和冷启动优化
- HearthstonePayload 纳入混淆
- 鼠标轨迹随机化增强
- 构建流程加入版本号和 Git hash
