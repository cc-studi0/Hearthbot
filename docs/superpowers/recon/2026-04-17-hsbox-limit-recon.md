# 盒子标传传说受限 — 侦察报告（阶段0）

**侦察日期**：2026-04-17
**HSAng.exe 版本**：4.0.4.314（来自 CDP UA `HSAng/4.0.4.314/2a80cc29-13a4-4949-ad55-3f9814e4a498`）
**HSAng.exe 架构**：ia32（32 位 / WOW64）
**目标账号档位**：标准模式传说受限档（实测 49000 名时受限、51000 名时正常 → 5 万阈值生效）
**侦察工具**：Frida 17.9.1（Python SDK） + 自研 trace 脚本

## 原始数据

| 标签 | 路径 | 时长 | 事件数 | 错误 |
|------|------|------|--------|------|
| smoke 基线（盒子挂机） | `raw/20260417_112657_smoke.log` | 30s | 566 | 0 |
| 第一次 wild 对局 | `raw/20260417_112950_wild.log` | 90s | 1709 | 0 |
| 第一次 std-legend 对局 | `raw/20260417_190421_std-legend.log` | 90s | 1786 | 0 |
| 第二次 wild 对局（去 filter） | `raw/20260417_191455_wild.log` | 90s | 590 | 0 |
| 第二次 std-legend 对局（去 filter） | `raw/20260417_191706_std-legend.log` | 90s | 279 | 0 |
| **std-legend 启动期**（最关键） | `raw/20260417_192643_std-legend-startup.log` | 180s | 1907 | 0 |

外部数据（盒子日志）：
- `C:\Users\qq324\AppData\Local\Temp\Log\HSAParasiteMother.log`（注入 dll 视角）
- `C:\Users\qq324\AppData\Local\Temp\MatchLogic_2026-04-17.log`（盒子主进程视角）

## Hook 覆盖

最终脚本 `tools/active/hsbox_limit_recon.js` 覆盖以下 API：

| API | 模块 | 节流 | 说明 |
|-----|------|------|------|
| CreateFileW | kernel32 | 每路径 ≤5 | 所有文件/管道客户端打开 |
| ReadProcessMemory | kernel32 | 每地址 ≤3 | 跨进程内存读 |
| WinHttpSendRequest | winhttp | 无 | C++ 层 HTTP |
| WinHttpConnect | winhttp | 无 | HTTP 目标主机 |
| OpenFileMappingW | kernel32 | 无 | 命名共享内存（client） |
| CreateFileMappingW | kernel32 | 无（仅 named） | 命名共享内存（server） |
| MapViewOfFile / Ex | kernel32 | 每 handle:size ≤3 | 共享内存映射 |
| CreateNamedPipeW | kernel32 | 无 | 命名管道（server） |

## 调用次数对比

| tag | wild-1 | std-1 | wild-2 | std-2 | startup |
|-----|--------|-------|--------|-------|---------|
| init-hook | 4 | 4 | 9 | 9 | 9 |
| CreateFileW | 1704 | 1781 | 134 | 152 | 715 |
| OpenFileMappingW | — | — | 0 | 0 | 0 |
| CreateFileMappingW | — | — | 0 | 0 | 1 |
| MapViewOfFile | — | — | 422 | 93 | 1100 |
| MapViewOfFileEx | — | — | 0 | 0 | 1 |
| CreateNamedPipeW | — | — | 24 | 24 | 80 |
| ReadProcessMemory | 0 | 0 | 0 | 0 | 0 |
| WinHttpSendRequest | 0 | 0 | 0 | 0 | 0 |
| WinHttpConnect | 0 | 0 | 0 | 0 | 0 |

（— 表示该轮录制此 hook 尚未加入）

**关键空集**：所有 5 轮录制中 `ReadProcessMemory = 0`、`WinHttpSendRequest = 0`、`WinHttpConnect = 0`。

## 关键发现

### 发现 1：`HearthstoneOfficialAddon\config.ini` 是红鲱鱼

第一轮对比（wild-1 vs std-1）：仅 std-legend 打开此文件。**当时误判为判定关键**。
第二轮对比（wild-2 vs std-2）：仅 wild 打开此文件，反向出现。
**结论**：访问时机与受限判定**无稳定关联**，可能是盒子启动 / HS 进程启动 / 某种偶发轮询。**排除作为判定信号**。

### 发现 2：盒子既不发 HTTP，也不读 HS 进程内存（HSAng 主进程视角）

5 轮 670+ 秒录制：
- 0 次 WinHttp
- 0 次 ReadProcessMemory

但**用户实测**盒子能感知 49000 名 vs 51000 名差异（粒度极细）→ 数据**必定**来自某处。

### 发现 3：盒子通过命名管道 IPC 与注入到 HS 进程的 dll 通信

`HSAParasiteMother.log`：
```
[19:27:12.991] ParasiteMother::Context::startup: service inject: succ=true
[19:27:12.992] ParasiteMother::Context::onConnected: named pipe connected
[19:27:13.385] ParasiteMother::Context::sendRequest succ: cmd=CmdInitCommonService
[19:27:13.387] ParasiteMother::Context::onReadyRead: service receiveResponse succ: cmd=CmdInitCommonService
```

证据链：
1. 盒子**确实**注入 HS 进程，注入 dll 代号 **ParasiteMother**
2. 注入 dll ↔ HSAng 主进程通过**命名管道**通信
3. 协议是 `Cmd*` 风格的请求/响应

**为什么我们的 trace 没抓到这条管道？**
- `CreateNamedPipeW` 在 startup 录制中命中 80 次，**全部**是 `\\.\pipe\mojo.*`（Chromium IPC）和 `\\.\pipe\qt-*`（Qt 内部）—— 这条业务管道**不在其中**
- 推测：盒子使用 `CreatePipe`（匿名管道）+ 把 handle 通过 `DuplicateHandle` 跨进程传递；或使用未 hook 的 `NtCreateNamedPipeFile` 直接系统调用绕过 Win32 层

**段位数据流**（推断）：
```
HS 内存（rank/mode 真实值）
  ↓ 注入的 ParasiteMother dll 进程内 deref（不走 RPM）
  ↓ 序列化成 Cmd* 请求/响应
  ↓ 命名管道（我们的 hook 漏抓）
HSAng 主进程
  ↓ MatchLogicPrivate 解析
  ↓ 判定 → 决定加载真 URL 还是 about:blank
```

### 发现 4：判定函数定位 — `HSAng.exe!MatchLogicPrivate::queryMatchAvailable`

`MatchLogic_2026-04-17.log` 直接揭示判定时序：
```
[19:27:12.836] MatchLogicPrivate::queryMatchAvailable: 3
[19:27:13.389] onParasiteMotherInitCommonServiceResponsed
[19:27:15.604] match view load started: about:blank   ← 受限时加载空白页
[19:27:15.604] match view load finished: about:blank
```

**判定函数**：`MatchLogicPrivate::queryMatchAvailable(int sceneMode)`
- 输入：`sceneMode`（gameSceneMode enum，观测到值 0/768/1024/1792 等位组合）
- 输出（推断）：bool，决定 match view 加载真的 ladder URL 还是 `about:blank`
- 受限路径：`queryMatchAvailable` 返回 false → match view 装载空白页 → 前端 React 组件检测到无 push 数据 → 渲染 `<受限文案>` 组件

**这是阶段 1 的精确目标**。

## HTTP 响应签名检查

不适用 —— 盒子主进程在 670+ 秒侦察期间**未发出任何 WinHttp 请求**，分支 c（CDP Fetch 改响应）整体不可行。

## 数据源定位结论

勾选 C++ 判定数据源：

- [ ] (a) HS 日志文件 — 排除：Power.log/Decks.log 不含段位信息
- [ ] (b) HS 进程内存（跨进程 RPM）— 排除：盒子注入 dll 走进程内 deref，不用 RPM
- [ ] (c) 网易 API（HTTP）— 排除：670+ 秒 0 WinHttp
- [x] **(d) C++ 内部固定逻辑 + 命名管道 IPC 输入** — 命中
  - 数据从 HS 内存 → 注入 dll → 命名管道 → HSAng → `MatchLogicPrivate::queryMatchAvailable`
  - 真正的判定汇聚点是 `queryMatchAvailable` 这一个函数

## 阶段 1 补丁方向决策

**首选分支**：**d 的精确版** — Frida hook `HSAng.exe!MatchLogicPrivate::queryMatchAvailable`，强制返回 true（或等价的"放行"语义值）。

**理由**：
1. 函数名已知（来自 MatchLogic 日志），定位地址不需要从零逆向
2. 单一函数 hook，比"hook 整个判定流程"工作量小一个数量级
3. 不碰 HS 进程，不碰段位读取链路，副作用最小
4. 不依赖盒子前端 JS 结构，盒子前端升级也不会影响

**放弃的分支**：a / b / c

**放弃理由**：见上文"数据源定位结论"。

## 阶段 1 实施前置（要在新 plan 里覆盖）

1. **定位函数地址**：
   - 优先尝试 `HSAng.exe.enumerateExports().find(/queryMatchAvailable/)` 或 `enumerateSymbols`
   - 若无导出/符号 → 用 `Memory.scanSync` 扫日志写出动作里的格式串 `MatchLogicPrivate::queryMatchAvailable: %d`，从写日志的 caller 反推 `queryMatchAvailable` 调用处，再上溯找函数入口
   - 兜底：用 IDA / Ghidra 静态分析 HSAng.exe，根据函数名字符串定位

2. **patch 策略选项**（按风险从低到高）：
   - 在函数入口设 `Interceptor.replace`，直接 return `true` 等价值（最干净）
   - 在函数 retn 前 `Interceptor.attach.onLeave`，篡改返回值（保留原函数副作用，安全降级）
   - 若函数有多个 ret 路径或调用约定复杂 → patch 调用方处的判定分支

3. **生命周期**：
   - 仅在 `IsStandardLegend == true` 时 enable
   - 切出标传传说立即 disable，恢复原函数行为
   - 盒子重启 → Hearthbot 监测到 PID 变化重新 attach + 重新 hook

4. **风险与回退**：
   - UniSDK CrashReporter 监控：注入 + replace 触发概率高于纯 trace；首次启用建议小流量测试
   - 失败兜底：facade 层 try/catch，hook 失败不影响 Hearthbot 主流程
   - 关闭开关：`HsBoxLimitBypass.Enabled = false` 一键退回受限态

## 风险声明

- 命名管道通道**未在 trace 中观察到**（所有命名管道都是 mojo/qt 内部）。这意味着我们对 IPC 协议细节没有直接证据，仅从 ParasiteMother log 的"named pipe connected"字样推断
- `queryMatchAvailable` 的具体签名（参数类型、返回类型）需在阶段 1 用 Frida 实测确认
- 阶段 1 实施时若发现 hook 后盒子仍受限，可能存在二次校验（例如 push 数据时再次校验）—— 此时需补侦察 ParasiteMother 协议层

## 放弃条件

如果阶段 1 实施后仍无法绕过受限：
- 转 ParasiteMother dll 协议侦察（需 attach Hearthstone.exe 看注入 dll 的命名管道行为，**这违反原约束**"不碰游戏进程"，需要重新讨论）
- 或转"Hearthbot 自推荐"另立项

## 附：相关侦察工件

- 脚本：`tools/active/hsbox_limit_recon.js`、`Scripts/recon_hsbox.ps1`、`Scripts/recon_hsbox.py`
- 一次性分析工具：`tools/active/analyze_recon.py`（Task 11 时清理或归档）
- 原始 log：`docs/superpowers/recon/raw/*.log`
