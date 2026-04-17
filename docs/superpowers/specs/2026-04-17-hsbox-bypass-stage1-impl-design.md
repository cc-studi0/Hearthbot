# 盒子标传传说受限绕过 — 阶段 1 实施设计

## 背景

阶段 0 侦察（`docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md`）已锁定判定函数：

```
HSAng.exe!MatchLogicPrivate::queryMatchAvailable(int sceneMode) → bool
```

受限时此函数返回 `false`，导致盒子加载 `about:blank` 而非真实 ladder URL，前端 React 组件渲染"AI 推荐功能实行阶梯开放"受限页。

本 spec 定义"让该函数永远返回 true"的实施方案，框架选择来自旧 spec `2026-04-17-hsbox-standard-legend-bypass-design.md` 中的"分支 d 精确版"。

## 约束

- 仅对 HSAng.exe **运行时状态**做 hook，不写持久化文件
- 不碰 Hearthstone.exe 进程
- 不绕过盒子登录 / 账号体系
- 复用阶段 0 侦察验证过的工具链（Python + frida-tools），最低化分发依赖

## 总体架构（三层）

```
┌────────────────────────────────────────────────────────────┐
│  Hearthbot (BotMain, .NET 8)                               │
│   BotService                                               │
│     - 构造时 new HsBoxLimitBypass(...)                     │
│     - Start() 末尾调用 _hsBoxLimitBypass.Start()           │
│     - Dispose() 开头调用 _hsBoxLimitBypass.Dispose()       │
│   HsBoxLimitBypass (新增 facade)                           │
│     - 拉起 Python 子进程并保持常驻                          │
│     - 监控存活、自动重启（限速）                            │
│     - 解析子进程 stdout (JSON 行) → 写主日志              │
└────────────────────────────┬───────────────────────────────┘
                             │ Process.Start
┌────────────────────────────▼───────────────────────────────┐
│  python Scripts/queryMatchAvailable_hook.py (新增)          │
│   - frida.attach('HSAng.exe') with retry                    │
│   - 监听 detach 事件 → 5s 间隔自动重连（盒子重启场景）       │
│   - 加载 tools/active/hsbox_querymatch_hook.js             │
│   - on_message: send 消息按 JSON 行 print 到 stdout        │
│   - 30 秒一次 heartbeat                                     │
│   - 退出条件: stdin EOF / fatal 消息 / 异常                │
└────────────────────────────┬───────────────────────────────┘
                             │ Frida attach (ia32)
┌────────────────────────────▼───────────────────────────────┐
│  HSAng.exe (32 位 / WOW64)                                  │
│   tools/active/hsbox_querymatch_hook.js (Frida JS)         │
│   1. Module.enumerateExports HSAng.exe                      │
│      → 找 mangled 名含 "queryMatchAvailable" 的 export     │
│   2. 找到 → Interceptor.attach(addr, { onLeave })          │
│      onLeave: 若 retval == 0 → retval.replace(1)、计数      │
│   3. 找不到 → send fatal（首版不做字符串 scan 兜底）         │
│   4. 每 20 次命中 send patch-stat                           │
└────────────────────────────────────────────────────────────┘
```

## 文件清单

### 新增

| 文件 | 角色 |
|------|------|
| `tools/active/hsbox_querymatch_hook.js` | Frida JS hook 脚本 |
| `Scripts/queryMatchAvailable_hook.py` | Python 常驻启动器 |
| `BotMain/HsBoxLimitBypass.cs` | C# facade |
| `BotCore.Tests/HsBoxLimitBypassTests.cs` | 单元测试 |

### 修改

| 文件 | 改动 |
|------|------|
| `BotMain/BotService.cs` | 构造时 new facade、Start 时 .Start()、Dispose 时 .Dispose() |
| `appsettings.json` | 新增 `HsBoxLimitBypass` 配置段 |

## 各组件细节

### Frida JS hook 脚本

`tools/active/hsbox_querymatch_hook.js`

- 入口：`locateQueryMatchAvailable()` 用 `Process.findModuleByName('HSAng.exe').enumerateExports()` 找 mangled name 匹配 `/queryMatchAvailable/i` 的 export
- 命中 → `Interceptor.attach(addr, { onLeave: function(retval) { if (retval.toInt32() === 0) retval.replace(1); } })`
- 未命中 → `send({ tag: 'fatal', msg: 'queryMatchAvailable export not found' })`。JS 脚本顶部留 `const MANUAL_RVA = null;` 字段；如果 export 失败、开发者可用 IDA 离线反编译 HSAng.exe 找到 RVA 后填入此字段，脚本会优先用 `module.base.add(MANUAL_RVA)` 作为兜底
- 计数：每 20 次命中 `send({ tag: 'patch-stat', hits, forced })`
- 启动：`send({ tag: 'hook-installed', source, symbol, addr })`
- 32 位 ABI 下 bool 返回值在 EAX 低 8 位，Frida `retval.replace(1)` 自动适配

### Python 常驻启动器

`Scripts/queryMatchAvailable_hook.py`

- 命令行：仅一个可选参数 `--script <path>`，默认 `tools/active/hsbox_querymatch_hook.js`
- 启动：`frida.attach('HSAng.exe')` 失败 → 200ms 重试，最多等 300 秒
- 加载 JS，绑 `script.on('message', on_message)`
- on_message：所有 `send()` payload 转 JSON 写 stdout
- 监听 `session.on('detached')` → 5 秒后重新 attach + 重新加载脚本
- 每 30 秒 send 一条 `{ tag: 'heartbeat', hits, forced }`，hits/forced 由 Python 累计 JS 上报的 `patch-stat` 增量得到
- 退出条件：
  - stdin EOF（Hearthbot kill 子进程时关闭 pipe）
  - JS 端 send `{ tag: 'fatal' }` → log 后退出码 2
  - 任何 unhandled exception → 退出码 3

### Hearthbot facade — `HsBoxLimitBypass.cs`

公共接口：

```csharp
internal sealed class HsBoxLimitBypass : IDisposable
{
    public HsBoxLimitBypass(Action<string> log, IBypassProcessHost host, BypassConfig cfg);
    public void Start();    // 启动子进程，幂等
    public void Stop();     // kill 子进程，幂等
    public void Dispose();  // 等价 Stop
    public BypassStatus Status { get; }    // Stopped/Starting/Running/Failed
}

internal interface IBypassProcessHost  // 可 mock
{
    IBypassProcess Spawn(ProcessStartInfo psi);
}
```

内部职责：

- 启动 Python 子进程（`PythonExecutable + queryMatchAvailable_hook.py`）
- stdout 行 → JSON 解析 → 主日志（前缀 `[LimitBypass]`）；非法 JSON 当 raw 行原样记
- stderr 行 → 主日志（前缀 `[LimitBypass][stderr]`）
- 子进程意外退出 → 自动重启，限速 1 分钟内 ≤3 次；超过 → Status=Failed、停止重启
- 收到 `tag:fatal` → log 后调 Stop（不再重启）
- 异常吞并 + log，**不向 BotService 抛**

### BotService 集成

```csharp
// BotService 构造里
_hsBoxLimitBypass = new HsBoxLimitBypass(
    msg => Log(msg),
    new DefaultBypassProcessHost(),
    _config.HsBoxLimitBypass);

// BotService.Start() 末尾
if (_config.HsBoxLimitBypass.Enabled)
    _hsBoxLimitBypass.Start();

// BotService.Dispose() 开头
_hsBoxLimitBypass?.Dispose();
```

不按 `IsStandardLegend` 触发 enable/disable —— 常驻策略，hook 始终在，非受限场景 retval 本来就是 1，replace(1) 是 noop。

### 配置（appsettings.json）

```json
"HsBoxLimitBypass": {
  "Enabled": true,
  "PythonExecutable": "python",
  "MaxRestartsPerMinute": 3
}
```

`PythonExecutable`：默认 PATH 下的 `python`，可改绝对路径。
`Enabled=false` 即一键关闭整个功能。

### 日志样例

```
[LimitBypass] starting python launcher pid=12345
[LimitBypass] hook-installed source=export symbol=?queryMatchAvailable@... addr=0x6abcde
[LimitBypass] heartbeat hits=120 forced=15
[LimitBypass] subprocess exited code=1, restart 1/3
```

## 测试策略

### 单元测试（`BotCore.Tests/HsBoxLimitBypassTests.cs`）

| 测试 | 验证 |
|------|------|
| StartIsIdempotent | 连续 Start 多次只起一个子进程 |
| DisposeKillsSubprocess | Dispose 后 Status=Stopped、子进程被 kill |
| ProcessSpawnFailureSwallowed | mock spawn 抛异常 → facade 不抛、Status=Failed、写错误日志 |
| RestartsLimitedPerMinute | mock 子进程连挂 4 次（5s 内）→ 第 4 次不重启、Status=Failed |
| MalformedJsonStdoutHandled | mock 子进程吐非 JSON → 不崩、按 raw line 记日志 |
| FatalMessageStopsRestart | mock 子进程 send fatal → facade 不再自动重启 |

Mock 策略：`IBypassProcessHost` 接口包装 `Process.Start/Kill/StandardOutput`，测试 mock 它。不真起 Python。

### 实机回归（人工，4 场景）

| # | 场景 | 期望 |
|---|------|------|
| 1 | 狂野对局（任意段位） | 盒子正常出推荐 |
| 2 | 标传非传说（如钻石） | 盒子正常出推荐 |
| 3 | **标传传说受限档** | **盒子出推荐**（核心验收） |
| 4 | 对局中切模式 / 退出 Hearthbot | bypass 干净 Stop，HSAng.exe 不崩 |

每场景查 log：含 `hook-installed source=export` + 至少一条 `patch-stat` 命中。

## 三级回退

1. **功能级**：`appsettings.json` 设 `HsBoxLimitBypass.Enabled=false` → 不启动 Python，盒子退回受限态
2. **进程级**：facade 启动失败 / Python 崩溃 → Hearthbot 主流程不受影响（继续不带 bypass 跑）
3. **代码级**：从 `BotService` 删 `_hsBoxLimitBypass.Start()` 调用 → 整功能关闭

三级都不需要重启游戏 / 盒子（除非要让本次对局生效，那需要 Hearthbot 重启）。

## 风险

| 风险 | 缓解 |
|------|------|
| Python / frida-tools 未安装 | spawn 失败时 facade Status=Failed、Hearthbot 继续跑（不影响主流程）；README/文档需写明依赖 |
| `queryMatchAvailable` 没 export（mangled 名找不到） | JS 脚本 send fatal；首版用 IDA 离线找 RVA 填到脚本（spec 留空字段）；后续迭代加字符串 scan 兜底 |
| 盒子升级使 export 名变化 | `enumerateExports` 用模糊正则 `/queryMatchAvailable/i`，对 mangled 后缀变动鲁棒；如果整体改名要重新侦察 |
| 盒子重启时 Frida session detach | Python 启动器内置 5s 节流自动重连 |
| C# 子进程被孤立（Hearthbot 异常退出未 Dispose） | Python 启动器在 stdin EOF 时主动退出（OS 关 pipe 即 EOF） |

## 成功定义

- 标传传说受限档实机：盒子出推荐
- 单元测试全过
- 切回其他模式 bypass 不影响
- Hearthbot 退出后 HSAng.exe 状态干净（不崩、不挂载残留 Frida agent）

## 放弃条件

- export 找不到 + IDA 离线 RVA（填入 `MANUAL_RVA`）也定位不到 → 转字符串 scan + xref 反推（迭代）
- onLeave 改 retval 实测无效（盒子仍受限） → 转 `Interceptor.replace` 全函数替换；再失败考虑改 hook 调用方分支指令
