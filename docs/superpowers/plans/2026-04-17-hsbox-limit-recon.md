# 盒子标传传说推荐受限 — 阶段0侦察实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 Frida 侦察 HSAng.exe（盒子主进程），定位盒子 C++ 判定"标传传说是否受限"的数据源（HS日志文件 / HS进程内存 / 网易API / C++内部逻辑），产出决策报告供阶段 1 选择精准补丁分支。

**Architecture:** 两部分独立工件：① 一份 Frida JS trace 脚本（hook `CreateFileW`/`ReadProcessMemory`/`WinHttpSendRequest`/`CefFrame::ExecuteJavaScript`），只观察不改写；② 一份 PowerShell 编排脚本，按场景参数（`wild` / `std-legend`）自动启动 Frida、录制指定秒数、落盘到 raw log 目录。产出为两组对比 log + 一份 Markdown 分析报告，不改动 Hearthbot 主项目源码。

**Tech Stack:** Frida CLI（已装于 Python 313 Scripts 下），Frida JS（Interceptor / DebugSymbol / Backtracer API），Windows PowerShell 5.1+。

**Spec 来源：** `docs/superpowers/specs/2026-04-17-hsbox-standard-legend-bypass-design.md`

**范围：** 本 plan **只**覆盖阶段 0 侦察。阶段 1（补丁实现）在侦察报告产出后另立 plan。

---

## 文件结构

| 文件 | 用途 |
|------|------|
| `tools/active/hsbox_limit_recon.js` | Frida JS trace 脚本（新增） |
| `Scripts/recon_hsbox.ps1` | PowerShell 编排（新增） |
| `docs/superpowers/recon/raw/<ts>_<mode>.log` | 原始 trace 落盘 |
| `docs/superpowers/recon/raw/<ts>_<mode>.log.err` | Frida stderr |
| `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md` | 对比分析报告（最终交付） |

**不修改** 任何 `BotMain/` / `BotCore/` 源码；本阶段产出纯工具 + 文档。

## 工作原则

- **侦察脚本无"单元测试"**：Frida hook 的正确性只能在运行时观察输出验证。每个 hook 点的验证步骤即"加载脚本 + 触发动作 + 观察是否命中"。
- **增量构建**：脚本按 hook 点分 4 个任务累加，每加一个 hook 都要在空闲盒子上"加载即不崩"验证一次。
- **频繁提交**：每完成一个 hook 点命中验证即 commit，便于失败回退。

## 前置条件

- 盒子已登录并处于能开对局状态
- Hearthstone 游戏可启动（侦察对局不需要 Hearthbot 介入，人工操作即可）
- 侦察账号处于**受限档**（传说 5 万名以内且当前日期 11-20 号；或月末且 3 万名以内）。如果账号名次不够高，需要先冲到受限档才能跑实验组 B。
- 管理员 PowerShell（Frida attach HSAng.exe 需要与目标同等权限）

---

## Task 1：建立目录结构和 Frida 脚本最小骨架

**Files:**
- Create: `tools/active/hsbox_limit_recon.js`
- Create: `docs/superpowers/recon/raw/.gitkeep`
- Create: `docs/superpowers/recon/.gitkeep`

- [ ] **Step 1：创建输出目录占位文件**

```bash
mkdir -p "docs/superpowers/recon/raw"
printf "" > "docs/superpowers/recon/.gitkeep"
printf "" > "docs/superpowers/recon/raw/.gitkeep"
```

- [ ] **Step 2：写最小 Frida 脚本骨架**

创建 `tools/active/hsbox_limit_recon.js`：

```javascript
'use strict';

// ============================================================================
// 盒子受限推荐侦察脚本 (阶段0 - 只观察，不改写)
// 用法：frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8
//
// 由 Scripts/recon_hsbox.ps1 调度启动。每次 attach 60 秒左右，对照组/实验组
// 分别跑一次，生成可 diff 的 raw log。
// ============================================================================

const T0 = Date.now();

function log(tag, payload) {
    const ts = ((Date.now() - T0) / 1000).toFixed(3);
    const line = JSON.stringify({ ts: ts, tag: tag, payload: payload });
    console.log(line);
}

log('init', { pid: Process.id, arch: Process.arch, ready: true });
```

- [ ] **Step 3：对空闲盒子做一次冒烟测试**

确认 HSAng.exe 正在运行，然后在**管理员 PowerShell** 中运行：

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

预期输出包含一行类似：

```
{"ts":"0.012","tag":"init","payload":{"pid":12345,"arch":"x64","ready":true}}
```

**验证点**：
- 命中 init 行 = 脚本加载成功
- 盒子未崩溃 = 钩子加载不致命
- 按 Ctrl+C 即可退出

**如果**：
- "Failed to attach: unable to find process" → 盒子未启动，启动后重试
- "Access denied" → 未用管理员 PowerShell
- Frida 加载时盒子崩 → 本步 PASS 的定义不成立，重新审查脚本语法

- [ ] **Step 4：提交骨架**

```bash
git add tools/active/hsbox_limit_recon.js docs/superpowers/recon/.gitkeep docs/superpowers/recon/raw/.gitkeep
git commit -m "recon: Frida侦察脚本骨架 + 输出目录"
```

---

## Task 2：添加 backtrace 工具 + 命中过滤工具

**Files:**
- Modify: `tools/active/hsbox_limit_recon.js`

- [ ] **Step 1：在 log() 之后、init 之前追加 backtrace 工具函数**

编辑 `tools/active/hsbox_limit_recon.js`，在 `log('init', ...)` 之前插入：

```javascript
function backtrace(ctx, depth) {
    if (!depth) depth = 8;
    try {
        return Thread.backtrace(ctx, Backtracer.ACCURATE)
            .slice(0, depth)
            .map(DebugSymbol.fromAddress)
            .map(function (s) {
                const mod = s.moduleName || '<?>';
                const nm = s.name || ('+0x' + s.address.toString(16));
                return mod + '!' + nm;
            });
    } catch (e) {
        return ['<bt-failed:' + e.message + '>'];
    }
}

function safeReadUtf16(ptr, maxLen) {
    if (maxLen === undefined) maxLen = 1024;
    try {
        if (ptr.isNull()) return null;
        return ptr.readUtf16String(maxLen);
    } catch (e) {
        return '<read-failed:' + e.message + '>';
    }
}

function safeReadAnsi(ptr, maxLen) {
    if (maxLen === undefined) maxLen = 1024;
    try {
        if (ptr.isNull()) return null;
        return ptr.readAnsiString(maxLen);
    } catch (e) {
        return '<read-failed:' + e.message + '>';
    }
}
```

- [ ] **Step 2：冒烟再跑一次确认脚本仍能加载**

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

预期：init 行依旧出现，无语法错误，按 Ctrl+C 退出。

- [ ] **Step 3：提交**

```bash
git add tools/active/hsbox_limit_recon.js
git commit -m "recon: 添加backtrace与安全字符串读取工具函数"
```

---

## Task 3：Hook CreateFileW（观察文件访问）

**Files:**
- Modify: `tools/active/hsbox_limit_recon.js`

- [ ] **Step 1：在 init 日志行之前追加 CreateFileW hook**

```javascript
// ---- CreateFileW：观察盒子读哪些文件（尤其 Hearthstone 日志） ----
(function hookCreateFileW() {
    const addr = Module.findExportByName('kernel32.dll', 'CreateFileW');
    if (!addr) {
        log('init-warn', { msg: 'CreateFileW export not found' });
        return;
    }
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const path = safeReadUtf16(args[0], 520);
            if (!path) return;
            // 只报 Hearthstone 相关或 .log 结尾，减噪
            if (!/hearthstone|\\logs\\|\.log$|output_log|achievements|power/i.test(path)) return;
            this._path = path;
            this._ctx = this.context;
        },
        onLeave: function (retval) {
            if (!this._path) return;
            log('CreateFileW', {
                path: this._path,
                handle: retval.toString(),
                bt: backtrace(this._ctx)
            });
        }
    });
    log('init-hook', { name: 'CreateFileW', at: addr.toString() });
})();
```

**放置位置**：紧跟 `safeReadAnsi` 函数定义之后，`log('init', ...)` 之前。

- [ ] **Step 2：启动盒子任意页面，跑脚本 15 秒，观察文件命中**

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

在盒子里随意点一下"天梯记牌器"切换页面。

**预期**：可能看到若干 `{"tag":"CreateFileW",...}` 行（如果盒子在进行日志轮询）；也可能没命中（盒子此刻没读 HS 日志）。两者都 OK —— 关键是**不崩且输出格式正确**。

Ctrl+C 退出。

- [ ] **Step 3：提交**

```bash
git add tools/active/hsbox_limit_recon.js
git commit -m "recon: hook CreateFileW观察盒子文件访问"
```

---

## Task 4：Hook ReadProcessMemory（观察跨进程内存读）

**Files:**
- Modify: `tools/active/hsbox_limit_recon.js`

- [ ] **Step 1：在 CreateFileW hook 之后追加 RPM hook**

```javascript
// ---- ReadProcessMemory：观察盒子是否读 Hearthstone.exe 内存 ----
(function hookRPM() {
    const addr = Module.findExportByName('kernel32.dll', 'ReadProcessMemory');
    if (!addr) {
        log('init-warn', { msg: 'ReadProcessMemory export not found' });
        return;
    }
    // 只对大小 >= 4 且命中次数节流（每地址最多 3 次），否则 log 会淹没
    const seen = {};
    Interceptor.attach(addr, {
        onEnter: function (args) {
            this._h = args[0];
            this._base = args[1];
            this._size = args[2].toInt32();
            this._ctx = this.context;
        },
        onLeave: function (retval) {
            if (retval.toInt32() === 0) return;  // 失败的读无信息量
            if (this._size <= 0 || this._size > 0x10000) return;  // 过滤异常
            const key = this._base.toString() + ':' + this._size;
            seen[key] = (seen[key] || 0) + 1;
            if (seen[key] > 3) return;
            log('RPM', {
                handle: this._h.toString(),
                addr: this._base.toString(),
                size: this._size,
                nth: seen[key],
                bt: backtrace(this._ctx, 6)
            });
        }
    });
    log('init-hook', { name: 'ReadProcessMemory', at: addr.toString() });
})();
```

- [ ] **Step 2：空跑 10 秒确认不崩**

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

**预期**：
- 如果 Hearthstone 未运行 → 大概率无 RPM 命中
- 如果 Hearthstone 运行 → 可能看到 RPM 读游戏内存的命中（不一定与段位相关，空跑阶段只验证 hook 框架本身不崩）

Ctrl+C 退出。

- [ ] **Step 3：提交**

```bash
git add tools/active/hsbox_limit_recon.js
git commit -m "recon: hook ReadProcessMemory观察跨进程内存读"
```

---

## Task 5：Hook WinHttpSendRequest（观察 C++ 层 HTTP）

**Files:**
- Modify: `tools/active/hsbox_limit_recon.js`

- [ ] **Step 1：追加 WinHttp hook**

在 RPM hook 之后：

```javascript
// ---- WinHttpSendRequest：观察盒子 C++ 层（非 CEF）直接发的 HTTP ----
(function hookWinHttp() {
    const addr = Module.findExportByName('winhttp.dll', 'WinHttpSendRequest');
    if (!addr) {
        log('init-warn', { msg: 'WinHttpSendRequest export not found' });
        return;
    }
    Interceptor.attach(addr, {
        onEnter: function (args) {
            // 参数 2 (args[1]) 是 lpszHeaders (LPCWSTR)，可能为 null
            // 参数 4 (args[3]) 是 lpOptional (LPVOID)，可能是 POST body
            const headers = safeReadUtf16(args[1], 512);
            log('WinHttpSendRequest', {
                hReq: args[0].toString(),
                headers: headers,
                bt: backtrace(this.context)
            });
        }
    });
    log('init-hook', { name: 'WinHttpSendRequest', at: addr.toString() });
})();

// 同时 hook WinHttpConnect 能看到目标主机名
(function hookWinHttpConnect() {
    const addr = Module.findExportByName('winhttp.dll', 'WinHttpConnect');
    if (!addr) return;
    Interceptor.attach(addr, {
        onEnter: function (args) {
            const host = safeReadUtf16(args[1], 256);
            const port = args[2].toInt32();
            log('WinHttpConnect', { host: host, port: port });
        }
    });
    log('init-hook', { name: 'WinHttpConnect', at: addr.toString() });
})();
```

- [ ] **Step 2：空跑确认不崩**

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

**预期**：盒子如果后台有心跳/配置拉取，可能看到 `WinHttpConnect` 命中（host 可能是 `*.lushi.163.com` 等）。

- [ ] **Step 3：提交**

```bash
git add tools/active/hsbox_limit_recon.js
git commit -m "recon: hook WinHttp观察C++层HTTP调用"
```

---

## Task 6：Hook CefFrame::ExecuteJavaScript（观察 C++→JS push）

> **最关键的 hook**。这里的输出能直接告诉我们：狂野时 C++ 推了哪些 `window.onUpdate*`，标传受限时哪些没推。

**Files:**
- Modify: `tools/active/hsbox_limit_recon.js`

- [ ] **Step 1：追加 ExecuteJavaScript hook**

在 WinHttp hook 之后：

```javascript
// ---- CefFrame::ExecuteJavaScript：C++ 推给 CEF V8 执行的所有 JS ----
(function hookExecJS() {
    const libcef = Process.findModuleByName('libcef.dll');
    if (!libcef) {
        log('init-warn', { msg: 'libcef.dll not loaded' });
        return;
    }
    // 寻找符号：优先 C API（cef_frame_t_execute_javascript），否则用残缺 C++ 符号
    const exp = libcef.enumerateExports().find(function (e) {
        return /execute[_]?java[sS]cript/i.test(e.name);
    });
    if (!exp) {
        log('init-warn', { msg: 'ExecuteJavaScript export not found in libcef.dll' });
        return;
    }
    Interceptor.attach(exp.address, {
        onEnter: function (args) {
            // 两种可能的签名：
            //   C API:  void cef_frame_t_execute_javascript(cef_frame_t* self,
            //             const cef_string_t* script,
            //             const cef_string_t* script_url,
            //             int start_line)
            //   C++ API: void CefFrame::ExecuteJavaScript(const CefString& script, ...)
            // cef_string_t 在 Windows 是 cef_string_utf16_t { char16* str; size_t length; ... }
            // 两种情况下第 2 个参数（args[1]）都指向 cef_string_t
            try {
                const strPtr = args[1].readPointer();
                const jsLen = args[1].add(Process.pointerSize).readULong();
                const maxRead = Math.min(jsLen, 400);
                const js = strPtr.readUtf16String(maxRead);
                log('ExecuteJS', {
                    js: js,
                    len: jsLen,
                    bt: backtrace(this.context, 10)
                });
            } catch (e) {
                log('ExecuteJS', { err: 'parse-failed:' + e.message });
            }
        }
    });
    log('init-hook', { name: 'ExecuteJavaScript', at: exp.address.toString(), sym: exp.name });
})();
```

- [ ] **Step 2：空跑确认命中并记录符号名**

```powershell
frida -n HSAng.exe -l tools/active/hsbox_limit_recon.js --runtime=v8 -q --no-pause
```

观察启动日志中的 `init-hook` 行，找到：

```
{"ts":"...","tag":"init-hook","payload":{"name":"ExecuteJavaScript","at":"0x...","sym":"<SYMBOL_NAME>"}}
```

把 `sym` 字段记下来备查（例如可能是 `cef_frame_t_execute_javascript` 或 `?ExecuteJavaScript@CefFrameImpl@...`）。如果未出现此行而是 `init-warn: ExecuteJavaScript export not found`，说明 libcef.dll 未 export 该符号——停止，把此情况记入后续侦察报告的"非常规路径"小节并手动找工程师排查（通常意味着盒子用了静态链接的 CEF 或自定义 wrapper）。

然后点击盒子几个页面/按钮。**预期**：出现多条 `ExecuteJS` 命中行，每行 `payload.js` 字段能看到真实 JS 片段（可能是 `window.fSetMode(...)` / `window.onSwitchModule(...)` 等）。

**如果命中 js 字段全是 `<read-failed>` 或乱码**：

说明签名猜错了（可能是 C++ ABI，参数顺序不同）。降级方案：把 `args[1].readPointer()` 改为 `args[1]` 直接作为 `cef_string_t*`，重试：

```javascript
// 降级：认为 args[1] 本身就是 cef_string_t*
const strPtr = args[1].readPointer();   // cef_string_t.str
const jsLen = args[1].add(Process.pointerSize).readULong();
```

上面代码已经是这个写法。如果还不行，再降级成：

```javascript
// 更激进：直接假设 args[1] 是 const wchar_t* （旧 API）
const js = safeReadUtf16(args[1], 400);
```

试过至少命中一条能读懂的 js 即 PASS。

- [ ] **Step 3：提交**

```bash
git add tools/active/hsbox_limit_recon.js
git commit -m "recon: hook CefFrame::ExecuteJavaScript观察C++→JS push"
```

---

## Task 7：PowerShell 编排脚本

**Files:**
- Create: `Scripts/recon_hsbox.ps1`

- [ ] **Step 1：写 PowerShell 编排脚本**

```powershell
<#
.SYNOPSIS
    盒子受限侦察录制脚本（阶段0）。
.DESCRIPTION
    以指定场景标签启动 Frida，附加 HSAng.exe，加载 hsbox_limit_recon.js，
    录制指定秒数后自动停止；log 落盘到 docs/superpowers/recon/raw/。
.PARAMETER Mode
    场景标签：'wild'（狂野对局）或 'std-legend'（标传受限档对局）。
.PARAMETER DurationSec
    录制秒数，默认 60。
.EXAMPLE
    .\Scripts\recon_hsbox.ps1 -Mode wild
    .\Scripts\recon_hsbox.ps1 -Mode std-legend -DurationSec 90
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('wild', 'std-legend')]
    [string]$Mode,
    [int]$DurationSec = 60
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Script   = Join-Path $RepoRoot 'tools\active\hsbox_limit_recon.js'
$OutDir   = Join-Path $RepoRoot 'docs\superpowers\recon\raw'

if (-not (Test-Path $Script)) {
    throw "找不到 Frida 脚本：$Script"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Ts       = Get-Date -Format 'yyyyMMdd_HHmmss'
$OutFile  = Join-Path $OutDir "${Ts}_${Mode}.log"
$ErrFile  = "$OutFile.err"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  盒子受限侦察录制" -ForegroundColor Cyan
Write-Host "  场景:    $Mode" -ForegroundColor Cyan
Write-Host "  时长:    $DurationSec 秒" -ForegroundColor Cyan
Write-Host "  输出:    $OutFile" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "前置检查：" -ForegroundColor Yellow
Write-Host "  1. HSAng.exe 必须已启动" -ForegroundColor Yellow
Write-Host "  2. 本脚本必须在【管理员】PowerShell 中运行" -ForegroundColor Yellow
Write-Host "  3. $($Mode) 场景：" -ForegroundColor Yellow
if ($Mode -eq 'wild') {
    Write-Host "     -> 进入【狂野】对局；录制期间盒子应显示推荐" -ForegroundColor Yellow
} else {
    Write-Host "     -> 进入【标准+传说受限档】对局；录制期间盒子应显示'阶梯开放'受限页" -ForegroundColor Yellow
}
Write-Host ""
Read-Host "准备好后按回车开始录制"

$FridaArgs = @(
    '-n', 'HSAng.exe',
    '-l', $Script,
    '--runtime=v8',
    '-q',
    '--no-pause'
)

Write-Host "启动 Frida..." -ForegroundColor Green
$proc = Start-Process -FilePath 'frida' -ArgumentList $FridaArgs `
    -RedirectStandardOutput $OutFile `
    -RedirectStandardError  $ErrFile `
    -NoNewWindow -PassThru

Write-Host "录制中 ($DurationSec 秒)... 请在对局里正常操作若干回合" -ForegroundColor Green
for ($i = 0; $i -lt $DurationSec; $i++) {
    Start-Sleep -Seconds 1
    if ($proc.HasExited) {
        Write-Warning "Frida 进程提前退出 (exit=$($proc.ExitCode))"
        break
    }
    if (($i + 1) % 10 -eq 0) {
        Write-Host "  已录制 $($i + 1) 秒" -ForegroundColor DarkGray
    }
}

if (-not $proc.HasExited) {
    Write-Host "停止 Frida..." -ForegroundColor Green
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 500
}

$sz = (Get-Item $OutFile).Length
Write-Host ""
Write-Host "完成。" -ForegroundColor Green
Write-Host "  Log: $OutFile  ($sz bytes)"
if (Test-Path $ErrFile) {
    $esz = (Get-Item $ErrFile).Length
    if ($esz -gt 0) {
        Write-Host "  Err: $ErrFile  ($esz bytes, 有错误输出，请检查)" -ForegroundColor Yellow
    }
}
```

- [ ] **Step 2：干跑确认语法正确**

```powershell
powershell -NoProfile -Command "Get-Command .\Scripts\recon_hsbox.ps1 -ErrorAction Stop | Out-Null; Write-Host 'parse-ok'"
```

预期输出：`parse-ok`。

- [ ] **Step 3：提交**

```bash
git add Scripts/recon_hsbox.ps1
git commit -m "recon: 添加侦察录制编排脚本"
```

---

## Task 8：对照组录制（wild，狂野对局）

**Files:**
- Create: `docs/superpowers/recon/raw/<ts>_wild.log`（运行时生成）
- Create: `docs/superpowers/recon/raw/<ts>_wild.log.err`（运行时生成）

- [ ] **Step 1：人工准备**

1. 启动 Hearthstone，登录账号
2. 启动炉石盒子（HSAng.exe），确认加载天梯记牌器正常
3. 进入**狂野**匹配队列，匹配成功后到对局中
4. 打开【管理员】PowerShell，`cd` 到 Hearthbot 项目根目录

- [ ] **Step 2：运行录制**

```powershell
.\Scripts\recon_hsbox.ps1 -Mode wild -DurationSec 60
```

按脚本提示回车开始。**录制期间**在狂野对局里正常打 5-10 回合（打出多张手牌、攻击、使用英雄技能），触发盒子的推荐推送路径。

- [ ] **Step 3：验收 log 质量**

录制完成后检查：

```bash
ls -l docs/superpowers/recon/raw/
```

- log 文件应存在且 > 10KB（60 秒录制正常体量）
- 用编辑器打开 log，至少应看到：
  - 若干条 `"tag":"init-hook"` 行（hook 注册成功证据）
  - **多条** `"tag":"ExecuteJS"` 行，且 `payload.js` 里能见到 `window.onUpdateLadderActionRecommend(...)` 或 `window.fRecommend(...)` 字样（证明盒子在狂野下确实推了推荐）
  - 若干 `"tag":"CreateFileW"` 或 `"tag":"RPM"` 或 `"tag":"WinHttpConnect"` 命中（具体哪种取决于盒子实现）
- `.err` 文件应为空或仅含无害的 Frida 启动信息

**如果 ExecuteJS 没命中 onUpdate\*Recommend**：说明本场对局太短、盒子还没触发推荐，重录一次并延长 `DurationSec` 到 120。

- [ ] **Step 4：提交 raw log**

```bash
git add docs/superpowers/recon/raw/
git commit -m "recon: 对照组录制 - 狂野对局"
```

---

## Task 9：实验组录制（std-legend，标传传说受限）

**Files:**
- Create: `docs/superpowers/recon/raw/<ts>_std-legend.log`
- Create: `docs/superpowers/recon/raw/<ts>_std-legend.log.err`

- [ ] **Step 1：人工准备**

1. 账号必须处于**当前日期对应的受限档**：
   - 若日期 1-10 号：账号需已打到传说（不用具体名次）
   - 若日期 11-20 号：账号需在传说 5 万名以内
   - 若日期 21-月末：账号需在传说 3 万名以内
2. 进入**标准模式**匹配，到传说匹配池匹配成功
3. 进入对局，确认盒子界面显示"AI 推荐功能实行阶梯开放..."受限页

- [ ] **Step 2：运行录制**

```powershell
.\Scripts\recon_hsbox.ps1 -Mode std-legend -DurationSec 60
```

录制期间在对局里正常操作 5-10 回合，让盒子"想推推荐但被自己判定拦截"的代码路径尽可能走到。

- [ ] **Step 3：验收 log 质量**

```bash
ls -l docs/superpowers/recon/raw/
```

- log 文件应存在且 > 5KB
- 应看到：
  - `init-hook` 行（与 wild 场景相同）
  - 预期 `ExecuteJS` 中 **缺少** `onUpdateLadderActionRecommend` 相关调用（这是受限的直接表征）
  - 但应该有其他 `ExecuteJS` 调用（盒子仍在推更新 UI 状态的 JS，如 mode/段位显示）
  - 盒子在标传下读的文件/HTTP/内存调用与 wild 组可能有交集也可能不同——这是下一步 diff 的材料

- [ ] **Step 4：提交**

```bash
git add docs/superpowers/recon/raw/
git commit -m "recon: 实验组录制 - 标传传说受限档对局"
```

---

## Task 10：对比分析并写侦察报告

**Files:**
- Create: `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md`

- [ ] **Step 1：统计各 tag 在两组 log 里的出现次数**

```bash
# 替换 <ts> 为实际时间戳
WILD=$(ls -t docs/superpowers/recon/raw/*_wild.log | head -1)
STD=$(ls  -t docs/superpowers/recon/raw/*_std-legend.log | head -1)
echo "Wild:     $WILD"
echo "StdLegend: $STD"

for tag in init-hook ExecuteJS CreateFileW RPM WinHttpSendRequest WinHttpConnect; do
    wc=$(grep -c "\"tag\":\"$tag\"" "$WILD" 2>/dev/null)
    sc=$(grep -c "\"tag\":\"$tag\"" "$STD"  2>/dev/null)
    printf "  %-20s wild=%s std=%s\n" "$tag" "$wc" "$sc"
done
```

把输出记下来填入报告的"调用次数对比"表。

- [ ] **Step 2：提取两组里所有唯一的 ExecuteJS 调用入口**

```bash
# 提取每行 ExecuteJS 的 js 字段前 80 个字符，去重
extract() {
    grep '"tag":"ExecuteJS"' "$1" \
        | grep -oE '"js":"[^"]*"' \
        | sed 's/"js":"//; s/"$//' \
        | cut -c1-80 \
        | sort -u
}
echo "=== WILD 独有 ==="
comm -23 <(extract "$WILD") <(extract "$STD")
echo "=== STD 独有 ==="
comm -13 <(extract "$WILD") <(extract "$STD")
echo "=== 两组共有 ==="
comm -12 <(extract "$WILD") <(extract "$STD")
```

重点关注 "WILD 独有" —— **这些就是"狂野会推、标传不推"的 C++→JS 调用，最接近判定差异**。

- [ ] **Step 3：提取两组 HTTP 目标主机**

```bash
for f in "$WILD" "$STD"; do
    echo "=== $(basename $f) WinHttpConnect hosts ==="
    grep '"tag":"WinHttpConnect"' "$f" \
        | grep -oE '"host":"[^"]*"' \
        | sort -u
done
```

- [ ] **Step 4：提取两组文件访问（Hearthstone 相关）**

```bash
for f in "$WILD" "$STD"; do
    echo "=== $(basename $f) CreateFileW paths ==="
    grep '"tag":"CreateFileW"' "$f" \
        | grep -oE '"path":"[^"]*"' \
        | sort -u
done
```

- [ ] **Step 5：提取两组 RPM 目标地址**

```bash
for f in "$WILD" "$STD"; do
    echo "=== $(basename $f) RPM addrs ==="
    grep '"tag":"RPM"' "$f" \
        | grep -oE '"addr":"[^"]*"' \
        | sort -u \
        | head -20
done
```

- [ ] **Step 6：综合分析并写报告**

创建 `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md`，按下述骨架填写（所有 TBD 必须替换为真实观察）：

```markdown
# 盒子标传传说受限 — 侦察报告

**侦察日期：** 2026-04-17
**盒子版本：** （从 log 里某条 init 上方或启动日志提取，或 `file F:\炉石传说盒子\HSAng.exe` 版本号）
**HS 版本：** （启动器页显示的版本）
**侦察账号档位：** （例如"标传传说 2.3 万名"）

## 原始数据

- 对照组（wild）：`docs/superpowers/recon/raw/<ts>_wild.log`
- 实验组（std-legend）：`docs/superpowers/recon/raw/<ts>_std-legend.log`

## 调用次数对比

| tag | wild | std-legend | 备注 |
|-----|------|------------|------|
| init-hook | ... | ... | 注册的 hook 数一致即正常 |
| ExecuteJS | ... | ... | **关键差异点** |
| CreateFileW | ... | ... | |
| RPM | ... | ... | |
| WinHttpSendRequest | ... | ... | |
| WinHttpConnect | ... | ... | |

## ExecuteJS 差异

### 「狂野独有」调用入口（重点）

```
（粘 Step 2 输出的 WILD 独有部分）
```

### 「标传独有」调用入口

```
（粘 Step 2 输出的 STD 独有部分）
```

### 判断

- 是否看到 `onUpdateLadderActionRecommend` 仅在 wild 出现？ **（是/否）**
- 是否看到 `fSetMode` 在两组都出现？ **（是/否）**
- 其他显著差异：...

## HTTP 目标差异

### wild 访问主机

```
（粘输出）
```

### std-legend 访问主机

```
（粘输出）
```

**结论**：
- 盒子在 std-legend 下是否仍访问 `*.lushi.163.com`？ **（是/否）**
- 是否有段位/排行相关的 URL path（如 `*rank*`, `*ladder*`, `*legend*`）？ **（列出）**

## 文件访问差异

### wild / std-legend 都访问的 Hearthstone 文件

列出并标注。

### 仅 std-legend 访问的文件（若有）

...

## RPM 差异

- 是否读 Hearthstone.exe？
- 读的地址是否在两组间有交集？
- 一个典型的 RPM 命中的 backtrace（贴一条）：

```
（从 log 里选一条含 bt 字段的 RPM 行，贴 backtrace）
```

## 数据源定位结论

（**核心产出**，基于以上对比）

勾选最可能的 C++ 判定数据源：

- [ ] (a) HS 日志文件 — 证据：...
- [ ] (b) HS 进程内存 — 证据：...
- [ ] (c) 网易 API — 证据：...
- [ ] (d) C++ 内部固定逻辑 — 证据：...
- [ ] (x) 混合 / 多源 — 说明：...

## HTTP 响应签名检查（分支 c 必做）

- 若 WinHttpConnect 命中了段位/排行相关 API，复用 log 中的 URL 用浏览器或 curl 模拟请求，观察响应是否含：
  - HMAC / `sign` / `signature` / `x-sign` 头
  - Body 里的 `sign` / `_sign` 字段
  - （对接 Frida 再 hook WinHttpReadData 抓响应是更彻底方式，但本阶段暂缓）

结论：**（签名/无签名/无 HTTP 路径，c 分支不适用）**

## 阶段 1 补丁方向决策

根据以上，阶段 1 选：

- **首选分支**：[a/b/c/d]
- **理由**：...
- **放弃的分支**：[...]
- **放弃理由**：...

如果决策为**放弃本 spec**：
- 说明原因（例如 C++ 做了 VMP 保护看不到分支）
- 后续建议（例如转"Hearthbot 自推荐"另立项）
```

- [ ] **Step 7：检查报告是否完整**

打开 `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md` 通读一遍：
- 是否还有 `TBD` / `（粘...）` / `...` 等占位符？
- "数据源定位结论" 是否明确勾了一个选项？
- "阶段 1 补丁方向决策" 是否给出了具体 a/b/c/d 或明确"放弃"？

全部填好后进入下一步。

- [ ] **Step 8：提交报告**

```bash
git add docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md
git commit -m "recon: 盒子受限侦察报告（阶段1决策依据）"
```

---

## Task 11：回归清理 + 推送

- [ ] **Step 1：确认无悬挂修改**

```bash
git status
```

预期：working tree clean（除已存在的 spec 之外已 commit 项目未提交的旧 diff）。

- [ ] **Step 2：浏览本次所有 commit**

```bash
git log --oneline origin/main..HEAD
```

应看到一串 `recon:` 前缀的提交：骨架 → 每个 hook → PowerShell → wild → std → 报告。

- [ ] **Step 3：推送（如果需要）**

```bash
git push
```

（按项目 CLAUDE.md 规范，默认应推。）

---

## 完成条件

- [ ] `docs/superpowers/recon/2026-04-17-hsbox-limit-recon.md` 明确给出阶段 1 的 a/b/c/d 决策或"放弃"
- [ ] raw log 两份已归档
- [ ] 所有提交已 push 到 origin/main

## 对阶段 1 的交接

**阶段 1 另立 plan 前需要：**
- 本报告的"数据源定位结论"和"阶段 1 补丁方向决策"小节已填完
- 如果决策是 c（CDP 改响应）：报告里需附上段位 API 的 URL pattern、响应是否签名
- 如果决策是 a（日志 shim）：报告里需附上盒子读的具体日志文件路径与段位信息所在行模式
- 如果决策是 b（RPM hook）：报告里需附上一个命中段位字段的 RPM 地址与大小
- 如果决策是 d（判定 hook）：报告里需附上"wild 独有 ExecuteJS"的至少一条 backtrace，用于后续从该栈帧上溯找判定分支
