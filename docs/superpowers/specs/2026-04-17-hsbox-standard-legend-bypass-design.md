# 盒子标传传说推荐受限绕过

## 背景

网易炉石盒子（HSAng.exe）在**标准模式 + 传说段位**对高名次玩家**阶梯式禁用出牌建议**，受限规则为：

- 每月 01–10 号：传说之下可用（传说段位全部禁用）
- 每月 11–20 号：传说 5 万名外可用
- 每月 21–月终：传说 3 万名外可用

受限时前端渲染"AI 推荐功能实行阶梯开放"文案页，Hearthbot 拿不到教师推荐。

本 spec 设计一套**先侦察后精准补丁**的绕过方案，唯一输出诉求：让盒子在受限档继续向 Hearthbot 提供出牌建议。

## 已知事实（深挖 `main.js` 得出）

1. 盒子推荐 UI 从网易远端在线加载：`https://hs-web-embed.lushi.163.com/client-jipaiqi/ladder`，非本地文件。
2. 前端 V8（Chrome 108）暴露 CDP 于 `127.0.0.1:9222`，已被 `HsBoxRecommendationProvider.cs` 使用。
3. 前端受限文案是 React 组件硬编码在 `main.js` 里的明文字符串。
4. **盒子 C++ → 前端 JS 单向通道**：`window.onUpdateLadderActionRecommend(json)` 等 push 式回调；**前端无 JS→C++ 反向桥**（未见 `cefQuery` / `window.external` / `postMessage` / Qt webChannel）。
5. 标传传说受限时，C++ **根本不调** `onUpdateLadderActionRecommend` → 前端 state 维持初始 `{status:2, data:[]}` → 渲染受限页。
6. 判定发生在盒子 C++，不在前端 JS。
7. 推荐数据**不走 HTTP**（旧方案 CDP Fetch 拦 `FT_STANDARD` 请求永远拦不到，此点可确认）；段位数据是否走 HTTP 待侦察。
8. 旧方案 Layer 2（Frida 字符串替换 `FT_STANDARD → FT_WILD`）实机验证无效 —— 说明 C++ 判定不依赖字符串常量比对。

## 约束

- 🚫 绝不写 Hearthstone.exe 内存
- 🚫 绝不修改 HS 真实日志文件（代理读取可以）
- 🚫 绝不向网易 API 发伪造请求（只改下行响应可以）
- 🚫 绝不绕过盒子登录/账号体系
- ✅ 仅对 HSAng.exe 运行时状态做拦截/改写，不写持久化文件
- ✅ 只在 `IsStandardLegend == true` 时生效

---

## 总体架构（双阶段）

```
┌─────────────────────────────────────────────────────────┐
│  阶段 0：Frida 侦察（独立脚本，非 Hearthbot 集成）           │
│    Frida attach HSAng.exe（只 trace，不 patch）            │
│    对照组 A：狂野对局          实验组 B：标传传说受限档       │
│    trace: ExecuteJavaScript / CreateFileW / ReadFile /    │
│           ReadProcessMemory / WinHttp / WSASend           │
│    产出：docs/superpowers/recon/2026-04-17-hsbox-limit-    │
│           recon.md  + raw logs                            │
│                                                           │
│  【决策门】                                                │
│  根据对比报告定位 C++ 判定的数据源：                         │
│    a) 读 HS 日志文件                                       │
│    b) 读 HS 进程内存                                       │
│    c) 请求网易 API                                         │
│    d) C++ 内部固定逻辑                                     │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│  阶段 1：精准补丁（Hearthbot 集成）                         │
│    根据阶段 0 决策选 a/b/c/d 中的一条实现                    │
│    共同外观：HsBoxLimitBypass（BotService 内挂钩）           │
└─────────────────────────────────────────────────────────┘
```

**阶段 1 的实现细节不在本 spec 范围**；本 spec 只定义四条分支的轮廓与决策优先级，阶段 0 完成后另开 spec。

---

## 阶段 0：Frida 侦察详设

### 侦察脚本

| 文件 | 说明 |
|------|------|
| `tools/active/hsbox_limit_recon.js` | Frida JS trace 脚本 |
| `Scripts/recon_hsbox.ps1` | PowerShell 一键编排（启动 Frida、打 tag、落盘 raw log） |

调用方式：`Scripts/recon_hsbox.ps1 wild` 或 `Scripts/recon_hsbox.ps1 std-legend`，每次录制 60 秒。

**附加方式**：`frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js`（attach 运行中进程，不 spawn，避免影响启动流程）。

### Trace 目标 API

| # | API | 模块 | 观察 |
|---|------|------|------|
| 1 | `CefFrame_ExecuteJavaScript` | `libcef.dll` 导出 | 所有 C++ → JS 的调用，尤其 `window.onUpdate*` |
| 2 | `CreateFileW` / `ReadFile` | `kernel32.dll` | 是否读 HS 日志文件或其他本地 cache |
| 3 | `ReadProcessMemory` | `kernel32.dll` | 是否读 Hearthstone.exe 内存、目标地址 |
| 4 | `WinHttpOpenRequest` / `WinHttpSendRequest` | `winhttp.dll` | C++ 层直接的 HTTP（不经 CEF） |
| 5 | `WSASend` / `send` | `ws2_32.dll` | 兜底 raw socket 流量 |

每次命中记录：时间戳 + 参数 + 调用栈前 8 帧。

### 对比实验

| 组 | 场景 | 预期盒子行为 |
|----|------|-------------|
| A | 狂野对局（任意段位） | 盒子**出推荐** |
| B | 标准对局 + 受限档传说 | 盒子**不出推荐**，渲染受限页 |

每组跑 1–2 局、各录 60 秒。对比 trace log 差异找到"狂野走但标传不走"的调用路径。

### 输出

- `docs/superpowers/recon/raw/<ts>_<mode>.log` — 原始 trace
- `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md` — 对比分析报告
  - 【模式读取来源】定位到哪个 API/地址
  - 【段位读取来源】定位到哪个 API/地址
  - 【判定函数位置】HSAng.exe 偏移（若可回溯）
  - 【签名校验】HTTP 响应是否含 HMAC / 数字签名
  - 【决策】阶段 1 走 a/b/c/d 哪条

### 风险与兜底

- Frida trace 崩盒子：脚本全程 try/catch，崩即自动 detach；用户重启盒子恢复。
- UniCrashReporter 告警：trace 完毕立即 detach，不留驻。
- 网络无法触达网易 API：分支 c 无法排除；报告里显式标注。

---

## 阶段 1：四条补丁分支的决策树

### 决策优先级

```
c (CDP 改响应)  →  a (日志 shim)  →  b (RPM hook)  →  d (判定函数 hook)
   最轻              较轻              中重               最重
```

依据：对盒子进程/游戏进程的侵入性从低到高，被反作弊识别的概率同步从低到高。多分支都成立时取最上者；不成立的分支丢弃。

### 分支 a：HS 日志 shim

- **触发条件**：`CreateFileW` 命中 `H:\Hearthstone\Logs\*.log` 或 `Hearthstone_Data\output_log.txt`
- **思路**：Hearthbot 启文件 tail 代理；监控真实日志追加，替换盒子副本的段位/模式字段；通过符号链接 / NTFS 过滤 / `CreateFileW` DLL hook 让盒子读代理文件
- **新增**：`BotMain/HsBoxLogShim.cs` + `BotMain/Native/FileRedirect.cs`
- **工作量**：中（关键难点："让盒子读代理文件而不是真文件"）

### 分支 b：Frida hook 盒子 RPM

- **触发条件**：`ReadProcessMemory` 命中 Hearthstone.exe 句柄 + 固定偏移
- **思路**：Frida 常驻注入 HSAng.exe；拦截 `ReadProcessMemory` 返回路径，改写 buffer 里段位/模式字段，其他透传
- **新增**：`BotMain/HsBoxFridaPatcher.cs`（名字复用旧文件但用途不同）+ `tools/active/hsbox_rpm_patch.js`
- **工作量**：中-大（需要逆向确认段位字段在 HS 内存中的偏移，跨版本脆弱）

### 分支 c：CDP Fetch 改段位响应

- **触发条件**：`WinHttpSendRequest` 命中 `*.lushi.163.com` 或 `*.gameyw.netease.com` 的段位/排行相关 path
- **思路**：复用 `HsBoxRecommendationProvider` 现有 CDP 通道；`Fetch.enable` 拦截响应，把名次字段改成 99999；若响应含 HMAC 签名则此路不通
- **扩展**：`BotMain/HsBoxRecommendationProvider.cs`（或拆出 `BotMain/HsBoxRankSpoofer.cs`）
- **工作量**：小（基础设施复用，约 20 行 C# + 30 行 CDP Fetch 配置）

### 分支 d：Frida hook 判定函数

- **触发条件**：a/b/c 侦察均未命中；对比运行下存在明显 C++ 分支差异
- **思路**：Frida 定位判定分支的 cmp/test 指令，patch 为 jmp 或常量 ret false
- **新增**：`BotMain/HsBoxFridaPatcher.cs` + `tools/active/hsbox_judge_patch.js`
- **工作量**：大（纯逆向，无符号）

### 多数据源合取情况

侦察若发现盒子同时读多个数据源（如日志 + API 交叉校验），**先按优先级补一个**（YAGNI），实机验证不够再追加。

---

## Hearthbot 集成骨架

无论阶段 1 走哪条分支，集成方式统一。

### `HsBoxLimitBypass.cs` 外观类

- 路径：`BotMain/HsBoxLimitBypass.cs`
- 接口：`Enable(Action<string> log)` / `Disable()` / `Dispose()`
- 内部持有一个具体实现（阶段 1 决定是 CdpRankSpoofer / LogShim / FridaRpmHooker / FridaJudgePatcher 中的哪一个）
- 只在 `IsStandardLegend == true` 生效
- 实现抛异常时 **catch 并记日志**，不向上冒泡

### BotService 挂钩（还原到旧方案被删的位置）

```csharp
// BotService.cs - RequestRecommendationAsync 入口处
// 即旧 Spoofer 方案被删除的那段位置（紧邻 `if (request == null) return ...` 之后）
if (IsStandardLegend)
    _hsBoxLimitBypass.Enable(msg => Log(msg));
else
    _hsBoxLimitBypass.Disable();
```

`BotService.Dispose()` 调 `_hsBoxLimitBypass.Dispose()` 保证清理。

### 配置

- `appsettings.json` 新增 `HsBoxLimitBypass.Enabled`（默认 `true`）
- `HsBoxLimitBypass.Strategy`（默认 `"auto"`；调试用 `"cdp" / "shim" / "rpm" / "judge"` 可强制）

### 日志

- 所有日志前缀 `[LimitBypass]`
- Enable / Disable / Patch 成功 / Patch 失败各一条
- **不打印**段位/牌局等敏感数据

### 测试

- **单元测试**：`BotCore.Tests/HsBoxLimitBypassTests.cs`
  - facade Enable/Disable 幂等性
  - 具体实现抛异常时被吞并记日志
  - Dispose 顺序正确
  - 具体实现用 mock
- **集成测试**：**不写自动化**。由阶段 1 完成后人工回归覆盖四种场景：
  1. 狂野对局 → 不触发 Bypass
  2. 标传非传说（如钻石）→ 不触发 Bypass
  3. 标传传说（受限档）→ 触发 Bypass，盒子出推荐（核心验收）
  4. 对局中模式切换 → Bypass 干净 Disable

---

## 风险矩阵

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| UniCrashReporter 识别 Frida/hook 上报告警 | 中 | 中 | 分支 c 不触发；b/d 仅限 HSAng.exe，不碰 Hearthstone.exe |
| Frida 注入导致 HSAng.exe 崩溃 | 低 | 低 | try/catch + 自动 detach；不自动重注入 |
| 盒子版本升级使偏移/字符串变动 | 高 | 高（补丁失效） | a/c 不依赖 HSAng 版本；b/d 必须加版本号白名单，超范围 disable |
| 网易响应含 HMAC 签名（分支 c） | 未知 | 高（c 整条失败） | 侦察时显式验证；失败降级 a/b/d |
| 补丁生效但推荐质量不对 | 中 | 中 | a/b/c 仅改段位数值不改模式，卡池仍基于真实；阶段 1 真机验证 |
| Hearthbot 消费端（DOM 读取）不兼容改变后的推荐流 | 低 | 中 | 现有 DOM 解析已支持"有推荐"场景（狂野/非传说下都走此路径），无需改动 |

## 回退机制

三级：

1. **功能级**：`HsBoxLimitBypass.Enabled = false` → 退回旧行为
2. **进程级**：实现抛异常 → facade 吞掉 → Hearthbot 继续跑（无推荐不挂）
3. **代码级**：从 BotService 拔掉外观 → 功能关闭

三级都不需要重启游戏/盒子。

## 成功定义

- **阶段 0**：产出可决策的侦察报告（明确 C++ 判定数据源，或明确四种都不是需扩展侦察）
- **阶段 1**：标传传说受限档实机，盒子持续输出推荐；切回其他模式 Bypass 不干扰

## 放弃条件

- 阶段 0 发现 C++ 判定既不读任何 IO 也无明显分支（混淆壳 / VMP 保护） → 转"Hearthbot 自推荐"另立项
- 阶段 1 任意分支实施后真机无效且原因不可知 → 停手、写失败复盘、关闭此 spec

## 文件清单

### 阶段 0 新增
| 文件 | 类型 |
|------|------|
| `tools/active/hsbox_limit_recon.js` | Frida JS trace 脚本 |
| `Scripts/recon_hsbox.ps1` | 侦察编排 PowerShell |
| `docs/superpowers/recon/raw/*.log` | 原始 trace 落盘 |
| `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md` | 对比分析报告 |

### 阶段 1 新增 / 修改（按所选分支；交集部分必出）
| 文件 | 类型 | 何时出现 |
|------|------|---------|
| `BotMain/HsBoxLimitBypass.cs` | 新增（facade） | 所有分支必出 |
| `BotMain/BotService.cs` | 修改（挂钩点） | 所有分支必出 |
| `BotCore.Tests/HsBoxLimitBypassTests.cs` | 新增 | 所有分支必出 |
| `appsettings.json` | 修改（新增配置项） | 所有分支必出 |
| `BotMain/HsBoxLogShim.cs` + `BotMain/Native/FileRedirect.cs` | 新增 | 仅分支 a |
| `BotMain/HsBoxFridaPatcher.cs` + `tools/active/hsbox_rpm_patch.js` | 新增 | 仅分支 b |
| `BotMain/HsBoxRankSpoofer.cs`（或扩展 Provider） | 新增/扩展 | 仅分支 c |
| `BotMain/HsBoxFridaPatcher.cs` + `tools/active/hsbox_judge_patch.js` | 新增 | 仅分支 d |
